param(
    [Parameter(Mandatory = $true)]
    [string] $UnrealVersion,

    [Parameter(Mandatory = $true)]
    [string] $UnrealPatchVersion,

    [Parameter(Mandatory = $true)]
    [string] $UnrealSourceCommit
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$sourceRoot = 'C:\UnrealEngine'
$buildVersionPath = Join-Path $sourceRoot 'Engine\Build\Build.version'
$buildVersion = Get-Content -LiteralPath $buildVersionPath -Raw | ConvertFrom-Json
$actualPatchVersion = '{0}.{1}.{2}' -f $buildVersion.MajorVersion, $buildVersion.MinorVersion, $buildVersion.PatchVersion
if ($actualPatchVersion -ne $UnrealPatchVersion) {
    throw "Source version mismatch: expected $UnrealPatchVersion, found $actualPatchVersion"
}
if ("$($buildVersion.MajorVersion).$($buildVersion.MinorVersion)" -ne $UnrealVersion) {
    throw "Source minor version does not match image tag: $UnrealVersion"
}
if ($UnrealSourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw "Invalid pinned Unreal source commit: $UnrealSourceCommit"
}

$gitDependenciesCandidates = @(
    (Join-Path $sourceRoot 'Engine\Binaries\DotNET\GitDependencies\win-x64\GitDependencies.exe'),
    (Join-Path $sourceRoot 'Engine\Binaries\DotNET\GitDependencies.exe')
)
$gitDependencies = $gitDependenciesCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $gitDependencies) {
    throw 'GitDependencies executable was not found in the Unreal source checkout'
}

Push-Location $sourceRoot
try {
    & $gitDependencies `
        '--force' `
        '--no-cache' `
        '--exclude=Android' `
        '--exclude=Linux' `
        '--exclude=Mac'
    if ($LASTEXITCODE -ne 0) {
        throw "GitDependencies failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

$gitDirectory = Join-Path $sourceRoot '.git'
if (Test-Path -LiteralPath $gitDirectory) {
    Remove-Item -LiteralPath $gitDirectory -Recurse -Force
}
