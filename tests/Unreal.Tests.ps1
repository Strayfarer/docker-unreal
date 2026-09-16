param(
    [Parameter(Mandatory)]
    [string] $Namespace,
    
    [Parameter(Mandatory)]
    [string] $Name,

    [Parameter(Mandatory)]
    [string] $Variant,
    
    [Parameter(Mandatory)]
    [string] $Context,
    
    [Parameter(Mandatory)]
    [string] $Image,

    [Parameter(Mandatory)]
    [string] $Os,

    [Parameter(Mandatory)]
    [AllowEmptyCollection()]
    [string[]] $DockerRunArguments
)

BeforeAll {
    . (Join-Path $PSScriptRoot '../.jenkins/Docker.ps1')

    $container = Invoke-DockerOutput `
        -Context $Context `
        -Arguments @('run', '--detach', '--tty', $Image, 'cmd.exe') `
        -RunArguments $DockerRunArguments
}

AfterAll {
    if ($container) {
        Invoke-Docker -Context $Context -Arguments @('rm', '--force', $container)
    }
}

Describe "Unreal container [$Context, $Image]" {
    It "declares the Jenkins Docker Pipeline command contract" {
        $entrypoint = Invoke-DockerOutput `
            -Context $Context `
            -Arguments @('image', 'inspect', $Image, '--format', '{{json .Config.Entrypoint}}')
        $command = Invoke-DockerOutput `
            -Context $Context `
            -Arguments @('image', 'inspect', $Image, '--format', '{{json .Config.Cmd}}')

        $entrypoint | Should -BeIn @('null', '[]')
        $command | Should -Be '["unreal","help"]'
    }

    It "runs cmd.exe as the keeper process" {
        $processes = Invoke-DockerOutput -Context $Context -Arguments @('top', $container)

        $processes | Should -Match '(?im)^cmd\.exe\s'
    }

    It "displays help" {
        Invoke-Docker -Context $Context -Arguments @('exec', $container, 'Unreal.exe', 'help')
    }
}
