param(
    [string]$Repo = "D:\Code\JBZUniversalTester-NEW\JBZUniversalTester",
    [string]$Message = "WIP: Codex refactor checkpoint 2026-09-03"
)

$ErrorActionPreference = "Stop"
Set-Location $Repo

Write-Host "=== REPO ==="
git rev-parse --show-toplevel

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupDir = Join-Path (Split-Path $Repo -Parent) ("CODEX_CHECKPOINT_" + $stamp)
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

Write-Host "=== SAVE STATUS / DIFF BEFORE COMMIT ==="
git status --porcelain=v1 | Out-File -Encoding utf8 (Join-Path $backupDir "git_status_before.txt")
git diff | Out-File -Encoding utf8 (Join-Path $backupDir "working_tree.diff")
git diff --cached | Out-File -Encoding utf8 (Join-Path $backupDir "staged.diff")
git log --oneline --decorate -30 | Out-File -Encoding utf8 (Join-Path $backupDir "git_log.txt")

Write-Host "=== COPY HANDOFF IF PRESENT ==="
$handoff = Join-Path $Repo "CODEX_HANDOFF_2026-09-03.md"
if (Test-Path $handoff) {
    Copy-Item $handoff $backupDir -Force
}

Write-Host "=== CREATE WIP COMMIT ==="
git add -A

$hasStaged = git diff --cached --quiet; $diffExit = $LASTEXITCODE
if ($diffExit -ne 0) {
    git commit -m $Message
} else {
    Write-Host "No uncommitted tracked/untracked changes to commit."
}

Write-Host "=== CREATE SAFETY BRANCH POINTER ==="
$branch = "backup/codex-wip-$stamp"
git branch $branch HEAD

Write-Host "=== CREATE PORTABLE GIT BUNDLE ==="
$bundle = Join-Path $backupDir ("JBZUniversalTester_" + $stamp + ".bundle")
git bundle create $bundle --all

Write-Host "=== SAVE FINAL STATE ==="
git status --porcelain=v1 | Out-File -Encoding utf8 (Join-Path $backupDir "git_status_after.txt")
git rev-parse HEAD | Out-File -Encoding ascii (Join-Path $backupDir "HEAD.txt")
git branch --show-current | Out-File -Encoding utf8 (Join-Path $backupDir "current_branch.txt")

Write-Host ""
Write-Host "DONE"
Write-Host "Checkpoint folder:"
Write-Host $backupDir
Write-Host ""
Write-Host "Tomorrow, open the repo and give Codex:"
Write-Host '  "Read CODEX_HANDOFF_2026-09-03.md first. Then inspect git status and continue from the WIP checkpoint. Do not rollback existing changes."'
