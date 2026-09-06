JBZLicenseGenerator - BẢN TOOL CẤP KEY OFFLINE
============================================================

MỤC ĐÍCH
--------
Đây là project WPF độc lập để cấp MÃ ĐĂNG KÝ cho JBZUniversalTester.
Tool dùng ECDSA P-256:

- JBZLicenseGenerator giữ PRIVATE KEY.
- JBZUniversalTester sau này chỉ được nhúng PUBLIC KEY.
- PRIVATE KEY tuyệt đối không được copy sang máy sản xuất.

CẤU TRÚC
--------
JBZLicenseGenerator\
    JBZLicenseGenerator.csproj
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Models\LicensePayload.cs
    Services\Base64Url.cs
    Services\RegistrationId.cs
    Services\LicenseKeyStore.cs
    Services\LicenseGeneratorService.cs

Add-JBZLicenseGenerator.ps1
Build-JBZLicenseGenerator.ps1

CÁCH ĐƯA VÀO SOLUTION HIỆN TẠI
-------------------------------
1. Giải nén package vào đúng thư mục gốc đang có JBZUniversalTester.sln.

Ví dụ:

D:\Code\JBZUniversalTester-NEW\JBZUniversalTester\
    JBZUniversalTester.sln
    JBZUniversalTester\
    JBZLicenseGenerator\
    Add-JBZLicenseGenerator.ps1

2. PowerShell:

.\Add-JBZLicenseGenerator.ps1

Hoặc:

.\Add-JBZLicenseGenerator.ps1 -Solution .\JBZUniversalTester.sln

3. Build tool:

.\Build-JBZLicenseGenerator.ps1

HOẶC mở Visual Studio 2022 và Build project JBZLicenseGenerator.

BUILD EXE PORTABLE
------------------
Chạy:

.\Build-JBZLicenseGenerator.ps1

Script tạo một file portable:

JBZLicenseGenerator\bin\Release\net8.0-windows\win-x64\publish\JBZLicenseGenerator.exe

Trong lúc build, bộ khóa chính thức tại đường dẫn dưới đây được nhúng vào riêng EXE
Generator:

%LOCALAPPDATA%\JBZ\LicenseGenerator\jbz-license-private.pem
%LOCALAPPDATA%\JBZ\LicenseGenerator\jbz-license-public.pem

Sau khi build, có thể mang riêng JBZLicenseGenerator.exe sang máy Windows x64 khác,
mở tool, dán ID và tạo mã mà không cần chép thêm file khóa.

QUAN TRỌNG: EXE Generator lúc này chứa khả năng ký license. Bất kỳ ai có file EXE đều
có thể cấp mã. Chỉ lưu EXE trên máy cấp phép do bạn quản lý; không gửi EXE Generator
sang máy sản xuất hoặc khách hàng.

Nếu mất PRIVATE KEY:
- không thể tạo thêm license tương thích với public key cũ.

Tool tự kiểm tra bộ khóa nhúng có khớp nhau và PUBLIC KEY có đúng fingerprint
production hay không. Nếu sai, nút tạo mã sẽ bị khóa.

CẤP LICENSE
-----------
1. Cài/chạy JBZUniversalTester trên máy sản xuất.
2. Khi chưa có license, phần mềm tự hiện ID ĐĂNG KÝ trước MainWindow.
3. Bấm SAO CHÉP ID trên máy sản xuất và gửi ID về máy cấp phép.
4. Trên JBZLicenseGenerator, bấm DÁN ID.
5. Bấm TẠO MÃ ĐĂNG KÝ CHO ID NÀY.
4. Tool tạo mã dạng:

JBZ1.<PAYLOAD>.<SIGNATURE>

6. Bấm SAO CHÉP MÃ ĐĂNG KÝ và gửi mã về đúng máy sản xuất.
7. Dán mã vào ô MÃ ĐĂNG KÝ của JBZUniversalTester rồi bấm ĐĂNG KÝ.
8. Mã hợp lệ được lưu và MainWindow mở ngay. Các lần chạy sau phần mềm tự xác minh lại.

FORMAT HIỆN TẠI
---------------
Product:
JBZUniversalTester

License:
PERPETUAL

Protocol:
JBZ1

ID được normalize:
- uppercase;
- bỏ khoảng trắng;
- bỏ dấu gạch;
- giữ chữ và số;
- dài 8..64 ký tự.

LƯU Ý QUAN TRỌNG
----------------
- JBZUniversalTester đã có RegistrationWindow và license gate trước MainWindow.
- Generator và ứng dụng dùng cùng format JBZ1/ECDSA P-256/SHA-256/P1363.
- Mã chỉ hợp lệ cho đúng Registration ID đã nhập.
- KHÔNG copy PRIVATE KEY vào JBZUniversalTester hoặc máy sản xuất.
