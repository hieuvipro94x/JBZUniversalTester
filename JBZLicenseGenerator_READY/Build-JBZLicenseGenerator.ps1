$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "JBZLicenseGenerator\JBZLicenseGenerator.csproj"

dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) {
    throw "Build thất bại."
}

Write-Host ""
Write-Host "BUILD THÀNH CÔNG." -ForegroundColor Green
Write-Host "EXE portable nằm trong JBZLicenseGenerator\bin\Release\net8.0-windows\win-x64\publish\"
Write-Host "Chỉ cần mang JBZLicenseGenerator.exe. Không gửi EXE này cho máy sản xuất hoặc khách hàng." -ForegroundColor Yellow
