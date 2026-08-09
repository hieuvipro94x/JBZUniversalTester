@echo off
setlocal
cd /d "%~dp0"
echo ==================================================
echo JBZUniversalTester V15.1.0 - CLEAN BUILD / VERIFY
echo ==================================================
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\Validate-V15.1.0.ps1
if errorlevel 1 goto fail
dotnet restore JBZUniversalTester.csproj
if errorlevel 1 goto fail
dotnet build JBZUniversalTester.csproj -c Release --no-restore
if errorlevel 1 goto fail
echo BUILD V15.1.0 PASS
goto end
:fail
echo BUILD V15.1.0 FAIL
exit /b 1
:end
pause
