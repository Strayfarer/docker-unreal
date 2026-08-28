@{
    VisualStudio = @(
        @{
            Name = 'Visual Studio 2019 Build Tools'
            Channel = '16'
            InstallPath = 'C:\BuildTools\2019'
            MinimumVersion = '16.11'
            Components = @(
                'Microsoft.VisualStudio.Workload.VCTools'
                'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
                'Microsoft.Net.Component.4.6.2.TargetingPack'
            )
            Toolchains = @(
                @{
                    MinimumVersion = '14.29.30133'
                    MaximumVersion = '14.29.30199'
                }
            )
        }
        @{
            Name = 'Visual Studio 2022 Build Tools'
            Channel = '17'
            InstallPath = 'C:\BuildTools\2022'
            MinimumVersion = '17.14'
            Components = @(
                'Microsoft.VisualStudio.Component.VC.CoreBuildTools'
                'Microsoft.VisualStudio.Component.VC.14.38.17.8.x86.x64'
                'Microsoft.VisualStudio.Component.VC.14.38.17.8.ATL'
                'Microsoft.VisualStudio.Component.VC.14.44.17.14.x86.x64'
                'Microsoft.VisualStudio.Component.VC.14.44.17.14.ATL'
                'Microsoft.VisualStudio.Component.Windows11SDK.22621'
                'Microsoft.Net.Component.4.6.2.TargetingPack'
            )
            Toolchains = @(
                @{
                    MinimumVersion = '14.38.33130'
                    MaximumVersion = '14.38.99999'
                }
                @{
                    MinimumVersion = '14.44.35207'
                    MaximumVersion = '14.44.99999'
                }
            )
        }
    )
    WindowsSdk = @(
        @{
            Version = '10.0.18362.0'
            InstallerUri = 'https://download.microsoft.com/download/4/2/2/42245968-6A79-4DA7-A5FB-08C0AD0AE661/windowssdk/winsdksetup.exe'
            InstallerSha256 = '2E28117E82B4D02FE30D564B835ACE9976612609271265872F20F2256A9C506B'
            InstallerFeatures = @(
                'OptionId.DesktopCPPx64'
                'OptionId.DesktopCPPx86'
                'OptionId.SigningTools'
                'OptionId.WindowsDesktopDebuggers'
                'OptionId.NetFxSoftwareDevelopmentKit'
            )
        }
        @{
            Version = '10.0.22621.0'
            InstallerUri = 'https://download.microsoft.com/download/cb9de490-6e67-4ac6-8c2c-6dfabb824e8a/windowssdk/winsdksetup.exe'
            InstallerSha256 = '007085E9B90637F0EB0F041F9CCE0D64E2C5DD3DE17E1B6976A92DBC46E100C5'
            InstallerFeatures = @(
                'OptionId.WindowsDesktopDebuggers'
            )
        }
    )
}
