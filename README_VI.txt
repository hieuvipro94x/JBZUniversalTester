V10.5 CURRENT - 2026-08-07
==============================
- TestPin tách decoder riêng: A0/A1 là chính chân đang chạm GND, không tạo lỗi chập.
- TestPin hiển thị A0/A1 ngay khi nhận byte, giảm khoảng 300 ms so với chờ C0.
- Production status: CHỜ LẮP SẢN PHẨM -> ĐANG KIỂM TRA -> PASS.
- PASS nền xanh, phát DINGDONG ngay và relay bắt đầu ngay.

JBZ UNIVERSAL TESTER - BUILD 1 CLICK + CẤU HÌNH JSON

1. CHÉP FILE
- Chép BUILD_ONE_FILE.cmd vào thư mục gốc solution/project.
- Chép thư mục Scripts đi cùng BUILD_ONE_FILE.cmd.
- Nếu dùng AppSettings.cs mới, thay file trong Services.

Cấu trúc:
  <thư mục project>/
    BUILD_ONE_FILE.cmd
    Scripts/
      Publish-OneFile.ps1
    JBZUniversalTester.csproj

2. BUILD/PUBLISH MỘT LẦN NHẤN
- Nhấp đúp BUILD_ONE_FILE.cmd.
- Script tự:
  + tìm JBZUniversalTester.csproj
  + dừng EXE đang chạy
  + xóa bin/obj/PublishSingle
  + restore
  + publish Release win-x86
  + framework-dependent
  + single-file
  + mở Explorer tới EXE

Kết quả:
  PublishSingle/JBZUniversalTester.exe

Máy chạy cần .NET 8 Desktop Runtime x86.

3. CẤU HÌNH KỸ THUẬT NỘI BỘ
Các giá trị AppSettings về bo, thiết bị đo và thời gian kỹ thuật được biên dịch
trong EXE. Phần mềm không đọc/ghi chúng vào JBZUniversalTester.cfg hoặc
appsettings.json.

JBZUniversalTester.cfg chỉ lưu các thông số vận hành được chỉnh trên màn hình
Cài đặt, ví dụ LOT, số card, Leak, relay, điện trở và máy in tem.

4. THÊM GIÁ TRỊ KỸ THUẬT SAU NÀY
Thêm giá trị mặc định vào lớp AppSettings/TestSettings rồi build phiên bản mới.
Không tạo khóa App.* trong file cấu hình dành cho người vận hành.

5. DÙNG TRONG CHƯƠNG TRÌNH
Khởi tạo:
  AppSettings settings = AppSettings.Load();

Đọc:
  int delay = settings.Keysight.SettleDelayMs;

Các giá trị này không có API lưu ra file cấu hình.

6. LƯU Ý
- Không bật EnableCompressionInSingleFile=true khi SelfContained=false.
- Không bật PublishTrimmed cho WPF.
- Nếu PublishSingle còn file ngoài EXE, kiểm tra các mục Content có:
  CopyToOutputDirectory hoặc CopyToPublishDirectory.
- Driver FTDI vẫn phải được cài trên máy.

============================================================
V10 TRACE FIXED - 2026-08-07
============================================================
Logic continuity, THT resistance, relay and auto-cycle have been rebuilt from
production trace `20260807_105731_production_Htdrv3-JBZ8_RT`.
See: docs\V10_TRACE_ANALYSIS.md

============================================================
V10.3 CURRENT - 2026-08-07
============================================================
Bản hiện tại dùng protocol SOURCE->TARGET 80/81 -> A0/A1, 64 I/O/card,
kiểm tra thiếu card trước khi test, TestPin 1..256 và fast scan theo trace thật.
Các note V10/V10.2 bên dưới là lịch sử; V10.3 là logic ưu tiên hiện tại.
Xem: docs\V10_3_TRACE_AND_BEHAVIOR.md
