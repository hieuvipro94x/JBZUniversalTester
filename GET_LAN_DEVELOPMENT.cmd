@echo off
setlocal EnableExtensions DisableDelayedExpansion
title JBZUniversalTester - Get separate LAN development branch

set "TARGET_BRANCH=feature/lan-backup-machine-id"
set "REPOSITORY_URL=https://github.com/hieuvipro94x/JBZUniversalTester.git"
for %%I in ("%~dp0..\JBZUniversalTester-LAN-DEV") do set "DEV_DIR=%%~fI"

echo ============================================================
echo GET LAN BACKUP DEVELOPMENT CODE
echo Branch: %TARGET_BRANCH%
echo Destination: "%DEV_DIR%"
echo The original project folder and old branch are kept unchanged.
echo ============================================================
echo.

where git >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Git is not installed or is missing from PATH.
    goto :FAIL
)

if exist "%DEV_DIR%" goto :UPDATE

echo [GIT] Downloading a separate development checkout...
git clone --single-branch --branch "%TARGET_BRANCH%" "%REPOSITORY_URL%" "%DEV_DIR%"
if errorlevel 1 (
    echo [ERROR] Clone failed. Check network access and GitHub authentication.
    echo Any existing files are preserved. No automatic cleanup is performed.
    goto :FAIL
)
goto :SUCCESS

:UPDATE
if not exist "%DEV_DIR%\.git" (
    echo [ERROR] The destination already exists but is not a Git checkout.
    echo Its files are preserved. Choose a different location before retrying.
    goto :FAIL
)

git -C "%DEV_DIR%" rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 goto :FAIL

set "REMOTE_OK=0"
for /f "delims=" %%U in ('git -C "%DEV_DIR%" remote get-url origin') do if /I "%%U"=="%REPOSITORY_URL%" set "REMOTE_OK=1"
if "%REMOTE_OK%"=="0" (
    echo [ERROR] The destination uses a different origin. Nothing is updated.
    goto :FAIL
)

set "BRANCH_OK=0"
for /f "delims=" %%B in ('git -C "%DEV_DIR%" symbolic-ref --quiet HEAD') do if "%%B"=="refs/heads/%TARGET_BRANCH%" set "BRANCH_OK=1"
if "%BRANCH_OK%"=="0" (
    echo [ERROR] The destination is on another branch. Nothing is switched.
    goto :FAIL
)

git -C "%DEV_DIR%" status --porcelain --untracked-files=all
if errorlevel 1 goto :FAIL
set "HAS_CHANGES=0"
for /f "delims=" %%S in ('git -C "%DEV_DIR%" status --porcelain --untracked-files^=all') do set "HAS_CHANGES=1"
if "%HAS_CHANGES%"=="1" (
    echo [ERROR] The development checkout has local changes.
    echo Commit or preserve them before retrying. No reset or stash is performed.
    goto :FAIL
)

echo [GIT] Fetching the development branch...
git -C "%DEV_DIR%" fetch --no-tags origin "refs/heads/%TARGET_BRANCH%:refs/remotes/origin/%TARGET_BRANCH%"
if errorlevel 1 (
    echo [ERROR] Fetch failed. The local code is preserved.
    goto :FAIL
)

git -C "%DEV_DIR%" merge-base --is-ancestor HEAD "origin/%TARGET_BRANCH%"
if errorlevel 1 (
    echo [ERROR] Local commits differ from origin. No automatic merge is performed.
    goto :FAIL
)

git -C "%DEV_DIR%" merge --ff-only "origin/%TARGET_BRANCH%"
if errorlevel 1 goto :FAIL

:SUCCESS
echo.
echo [OK] Development code is ready in:
echo "%DEV_DIR%"
git -C "%DEV_DIR%" log -1 --oneline
echo Open that folder in Codex or Visual Studio to continue development.
echo The old checkout was not switched, updated, or merged.
echo.
pause
exit /b 0

:FAIL
echo.
echo [STOPPED] No code was deleted or overwritten with a forced reset.
echo.
pause
exit /b 1
