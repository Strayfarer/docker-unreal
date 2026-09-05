def assertValue(actual, expected, description) {
    if (actual != expected) {
        error "${description}: expected '${expected}', got '${actual}'"
    }
}

def candidateImage() {
    return "$DOCKER_NAMESPACE/$DOCKER_IMAGE"
}

def testVersionResolution(unrealDdc) {
    withEnv([
        "UNREAL_DDC=${unrealDdc}",
        "UNREAL_RESOLVER_IMAGE=${candidateImage()}"
    ]) {
        exec '''
            $containerScript = @'
            $ErrorActionPreference = 'Stop'
            $repository = 'C:/workspace/resolver-source'
            New-Item -ItemType Directory -Path $repository | Out-Null
            function Invoke-Git {
                & git.exe -C $repository @args
                if ($LASTEXITCODE -ne 0) {
                    throw "Git failed with exit code ${LASTEXITCODE}: $($args -join ' ')"
                }
            }

            Invoke-Git init --quiet --initial-branch=main
            Invoke-Git config user.name 'docker-unreal integration tests'
            Invoke-Git config user.email 'docker-unreal@example.invalid'
            Set-Content -LiteralPath (Join-Path $repository 'fixture.txt') -Value 'resolver fixture' -Encoding ascii
            Invoke-Git add fixture.txt
            Invoke-Git commit --quiet -m fixture
            Invoke-Git branch 5.8
            @(
                '5.8.0',
                '5.8.2-release',
                '5.8.9-release',
                '5.8.10-release',
                '5.8.11-preview-1',
                '5.8.12-release.1',
                '5.8.020-release',
                '5.80.0-release',
                '5.9.0-release',
                'not-semver'
            ) | ForEach-Object {
                Invoke-Git tag $_
            }

            $env:UNREAL_SOURCE = $repository
            $env:UNREAL_VERSION = '5.8'
            Remove-Item Env:UNREAL_VERSION_MODE -ErrorAction SilentlyContinue
            $resolvedTag = (& Unreal.exe --version | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $resolvedTag -ne '5.8.10-release') {
                throw "Default tag resolution expected 5.8.10-release, got: $resolvedTag"
            }

            $env:UNREAL_VERSION_MODE = 'branch'
            $resolvedCommit = (& Unreal.exe --version | Out-String).Trim()
            $expectedCommit = (& git.exe -C $repository rev-parse refs/heads/5.8 | Out-String).Trim()
            if ($LASTEXITCODE -ne 0 -or $resolvedCommit -ne $expectedCommit) {
                throw "Branch resolution expected $expectedCommit, got: $resolvedCommit"
            }

            $roots = @('C:/unreal/binaries', 'C:/unreal/cache', 'C:/unreal/sources')
            $entries = @($roots | ForEach-Object { Get-ChildItem -LiteralPath $_ -Force })
            if ($entries.Count -ne 0) {
                throw "Unreal --version materialized engine state: $($entries.FullName -join ', ')"
            }
            $toolchainConfiguration = Join-Path $env:APPDATA 'Unreal Engine/UnrealBuildTool/BuildConfiguration.xml'
            if (Test-Path -LiteralPath $toolchainConfiguration) {
                throw "Unreal --version configured the toolchain: $toolchainConfiguration"
            }
            if ((& Unreal.exe --help) -notcontains '  Unreal --compile') {
                throw 'Unreal help does not list the compile command'
            }

            $engineRoot = 'C:/unreal/binaries/5.8'
            $buildDirectory = Join-Path $engineRoot 'Engine/Build'
            $batchDirectory = Join-Path $buildDirectory 'BatchFiles'
            $dotnetDirectory = Join-Path $engineRoot 'Engine/Binaries/ThirdParty/DotNet/8.0.300/win-x64'
            New-Item -ItemType Directory -Path $batchDirectory -Force | Out-Null
            New-Item -ItemType Directory -Path $dotnetDirectory -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $dotnetDirectory 'dotnet.exe') -Value 'integration fixture' -Encoding ascii
            @(
                '@echo off',
                'powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0VerifyDdc.ps1" %*',
                'exit /b %ERRORLEVEL%'
            ) | Set-Content -LiteralPath (Join-Path $batchDirectory 'Build.bat') -Encoding ascii
            @(
                '$ErrorActionPreference = "Stop"',
                '$expectedLocal = [IO.Path]::GetFullPath("C:/unreal/cache/ddc")',
                '$localDdc = [Environment]::GetEnvironmentVariable("UE-LocalDataCachePath")',
                '$sharedDdc = [Environment]::GetEnvironmentVariable("UE-ZenSharedDataCacheHost")',
                'if ([IO.Path]::GetFullPath($localDdc) -ne $expectedLocal) { throw "Unexpected local DDC: $localDdc" }',
                'if ($args -contains "-Target=integration-shared-ddc") {',
                '    if ($sharedDdc -ne $env:UNREAL_DDC) { throw "UNREAL_DDC was not mapped to the Zen shared host override" }',
                '    $response = Invoke-WebRequest -UseBasicParsing -Uri $sharedDdc -TimeoutSec 15',
                '    if ([int]$response.StatusCode -ne 200) { throw "Shared DDC returned HTTP $($response.StatusCode)" }',
                '    exit 0',
                '}',
                'if ($args -contains "-Target=integration-local-ddc-fallback") {',
                '    if (-not [string]::IsNullOrEmpty($sharedDdc)) { throw "Zen shared host override remained set without UNREAL_DDC" }',
                '    New-Item -ItemType Directory -Path $localDdc -Force | Out-Null',
                '    Set-Content -LiteralPath (Join-Path $localDdc "integration-sentinel.txt") -Value "local fallback" -Encoding ascii',
                '    exit 0',
                '}',
                'throw "Unexpected DDC integration target: $args"'
            ) | Set-Content -LiteralPath (Join-Path $batchDirectory 'VerifyDdc.ps1') -Encoding ascii
            Set-Content -LiteralPath (Join-Path $buildDirectory 'Build.version') -Value '{"MajorVersion":5,"MinorVersion":8,"PatchVersion":10}' -Encoding ascii
            Set-Content -LiteralPath (Join-Path $engineRoot 'branch-resolved.marker') -Value $resolvedCommit -Encoding ascii
            [ordered]@{
                Version = '5.8'
                PatchVersion = '5.8.10'
                Source = $repository
                Commit = $resolvedCommit
                BuildProfile = 'win64-development-shipping-v1'
            } | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $engineRoot '.docker-unreal.json') -Encoding utf8

            Remove-Item Env:UNREAL_VERSION_MODE
            & Unreal.exe --compile
            if ($LASTEXITCODE -ne 0) {
                throw "Unreal --compile failed with exit code $LASTEXITCODE"
            }
            $sourceEntries = @(Get-ChildItem -LiteralPath 'C:/unreal/sources' -Force)
            if ($sourceEntries.Count -ne 0) {
                throw "Tag-mode compile touched source despite an exact branch-resolved installation: $($sourceEntries.FullName -join ', ')"
            }
            $sentinel = Join-Path $engineRoot 'branch-resolved.marker'
            if (-not (Test-Path -LiteralPath $sentinel) -or (Get-Content -Raw -LiteralPath $sentinel).Trim() -ne $resolvedCommit) {
                throw 'Tag-mode compile replaced the branch-resolved installation at the same commit'
            }

            $expectedDdc = 'C:/unreal/cache/ddc'
            if (-not (Test-Path -LiteralPath $expectedDdc -PathType Container)) {
                throw "Unreal --compile did not prepare the persistent local DDC at $expectedDdc"
            }
            if (Test-Path -LiteralPath (Join-Path $expectedDdc '5.8')) {
                throw 'The persistent local DDC was partitioned by UNREAL_VERSION'
            }
            & Unreal.exe Build -Target=integration-shared-ddc
            if ($LASTEXITCODE -ne 0) {
                throw "Unreal Build did not connect to the configured shared DDC; exit code $LASTEXITCODE"
            }

            Remove-Item Env:UNREAL_DDC
            & Unreal.exe Build -Target=integration-local-ddc-fallback
            if ($LASTEXITCODE -ne 0) {
                throw "Unreal Build did not fall back to the local DDC; exit code $LASTEXITCODE"
            }
            if (-not (Test-Path -LiteralPath (Join-Path $expectedDdc 'integration-sentinel.txt'))) {
                throw 'The local DDC fallback did not retain derived data'
            }
'@

            & docker run --rm --env UNREAL_DDC $env:UNREAL_RESOLVER_IMAGE powershell.exe -NoLogo -NoProfile -NonInteractive -Command $containerScript
            if ($LASTEXITCODE -ne 0) {
                throw "Resolver contract container failed with exit code $LASTEXITCODE"
            }
        '''
    }
}

def testImage(unrealVersion, expectedTag) {
    def container = execStdout "docker run --detach --tty --volumes-from agents_jenkins-agent --workdir=%WORKSPACE% --env WORKSPACE --env UNREAL_VERSION --env UNREAL_VERSION_MODE --env UNREAL_SOURCE --env UNREAL_DDC --env UNREAL_CREDENTIALS_USR --env UNREAL_CREDENTIALS_PSW ${candidateImage()} cmd.exe"
    try {
        exec "docker exec ${container} powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .jenkins/TestImage.ps1 -UnrealVersion ${unrealVersion} -ExpectedTag ${expectedTag}"
    } finally {
        exec "docker rm --force ${container}"
    }
}

properties([
    parameters([
        choice(
            name: 'DOCKER_NAMESPACE',
            choices: ['faulo', 'tmp'],
            description: 'Docker image namespace to test'
        )
    ]),
    disableConcurrentBuilds(),
    disableResume()
])

def hosts = ['Dende']
def dockerNamespace = params.DOCKER_NAMESPACE ?: 'faulo'
def unrealVersions = [
    [minor: '5.0', tag: '5.0.3-release'],
    [minor: '5.6', tag: '5.6.1-release'],
    [minor: '5.7', tag: '5.7.4-release']
]
def unrealSource = 'https://github.com/EpicGames/UnrealEngine'
def unrealDdc = 'http://192.168.194.110:8558'
def unrealCredentials = 'Faulo-GitHub'

stage('Integration Tests') {
    for (def host in hosts) {
        stage("Host: ${host}") {
            node(host) {
                deleteDir()
                checkout scm

                stage('Version resolution') {
                    withEnv(["DOCKER_NAMESPACE=${dockerNamespace}"]) {
                        withEnvFile {
                            testVersionResolution(unrealDdc)
                        }
                    }
                }

                for (def selectedVersion in unrealVersions) {
                    def unrealVersion = selectedVersion.minor
                    stage("Unreal v${unrealVersion}") {
                        catchError(
                            message: "Unreal v${unrealVersion} integration test failed on ${host}",
                            stageResult: 'FAILURE',
                            buildResult: 'FAILURE',
                            catchInterruptions: false
                        ) {
                            withEnv([
                                "DOCKER_NAMESPACE=${dockerNamespace}",
                                "UNREAL_VERSION=${unrealVersion}",
                                'UNREAL_VERSION_MODE=tag',
                                "UNREAL_SOURCE=${unrealSource}",
                                "UNREAL_DDC=${unrealDdc}"
                            ]) {
                                withEnvFile {
                                 withCredentials([usernamePassword(credentialsId: unrealCredentials, usernameVariable: 'UNREAL_CREDENTIALS_USR', passwordVariable: 'UNREAL_CREDENTIALS_PSW')]) {
                                    echo "Testing ${candidateImage()} with Unreal v${unrealVersion} on ${host}"
                                    testImage(unrealVersion, selectedVersion.tag)
                            }}}
                        }
                    }
                }
            }
        }
    }
}
