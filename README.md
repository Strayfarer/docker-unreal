# Unreal Engine Docker Image

This repository builds Windows Docker images containing source-built Unreal Engine Installed Builds. Linux is not supported yet; shared inputs live in `common/` and Windows-specific inputs live in `windows/` so another platform can be added without reorganizing the project.

## Image contents

Each minor image tag tracks the newest released patch in that series:

| Image | Unreal Engine source tag | Compiler | Windows SDK |
| --- | --- | --- | --- |
| `tmp/unreal:5.0` | `5.0.3-release` | Visual Studio 2019 / MSVC 14.29 | 10.0.18362 |
| `tmp/unreal:5.7` | `5.7.4-release` | Visual Studio 2022 / MSVC 14.44 | 10.0.22621 |

Both images use Microsoft's full `mcr.microsoft.com/windows:ltsc2019` base. The full Windows API surface is retained for Unreal tools, and the matching Visual Studio Build Tools remain in the final image for C++ project builds. The source-builder stage additionally installs a .NET Framework SDK through Microsoft's checksum-pinned Windows SDK 18362 bootstrapper, satisfying Unreal's 4.6-or-newer requirement while compiling SwarmInterface.

The Installed Build is placed at `C:/Program Files/Epic Games/UE_<minor>`. Its `Engine/Build/BatchFiles` directory is prepended to the machine `PATH`, so tools such as `Build`, `GenerateProjectFiles`, and `RunUAT` can be called directly.

To keep build time and image size bounded, the image includes only the Win64 host platform and the Development game configuration. The derived data cache, templates, samples, and debug-symbol files are omitted. These are optional Installed Build components and can be added when an integration contract needs them.

## Prerequisites

Building requires:

- A Windows container daemon compatible with LTSC 2019. The project convention is to register it as the `windows` Docker context; Dende can be selected explicitly with `-DockerContext dende`.
- A GitHub account linked to an Epic Games account, authenticated in the GitHub CLI with permission to read `EpicGames/UnrealEngine`.
- Substantial free disk space and build time. Unreal source, dependencies, compiler output, BuildKit cache, and the final image coexist during a build.

Only disposable `tmp/unreal` images are built. The images are not published by this repository. Unreal Engine development images are governed by the Unreal Engine EULA and must not be distributed to users who are not permitted to access their contents.

## Build

The build script downloads each pinned Epic source commit through the authenticated GitHub API into a cache outside this repository. It packs the selected source into a temporary, ignored `unreal.tar` at the repository root so the legacy Windows builder receives one context file instead of more than 100,000 individual files. The selected dependency manifest is staged separately as `unreal-dependencies.xml`; UE 5.0 uses Epic's checksum-pinned replacement release asset because the manifest committed in the 5.0.3 source tag points at a retired CDN namespace. Both temporary inputs are removed even if the build fails. The commit is resolved before download and recorded in a cache provenance marker. Credentials and the cache marker are used only by the host and are never copied into the Docker build context or an image layer.

Build both integration images on Dende:

```powershell
./windows/build-images.ps1 -DockerContext dende
```

Build one image:

```powershell
./windows/build-images.ps1 -DockerContext dende -UnrealVersion 5.0
```

By default, source checkouts are cached under the current user's local application-data directory. Override that location with `-SourceCache` when another disk has more room.

Both Docker builds use the repository root as their context. The script always names the Docker context explicitly, selects Docker's legacy builder because Windows BuildKit execution is unavailable on Dende, and refuses to build outside the `tmp` namespace.

The Explorer entry point is `docker-build-windows.bat`. It is interactive and pauses after the build; use the PowerShell script for automation.

## Test

Run the same command exercised by the current integration tests against both images:

```powershell
./windows/test-images.ps1 -DockerContext dende
```

Or test an image directly:

```powershell
docker --context dende run --rm tmp/unreal:5.0 Build -Help
docker --context dende run --rm tmp/unreal:5.7 Build -Help
```

The Explorer entry point is `docker-test-windows.bat`.

## Version updates

`common/versions.psd1` is authoritative for the minor-to-patch mapping, source ref and commit, and required Visual Studio components. `common/dependency-manifests.psd1` pins any official replacement dependency manifests needed by older releases. When updating a series, verify the newest stable Epic release, update all pinned metadata together, and build and test the corresponding `tmp` image before changing any integration expectation.
