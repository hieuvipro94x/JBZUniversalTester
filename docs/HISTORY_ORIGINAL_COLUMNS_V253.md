# History/export V2026.09.253

CSV và XLSX xuất theo đúng thứ tự 18 cột của mẫu `검사 20260112.Xls`:

`장착시간, 파일, 파트명, 파트번호, Eco, Nco, Alc, Lot, 진도, 결과, 합격, 검사기록, 바코드입력, 바코드출력, 측정, HtdrvName, 내용, 메모`

- Eco/Nco/Alc dùng các trường cùng tên được ThtModelParser đọc từ bảng PART của `.tht` và đã lưu trong History. Không đoán từ vị trí cột hoặc lấy lại file `.tht` mới để ghi đè thông tin lịch sử cũ. Nếu dữ liệu cũ không có giá trị, ô đó vẫn trống.
- Cột `Lot` luôn trống. Cột đếm PASS `합격` hiển thị đúng số `Tests.Lot` đã lưu cho sản phẩm PASS, không đánh số lại theo bộ lọc/trang, không lấy số thứ tự dòng thay cho số LOT. FAIL, Master và Leak Retest không tiêu thụ số đếm sản phẩm.
- `진도` hiển thị `1/1` cho kết quả đạt, `0/1` cho kết quả không đạt.
- Giữ nội dung nhật ký và barcode hiện hành; không đổi barcode thành lệnh EPL. Cột đo lường dùng tóm tắt điện trở đã lưu, cột nội dung dùng thông tin hiện có; ghi chú để trống vì không có trường ghi chú được lưu.
- XLSX vẫn là `.xlsx`, CSV vẫn CP949. Thời gian xuất được gộp trong một cột, vẫn dùng `TestStartedAt ?? Started`; không đổi timestamp lọc, thứ tự, keyset cursor hay fallback cho dòng legacy.
- Giao diện History hiển thị các trường tương ứng; giữ ngày/giờ tách riêng và loại kiểm tra để nhận biết Master/Leak Retest.

Không đổi schema, không migration mới, không đổi hoặc xóa database hiện có. Các thay đổi chỉ là cột trình bày/xuất. Bộ kiểm thử `dotnet run --project Tests -c Release -- --test-history-export` kiểm tra metadata THT, file CSV/XLSX thực tế, legacy timestamp, phân trang, tổng số, dữ liệu Test/child/metadata không đổi khi đọc/xuất, exactly-once CycleId, migration hiện hữu và SQLite integrity.
