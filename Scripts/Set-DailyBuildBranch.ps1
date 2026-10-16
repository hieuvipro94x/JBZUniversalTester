param(
    [string]$RepositoryPath = (Split-Path -Parent $PSScriptRoot),
    [datetime]$BuildDate = (Get-Date),
    [string]$Remote = "build-target"
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$current = & git -C $RepositoryPath symbolic-ref --quiet --short HEAD
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($current)) {
    throw "Cannot select a daily build branch from detached HEAD or outside a Git repository."
}

$target = "wip/" + $BuildDate.ToString("yyyyMMdd", [Globalization.CultureInfo]::InvariantCulture) + "-new"
if ($current -ne $target) {
    # Renaming must not bypass the source synchronization guard for yesterday's branch.
    & git -C $RepositoryPath show-ref --verify --quiet "refs/remotes/$Remote/$current"
    if ($LASTEXITCODE -eq 0) {
        & git -C $RepositoryPath merge-base --is-ancestor "$Remote/$current" HEAD
        if ($LASTEXITCODE -ne 0) {
            throw "Sync $Remote/$current before renaming the build branch."
        }
    }

    & git -C $RepositoryPath show-ref --verify --quiet "refs/heads/$target"
    if ($LASTEXITCODE -eq 0) {
        throw "Branch $target already exists. No branch was overwritten or switched."
    }

    & git -C $RepositoryPath branch -m $target
    if ($LASTEXITCODE -ne 0) {
        throw "Cannot rename branch $current to $target."
    }
}

Write-Output $target
