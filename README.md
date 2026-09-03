# Unreal Engine Docker Image

This repository builds one Windows Docker image that resolves a requested Unreal Engine minor release and compiles a Win64 Installed Build on demand. Resolution can follow either the current head of the minor branch or the highest eligible semantic-version tag for that minor. Source, caches, and Installed Builds live in named volumes and are reused by later containers.

## Image contents

The image provides:

- `Unreal.exe`, the entry point for Unreal Engine build, automation, and commandlet tools.
- Git for Windows for authenticated Epic source checkout and update checks.
- Visual Studio 2019 and 2022 Build Tools, including the MSVC 14.29, 14.38, and 14.44 toolchains.
- Windows SDK 10.0.18362 and 10.0.22621, the .NET Framework SDK and legacy reference assemblies, and the June 2010 DirectX runtime files required by Unreal's Windows tools.

The image supports minor selectors from Unreal Engine 5.0 onward. UE4 is not supported because its older compiler, SDK, .NET, and BuildGraph matrix would materially expand the image and installer; 5.0 is the supported floor.

Only Win64 host Installed Builds are compiled. Client, server, packaged derived-data-cache, full debug information, signing, and Datasmith build variants are disabled. The supported game configurations are Development and Shipping.

## Commands

The image exposes six public command forms:

```text
Unreal Build <Build.bat arguments>
Unreal RunUAT <RunUAT.bat arguments>
Unreal Cmd <UnrealEditor-Cmd.exe arguments>
Unreal --version
Unreal --compile
Unreal --help
```

`Build`, `RunUAT`, and `Cmd` ensure that the selected engine is compiled, pass all following arguments to its corresponding tool, and return the tool's native exit code. `RunUAT` initializes the engine's bundled .NET environment before invoking `Engine/Build/BatchFiles/RunUAT.bat`.

`--version` resolves and prints the identifier that would be compiled, then exits without preparing source or compiling the engine. `--compile` resolves the same identifier and ensures its Installed Build is present without invoking an engine tool. It reuses an exact published build when one already exists; it does not force an otherwise unnecessary rebuild. `--help` lists every command and its usage without requiring runtime configuration.

## Version resolution

`UNREAL_VERSION` declares a numeric minor release such as `5.7` or `5.8`. `UNREAL_VERSION_MODE` controls how that minor is resolved at `UNREAL_SOURCE`:

- `tag` is the default. It considers tags whose complete names are valid [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html) versions with major and minor components equal to `UNREAL_VERSION`. A tag with prerelease identifiers is excluded unless its prerelease consists of the single identifier `release`. The remaining tags are ordered by SemVer precedence and the greatest one is selected. For example, `UNREAL_VERSION=5.8` selects `5.8.2-release` over both `5.8.1-release` and `5.8.0`, while tags such as `5.8.3-preview-1` are ignored. The reported identifier is the complete tag name.
- `branch` enables the rolling behavior. It resolves the exact `refs/heads/<minor>` branch, such as `refs/heads/5.8`, to its current commit. The reported identifier is the full commit hash.

Tag selection follows semantic-version precedence and does not use tag creation dates or remote enumeration order. Tags that are not valid semantic versions, target another major or minor, or have a disallowed prerelease are ignored. Build metadata does not affect precedence; resolution fails rather than choose arbitrarily if distinct greatest candidates have equal precedence. An annotated tag is peeled to its commit. Resolution also fails if the selected branch does not exist, no eligible tag exists, a tag does not resolve to a commit, or the mode is unknown; it never silently falls back to the other mode.

The resolved commit hash is always the engine version's immutable ID and installation cache key, including in tag mode. A tag is only the reported identifier and a means of selecting that commit. Branch and tag resolution of the same source commit therefore identify the same engine version: switching `UNREAL_VERSION_MODE` must reuse an otherwise compatible published build and must not recompile it.

`--version`, `--compile`, `Build`, `RunUAT`, and `Cmd` resolve the remote on every invocation, so branch mode observes a moved branch and tag mode observes a newly published patch tag. `Unreal --version` performs only that authenticated remote lookup. It does not create or update a clone, source worktree, cache, Installed Build, or toolchain configuration, and it does not acquire the installation lock. On success it writes only the resolved identifier followed by a newline to standard output, making it safe to use from scripts. Diagnostics and errors go to standard error.

## Runtime installation

`Unreal --compile`, `Unreal Build`, `Unreal RunUAT`, and `Unreal Cmd` require an Installed Build. On a cache miss they perform the following work:

1. Resolve the configured branch or highest eligible semantic-version tag at `UNREAL_SOURCE` and retain both its reported identifier and immutable commit ID.
2. Clone the shared repository in `C:/unreal/sources/EpicGames.UnrealEngine` and create or update a persistent worktree for the requested minor release using the supplied credentials. The original checkout is retained as one minor's worktree; additional worktrees live under `C:/unreal/sources/worktrees/<minor>`.
3. Validate that `Engine/Build/Build.version` belongs to the requested minor release.
4. Download Epic's version-specific dependencies. UE 5.0 receives Epic's checksum-pinned repaired dependency manifest because the manifest committed on that branch uses a retired CDN namespace.
5. Compile a Win64 Installed Build with the matching MSVC and Windows SDK profile, using the requested minor release's persistent local Derived Data Cache.
6. Atomically publish the completed engine under `C:/unreal/binaries/<minor>` and record its source URL, requested minor, commit ID, patch version, and Installed Build profile.
7. Exit successfully for `--compile`, or dispatch to that installation's `Build.bat`, `RunUAT.bat`, or `UnrealEditor-Cmd.exe` with the original arguments and return its exit code.

If the exact source commit and Installed Build profile are already published for the requested minor and source, the launcher does not touch a source worktree or invoke the compiler, regardless of the mode or reported identifier that selected that commit. If a branch moves or a newer matching tag appears, it cleans and rebuilds only that minor's worktree while retaining other minor worktrees and the last complete installation, then replaces that installation atomically. A failed update therefore does not destroy the previous engine.

All source preparation and engine compilation is serialized by the exclusive file lock `C:/unreal/binaries/.docker-unreal.lock`. The lock is held through publication, uses only shared-volume filesystem semantics, and is released automatically if a process or container exits. Keeping the established lock path also coordinates rolling upgrades from older image revisions. Any number of containers may share the three named volumes: exact published-commit hits bypass the queue, while cache misses queue behind the one container performing source/build work and re-check the remote commit and installation after acquiring the lock.

## Configuration

- `UNREAL_VERSION` is required and declares a numeric minor release such as `5.0` or `5.8`.
- `UNREAL_VERSION_MODE` selects `tag` or `branch` resolution and defaults to `tag`.
- `UNREAL_SOURCE` defaults to `https://github.com/EpicGames/UnrealEngine`.
- `UNREAL_DDC` optionally specifies a shared Zen server hostname or URL, such as `http://ddc.example.com:8558`.
- `UNREAL_CREDENTIALS_USR` and `UNREAL_CREDENTIALS_PSW` provide the Git username and personal access token. The account must be linked to an Epic Games account and able to read `EpicGames/UnrealEngine`.

Credentials are supplied to Git through `GIT_ASKPASS`; they are never placed in a command-line URL, source marker, image layer, or forwarded to the selected Unreal Engine tool.

## Volumes

Always mount all three advertised locations. Unreal source, downloaded dependencies, compiler caches, intermediates, and Installed Builds are very large. GitDependencies packs are shared in `C:/unreal/cache/gitdeps`, NuGet and .NET caches in `C:/unreal/cache/nuget` and `C:/unreal/cache/dotnet`, and Unreal Build Accelerator storage is isolated per minor under `C:/unreal/cache/uba/<minor>`.

The launcher sets Unreal's supported `UE-LocalDataCachePath` override to the unversioned `C:/unreal/cache/ddc` directory for Installed Build compilation and every forwarded engine tool. DDC keys carry the compatibility information needed to share this content-addressed cache across `UNREAL_VERSION` values, so partitioning it by engine minor would only reduce reuse. Unreal 5.4 and newer use a local Zen store and place its data in a `Zen` subdirectory of that override; older versions use the same override as their filesystem DDC root.

When `UNREAL_DDC` is set, the launcher also supplies its value through Epic's `UE-ZenSharedDataCacheHost` override. Unreal keeps the local cache in its DDC hierarchy, automatically deactivates an unavailable or excessively slow shared Zen layer, and falls back to `C:/unreal/cache/ddc`. The stock Zen shared layer is available from Unreal 5.4 onward; older supported engines ignore the host override and use the persistent local cache. Packaged Installed Build DDC generation remains disabled because the writable caches are populated on demand. See Epic's [Derived Data Cache documentation](https://dev.epicgames.com/documentation/unreal-engine/using-derived-data-cache-in-unreal-engine) and [shared Zen setup guide](https://dev.epicgames.com/documentation/unreal-engine/set-up-zen-storage-server-as-shared-ddc-for-unreal-engine).

NuGet vulnerability auditing is disabled only for Unreal's historical, commit-pinned build projects. Otherwise newly published advisories can become warnings-as-errors and make an unchanged engine commit stop compiling over time. Package restore integrity checks and normal compiler warnings and errors remain enabled.

```yaml
services:
  unreal:
    image: faulo/unreal:latest
    environment:
      UNREAL_VERSION: "5.8"
      UNREAL_VERSION_MODE: tag
      UNREAL_SOURCE: https://github.com/EpicGames/UnrealEngine
      UNREAL_DDC: http://ddc.example.com:8558
      UNREAL_CREDENTIALS_USR: ${UNREAL_CREDENTIALS_USR}
      UNREAL_CREDENTIALS_PSW: ${UNREAL_CREDENTIALS_PSW}
    volumes:
      - unreal-binaries:C:/unreal/binaries
      - unreal-cache:C:/unreal/cache
      - unreal-sources:C:/unreal/sources

volumes:
  unreal-binaries:
  unreal-cache:
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

To run the same three selectors as the integration contract, export credentials and run:

```powershell
$env:UNREAL_CREDENTIALS_USR = '<github-user>'
$env:UNREAL_CREDENTIALS_PSW = '<github-token>'
./windows/test-images.ps1 -DockerContext dende
```

The integration contract uses the persistent `unreal-binaries`, `unreal-cache`, and `unreal-sources` volumes and exercises all six public command forms. It verifies branch and tag resolution, verifies that `--version` does not materialize an engine, verifies cross-mode reuse when both modes resolve the same commit, exercises `--compile` against an exact precompiled installation, and verifies the unversioned local and optional shared DDC environment. Each real-engine stage then lets `Unreal Build` either reuse the persistent precompiled engine or compile it on a cache miss. It builds the editor target for `test-files/EmptyGame`, compiles the fixture's Blueprint through `Unreal Cmd`, and uses `Unreal RunUAT BuildCookRun` to archive a Shipping executable and pak file. The Explorer entry points are `docker-build-windows.bat` and `docker-test-windows.bat`; they are interactive and pause before closing.

Only images under the disposable `tmp/unreal` namespace may be built locally. Unreal Engine development images are governed by the Unreal Engine EULA and must not be distributed to users who are not permitted to access their contents.
