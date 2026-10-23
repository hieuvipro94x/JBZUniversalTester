# V2026.09.303 — mật khẩu và phân quyền Cài đặt

## Sử dụng

- Bấm Cài đặt: nhập mật khẩu Admin để chỉnh toàn bộ, hoặc chọn Người vận hành.
- Mật khẩu Admin ban đầu: `admin` (phân biệt hoa/thường).
- Trong Cài đặt, Admin có nút ĐỔI MẬT KHẨU và PHÂN QUYỀN.
- Đổi mật khẩu yêu cầu mật khẩu hiện tại, mật khẩu mới và nhập lại mật khẩu mới.
- Phân quyền chọn độc lập: IO / Production, Relay / Bảo trì, Test Leak, Điện trở,
  In tem. Nhóm bỏ chọn vẫn xem được nhưng không chỉnh hoặc thao tác trong nhóm.
- Người vận hành ban đầu chỉ xem. Admin luôn có toàn quyền. Mỗi lần mở lại Cài
  đặt chọn lại quyền truy cập; không giữ phiên Admin sau khi trở về MainWindow.
- Quyền Relay bao gồm các ô trong phần Relay / Bảo trì (kể cả Resistance delay
  và Scroll delay đang nằm tại đây), và Manual Relay ở cột IO. Các kênh/min/max
  và đo điện trở thủ công thuộc quyền Điện trở.

## Lưu trữ và thực thi

Thông tin truy cập nằm tại
`%LOCALAPPDATA%\JBZUniversalTester\settings-access.json`, dùng chung cho các phiên
bản EXE của cùng tài khoản Windows. Không thêm mật khẩu vào CFG hoặc lịch sử test.
Mật khẩu dùng PBKDF2-SHA256, salt ngẫu nhiên, 210.000 vòng; không lưu/log mật khẩu
thuần. File lưu bằng ghi tạm, flush và thay thế nguyên tử, có bản backup.
File hỏng không tự đặt lại mật khẩu về admin hoặc ghi đè dữ liệu đang có.

Xác thực và thay mật khẩu chạy ở worker để không giữ Dispatcher. Cài đặt tiếp tục
dùng chung MainWindow. Hộp đăng nhập/đổi mật khẩu/phân quyền là hộp thoại modal,
không có nút riêng trên taskbar. Operator không có nút quản trị truy cập.

Ngoài việc khóa control theo nhóm, ViewModel so sánh snapshot các thuộc tính/editor
bị cấm trước lưu. Save/SaveAsync từ chối sửa nhóm bị cấm; chuẩn bị CFG chỉ cập nhật
profile/master/LOT của các nhóm được phép. Manual Relay/Leak/điện trở kiểm tra quyền
ở cả CanExecute và đầu phương thức thực thi. Dọn dẹp phần cứng khi thoát vẫn dùng
luồng an toàn hiện tại. Quyền không thay đổi scan, PASS/FAIL, relay sản xuất hoặc
in tem tự động sau PASS. Không thay schema/ghi lại dữ liệu lịch sử sản xuất.

## Kiểm chứng

Self-tests dùng thư mục credential tạm và callback lưu giả: mật khẩu ban đầu,
mật khẩu sai, đổi mật khẩu, nhập lại không khớp, persist/reload, operator không
quản trị, file hỏng fail-closed, từng nhóm được lưu riêng, SaveAsync bị chặn khi
sửa nhóm khác, manual command bị chặn, và quyền UI sau chuẩn bị layout nền.
Kiểm thử điều hướng đảm bảo Settings/History vẫn dùng một cửa sổ chính.
Không dùng credential thật hoặc thiết bị thật trong self-test.
