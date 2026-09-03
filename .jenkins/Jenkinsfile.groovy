def assertValue(actual, expected, description) {
    if (actual != expected) {
        error "${description}: expected '${expected}', got '${actual}'"
    }
}

def candidateImage() {
    return "$DOCKER_NAMESPACE/$DOCKER_IMAGE"
}

def testVersionResolution() {
    withEnv(["UNREAL_RESOLVER_IMAGE=${candidateImage()}"]) {
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
                'if /I not "%UE-LocalDataCachePath%"=="C:\unreal\cache\ddc\5.8" exit /b 92',
                'if not exist "%UE-LocalDataCachePath%\integration-sentinel.txt" exit /b 93',
                'exit /b 0'
            ) | Set-Content -LiteralPath (Join-Path $batchDirectory 'Build.bat') -Encoding ascii
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

            $expectedDdc = 'C:/unreal/cache/ddc/5.8'
            if (-not (Test-Path -LiteralPath $expectedDdc -PathType Container)) {
                throw "Unreal --compile did not prepare the version-scoped DDC at $expectedDdc"
            }
            Set-Content -LiteralPath (Join-Path $expectedDdc 'integration-sentinel.txt') -Value 'persistent DDC fixture' -Encoding ascii
            & Unreal.exe Build -Target='integration DDC environment fixture'
            if ($LASTEXITCODE -ne 0) {
                throw "Unreal Build did not receive the version-scoped DDC environment; exit code $LASTEXITCODE"
            }
'@

            & docker run --rm $env:UNREAL_RESOLVER_IMAGE powershell.exe -NoLogo -NoProfile -NonInteractive -Command $containerScript
            if ($LASTEXITCODE -ne 0) {
                throw "Resolver contract container failed with exit code $LASTEXITCODE"
            }
        '''
    }
}

def testImage(unrealVersion, expectedTag) {
    docker.image(candidateImage()).inside('-v unreal-binaries:C:/unreal/binaries -v unreal-cache:C:/unreal/cache -v unreal-sources:C:/unreal/sources') {
        def project = '%WORKSPACE%\\test-files\\EmptyGame\\EmptyGame.uproject'
        def archive = "%WORKSPACE%\\.jenkins\\artifacts\\${unrealVersion}"

        exec 'Unreal --help'
        def resolvedTag = execStdout 'Unreal --version'
        assertValue(resolvedTag, expectedTag, "Unreal v${unrealVersion} resolved tag")

        // Build ensures the engine on a cold volume and takes the precompiled happy path on a cache hit.
        exec "Unreal Build -Target=\"EmptyGameEditor Win64 Development\" -Project=\"${project}\" -WaitMutex"
        exec "Unreal Cmd \"${project}\" -run=CompileAllBlueprints -AllowListFile=Config/BlueprintAllowList.txt -Unattended -NullRHI -NoSplash -NoP4"

        dir(".jenkins/artifacts/${unrealVersion}") {
            deleteDir()
        }
        exec "Unreal RunUAT BuildCookRun -Project=\"${project}\" -ClientConfig=Shipping -TargetPlatform=Win64 -NoP4 -Build -Cook -AllMaps -Stage -Pak -Package -Archive -ArchiveDirectory=\"${archive}\" -Unattended -UTF8Output"
        def packagedExecutables = findFiles(glob: ".jenkins/artifacts/${unrealVersion}/**/EmptyGame.exe")
        assertValue(
            packagedExecutables.size() > 0,
            true,
            "Unreal v${unrealVersion} packaged Shipping executable"
        )
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
                            testVersionResolution()
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
                                "UNREAL_SOURCE=${unrealSource}"
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
