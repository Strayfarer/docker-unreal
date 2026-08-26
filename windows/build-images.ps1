[CmdletBinding()]
param(
    [string] $DockerContext = 'windows',

    [ValidateSet('tmp')]
    [string] $DockerNamespace = 'tmp',

    [string[]] $UnrealVersion,

    [string] $SourceCache = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'docker-unreal\sources')
)

$ErrorActionPreference = 'Stop'

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $ArgumentList,

        [ValidateRange(1, 5)]
        [int] $Attempts = 1
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        & $FilePath @ArgumentList
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) {
            return
        }
        if ($attempt -eq $Attempts) {
            throw "Command failed with exit code ${exitCode}: $FilePath $($ArgumentList -join ' ')"
        }
        Write-Warning "Command failed with exit code $exitCode; retrying attempt $($attempt + 1) of $Attempts"
        Start-Sleep -Seconds 5
    }
}

$repositoryRoot = Split-Path $PSScriptRoot -Parent
$versions = Import-PowerShellDataFile -LiteralPath (Join-Path $repositoryRoot 'common\versions.psd1')
$dependencyManifests = Import-PowerShellDataFile -LiteralPath (Join-Path $repositoryRoot 'common\dependency-manifests.psd1')
if (-not $UnrealVersion) {
    $UnrealVersion = @($versions.Keys | Sort-Object { [version] $_ })
}

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
if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
    throw 'tar.exe is required to prepare the Unreal Engine source context'
}

New-Item -ItemType Directory -Path $SourceCache -Force | Out-Null
foreach ($minorVersion in $UnrealVersion) {
    if (-not $versions.ContainsKey($minorVersion)) {
        throw "Unsupported Unreal Engine version: $minorVersion"
    }
    $settings = $versions[$minorVersion]
    $sourceDirectory = Join-Path $SourceCache $settings.PatchVersion

    if (-not (Test-Path -LiteralPath $sourceDirectory)) {
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
            throw 'GitHub CLI is required to download the private Unreal Engine source archive'
        }
        $commitEndpoint = "repos/EpicGames/UnrealEngine/commits/$($settings.SourceCommit)"
        $accessibleCommit = (& gh api $commitEndpoint --jq '.sha').Trim()
        if ($LASTEXITCODE -ne 0 -or $accessibleCommit -ne $settings.SourceCommit) {
            throw "GitHub did not resolve the pinned Unreal source commit: $($settings.SourceCommit)"
        }

        $sourceStaging = "$sourceDirectory.extracting"
        if (Test-Path -LiteralPath $sourceStaging) {
            throw "An incomplete source extraction exists at '$sourceStaging'. Move it aside and retry."
        }
        New-Item -ItemType Directory -Path $sourceStaging | Out-Null

        # Run the binary pipeline through cmd.exe so this remains safe when the
        # Explorer entry point invokes Windows PowerShell 5.1.
        $archiveEndpoint = "repos/EpicGames/UnrealEngine/tarball/$($settings.SourceCommit)"
        $archiveCommand = "gh api `"$archiveEndpoint`" | tar.exe -xzf - --strip-components=1 -C `"$sourceStaging`""
        Invoke-NativeCommand -FilePath 'cmd.exe' -ArgumentList @('/D', '/S', '/C', $archiveCommand) -Attempts 3

        $sourceMarker = @{
            PatchVersion = [string] $settings.PatchVersion
            SourceCommit = [string] $settings.SourceCommit
        } | ConvertTo-Json -Compress
        Set-Content -LiteralPath (Join-Path $sourceStaging '.docker-unreal-source.json') -Value $sourceMarker -Encoding ascii
        Set-Content -LiteralPath (Join-Path $sourceStaging '.dockerignore') -Value @('.git', '.docker-unreal-source.json') -Encoding ascii
        Move-Item -LiteralPath $sourceStaging -Destination $sourceDirectory
    }

    $sourceMarkerPath = Join-Path $sourceDirectory '.docker-unreal-source.json'
    if (-not (Test-Path -LiteralPath $sourceMarkerPath -PathType Leaf)) {
        throw "Source cache '$sourceDirectory' has no provenance marker. Move it aside and retry."
    }
    $sourceMarker = Get-Content -LiteralPath $sourceMarkerPath -Raw | ConvertFrom-Json
    if ($sourceMarker.PatchVersion -ne $settings.PatchVersion -or $sourceMarker.SourceCommit -ne $settings.SourceCommit) {
        throw "Source cache '$sourceDirectory' does not match the pinned Unreal release. Move it aside and retry."
    }

    $sourceBuildVersionPath = Join-Path $sourceDirectory 'Engine\Build\Build.version'
    $sourceBuildVersion = Get-Content -LiteralPath $sourceBuildVersionPath -Raw | ConvertFrom-Json
    $sourcePatchVersion = '{0}.{1}.{2}' -f $sourceBuildVersion.MajorVersion, $sourceBuildVersion.MinorVersion, $sourceBuildVersion.PatchVersion
    if ($sourcePatchVersion -ne $settings.PatchVersion) {
        throw "Source cache '$sourceDirectory' contains Unreal Engine $sourcePatchVersion instead of $($settings.PatchVersion)."
    }

    $dependencyManifestSource = Join-Path $sourceDirectory 'Engine\Build\Commit.gitdeps.xml'
    if (-not (Test-Path -LiteralPath $dependencyManifestSource -PathType Leaf)) {
        throw "Source cache '$sourceDirectory' has no dependency manifest."
    }
    if ($dependencyManifests.ContainsKey($minorVersion)) {
        $dependencySettings = $dependencyManifests[$minorVersion]
        $assetName = [string] $dependencySettings.AssetName
        $assetDirectory = Join-Path $SourceCache ("release-assets\{0}" -f $settings.PatchVersion)
        $assetPath = Join-Path $assetDirectory $assetName
        New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
            if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
                throw 'GitHub CLI is required to download the repaired Epic dependency manifest'
            }
            $assetStaging = Join-Path $assetDirectory 'downloading'
            if (Test-Path -LiteralPath $assetStaging) {
                throw "An incomplete dependency-manifest download exists at '$assetStaging'. Move it aside and retry."
            }
            New-Item -ItemType Directory -Path $assetStaging | Out-Null
            try {
                Invoke-NativeCommand -FilePath 'gh' -ArgumentList @(
                    'release'
                    'download'
                    [string] $settings.SourceRef
                    '--repo'
                    'EpicGames/UnrealEngine'
                    '--pattern'
                    $assetName
                    '--dir'
                    $assetStaging
                ) -Attempts 3
                Move-Item -LiteralPath (Join-Path $assetStaging $assetName) -Destination $assetPath
            } finally {
                if (Test-Path -LiteralPath $assetStaging) {
                    Remove-Item -LiteralPath $assetStaging -Recurse -Force
                }
            }
        }

        $asset = Get-Item -LiteralPath $assetPath
        if ($asset.Length -ne [long] $dependencySettings.AssetSize) {
            throw "Epic dependency manifest has unexpected size $($asset.Length): $assetPath"
        }
        $assetHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $assetPath).Hash
        if ($assetHash -ne $dependencySettings.AssetSha256) {
            throw "Epic dependency manifest checksum mismatch: $assetHash"
        }
        $dependencyManifestSource = $assetPath
    }

    $tag = "$DockerNamespace/${imageName}:$minorVersion"
    Write-Host "Building $tag from Unreal Engine $($settings.PatchVersion) ($($settings.SourceCommit)) on Docker context $DockerContext"
    $contextArchive = Join-Path $repositoryRoot 'unreal.tar'
    $contextDependencyManifest = Join-Path $repositoryRoot 'unreal-dependencies.xml'
    foreach ($contextInput in @($contextArchive, $contextDependencyManifest)) {
        if (Test-Path -LiteralPath $contextInput) {
            throw "Docker source staging path already exists: $contextInput"
        }
    }

    $previousBuildKit = $env:DOCKER_BUILDKIT
    try {
        Copy-Item -LiteralPath $dependencyManifestSource -Destination $contextDependencyManifest
        Write-Host "Packing the pinned source into $contextArchive"
        $tarArguments = @(
            '-cf'
            $contextArchive
            '--exclude'
            '.git'
            '--exclude'
            '.dockerignore'
            '--exclude'
            '.docker-unreal-source.json'
            '-C'
            $sourceDirectory
            '.'
        )
        Invoke-NativeCommand -FilePath 'tar.exe' -ArgumentList $tarArguments

        # BuildKit's Windows executor is not implemented on the LTSC 2019
        # daemon, so Windows builds must use Docker's legacy builder.
        $env:DOCKER_BUILDKIT = '0'
        $dockerArguments = @(
            '--context'
            $DockerContext
            'build'
            '--pull'
            '--file'
            (Join-Path $repositoryRoot 'windows\Dockerfile')
            '--build-arg'
            "UNREAL_VERSION=$minorVersion"
            '--build-arg'
            "UNREAL_PATCH_VERSION=$($settings.PatchVersion)"
            '--build-arg'
            "UNREAL_SOURCE_REF=$($settings.SourceRef)"
            '--build-arg'
            "UNREAL_SOURCE_COMMIT=$($settings.SourceCommit)"
            '--tag'
            $tag
            $repositoryRoot
        )
        Invoke-NativeCommand -FilePath 'docker' -ArgumentList $dockerArguments
    } finally {
        $env:DOCKER_BUILDKIT = $previousBuildKit
        if (Test-Path -LiteralPath $contextArchive) {
            Remove-Item -LiteralPath $contextArchive -Force
        }
        if (Test-Path -LiteralPath $contextDependencyManifest) {
            Remove-Item -LiteralPath $contextDependencyManifest -Force
        }
    }
}
