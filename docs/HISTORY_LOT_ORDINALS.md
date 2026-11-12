# STT lịch sử theo đợt LOT

Từ V2026.09.184, lịch sử Master và CSV/XLSX trình bày tên mẫu, ngày sản xuất, số điểm, kết quả xác nhận, mẫu yêu cầu và tỷ lệ kết nối bằng tiếng Hàn. Mã phiên kỹ thuật được ẩn khi trình bày; `InspectionTrace` gốc trong SQLite vẫn được giữ nguyên, kể cả các bản ghi cũ.

Từ V2026.09.183, STT đếm tất cả lượt test Production (PASS và FAIL), riêng theo mã hàng và đợt LOT. FAIL không hiển thị LOT và không tăng LOT sản xuất. Master đạt/lỗi và Leak retest vẫn có lịch sử riêng nhưng không chiếm STT hoặc cộng tổng PASS/FAIL Production.

Đổi LOTNO bắt đầu của mã hàng tạo đợt LOT mới; LOT tự đặt lại khi sang ngày mới cũng tạo đợt mới. Đổi mã hàng, đóng/mở phần mềm hoặc lưu cài đặt khác không tự tạo đợt mới. STT được tính trên toàn bộ lịch sử của đợt trước khi lọc hoặc phân trang; CSV/XLSX dùng cùng STT.

SQLite schema v8 chỉ thêm `Tests.ProductionBatchKey`, không sửa hoặc xóa các lượt test cũ. Trước migration tạo backup và kiểm tra integrity; migration kiểm tra số hàng các bảng lịch sử không giảm và kiểm tra integrity sau commit. Dữ liệu cũ chưa có thông tin đợt LOT giữ một nhóm tương thích riêng theo mã hàng; không suy đoán đợt LOT cũ từ LOT trên bản ghi FAIL. Các đợt mới được ghi rõ nên có thể đặt lại STT chính xác.
