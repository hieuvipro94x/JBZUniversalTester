@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title JBZUniversalTester V14.0.0 DUAL BOARD - Verify + Build

echo [1/3] Static validation V14.0.0
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Scripts\Validate-V14.0.0.ps1"
if errorlevel 1 goto :fail

echo [2/3] Restore + Build Release x86
dotnet restore "JBZUniversalTester.csproj"
if errorlevel 1 goto :fail
dotnet build "JBZUniversalTester.csproj" -c Release -r win-x86 --no-restore
if errorlevel 1 goto :fail

echo [3/3] Publish one-file
call BUILD_ONE_FILE.cmd
if errorlevel 1 goto :fail

echo.
echo ============================================================
echo V14.0.0 VERIFY + BUILD + PUBLISH SUCCESS
echo ============================================================
pause
exit /b 0

:fail
echo.
echo ============================================================
echo V14.0.0 VERIFY / BUILD FAILED
 echo Check the error above before production use.
echo ============================================================
pause
exit /b 1
