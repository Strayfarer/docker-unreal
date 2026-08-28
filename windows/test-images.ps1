[CmdletBinding()]
param(
    [string] $DockerContext = 'windows',

    [ValidateSet('tmp')]
    [string] $DockerNamespace = 'tmp'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$environment = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $repositoryRoot '.env')) {
    if ($line -match '^(?<name>[^#=]+)=(?<value>.*)$') {
        $environment[$Matches.name] = $Matches.value.Trim('"')
    }
}
foreach ($requiredName in @('DOCKER_IMAGE', 'DOCKER_TEST_VERSIONS', 'DOCKER_TEST_CMD')) {
    if (-not $environment[$requiredName]) {
        throw "$requiredName is missing from .env"
    }
}
foreach ($credentialName in @('UNREAL_CREDENTIALS_USR', 'UNREAL_CREDENTIALS_PSW')) {
    if (-not [Environment]::GetEnvironmentVariable($credentialName)) {
        throw "$credentialName must be set for the private Epic Games repository"
    }
}

$daemonOs = (& docker --context $DockerContext info --format '{{.OSType}}').Trim()
if ($LASTEXITCODE -ne 0 -or $daemonOs -ne 'windows') {
    throw "Docker context '$DockerContext' is not connected to a Windows daemon: $daemonOs"
}

$image = "$DockerNamespace/$($environment.DOCKER_IMAGE):latest"
$versions = @($environment.DOCKER_TEST_VERSIONS.Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
$testCommand = @($environment.DOCKER_TEST_CMD.Split(' ', [StringSplitOptions]::RemoveEmptyEntries))
$unrealSource = [Environment]::GetEnvironmentVariable('UNREAL_SOURCE')
if (-not $unrealSource) {
    $unrealSource = 'https://github.com/EpicGames/UnrealEngine'
}
foreach ($version in $versions) {
    Write-Host "Testing $image with Unreal Engine $version"
    & docker --context $DockerContext run --rm `
        --env "UNREAL_VERSION=$version" `
        --env "UNREAL_SOURCE=$unrealSource" `
        --env UNREAL_CREDENTIALS_USR `
        --env UNREAL_CREDENTIALS_PSW `
        --volume 'unreal-binaries:C:/unreal/binaries' `
        --volume 'unreal-sources:C:/unreal/sources' `
        $image `
        @testCommand
    if ($LASTEXITCODE -ne 0) {
        throw "Image test failed with exit code ${LASTEXITCODE}: $image with Unreal Engine $version"
    }
}
