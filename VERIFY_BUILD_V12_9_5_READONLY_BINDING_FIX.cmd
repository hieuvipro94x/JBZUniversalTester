@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"

title JBZUniversalTester V12.9.5 - ReadOnly Binding Fix Verify
set "LOG=verify_readonly_binding_fix_V12.9.5.log"

echo ============================================================ > "%LOG%"
echo JBZUniversalTester V12.9.5 READONLY BINDING FIX VERIFY >> "%LOG%"
echo Date: %DATE% %TIME% >> "%LOG%"
echo ============================================================ >> "%LOG%"

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Khong tim thay Windows PowerShell. >> "%LOG%"
    goto :fail
)

echo [1/5] Audit WPF read-only bindings
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Scripts\Audit-ReadOnlyBindings.ps1" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Khong tim thay .NET SDK 8. >> "%LOG%"
    echo [ERROR] Khong tim thay .NET SDK 8.
    echo Cai .NET 8 SDK tren may build, sau do chay lai file nay.
    pause
    exit /b 1
)

echo [2/5] dotnet --version
dotnet --version >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [3/5] Restore
dotnet restore "JBZUniversalTester.csproj" -r win-x86 --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [4/5] Build Release win-x86
dotnet build "JBZUniversalTester.csproj" -c Release -r win-x86 --no-restore --nologo >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo [5/5] Publish one-file
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Scripts\Publish-OneFile.ps1" -Runtime "win-x86" -Configuration "Release" -OutputFolder "PublishSingle" >> "%LOG%" 2>&1
if errorlevel 1 goto :fail

echo.
echo ============================================================
echo READONLY BINDING AUDIT + BUILD + PUBLISH SUCCESS
echo Log: %CD%\%LOG%
echo ============================================================
pause
exit /b 0

:fail
echo.
echo ============================================================
echo VERIFY FAILED - xem log: %CD%\%LOG%
echo ============================================================
pause
exit /b 1
