# Lựa chọn mẫu Master đầu ca

Trong ProductionSettingsPage, bật **KIỂM TRA MẪU ĐẦU CA** dùng chung cho tất cả mã hàng và nhập hai giá trị độc lập:

- **Số điểm sai dây**: số cạnh sai dây duy nhất đang xuất hiện đồng thời, ví dụ 2 hoặc 4. Hai hướng của cùng cạnh IO chỉ tính một điểm. Lỗi mất ở frame hiện tại được bỏ khỏi bộ đếm/bảng; không cộng dồn lỗi đã biến mất.
- **Số điểm đứt dây**: số quan hệ dây hở yêu cầu chính xác, mặc định 1. Ví dụ model 5 dây, yêu cầu đứt 1: chỉ mẫu 4/5 kết nối đúng được chấp nhận; 5/5 và 3/5 không đạt bài kiểm tra này.

Khi bật Master, **Mẫu đạt luôn bắt buộc**. Chọn một hoặc nhiều mẫu NG trong cài đặt chung: Sai dây, Chập mạch, Tuột tuýt/đứt dây. Phần mềm giữ thứ tự sau và bỏ qua những mẫu NG không chọn:

1. Mẫu đạt.
2. Mẫu sai dây.
3. Mẫu chập mạch (ít nhất một cầu chập).
4. Mẫu tuột tuýt / đứt dây.

Sau mỗi mẫu phải xác nhận tháo hoàn toàn bằng các frame Production hợp lệ trước khi bắt đầu kiểm tra mẫu tiếp theo. Production chỉ mở sau khi đủ mẫu đạt và tất cả mẫu NG đã chọn, rồi tháo mẫu cuối. Không có thanh bốn bước riêng. Ô trạng thái và vùng chờ trên TestView chỉ hiển thị tên mẫu, ví dụ KIỂM TRA MẪU ĐẠT → KIỂM TRA MẪU CHẬP MẠCH. Khi mẫu đạt, ô trạng thái hiển thị ngay tên mẫu tiếp theo đã chọn; luồng kiểm tra vẫn chờ xác nhận tháo hết mẫu trước. Không thêm số bước, dòng OK - THÁO MẪU hoặc tỷ lệ kết nối vào lời nhắc mẫu. Mẫu cuối vẫn hiển thị tên mẫu cho đến khi tháo hoàn toàn, rồi chuyển về LẮP SẢN PHẨM. Bật Master mà không chọn mẫu NG sẽ bị chặn khi lưu.

Mẫu tuột tuýt/đứt dây tự xác nhận khi số điểm đứt hiện tại đúng số điểm đã cấu hình, không cần nút xác nhận lắp mẫu. Điểm đứt được lấy từ snapshot Production hiện tại và đếm theo quan hệ nguồn–đích THT duy nhất; không cộng dồn qua các lần quét. Mẫu sai dây/chập mạch không được chấp nhận ở bước hở mạch. Mẫu phải còn ít nhất một kết nối đúng để phân biệt với không có sản phẩm.

Trong luồng Master, một cầu nối giữa các mạng THT có kết nối gốc vẫn nguyên vẹn được xác nhận là chập mạch; nếu quan hệ gốc bị thiếu thì là sai dây. Phân loại lỗi và xử lý Production hiện tại được giữ nguyên. Các dòng giải thích trên UI không dùng thay cho evidence điện. Master không cộng LOT/PASS/FAIL sản xuất và không dùng MARKING. Mọi mẫu Master hoàn thành (đạt, sai dây, chập mạch, tuột tuýt/đứt dây) đều pulse relay JIG theo kiểu đấu máy sau khi commit kết quả thành công, kể cả khi tùy chọn mở JIG production đang tắt. Relay trở về OFF sau pulse. Không mở JIG nếu mẫu chưa đạt yêu cầu hoặc có lỗi lưu dữ liệu; sau khi tháo hết mẫu cuối mới mở production.

## Đầu ca

### Kiểm tra relay và Leak từ V2026.09.155

- Khóa chuyển bước trong toàn bộ thao tác mở JIG, đưa relay về OFF và khởi động lại scan. Sau đó mới đếm hai frame hoàn chỉnh khác nhau xác nhận tháo; còn chân active hoặc kết nối ngoài model thì giữ bước hiện tại.
- Mẫu đạt chạy Leak nếu mã hàng bật Leak. Các mẫu NG sai dây/chập/tuột tuýt chỉ kiểm tra lỗi điện D2XX, không yêu cầu PASS Leak hoặc dây RET thông ở các mẫu NG.
- Ô trạng thái hiển thị KHÔNG ĐẠT khi Master có lỗi thực thi/kiểm tra, và hiển thị thao tác Leak khi mẫu đạt chạy Leak hoặc yêu cầu tháo/lắp connector. Di chuột vào ô trạng thái để xem thông báo chi tiết.
- Kiểm tra bo và relay không thay thế chạy đủ chuỗi mẫu thật; làm theo `MASTER_VALIDATION_V2026.09.155.md` và giữ log máy sản xuất.

Ngày sản xuất bắt đầu lúc **07:00 giờ máy**. Khi bật Master, mọi mã hàng phải kiểm tra lại mẫu đạt và những mẫu NG đã chọn khi sang ngày sản xuất mới, đổi mã hàng hoặc mở lại phần mềm. Mốc 08:00 không bỏ qua Master chưa hoàn tất. Chu kỳ sản phẩm đang chạy được hoàn tất và xác nhận tháo trước khi chuyển sang Master. Khi tắt Master, mọi mã hàng bỏ qua cả chuỗi; giá trị số điểm sai dây bằng 0 vẫn tương đương tắt Master.

## Cấu hình và lịch sử

Số điểm sai dây dùng `MasterFaultRequiredCount`; số điểm đứt dây dùng `MasterOpenFaultRequiredCount`. Lựa chọn mẫu NG dùng `MasterSelectedFaultSamples`. Ba giá trị này dùng chung cho toàn bộ mã hàng. Các khóa theo mã hàng cũ `MasterFault.<model-key>`, `MasterOpenFault.<model-key>` và `MasterSelected.<model-key>` vẫn được giữ để đọc/ghi tương thích nhưng không quyết định yêu cầu Master. Tắt ở một trang cài đặt sẽ tắt cho tất cả mã hàng; bật lại sẽ yêu cầu kiểm tra mẫu cho mỗi mã hàng. Kết quả hoàn tất mẫu vẫn thuộc mã hàng đang kiểm tra, không dùng kết quả của mã hàng này để mở sản xuất mã hàng khác.

Sau khi đủ mẫu và xác nhận tháo mẫu cuối, runtime chuyển về `WaitingForProduct`, presentation về `Waiting` và hai vùng trạng thái hiển thị LẮP SẢN PHẨM.

Khi bàn test trống, nút TRỞ VỀ được phép hoạt động ở mọi bước; việc trở về không xác nhận hoàn tất Master. Chỉ chặn khi còn bằng chứng kết nối sản phẩm hoặc chưa xác nhận tháo hoàn toàn bằng frame thật. Trạng thái kiểm tra, relay hoặc Leak đang chạy không tự được coi là có sản phẩm; trở về hủy luồng hiện tại và giữ scan nền.

Mỗi mẫu có CycleId và bản ghi lịch sử riêng. Mẫu đạt dùng `MASTER_GOOD`, ba mẫu NG dùng `MASTER_BAD`; `InspectionTrace` ghi ngày sản xuất, mã phiên chung cho cả chuỗi, loại mẫu, số điểm phát hiện/yêu cầu, tỷ lệ kết nối mẫu đứt và kết quả xác nhận. Tháo mẫu chưa đạt cũng ghi lần kiểm tra không đạt. Kết quả được commit SQLite trước khi mở JIG; lỗi lưu dữ liệu không mở Production. Không thay đổi schema, không sửa hoặc xóa lịch sử cũ.
