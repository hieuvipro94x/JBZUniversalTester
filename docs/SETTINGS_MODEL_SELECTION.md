# Chọn mã cấu hình và danh mục MainWindow — V2026.09.467

- Header Cài đặt có **CHỌN MÃ CÀI ĐẶT**. Chọn THT chỉ thay ngữ cảnh chỉnh cấu hình; không nạp sản phẩm vào TestView, không đổi `LastThtPath` của máy đang chạy.
- Nhãn tem, LOT bắt đầu, điện trở, Leak, relay và Master sử dụng khóa theo tên file THT như cơ chế hiện có. Cổng thiết bị và các thông số máy dùng chung vẫn là cấu hình chung.
- Khi đổi ngữ cảnh: thay đổi chưa lưu cho phép Lưu / Bỏ qua / Hủy. THT được đọc nền trước khi gắn trang mới; lỗi đọc giữ trang hiện tại. Phiên quyền truy cập được giữ nguyên.
- Các thao tác điều khiển bo/Leak/đo điện trở bằng tay chỉ dùng khi mã cấu hình trùng mã runtime. In thử/xem trước dùng file cấu hình đang chọn.
- MainWindow giới hạn bề rộng toàn khối thông tin/danh mục; mỗi dòng danh mục cao cố định, dành tối đa hai dòng tên file. Đường dẫn đầy đủ ở tooltip.
- **Xóa mã** yêu cầu xác nhận tên và đường dẫn, mặc định Không. Mã runtime bị chặn xóa. Xử lý file trên worker, khóa thao tác danh mục trong lúc xử lý, làm mới sau thành công.
- Dùng Windows Recycle Bin; không tự chuyển sang xóa vĩnh viễn khi hệ thống/ổ mạng không hỗ trợ. Lỗi quyền hoặc lỗi hệ thống được báo và ghi log.
- Xóa THT không xóa lịch sử SQLite, CFG hoặc dữ liệu tem. Các cấu hình theo khóa tên file được giữ lại.

Kiểm thử dùng file và DB tạm, delegate giả lập recycle để kiểm tra xác nhận/hủy/lỗi/chặn lặp và cô lập runtime. Chưa kiểm thử thao tác cảm biến, bo thật hay in vật lý.
