$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [string[]] $ArgumentList = @(),

        [int[]] $SuccessExitCodes = @(0)
    )

    Write-Host ('Executing: {0} {1}' -f $FilePath, ($ArgumentList -join ' '))
    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -NoNewWindow -Wait -PassThru
    if ($process.ExitCode -notin $SuccessExitCodes) {
        throw "Process failed with exit code $($process.ExitCode): $FilePath"
    }
}

function Assert-MicrosoftSignature {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid') {
        throw "Invalid Authenticode signature for ${Path}: $($signature.Status)"
    }
    if ($signature.SignerCertificate.Subject -notmatch '(^|, )O=Microsoft Corporation(,|$)') {
        throw "Unexpected publisher for ${Path}: $($signature.SignerCertificate.Subject)"
    }
}

function Get-NetFxSdkRoot {
    $registryRoot = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Microsoft SDKs\NETFXSDK'
    if (-not (Test-Path -LiteralPath $registryRoot)) {
        return $null
    }

    $candidates = @(
        foreach ($key in Get-ChildItem -LiteralPath $registryRoot) {
            $version = $null
            if ([version]::TryParse($key.PSChildName, [ref] $version) -and $version -ge [version] '4.6') {
                [pscustomobject] @{
                    Key = $key.PSPath
                    Version = $version
                }
            }
        }
    ) | Sort-Object Version -Descending

    foreach ($candidate in $candidates) {
        $root = (Get-ItemProperty -LiteralPath $candidate.Key -Name KitsInstallationFolder -ErrorAction SilentlyContinue).KitsInstallationFolder
        if ($root -and
            (Test-Path -LiteralPath (Join-Path $root 'Include\um\mscoree.h') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $root 'Lib\um\x64\mscoree.lib') -PathType Leaf)) {
            return $root
        }
    }
    return $null
}

$netFxSdkRoot = Get-NetFxSdkRoot
if (-not $netFxSdkRoot) {
    # Windows SDK 18362 is the last SDK in the archive that matches UE 5.0's
    # preferred toolchain and remains compatible with LTSC 2019 containers.
    $installerUri = 'https://download.microsoft.com/download/4/2/2/42245968-6A79-4DA7-A5FB-08C0AD0AE661/windowssdk/winsdksetup.exe'
    $installerSha256 = '2E28117E82B4D02FE30D564B835ACE9976612609271265872F20F2256A9C506B'
    $installer = 'C:\winsdk-netfx-18362.exe'
    Invoke-WebRequest -Uri $installerUri -OutFile $installer
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash
    if ($actualHash -ne $installerSha256) {
        throw "Windows SDK installer checksum mismatch: $actualHash"
    }
    Assert-MicrosoftSignature -Path $installer
    Invoke-NativeCommand -FilePath $installer -ArgumentList @(
        '/features'
        'OptionId.NetFxSoftwareDevelopmentKit'
        '/quiet'
        '/norestart'
        '/ceip'
        'off'
    ) -SuccessExitCodes @(0, 3010)
    Remove-Item -LiteralPath $installer -Force
    $netFxSdkRoot = Get-NetFxSdkRoot
}

if (-not $netFxSdkRoot) {
    throw '.NET Framework SDK 4.6 or newer did not register a usable KitsInstallationFolder'
}
Write-Host ".NET Framework SDK validated at $netFxSdkRoot"
