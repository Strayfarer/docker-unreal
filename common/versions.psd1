@{
    '5.0' = @{
        PatchVersion = '5.0.3'
        SourceRef = '5.0.3-release'
        SourceCommit = 'd9d435c9c280b99a6c679b517adedd3f4b02cfd7'
        VisualStudioChannel = '16'
        VisualStudioMinimumVersion = '16.11'
        MsvcMinimumVersion = '14.29.30133'
        MsvcMaximumVersion = '14.29.30136'
        VisualStudioComponents = @(
            'Microsoft.VisualStudio.Workload.VCTools'
            'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
            'Microsoft.Net.Component.4.6.2.TargetingPack'
        )
        WindowsSdkVersion = '10.0.18362.0'
        WindowsSdkInstallerUri = 'https://download.microsoft.com/download/4/2/2/42245968-6A79-4DA7-A5FB-08C0AD0AE661/windowssdk/winsdksetup.exe'
        WindowsSdkInstallerSha256 = '2E28117E82B4D02FE30D564B835ACE9976612609271265872F20F2256A9C506B'
        WindowsSdkInstallerFeatures = @(
            'OptionId.DesktopCPPx64'
            'OptionId.DesktopCPPx86'
            'OptionId.SigningTools'
            'OptionId.WindowsDesktopDebuggers'
        )
    }
    '5.7' = @{
        PatchVersion = '5.7.4'
        SourceRef = '5.7.4-release'
        SourceCommit = '260bb2e1c5610b31c63a36206eedd289409c5f11'
        VisualStudioChannel = '17'
        VisualStudioMinimumVersion = '17.14'
        MsvcMinimumVersion = '14.44.35207'
        MsvcMaximumVersion = '14.44.99999'
        VisualStudioComponents = @(
            'Microsoft.VisualStudio.Workload.VCTools'
            'Microsoft.VisualStudio.Component.VC.14.44.17.14.x86.x64'
            'Microsoft.VisualStudio.Component.VC.14.44.17.14.ATL'
            'Microsoft.VisualStudio.Component.Windows11SDK.22621'
            'Microsoft.Net.Component.4.6.2.TargetingPack'
        )
        WindowsSdkVersion = '10.0.22621.0'
        WindowsSdkInstallerUri = 'https://download.microsoft.com/download/cb9de490-6e67-4ac6-8c2c-6dfabb824e8a/windowssdk/winsdksetup.exe'
        WindowsSdkInstallerSha256 = '007085E9B90637F0EB0F041F9CCE0D64E2C5DD3DE17E1B6976A92DBC46E100C5'
        WindowsSdkInstallerFeatures = @(
            'OptionId.WindowsDesktopDebuggers'
        )
    }
}
