@echo off
setlocal
chcp 65001 >nul
title JBZUniversalTester - One Click Publish

set "ROOT=%~dp0"
set "PS_SCRIPT=%ROOT%Scripts\Publish-OneFile.ps1"

if not exist "%PS_SCRIPT%" (
    echo [ERROR] File not found:
    echo %PS_SCRIPT%
    echo.
    pause
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass ^
  -File "%PS_SCRIPT%" ^
  -Runtime "win-x86" ^
  -Configuration "Release" ^
  -OutputFolder "PublishSingle"

set "EXIT_CODE=%ERRORLEVEL%"
echo.

if not "%EXIT_CODE%"=="0" (
    echo ============================================================
    echo PUBLISH FAILED. Check publish.log for details.
    echo ============================================================
    pause
    exit /b %EXIT_CODE%
)

echo ============================================================
echo PUBLISH SUCCESS.
echo ============================================================
pause
exit /b 0
