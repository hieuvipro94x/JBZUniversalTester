param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([pscustomobject]@{ Name=$Name; Passed=$Passed; Detail=$Detail })
}

$xamlPath = Join-Path $ProjectRoot 'Views\TestWindow.xaml'
$vmPath = Join-Path $ProjectRoot 'ViewModels\TestViewModel.cs'
$versionPath = Join-Path $ProjectRoot 'Version.props'
$xaml = Get-Content $xamlPath -Raw -Encoding UTF8
$vm = Get-Content $vmPath -Raw -Encoding UTF8
$version = Get-Content $versionPath -Raw -Encoding UTF8

try { [xml](Get-Content $xamlPath -Raw -Encoding UTF8) | Out-Null; Add-Check 'XAML XML parse' $true 'TestWindow.xaml hợp lệ về XML.' }
catch { Add-Check 'XAML XML parse' $false $_.Exception.Message }

Add-Check 'Version 12.10.1' ($version -match '<Version>12\.10\.1</Version>') 'Version.props = 12.10.1.'
Add-Check 'Version tag 12_10_1' ($version -match '<VersionFileTag>12_10_1</VersionFileTag>') 'EXE tag đồng bộ.'
Add-Check 'Backup TestWindow' (Test-Path (Join-Path $ProjectRoot 'Backup\TestWindow_V12_9_5_before_faultgrid_restore.xaml')) 'Có backup XAML trước restore.'
Add-Check 'Status strip 68px' ($xaml -match '<Border Grid.Row="2"[\s\S]*?Height="68"') 'Master/Production status gọn.'
Add-Check 'Không còn FontSize 44' (-not ($xaml -match 'FontSize="44"')) 'Không còn title Master khổng lồ.'
Add-Check 'Main TabControl row star' ($xaml -match '<RowDefinition Height="\*" MinHeight="300"/>') 'Fault area nhận chiều cao còn lại.'
Add-Check 'TabControl stretch H' ($xaml -match '<TabControl Grid.Row="3"[\s\S]*?HorizontalAlignment="Stretch"') 'TabControl stretch ngang.'
Add-Check 'TabControl stretch V' ($xaml -match '<TabControl Grid.Row="3"[\s\S]*?VerticalAlignment="Stretch"') 'TabControl stretch dọc.'
Add-Check 'Fault Tab stretch' ($xaml -match '<TabItem Header="DANH SÁCH LỖI / MẠNG I/O"[\s\S]*?VerticalContentAlignment="Stretch"') 'Tab lỗi stretch.'
Add-Check 'FaultGrid stretch' ($xaml -match '<DataGrid x:Name="FaultGrid"[\s\S]*?VerticalAlignment="Stretch"') 'FaultGrid chiếm toàn vùng.'
Add-Check 'FaultGrid không collapse MasterBad' (-not ($xaml -match 'x:Name="FaultGrid"[\s\S]{0,1000}IsMasterBadPhase[\s\S]{0,250}Visibility" Value="Collapsed"')) 'MasterBad vẫn nhìn FaultGrid.'
Add-Check 'Không còn MasterFaults ItemsControl riêng' (-not ($xaml -match 'ItemsSource="\{Binding MasterFaults\}"')) 'MasterBad dùng cùng FaultGrid.'
Add-Check 'Master unique HashSet' ($vm -match 'HashSet<MasterFaultKey> _masterDetectedFaultKeys') 'Fault lặp frame không tăng count.'
Add-Check 'Master unique detail snapshot' ($vm -match 'Dictionary<MasterFaultKey, FaultDetail> _masterDetectedFaultDetails') 'Snapshot DataGrid theo key unique.'
Add-Check 'FaultGrid master builder' ($vm -match 'BuildMasterFaultGridRows\(\)') 'Có builder chung cho master rows.'
Add-Check 'Refresh master unique rows' ($vm -match '!MasterApproved && IsMasterBadPhase[\s\S]*?SynchronizeFaultRows\(BuildMasterFaultGridRows\(\)\)') 'MasterBad hiển thị snapshot unique.'
Add-Check 'Live engine source retained' ($vm -match 'liveFaultRows = !MasterApproved && IsMasterBadPhase[\s\S]*?_engine\.BuildRows\(\)') 'Open mới vẫn phát hiện từ engine live.'
Add-Check 'Unique detail add after HashSet' ($vm -match '_masterDetectedFaultDetails\[key\] = fault;') 'Mỗi key lưu một detail.'
Add-Check 'RowKey includes expected actual' ($vm -match 'row\.ExpectedSourceIo.*row\.ExpectedTargetIo.*row\.ActualSourceIo.*row\.ActualTargetIo') 'Không merge sai hai WrongWiring khác nhau.'
Add-Check 'Readable row min 30' ($vm -match 'Math\.Clamp\(_productionSettings\.ItemHeight, 30, 44\)') 'RowHeight tối thiểu 30px.'
Add-Check 'Grid font 15' ($xaml -match '<Setter Property="FontSize" Value="15"/>') 'Dòng grid đủ lớn.'
Add-Check 'Header height 40' ($xaml -match '<Setter Property="ColumnHeaderHeight" Value="40"/>') 'Header rõ.'
Add-Check 'PartNumber OneWay retained' ($xaml -match 'Text="\{Binding PartNumber, Mode=OneWay\}"') 'Không rollback binding fix.'
Add-Check 'ProductName OneWay retained' ($xaml -match 'Text="\{Binding ProductName, Mode=OneWay\}"') 'Không rollback binding fix.'
Add-Check 'VehicleType OneWay retained' ($xaml -match 'Text="\{Binding VehicleType, Mode=OneWay\}"') 'Không rollback binding fix.'
Add-Check 'CustomerCode OneWay retained' ($xaml -match 'Text="\{Binding CustomerCode, Mode=OneWay\}"') 'Không rollback binding fix.'
Add-Check 'Lot OneWay retained' ($xaml -match 'Text="\{Binding Lot, Mode=OneWay\}"') 'Không rollback binding fix.'
Add-Check 'Probe contacts retained' ($xaml -match 'ItemsSource="\{Binding ProbeContacts\}"') 'Probe UI còn nguyên.'
Add-Check 'Cards retained' ($xaml -match 'ItemsSource="\{Binding Cards\}"') 'Card active/inactive còn nguyên.'
Add-Check 'Bottom toolbar overlay retained' ($xaml -match 'x:Name="BottomToolbarOverlay"') 'Toolbar auto-hide còn nguyên.'
Add-Check 'Toolbar hotzone retained' ($xaml -match 'x:Name="BottomToolbarHotZone"[\s\S]*?Height="24"') 'Hot zone 24px còn nguyên.'
Add-Check 'No manual Master commands' (-not ($vm -match 'StartGoodMasterCommand|StartBadMasterCommand|ConfirmMasterSamplesCommand')) 'Không rollback Master Auto.'
Add-Check 'Master does not record production directly' (-not ($vm -match 'MASTER BAD[\s\S]{0,1200}RecordCompletedProduct')) 'MasterBad không tăng LOT/FAIL.'
Add-Check 'Default tab set to 0' ($vm -match 'SelectedOperationTabIndex = 0;') 'Tab lỗi được ưu tiên khi vận hành.'

$passed = ($checks | Where-Object Passed).Count
$failed = ($checks | Where-Object { -not $_.Passed }).Count
$checks | ForEach-Object { Write-Host ("[{0}] {1} - {2}" -f ($(if ($_.Passed) {'PASS'} else {'FAIL'})), $_.Name, $_.Detail) }
Write-Host ""
Write-Host "TOTAL: $passed PASS / $failed FAIL"
if ($failed -gt 0) { exit 1 }
exit 0
