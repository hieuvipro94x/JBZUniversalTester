# UniversalTester License Issuer

Nhà phát triển: Vũ Tuấn Nam. .NET 8 Desktop Runtime x86 / Windows.

Tool này nằm riêng ngoài project UniversalTester. Nhánh Git: `tools/license-issuer` trong repository JBZUniversalTester. Để lấy ở máy thứ hai, clone riêng, không checkout nhánh tool đè lên thư mục phần mềm test:

```powershell
git clone --branch tools/license-issuer --single-branch https://github.com/hieuvipro94x/JBZUniversalTester.git JBZLicenseIssuer
```

## Sử dụng

1. Máy khách mở UniversalTester, sao chép toàn bộ **MÃ ĐĂNG KÝ CỦA MÁY**.
2. Tool cấp key: dán mã, nhập đơn vị/vị trí/ghi chú, bấm **TẠO MÃ KÍCH HOẠT**. Lưu hồ sơ thành công rồi mới hiển thị mã.
3. Sao chép mã gửi máy khách; dán vào ô **MÃ KÍCH HOẠT**, bấm **KÍCH HOẠT**.
4. Chọn hồ sơ và **XÁC NHẬN ĐÃ KÍCH HOẠT** khi đã kiểm tra thực tế. Đây là xác nhận thủ công; chế độ offline không báo trạng thái máy khách về tool.
5. **XUẤT EXCEL** tạo `.xlsx` gồm máy, đơn vị, vị trí, ngày cấp, ngày xác nhận, ghi chú. Không xuất khóa bí mật hoặc mã kích hoạt vào Excel.

## Làm việc ở nhà và công ty

Khóa ký ECDSA P-256 đầu tiên đã được tạo trên máy quản trị triển khai. Kho nằm tại `%LOCALAPPDATA%\JBZLicenseIssuer\PrivateVault\issuer.dat`, được Windows DPAPI CurrentUser mã hóa. Không sao chép trực tiếp kho DPAPI sang tài khoản/Windows khác.

**Ngay lần đầu dùng tool trên máy quản trị hiện tại, bấm XUẤT BỘ CẤP PHÉP**, đặt mật khẩu ít nhất 12 ký tự. Tệp `.jbzvault` chứa khóa ký và danh sách máy, được PBKDF2-SHA256 (210.000 vòng) + AES-256-GCM bảo vệ. Giữ ít nhất một bản sao an toàn ngoài ổ Windows. Ở máy quản trị thứ hai bấm **NHẬP BỘ CẤP PHÉP**, nhập mật khẩu. Nhập lại sẽ gộp hồ sơ theo mã giấy phép. Muốn đồng bộ hai nơi, xuất bộ mới nhất rồi nhập bên còn lại. Git chuyển mã nguồn. Theo yêu cầu riêng ngày 05/11/2026, bản xuất mã hóa `Backups/JBZ-cap-phep-20261105.jbzvault` cũng được lưu trên nhánh này để nhập ở máy còn lại; mật khẩu được giữ riêng ngoài Git.

Không đưa `issuer.dat`, mật khẩu bảo vệ hay khóa PKCS8 dạng rõ lên Git hoặc thư mục public LAN. Chỉ bản xuất mã hóa trong `Backups/` được chỉ định rõ mới được đưa lên Git; các tệp `.jbzvault` khác vẫn bị `.gitignore` chặn. Mất cả kho và bản xuất sẽ không cấp tiếp được giấy phép cho EXE hiện tại; phải thay khóa công khai và phát hành EXE mới. Tool không tự tạo khóa thay thế khi thiếu kho.

## Bảo mật và giới hạn

UniversalTester chỉ chứa SPKI khóa công khai, không chứa khóa ký. Mã kích hoạt được ký và gắn với nonce bản cài + MachineGuid Windows + số sê-ri ổ vật lý chứa Windows. Cài mới Windows/thay ổ hệ thống yêu cầu kích hoạt lại. Đổi thư mục hoặc cập nhật phần mềm giữ kích hoạt. Khôi phục nguyên ảnh Windows trên cùng ổ có thể giữ nguyên nhận dạng.

Giấy phép khách nằm ngoài CFG, tại `%PROGRAMDATA%\JBZUniversalTester\Licensing`, được DPAPI LocalMachine bảo vệ. Xóa dữ liệu này sẽ yêu cầu kích hoạt lại. Đây là chữ ký offline chống tự tạo mã hợp lệ; không cam kết ngăn tuyệt đối việc sửa EXE bởi người kiểm soát máy.

## Build / kiểm thử

```powershell
dotnet build -c Release
.\bin\Release\net8.0-windows\win-x86\JBZLicenseIssuer.exe --self-test C:\Temp\issuer-tests.txt
.\BUILD_ISSUER.ps1
```

Self-test dùng khóa tạm trong bộ nhớ, không đọc hoặc sửa kho thật. Public key trong `Common/LicenseProtocol.cs` phải khớp UniversalTester. Không thay khóa khi cập nhật giao diện.
