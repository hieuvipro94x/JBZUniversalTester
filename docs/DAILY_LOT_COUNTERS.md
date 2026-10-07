# Bộ đếm sản xuất và LOT theo ngày

Từ V2026.09.200, sản lượng trên màn hình test và khôi phục LOT từ SQLite dùng đúng `PartId` và ngày kết quả được chọn. Truy vấn bao gồm các đợt LOT của cùng mã hàng trong ngày đó để không bỏ sót sản lượng cũ. LOT lấy đúng PASS cuối cùng của ngày; FAIL không tăng LOT. Số lượng tháng và tích lũy vẫn giữ toàn bộ dữ liệu tương ứng.

Khi ngày Windows thay đổi (tự động qua ngày, chỉnh ngày tiến hoặc lùi), màn hình nạp sản lượng và LOT đã lưu của mã hàng trong ngày được chọn, kể cả khi chuyển mã hàng hoặc mở lại ứng dụng. Không tạo một `HistoryBatchId` mới chỉ vì đổi ngày. Nếu mã hàng chưa có kết quả trong ngày đó thì Tổng/PASS/FAIL về 0, LOT về `StartLotNo`; ví dụ mức bắt đầu 2000 thì PASS đầu là 2001. Đổi mức LOT bắt đầu trong cài đặt vẫn tạo đợt History mới nhưng không xóa hay che sản lượng đã kiểm tra của ngày đó.

Màn hình test kiểm tra đổi ngày bằng timer đồng hồ hiện có; START cũng kiểm tra trước khi cho chu kỳ reserve LOT. Kết quả tải thống kê của ngày/đợt cũ không được ghi đè đợt mới. Reservation chưa commit giữ nguyên chu kỳ hiện tại; chuyển ngày sau khi reservation được giải quyết.

Reset chỉ đổi cấu hình/bộ đếm hiển thị và phạm vi truy vấn. Không xóa hoặc sửa LOT, timestamp, sản lượng hay bản ghi lịch sử cũ. Các bản ghi sai LOT đã lưu ở phiên cũ được giữ nguyên để bảo toàn truy vết. History và export tiếp tục truy vấn các bản ghi cũ.
