# In tem qua COM hoặc Máy in Windows USB

Trong Cài đặt > TEM > Cổng in:

- **In qua COM**: danh sách là các cổng COM. Cổng đã lưu nhưng tạm mất vẫn được giữ.
- **In qua USB**: danh sách là các máy in đã cài trong Windows. Bấm QUÉT sau khi cài driver hoặc đổi tên máy in.
- Chọn đúng máy in/cổng, sau đó bấm **LƯU CÀI ĐẶT**. Thử kết nối khi chọn không tự ghi CFG hoặc tự in tem.

Kiểu kết nối dùng chung cho máy/bàn test, lưu trong CFG hiện có bằng `LabelPrinterCom` hoặc `LabelPrinterName`. Bộ chọn giữ hai đích loại trừ nhau. CFG cũ có cả hai trường tiếp tục ưu tiên COM; không đổi schema hay dữ liệu lịch sử.

USB sử dụng Windows spooler ở chế độ RAW, gửi nguyên payload EPL/ZPL của mẫu. Máy in phải hỗ trợ đúng ngôn ngữ; ví dụ PC43t cần ESim cho EPL. Driver Windows không tự dịch payload RAW. Mẫu tham chiếu form lưu trong máy vẫn cần form tương ứng. Không có bước tự cài driver hay tự đổi ngôn ngữ máy in.

Áp dụng cho in thử, in hàng loạt, in sau PASS, in lại và BCW. Job đã chốt giữ nguyên đích/payload; lượt in mới sử dụng cấu hình đã lưu. Xử lý gửi và liệt kê máy in chạy ngoài Dispatcher; COM/USB dùng cùng cổng tuần tự hóa tác vụ in.

Windows nhận job chỉ xác nhận đã gửi vào hàng đợi, không chứng minh tem đã ra giấy hoặc máy đang online. Lỗi driver/spooler không tự đổi cổng, không tự gửi lại; giao dịch PASS/LOT và snapshot tem vẫn được bảo toàn. BCW giữ chặn gửi trùng và serial đã cấp. Job RAW bị lỗi giữa chừng được yêu cầu hủy qua AbortPrinter, trạng thái tem vật lý vẫn có thể chưa xác định.

Kiểm thử dùng bộ gửi giả lập, CFG/SQLite tạm và giao diện WPF thật: payload/copies/các mẫu tem, first-print transaction, lỗi spooler, hủy, ưu tiên COM, không fallback, lưu/nạp cấu hình, khôi phục tên máy in thiếu, chuyển kiểu không tự lưu, liệt kê nền. Cần xác nhận in thật trên máy khách sau khi chọn driver và ngôn ngữ đúng.
