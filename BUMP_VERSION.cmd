@echo off
setlocal
chcp 65001 >nul
title JBZUniversalTester - Bump Version

set "ROOT=%~dp0"
set "SCRIPT=%ROOT%Scripts\Bump-Version.ps1"

if not exist "%SCRIPT%" (
    echo [ERROR] File not found: %SCRIPT%
    pause
    exit /b 1
)

if "%~1"=="" (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
) else (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" -Version "%~1"
)

set "EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%EXIT_CODE%"=="0" (
    echo VERSION UPDATE FAILED.
    pause
    exit /b %EXIT_CODE%
)

echo Version updated successfully.
pause
exit /b 0
