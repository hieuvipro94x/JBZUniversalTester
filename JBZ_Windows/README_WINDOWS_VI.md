# JBZ Universal Tester - bản cấu hình Windows

Bản này chuyển cấu hình hệ thống từ Raspberry Pi/Linux sang Windows nhưng giữ nguyên logic Production, protocol UART và bố cục giao diện Tkinter.

## Những gì được giữ nguyên

- Giao diện chính, menu, màn hình test, settings, reports và diagnostic.
- Protocol bo: `115200 8N1`, CRLF.
- Handshake `*IDN?` và `:MODELNAME?`.
- Quy trình `:START`, `MEASURE`, `:MAXEXT`, `:STOP`, PASS PEN, UNCONNECT.
- Logic model/setup, fault table, cycle state, pin probe, lịch sử SQLite.
- Tự tìm đúng bo bằng phản hồi `Universal Tester`; người vận hành không cần chọn COM thủ công.

## Thay đổi dành riêng cho Windows

- UART `/dev/tty*` được thay bằng danh sách COM từ `pyserial`.
- Cache `COMx` không dùng `os.path.exists()` nữa vì COM không phải file path.
- Models: `%USERPROFILE%\Models`.
- Setups: `%USERPROFILE%\Setups`.
- Config Production: `%APPDATA%\JBZUniversalTesterProduction\app.json`.
- Database/log Production: `%LOCALAPPDATA%\JBZUniversalTesterProduction\`.
- Config Model Downloader: `%APPDATA%\JBZModelSetupDownloader\app.json`.
- Log Model Downloader: `%LOCALAPPDATA%\JBZModelSetupDownloader\logs\`.

Có thể ghi đè thư mục model/setup bằng biến môi trường:

```text
JBZ_MODELS_DIR=D:\JBZ\Models
JBZ_SETUPS_DIR=D:\JBZ\Setups
```

## Cài từ source

Yêu cầu Python 3.10+ bản chính thức có Tkinter.

Mở PowerShell trong thư mục project:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install_windows.ps1
```

Sau đó chạy:

```text
run_gui.bat
```

## Kết nối phần cứng

Bo test phải xuất hiện trong **Device Manager > Ports (COM & LPT)**, ví dụ `COM3` hoặc `COM7`.

Ứng dụng sẽ:

1. Thử COM đã dùng thành công lần trước.
2. Nếu không đúng, lấy toàn bộ COM hiện có từ Windows.
3. Quét song song từng COM.
4. Gửi `*IDN?`.
5. Chỉ chấp nhận cổng có chuỗi `Universal Tester`.
6. Gửi `:MODELNAME?` và lưu lại COM đã xác nhận.

Kiểm tra nhanh:

```powershell
.\.venv\Scripts\python.exe .\tools\check_uart_windows.py
```

Nếu không thấy COM, cần cài đúng driver của module USB-UART đang dùng (FTDI/CP210x/CH340 hoặc driver tương ứng phần cứng thực tế).

## Build EXE Windows

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build_windows.ps1
```

Đầu ra:

```text
release\JBZUniversalTester_<VERSION>_windows_x64.zip
```

Bản build dùng PyInstaller `onedir` để ổn định hơn với Tkinter và pyserial.

## Lưu ý phần cứng Raspberry Pi

Windows PC không truy cập trực tiếp chân UART GPIO của Raspberry Pi theo kiểu `/dev/serial0`. Nếu bo JBZ trước đây nối vào UART GPIO của Pi, khi chuyển sang Windows cần một bộ **USB-UART đúng mức điện áp của bo** để Windows nhận thành COM. Không thay đổi điện áp logic bằng phần mềm.
