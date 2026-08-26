def assertValue(actual, expected, description) {
    if (actual != expected) {
        error "${description}: expected '${expected}', got '${actual}'"
    }
}

def candidateImage() {
    return "$DOCKER_NAMESPACE/$DOCKER_IMAGE:$UNREAL_VERSION"
}

def testImage() {
    docker.image(candidateImage()).inside() {
        exec 'Build -Help'
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
def unrealVersions = ['5.0']

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
                                "UNREAL_VERSION=${unrealVersion}"
                            ]) {
                                withEnvFile {
                                    echo "Testing ${candidateImage()} with Unreal v${unrealVersion} on ${host}"
                                    testImage()
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
