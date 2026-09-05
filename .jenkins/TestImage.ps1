param(
    [Parameter(Mandatory = $true)]
    [string] $UnrealVersion,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedTag
)

$ErrorActionPreference = 'Stop'

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Executable,

        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE"
    }
}

$project = Join-Path $env:WORKSPACE 'test-files/EmptyGame/EmptyGame.uproject'
$archive = Join-Path $env:WORKSPACE ".jenkins/artifacts/$UnrealVersion"

Invoke-Native Unreal.exe --help
$resolvedTag = (& Unreal.exe --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Unreal --version failed with exit code $LASTEXITCODE"
}
if ($resolvedTag -ne $ExpectedTag) {
    throw "Unreal v$UnrealVersion resolved tag: expected '$ExpectedTag', got '$resolvedTag'"
}

# Build ensures the engine on a cold volume and takes the precompiled happy path on a cache hit.
Invoke-Native Unreal.exe Build "-Target=EmptyGameEditor Win64 Development" "-Project=$project" -WaitMutex
Invoke-Native Unreal.exe Cmd $project -run=CompileAllBlueprints -AllowListFile=Config/BlueprintAllowList.txt -Unattended -NullRHI -NoSplash -NoP4

Remove-Item -LiteralPath $archive -Recurse -Force -ErrorAction SilentlyContinue
Invoke-Native Unreal.exe RunUAT BuildCookRun "-Project=$project" -ClientConfig=Shipping -TargetPlatform=Win64 -NoP4 -Build -Cook -AllMaps -Stage -Pak -Package -Archive "-ArchiveDirectory=$archive" -Unattended -UTF8Output

$packagedExecutable = Get-ChildItem -LiteralPath $archive -Filter EmptyGame.exe -File -Recurse | Select-Object -First 1
if (-not $packagedExecutable) {
    throw "Unreal v$UnrealVersion did not produce a packaged Shipping executable under $archive"
}
