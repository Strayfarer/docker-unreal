[CmdletBinding()]
param(
    [string] $DockerContext = 'windows',

    [ValidateSet('tmp')]
    [string] $DockerNamespace = 'tmp',

    [string[]] $UnrealVersion
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$versions = Import-PowerShellDataFile -LiteralPath (Join-Path $repositoryRoot 'common\versions.psd1')
if (-not $UnrealVersion) {
    $UnrealVersion = @($versions.Keys | Sort-Object { [version] $_ })
}

$environment = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $repositoryRoot '.env')) {
    if ($line -match '^(?<name>[^#=]+)=(?<value>.*)$') {
        $environment[$Matches.name] = $Matches.value.Trim('"')
    }
}
foreach ($requiredName in @('DOCKER_IMAGE', 'DOCKER_TEST_CMD')) {
    if (-not $environment[$requiredName]) {
        throw "$requiredName is missing from .env"
    }
}
$testCommand = @($environment.DOCKER_TEST_CMD.Split(' ', [StringSplitOptions]::RemoveEmptyEntries))

$daemonOs = (& docker --context $DockerContext info --format '{{.OSType}}').Trim()
if ($LASTEXITCODE -ne 0 -or $daemonOs -ne 'windows') {
    throw "Docker context '$DockerContext' is not connected to a Windows daemon: $daemonOs"
}

foreach ($minorVersion in $UnrealVersion) {
    if (-not $versions.ContainsKey($minorVersion)) {
        throw "Unsupported Unreal Engine version: $minorVersion"
    }
    $image = "$DockerNamespace/$($environment.DOCKER_IMAGE):$minorVersion"
    Write-Host "Testing $image"
    & docker --context $DockerContext run --rm $image @testCommand
    if ($LASTEXITCODE -ne 0) {
        throw "Image test failed with exit code ${LASTEXITCODE}: $image"
    }
}
