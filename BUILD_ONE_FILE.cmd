@echo off
setlocal EnableExtensions
chcp 65001 >nul
title JBZUniversalTester - Build + Smart Version + GitHub

set "ROOT=%~dp0"
set "PS_SCRIPT=%ROOT%Scripts\Publish-OneFile.ps1"
set "VERSION_RESOLVER=%ROOT%Scripts\Resolve-BuildVersion.ps1"
set "VERSION_FILE=%ROOT%Version.props"

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
    echo [LOI] Khong tim thay:
    echo %VERSION_RESOLVER%
    goto :FAIL
)

if not exist "%ROOT%.gitignore" (
    echo [LỖI] Không tìm thấy .gitignore tai:
    echo %ROOT%.gitignore
    echo Dung lai de tranh day nham file runtime/build len GitHub.
    goto :FAIL
)

where git >nul 2>&1
if errorlevel 1 (
    echo [LỖI GIT] Chưa cài Git hoac Git chua co trong PATH.
    goto :FAIL
)

git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo [LỖI GIT] Thư mục nay khong phai repository Git.
    goto :FAIL
)

for /f "delims=" %%B in ('git branch --show-current') do set "CURRENT_BRANCH=%%B"
if /I not "%CURRENT_BRANCH%"=="main" (
    echo [LỖI GIT] Nhánh hiện tại: %CURRENT_BRANCH%
    echo Chỉ cho phép chay tren nhanh main.
    echo Hãy chạy: git switch main
    goto :FAIL
)

git remote get-url origin >nul 2>&1
if errorlevel 1 (
    echo [LỖI GIT] Chưa cấu hình remote origin.
    goto :FAIL
)

echo [GIT] Nhánh hiện tại: main
echo [GIT] Mọi file được stage sẽ tuân theo .gitignore.
echo.

rem ============================================================
rem B1 - XAC NHAN VERSION, CHI TU TANG KHI SOURCE DOI MA VERSION CHUA TANG
rem ============================================================
echo ============================================================
echo BUOC 1/3 - KIEM TRA PHIEN BAN
echo ============================================================

set "VERSION_BACKUP=%TEMP%\JBZUniversalTester_Version_%RANDOM%_%RANDOM%.props"
copy /Y "%VERSION_FILE%" "%VERSION_BACKUP%" >nul
if errorlevel 1 (
    echo [LỖI] Không thể tao bản tạm Version.props.
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
    echo [VERSION] Version da duoc tang khi sua source: giu nguyen, khong tang tiep.
) else if /I "%VERSION_ACTION%"=="UNCHANGED_REBUILD" (
    echo [VERSION] Source khong doi: build lai dung version hien tai.
) else (
    echo [LOI VERSION] Trang thai khong hop le: %VERSION_ACTION%
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
    echo Khôi phục Version.props cu.
    copy /Y "%VERSION_BACKUP%" "%VERSION_FILE%" >nul
    del /Q "%VERSION_BACKUP%" >nul 2>&1
    goto :FAIL
)

if not exist "%EXPECTED_EXE%" (
    echo.
    echo [LỖI] Publish thành công nhung không tìm thấy EXE:
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

git add -A
if errorlevel 1 (
    echo [LỖI GIT] git add -A that bai.
    goto :FAIL
)

echo.
echo Các thay đổi sẽ được commit:
echo ------------------------------------------------------------
git status --short
echo ------------------------------------------------------------
echo.

choice /C YN /N /M "Commit và đẩy source lên GitHub main? [Y/N]: "
if errorlevel 2 (
    echo Đã hủy push. File build V%NEW_VERSION% vẫn được giữ trên máy.
    goto :CANCEL
)

git diff --cached --quiet
if errorlevel 1 (
    git commit -m "Release V%NEW_VERSION% - auto publish"
    if errorlevel 1 (
        echo [LỖI GIT] git commit that bai.
        goto :FAIL
    )
) else (
    echo Không có thay doi moi can commit.
)

echo.
echo Đang fetch origin...
git fetch origin
if errorlevel 1 (
    echo [LỖI GIT] git fetch that bai.
    goto :FAIL
)

echo Đang rebase với origin/main...
git rebase --autostash origin/main
if errorlevel 1 (
    echo [LỖI GIT] REBASE THẤT BẠI. Có thể có xung đột code.
    echo Xử lý xung đột rồi chạy:
    echo git rebase --continue
    echo git push origin main
    goto :FAIL
)

echo.
echo Các commit đang chờ push:
git log --oneline origin/main..HEAD
echo.

choice /C YN /N /M "Xác nhận PUSH lên origin/main ngay bây giờ? [Y/N]: "
if errorlevel 2 (
    echo Đã hủy PUSH. Commit van an toan tren may.
    goto :CANCEL
)

git push origin main
if errorlevel 1 (
    echo [LỖI GIT] PUSH thất bại.
    goto :FAIL
)

echo.
echo ============================================================
echo HOÀN TẤT THÀNH CÔNG
echo Version : V%NEW_VERSION%
echo GitHub  : origin/main đã cập nhật
echo ============================================================
git status -sb
goto :DONE

:CANCEL
echo.
echo Đã hủy theo yeu cau. Không có source nao bi xoa.
goto :DONE

:FAIL
echo.
echo ============================================================
echo ĐÃ DỪNG DO CÓ LỖI
echo Cửa sổ sẽ KHÔNG tự động đóng.
echo ============================================================
goto :DONE

:DONE
echo.
pause
popd >nul
exit /b 0
