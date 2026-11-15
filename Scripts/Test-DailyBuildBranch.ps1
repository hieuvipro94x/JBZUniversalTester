$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Set-DailyBuildBranch.ps1'
$root = Join-Path ([IO.Path]::GetTempPath()) ('JBZ-DailyBranch-' + [guid]::NewGuid().ToString('N'))
function Fixture-Git([string[]]$Arguments) {
    $result = & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture Git failed: $Arguments" }
    return $result
}
function Check($Condition, $Message) {
    if (-not $Condition) { throw $Message }
    Write-Host "PASS: $Message"
}
function Run-Daily {
    $ErrorActionPreference = 'Continue'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath -RepositoryPath $root -BuildDate '2026-11-10' 2>&1 | Out-Host
    return $LASTEXITCODE
}
try {
    New-Item -ItemType Directory -Path $root | Out-Null
    Fixture-Git @('init', '--quiet', '--initial-branch=wip/20261109-new')
    [IO.File]::WriteAllText((Join-Path $root 'file.txt'), 'original')
    Fixture-Git @('add', '.')
    Fixture-Git @('-c','user.name=Test','-c','user.email=test@example.invalid','commit','--quiet','-m','initial')
    $initial = Fixture-Git @('rev-parse','HEAD')
    Check ((Run-Daily) -eq 0) 'Missing daily branch is created by rename'
    Check ((Fixture-Git @('branch','--show-current')) -eq 'wip/20261110-new') 'Daily branch selected'
    Check ((Run-Daily) -eq 0) 'Same-day rerun succeeds'
    Fixture-Git @('switch','--quiet','-c','wip/20261109-new')
    [IO.File]::WriteAllText((Join-Path $root 'file.txt'), 'staged')
    Fixture-Git @('add','file.txt')
    [IO.File]::WriteAllText((Join-Path $root 'file.txt'), 'unstaged')
    [IO.File]::WriteAllText((Join-Path $root 'note.txt'), 'untracked')
    $before = (Fixture-Git @('status','--porcelain=v1')) -join "`n"
    Check ((Run-Daily) -eq 0) 'Existing daily branch at identical commit is reused'
    Check (((Fixture-Git @('status','--porcelain=v1')) -join "`n") -eq $before) 'Staged, unstaged and untracked work preserved'
    Check ((Fixture-Git @('rev-parse','HEAD')) -eq $initial) 'Commit unchanged after switching'
    Fixture-Git @('switch','--quiet','wip/20261109-new')
    Fixture-Git @('add','-A')
    Fixture-Git @('-c','user.name=Test','-c','user.email=test@example.invalid','commit','--quiet','-m','local work')
    $local = Fixture-Git @('rev-parse','HEAD')
    Check ((Run-Daily) -ne 0) 'Different daily branch commit is rejected'
    Check ((Fixture-Git @('rev-parse','HEAD')) -eq $local) 'Rejected switch preserves local commits'
    Fixture-Git @('branch','-f','wip/20261110-new','HEAD')
    Fixture-Git @('worktree','add','--quiet',(Join-Path $root 'other'),'wip/20261110-new')
    Check ((Run-Daily) -ne 0) 'Branch owned by another worktree is rejected'
    Check ((Fixture-Git @('branch','--show-current')) -eq 'wip/20261109-new') 'Worktree rejection preserves current branch'
    Write-Host 'DAILY BRANCH: 10 checks PASS'
}
finally {
    $absolute = [IO.Path]::GetFullPath($root)
    $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $absolute.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
