# Thay đổi V8

- Phân tích trace đầu dò thật `JBZ_TRACE_20260805_172436`.
- Xóa hoàn toàn việc gửi command `:PINTEST`.
- Đổi đầu dò thành sự kiện tự phát `:TESTPIN,<pin>,ON/OFF` trong chu kỳ START.
- Nút đầu dò chỉ ẩn/hiện GUI, không gửi dữ liệu UART.
- Thêm tracker nhiều pin active đồng thời, giữ thứ tự chạm mới nhất.
- Thêm các dòng **Đầu dò GND** nền xanh ngọc vào bảng.
- Chân đầu dò đứng sau lỗi đấu sai nhưng trước lỗi hở mạch.
- OFF xóa dòng đầu dò ngay lập tức.
- CLEAR, STOP và về menu xóa sạch trạng thái đầu dò.
- Giữ cột Màu dây và bốn ô màu.
- Nâng config lên version 8 và tự chuyển lựa chọn đầu dò cũ sang tùy chọn ẩn/hiện.
