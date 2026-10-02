@echo off
setlocal
title JBZ Code Signing Certificate Installer
echo ============================================================
echo JBZ CODE SIGNING CERTIFICATE INSTALLER
echo ============================================================
echo.

if not exist "%~dp0Install-JBZCertificate.ps1" (
    echo ERROR: Missing installer script:
    echo "%~dp0Install-JBZCertificate.ps1"
    set "JBZ_INSTALL_EXIT=1"
    goto FINISH
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-JBZCertificate.ps1"
set "JBZ_INSTALL_EXIT=%ERRORLEVEL%"

:FINISH
echo.
echo ============================================================
if "%JBZ_INSTALL_EXIT%"=="0" (
    echo INSTALLATION COMPLETED SUCCESSFULLY.
) else (
    echo INSTALLATION FAILED. Exit code: %JBZ_INSTALL_EXIT%
    echo Review the error above.
    echo For permission errors, right-click this file and select:
    echo Run as administrator.
)
echo This window stays open until you press a key.
echo ============================================================
pause
exit /b %JBZ_INSTALL_EXIT%
