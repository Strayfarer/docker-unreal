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

Push-Location $sourceRoot
try {
    $runUat = Join-Path $sourceRoot 'Engine\Build\BatchFiles\RunUAT.bat'
    $arguments = @(
        'BuildGraph'
        '-target=Make Installed Build Win64'
        '-script=Engine/Build/InstalledEngineBuild.xml'
        '-set:HostPlatformOnly=true'
        '-set:WithWin64=true'
        '-set:WithClient=false'
        '-set:WithServer=false'
        '-set:WithDDC=false'
        '-set:WithFullDebugInfo=false'
        '-set:GameConfigurations=Development'
        '-set:CompileDatasmithPlugins=false'
        '-set:SignExecutables=false'
        '-set:EmbedSrcSrvInfo=false'
        '-nosign'
    )
    $installedBuildScript = Get-Content -LiteralPath (Join-Path $sourceRoot 'Engine\Build\InstalledEngineBuild.xml') -Raw
    if ($installedBuildScript -match 'Name="BuildIdOverride"') {
        $arguments += "-set:BuildIdOverride=UE_$UnrealVersion"
    }
    & $runUat @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Installed Build failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

$installedRoot = Join-Path $sourceRoot 'LocalBuilds\Engine\Windows'
$requiredFiles = @(
    'Engine\Build\InstalledBuild.txt'
    'Engine\Build\BatchFiles\Build.bat'
    'Engine\Binaries\Win64\UnrealEditor.exe'
)
foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $installedRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Installed Build output is missing: $path"
    }
}

Get-ChildItem -LiteralPath $installedRoot -Filter '*.pdb' -File -Recurse | Remove-Item -Force
foreach ($optionalDirectory in @('FeaturePacks', 'Samples', 'Templates')) {
    $path = Join-Path $installedRoot $optionalDirectory
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

$componentsRoot = 'C:\UnrealEngineComponents'
New-Item -ItemType Directory -Path $componentsRoot | Out-Null
foreach ($component in @('Binaries', 'Content', 'Extras', 'Intermediate', 'Plugins', 'Source')) {
    $source = Join-Path $installedRoot "Engine\$component"
    if (-not (Test-Path -LiteralPath $source -PathType Container)) {
        throw "Installed Build component is missing: $source"
    }
    $componentEngineRoot = Join-Path $componentsRoot "$component\Engine"
    New-Item -ItemType Directory -Path $componentEngineRoot -Force | Out-Null
    Move-Item -LiteralPath $source -Destination $componentEngineRoot
}
