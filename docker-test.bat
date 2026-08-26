@echo off
setlocal
cd /d "%~dp0"

set "TEST_EXIT_CODE=1"
set "DOCKER_CONTEXT=%~1"

if not defined DOCKER_CONTEXT (
    echo Missing Docker context. Use docker-test-windows.bat.
    goto test_done
)

powershell.exe -ExecutionPolicy Bypass -NoLogo -NoProfile -File "%~dp0windows\test-images.ps1" -DockerContext "%DOCKER_CONTEXT%"
set "TEST_EXIT_CODE=%ERRORLEVEL%"

:test_done
pause
endlocal & exit /b %TEST_EXIT_CODE%
