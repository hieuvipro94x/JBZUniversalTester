@echo off
setlocal
echo ===============================================
echo JBZUniversalTester V15.2.0 VERIFY BUILD
echo ===============================================
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\Validate-V15.2.0.ps1
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\Audit-ReadOnlyBindings.ps1
if errorlevel 1 exit /b 1
dotnet clean JBZUniversalTester.slnx -c Release
if errorlevel 1 exit /b 1
dotnet restore JBZUniversalTester.slnx
if errorlevel 1 exit /b 1
dotnet build JBZUniversalTester.slnx -c Release --no-restore
if errorlevel 1 exit /b 1
dotnet run --project Tests\JBZUniversalTester.SelfTests.csproj -c Release --no-build
if errorlevel 1 exit /b 1
echo.
echo BUILD PASS
echo Hay test relay tren bo that truoc production.
