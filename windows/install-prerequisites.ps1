param(
    [Parameter(Mandatory = $true)]
    [string] $UnrealVersion
)

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
    # Under Windows container process isolation, ExitCode remains null after
    # WaitForExit(timeout). Start-Process -Wait returns the real native code.
    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -NoNewWindow -Wait -PassThru
    $exitCode = $process.ExitCode
    if ($exitCode -notin $SuccessExitCodes) {
        throw "Process failed with exit code ${exitCode}: $FilePath"
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

$versionsPath = Join-Path $PSScriptRoot '..\common\versions.psd1'
$versions = Import-PowerShellDataFile -LiteralPath $versionsPath
if (-not $versions.ContainsKey($UnrealVersion)) {
    throw "Unsupported Unreal Engine version: $UnrealVersion"
}
$settings = $versions[$UnrealVersion]

$longPathsKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'
Set-ItemProperty -LiteralPath $longPathsKey -Name LongPathsEnabled -Value 1

$visualStudioInstaller = 'C:\vs_buildtools.exe'
Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vs_buildtools.exe' -OutFile $visualStudioInstaller
Assert-MicrosoftSignature -Path $visualStudioInstaller

$visualStudioInstallPath = 'C:\BuildTools'
$visualStudioChannel = [string] $settings.VisualStudioChannel
$visualStudioArguments = @(
    '--quiet'
    '--wait'
    '--norestart'
    '--nocache'
    '--installPath'
    $visualStudioInstallPath
    '--channelUri'
    "https://aka.ms/vs/$visualStudioChannel/release/channel"
    '--installChannelUri'
    "https://aka.ms/vs/$visualStudioChannel/release/channel"
    '--channelId'
    "VisualStudio.$visualStudioChannel.Release"
    '--productId'
    'Microsoft.VisualStudio.Product.BuildTools'
    '--locale'
    'en-US'
)
foreach ($component in $settings.VisualStudioComponents) {
    $visualStudioArguments += @('--add', [string] $component)
}
Invoke-NativeCommand -FilePath $visualStudioInstaller -ArgumentList $visualStudioArguments -SuccessExitCodes @(0, 3010)
Remove-Item -LiteralPath $visualStudioInstaller -Force

$vsWhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vsWhere -PathType Leaf)) {
    throw "Visual Studio locator not found: $vsWhere"
}
$installationVersion = (& $vsWhere -products Microsoft.VisualStudio.Product.BuildTools -property installationVersion -latest).Trim()
if ($LASTEXITCODE -ne 0 -or -not $installationVersion) {
    throw 'Failed to query the Visual Studio Build Tools installation'
}
if ([version] $installationVersion -lt [version] $settings.VisualStudioMinimumVersion) {
    throw "Visual Studio $installationVersion is older than required version $($settings.VisualStudioMinimumVersion)"
}

$toolchainRoots = @(Get-ChildItem -LiteralPath (Join-Path $visualStudioInstallPath 'VC\Tools\MSVC') -Directory)
$validToolchains = @($toolchainRoots | Where-Object {
    $version = [version] $_.Name
    $version -ge [version] $settings.MsvcMinimumVersion -and $version -le [version] $settings.MsvcMaximumVersion
})
if ($validToolchains.Count -eq 0) {
    $installed = ($toolchainRoots.Name -join ', ')
    throw "Required MSVC range $($settings.MsvcMinimumVersion)-$($settings.MsvcMaximumVersion) was not installed. Found: $installed"
}
$compiler = Join-Path ($validToolchains | Sort-Object { [version] $_.Name } -Descending | Select-Object -First 1).FullName 'bin\Hostx64\x64\cl.exe'
Invoke-NativeCommand -FilePath $compiler -ArgumentList @('/?')

# Install the exact SDK selected for this Unreal release. BuildGraph's Windows
# strip task additionally requires PDBCopy from Debugging Tools for Windows.
$windowsSdkInstaller = 'C:\winsdksetup.exe'
Invoke-WebRequest -Uri $settings.WindowsSdkInstallerUri -OutFile $windowsSdkInstaller
$windowsSdkInstallerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $windowsSdkInstaller).Hash
if ($windowsSdkInstallerHash -ne $settings.WindowsSdkInstallerSha256) {
    throw "Windows SDK installer checksum mismatch: $windowsSdkInstallerHash"
}
Assert-MicrosoftSignature -Path $windowsSdkInstaller
$windowsSdkArguments = @('/features') + @($settings.WindowsSdkInstallerFeatures) + @('/quiet', '/norestart', '/ceip', 'off')
Invoke-NativeCommand -FilePath $windowsSdkInstaller -ArgumentList $windowsSdkArguments -SuccessExitCodes @(0, 3010)
Remove-Item -LiteralPath $windowsSdkInstaller -Force

$windowsSdkInclude = Join-Path 'C:\Program Files (x86)\Windows Kits\10\Include' $settings.WindowsSdkVersion
if (-not (Test-Path -LiteralPath $windowsSdkInclude -PathType Container)) {
    throw "Required Windows SDK was not installed: $windowsSdkInclude"
}
$pdbCopy = 'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\pdbcopy.exe'
if (-not (Test-Path -LiteralPath $pdbCopy -PathType Leaf)) {
    throw "PDBCopy was not installed: $pdbCopy"
}

if ($UnrealVersion -eq '5.0') {
    $referenceArchive = 'C:\net45-reference-assemblies.zip'
    $referenceUri = 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net45/1.0.3/microsoft.netframework.referenceassemblies.net45.1.0.3.nupkg'
    Invoke-WebRequest -Uri $referenceUri -OutFile $referenceArchive
    $sha512 = [Security.Cryptography.SHA512]::Create()
    try {
        $actualReferenceHash = [Convert]::ToBase64String($sha512.ComputeHash([IO.File]::ReadAllBytes($referenceArchive)))
    } finally {
        $sha512.Dispose()
    }
    $expectedReferenceHash = 'zPJ5Pqc6+cBg4ir33AWryA8CUxJJj68Cs1Cfo8plZt1HH3Q0B/EqVon6LRXw9b8dfQyLYMqTJJk2maXgLhGJIw=='
    if ($actualReferenceHash -ne $expectedReferenceHash) {
        throw "Microsoft .NET Framework 4.5 reference-assemblies checksum mismatch: $actualReferenceHash"
    }
    $referenceRoot = 'C:\net45-reference-assemblies'
    Expand-Archive -LiteralPath $referenceArchive -DestinationPath $referenceRoot
    $referenceDestination = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5'
    New-Item -ItemType Directory -Path $referenceDestination -Force | Out-Null
    Copy-Item -Path (Join-Path $referenceRoot 'build\.NETFramework\v4.5\*') -Destination $referenceDestination -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path $referenceDestination 'mscorlib.dll') -PathType Leaf)) {
        throw '.NET Framework 4.5 reference assemblies were not installed'
    }
    Remove-Item -LiteralPath $referenceArchive, $referenceRoot -Recurse -Force
}

# The full Windows image lacks the legacy DirectX DLLs required by Unreal tools.
$directXArchive = 'C:\directx_Jun2010_redist.exe'
$directXRoot = 'C:\directx_Jun2010_redist'
Invoke-WebRequest -Uri 'https://download.microsoft.com/download/8/4/A/84A35BF1-DAFE-4AE8-82AF-AD2AE20B6B14/directx_Jun2010_redist.exe' -OutFile $directXArchive
$directXHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $directXArchive).Hash
if ($directXHash -ne '053F76DCBB28802E23341B6A787E3B0791C0FA5C8D4D011B1044172DBF89C73B') {
    throw "DirectX redistributable checksum mismatch: $directXHash"
}
Assert-MicrosoftSignature -Path $directXArchive
New-Item -ItemType Directory -Path $directXRoot | Out-Null
Invoke-NativeCommand -FilePath $directXArchive -ArgumentList @('/Q', "/T:$directXRoot")
$directXFiles = @{
    'APR2007_xinput_x64.cab' = 'xinput1_3.dll'
    'Feb2010_X3DAudio_x64.cab' = 'X3DAudio1_7.dll'
    'Jun2010_D3DCompiler_43_x64.cab' = 'D3DCompiler_43.dll'
    'Jun2010_XAudio_x64.cab' = @('XAudio2_7.dll', 'XAPOFX1_5.dll')
}
foreach ($cabinet in $directXFiles.Keys) {
    foreach ($file in @($directXFiles[$cabinet])) {
        Invoke-NativeCommand -FilePath 'C:\Windows\System32\expand.exe' -ArgumentList @((Join-Path $directXRoot $cabinet), "-F:$file", 'C:\Windows\System32\')
    }
}
Remove-Item -LiteralPath $directXArchive, $directXRoot -Recurse -Force
