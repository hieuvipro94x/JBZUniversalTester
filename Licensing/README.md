# Kích hoạt UniversalTester (V2026.09.388)

Nhà phát triển hiển thị: **Vũ Tuấn Nam**.

Ứng dụng xác minh giấy phép offline trước khi nạp cấu hình, tạo MainViewModel hoặc mở bo/COM. Lần đầu hiện cửa sổ đăng ký. Sau khi mã có chữ ký hợp lệ được lưu thành công, các lần mở sau tự xác minh và vào phần mềm. Không kiểm tra giấy phép trong vòng quét IO, Probe, Leak, điện trở hoặc relay.

Mã đăng ký gồm nhận dạng bản cài và SHA-256 của MachineGuid Windows + số sê-ri ổ hệ thống. Không dùng RAM/CPU/MAC/serial FTDI. Cài mới Windows hoặc thay ổ hệ thống cần cấp mã mới. Cập nhật EXE/đổi thư mục giữ kích hoạt. Khôi phục nguyên ảnh Windows trên cùng ổ có thể giữ nhận dạng cũ. Nếu WMI không đọc được ổ, cửa sổ kích hoạt cho thử lại; không tự bỏ qua xác minh.

Hai tệp trạng thái `installation.dat` và `activation.dat` nằm ở `%PROGRAMDATA%\JBZUniversalTester\Licensing`, được Windows DPAPI LocalMachine mã hóa, ghi tệp tạm rồi đổi tên nguyên tử. Không nằm trong CFG, thư mục phát hành hoặc lịch sử test. Không ghi mã kích hoạt vào logs. Xóa tệp trạng thái sẽ yêu cầu kích hoạt lại. Khóa công khai nằm trong `Licensing/LicenseProtocol.cs`; không có khóa ký trong phần mềm.

Tool cấp phép là project riêng `C:\Users\user\Desktop\Nam\JBZLicenseIssuer`, nguồn tại [nhánh tools/license-issuer](https://github.com/hieuvipro94x/JBZUniversalTester/tree/tools/license-issuer). Xem README ở đó để xuất/nhập bộ cấp phép có mật khẩu giữa nhà và công ty. Git không đồng bộ khóa bí mật và danh sách máy.

Chữ ký ECDSA P-256/SHA-256 ngăn tự tạo license hợp lệ nếu không có khóa ký. Không cam kết EXE .NET không thể bị vá bởi người kiểm soát máy. Bảo vệ bản sao bộ cấp phép và mật khẩu ngoài Git/LAN public.

Kiểm thử: `JBZUniversalTester.SelfTests.exe --test-offline-activation`. Dùng thư mục tạm và khóa kiểm thử, không kích hoạt máy thật hoặc đọc/sửa dữ liệu sản xuất. Bộ kiểm thử đầy đủ bao gồm cả kiểm thử này.
