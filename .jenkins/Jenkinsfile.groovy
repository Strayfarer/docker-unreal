def assertValue(actual, expected, description) {
    if (actual != expected) {
        error "${description}: expected '${expected}', got '${actual}'"
    }
}

def candidateImage() {
    return "$DOCKER_NAMESPACE/$DOCKER_IMAGE"
}

def createResolverSource() {
    exec '''
        $repository = Join-Path $env:WORKSPACE '.jenkins/resolver-source'
        if (Test-Path -LiteralPath $repository) {
            throw "Resolver fixture already exists: $repository"
        }

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
    '''
}

def assertVersionDidNotMaterializeEngine() {
    exec '''
        $roots = @('C:/unreal/binaries', 'C:/unreal/cache', 'C:/unreal/sources')
        $entries = @($roots | ForEach-Object { Get-ChildItem -LiteralPath $_ -Force })
        if ($entries.Count -ne 0) {
            throw "Unreal --version materialized engine state: $($entries.FullName -join ', ')"
        }

        $toolchainConfiguration = Join-Path $env:APPDATA 'Unreal Engine/UnrealBuildTool/BuildConfiguration.xml'
        if (Test-Path -LiteralPath $toolchainConfiguration) {
            throw "Unreal --version configured the toolchain: $toolchainConfiguration"
        }
    '''
}

def seedPublishedResolverEngine(commit) {
    withEnv(["RESOLVER_COMMIT=${commit}"]) {
        exec '''
            $engineRoot = 'C:/unreal/binaries/5.8'
            $buildDirectory = Join-Path $engineRoot 'Engine/Build'
            $batchDirectory = Join-Path $buildDirectory 'BatchFiles'
            New-Item -ItemType Directory -Path $batchDirectory -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $batchDirectory 'Build.bat') -Value '@exit /b 91' -Encoding ascii
            Set-Content -LiteralPath (Join-Path $buildDirectory 'Build.version') -Value '{"MajorVersion":5,"MinorVersion":8,"PatchVersion":10}' -Encoding ascii
            Set-Content -LiteralPath (Join-Path $engineRoot 'branch-resolved.marker') -Value $env:RESOLVER_COMMIT -Encoding ascii

            $marker = [ordered]@{
                Version = '5.8'
                PatchVersion = '5.8.10'
                Source = $env:UNREAL_SOURCE
                Commit = $env:RESOLVER_COMMIT
                BuildProfile = 'win64-development-shipping-v1'
            }
            $marker | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $engineRoot '.docker-unreal.json') -Encoding utf8
        '''
    }
}

def assertCompileReusedResolvedCommit(commit) {
    withEnv(["RESOLVER_COMMIT=${commit}"]) {
        exec '''
            $sourceEntries = @(Get-ChildItem -LiteralPath 'C:/unreal/sources' -Force)
            if ($sourceEntries.Count -ne 0) {
                throw "Tag-mode compile touched source despite an exact branch-resolved installation: $($sourceEntries.FullName -join ', ')"
            }

            $sentinel = 'C:/unreal/binaries/5.8/branch-resolved.marker'
            if (-not (Test-Path -LiteralPath $sentinel)) {
                throw 'Tag-mode compile replaced the branch-resolved installation at the same commit'
            }
            if ((Get-Content -Raw -LiteralPath $sentinel).Trim() -ne $env:RESOLVER_COMMIT) {
                throw 'The reused installation contains the wrong commit ID'
            }
        '''
    }
}

def testVersionResolution() {
    def resolverSource = "${pwd()}\\.jenkins\\resolver-source"
    withEnv([
        "UNREAL_SOURCE=${resolverSource}",
        'UNREAL_VERSION=5.8'
    ]) {
        docker.image(candidateImage()).inside() {
            createResolverSource()

            def resolvedTag = execStdout '''
                Remove-Item Env:UNREAL_VERSION_MODE -ErrorAction SilentlyContinue
                Unreal --version
            '''
            assertValue(
                resolvedTag,
                '5.8.10-release',
                'default tag resolution uses SemVer precedence and excludes other prereleases'
            )

            def expectedCommit = execStdout 'git.exe -C $env:UNREAL_SOURCE rev-parse refs/heads/5.8'
            def resolvedCommit = ''
            withEnv(['UNREAL_VERSION_MODE=branch']) {
                resolvedCommit = execStdout 'Unreal --version'
            }
            assertValue(resolvedCommit, expectedCommit, 'branch resolution reports the branch commit')
            assertVersionDidNotMaterializeEngine()

            def help = execStdout 'Unreal --help'
            assertValue(help.contains('Unreal --compile'), true, 'Unreal help lists the compile command')

            seedPublishedResolverEngine(expectedCommit)
            exec '''
                Remove-Item Env:UNREAL_VERSION_MODE -ErrorAction SilentlyContinue
                Unreal --compile
            '''
            assertCompileReusedResolvedCommit(expectedCommit)
        }
    }
}

def testImage(unrealVersion, expectedTag) {
    docker.image(candidateImage()).inside('-v unreal-binaries:C:/unreal/binaries -v unreal-cache:C:/unreal/cache -v unreal-sources:C:/unreal/sources') {
        def project = '%WORKSPACE%\\test-files\\EmptyGame\\EmptyGame.uproject'
        def archive = "%WORKSPACE%\\.jenkins\\artifacts\\${unrealVersion}"

        exec 'Unreal --help'
        def resolvedTag = execStdout 'Unreal --version'
        assertValue(resolvedTag, expectedTag, "Unreal v${unrealVersion} resolved tag")
        exec 'Unreal --compile'

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
