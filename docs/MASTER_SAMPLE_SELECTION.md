# Lựa chọn mẫu Master đầu ca

Trong ProductionSettingsPage, bật **KIỂM TRA MẪU ĐẦU CA** theo mã hàng/model và nhập hai giá trị độc lập:

- **Số điểm sai dây**: số cạnh sai dây duy nhất cần phát hiện, ví dụ 2 hoặc 4. Hai hướng của cùng cạnh IO chỉ tính một điểm.
- **Số điểm đứt dây**: số quan hệ dây hở yêu cầu chính xác, mặc định 1. Ví dụ model 5 dây, yêu cầu đứt 1: chỉ mẫu 4/5 kết nối đúng được chấp nhận; 5/5 và 3/5 không đạt bài kiểm tra này.

Khi bật Master, **Mẫu đạt luôn bắt buộc**. Chọn một hoặc nhiều mẫu NG trong cài đặt theo mã hàng: Sai dây, Chập mạch, Tuột tuýt/đứt dây. Phần mềm giữ thứ tự sau và bỏ qua những mẫu NG không chọn:

1. Mẫu đạt.
2. Mẫu sai dây.
3. Mẫu chập mạch (ít nhất một cầu chập).
4. Mẫu tuột tuýt / đứt dây.

Sau mỗi mẫu phải xác nhận tháo hoàn toàn bằng các frame Production hợp lệ trước khi chuyển sang mẫu tiếp theo. Production chỉ mở sau khi đủ mẫu đạt và tất cả mẫu NG đã chọn, rồi tháo mẫu cuối. Không có thanh bốn bước riêng. Ô trạng thái và vùng chờ trên TestView hiển thị tên mẫu cùng số bước, ví dụ KIỂM TRA MẪU ĐẠT 1/2 → KIỂM TRA MẪU CHẬP MẠCH 2/2. Khi chọn đủ ba loại NG thì hiển thị 1/4 đến 4/4. Khi đạt chờ tháo, ô trạng thái thêm OK - THÁO MẪU. Hoàn tất chuỗi chuyển về LẮP SẢN PHẨM. Bật Master mà không chọn mẫu NG sẽ bị chặn khi lưu.

Mẫu tuột tuýt/đứt dây tự xác nhận khi số điểm đứt hiện tại đúng số điểm đã cấu hình, không cần nút xác nhận lắp mẫu. Điểm đứt được lấy từ snapshot Production hiện tại và đếm theo quan hệ nguồn–đích THT duy nhất; không cộng dồn qua các lần quét. Mẫu sai dây/chập mạch không được chấp nhận ở bước hở mạch. Mẫu phải còn ít nhất một kết nối đúng để phân biệt với không có sản phẩm.

Trong luồng Master, một cầu nối giữa các mạng THT có kết nối gốc vẫn nguyên vẹn được xác nhận là chập mạch; nếu quan hệ gốc bị thiếu thì là sai dây. Phân loại lỗi và xử lý Production hiện tại được giữ nguyên. Các dòng giải thích trên UI không dùng thay cho evidence điện. Master không cộng LOT/PASS/FAIL sản xuất và không dùng MARKING; giữ nhánh relay Master hiện có.

## Đầu ca

Ngày sản xuất bắt đầu lúc **07:00 giờ máy**. Mã hàng bật Master phải kiểm tra lại mẫu đạt và những mẫu NG đã chọn khi sang ngày sản xuất mới, đổi mã hàng hoặc mở lại phần mềm. Mốc 08:00 không bỏ qua Master chưa hoàn tất. Chu kỳ sản phẩm đang chạy được hoàn tất và xác nhận tháo trước khi chuyển sang Master. Mã hàng tắt Master bỏ qua cả chuỗi; giá trị số điểm sai dây bằng 0 vẫn tương đương tắt Master.

## Cấu hình và lịch sử

Số điểm sai dây dùng `MasterFaultRequiredCount` / `MasterFault.<model-key>`; số điểm đứt dây dùng `MasterOpenFaultRequiredCount` / `MasterOpenFault.<model-key>`. Lựa chọn mẫu NG dùng MasterSelectedFaultSamples / MasterSelected.<model-key>. Cấu hình cũ chưa có khóa này mặc định chọn đủ ba loại NG. Các khóa lựa chọn một loại Master cũ được giữ tương thích khi đọc/ghi CFG nhưng không quyết định chuỗi hiện tại.

Mỗi mẫu có CycleId và bản ghi lịch sử riêng. Mẫu đạt dùng `MASTER_GOOD`, ba mẫu NG dùng `MASTER_BAD`; `InspectionTrace` ghi ngày sản xuất, mã phiên chung cho cả chuỗi, loại mẫu, số điểm phát hiện/yêu cầu, tỷ lệ kết nối mẫu đứt và kết quả xác nhận. Tháo mẫu chưa đạt cũng ghi lần kiểm tra không đạt. Kết quả được commit SQLite trước khi mở JIG; lỗi lưu dữ liệu không mở Production. Không thay đổi schema, không sửa hoặc xóa lịch sử cũ.