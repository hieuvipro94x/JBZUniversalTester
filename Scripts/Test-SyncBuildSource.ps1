Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
$syncScript = Join-Path $PSScriptRoot "Sync-BuildSource.ps1"
$buildScript = Join-Path (Split-Path -Parent $PSScriptRoot) "BUILD_ONE_FILE.cmd"
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("JBZ Source Sync Test " + [guid]::NewGuid().ToString("N"))
$remote = Join-Path $testRoot "remote.git"
$seed = Join-Path $testRoot "seed"
$script:checks = 0

function Assert-Test($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
    Write-Host "PASS: $Message"
}
function Git-Test([string]$At, [string[]]$Arguments) {
    $output = @(& git -C $At @Arguments)
    if ($LASTEXITCODE -ne 0) { throw ("Fixture Git failed: " + ($Arguments -join " ")) }
    return ($output -join "`n")
}
function Write-TestFile([string]$At, [string]$Relative, [string]$Text) {
    $path = Join-Path $At $Relative
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    [IO.File]::WriteAllText($path, $Text, (New-Object Text.UTF8Encoding($false)))
}
function Set-TestVersion([string]$At, [string]$Version) {
    Write-TestFile $At "Version.props" "<Project><PropertyGroup><VersionPrefix>$Version</VersionPrefix></PropertyGroup></Project>"
}
function Commit-Test([string]$At, [string]$Message) {
    [void](Git-Test $At @("-c", "user.name=Source Sync Test", "-c", "user.email=sync-test@example.invalid", "add", "-A"))
    [void](Git-Test $At @("-c", "user.name=Source Sync Test", "-c", "user.email=sync-test@example.invalid", "commit", "--quiet", "-m", $Message))
}
function Clone-Test([string]$Name) {
    $path = Join-Path $testRoot $Name
    [void](Git-Test $testRoot @("clone", "--quiet", "--branch", "wip/20261108-new", $remote, $path))
    return $path
}
function Run-TestProgram([string]$File, [string]$Arguments) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $File; $info.Arguments = $Arguments
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    try {
        [void]$process.Start(); $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw "Fixture process timed out: $File" }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Text = ($stdout.Result + $stderr.Result) }
    }
    finally { $process.Dispose() }
}
function Run-Sync([string]$At, [string]$Extra = "") {
    Run-TestProgram "powershell.exe" "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$syncScript`" -RepositoryPath `"$At`" -Remote origin -ExpectedRepositoryUrl `"$remote`" $Extra"
}

try {
    [void][IO.Directory]::CreateDirectory($testRoot)
    [void](Git-Test $testRoot @("init", "--quiet", "--bare", $remote))
    [void][IO.Directory]::CreateDirectory($seed)
    [void](Git-Test $seed @("init", "--quiet", "--initial-branch=wip/20261108-new"))
    [void](Git-Test $seed @("remote", "add", "origin", $remote))
    Set-TestVersion $seed "2026.09.402"
    Write-TestFile $seed "JBZUniversalTester.csproj" "<Project />"
    Write-TestFile $seed ".gitignore" "PublishSingle/"
    Write-TestFile $seed "README.md" "old source"
    $launcher = [IO.File]::ReadAllText($buildScript).Replace("https://github.com/hieuvipro94x/JBZUniversalTester.git", $remote)
    Write-TestFile $seed "BUILD_ONE_FILE.cmd" $launcher
    Write-TestFile $seed "Scripts/Sync-BuildSource.ps1" ([IO.File]::ReadAllText($syncScript))
    foreach ($name in @("Publish-OneFile.ps1", "Verify-JBZSignature.ps1", "Resolve-BuildVersion.ps1", "Set-DailyBuildBranch.ps1")) {
        Write-TestFile $seed "Scripts/$name" "throw 'Fixture must not reach publish/version/branch code before updating.'"
    }
    Commit-Test $seed "old release"
    [void](Git-Test $seed @("push", "--quiet", "origin", "wip/20261108-new"))
    $old = Git-Test $seed @("rev-parse", "HEAD")
    [void](Git-Test $seed @("checkout", "--quiet", "-b", "wip/20261109-new"))
    Set-TestVersion $seed "2026.09.403"
    Write-TestFile $seed "README.md" "new source"
    Write-TestFile $seed "BUILD_ONE_FILE.cmd" "@echo off`r`necho UPDATED_LAUNCHER_RAN`r`nexit /b 0`r`n"
    Commit-Test $seed "latest production release"
    [void](Git-Test $seed @("push", "--quiet", "origin", "wip/20261109-new"))
    $latest = Git-Test $seed @("rev-parse", "HEAD")
    # A newer, higher version on a tools branch must never become app source.
    [void](Git-Test $seed @("checkout", "--quiet", "-b", "tools/license-issuer"))
    Set-TestVersion $seed "9999.0.0"
    Commit-Test $seed "unrelated tool"
    [void](Git-Test $seed @("push", "--quiet", "origin", "tools/license-issuer"))

    $clean = Clone-Test "clean clone"
    $inspection = Run-Sync $clean "-CheckOnly"
    Assert-Test ($inspection.ExitCode -eq 0 -and $inspection.Text.Contains("V2026.09.403") -and
        -not $inspection.Text.Contains("V9999.0.0") -and (Git-Test $clean @("rev-parse", "HEAD")) -eq $old) "Inspection reports the latest production version without updating source"
    Assert-Test ((Run-Sync $clean "-VerifyCurrent").ExitCode -eq 1 -and
        (Git-Test $clean @("rev-parse", "HEAD")) -eq $old) "Post-build verification rejects newer remote source without changing the built source"
    $updated = Run-Sync $clean
    Assert-Test ($updated.ExitCode -eq 10 -and (Git-Test $clean @("rev-parse", "HEAD")) -eq $latest -and
        $updated.Text.IndexOf("Phien ban Git moi nhat") -lt $updated.Text.IndexOf("Dang lay code")) "Clean source fast-forwards only after displaying latest version/commit"
    Assert-Test ((Run-Sync $clean).ExitCode -eq 0) "Already-current source does not update or request a launcher restart"
    Assert-Test ((Run-Sync $clean "-VerifyCurrent").ExitCode -eq 0) "Post-build verification accepts a build based on the latest source"

    $dirty = Clone-Test "dirty clone"
    Write-TestFile $dirty "README.md" "my unsaved edit"
    Write-TestFile $dirty "local-note.txt" "keep untracked work"
    $rejected = Run-Sync $dirty
    Assert-Test ($rejected.ExitCode -eq 1 -and (Git-Test $dirty @("rev-parse", "HEAD")) -eq $old -and
        [IO.File]::ReadAllText((Join-Path $dirty "README.md")) -eq "my unsaved edit" -and
        [IO.File]::ReadAllText((Join-Path $dirty "local-note.txt")) -eq "keep untracked work") "Behind/dirty source stops while preserving tracked and untracked edits"

    $diverged = Clone-Test "diverged clone"
    Write-TestFile $diverged "local-commit.txt" "local committed work"
    Commit-Test $diverged "local branch work"
    $localCommit = Git-Test $diverged @("rev-parse", "HEAD")
    Assert-Test ((Run-Sync $diverged).ExitCode -eq 1 -and (Git-Test $diverged @("rev-parse", "HEAD")) -eq $localCommit) "Divergent local commits are never reset or rebased automatically"

    Write-TestFile $clean "README.md" "current release with local edits"
    Assert-Test ((Run-Sync $clean).ExitCode -eq 0 -and
        [IO.File]::ReadAllText((Join-Path $clean "README.md")) -eq "current release with local edits") "Local edits based on the latest release remain buildable and untouched"
    Set-TestVersion $clean "2026.09.404"
    Commit-Test $clean "local next release"
    Assert-Test ((Run-Sync $clean).ExitCode -eq 0) "Local commits ahead of the latest release are retained"

    $selfUpdate = Clone-Test "launcher clone"
    $path = Join-Path $selfUpdate "BUILD_ONE_FILE.cmd"
    $launched = Run-TestProgram $env:ComSpec "/d /c `"`"$path`"`""
    Assert-Test ($launched.ExitCode -eq 0 -and $launched.Text.Contains("UPDATED_LAUNCHER_RAN") -and
        (Git-Test $selfUpdate @("rev-parse", "HEAD")) -eq $latest) "A running CMD safely updates itself and executes the new launcher"

    $failedFetch = Clone-Test "fetch failure clone"
    $missingRemote = Join-Path $testRoot "missing.git"
    [void](Git-Test $failedFetch @("remote", "set-url", "origin", $missingRemote))
    $failure = Run-TestProgram "powershell.exe" "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$syncScript`" -RepositoryPath `"$failedFetch`" -Remote origin -ExpectedRepositoryUrl `"$missingRemote`""
    Assert-Test ($failure.ExitCode -eq 1 -and (Git-Test $failedFetch @("rev-parse", "HEAD")) -eq $old) "Failed fetch stops before any source update or build"
    Write-Host "SOURCE SYNC TEST SUMMARY: $script:checks checks PASS"
}
finally {
    $absolute = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $absolute.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe fixture cleanup path." }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}
