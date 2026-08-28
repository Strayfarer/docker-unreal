# TODO

## Task
- add docker-unreal.sln at root, with c# projects going to common/*/ same as ../docker-godot does it
- bake Build.exe into the docker image as a shim for Unreal Engine's Build.bat (UE_5.0\Engine\Build\BatchFiles) but with an additional first-call behavior of:
- compiling Unreal Engine from source!
- source is downloaded from UNREAL_SOURCE (default 'https://github.com/EpicGames/UnrealEngine')
- credentials UNREAL_CREDENTIALS_USR/UNREAL_CREDENTIALS_PSW ('Faulo-GitHub' in jenkins) have access to that repo
- source is cloned into `C:/unreal/sources/EpicGames.UnrealEngine`
- then branch UNREAL_VERSION (for example, '5.7') is checked out and compiled
- build lands in 'C:/unreal/binaries/5.7'
- commit hash of the compiled build is stored, if the branch ever moves, a recompile is issued by the next Build.exe invocation
- binaries automatically only ever contain the latest (at compile time) patch version of that minor release.
- the installer then calls `C:/unreal/binaries/5.7/Engine/Build/BatchFiles/Build.bat` such that that script can find its sibling scripts and everything works
- special care has to be taken to install all dependencies of each unreal engine version
- any dependencies that are shared between unreal 4 through 6 should be installed in the Dockerfile directly
- supported floor of the installer is Unreal Engine `4.0`, unless 4.x is stupid hard, then we start with `5.0`.
- you are free to make commits to keep your own history clean, but do not push them yet
- leave this TODO as-is
- you may ssh do `dende` and use the `dende` docker context

## Help

- ../docker-godot for a similar auto-install setup using environment variables and Godot
- ../docker-compose-unity for a different auto-install setup with more tooling in the base image
- always include the unreal-binaries and unreal-sources volumes, those things get huge
- the current Dockerfile is a previous, aborted attempt of compiling Unreal into the Dockerfile. these TODO instructions supersede anything in there.
- the real goal is getting integration tests green without changing them.
- you may also use the local `windows` docker daemon, but remember CI (jenkins) only runs on Dende
