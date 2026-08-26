# escape=`

ARG OS_BASE=ltsc2019
FROM mcr.microsoft.com/windows:${OS_BASE} AS prerequisites

ARG UNREAL_VERSION

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
WORKDIR C:/docker-unreal

COPY common/versions.psd1 C:/docker-unreal/common/versions.psd1
COPY windows/install-prerequisites.ps1 C:/docker-unreal/windows/install-prerequisites.ps1
WORKDIR C:/
RUN & C:/docker-unreal/windows/install-prerequisites.ps1 -UnrealVersion $env:UNREAL_VERSION; `
    Remove-Item -LiteralPath C:/docker-unreal -Recurse -Force

FROM prerequisites AS builder

ARG UNREAL_VERSION
ARG UNREAL_PATCH_VERSION
ARG UNREAL_SOURCE_COMMIT

WORKDIR C:/UnrealEngine
COPY unreal.tar C:/unreal.tar
RUN tar.exe -xf C:/unreal.tar -C C:/UnrealEngine; `
    if ($LASTEXITCODE -ne 0) { throw "Failed to extract the Unreal Engine source archive: $LASTEXITCODE" }; `
    Remove-Item -LiteralPath C:/unreal.tar -Force
COPY windows/install-netfx-sdk.ps1 C:/install-netfx-sdk.ps1
RUN & C:/install-netfx-sdk.ps1; `
    Remove-Item -LiteralPath C:/install-netfx-sdk.ps1 -Force
COPY unreal-dependencies.xml C:/UnrealEngine/Engine/Build/Commit.gitdeps.xml
COPY windows/install-unreal-dependencies.ps1 C:/install-unreal-dependencies.ps1
RUN & C:/install-unreal-dependencies.ps1 `
        -UnrealVersion $env:UNREAL_VERSION `
        -UnrealPatchVersion $env:UNREAL_PATCH_VERSION `
        -UnrealSourceCommit $env:UNREAL_SOURCE_COMMIT; `
    Remove-Item -LiteralPath C:/install-unreal-dependencies.ps1 -Force
COPY windows/build-installed-engine.ps1 C:/build-installed-engine.ps1
RUN & C:/build-installed-engine.ps1 `
        -UnrealVersion $env:UNREAL_VERSION `
        -UnrealPatchVersion $env:UNREAL_PATCH_VERSION `
        -UnrealSourceCommit $env:UNREAL_SOURCE_COMMIT; `
    Remove-Item -LiteralPath C:/build-installed-engine.ps1 -Force

FROM prerequisites

ARG UNREAL_VERSION
ARG UNREAL_PATCH_VERSION
ARG UNREAL_SOURCE_REF
ARG UNREAL_SOURCE_COMMIT

ENV UNREAL_HOME="C:\Program Files\Epic Games\UE_${UNREAL_VERSION}"

LABEL org.opencontainers.image.title="Unreal Engine" `
      org.opencontainers.image.version="${UNREAL_PATCH_VERSION}" `
      net.slothsoft.unreal.source-ref="${UNREAL_SOURCE_REF}" `
      net.slothsoft.unreal.source-commit="${UNREAL_SOURCE_COMMIT}"

COPY --from=builder ["C:/UnrealEngine/LocalBuilds/Engine/Windows/", "C:/Program Files/Epic Games/UE_${UNREAL_VERSION}/"]
COPY --from=builder ["C:/UnrealEngineComponents/Binaries/", "C:/Program Files/Epic Games/UE_${UNREAL_VERSION}/"]
COPY --from=builder ["C:/UnrealEngineComponents/Content/", "C:/Program Files/Epic Games/UE_${UNREAL_VERSION}/"]
COPY --from=builder ["C:/UnrealEngineComponents/Extras/", "C:/Program Files/Epic Games/UE_${UNREAL_VERSION}/"]
COPY --from=builder ["C:/UnrealEngineComponents/Intermediate/", "C:/Program Files/Epic Games/UE_${UNREAL_VERSION}/"]
COPY --from=builder ["C:/UnrealEngineComponents/Plugins/", "C:/Program Files/Epic Games/UE_${UNREAL_VERSION}/"]
COPY --from=builder ["C:/UnrealEngineComponents/Source/", "C:/Program Files/Epic Games/UE_${UNREAL_VERSION}/"]

RUN $batchFiles = Join-Path $env:UNREAL_HOME 'Engine\Build\BatchFiles'; `
    if (-not (Test-Path -LiteralPath (Join-Path $batchFiles 'Build.bat') -PathType Leaf)) { throw "Build.bat not found: $batchFiles" }; `
    $buildVersion = Get-Content -LiteralPath (Join-Path $env:UNREAL_HOME 'Engine\Build\Build.version') -Raw | ConvertFrom-Json; `
    $actualVersion = '{0}.{1}.{2}' -f $buildVersion.MajorVersion, $buildVersion.MinorVersion, $buildVersion.PatchVersion; `
    if ($actualVersion -ne $env:UNREAL_PATCH_VERSION) { throw "Installed version mismatch: $actualVersion" }; `
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); `
    [Environment]::SetEnvironmentVariable('Path', ($batchFiles + ';' + $machinePath), 'Machine')

WORKDIR C:/workspace

RUN $command = Get-Command Build -ErrorAction Stop; `
    $expectedDirectory = Join-Path $env:UNREAL_HOME 'Engine\Build\BatchFiles'; `
    if ((Split-Path $command.Source -Parent) -ne $expectedDirectory) { throw "Build resolved to unexpected path: $($command.Source)" }; `
    Build -Help; `
    if ($LASTEXITCODE -ne 0) { throw "Build -Help failed with exit code $LASTEXITCODE" }

CMD ["Build.bat", "-Help"]
