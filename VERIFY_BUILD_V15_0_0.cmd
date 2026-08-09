@echo off
setlocal
cd /d "%~dp0"
echo ============================================================
echo JBZUniversalTester V15.0.0 - CLEAN BUILD / VERIFY
echo ============================================================
dotnet --info || goto :fail
dotnet clean JBZUniversalTester.csproj -c Release || goto :fail
dotnet restore JBZUniversalTester.csproj || goto :fail
dotnet build JBZUniversalTester.csproj -c Release --no-restore || goto :fail
if exist Scripts\Validate-V15.0.0.ps1 powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\Validate-V15.0.0.ps1
if errorlevel 1 goto :fail
echo.
echo BUILD V15.0.0 PASS
exit /b 0
:fail
echo.
echo BUILD V15.0.0 FAIL
exit /b 1
