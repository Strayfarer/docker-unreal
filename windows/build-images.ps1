[CmdletBinding()]
param(
    [string] $DockerContext = 'windows',

    [ValidateSet('tmp')]
    [string] $DockerNamespace = 'tmp'
)

$ErrorActionPreference = 'Stop'

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($ArgumentList -join ' ')"
    }
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$environmentLine = Get-Content -LiteralPath (Join-Path $repositoryRoot '.env') | Where-Object { $_ -match '^DOCKER_IMAGE=' } | Select-Object -First 1
if (-not $environmentLine) {
    throw 'DOCKER_IMAGE is missing from .env'
}
$imageName = $environmentLine.Substring('DOCKER_IMAGE='.Length).Trim('"')
if (-not $imageName) {
    throw 'DOCKER_IMAGE is empty in .env'
}

$daemonOs = (& docker --context $DockerContext info --format '{{.OSType}}').Trim()
if ($LASTEXITCODE -ne 0 -or $daemonOs -ne 'windows') {
    throw "Docker context '$DockerContext' is not connected to a Windows daemon: $daemonOs"
}

$tag = "$DockerNamespace/${imageName}:latest"
Write-Host "Building $tag on Docker context $DockerContext"
$previousBuildKit = $env:DOCKER_BUILDKIT
try {
    # The LTSC 2019 daemon cannot execute Windows Dockerfiles with BuildKit.
    $env:DOCKER_BUILDKIT = '0'
    Invoke-NativeCommand -FilePath 'docker' -ArgumentList @(
        '--context'
        $DockerContext
        'build'
        '--pull'
        '--file'
        (Join-Path $repositoryRoot 'windows\Dockerfile')
        '--tag'
        $tag
        $repositoryRoot
    )
} finally {
    $env:DOCKER_BUILDKIT = $previousBuildKit
}
