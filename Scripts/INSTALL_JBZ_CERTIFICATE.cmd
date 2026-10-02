@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-JBZCertificate.ps1"
exit /b %ERRORLEVEL%
