$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$AppName = "JBZUniversalTester"
$Version = (Get-Content ".\VERSION.txt" -ErrorAction SilentlyContinue | Select-Object -First 1).Trim()
if (-not $Version) { $Version = "V10-Windows" }
$Package = "${AppName}_${Version}_windows_x64"

if (-not (Test-Path ".venv\Scripts\python.exe")) {
    Write-Host "Chua co .venv - dang chay install_windows.ps1" -ForegroundColor Yellow
    & "$PSScriptRoot\install_windows.ps1"
}

& ".\.venv\Scripts\python.exe" -m pip install "pyinstaller>=6,<7"
Remove-Item build, dist, release -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$AppName.spec" -Force -ErrorAction SilentlyContinue

& ".\.venv\Scripts\python.exe" -m PyInstaller `
    --noconfirm `
    --clean `
    --onedir `
    --windowed `
    --name $AppName `
    --paths . `
    --collect-submodules jbz_tester `
    --collect-submodules jbz_model_loader `
    --collect-submodules jbz_uart `
    --hidden-import serial.tools.list_ports `
    app.py

New-Item -ItemType Directory -Force -Path ".\dist\$AppName\docs" | Out-Null
New-Item -ItemType Directory -Force -Path ".\dist\$AppName\profiles" | Out-Null
Copy-Item ".\docs\*" ".\dist\$AppName\docs" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item ".\profiles\*" ".\dist\$AppName\profiles" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item ".\README.md", ".\README_VI.md", ".\README_WINDOWS_VI.md", ".\VERSION.txt" ".\dist\$AppName\" -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force -Path ".\release" | Out-Null
$zip = ".\release\$Package.zip"
Compress-Archive -Path ".\dist\$AppName" -DestinationPath $zip -Force
$hash = Get-FileHash $zip -Algorithm SHA256
$hash.Hash | Set-Content "$zip.sha256"

Write-Host ""
Write-Host "BUILD WINDOWS THANH CONG" -ForegroundColor Green
Write-Host (Resolve-Path $zip)
Write-Host "SHA256: $($hash.Hash)"
