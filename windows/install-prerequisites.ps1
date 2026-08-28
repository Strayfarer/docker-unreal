param(
    [ValidateSet('All', 'VisualStudio2019', 'VisualStudio2022', 'WindowsSdks', 'Common')]
    [string] $Phase = 'All'
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
    # Process isolation on the LTSC 2019 daemon only reports the native exit
    # code reliably when Start-Process performs the wait itself.
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

function Install-VisualStudioProfile {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable] $Profile
    )

    $channel = [string] $Profile.Channel
    $installer = "C:\vs_buildtools_${channel}.exe"
    Invoke-WebRequest -Uri "https://aka.ms/vs/$channel/release/vs_buildtools.exe" -OutFile $installer
    Assert-MicrosoftSignature -Path $installer
    $arguments = @(
        '--quiet'
        '--wait'
        '--norestart'
        '--nocache'
        '--installPath'
        [string] $Profile.InstallPath
        '--channelUri'
        "https://aka.ms/vs/$channel/release/channel"
        '--installChannelUri'
        "https://aka.ms/vs/$channel/release/channel"
        '--channelId'
        "VisualStudio.$channel.Release"
        '--productId'
        'Microsoft.VisualStudio.Product.BuildTools'
        '--locale'
        'en-US'
    )
    foreach ($component in $Profile.Components) {
        $arguments += @('--add', [string] $component)
    }
    Invoke-NativeCommand -FilePath $installer -ArgumentList $arguments -SuccessExitCodes @(0, 3010)
    Remove-Item -LiteralPath $installer -Force

    $vsWhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vsWhere -PathType Leaf)) {
        throw "Visual Studio locator not found: $vsWhere"
    }
    $maximumMajor = [int] $channel + 1
    $versionRange = "[$($Profile.MinimumVersion),${maximumMajor}.0)"
    $queryArguments = @(
        '-products'
        'Microsoft.VisualStudio.Product.BuildTools'
        '-version'
        $versionRange
        '-latest'
    )
    $installationPath = (& $vsWhere @queryArguments -property installationPath).Trim()
    $installationVersion = (& $vsWhere @queryArguments -property installationVersion).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $installationPath -or -not $installationVersion) {
        throw "Failed to query $($Profile.Name)"
    }
    if ([IO.Path]::GetFullPath($installationPath).TrimEnd('\') -ne [IO.Path]::GetFullPath($Profile.InstallPath).TrimEnd('\')) {
        throw "$($Profile.Name) was installed at an unexpected path: $installationPath"
    }

    $toolchainRoots = @(Get-ChildItem -LiteralPath (Join-Path $installationPath 'VC\Tools\MSVC') -Directory)
    foreach ($requirement in $Profile.Toolchains) {
        $validToolchains = @($toolchainRoots | Where-Object {
            $version = [version] $_.Name
            $version -ge [version] $requirement.MinimumVersion -and $version -le [version] $requirement.MaximumVersion
        })
        if ($validToolchains.Count -eq 0) {
            $installed = ($toolchainRoots.Name -join ', ')
            throw "Required MSVC range $($requirement.MinimumVersion)-$($requirement.MaximumVersion) was not installed. Found: $installed"
        }
        $compiler = Join-Path ($validToolchains | Sort-Object { [version] $_.Name } -Descending | Select-Object -First 1).FullName 'bin\Hostx64\x64\cl.exe'
        Invoke-NativeCommand -FilePath $compiler -ArgumentList @('/?')
    }
}

function Install-WindowsSdkProfile {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable] $Profile
    )

    $installer = 'C:\winsdksetup.exe'
    Invoke-WebRequest -Uri $Profile.InstallerUri -OutFile $installer
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash
    if ($actualHash -ne $Profile.InstallerSha256) {
        throw "Windows SDK installer checksum mismatch: $actualHash"
    }
    Assert-MicrosoftSignature -Path $installer
    $arguments = @('/features') + @($Profile.InstallerFeatures) + @('/quiet', '/norestart', '/ceip', 'off')
    Invoke-NativeCommand -FilePath $installer -ArgumentList $arguments -SuccessExitCodes @(0, 3010)
    Remove-Item -LiteralPath $installer -Force

    $includeRoot = Join-Path 'C:\Program Files (x86)\Windows Kits\10\Include' $Profile.Version
    if (-not (Test-Path -LiteralPath $includeRoot -PathType Container)) {
        throw "Required Windows SDK was not installed: $includeRoot"
    }
    $pdbCopy = 'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\pdbcopy.exe'
    if (-not (Test-Path -LiteralPath $pdbCopy -PathType Leaf)) {
        throw "PDBCopy was not installed: $pdbCopy"
    }
}

$prerequisitesPath = Join-Path $PSScriptRoot '..\common\prerequisites.psd1'
$prerequisites = Import-PowerShellDataFile -LiteralPath $prerequisitesPath

$longPathsKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem'
Set-ItemProperty -LiteralPath $longPathsKey -Name LongPathsEnabled -Value 1

if ($Phase -in @('All', 'VisualStudio2019', 'VisualStudio2022')) {
    $requestedChannel = if ($Phase -eq 'VisualStudio2019') { '16' } elseif ($Phase -eq 'VisualStudio2022') { '17' } else { $null }
    foreach ($profile in $prerequisites.VisualStudio) {
        if (-not $requestedChannel -or $profile.Channel -eq $requestedChannel) {
            Install-VisualStudioProfile -Profile $profile
        }
    }
    if ($Phase -ne 'All') {
        return
    }
}
if ($Phase -in @('All', 'WindowsSdks')) {
    foreach ($profile in $prerequisites.WindowsSdk) {
        Install-WindowsSdkProfile -Profile $profile
    }
    if ($Phase -eq 'WindowsSdks') {
        return
    }
}

# UE 5.0 still compiles a few .NET Framework 4.5 projects.
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

# Unreal tools from every supported branch still load the legacy June 2010
# DirectX side-by-side DLLs.
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

# MinGit supplies the authenticated clone/fetch operations performed by Build.exe.
$gitVersion = '2.55.0.5'
$gitArchive = "C:\MinGit-${gitVersion}-64-bit.zip"
$gitUri = "https://github.com/git-for-windows/git/releases/download/v2.55.0.windows.5/MinGit-${gitVersion}-64-bit.zip"
Invoke-WebRequest -Uri $gitUri -OutFile $gitArchive
$gitHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $gitArchive).Hash
if ($gitHash -ne '56D7B226B7693196CFC71FEF26568F536C4A021AB6C37FF2DB4287BED908E96E') {
    throw "MinGit archive checksum mismatch: $gitHash"
}
$gitRoot = 'C:\Git'
Expand-Archive -LiteralPath $gitArchive -DestinationPath $gitRoot
Remove-Item -LiteralPath $gitArchive -Force
$git = Join-Path $gitRoot 'cmd\git.exe'
Invoke-NativeCommand -FilePath $git -ArgumentList @('--version')
Invoke-NativeCommand -FilePath $git -ArgumentList @('config', '--system', 'core.longpaths', 'true')
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
[Environment]::SetEnvironmentVariable('Path', ((Join-Path $gitRoot 'cmd') + ';' + $machinePath), 'Machine')
