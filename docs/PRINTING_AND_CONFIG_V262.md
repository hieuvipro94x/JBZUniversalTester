# In tự động và cấu hình từ V2026.09.262

## Cách vận hành

- In tự động luôn áp dụng cho sản phẩm sản xuất PASS của mọi mã hàng. Không còn checkbox BẬT IN TEM.
- Chọn COM máy in một lần. Loại tem và mẫu tem đã chỉnh vẫn được giữ như trước.
- Mã hàng dùng để in tự động lấy từ chu kỳ test vừa PASS. Danh sách mã hàng trong phần in thử chỉ dùng cho xem trước, in thử và in hàng loạt.
- PASS tạo snapshot lệnh máy in trước khi lưu giao dịch kết quả. Sau khi kết quả đã commit mới gửi COM trên worker. FAIL, Master và Probe không tự in tem sản xuất.
- Không có COM/máy in: vẫn giữ kết quả PASS và toàn bộ lệnh tem. Không mở popup lỗi COM, không chèn lỗi COM vào lịch sử test. Chi tiết kỹ thuật chỉ đi vào log hệ thống theo công tắc log hiện có.
- Cột 바코드출력 trong lịch sử và file xuất chứa nguyên lệnh đã lưu, không phụ thuộc trạng thái gửi. Bảng trên màn hình thu gọn thành một dòng; tooltip giữ đầy đủ lệnh. File Excel giữ xuống dòng trong nội dung ô, chiều cao dòng vẫn cố định.
- Gửi thành công không phải xác nhận tem đã in vật lý. Trạng thái giao dịch nội bộ vẫn phân biệt gửi/không gửi/chưa xác định; không tự gửi bù khi kết nối máy in trở lại.
- BCW cấp serial riêng cho từng snapshot PASS, kể cả khi offline. In lại dùng đúng snapshot/serial đã lưu. Nhật ký BCW cũ được sao lưu trước khi thêm cột phân biệt capture sản xuất; không xóa hoặc thay các bản ghi cũ.

## Phần mềm cần nạp gì từ CFG?

| Nhóm | Mục đích |
|---|---|
| D2XX | Số card, card bắt đầu, xác nhận quét và độ trễ USB |
| Kiểm tra mẫu | Bật/tắt yêu cầu Master qua cấu hình chung, loại mẫu và số lỗi cần xác nhận |
| Relay và xác nhận lỗi | Kiểu đấu relay, thời gian xung, bật marking/JIG, xác nhận sai dây/tuột tuýt |
| LOT từng mã | LOT hiện tại, LOT bắt đầu, ngày, mốc in hàng loạt và batch lịch sử |
| Leak | COM máy Leak, thông số giao tiếp và profile theo file mã hàng, kể cả profile đã tắt |
| Điện trở | Kênh, bật/tắt và MIN/MAX riêng theo mã hàng; cấu hình dự phòng cho mã chưa có profile |
| Máy in | COM, giao tiếp, loại tem, nội dung mẫu tem đã chỉnh và dữ liệu BCW khi cần |
| Giao diện và vận hành | Mã/part gần nhất, hiển thị connector/tiêu đề, cuộn bảng, log và chu kỳ bảo trì pin |
| Thông tin trạm | Tên/số thiết bị, công ty, dây chuyền và thư mục dữ liệu |

## Tinh gọn khi lưu

Không ghi lại `AutoPrintLabelOnPass`, các alias cũ `CardCount`, `WaterproofSerialPort`, `StampDelayMs`, các mục không có tác dụng vận hành `TemperatureTolerance`, `MinimumErrorLogValue`, `OversizeWaitSeconds`, `ShieldDelayMs`, và các bảng Master theo mã hàng đã được thay bằng cấu hình chung.

Các trường tem tùy chọn đang rỗng cũng không ghi ra file. Profile điện trở chỉ ghi R1 để đánh dấu profile tồn tại và các kênh có cấu hình khác mặc định. Khi đọc, phần mềm dựng lại đủ R1–R10; một profile toàn bộ kênh tắt vẫn giữ quyết định tắt riêng, không rơi về cấu hình chung. Giới hạn hoặc kênh tùy chỉnh vẫn lưu dù kênh đang tắt. Cấu hình cũ vẫn đọc được; `AutoPrintLabelOnPass=0` trong file cũ không tắt quy tắc PASS mới.

Không xóa profile điện trở/Leak chỉ vì tất cả kênh đang tắt: chúng ghi nhận quyết định tắt cho mã đó. Không xóa LOT hoặc mẫu tem đã chỉnh. Cấu trúc dữ liệu và vị trí file cấu hình vẫn được giữ; thay đổi này không chuyển sang ProgramData và không nhúng cấu hình vào EXE.

Lịch sử sản xuất cũ không bị viết lại. Dòng cũ có payload sẽ hiển thị payload; nếu không có thì dùng barcode đã lưu. Không thể tự khôi phục lệnh đầy đủ nếu bản cũ chưa từng lưu lệnh đó.
