@echo off
setlocal EnableExtensions
chcp 65001 >nul
title JBZUniversalTester - Build + Smart Version + GitHub

set "ROOT=%~dp0"
set "PS_SCRIPT=%ROOT%Scripts\Publish-OneFile.ps1"
set "VERSION_RESOLVER=%ROOT%Scripts\Resolve-BuildVersion.ps1"
set "VERSION_FILE=%ROOT%Version.props"
set "PROJECT_NAME=JBZUniversalTester"
set "PROJECT_FILE_NAME=JBZUniversalTester.csproj"
set "TARGET_REPO_URL=https://github.com/hieuvipro94x/JBZUniversalTester.git"
set "BUILD_REMOTE=build-target"

pushd "%ROOT%" >nul 2>&1
if errorlevel 1 (
    echo [LỖI] Không thể mo thu muc project:
    echo %ROOT%
    pause
    exit /b 1
)

echo ============================================================
echo JBZUniversalTester - BUILD + KIỂM TRA VERSION + GITHUB
echo ============================================================
echo.

rem ============================================================
rem KIỂM TRA ĐÚNG PROJECT TRƯỚC KHI FETCH/PUSH
rem ============================================================
if not exist "%ROOT%JBZUniversalTester.csproj" (
    echo [LỖI AN TOÀN] File BUILD_ONE_FILE này không thuộc project hiện tại.
    echo Project yêu cầu : JBZUniversalTester
    echo File cần có      : JBZUniversalTester.csproj
    echo Thư mục hiện tại : %ROOT%
    echo Dừng để tránh fetch/push nhầm repository.
    goto :FAIL
)

if not exist "%PS_SCRIPT%" (
    echo [LỖI] Không tìm thấy:
    echo %PS_SCRIPT%
    goto :FAIL
)

if not exist "%VERSION_FILE%" (
    echo [LỖI] Không tìm thấy:
    echo %VERSION_FILE%
    goto :FAIL
)

if not exist "%VERSION_RESOLVER%" (
    echo [LỖI] Không tìm thấy:
    echo %VERSION_RESOLVER%
    goto :FAIL
)

if not exist "%ROOT%.gitignore" (
    echo [LỖI] Không tìm thấy .gitignore tai:
    echo %ROOT%.gitignore
    echo Dừng lại để tránh đẩy nhầm file runtime/build lên GitHub.
    goto :FAIL
)

where git >nul 2>&1
if errorlevel 1 (
    echo [LỖI GIT] Chưa cài Git hoặc Git chưa có trong PATH.
    goto :FAIL
)

git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo [LỖI GIT] Thư mục này không phải repository Git.
    goto :FAIL
)

for /f "delims=" %%B in ('git branch --show-current') do set "CURRENT_BRANCH=%%B"
if not defined CURRENT_BRANCH (
    echo [LỖI GIT] Đang ở detached HEAD. Hãy chuyển sang một branch trước khi build.
    goto :FAIL
)

rem Cho phép Git for Windows xử lý đường dẫn dài.
git config core.longpaths true >nul 2>&1

rem Remote build riêng cho đúng project; KHÔNG phụ thuộc origin/upstream hiện tại.
git remote get-url "%BUILD_REMOTE%" >nul 2>&1
if errorlevel 1 (
    echo [GIT] Tạo remote riêng "%BUILD_REMOTE%" cho đúng project.
    git remote add "%BUILD_REMOTE%" "%TARGET_REPO_URL%"
    if errorlevel 1 (
        echo [LỖI GIT] Không thể tạo remote "%BUILD_REMOTE%".
        goto :FAIL
    )
) else (
    rem Luôn ép build-target về đúng repository của project hiện tại.
    git remote set-url "%BUILD_REMOTE%" "%TARGET_REPO_URL%"
    if errorlevel 1 (
        echo [LỖI GIT] Không thể cấu hình remote "%BUILD_REMOTE%".
        goto :FAIL
    )
)

set "VERIFIED_BUILD_URL="
for /f "delims=" %%U in ('git remote get-url "%BUILD_REMOTE%"') do set "VERIFIED_BUILD_URL=%%U"
if /I not "%VERIFIED_BUILD_URL%"=="%TARGET_REPO_URL%" (
    echo [LỖI AN TOÀN] Remote build không đúng repository yêu cầu.
    echo Đang có : %VERIFIED_BUILD_URL%
    echo Cần đúng : %TARGET_REPO_URL%
    goto :FAIL
)

echo [GIT] Project hiện tại : JBZUniversalTester
echo [GIT] Nhánh hiện tại   : %CURRENT_BRANCH%
echo [GIT] Repository đích  : %TARGET_REPO_URL%
echo [GIT] Remote sử dụng   : %BUILD_REMOTE%
echo [GIT] Mọi file được stage sẽ tuân theo .gitignore.
echo.

rem ============================================================
rem B0 - DONG BO SOURCE TRUOC KHI GAN VERSION VA BUILD
rem ============================================================
echo ============================================================
echo BƯỚC 0/3 - ĐỒNG BỘ %BUILD_REMOTE%/%CURRENT_BRANCH%
echo ============================================================

git -c core.longpaths=true fetch "%BUILD_REMOTE%"
if errorlevel 1 (
    echo [LỖI GIT] Không fetch được repository đích. Dừng build để tránh trùng version.
    goto :FAIL
)

set "REMOTE_BEFORE_BUILD=NONE"
git show-ref --verify --quiet "refs/remotes/%BUILD_REMOTE%/%CURRENT_BRANCH%"
if errorlevel 1 (
    echo [GIT] Branch %BUILD_REMOTE%/%CURRENT_BRANCH% chưa tồn tại; sẽ tạo khi push lần đầu.
) else (
    for /f "delims=" %%C in ('git rev-parse "refs/remotes/%BUILD_REMOTE%/%CURRENT_BRANCH%"') do set "REMOTE_BEFORE_BUILD=%%C"

    rem Không dùng rebase --autostash vì working tree có thể đang có thay đổi
    rem và project có file đường dẫn dài. Chỉ kiểm tra remote có đi trước local hay không.
    git merge-base --is-ancestor "%BUILD_REMOTE%/%CURRENT_BRANCH%" HEAD
    if errorlevel 1 (
        echo [LỖI GIT] Repository đích có commit mới hoặc lịch sử đã tách nhánh.
        echo Script sẽ KHÔNG tự rebase/autostash để tránh mất thay đổi đang làm.
        echo Hãy đồng bộ source trước rồi chạy lại BUILD_ONE_FILE.cmd.
        goto :FAIL
    )
)

echo [GIT] Source đã kiểm tra với đúng repository trước build.
echo.

rem ============================================================
rem B1 - XAC NHAN VERSION, CHI TU TANG KHI SOURCE DOI MA VERSION CHUA TANG
rem ============================================================
echo ============================================================
echo BƯỚC 1/3 - KIỂM TRA PHIÊN BẢN
echo ============================================================

set "VERSION_BACKUP=%TEMP%\JBZUniversalTester_Version_%RANDOM%_%RANDOM%.props"
copy /Y "%VERSION_FILE%" "%VERSION_BACKUP%" >nul
if errorlevel 1 (
    echo [LỖI] Không thể tạo bản tạm Version.props.
    goto :FAIL
)

set "NEW_VERSION="
set "VERSION_ACTION="
set "VERSION_RESULT=%TEMP%\JBZ_VersionResult_%RANDOM%_%RANDOM%.txt"
set "VERSION_ERROR=%TEMP%\JBZ_VersionError_%RANDOM%_%RANDOM%.txt"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%VERSION_RESOLVER%" 1>"%VERSION_RESULT%" 2>"%VERSION_ERROR%"
set "VERSION_EXIT=%ERRORLEVEL%"

if not "%VERSION_EXIT%"=="0" (
    echo [LỖI VERSION] Không thể xác nhận Version.props.
    if exist "%VERSION_ERROR%" type "%VERSION_ERROR%"
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" "%VERSION_RESULT%" "%VERSION_ERROR%" >nul 2>&1
    goto :FAIL
)

for /f "usebackq tokens=1,2 delims=|" %%V in ("%VERSION_RESULT%") do (
    set "NEW_VERSION=%%V"
    set "VERSION_ACTION=%%W"
)

del /Q "%VERSION_RESULT%" "%VERSION_ERROR%" >nul 2>&1

if not defined NEW_VERSION (
    echo [LỖI VERSION] Resolve-BuildVersion.ps1 không trả về phiên bản hợp lệ.
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :FAIL
)

if /I "%VERSION_ACTION%"=="AUTO_INCREMENTED" (
    echo [VERSION] Source đã thay đổi và version chưa tăng: đã tự tăng một lần.
) else if /I "%VERSION_ACTION%"=="ALREADY_INCREMENTED" (
    echo [VERSION] Version đã được tăng khi sửa source: giữ nguyên, không tăng tiếp.
) else if /I "%VERSION_ACTION%"=="UNCHANGED_REBUILD" (
    echo [VERSION] Source không đổi: build lại đúng version hiện tại.
) else (
    echo [LỖI VERSION] Trạng thái không hợp lệ: %VERSION_ACTION%
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :FAIL
)

set "VERSION_TAG=%NEW_VERSION:.=_%"
set "EXPECTED_EXE=%ROOT%PublishSingle\V%NEW_VERSION%\JBZUniversalTester.exe"

echo Phiên bản build: V%NEW_VERSION%
echo File EXE       : JBZUniversalTester.exe
echo Thư mục        : PublishSingle\V%NEW_VERSION%\
echo.

choice /C YN /N /M "Tiếp tục BUILD phiên bản V%NEW_VERSION%? [Y/N]: "
if errorlevel 2 (
    echo Đã hủy. Đang khôi phục Version.props...
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :CANCEL
)

rem ============================================================
rem B2 - PUBLISH
rem ============================================================
echo.
echo ============================================================
echo BƯỚC 2/3 - BUILD/PUBLISH V%NEW_VERSION%
echo ============================================================

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass ^
  -File "%PS_SCRIPT%" ^
  -Runtime "win-x86" ^
  -Configuration "Release" ^
  -OutputFolder "PublishSingle"

set "BUILD_EXIT=%ERRORLEVEL%"
if not "%BUILD_EXIT%"=="0" (
    echo.
    echo [LỖI] BUILD/PUBLISH THẤT BẠI.
    echo Khôi phục Version.props cũ.
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :FAIL
)

if not exist "%EXPECTED_EXE%" (
    echo.
    echo [LỖI] Publish thành công nhưng không tìm thấy EXE:
    echo %EXPECTED_EXE%
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :FAIL
)

del /Q "%VERSION_BACKUP%" >nul 2>&1

echo.
echo BUILD THÀNH CÔNG: V%NEW_VERSION%
echo EXE: %EXPECTED_EXE%
echo.

rem ============================================================
rem B3 - GIT
rem ============================================================
echo ============================================================
echo BƯỚC 3/3 - TỔNG HỢP SOURCE THEO .GITIGNORE
echo ============================================================

rem Remote co the thay doi trong vai phut build. Neu co, dung lai de EXE
rem khong bi lech source/version so voi commit sap push.
git -c core.longpaths=true fetch "%BUILD_REMOTE%"
if errorlevel 1 (
    echo [LỖI GIT] Không fetch được repository đích sau build.
    goto :FAIL
)

set "REMOTE_AFTER_BUILD=NONE"
git show-ref --verify --quiet "refs/remotes/%BUILD_REMOTE%/%CURRENT_BRANCH%"
if not errorlevel 1 (
    for /f "delims=" %%C in ('git rev-parse "refs/remotes/%BUILD_REMOTE%/%CURRENT_BRANCH%"') do set "REMOTE_AFTER_BUILD=%%C"
)

if /I not "%REMOTE_AFTER_BUILD%"=="%REMOTE_BEFORE_BUILD%" (
    echo [LỖI GIT] %BUILD_REMOTE%/%CURRENT_BRANCH% đã thay đổi trong lúc build.
    echo Chạy lại BUILD_ONE_FILE.cmd để đồng bộ và build đúng source mới nhất.
    goto :FAIL
)

git -c core.longpaths=true add -A
if errorlevel 1 (
    echo [LỖI GIT] git add -A thất bại.
    goto :FAIL
)

echo.
echo Các thay đổi sẽ được commit:
echo ------------------------------------------------------------
git status --short
echo ------------------------------------------------------------
echo.

choice /C YN /N /M "Commit và đẩy source lên GitHub branch %CURRENT_BRANCH%? [Y/N]: "
if errorlevel 2 (
    echo Đã hủy push. File build V%NEW_VERSION% vẫn được giữ trên máy.
    goto :CANCEL
)

git diff --cached --quiet
if errorlevel 1 (
    git commit -m "Release V%NEW_VERSION% - auto publish"
    if errorlevel 1 (
        echo [LỖI GIT] git commit thất bại.
        goto :FAIL
    )
) else (
    echo Không có thay đổi mới cần commit.
)

echo.
echo.
echo Các commit đang chờ push:
if "%REMOTE_AFTER_BUILD%"=="NONE" (
    git log --oneline -5 HEAD
) else (
    git log --oneline "%BUILD_REMOTE%/%CURRENT_BRANCH%..HEAD"
)
echo.

echo Repository đích: %TARGET_REPO_URL%
choice /C YN /N /M "Xác nhận PUSH branch %CURRENT_BRANCH% lên repository trên ngay bây giờ? [Y/N]: "
if errorlevel 2 (
    echo Đã hủy PUSH. Commit vẫn an toàn trên máy.
    goto :CANCEL
)

git -c core.longpaths=true push "%BUILD_REMOTE%" HEAD:"%CURRENT_BRANCH%"
if errorlevel 1 (
    echo [LỖI GIT] PUSH thất bại.
    goto :FAIL
)

echo.
echo ============================================================
echo HOÀN TẤT THÀNH CÔNG
echo Version : V%NEW_VERSION%
echo GitHub  : %TARGET_REPO_URL%
echo ============================================================
git status -sb
goto :SUCCESS

:SUCCESS
set "FINAL_EXIT=0"
goto :DONE

:CANCEL
echo.
echo Đã hủy theo yêu cầu. Không có source nào bị xóa.
set "FINAL_EXIT=0"
goto :DONE

:FAIL
echo.
echo ============================================================
echo ĐÃ DỪNG DO CÓ LỖI
echo Cửa sổ sẽ KHÔNG tự động đóng.
echo ============================================================
set "FINAL_EXIT=1"
goto :DONE

:DONE
echo.
pause
popd >nul
exit /b %FINAL_EXIT%
