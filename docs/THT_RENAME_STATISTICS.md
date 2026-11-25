# Khôi phục sản lượng khi đổi tên THT — V2026.09.475

- Tên file trong từng dòng `Tests.ThtFileName` và bộ đếm gốc được giữ nguyên.
- Bổ sung bảng `ThtFileIdentities` lưu liên kết tên mới với tên gốc; truy vấn cộng các tên thuộc cùng liên kết. Không gộp theo mã sản phẩm đơn thuần.
- Chỉ nhận diện tự động khi THT mới có cùng SHA256 và mã sản phẩm, nguồn cũ đã biến mất trong cùng thư mục còn truy cập được, và chỉ có một nhóm sản lượng phù hợp. Tên model nội bộ độc lập, nếu có, cũng phải trùng. Parser hiện tại lấy ModelName từ tên file nên không dùng tên này làm danh tính duy nhất.
- File nguồn vẫn tồn tại thì bản sao mới tiếp tục có sản lượng riêng. Nếu có nhiều nhóm phù hợp hoặc tên mới đã có sản lượng độc lập, không tự cộng/gộp; giữ nguyên dữ liệu và ghi log nhận diện.
- Hỗ trợ nhiều lần đổi tên và khởi động lại. Kết quả mới ghi đúng tên file đang dùng, truy vấn vẫn tiếp tục sản lượng cùng nhóm. PASS/FAIL theo ngày, tháng, tổng và LOT cuối theo ngày dùng cùng nhóm.
- THT cũ trước bản sửa có thể được nhận diện từ SHA256 và thông tin mã trong Models/Parts. Thiếu hash, sửa nội dung đồng thời với đổi tên, hoặc chuyển sang thư mục khác không được suy đoán tự động.
- Cấu hình máy và các cấu hình theo tên file trong CFG không được tự di chuyển bởi cơ chế thống kê này.
- Schema v9 → v10 chỉ thêm bảng/chỉ mục. Khởi tạo SQLite kiểm tra integrity, tạo backup, chạy transaction và xác minh số dòng Tests/TestFaults/ResistanceMeasurements/WaterProofMeasurements không giảm. Không sửa file THT và không xóa lịch sử.

Kiểm thử dùng THT thật trong thư mục tạm: A có 100 kết quả, đổi tên B vẫn 100; test thêm thành 101; CycleId lặp không tăng thêm; restart/đổi tên C vẫn 101; bản sao giữ số riêng; trường hợp mơ hồ không gộp. Kiểm tra migration v9 và snapshot đầy đủ lịch sử/đo lường không đổi, integrity_check=ok. Không dùng database sản xuất cho kiểm thử.
