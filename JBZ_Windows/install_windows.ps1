$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$PythonExe = $null
$PythonPrefixArgs = @()
if (Get-Command py -ErrorAction SilentlyContinue) {
    $PythonExe = "py"
    $PythonPrefixArgs = @("-3")
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    $PythonExe = "python"
} else {
    throw "Khong tim thay Python. Cai Python 3.10+ tu python.org va chon 'Add Python to PATH'."
}

Write-Host "=== JBZ Universal Tester - cai dat Windows ===" -ForegroundColor Cyan
& $PythonExe @PythonPrefixArgs -c "import sys; assert sys.version_info >= (3,10), sys.version"

if (Test-Path ".venv") {
    Remove-Item ".venv" -Recurse -Force
}
& $PythonExe @PythonPrefixArgs -m venv .venv
& ".\.venv\Scripts\python.exe" -m pip install --upgrade pip
& ".\.venv\Scripts\python.exe" -m pip install -r requirements.txt
& ".\.venv\Scripts\python.exe" -c "import serial, tkinter; print('Python/Tkinter/pyserial OK')"

$models = Join-Path $env:USERPROFILE "Models"
$setups = Join-Path $env:USERPROFILE "Setups"
New-Item -ItemType Directory -Force -Path $models | Out-Null
New-Item -ItemType Directory -Force -Path $setups | Out-Null

Write-Host ""
Write-Host "Cai dat hoan tat." -ForegroundColor Green
Write-Host "Models : $models"
Write-Host "Setups : $setups"
Write-Host "Chay ung dung: run_gui.bat"
Write-Host "Kiem tra COM: .\.venv\Scripts\python.exe .\tools\check_uart_windows.py"
