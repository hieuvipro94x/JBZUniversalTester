@echo off
setlocal EnableExtensions
chcp 65001 >nul
title JBZUniversalTester - Auto Version + Publish + Git Push

set "ROOT=%~dp0"
set "PS_SCRIPT=%ROOT%Scripts\Publish-OneFile.ps1"
set "VERSION_FILE=%ROOT%Version.props"

pushd "%ROOT%" >nul

echo ============================================================
echo JBZUniversalTester - AUTO VERSION + PUBLISH + GITHUB
echo ============================================================
echo.

rem ============================================================
rem 0. BASIC CHECKS
rem ============================================================

if not exist "%PS_SCRIPT%" (
    echo [ERROR] File not found:
    echo %PS_SCRIPT%
    popd >nul
    pause
    exit /b 1
)

if not exist "%VERSION_FILE%" (
    echo [ERROR] File not found:
    echo %VERSION_FILE%
    popd >nul
    pause
    exit /b 1
)

where git >nul 2>&1
if errorlevel 1 (
    echo [GIT ERROR] Git is not installed or not available in PATH.
    popd >nul
    pause
    exit /b 2
)

git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo [GIT ERROR] "%ROOT%" is not a Git repository.
    popd >nul
    pause
    exit /b 3
)

for /f "delims=" %%B in ('git branch --show-current') do set "CURRENT_BRANCH=%%B"
if /I not "%CURRENT_BRANCH%"=="main" (
    echo [GIT ERROR] Current branch is "%CURRENT_BRANCH%".
    echo Auto publish/push is allowed only on branch "main".
    echo Run:
    echo     git switch main
    popd >nul
    pause
    exit /b 4
)

git remote get-url origin >nul 2>&1
if errorlevel 1 (
    echo [GIT ERROR] Remote "origin" is not configured.
    popd >nul
    pause
    exit /b 5
)

rem ============================================================
rem 1. AUTO-INCREMENT VERSION
rem    Example: 16.0.31 -> 16.0.32
rem ============================================================

echo ============================================================
echo STEP 1/3 - AUTO INCREMENT VERSION
echo ============================================================

set "VERSION_BACKUP=%TEMP%\JBZUniversalTester_Version_%RANDOM%_%RANDOM%.props"
copy /Y "%VERSION_FILE%" "%VERSION_BACKUP%" >nul
if errorlevel 1 (
    echo [ERROR] Cannot create temporary Version.props backup.
    popd >nul
    pause
    exit /b 6
)

set "NEW_VERSION="

for /f "usebackq delims=" %%V in (`powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ^
 "$ErrorActionPreference='Stop';" ^
 "$p=$env:VERSION_FILE;" ^
 "[xml]$x=Get-Content -LiteralPath $p -Raw;" ^
 "$g=@($x.Project.PropertyGroup)[0];" ^
 "$current=[string]$g.Version;" ^
 "if([string]::IsNullOrWhiteSpace($current)){throw 'Version is empty in Version.props'};" ^
 "$v=[Version]$current;" ^
 "$patch=if($v.Build -lt 0){0}else{$v.Build};" ^
 "$n=('{0}.{1}.{2}' -f $v.Major,$v.Minor,($patch+1));" ^
 "$tag=$n.Replace('.','_');" ^
 "$g.VersionPrefix=$n;" ^
 "$g.Version=$n;" ^
 "$g.AssemblyVersion=($n+'.0');" ^
 "$g.FileVersion=($n+'.0');" ^
 "$g.InformationalVersion=$n;" ^
 "$g.VersionFileTag=$tag;" ^
 "$g.AssemblyTitle=('JBZUniversalTester V'+$n);" ^
 "$settings=New-Object System.Xml.XmlWriterSettings;" ^
 "$settings.Indent=$true;" ^
 "$settings.Encoding=New-Object System.Text.UTF8Encoding($true);" ^
 "$writer=[System.Xml.XmlWriter]::Create($p,$settings);" ^
 "$x.Save($writer);" ^
 "$writer.Close();" ^
 "Write-Output $n"`) do set "NEW_VERSION=%%V"

if not defined NEW_VERSION (
    echo [VERSION ERROR] Cannot increment Version.props.
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    popd >nul
    pause
    exit /b 7
)

set "VERSION_TAG=%NEW_VERSION:.=_%"
set "EXPECTED_EXE=%ROOT%PublishSingle\V%NEW_VERSION%\JBZUniversalTester_V%VERSION_TAG%.exe"

echo [VERSION] New version : %NEW_VERSION%
echo [VERSION] EXE name    : JBZUniversalTester_V%VERSION_TAG%.exe
echo [VERSION] Output      : PublishSingle\V%NEW_VERSION%\
echo.

rem ============================================================
rem 2. PUBLISH
rem ============================================================

echo ============================================================
echo STEP 2/3 - PUBLISH ONE FILE V%NEW_VERSION%
echo ============================================================

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass ^
  -File "%PS_SCRIPT%" ^
  -Runtime "win-x86" ^
  -Configuration "Release" ^
  -OutputFolder "PublishSingle"

set "EXIT_CODE=%ERRORLEVEL%"
echo.

if not "%EXIT_CODE%"=="0" (
    echo ============================================================
    echo PUBLISH FAILED.
    echo Version.props will be restored to the previous version.
    echo Git commit/push was NOT run.
    echo ============================================================
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    popd >nul
    pause
    exit /b %EXIT_CODE%
)

if not exist "%EXPECTED_EXE%" (
    echo ============================================================
    echo [ERROR] Publish reported success but versioned EXE is missing:
    echo %EXPECTED_EXE%
    echo Version.props will be restored.
    echo ============================================================
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    popd >nul
    pause
    exit /b 8
)

del /Q "%VERSION_BACKUP%" >nul 2>&1

echo ============================================================
echo PUBLISH SUCCESS
echo VERSION : %NEW_VERSION%
echo EXE     : JBZUniversalTester_V%VERSION_TAG%.exe
echo FOLDER  : PublishSingle\V%NEW_VERSION%\
echo ============================================================
echo.

rem ============================================================
rem 3. COMMIT SOURCE + PUSH MAIN
rem ============================================================

echo ============================================================
echo STEP 3/3 - COMMIT AND PUSH SOURCE TO GITHUB MAIN
echo ============================================================

echo [GIT] Staging source changes...

rem Always include the exact CMD file currently being executed.
rem %~nx0 = current script file name, so renaming this file will not break Git staging.
set "SELF_SCRIPT=%~nx0"
echo [GIT] Including build script: %SELF_SCRIPT%
git add -f -- "%SELF_SCRIPT%"
if errorlevel 1 (
    echo [GIT ERROR] Cannot stage current build script: %SELF_SCRIPT%
    echo Make sure this CMD file is located inside the Git repository root:
    echo     %ROOT%
    popd >nul
    pause
    exit /b 9
)

rem Publish/runtime files are intentionally NOT auto-committed.
rem Keep Labels/, Services/, Models/, Views/, Tests/, Version.props, etc.
git add -A -- . ^
  ":(exclude)PublishSingle/**" ^
  ":(exclude)Data/**" ^
  ":(exclude)Data.zip" ^
  ":(exclude)ALL13.csv" ^
  ":(exclude)publish.log" ^
  ":(exclude)publish_V*.log"

if errorlevel 1 (
    echo [GIT ERROR] git add failed.
    echo Version %NEW_VERSION% was published locally but NOT pushed.
    popd >nul
    pause
    exit /b 9
)

git diff --cached --quiet
if errorlevel 1 (
    echo [GIT] Creating commit for V%NEW_VERSION%...
    git commit -m "Release V%NEW_VERSION% - auto publish"
    if errorlevel 1 (
        echo [GIT ERROR] git commit failed.
        echo Version %NEW_VERSION% remains on this PC.
        popd >nul
        pause
        exit /b 10
    )
) else (
    echo [GIT] No source changes to commit.
)

echo [GIT] Fetching origin/main...
git fetch origin
if errorlevel 1 (
    echo [GIT ERROR] git fetch failed.
    echo Check Internet/GitHub login.
    echo Local V%NEW_VERSION% is safe on this PC.
    popd >nul
    pause
    exit /b 11
)

echo [GIT] Rebasing local main onto origin/main...
git rebase --autostash origin/main
if errorlevel 1 (
    echo.
    echo ============================================================
    echo [GIT ERROR] REBASE FAILED.
    echo Resolve the conflict manually.
    echo Then run:
    echo     git rebase --continue
    echo     git push origin main
    echo.
    echo Local V%NEW_VERSION% is NOT lost.
    echo ============================================================
    popd >nul
    pause
    exit /b 12
)

echo [GIT] Pushing main to GitHub...
git push origin main
if errorlevel 1 (
    echo.
    echo ============================================================
    echo [GIT ERROR] PUSH FAILED.
    echo Check Internet / GitHub authentication / repository access.
    echo Local commit V%NEW_VERSION% is still safe on this PC.
    echo ============================================================
    popd >nul
    pause
    exit /b 13
)

echo.
echo ============================================================
echo ALL DONE
echo ============================================================
echo VERSION : V%NEW_VERSION%
echo EXE     : %EXPECTED_EXE%
echo GITHUB  : origin/main UPDATED
echo ============================================================
echo.

git status -sb
echo.

echo When using another PC:
echo     git switch main
echo     git pull origin main
echo.

popd >nul
pause
exit /b 0
