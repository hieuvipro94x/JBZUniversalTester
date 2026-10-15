# BCW — V2026.09.238

Tích hợp trong UniversalTester WPF .NET 8 Windows x86. Mẫu nguồn đối chiếu: `C:\PHT20\BCW.txt`. Không tạo project mới và không thay thế hệ thống in tem hiện có.

## Dùng trên giao diện

Trong Cài đặt Production → TEM, chọn **BCW**, chọn mã hàng cần in, nhập mã xe 2 ký tự và chọn RH/LH. Mã sản phẩm lấy từ PartNumber của mã hàng đã chọn. Bảng BCW hiện PartNumber, ngày, mã Julian, serial dự kiến và QR. Có ô số lượng cho IN HÀNG LOẠT và nút IN LẠI BCW ĐÃ LƯU.

BCW ẩn các ô LOT in thử/LOT từ–đến vì số BCW được cấp tự động, độc lập LOT Production. Bốn loại tem cũ vẫn dùng các ô và pipeline cũ.

### Tự in khi test mã hàng THT

Chọn **Loại tem = BCW**, bật **BẬT IN TEM**, chọn cổng in và lưu Cài đặt. Sau đó chọn mã hàng `.tht` để chạy TestView như bình thường. Chỉ khi chu kỳ production đạt PASS cuối cùng và đã lưu kết quả vào History mới gửi tem tự động; FAIL, kiểm tra mẫu hoặc tắt in tự động không được tạo tem PASS sản xuất.

Dữ liệu lấy từ mô hình `.tht` đang test trong chu kỳ, không lấy từ combobox “Mã hàng cần in” dành cho in thủ công. Part Number lấy trường mã sản phẩm của THT, không lấy tên file. Các cột `BCW_VEHICLE_CODE`, `BCW_POSITION` trong bảng thông tin mã hàng được parser có sẵn đọc thành biến nhãn; ví dụ `09`, `LH`. Nếu THT không có hai cột đó, dùng mã xe/vị trí đã lưu trong Cài đặt. Không tự đoán mã xe từ ECO/ALC khi chưa có ánh xạ được xác nhận.

Snapshot mã hàng và thông tin BCW được chụp từ chu kỳ PASS. Đổi mã hàng/cài đặt trong khi yêu cầu đang chờ gửi không đổi dữ liệu của tem đã chụp. Ngày địa phương và serial mới được cấp dưới khóa gửi; QR cuối được lưu vào History trước khi gửi byte.

Mã xe và vị trí lưu trong CFG qua `BcwVehicleCode`, `BcwPosition`. Có thể khai báo dữ liệu sản phẩm qua các biến nhãn `BCW_VEHICLE_CODE`, `BCW_POSITION`; biến của sản phẩm được ưu tiên hơn cấu hình máy. Không tự đoán từ tên file, ECO hoặc ALC.

XEM TRƯỚC không giữ/chốt serial; QR trên preview là dự kiến, hình QR là sơ đồ theo renderer hiện có. Lúc thực sự gửi, tool cấp serial và lấy DateTime.Now địa phương trong khóa gửi của máy in. IN THỬ thực sự gửi nên sẽ ghi nhận serial BCW nếu gửi thành công; không tăng LOT Production hoặc sản lượng test. In nhiều tem cấp từng serial, dừng nếu có lỗi.

Ngày/serial dự kiến trên trang cập nhật khi thay loại tem, mã hàng, mã xe/vị trí hoặc sau in thử/in lại. Ngày của tem mới lúc gửi luôn lấy lại từ hệ thống, kể cả trang đã mở qua nửa đêm.

## QR

`YYDDD + MaXe(2) + R/L + serial(5)` = **13 ký tự**.

- 14/10/2026, xe 09, LH, serial 1: **2628709L00001**.
- 29/02/2028: tiền tố **28060**.
- 31/12/2026: tiền tố **26365**.
- RH tạo R; LH tạo L. Mã xe là hai ký tự chữ/số; serial 1..99999. Không cắt hoặc quay vòng khi hết dải.

QR và dòng chữ dưới QR dùng cùng một chuỗi, không tạo hai mã độc lập.

## EPL thực tế

Ví dụ với mã `OE366-33050`, xe 09, LH, serial 1, ngày 14/10/2026:

```text
N
Q120,24
R0,0
ON
D4
S2
b245,16,Q,h5,"2628709L00001"
A110,5,0,3,2,2,N,"LH"
A85,56,0,2,1,1,N,"OE366-33050"
A80,80,0,2,1,1,N,"2628709L00001"
ZB
P1
```

Mọi dòng kết thúc CRLF; P1 có CRLF cuối. Giữ tọa độ, font, độ phóng, lệnh QR, tốc độ và độ đậm của mẫu gốc. Thay V00 bằng PartNumber, V01 bằng RH/LH, V02 bằng QR trực tiếp trong lệnh. Không khai báo V03 hoặc truyền dòng dữ liệu giả. Không gửi FK, FS, FE hoặc FR: không xóa form khác, không ghi lại flash mỗi tem, không phụ thuộc form KS91 có sẵn. Mẫu này là EPL/EPL2, không chuyển ZPL.

Template nguồn chỉ có Q120 và không khai báo chiều rộng. Preview đặt mặc định 60 × 15 mm cho BCW; chiều cao Q120 tương ứng khoảng 15 mm ở 203 dpi. Cần đối chiếu kích thước giấy và DPI thực tế, vì layout máy in được giữ nguyên tọa độ theo mẫu.

PartNumber kiểm tra ASCII tối đa 15 ký tự theo biến V00 của mẫu gốc, không cho phép nháy hoặc xuống dòng làm hỏng lệnh EPL.

## Chỉnh mẫu BCW

Chọn BCW → CHỈNH TEM để mở trình chỉnh EPL có sẵn. Có thể đổi tọa độ, font, độ phóng chữ, vị trí và kích thước module QR. Giữ `{PART_NUMBER}`, `{BCW_POSITION}` và `{BCW_QR}`; QR và dòng chữ dưới QR phải dùng cùng `{BCW_QR}`. Mỗi serial chỉ dùng một lệnh P1. Không chấp nhận lệnh xóa/lưu/đọc form FK/FS/FE/FR.

Bấm ÁP DỤNG rồi LƯU CÀI ĐẶT. Mẫu riêng của BCW lưu trong `LabelTemplateBCWBase64`, không ghi đè mẫu TEM_TO/TEM_BE. KHÔI PHỤC MẶC ĐỊNH bỏ phần tùy chỉnh BCW. Xem trước, in thử, in hàng loạt và tem PASS production đều dùng bản BCW đang áp dụng khi chụp yêu cầu.

Yêu cầu đã chụp giữ nguyên mẫu trước đó dù sau này sửa/khôi phục Cài đặt. Khi cấp serial lúc gửi, chỉ điền lại ngày/QR/serial vào đúng mẫu đã chụp. In lại tiếp tục dùng nguyên payload đã lưu, không chuyển sang bố cục mới. Khôi phục tem chưa gửi cũng đọc được QR ở tọa độ đã chỉnh, không còn cố định b245,16.

## Lưu serial và lỗi in

Nhật ký riêng `Data\BCW-labels.db` lưu CycleId, phạm vi ngày/mã xe/vị trí, serial, trạng thái, snapshot yêu cầu và thông báo. Cùng ngày + mã xe + vị trí dùng chung serial, kể cả đổi PartNumber, vì PartNumber không nằm trong QR; điều này tránh trùng QR giữa hai mã hàng cùng nhóm. Ngày mới hoặc nhóm xe/vị trí khác bắt đầu 00001; quay lại ngày cũ đọc số đã lưu của ngày đó.

Serial được giữ trong transaction SQLite trước khi gửi, nhưng chỉ tiến sau khi transport báo gửi thành công. Khóa gửi hiện có và unique constraints ngăn hai request tự động nhận cùng serial. Cùng CycleId đã gửi được chặn gửi lặp tự động. Nhật ký BCW được kế thừa khi chuyển thư mục phiên bản, không ghi đè file đích đã tồn tại; sao chép bằng SQLite backup và kiểm tra integrity.

Gửi lỗi/mất kết nối hoặc trạng thái chưa rõ: không tiến serial, giữ snapshot và chặn serial mới **trong cùng nhóm** cho đến khi xử lý tem đang giữ. Điều này chỉ chặn in BCW của nhóm đó, không giữ LOT Production, không đổi PASS thành FAIL và không dừng vòng kiểm tra sản phẩm.

IN LẠI dùng đúng payload/QR đã lưu, kể cả khác ngày hoặc đã sửa cấu hình. UI hỏi xác nhận trước khi gửi lại, vì mất kết nối có thể đã gửi một phần. Không tự in lại sau restart. `Sending` còn sót sau crash cũng cần xử lý bằng snapshot đã lưu. Cổng/baud đích được lấy lại từ cấu hình hiện tại để có thể sửa cổng lỗi; dữ liệu tem không đổi.

Log phân biệt BCW_PREPARED, BCW_STATE và kết quả transport. `Sent`/PrintStatus.Printed có nghĩa đã gửi lệnh thành công theo cơ chế hiện có, **không phải ACK xác nhận máy in đã in vật lý**.

## Lịch sử và dữ liệu

Không đổi schema/version SQLite History. Trước gửi tem production BCW, snapshot QR và payload cuối được ghi vào các cột label có sẵn, chỉ đúng Id/CycleId và loại BCW. Kết quả in vẫn đi qua pipeline lịch sử hiện có. Không sửa topology THT, quét IO, bắt lỗi, relay, Leak hoặc Keysight.

In thử/hàng loạt/in lại tại Cài đặt được ghi trong nhật ký BCW, không tạo kết quả test giả và không sửa sản lượng. Nút in lại tem trong TestView vẫn dùng pipeline reprint History hiện có.

## File thêm/sửa

Thêm:
- `Labels/BCW.txt`: mẫu EPL riêng nhúng trong EXE.
- `Services/BcwLabelService.cs`: dữ liệu, Julian, validation, renderer và khôi phục tem chưa gửi.
- `Services/BcwSerialStore.cs`: nhật ký và quản lý serial riêng.
- `Tests/BcwLabelTests.cs`: hồi quy BCW.
- `docs/BCW_LABEL.md`: báo cáo và hướng dẫn này.

Sửa:
- `JBZUniversalTester.csproj`, `Version.props`: resource và V238.
- `Models/ProductionSettings.cs`, `Models/HistoryModels.cs`: cấu hình và snapshot BCW.
- `Services/BuiltInLabelTemplateStore.cs`, `LabelProfileResolver.cs`, `LabelTemplateProvider.cs`: nhận dạng/chọn mẫu BCW, giữ hành vi mẫu cũ.
- `Services/LabelPrintService.cs`: nhánh BCW trong khóa gửi hiện có.
- `Services/ProductionConfigService.cs`: đọc/ghi mã xe và vị trí.
- `Services/ProductionDataUpgradeService.cs`: kế thừa nhật ký BCW.
- `Services/TestHistoryStore.cs`, `ProductionPersistenceService.cs`: lưu snapshot vào cột có sẵn trước gửi.
- `ViewModels/TestViewModel.cs`: snapshot thực tế và reprint BCW.
- `Views/ProductionSettingsPage.xaml(.cs)`: selector, trường BCW, preview/in/batch/reprint.
- `Tests/Program.cs`: đăng ký kiểm thử và lệnh `--test-bcw`.

## Xác minh

Build Release: thành công, 0 warning, 0 error.

Kiểm thử BCW đạt: vector Julian/năm nhuận/ngày cuối năm/RH/LH, QR và chữ giống nhau, validation, không FK/FS, CFG round-trip, gửi thất bại không tăng serial, restart, retry/reprint cùng QR, ngày mới, gửi đồng thời, kế thừa phiên bản và SQLite integrity. Có kiểm thử bổ sung bằng file THT fixture qua parser thật, hàm chụp tem PASS production, gửi RAW giả lập và xác minh QR trong History; xác minh model/config đổi sau capture không đổi tem cũ. Bộ đầy đủ V238 đạt **85/89**; 4 lỗi cũ đã ghi nhận trong các kiểm thử learned topology, resistance selectors/layout, TestView status guards và fault debounce/jig. Cả 5 nhóm kiểm thử BCW và các kiểm thử tem cũ đều đạt.

Không chỉnh dữ liệu sản xuất thật khi phát triển. Integrity trên DB fixture History và nhật ký BCW đều `ok`, số dòng History giữ nguyên qua cập nhật snapshot.

Chưa có Zebra thật để xác minh: hỗ trợ EPL QR `Q,h5`, khả năng đọc QR sau in, vị trí in/độ rộng giấy/DPI, tốc độ/độ đậm, COM/RAW thực tế, rút cáp khi đang gửi và kiểm tra tem đã nhận trước in lại. Không tuyên bố kiểm thử file RAW là xác nhận in vật lý.
