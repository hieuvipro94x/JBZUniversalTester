# BÁO CÁO YÊU CẦU SỬA CHỮA `JBZUniversalTester_V12_9`
## TestView – Hiển thị lỗi thực tế – Lịch sử test – Config/Log tương thích phần mềm gốc – Giữ nguyên Probe/Card mở rộng

---

# 1. MỤC TIÊU CỦA BẢN SỬA V12.9

Sau khi đã có bản `JBZUniversalTester_V12_9`, tiếp tục sửa toàn bộ project theo các mục tiêu sau:

1. Chỉnh lại giao diện `TestView` cân đối, rõ ràng, không mất chữ và không mất viền.
2. Rút gọn khu thông tin sản phẩm để dành diện tích cho vùng kết quả test.
3. Ngày giờ phải lớn hơn, chữ đậm, nổi bật và dễ nhìn.
4. Đổi tên:
   ```text
   Số lỗi hở mạch
   ```
   thành:
   ```text
   Dây chưa kết nối
   ```
5. Màn hình chính phải hiển thị **đúng lỗi thực tế đang xảy ra**, chữ lớn và nổi bật.
6. Với lỗi đấu sai/chập, phải chỉ rõ:
   ```text
   chân nào → chân nào
   ```
   để người vận hành biết phải sửa ở đâu.
7. Lịch sử test phải ghi đúng lỗi thật đã xảy ra, không được ghi chung chung hoặc sai loại lỗi.
8. Cột `Loại lỗi` trong lịch sử phải dùng đúng tên lỗi tương ứng với TestEngine.
9. Khi khởi động, phần mềm dựng lại phải tạo các file/config/log theo hướng gần phần mềm gốc nhất có thể.
10. Không làm hỏng các chức năng đã sửa trước:
    - Probe/Test Pointer;
    - card mở rộng;
    - Production;
    - FTDI/D2XX;
    - THT;
    - Keysight;
    - relay;
    - PASS/FAIL;
    - LOT/statistics;
    - Master Sample;
    - SQLite history.

---

# 2. PHÂN TÍCH GIAO DIỆN TESTVIEW HIỆN TẠI TỪ ẢNH

Ảnh hiện tại cho thấy giao diện gồm 3 vùng chính:

```text
┌──────────────┬──────────────────────────────────────────────┬─────────────────────┐
│ LOT / lỗi    │ Model + thông tin sản phẩm + board          │ Master + thống kê   │
└──────────────┴──────────────────────────────────────────────┴─────────────────────┘
```

## 2.1. Các lỗi nhìn thấy trực tiếp

### A. Nhãn bị cắt chữ

Trong ảnh xuất hiện:

```text
Mã sản phẩ
Tên sản phẩ
Mã khách h
```

thay vì đầy đủ:

```text
Mã sản phẩm
Tên sản phẩm
Mã khách hàng
```

Đây là lỗi layout rõ ràng.

Không được giải quyết bằng cách:
- giảm font quá nhỏ;
- cắt text;
- dùng `TextTrimming` cho những label cố định này.

Phải sửa lại Grid/ColumnDefinition/MinWidth.

---

### B. Khu thông tin sản phẩm đang chiếm chiều ngang chưa hợp lý

Hiện tại các ô:

```text
Mã sản phẩm
Tên sản phẩm
Loại xe
Mã khách hàng
```

đang trải quá rộng trong khi một số giá trị ngắn.

Yêu cầu mới:

- các `TextBlock` label và các `TextBox` thông tin cần **ngắn và gọn hơn**;
- không để ô input kéo dài gần hết màn hình khi không cần;
- khoảng trống thu được dành cho vùng trạng thái/kết quả;
- các cột phải cân đối giữa bên trái và bên phải.

Không hard-code layout chỉ đúng với độ phân giải ảnh hiện tại.

---

### C. Ngày giờ quá nhỏ và chìm

Hiện:

```text
2026/08/08 13:09:36
```

nằm góc phải phía trên nhưng quá nhỏ.

Yêu cầu:

```text
FontWeight = Bold
FontSize lớn hơn hiện tại
```

và có thể dùng một vùng nền/Border nhẹ để nổi bật.

Ngày giờ phải dễ đọc từ vị trí người vận hành đứng trước máy.

---

### D. Khu thống kê bên phải cần giữ viền hoàn chỉnh

Ảnh cho thấy các ô:

```text
Tổng số
Số đạt
Số lỗi
Tỷ lệ đạt
```

nằm sát cạnh phải.

Phải bảo đảm:

- Border không bị mất;
- không clipping;
- không tràn khỏi `Grid`;
- không bị mất viền khi:
  - maximize;
  - restore;
  - thay DPI;
  - Windows scaling 100/125/150%;
  - độ phân giải màn hình khác.

Không dùng Width cứng gây tràn.

---

### E. Khu MASTER hiện quá lớn so với một số thông tin khác

Hiện ô vàng:

```text
MASTER ĐẠT
- ĐANG KIỂM
TRA
```

chiếm diện tích khá lớn và text bị xuống dòng mạnh.

Cần cân đối lại để:

- chữ không bị ngắt xấu;
- trạng thái vẫn nổi bật;
- không chiếm diện tích quá mức;
- vùng lỗi test chính vẫn có đủ diện tích.

---

# 3. BỐ CỤC TESTVIEW MONG MUỐN

Nên refactor TestView thành các vùng rõ ràng:

```text
┌──────────────────────────────────────────────────────────────────────────────────┐
│ MODEL / VERSION                                      NGÀY GIỜ                    │
├───────────────┬──────────────────────────────────────────┬────────────────────────┤
│ LOT/COUNTER   │ THÔNG TIN SẢN PHẨM                      │ MASTER / STATISTICS    │
├───────────────┴──────────────────────────────────────────┴────────────────────────┤
│                         TRẠNG THÁI TEST / LỖI                                      │
├──────────────────────────────────────────────────────────────────────────────────┤
│                         CẤU HÌNH DÂY / TEST RESULT                                │
├──────────────────────────────────────────────────────────────────────────────────┤
│                         PROBE STATE (song song)                                    │
└──────────────────────────────────────────────────────────────────────────────────┘
```

Không bắt buộc y nguyên sơ đồ trên nhưng phải giữ nguyên nguyên tắc:

- thông tin cố định gọn;
- trạng thái test/lỗi là vùng nổi bật nhất;
- Probe không che Production;
- card mở rộng hiển thị động;
- không có text nào bị clipping.

---

# 4. RÚT GỌN CÁC LABEL / Ô THÔNG TIN

Có thể chuẩn hóa label thành:

```text
Mã hàng
Sản phẩm
Loại xe
Mã KH
```

hoặc nếu giữ tên đầy đủ:

```text
Mã sản phẩm
Tên sản phẩm
Loại xe
Mã khách hàng
```

thì Grid phải đủ rộng để không bị cắt.

Khuyến nghị:

```text
Label column: Auto hoặc MinWidth đủ
Value column: *
```

Không dùng một width quá nhỏ kiểu:

```xml
<ColumnDefinition Width="70"/>
```

nếu label thực tế dài hơn.

Các ô value nên có:

```text
MinWidth
MaxWidth hợp lý
TextTrimming chỉ dùng cho giá trị động nếu cần
ToolTip = full value
```

---

# 5. NGÀY GIỜ PHẢI NỔI BẬT

Yêu cầu:

```text
2026/08/08 13:09:36
```

phải:

- chữ đậm;
- font lớn hơn;
- màu có độ tương phản tốt;
- không đặt quá sát mép;
- không bị co nhỏ theo layout.

Ví dụ định hướng:

```xml
FontSize="18"
FontWeight="Bold"
HorizontalAlignment="Right"
VerticalAlignment="Center"
```

Không bắt buộc đúng 18 nếu UI scale khác, nhưng phải nhìn rõ.

Có thể thêm:

```xml
<Border Padding="8,4">
```

để ngày giờ thành một vùng riêng.

---

# 6. ĐỔI TÊN `SỐ LỖI HỞ MẠCH`

Hiện tại:

```text
Số lỗi hở mạch
```

phải đổi thành:

```text
Dây chưa kết nối
```

Nếu đây là counter thì UI có thể hiển thị:

```text
Dây chưa kết nối    4
```

Không chỉ đổi XAML text.

Phải kiểm tra toàn project:

```text
OpenCircuit
OpenWire
OpenCount
OpenErrorCount
DisconnectedWire
```

để bảo đảm:

- label UI;
- ViewModel property;
- history;
- export;
- report;
- statistics;

đều dùng thuật ngữ thống nhất.

---

# 7. CHUẨN HÓA TÊN LỖI TRONG TOÀN HỆ THỐNG

Cần tạo một bộ tên lỗi thống nhất, ví dụ:

```text
PASS
DÂY CHƯA KẾT NỐI
ĐẤU SAI
CHẬP MẠCH
ĐIỆN TRỞ KHÔNG ĐẠT
LỖI MODEL/THT
LỖI THIẾT BỊ ĐO
LỖI BO MẠCH / GIAO TIẾP
```

Nhưng phải phân biệt:

```text
PRODUCT FAULT
```

với:

```text
SYSTEM/DEVICE ERROR
```

Ví dụ:

```text
Keysight mất kết nối
FTDI disconnect
file THT lỗi
```

không được tự động tính là `Số lỗi sản phẩm`.

---

# 8. MÀN HÌNH CHÍNH PHẢI HIỂN THỊ LỖI TO, RÕ, ĐÚNG LỖI THỰC TẾ

Đây là yêu cầu quan trọng nhất của phần giao diện Production.

Khi PASS:

```text
PASS
```

hiển thị lớn, rõ.

Khi lỗi dây chưa kết nối:

```text
DÂY CHƯA KẾT NỐI
IO 11 – C05/Pin 3
```

hoặc theo metadata THT.

Khi đấu sai:

```text
ĐẤU SAI
IO 11 → IO 24
```

Nếu có connector/pin:

```text
ĐẤU SAI
C05-PIN3 / IO11
→
C08-PIN2 / IO24
```

Khi chập:

```text
CHẬP MẠCH
IO 11 ↔ IO 24
```

Nếu nhiều chân cùng chập:

```text
CHẬP MẠCH
IO 11 ↔ IO 24 ↔ IO 47
```

Không được chỉ hiện:

```text
FAIL
```

hoặc:

```text
Đấu sai
```

mà không chỉ ra chân.

---

# 9. PHẢI HIỂN THỊ ĐÚNG CHÂN NGUỒN → CHÂN THỰC TẾ

Đối với `Wrong Wiring`, phải lưu và hiển thị tối thiểu:

```text
ExpectedSourceIo
ExpectedTargetIo
ActualSourceIo
ActualTargetIo
```

hoặc cấu trúc tương đương.

Ví dụ THT yêu cầu:

```text
IO11 ↔ IO18
```

nhưng board thực tế nhận:

```text
IO11 ↔ IO24
```

UI phải hiển thị:

```text
ĐẤU SAI

Mong đợi:
IO11 → IO18

Thực tế:
IO11 → IO24
```

Nếu THT có metadata:

```text
IO11 / C01-Pin3 / BG21
IO18 / C05-Pin6 / BG21
```

thì ưu tiên hiển thị cả:

```text
IO + Connector + Pin + Wire
```

để công nhân xử lý nhanh.

---

# 10. LỖI OPEN / DÂY CHƯA KẾT NỐI PHẢI HIỂN THỊ CHÍNH XÁC

Nếu THT yêu cầu:

```text
IO11 ↔ IO18
```

nhưng chưa có kết nối:

UI không chỉ hiện:

```text
Dây chưa kết nối = 1
```

mà vùng lỗi chính phải hiện:

```text
DÂY CHƯA KẾT NỐI
IO11 ↔ IO18
```

hoặc:

```text
C01-Pin3 ↔ C05-Pin6
Wire BG21
```

Nếu có nhiều open:

```text
DÂY CHƯA KẾT NỐI: 4
```

và danh sách chi tiết bên dưới.

---

# 11. LỖI SHORT / CHẬP PHẢI HIỂN THỊ ĐÚNG NHÓM CHÂN

Nếu board phát hiện một network ngoài mong đợi:

```text
IO11 ↔ IO24
```

phải hiển thị:

```text
CHẬP MẠCH
IO11 ↔ IO24
```

Nếu là nhiều chân:

```text
CHẬP MẠCH
IO11 ↔ IO24 ↔ IO47
```

Không được làm mất nguồn/target do dictionary overwrite.

---

# 12. KHÔNG ĐƯỢC NHẦM PROBE THÀNH SHORT/WRONG WIRING

Giữ nguyên yêu cầu đã chốt trước:

```text
Probe
```

phải đi qua `ProbeDecoder` riêng.

Không được đưa Probe vào:

```text
ProductionFrameDecoder
TestEngine
FaultEngine
Relay
Statistics
History product FAIL
```

Nếu Probe chạm IO7 thì không được sinh lỗi giả:

```text
IO45 → IO7
IO46 → IO7
IO47 → IO7
...
```

---

# 13. VÙNG HIỂN THỊ LỖI NÊN LÀ MỘT COMPONENT RIÊNG

Nên tạo:

```text
ActiveFaultViewModel
```

hoặc model tương đương:

```csharp
FaultType
Title
PrimaryMessage
ExpectedConnection
ActualConnection
SourceIo
TargetIo
RelatedIos
Connector
Pin
Wire
Color
Severity
Timestamp
```

UI chỉ bind vào object này.

Không để XAML tự suy luận lỗi từ nhiều property rời rạc.

---

# 14. CỘT `LOẠI LỖI` TRONG LỊCH SỬ TEST PHẢI ĐÚNG

Hiện lịch sử test phải được audit toàn bộ.

Không được ghi chung:

```text
FAIL
```

cho mọi lỗi.

Cột:

```text
Loại lỗi
```

phải có đúng tên:

```text
DÂY CHƯA KẾT NỐI
ĐẤU SAI
CHẬP MẠCH
ĐIỆN TRỞ KHÔNG ĐẠT
```

v.v.

Nếu một sản phẩm có nhiều lỗi, cần quy định rõ:

### Cách 1 – một result có nhiều fault detail

```text
Result = FAIL
PrimaryFault = ĐẤU SAI
FaultCount = 3
```

và detail table lưu 3 lỗi.

### Cách 2 – history detail JSON

Lưu danh sách chi tiết nhưng UI vẫn hiển thị đúng.

Không được chỉ lưu text đã render trên UI.

---

# 15. HISTORY PHẢI LƯU ĐỦ CHI TIẾT ĐỂ ĐỌC LẠI ĐÚNG

Một fault record nên có tối thiểu:

```text
TestId
Timestamp
Model
PartNumber
LotNo

Result
FaultType
FaultCode

ExpectedSourceIo
ExpectedTargetIo

ActualSourceIo
ActualTargetIo

ConnectorFrom
PinFrom
ConnectorTo
PinTo

WireName
WireColor

MeasuredResistance
ResistanceMin
ResistanceMax
```

Không bắt buộc mọi trường luôn có giá trị.

Ví dụ lỗi resistance:

```text
MeasuredResistance
Min
Max
```

mới có ý nghĩa.

---

# 16. CẦN MỘT `FaultType` DUY NHẤT, KHÔNG DÙNG TEXT RỜI RẠC

Nên định nghĩa enum:

```csharp
public enum ProductFaultType
{
    None,
    OpenCircuit,
    WrongWiring,
    ShortCircuit,
    ResistanceOutOfRange
}
```

Tên tiếng Việt hiển thị qua một converter/service duy nhất:

```text
OpenCircuit          → Dây chưa kết nối
WrongWiring          → Đấu sai
ShortCircuit         → Chập mạch
ResistanceOutOfRange → Điện trở không đạt
```

History và TestView cùng dùng một nguồn mapping.

Không viết string khác nhau ở nhiều nơi kiểu:

```text
"Hở mạch"
"HỞ"
"Open"
"Open Circuit"
"Dây chưa kết nối"
```

---

# 17. THỨ TỰ ƯU TIÊN HIỂN THỊ LỖI

Nếu nhiều lỗi cùng lúc, cần quy định deterministic.

Khuyến nghị:

```text
1. CHẬP MẠCH
2. ĐẤU SAI
3. DÂY CHƯA KẾT NỐI
4. ĐIỆN TRỞ KHÔNG ĐẠT
```

Nhưng phải giữ toàn bộ danh sách lỗi.

UI chính:

```text
Primary Fault
```

Danh sách bên dưới:

```text
All Active Faults
```

Không được mất các lỗi phụ.

---

# 18. LỖI MỚI PHẢI ĐƯỢC ĐƯA LÊN ĐẦU

Trong danh sách fault:

```text
fault mới / fault đang active
```

phải ở trên cùng.

Các dòng lỗi có thể:
- chữ đậm;
- font lớn hơn;
- border rõ;
- màu theo severity.

Không được để người vận hành cuộn xuống mới thấy lỗi hiện tại.

---

# 19. LỊCH SỬ TEST PHẢI PHẢN ÁNH ĐÚNG KẾT QUẢ THỰC TẾ

Phải audit đường ghi history.

Tuyệt đối không để:

```text
UI hiển thị CHẬP MẠCH
History ghi ĐẤU SAI
```

hoặc:

```text
UI = DÂY CHƯA KẾT NỐI
History = FAIL
```

một cách mơ hồ.

Nên có một object result duy nhất:

```csharp
CompletedTestResult
```

được tạo đúng một lần.

Sau đó:

```text
CompletedTestResult
   ├─ UI
   ├─ History
   ├─ Statistics
   ├─ LOT
   ├─ Label
   └─ Export
```

Không để mỗi subsystem tự tính lại loại lỗi.

---

# 20. CHỈ CÓ MỘT ĐƯỜNG `COMPLETE TEST`

Đây là yêu cầu quan trọng để tránh:
- duplicate history;
- LOT tăng 2 lần;
- statistics sai;
- Loại lỗi khác UI.

Nên centralize:

```csharp
CompleteTest(CompletedTestResult result)
```

và từ đó mới:

```text
save history
update statistics
update LOT
print label
update UI
```

---

# 21. CONFIG/LOG KHI KHỞI ĐỘNG PHẢI GIỐNG PHẦN MỀM GỐC NHẤT

Phần mềm dựng lại hiện dùng:
- JSON config;
- compatibility CFG;
- SQLite history.

Không bắt buộc bỏ kiến trúc mới.

Nhưng khi khởi động cần tạo các file compatibility/config/log theo hướng giống phần mềm gốc nhất có thể.

Phải dựa trên trace/phân tích phần mềm gốc để xác định chính xác:
- tên file;
- vị trí;
- encoding;
- thời điểm tạo;
- cách ghi.

---

# 22. FILE CONFIG COMPATIBILITY

Nên giữ backend hiện đại:

```text
appsettings.json
production.settings.json
```

nhưng đồng thời sinh file compatibility kiểu software gốc.

Ví dụ project hiện đã có:

```text
UniversalTester.cfg
```

Cần audit xem có nên sinh tên theo executable/model compatibility.

Nếu cần mô phỏng gốc hơn, có thể tạo file dạng:

```text
Htdrv3-KETQ2000.cfg
```

hoặc tên tương ứng với app, nhưng **chỉ sau khi xác nhận format và mục đích từ trace/source**.

Không tự tạo file tên giống gốc nếu không dùng.

Mục tiêu là:
- dễ đối chiếu;
- dễ support;
- cấu trúc startup gần original;
- không phá config hiện đại.

---

# 23. CONFIG PHẢI ĐƯỢC TẠO TỰ ĐỘNG NẾU CHƯA CÓ

Startup flow mong muốn:

```text
Application Start
   ↓
Resolve app data path
   ↓
Ensure config directory
   ↓
Ensure appsettings.json
   ↓
Ensure production.settings.json
   ↓
Ensure compatibility CFG
   ↓
Ensure log directories
   ↓
Ensure history database
   ↓
Load model
   ↓
Connect board
```

Nếu file chưa có:
- sinh default hợp lệ;
- log lại việc tạo mới.

Nếu file hỏng:
- không crash im lặng;
- backup file lỗi;
- tạo default;
- ghi error log.

---

# 24. LOG PHẢI ĐƯỢC TẠO NGAY TỪ STARTUP

Phần mềm phải có log ngay từ lúc khởi động.

Khuyến nghị:

```text
Logs/
  Application/
  Board/
  Test/
  Error/
```

Có thể tổ chức theo ngày:

```text
Logs/2026/08/08/
```

hoặc:

```text
Logs/Application_20260808.log
Logs/Test_20260808.log
Logs/Error_20260808.log
```

Phải chọn một chuẩn nhất quán.

Nếu muốn giống phần mềm gốc, cần ưu tiên cấu trúc/path đã quan sát được từ original.

---

# 25. LOG NÊN GHI NHỮNG GÌ

## Application log

```text
startup
version
machine
config load
model load
service initialization
shutdown
```

## Board log

```text
FTDI enumerate
serial
VID/PID
open
configuration
start scan
stop scan
mode switch
disconnect
exception
```

## Test log

```text
test start
model
lot
active card count
fault
PASS
FAIL
resistance
relay
product removed
```

## Error log

```text
exception
stack trace
device error
file error
database error
```

---

# 26. LOG TEST PHẢI GHI ĐÚNG LỖI GIỐNG UI/HISTORY

Ví dụ:

```text
[13:10:21.152] FAIL WRONG_WIRING
Expected: IO11 -> IO18
Actual:   IO11 -> IO24
Model: M030066701S-CL4
Lot: 2000
```

Open:

```text
[13:10:42.503] FAIL OPEN
IO11 <-> IO18
Wire: BG21
```

Short:

```text
[13:11:02.714] FAIL SHORT
IO11 <-> IO24 <-> IO47
```

Không được chỉ log:

```text
FAIL
```

---

# 27. GIỮ LOG NHẸ, KHÔNG LÀM CHẬM SCAN

Không log mỗi raw RX frame ra disk trong Production bình thường nếu tốc độ cao.

Cần có level:

```text
Normal
Diagnostic
ProtocolTrace
```

Normal:
- chỉ event quan trọng.

Diagnostic:
- nhiều chi tiết hơn.

ProtocolTrace:
- TX/RX raw, chỉ bật khi debug.

Writer nên:
- async/background;
- queue;
- batch flush;
- không block FTDI scan thread.

---

# 28. TESTVIEW – VÙNG BOARD STATUS CŨ

Ảnh hiện đang hiện:

```text
Bo: FT245R USB FIFO [A90764PH] - ĐÃ KẾT NỐI
```

Đây không cần là vùng nổi bật nhất.

Có thể thu gọn thành:

```text
BO: A90764PH • KẾT NỐI
```

hoặc chỉ một indicator nhỏ.

Phần diện tích chính nên dành cho:
- lỗi;
- status test;
- Probe;
- active card.

---

# 29. CARD MỞ RỘNG PHẢI TIẾP TỤC HOẠT ĐỘNG ĐÚNG

Giữ nguyên yêu cầu V12.9 đã chốt:

```text
Settings
   ↓
BoardCapacity
   ↓
START_SCAN
   ↓
BoardAddressMapper
   ↓
Production / Probe
   ↓
TestView ActiveCards
```

Không được hard-code:

```text
64
128
```

nếu board capacity thực tế lớn hơn.

Một card vật lý hiện được xác định ở mức yêu cầu là:

```text
32 IO
```

nhưng phải tiếp tục xác minh:
- physical card;
- expansion module;
- scan card;
- START_SCAN `xx`.

Không được tự suy đoán.

---

# 30. TESTVIEW PHẢI HIỂN THỊ ACTIVE CARD ĐỘNG

Khi Settings bật card:

```text
Card 1
Card 2
Card 3
...
```

TestView phải cập nhật.

Probe phải hoạt động được trên toàn bộ card active.

Ví dụ boundary phải test:

```text
IO32
IO33

IO64
IO65
```

để tránh off-by-one.

---

# 31. PROBE HIỂN THỊ SONG SONG VỚI KẾT QUẢ TEST

Giữ nguyên:

Khi Probe chưa chạm:

```text
Sẵn sàng dò chân
```

Khi chạm IO không map:

```text
ĐANG DÒ: IO (24)
```

Mapped:

```text
ĐANG DÒ:
IO (11) | C05 | PIN 3 | BG21 | ĐEN
```

Rút Probe:

```text
chỉ dòng Probe mất
```

Không reset Production UI.

---

# 32. RESPONSIVE LAYOUT / DPI

TestView phải test ở:

```text
100%
125%
150%
```

Windows display scaling.

Và các resolution phổ biến.

Phải bảo đảm:

```text
không mất chữ
không mất border
không overlap
không clip
```

Nên sử dụng:
- `Grid`;
- `Auto`;
- `*`;
- `MinWidth`;
- `SharedSizeGroup`;
- `Viewbox` chỉ khi thực sự cần;
- tránh absolute Canvas positioning.

---

# 33. KHÔNG DÙNG FONT QUÁ NHỎ ĐỂ CHỮ VỪA Ô

Không được sửa lỗi clipping bằng:

```text
FontSize 9
```

Mục tiêu là:
- chữ rõ hơn;
- giao diện chuyên nghiệp hơn;
- thao tác tại máy dễ hơn.

Label có thể 13–15.
Dữ liệu quan trọng 15–18.
Fault chính 24–40 tùy không gian.

Đây chỉ là định hướng, phải cân bằng thực tế.

---

# 34. MÀU TRẠNG THÁI

Nên thống nhất:

```text
PASS             → xanh
Dây chưa kết nối → đỏ/cam
Đấu sai           → đỏ
Chập mạch         → đỏ đậm
Đang kiểm tra     → vàng/xanh dương
Probe             → màu riêng không trùng fault
System error      → màu riêng
```

Không chỉ dùng màu; luôn có text rõ ràng.

---

# 35. TEST CASE UI BẮT BUỘC

## Test UI-1

Ảnh hiện tại ở đúng resolution đang dùng.

Expected:
- `Mã sản phẩm` không mất chữ;
- `Tên sản phẩm` không mất chữ;
- `Mã khách hàng` không mất chữ;
- viền phải hoàn chỉnh.

## Test UI-2

Windows scaling 125%.

Expected:
- không clipping.

## Test UI-3

Resize nhỏ hơn.

Expected:
- layout co hợp lý;
- không mất border thống kê.

## Test UI-4

Ngày giờ:

```text
2026/08/08 13:09:36
```

phải nổi bật và đậm.

## Test UI-5

MASTER text dài.

Expected:
- không bị ngắt dòng xấu;
- không chèn lên border.

---

# 36. TEST CASE FAULT BẮT BUỘC

## Fault-1 – Open

THT:

```text
IO11 ↔ IO18
```

thực tế không kết nối.

Expected UI:

```text
DÂY CHƯA KẾT NỐI
IO11 ↔ IO18
```

History:

```text
Loại lỗi = Dây chưa kết nối
```

---

## Fault-2 – Wrong Wiring

THT:

```text
IO11 ↔ IO18
```

Actual:

```text
IO11 ↔ IO24
```

Expected:

```text
ĐẤU SAI
Mong đợi: IO11 → IO18
Thực tế:  IO11 → IO24
```

History:

```text
Loại lỗi = Đấu sai
ExpectedTargetIo = 18
ActualTargetIo = 24
```

---

## Fault-3 – Short

Actual:

```text
IO11 ↔ IO24
```

ngoài expected network.

Expected:

```text
CHẬP MẠCH
IO11 ↔ IO24
```

History:

```text
Loại lỗi = Chập mạch
```

---

## Fault-4 – Resistance

Expected:

```text
100 Ω ≤ R ≤ 110 Ω
```

Measured:

```text
125 Ω
```

UI:

```text
ĐIỆN TRỞ KHÔNG ĐẠT
125 Ω
Giới hạn: 100–110 Ω
```

History phải ghi measured/min/max.

---

# 37. TEST CASE HISTORY BẮT BUỘC

Sau mỗi lỗi trên:

1. mở History;
2. tìm đúng TestId;
3. kiểm tra:
   - Result;
   - Loại lỗi;
   - IO;
   - expected;
   - actual;
   - timestamp;
   - model;
   - lot.

Không được khác với màn hình Production.

---

# 38. TEST CASE STARTUP CONFIG/LOG

## Startup-1

Xóa folder config/log của app test.

Khởi động.

Expected:
- tự tạo directory;
- tự tạo default config;
- tạo log;
- không crash.

## Startup-2

Config hợp lệ.

Expected:
- load;
- không overwrite giá trị người dùng.

## Startup-3

Config hỏng.

Expected:
- backup file lỗi;
- tạo default;
- ghi error log;
- UI báo lỗi rõ nếu cần.

## Startup-4

Không có history DB.

Expected:
- tự tạo SQLite schema;
- không crash.

---

# 39. PHẢI AUDIT TOÀN BỘ CÁC CHỖ GHI `FAIL`

Search project:

```text
FAIL
Fault
ErrorType
FailureReason
OpenCircuit
WrongWiring
ShortCircuit
ResistanceFail
```

Phải xác định mọi chỗ:
- tạo lỗi;
- đổi tên lỗi;
- ghi DB;
- render UI;
- export Excel/CSV;
- statistics.

Không được còn các đường code khác nhau tạo tên lỗi không đồng bộ.

---

# 40. PHẢI AUDIT TOÀN BỘ LAYOUT TEXT CÓ NGUY CƠ BỊ CẮT

Search XAML:

```text
Width="
MaxWidth
TextTrimming
ClipToBounds
ColumnDefinition
RowDefinition
```

Kiểm tra các label:
- Mã hàng;
- Mã sản phẩm;
- Tên sản phẩm;
- Loại xe;
- Mã khách hàng;
- LOTNO;
- counters;
- master;
- statistics;
- board state;
- date/time.

---

# 41. KHÔNG ĐƯỢC PHÁ CÁC SỬA CHỮA TRƯỚC

Sau refactor phải regression test:

```text
Probe single IO
Probe multiple IO
Probe release
Expansion card IO
Production open
Production wrong wiring
Production short
PASS
Resistance
Master Sample
LOT
History
Relay
Settings
```

---

# 42. DANH SÁCH FILE/CLASS DỰ KIẾN CẦN KIỂM TRA

Tên thực tế có thể khác, nhưng phải tìm tương đương:

```text
Views/TestView.xaml
Views/TestView.xaml.cs

ViewModels/TestViewModel.cs
ViewModels/MainViewModel.cs

Services/TestEngine.cs
Services/BoardIoDecoder.cs
Services/D2xxBoardTransport.cs
Services/ThtModelParser.cs

Services/TestHistoryStore.cs
Services/ResultStore.cs
Services/ProductionStatisticsStore.cs

Services/AppSettings.cs
Services/ProductionConfigService.cs
Services/ErrorLogService.cs

Models/TestResult.cs
Models/FaultResult.cs
Models/WireResult.cs
Models/PinMapping.cs
```

Nếu chưa có central fault model thì phải tạo.

---

# 43. KẾT QUẢ CẦN TRẢ SAU KHI SỬA PROJECT

Không chỉ đưa code mẫu.

Phải:

1. đọc toàn project;
2. sửa trực tiếp project;
3. compile;
4. sửa lỗi build;
5. chạy static/unit tests nếu có;
6. kiểm tra XAML;
7. kiểm tra nullability;
8. kiểm tra thread/async;
9. regression Probe/Card;
10. đóng gói ZIP hoàn chỉnh.

---

# 44. BÁO CÁO SAU SỬA PHẢI NÊU RÕ

```text
1. Những file đã sửa.
2. Nguyên nhân label bị cắt.
3. Cách layout TestView mới.
4. Cách ngày giờ mới hiển thị.
5. Cách đổi "Số lỗi hở mạch" → "Dây chưa kết nối".
6. Fault model mới.
7. Cách xác định Open.
8. Cách xác định Wrong Wiring.
9. Cách xác định Short.
10. Cách lấy source/target/expected/actual.
11. Cách màn hình chính hiển thị fault.
12. Cách History ghi fault.
13. Cách đồng bộ FaultType giữa UI/History/Log.
14. Config nào được tạo lúc startup.
15. Log nào được tạo lúc startup.
16. Folder/path của config/log.
17. Cơ chế log async.
18. Regression Probe.
19. Regression card mở rộng.
20. Kết quả test DPI/layout.
```

---

# 45. TIÊU CHÍ NGHIỆM THU GIAO DIỆN

Bản sửa chỉ được coi là đạt nếu:

- không còn label nào bị mất chữ;
- không còn Border bị mất;
- layout cân đối;
- các ô `Mã hàng / Mã sản phẩm / Loại xe` gọn hơn;
- ngày giờ to, đậm, nổi bật;
- `Dây chưa kết nối` thay cho `Số lỗi hở mạch`;
- vùng lỗi chính lớn và rõ;
- không chỉ hiện chữ `FAIL`;
- Wrong Wiring chỉ rõ chân sai;
- Short chỉ rõ các IO bị chập;
- Open chỉ rõ cặp dây/chân chưa nối.

---

# 46. TIÊU CHÍ NGHIỆM THU HISTORY

History chỉ đạt khi cùng một lỗi:

```text
TestView
Log
History
Export
```

có cùng:

```text
FaultType
Fault name
Expected connection
Actual connection
IO details
```

Không có trường hợp UI và History ghi khác nhau.

---

# 47. TIÊU CHÍ NGHIỆM THU CONFIG/LOG

Startup phải:

```text
không cần người dùng tự tạo file
```

và tự bảo đảm:

```text
config exists
log folder exists
error log works
history DB exists
```

Cấu trúc file nên gần original nhất nhưng:
- không được phá kiến trúc .NET/JSON/SQLite mới;
- không được tạo file compatibility vô nghĩa;
- mọi file phải có lý do sử dụng.

---

# 48. TIÊU CHÍ NGHIỆM THU PROBE/CARD SAU KHI SỬA UI

Mọi card active trong Settings phải:

```text
SCANNED
PRODUCTION-CAPABLE
PROBE-CAPABLE
```

Probe:
- không tạo fault;
- không relay;
- không tăng LOT;
- không tăng FAIL;
- hiển thị song song;
- release chỉ xóa Probe state.

---

# 49. TÊN BUILD ĐỀ XUẤT

Có thể dùng:

```text
JBZUniversalTester_V12_9_1_TESTVIEW_FAULT_HISTORY_LOG
```

hoặc version mới hơn nếu project đã có versioning khác.

---

# 50. MỤC TIÊU CUỐI CÙNG

Người vận hành nhìn màn hình phải biết ngay:

```text
Máy đang test model nào?
Sản phẩm nào?
LOT nào?
Đang PASS hay FAIL?
Nếu FAIL là lỗi gì?
Chân nào bị lỗi?
Đáng lẽ phải nối chân nào?
Thực tế đang nối vào chân nào?
Có bao nhiêu dây chưa kết nối?
Card nào đang active?
Probe đang chạm chân nào?
```

Không cần mở màn hình phụ mới biết nguyên nhân.

Ví dụ lỗi cuối cùng phải rõ như:

```text
════════════════════════════════════
              ĐẤU SAI
════════════════════════════════════

Mong đợi:
IO 11 / C05-PIN3
        →
IO 18 / C08-PIN2

Thực tế:
IO 11 / C05-PIN3
        →
IO 24 / C09-PIN7

Wire: BG21
════════════════════════════════════
```

Hoặc:

```text
════════════════════════════════════
        DÂY CHƯA KẾT NỐI
════════════════════════════════════

IO 11 / C05-PIN3
        ↔
IO 18 / C08-PIN2

Wire: BG21
════════════════════════════════════
```

Đây là tiêu chí chính của bản sửa: **giao diện không mất chữ, thông tin lỗi chính xác, dễ nhìn, lịch sử đúng với lỗi thật, và phần mềm khởi động tạo config/log có cấu trúc rõ ràng và gần hành vi phần mềm gốc nhất có thể.**
