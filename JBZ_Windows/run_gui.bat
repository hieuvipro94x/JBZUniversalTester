@echo off
setlocal
cd /d "%~dp0"

if exist ".venv\Scripts\pythonw.exe" (
    start "" ".venv\Scripts\pythonw.exe" app.py
    exit /b 0
)

where py >nul 2>nul
if %errorlevel%==0 (
    start "" pyw -3 app.py
    exit /b 0
)

where pythonw >nul 2>nul
if %errorlevel%==0 (
    start "" pythonw app.py
    exit /b 0
)

echo Khong tim thay Python. Hay chay install_windows.ps1 truoc.
pause
exit /b 1
