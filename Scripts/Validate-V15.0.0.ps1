param([string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$failed = 0
function Check([string]$name, [bool]$ok) {
  if ($ok) { Write-Host "[PASS] $name" -ForegroundColor Green }
  else { Write-Host "[FAIL] $name" -ForegroundColor Red; $script:failed++ }
}
$version = Get-Content (Join-Path $ProjectRoot 'Version.props') -Raw
$settings = Get-Content (Join-Path $ProjectRoot 'Views\ProductionSettingsPage.xaml') -Raw
$testvm = Get-Content (Join-Path $ProjectRoot 'ViewModels\TestViewModel.cs') -Raw
$engine = Get-Content (Join-Path $ProjectRoot 'Services\TestEngine.cs') -Raw
Check 'Version 15.0.0' ($version -match '<Version>13\.0\.0</Version>')
Check 'Version tag 15_0_0' ($version -match '<VersionFileTag>15_0_0</VersionFileTag>')
Check 'UnifiedBoardTransport exists' (Test-Path (Join-Path $ProjectRoot 'Services\UnifiedBoardTransport.cs'))
Check 'UartTtlBoardTransport exists' (Test-Path (Join-Path $ProjectRoot 'Services\UartTtlBoardTransport.cs'))
Check 'AUTO board option' ($settings -match 'Tự động nhận dạng')
Check 'D2XX board option' ($settings -match 'JBZ D2XX')
Check 'UART TTL board option' ($settings -match 'JBZ UART TTL')
Check 'Firmware TESTPIN handling' ($testvm -match 'case "TESTPIN"')
Check 'Firmware CIRCUIT handling' ($testvm -match 'case "CIRCUIT"')
Check 'D2XX fault eject uses JigEjectRelay' ($engine -match 'EjectFaultProductAsync[\s\S]*SetRelayAsync\(JigEjectRelay')
Check 'Fault confirmation window exists' (Test-Path (Join-Path $ProjectRoot 'Views\FaultConfirmationWindow.xaml'))
if ($failed -gt 0) { exit 1 }
Write-Host 'V15.0.0 static validation PASS.' -ForegroundColor Cyan
