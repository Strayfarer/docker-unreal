# escape=`

ARG OS_BASE=ltsc2019

# Keep the launcher build small enough for Hyper-V-isolated CI runners.
FROM mcr.microsoft.com/dotnet/sdk:9.0-nanoserver-1809 AS launcher

WORKDIR C:/source
COPY common/Build/ .
RUN dotnet publish Build.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --property:PublishSingleFile=true `
        --output C:/out

FROM mcr.microsoft.com/windows:${OS_BASE}

SHELL ["C:/Windows/System32/WindowsPowerShell/v1.0/powershell", "-ExecutionPolicy", "Bypass", "-NonInteractive", "-NoProfile", "-Command", "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue';"]

USER ContainerAdministrator
WORKDIR C:/docker-unreal

# Compiler, SDK, Git, .NET Framework, and DirectX dependencies are shared by
# source setup, engine compilation, and later project builds.
COPY common/prerequisites.psd1 C:/docker-unreal/common/prerequisites.psd1
COPY windows/install-prerequisites.ps1 C:/docker-unreal/windows/install-prerequisites.ps1
COPY windows/install-netfx-sdk.ps1 C:/docker-unreal/windows/install-netfx-sdk.ps1
RUN & C:/docker-unreal/windows/install-prerequisites.ps1 -Phase VisualStudio2019
RUN & C:/docker-unreal/windows/install-prerequisites.ps1 -Phase VisualStudio2022
RUN & C:/docker-unreal/windows/install-prerequisites.ps1 -Phase WindowsSdks
RUN & C:/docker-unreal/windows/install-prerequisites.ps1 -Phase Common; `
    & C:/docker-unreal/windows/install-netfx-sdk.ps1

COPY --from=launcher C:/out/Unreal.exe C:/unreal/Unreal.exe

ENV UNREAL_SOURCE="https://github.com/EpicGames/UnrealEngine"

RUN New-Item -ItemType Directory -Path C:/unreal/binaries, C:/unreal/cache, C:/unreal/sources, C:/workspace -Force | Out-Null; `
    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine'); `
    [Environment]::SetEnvironmentVariable('Path', ('C:\unreal;' + $machinePath), 'Machine'); `
    & C:/unreal/Unreal.exe --shim-version; `
    if ($LASTEXITCODE -ne 0) { throw 'Unreal.exe smoke check failed' }; `
    & C:/Git/cmd/git.exe --version; `
    if ($LASTEXITCODE -ne 0) { throw 'Git smoke check failed' }

VOLUME ["C:/unreal/binaries", "C:/unreal/cache", "C:/unreal/sources"]

WORKDIR C:/workspace

CMD ["Unreal.exe", "--help"]
