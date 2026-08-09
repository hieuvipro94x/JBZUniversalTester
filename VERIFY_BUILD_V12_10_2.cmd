@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

title JBZUniversalTester V12.10.2 - Master Fault Fix Verify
set "LOG=verify_build_V12.10.2.log"

echo ============================================================ > "%LOG%"
echo JBZUniversalTester V12.10.2 MASTER FAULT FIX VERIFY >> "%LOG%"
echo Date: %DATE% %TIME% >> "%LOG%"
echo ============================================================ >> "%LOG%"

where powershell.exe >nul 2>nul
if errorlevel 1 goto :no_powershell

echo [1/6] Static validation V12.10.2
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Scripts\Validate-V12.10.2.ps1" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [2/6] Audit WPF read-only bindings
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Scripts\Audit-ReadOnlyBindings.ps1" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

where dotnet >nul 2>nul
if errorlevel 1 goto :no_dotnet

echo [3/6] dotnet --version
dotnet --version >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [4/6] Restore
dotnet restore "JBZUniversalTester.csproj" -r win-x86 --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [5/6] Build Release win-x86
dotnet build "JBZUniversalTester.csproj" -c Release -r win-x86 --no-restore --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [6/6] Publish one-file
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Scripts\Publish-OneFile.ps1" -Runtime "win-x86" -Configuration "Release" -OutputFolder "PublishSingle" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo.
echo ============================================================
echo VERIFY + BUILD + PUBLISH V12.10.2 SUCCESS
echo Log: %CD%\%LOG%
echo ============================================================
pause
exit /b 0

:no_powershell
echo [ERROR] Khong tim thay Windows PowerShell. >> "%LOG%"
echo Khong tim thay Windows PowerShell.
pause
exit /b 1

:no_dotnet
echo [ERROR] Khong tim thay .NET 8 SDK. >> "%LOG%"
echo Khong tim thay .NET 8 SDK. Cai .NET 8 SDK x86/x64 tren may build roi chay lai.
pause
exit /b 1

:fail
echo.
echo ============================================================
echo VERIFY FAILED - xem log: %CD%\%LOG%
echo ============================================================
pause
exit /b 1
