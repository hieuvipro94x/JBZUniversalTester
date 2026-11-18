Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$copyScript = Join-Path $PSScriptRoot 'Copy-VerifiedRelease.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('JBZ-ReleaseCopy-' + [guid]::NewGuid().ToString('N'))
$checks = 0
function Check($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
    Write-Host "PASS: $Message"
}
function Run-Copy([string]$From, [string]$To) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = 'powershell.exe'
    $info.Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$copyScript`" -Source `"$From`" -Destination `"$To`""
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Release copy test timed out.' }
        [pscustomobject]@{ Code = $process.ExitCode; Text = $stdout.Result + $stderr.Result }
    } finally { $process.Dispose() }
}
try {
    $source = Join-Path $root 'source'
    $dest = Join-Path $root 'V2026.09.test'
    [void][IO.Directory]::CreateDirectory((Join-Path $source 'Certificate'))
    [void][IO.Directory]::CreateDirectory((Join-Path $source 'Empty'))
    [IO.File]::WriteAllText((Join-Path $source 'JBZUniversalTester.exe'), 'new binary')
    [IO.File]::WriteAllText((Join-Path $source 'Certificate\public.cer'), 'public cert')
    $result = Run-Copy $source $dest
    Check ($result.Code -eq 0 -and $result.Text.Contains('SHA256 verified: 2 files')) 'New release verifies every copied file'
    Check ((Get-FileHash (Join-Path $source 'JBZUniversalTester.exe')).Hash -eq
        (Get-FileHash (Join-Path $dest 'JBZUniversalTester.exe')).Hash) 'Copied EXE matches source SHA256'
    Check (Test-Path -LiteralPath (Join-Path $dest 'Empty') -PathType Container) 'Empty package directories are copied'
    [IO.File]::WriteAllText((Join-Path $dest 'JBZUniversalTester.cfg'), 'station runtime CFG')
    [IO.File]::WriteAllText((Join-Path $dest 'JBZUniversalTester.db'), 'station history preserved')
    [IO.File]::WriteAllText((Join-Path $source 'JBZUniversalTester.exe'), 'replacement binary')
    $result = Run-Copy $source $dest
    Check ($result.Code -eq 0 -and $result.Text.Contains('Previous folder preserved and verified')) 'Existing release is preserved before replacement'
    $backup = @(Get-ChildItem -LiteralPath (Join-Path $root '.preserved') -Directory)
    Check ($backup.Count -eq 1 -and [IO.File]::ReadAllText((Join-Path $backup[0].FullName 'JBZUniversalTester.exe')) -eq 'new binary') 'Preserved folder contains the original binary'
    foreach ($name in @('JBZUniversalTester.cfg', 'JBZUniversalTester.db')) {
        Check ((Get-FileHash (Join-Path $dest $name)).Hash -eq
            (Get-FileHash (Join-Path $backup[0].FullName $name)).Hash) "Runtime data retained and backed up: $name"
    }
    $before = (Get-FileHash (Join-Path $dest 'JBZUniversalTester.exe')).Hash
    $locked = [IO.File]::Open((Join-Path $source 'JBZUniversalTester.exe'), 'Open', 'ReadWrite', 'None')
    try {
        $result = Run-Copy $source $dest
        Check ($result.Code -eq 1 -and (Get-FileHash (Join-Path $dest 'JBZUniversalTester.exe')).Hash -eq $before) 'Unreadable source fails without damaging destination'
    } finally { $locked.Dispose() }
    $locked = [IO.File]::Open((Join-Path $dest 'JBZUniversalTester.cfg'), 'Open', 'ReadWrite', 'None')
    try {
        $result = Run-Copy $source $dest
        Check ($result.Code -eq 1 -and (Get-FileHash (Join-Path $dest 'JBZUniversalTester.exe')).Hash -eq $before) 'Occupied production data prevents release overwrite'
    } finally { $locked.Dispose() }
    Check ((Run-Copy $source (Join-Path $source 'nested')).Code -eq 1) 'Nested source/destination paths are rejected'
    Check ((Run-Copy $source $source).Code -eq 1) 'Self-copy is rejected'

    # Execute the launcher's real final-status block with a harmless verifier.
    # No build, push or network access is performed by this fixture.
    $launcher = [IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'BUILD_ONE_FILE.cmd'))
    $success = [regex]::Match($launcher, '(?ms)^:SUCCESS\r?\n.*?(?=^:CANCEL\r?\n)').Value
    if (-not $success) { throw 'Cannot locate launcher success block.' }
    $verifier = Join-Path $root 'verify.ps1'
    [IO.File]::WriteAllText($verifier, 'exit 0')
    foreach ($failed in @(0, 1)) {
        $fixture = Join-Path $root ("status-$failed.cmd")
        $header = "@echo off`r`nchcp 65001 >nul`r`nset `"LAN_COPY_FAILED=$failed`"`r`nset `"SIGN_VERIFY_SCRIPT=$verifier`"`r`nset `"EXPECTED_EXE=test.exe`"`r`nset `"NEW_VERSION=TEST`"`r`ngoto :SUCCESS`r`n"
        [IO.File]::WriteAllText($fixture, $header + $success + "`r`n:FAIL`r`nexit /b 1`r`n:DONE`r`nexit /b %FINAL_EXIT%`r`n", (New-Object Text.UTF8Encoding($false)))
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = $env:ComSpec; $info.Arguments = "/d /c `"`"$fixture`"`""
        $info.WorkingDirectory = $root; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        $process = New-Object Diagnostics.Process; $process.StartInfo = $info
        try {
            [void]$process.Start()
            $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit(10000)) { $process.Kill(); throw 'Launcher final-status test timed out.' }
            if ($failed -eq 1) {
                Check ($process.ExitCode -eq 1 -and $stdout.Result.Contains('[LOI GIAO HANG]') -and
                    -not $stdout.Result.Contains('Version :')) 'A failed LAN copy cannot become successful delivery after Git push'
            } else {
                Check ($process.ExitCode -eq 0 -and $stdout.Result.Contains('Version :')) 'Successful copy or allowed unavailable-LAN skip retains successful exit'
            }
        } finally { $process.Dispose() }
    }
    Write-Host "RELEASE COPY TEST SUMMARY: $checks checks PASS"
} finally {
    $absolute = [IO.Path]::GetFullPath($root)
    $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path.' }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}
