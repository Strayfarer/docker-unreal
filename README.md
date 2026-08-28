# Unreal Engine Docker Image

This repository builds one Windows Docker image that installs and compiles the requested Unreal Engine minor release when the `Build` launcher is first invoked. Source and Installed Builds live in named volumes and are reused by later containers.

## Image contents

The image provides:

- `Build.exe`, a shim for Unreal Engine's `Engine/Build/BatchFiles/Build.bat`.
- Git for Windows for authenticated Epic source checkout and update checks.
- Visual Studio 2019 and 2022 Build Tools, including the MSVC 14.29, 14.38, and 14.44 toolchains.
- Windows SDK 10.0.18362 and 10.0.22621, the .NET Framework SDK and legacy reference assemblies, and the June 2010 DirectX runtime files required by Unreal's Windows tools.

The image currently supports minor branch selectors from Unreal Engine 5.0 onward. UE4 is not supported because its older compiler, SDK, .NET, and BuildGraph matrix would materially expand the image and installer; 5.0 is the supported floor.

Only Win64 host Installed Builds are compiled. Client, server, derived-data-cache, full debug information, signing, and Datasmith build variants are disabled, and the game configuration is limited to Development.

## Runtime installation

The first `Build` invocation performs the following work:

1. Resolve the current commit of the `UNREAL_VERSION` branch at `UNREAL_SOURCE`.
2. Clone or update that commit in `C:/unreal/sources/EpicGames.UnrealEngine` using the supplied credentials.
3. Validate that `Engine/Build/Build.version` belongs to the requested minor release.
4. Download Epic's version-specific dependencies. UE 5.0 receives Epic's checksum-pinned repaired dependency manifest because the manifest committed on that branch uses a retired CDN namespace.
5. Compile a Win64 Installed Build with the matching MSVC and Windows SDK profile.
6. Atomically publish the completed engine under `C:/unreal/binaries/<minor>` and record its source URL, branch, patch version, and commit.
7. Run that installation's real `Engine/Build/BatchFiles/Build.bat` with the original arguments and return its exit code.

Every invocation resolves the branch again. If its commit has moved, the launcher builds the new patch while retaining the last complete installation, then replaces that installation atomically. A failed update therefore does not destroy the previous engine. Builds sharing the volumes are serialized with a cross-container file lock.

## Configuration

- `UNREAL_VERSION` is required and selects a numeric minor branch such as `5.0` or `5.7`.
- `UNREAL_SOURCE` defaults to `https://github.com/EpicGames/UnrealEngine`.
- `UNREAL_CREDENTIALS_USR` and `UNREAL_CREDENTIALS_PSW` provide the Git username and personal access token. The account must be linked to an Epic Games account and able to read `EpicGames/UnrealEngine`.

Credentials are supplied to Git through `GIT_ASKPASS`; they are never placed in a command-line URL, source marker, image layer, or forwarded to the final Unreal `Build.bat` process.

## Volumes

Always mount both advertised locations. Unreal source, downloaded dependencies, intermediates, and Installed Builds are very large.

```yaml
services:
  unreal:
    image: faulo/unreal:latest
    environment:
      UNREAL_VERSION: "5.7"
      UNREAL_SOURCE: https://github.com/EpicGames/UnrealEngine
      UNREAL_CREDENTIALS_USR: ${UNREAL_CREDENTIALS_USR}
      UNREAL_CREDENTIALS_PSW: ${UNREAL_CREDENTIALS_PSW}
    volumes:
      - unreal-binaries:C:/unreal/binaries
      - unreal-sources:C:/unreal/sources

volumes:
  unreal-binaries:
  unreal-sources:
```

Cleanup of obsolete source intermediates or no-longer-used minor installations is intentionally outside the launcher's scope.

## Build and test

Launcher unit tests do not require Docker:

```powershell
dotnet test docker-unreal.sln --configuration Release
```

Build the disposable candidate image on Dende:

```powershell
./windows/build-images.ps1 -DockerContext dende
```

The script always passes the Docker context explicitly, uses the repository root as build context, selects the legacy Windows builder required by Dende, and refuses non-`tmp` namespaces.

To run the same two selectors as the integration contract, export credentials and run:

```powershell
$env:UNREAL_CREDENTIALS_USR = '<github-user>'
$env:UNREAL_CREDENTIALS_PSW = '<github-token>'
./windows/test-images.ps1 -DockerContext dende
```

The test uses the persistent `unreal-binaries` and `unreal-sources` volumes and invokes `Build -Help` for each version in `.env`. The Explorer entry points are `docker-build-windows.bat` and `docker-test-windows.bat`; they are interactive and pause before closing.

Only images under the disposable `tmp/unreal` namespace may be built locally. Unreal Engine development images are governed by the Unreal Engine EULA and must not be distributed to users who are not permitted to access their contents.
