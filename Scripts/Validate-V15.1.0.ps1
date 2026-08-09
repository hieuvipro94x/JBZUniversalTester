param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$failed = 0
function Check([string]$name, [bool]$ok) {
  if ($ok) { Write-Host "[PASS] $name" -ForegroundColor Green }
  else { Write-Host "[FAIL] $name" -ForegroundColor Red; $script:failed++ }
}
$version = Get-Content (Join-Path $ProjectRoot 'Version.props') -Raw
$testvm = Get-Content (Join-Path $ProjectRoot 'ViewModels\TestViewModel.cs') -Raw
$cmd = Get-Content (Join-Path $ProjectRoot 'Core\AsyncRelayCommand.cs') -Raw
$app = Get-Content (Join-Path $ProjectRoot 'App.xaml.cs') -Raw
Check 'Version 15.1.0' ($version -match '<Version>15\.1\.0</Version>')
Check 'Version tag 15_1_0' ($version -match '<VersionFileTag>15_1_0</VersionFileTag>')
Check 'Manual board guard' ($testvm -match 'EnsureManualBoardReady')
Check 'Offline message' ($testvm -match 'CHƯA KẾT NỐI VỚI BO MẠCH TEST')
Check 'UART relay guard' ($testvm -match 'backend UART TTL không hỗ trợ relay D2XX')
Check 'Async command exception guard' ($cmd -match 'catch \(Exception ex\)')
Check 'Dispatcher guard handled' ($app -match 'e\.Handled = true')
Check 'Unobserved task observed' ($app -match 'e\.SetObserved\(\)')
if ($failed -gt 0) { exit 1 }
Write-Host 'V15.1.0 static validation PASS.' -ForegroundColor Cyan
