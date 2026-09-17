@echo off
setlocal EnableExtensions
chcp 65001 >nul

rem ============================================================
rem PROJECT-SPECIFIC SAFETY CONFIGURATION
rem This file is intentionally bound to ONE project/repository.
rem It never uses "origin" implicitly for fetch/push.
rem ============================================================
set "PROJECT_NAME=JBZUniversalTester"
set "PROJECT_FILE_NAME=JBZUniversalTester.csproj"
set "TARGET_REPO_URL=https://github.com/hieuvipro94x/JBZUniversalTester.git"
set "BUILD_REMOTE=build-target"

title %PROJECT_NAME% - Build + Smart Version + GitHub

set "ROOT=%~dp0"
set "PROJECT_FILE=%ROOT%%PROJECT_FILE_NAME%"
set "PS_SCRIPT=%ROOT%Scripts\Publish-OneFile.ps1"
set "VERSION_RESOLVER=%ROOT%Scripts\Resolve-BuildVersion.ps1"
set "VERSION_FILE=%ROOT%Version.props"

pushd "%ROOT%" >nul 2>&1
if errorlevel 1 (
    echo [LOI] Khong the mo thu muc project:
    echo %ROOT%
    pause
    exit /b 1
)

echo ============================================================
echo %PROJECT_NAME% - BUILD + VERSION + GITHUB
echo ============================================================
echo.

rem ============================================================
rem A0 - VERIFY THIS SCRIPT IS IN THE CORRECT PROJECT
rem ============================================================
if not exist "%PROJECT_FILE%" (
    echo [LOI AN TOAN] File nay KHONG thuoc project hien tai.
    echo.
    echo Project mong doi : %PROJECT_NAME%
    echo Can tim thay      : %PROJECT_FILE_NAME%
    echo Thu muc hien tai  : %ROOT%
    echo.
    echo DUNG LAI de tranh fetch/push nham repository.
    goto :FAIL
)

if not exist "%PS_SCRIPT%" (
    echo [LOI] Khong tim thay:
    echo %PS_SCRIPT%
    goto :FAIL
)

if not exist "%VERSION_FILE%" (
    echo [LOI] Khong tim thay:
    echo %VERSION_FILE%
    goto :FAIL
)

if not exist "%VERSION_RESOLVER%" (
    echo [LOI] Khong tim thay:
    echo %VERSION_RESOLVER%
    goto :FAIL
)

if not exist "%ROOT%.gitignore" (
    echo [LOI] Khong tim thay .gitignore tai:
    echo %ROOT%.gitignore
    echo Dung lai de tranh day nham file runtime/build len GitHub.
    goto :FAIL
)

where git >nul 2>&1
if errorlevel 1 (
    echo [LOI GIT] Chua cai Git hoac Git chua co trong PATH.
    goto :FAIL
)

git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo [LOI GIT] Thu muc nay khong phai repository Git.
    goto :FAIL
)

rem Cho phep Git for Windows xu ly duong dan > 260 ky tu.
git config core.longpaths true >nul 2>&1
if errorlevel 1 (
    echo [CANH BAO] Khong dat duoc core.longpaths=true, nhung script van tiep tuc.
)

set "CURRENT_BRANCH="
for /f "delims=" %%B in ('git branch --show-current') do set "CURRENT_BRANCH=%%B"
if not defined CURRENT_BRANCH (
    echo [LOI GIT] Dang o detached HEAD. Hay switch sang mot branch truoc khi build.
    goto :FAIL
)

rem ============================================================
rem A1 - CREATE/REPAIR DEDICATED BUILD REMOTE
rem ============================================================
git remote get-url "%BUILD_REMOTE%" >nul 2>&1
if errorlevel 1 (
    echo [GIT] Tao remote rieng "%BUILD_REMOTE%".
    git remote add "%BUILD_REMOTE%" "%TARGET_REPO_URL%"
    if errorlevel 1 (
        echo [LOI GIT] Khong tao duoc remote "%BUILD_REMOTE%".
        goto :FAIL
    )
) else (
    set "CURRENT_BUILD_URL="
    for /f "delims=" %%U in ('git remote get-url "%BUILD_REMOTE%"') do set "CURRENT_BUILD_URL=%%U"
    if /I not "%CURRENT_BUILD_URL%"=="%TARGET_REPO_URL%" (
        echo [GIT] Remote "%BUILD_REMOTE%" dang tro sai repo.
        echo [GIT] Cu : %CURRENT_BUILD_URL%
        echo [GIT] Moi: %TARGET_REPO_URL%
        git remote set-url "%BUILD_REMOTE%" "%TARGET_REPO_URL%"
        if errorlevel 1 (
            echo [LOI GIT] Khong sua duoc remote "%BUILD_REMOTE%".
            goto :FAIL
        )
    )
)

set "VERIFIED_BUILD_URL="
for /f "delims=" %%U in ('git remote get-url "%BUILD_REMOTE%"') do set "VERIFIED_BUILD_URL=%%U"
if /I not "%VERIFIED_BUILD_URL%"=="%TARGET_REPO_URL%" (
    echo [LOI AN TOAN] Remote build khong dung repository mong doi.
    echo Dang co : %VERIFIED_BUILD_URL%
    echo Can dung: %TARGET_REPO_URL%
    goto :FAIL
)

echo [PROJECT] %PROJECT_NAME%
echo [PROJECT] File   : %PROJECT_FILE_NAME%
echo [GIT] Remote    : %BUILD_REMOTE%
echo [GIT] Repository: %TARGET_REPO_URL%
echo [GIT] Branch    : %CURRENT_BRANCH%
echo [GIT] origin/upstream khac se KHONG duoc dung de fetch/push.
echo.

rem Lay ten EXE tu AssemblyName trong csproj; neu khong co thi dung ten csproj.
set "ASSEMBLY_NAME="
for /f "usebackq delims=" %%A in (`powershell.exe -NoLogo -NoProfile -Command "$p='%PROJECT_FILE%'; $x=[xml](Get-Content -LiteralPath $p -Raw); $n=($x.Project.PropertyGroup.AssemblyName ^| Where-Object { $_ } ^| Select-Object -First 1); if([string]::IsNullOrWhiteSpace([string]$n)){[IO.Path]::GetFileNameWithoutExtension($p)}else{[string]$n}"`) do set "ASSEMBLY_NAME=%%A"
if not defined ASSEMBLY_NAME set "ASSEMBLY_NAME=%PROJECT_NAME%"

rem ============================================================
rem B0 - DONG BO DUNG REPOSITORY + DUNG BRANCH TRUOC BUILD
rem ============================================================
echo ============================================================
echo BUOC 0/3 - DONG BO %BUILD_REMOTE%/%CURRENT_BRANCH%
echo ============================================================

git -c core.longpaths=true fetch "%BUILD_REMOTE%"
if errorlevel 1 (
    echo [LOI GIT] Khong fetch duoc:
    echo %TARGET_REPO_URL%
    goto :FAIL
)

set "REMOTE_BEFORE_BUILD=NONE"
git show-ref --verify --quiet "refs/remotes/%BUILD_REMOTE%/%CURRENT_BRANCH%"
if errorlevel 1 (
    echo [GIT] Branch %BUILD_REMOTE%/%CURRENT_BRANCH% chua ton tai.
    echo [GIT] Se tao branch nay khi push lan dau.
) else (
    for /f "delims=" %%C in ('git rev-parse "refs/remotes/%BUILD_REMOTE%/%CURRENT_BRANCH%"') do set "REMOTE_BEFORE_BUILD=%%C"

    rem Khong rebase/autostash khi working tree dang co thay doi.
    rem Chi kiem tra remote co nam trong lich su local hay khong.
    git merge-base --is-ancestor "%BUILD_REMOTE%/%CURRENT_BRANCH%" HEAD
    if errorlevel 1 (
        echo.
        echo [LOI GIT] Remote %BUILD_REMOTE%/%CURRENT_BRANCH% co commit moi
        echo hoac lich su da bi tach nhanh so voi local.
        echo Script KHONG tu rebase/autostash de tranh mat thay doi dang lam.
        echo Hay dong bo project truoc, sau do chay lai BUILD_ONE_FILE.cmd.
        goto :FAIL
    )
)

echo [GIT] Da fetch dung repo. Local hien tai khong bi remote di truoc.
echo.

rem ============================================================
rem B1 - XAC NHAN VERSION
rem ============================================================
echo ============================================================
echo BUOC 1/3 - KIEM TRA PHIEN BAN
echo ============================================================

set "VERSION_BACKUP=%TEMP%\%PROJECT_NAME%_Version_%RANDOM%_%RANDOM%.props"
copy /Y "%VERSION_FILE%" "%VERSION_BACKUP%" >nul
if errorlevel 1 (
    echo [LOI] Khong the tao ban tam Version.props.
    goto :FAIL
)

set "NEW_VERSION="
set "VERSION_ACTION="

for /f "tokens=1,2 delims=|" %%V in ('powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%VERSION_RESOLVER%"') do (
    set "NEW_VERSION=%%V"
    set "VERSION_ACTION=%%W"
)

if not defined NEW_VERSION (
    echo [LOI VERSION] Khong the xac nhan Version.props.
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :FAIL
)

if /I "%VERSION_ACTION%"=="AUTO_INCREMENTED" (
    echo [VERSION] Source da thay doi va version chua tang: da tu tang mot lan.
) else if /I "%VERSION_ACTION%"=="ALREADY_INCREMENTED" (
    echo [VERSION] Version da duoc tang khi sua source: giu nguyen.
) else if /I "%VERSION_ACTION%"=="UNCHANGED_REBUILD" (
    echo [VERSION] Source khong doi: build lai dung version hien tai.
) else (
    echo [LOI VERSION] Trang thai khong hop le: %VERSION_ACTION%
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :FAIL
)

set "EXPECTED_EXE=%ROOT%PublishSingle\V%NEW_VERSION%\%ASSEMBLY_NAME%.exe"

echo Version build : V%NEW_VERSION%
echo File EXE      : %ASSEMBLY_NAME%.exe
echo Thu muc       : PublishSingle\V%NEW_VERSION%\
echo GitHub target : %TARGET_REPO_URL%
echo Branch target : %CURRENT_BRANCH%
echo.

choice /C YN /N /M "Tiep tuc BUILD V%NEW_VERSION%? [Y/N]: "
if errorlevel 2 (
    echo Da huy. Dang khoi phuc Version.props...
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :CANCEL
)

rem ============================================================
rem B2 - PUBLISH
rem ============================================================
echo.
echo ============================================================
echo BUOC 2/3 - BUILD/PUBLISH V%NEW_VERSION%
echo ============================================================

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass ^
  -File "%PS_SCRIPT%" ^
  -Runtime "win-x86" ^
  -Configuration "Release" ^
  -OutputFolder "PublishSingle"

set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" (
    echo.
    echo [LOI] BUILD/PUBLISH THAT BAI.
    echo Khoi phuc Version.props cu.
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :FAIL
)

if not exist "%EXPECTED_EXE%" (
    echo.
    echo [LOI] Publish thanh cong nhung khong tim thay EXE:
    echo %EXPECTED_EXE%
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :FAIL
)

del /Q "%VERSION_BACKUP%" >nul 2>&1

echo.
echo BUILD THANH CONG: V%NEW_VERSION%
echo EXE: %EXPECTED_EXE%
echo.

rem ============================================================
rem B3 - GIT COMMIT + PUSH ONLY TO THE BOUND REPOSITORY
rem ============================================================
echo ============================================================
echo BUOC 3/3 - COMMIT + PUSH DUNG REPOSITORY
echo ============================================================

git -c core.longpaths=true fetch "%BUILD_REMOTE%"
if errorlevel 1 (
    echo [LOI GIT] Khong fetch duoc %BUILD_REMOTE% sau build.
    goto :FAIL
)

set "REMOTE_AFTER_BUILD=NONE"
git show-ref --verify --quiet "refs/remotes/%BUILD_REMOTE%/%CURRENT_BRANCH%"
if not errorlevel 1 (
    for /f "delims=" %%C in ('git rev-parse "refs/remotes/%BUILD_REMOTE%/%CURRENT_BRANCH%"') do set "REMOTE_AFTER_BUILD=%%C"
)

if /I not "%REMOTE_AFTER_BUILD%"=="%REMOTE_BEFORE_BUILD%" (
    echo [LOI GIT] %BUILD_REMOTE%/%CURRENT_BRANCH% da thay doi trong luc build.
    echo Chay lai BUILD_ONE_FILE.cmd de dong bo va build dung source moi nhat.
    goto :FAIL
)

git -c core.longpaths=true add -A
if errorlevel 1 (
    echo [LOI GIT] git add -A that bai.
    goto :FAIL
)

echo.
echo Cac thay doi se duoc commit:
echo ------------------------------------------------------------
git status --short
echo ------------------------------------------------------------
echo.

choice /C YN /N /M "Commit source cua %PROJECT_NAME% len branch %CURRENT_BRANCH%? [Y/N]: "
if errorlevel 2 (
    echo Da huy push. File build V%NEW_VERSION% van duoc giu tren may.
    goto :CANCEL
)

git diff --cached --quiet
if errorlevel 1 (
    git commit -m "Release V%NEW_VERSION% - auto publish"
    if errorlevel 1 (
        echo [LOI GIT] git commit that bai.
        goto :FAIL
    )
) else (
    echo Khong co thay doi moi can commit.
)

echo.
echo Commit dang cho push vao:
echo %TARGET_REPO_URL%
echo Branch: %CURRENT_BRANCH%
echo ------------------------------------------------------------
if "%REMOTE_AFTER_BUILD%"=="NONE" (
    git log --oneline -5 HEAD
) else (
    git log --oneline "%BUILD_REMOTE%/%CURRENT_BRANCH%..HEAD"
)
echo ------------------------------------------------------------
echo.

choice /C YN /N /M "Xac nhan PUSH dung repo tren? [Y/N]: "
if errorlevel 2 (
    echo Da huy PUSH. Commit van an toan tren may.
    goto :CANCEL
)

rem IMPORTANT: explicit remote + explicit HEAD:branch.
rem Never relies on branch upstream, origin, push.default, or previous config.
git -c core.longpaths=true push "%BUILD_REMOTE%" HEAD:"%CURRENT_BRANCH%"
if errorlevel 1 (
    echo [LOI GIT] PUSH that bai.
    goto :FAIL
)

echo.
echo ============================================================
echo HOAN TAT THANH CONG
echo Project : %PROJECT_NAME%
echo Version : V%NEW_VERSION%
echo Repo    : %TARGET_REPO_URL%
echo Branch  : %CURRENT_BRANCH%
echo ============================================================
git status -sb
goto :SUCCESS

:SUCCESS
set "FINAL_EXIT=0"
goto :DONE

:CANCEL
echo.
echo Da huy theo yeu cau. Khong co source nao bi xoa.
set "FINAL_EXIT=0"
goto :DONE

:FAIL
echo.
echo ============================================================
echo DA DUNG DO CO LOI
echo Cua so se KHONG tu dong dong.
echo ============================================================
set "FINAL_EXIT=1"
goto :DONE

:DONE
echo.
pause
popd >nul
exit /b %FINAL_EXIT%
