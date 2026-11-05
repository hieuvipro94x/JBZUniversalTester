# Bộ cấp phép mã hóa dùng ở nhà và công ty

Theo yêu cầu của Vũ Tuấn Nam ngày 05/11/2026, bản xuất `JBZ-cap-phep-20261105.jbzvault` được lưu trên nhánh `tools/license-issuer`.

- Tệp chứa khóa cấp phép và danh sách máy tại thời điểm xuất, được AES-256-GCM mã hóa, dẫn xuất khóa bằng PBKDF2-SHA256 210.000 vòng.
- Mật khẩu ngẫu nhiên 256 bit lưu riêng trên máy công ty: `C:\Users\user\Documents\JBZLicenseIssuer-Private\Mat-khau-bo-cap-phep-20261105.txt`.
- Mật khẩu không có trên Git và không nằm trong thư mục phát hành/LAN public. Chuyển mật khẩu sang máy quản trị còn lại bằng kênh riêng.
- Máy ở nhà: lấy tệp này, mở tool, chọn **NHẬP BỘ CẤP PHÉP**, chọn tệp và nhập mật khẩu.
- Bản xuất này là ảnh chụp tại thời điểm xuất, không tự cập nhật khi cấp thêm mã. Xuất bộ mới khi cần đồng bộ danh sách máy.
- Không commit khóa dạng rõ, `issuer.dat` hoặc tệp chứa mật khẩu.
