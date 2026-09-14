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
    It "displays help" {
        Invoke-Docker -Context $Context -Arguments @('exec', $container, 'Unreal.exe', '--help')
    }
}
