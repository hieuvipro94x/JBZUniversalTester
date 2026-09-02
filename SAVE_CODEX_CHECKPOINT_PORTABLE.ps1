param(
    [string]$Repo = "",
    [string]$Message = "WIP: Codex refactor checkpoint"
)

$ErrorActionPreference = "Stop"

# Nếu không truyền -Repo thì dùng chính thư mục hiện tại.
if ([string]::IsNullOrWhiteSpace($Repo)) {
    $Repo = (Get-Location).Path
}

Set-Location $Repo

# Xác nhận đây là Git repository.
$repoRoot = (git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
    throw "Thư mục hiện tại không phải Git repository: $Repo"
}

Set-Location $repoRoot

Write-Host ""
Write-Host "=== JBZ CODEX CHECKPOINT ==="
Write-Host "Repo: $repoRoot"
Write-Host ""

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$parent = Split-Path $repoRoot -Parent
$backupDir = Join-Path $parent ("CODEX_CHECKPOINT_" + $stamp)
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null

Write-Host "1/7 Lưu git status và diff..."
git status --porcelain=v1 | Out-File -Encoding utf8 (Join-Path $backupDir "git_status_before.txt")
git diff | Out-File -Encoding utf8 (Join-Path $backupDir "working_tree.diff")
git diff --cached | Out-File -Encoding utf8 (Join-Path $backupDir "staged.diff")
git log --oneline --decorate -30 | Out-File -Encoding utf8 (Join-Path $backupDir "git_log.txt")

Write-Host "2/7 Sao chép file bàn giao nếu có..."
$handoffFiles = @(
    "CODEX_HANDOFF_2026-09-03.md",
    "CODEX_HANDOFF.md"
)

foreach ($name in $handoffFiles) {
    $p = Join-Path $repoRoot $name
    if (Test-Path $p) {
        Copy-Item $p $backupDir -Force
    }
}

Write-Host "3/7 Stage toàn bộ thay đổi..."
git add -A

Write-Host "4/7 Tạo WIP commit nếu có thay đổi..."
git diff --cached --quiet
$diffExit = $LASTEXITCODE

if ($diffExit -ne 0) {
    $fullMessage = "$Message $stamp"
    git commit -m $fullMessage
    if ($LASTEXITCODE -ne 0) {
        throw "git commit thất bại."
    }
} else {
    Write-Host "Không có thay đổi mới cần commit."
}

Write-Host "5/7 Tạo nhánh backup..."
$branch = "backup/codex-wip-$stamp"
git branch $branch HEAD
if ($LASTEXITCODE -ne 0) {
    throw "Không tạo được nhánh backup."
}

Write-Host "6/7 Tạo Git bundle portable..."
$bundle = Join-Path $backupDir ("JBZUniversalTester_" + $stamp + ".bundle")
git bundle create $bundle --all
if ($LASTEXITCODE -ne 0) {
    throw "Không tạo được Git bundle."
}

Write-Host "7/7 Lưu trạng thái cuối..."
git status --porcelain=v1 | Out-File -Encoding utf8 (Join-Path $backupDir "git_status_after.txt")
git rev-parse HEAD | Out-File -Encoding ascii (Join-Path $backupDir "HEAD.txt")
git branch --show-current | Out-File -Encoding utf8 (Join-Path $backupDir "current_branch.txt")
$repoRoot | Out-File -Encoding utf8 (Join-Path $backupDir "repo_path_at_checkpoint.txt")

Write-Host ""
Write-Host "=============================================="
Write-Host "CHECKPOINT HOAN TAT"
Write-Host "=============================================="
Write-Host "Repo:"
Write-Host "  $repoRoot"
Write-Host ""
Write-Host "Backup:"
Write-Host "  $backupDir"
Write-Host ""
Write-Host "Git bundle:"
Write-Host "  $bundle"
Write-Host ""
Write-Host "Ngay mai duong dan project co the KHAC."
Write-Host "Chi can cd vao project moi roi chay Codex."
Write-Host ""
Write-Host "Prompt de tiep tuc:"
Write-Host 'Doc CODEX_HANDOFF_2026-09-03.md truoc. Sau do doc AGENTS.md, git status va git log. Tiep tuc tu WIP checkpoint hien tai. Khong rollback cac thay doi da co.'
Write-Host ""
