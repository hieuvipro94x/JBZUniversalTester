param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
$notes = New-Object System.Collections.Generic.List[string]

function Require-Contains([string]$Path, [string]$Pattern, [string]$Message) {
    $text = Get-Content -Raw -LiteralPath $Path
    if ($text -notmatch $Pattern) { $failures.Add($Message) }
}

$testXaml = Join-Path $ProjectRoot 'Views/TestWindow.xaml'
$testVm   = Join-Path $ProjectRoot 'ViewModels/TestViewModel.cs'
$settingsXaml = Join-Path $ProjectRoot 'Views/ProductionSettingsPage.xaml'
$settingsModels = Join-Path $ProjectRoot 'Models'
$settingsVm = Join-Path $ProjectRoot 'ViewModels/ProductionSettingsViewModel.cs'

# 1) Header information in TestView is display-only and MUST be OneWay.
$displayOnly = @('PartNumber','ProductName','VehicleType','CustomerCode')
$testText = Get-Content -Raw -LiteralPath $testXaml
foreach ($name in $displayOnly) {
    $pattern = 'Text="\{Binding\s+' + [regex]::Escape($name) + ',\s*Mode=OneWay\}"'
    if ($testText -notmatch $pattern) {
        $failures.Add("TestWindow: $name must use explicit Mode=OneWay on TextBox.Text")
    }
}
if ($testText -notmatch 'Text="\{Binding\s+ProbeCycleText,\s*Mode=OneWay\}"') {
    $failures.Add('TestWindow: PartCnt display must bind ProbeCycleText with explicit Mode=OneWay')
}

# 2) No implicit TextBox.Text bindings are allowed in TestWindow.
$textBoxes = [regex]::Matches($testText, '<TextBox\b[^>]*>', 'Singleline')
foreach ($m in $textBoxes) {
    $tag = $m.Value
    if ($tag -match 'Text="\{Binding\s+([^,}\s]+)([^}]*)\}"') {
        $path = $Matches[1]
        $args = $Matches[2]
        if ($args -notmatch 'Mode=') {
            $failures.Add("TestWindow: TextBox binding '$path' has no explicit Binding Mode")
        }
    }
}

# 3) No OneWayToSource anywhere in XAML.
Get-ChildItem -Path $ProjectRoot -Recurse -Filter *.xaml | ForEach-Object {
    $content = Get-Content -Raw -LiteralPath $_.FullName
    if ($content -match 'OneWayToSource') {
        $failures.Add("OneWayToSource found in $($_.FullName)")
    }
}

# 4) Ensure the known display properties remain read-only/computed (no fake setter).
$vmText = Get-Content -Raw -LiteralPath $testVm
foreach ($name in @('PartNumber','ProductName','VehicleType','CustomerCode')) {
    $pattern = 'public\s+string\s+' + [regex]::Escape($name) + '\s*=>'
    if ($vmText -notmatch $pattern) {
        $failures.Add("TestViewModel: expected computed read-only property '$name'")
    }
}
if ($vmText -notmatch 'public\s+string\s+Lot\s*\{[^}]*private\s+set\s*=>') {
    $failures.Add('TestViewModel: Lot should remain externally read-only (private setter)')
}

# 5) Model changes must notify the OneWay display bindings.
foreach ($name in @('PartNumber','ProductName','VehicleType','CustomerCode')) {
    $pattern = 'Raise\(nameof\(' + [regex]::Escape($name) + '\)\)'
    if ($vmText -notmatch $pattern) {
        $failures.Add("TestViewModel: missing PropertyChanged notification for '$name'")
    }
}

# 6) TestWindow may retain TwoWay only for the editable operation tab index.
$twoWayMatches = [regex]::Matches($testText, '\{Binding\s+([^,}\s]+)[^}]*Mode=TwoWay[^}]*\}')
foreach ($m in $twoWayMatches) {
    $path = $m.Groups[1].Value
    if ($path -ne 'SelectedOperationTabIndex') {
        $failures.Add("TestWindow: unexpected TwoWay binding '$path'")
    }
}
Require-Contains $testVm 'public\s+int\s+SelectedOperationTabIndex\s*\{[^}]*set\s*=>' 'TestViewModel: SelectedOperationTabIndex must remain writable for TwoWay binding'

# 7) Production Settings is the editable surface. All explicit Settings.* TwoWay paths must have public setters.
$settingsText = Get-Content -Raw -LiteralPath $settingsXaml
$modelText = (Get-ChildItem -LiteralPath $settingsModels -Filter '*.cs' |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$settingsVmText = Get-Content -Raw -LiteralPath $settingsVm
$settingsBindings = [regex]::Matches($settingsText, '\{Binding\s+([^,}\s]+)[^}]*Mode=TwoWay[^}]*\}')
foreach ($m in $settingsBindings) {
    $path = $m.Groups[1].Value
    if ($path -like 'Settings.*') {
        $leaf = ($path -split '\.')[-1]
        $pattern = 'public\s+[\w<>?\[\]]+\s+' + [regex]::Escape($leaf) + '\s*\{\s*get;\s*set;'
        if ($modelText -notmatch $pattern) {
            $failures.Add("ProductionSettings: TwoWay path '$path' has no public get/set property '$leaf'")
        }
    } elseif ($path -eq 'MasterFaultRequiredCount') {
        if ($settingsVmText -notmatch 'public\s+int\s+MasterFaultRequiredCount\s*\{[^}]*set\s*=>') {
            $failures.Add('ProductionSettingsViewModel.MasterFaultRequiredCount is TwoWay but not writable')
        }
    } elseif ($path -in @('Enabled','Name','Channel','MinOhm','MaxOhm')) {
        $leaf = $path
        $pattern = 'public\s+[\w<>?\[\]]+\s+' + [regex]::Escape($leaf) + '\s*\{\s*get;\s*set;'
        if ($modelText -notmatch $pattern) {
            $failures.Add("ResistanceChannelSetting: TwoWay property '$leaf' has no public get/set")
        }
    }
}

# 8) The LastThtPath display field must stay OneWay.
if ($settingsText -notmatch 'Text="\{Binding\s+Settings\.LastThtPath,\s*Mode=OneWay\}"') {
    $failures.Add('ProductionSettingsPage: Settings.LastThtPath display field must be OneWay')
}

if ($failures.Count -gt 0) {
    Write-Host "READ-ONLY BINDING AUDIT: FAIL ($($failures.Count))" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host 'READ-ONLY BINDING AUDIT: PASS' -ForegroundColor Green
Write-Host ' - TestView header fields are explicit OneWay.'
Write-Host ' - No implicit TextBox.Text binding remains in TestWindow.'
Write-Host ' - No OneWayToSource exists in project XAML.'
Write-Host ' - TestView display properties remain read-only and notify on model changes.'
Write-Host ' - Settings TwoWay bindings target writable properties.'
exit 0
