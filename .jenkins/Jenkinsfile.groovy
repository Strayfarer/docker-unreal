def assertValue(actual, expected, description) {
    if (actual != expected) {
        error "${description}: expected '${expected}', got '${actual}'"
    }
}

def candidateImage() {
    return "$DOCKER_NAMESPACE/$DOCKER_IMAGE"
}

def testImage(unrealVersion) {
    docker.image(candidateImage()).inside('-v unreal-binaries:C:/unreal/binaries -v unreal-cache:C:/unreal/cache -v unreal-sources:C:/unreal/sources') {
        def project = '%WORKSPACE%\\test-files\\EmptyGame\\EmptyGame.uproject'
        def archive = "%WORKSPACE%\\.jenkins\\artifacts\\${unrealVersion}"

        exec 'Unreal --help'
        exec 'Unreal --version'

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
def unrealVersions = ['5.0', '5.6', '5.7']
def unrealSource = 'https://github.com/EpicGames/UnrealEngine'
def unrealCredentials = 'Faulo-GitHub'

stage('Integration Tests') {
    for (def host in hosts) {
        stage("Host: ${host}") {
            node(host) {
                deleteDir()
                checkout scm

                for (def unrealVersion in unrealVersions) {
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
                                "UNREAL_SOURCE=${unrealSource}"
                            ]) {
                                withEnvFile {
                                 withCredentials([usernamePassword(credentialsId: unrealCredentials, usernameVariable: 'UNREAL_CREDENTIALS_USR', passwordVariable: 'UNREAL_CREDENTIALS_PSW')]) {
                                    echo "Testing ${candidateImage()} with Unreal v${unrealVersion} on ${host}"
                                    testImage(unrealVersion)
                            }}}
                        }
                    }
                }
            }
        }
    }
}
