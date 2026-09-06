param(
    [string]$Solution = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "JBZLicenseGenerator\JBZLicenseGenerator.csproj"

if (-not (Test-Path $project)) {
    throw "Không tìm thấy project: $project"
}

if ([string]::IsNullOrWhiteSpace($Solution)) {
    $solutions = Get-ChildItem -Path $root -Filter *.sln -File

    if ($solutions.Count -eq 0) {
        Write-Host ""
        Write-Host "CHƯA CÓ FILE .sln TRONG THƯ MỤC NÀY." -ForegroundColor Yellow
        Write-Host "Hãy giải nén package vào thư mục gốc solution JBZUniversalTester rồi chạy lại."
        Write-Host ""
        Write-Host "Hoặc truyền đường dẫn:"
        Write-Host ".\Add-JBZLicenseGenerator.ps1 -Solution .\JBZUniversalTester.sln"
        exit 2
    }

    if ($solutions.Count -gt 1) {
        Write-Host "Có nhiều .sln. Hãy chỉ rõ -Solution." -ForegroundColor Yellow
        $solutions | ForEach-Object { Write-Host $_.FullName }
        exit 3
    }

    $Solution = $solutions[0].FullName
}
elseif (-not [System.IO.Path]::IsPathRooted($Solution)) {
    $Solution = Join-Path $root $Solution
}

Write-Host "Solution: $Solution"
Write-Host "Project : $project"

dotnet sln $Solution add $project
if ($LASTEXITCODE -ne 0) {
    throw "dotnet sln add thất bại."
}

Write-Host ""
Write-Host "ĐÃ THÊM JBZLicenseGenerator VÀO SOLUTION." -ForegroundColor Green
Write-Host "Tool KHÔNG có ProjectReference sang JBZUniversalTester và không ảnh hưởng app production."
