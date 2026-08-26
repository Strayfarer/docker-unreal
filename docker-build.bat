@echo off
setlocal
cd /d "%~dp0"

set "BUILD_EXIT_CODE=1"
set "DOCKER_CONTEXT=%~1"

if not defined DOCKER_CONTEXT (
    echo Missing Docker context. Use docker-build-windows.bat.
    goto build_done
)

powershell.exe -ExecutionPolicy Bypass -NoLogo -NoProfile -File "%~dp0windows\build-images.ps1" -DockerContext "%DOCKER_CONTEXT%"
set "BUILD_EXIT_CODE=%ERRORLEVEL%"

:build_done
pause
endlocal & exit /b %BUILD_EXIT_CODE%
