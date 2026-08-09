# BÁO CÁO SAU SỬA `JBZUniversalTester V12.9.1`

## TestView – Fault thực tế – History – Startup Config/Log – Probe/Card regression

**Ngày sửa:** 2026-08-08  
**Build source:** `JBZUniversalTester_V12_9_1`  
**Phạm vi:** tiếp tục từ `JBZUniversalTester V12.9.0`, bám theo báo cáo yêu cầu sửa TestView/Fault/History/Config-Log, đồng thời không phá Probe/Card mở rộng đã làm ở V12.9.

> Trạng thái build trong môi trường sửa: **không thể chạy WPF build thật vì sandbox không có `dotnet`/MSBuild**. Các kiểm tra tĩnh đã chạy và được ghi riêng tại `docs/V12_9_1_STATIC_VALIDATION.txt`. Project có thêm `VERIFY_BUILD_V12_9_1.cmd` để restore/build/publish trên máy Windows có .NET 8 SDK.

---

## 1. Những file đã sửa/thêm

### File sửa

- `App.xaml.cs`
- `Models/HistoryModels.cs`
- `Models/TestModels.cs`
- `Services/ErrorLogService.cs`
- `Services/HistoryExportService.cs`
- `Services/ProductionConfigService.cs`
- `Services/TestEngine.cs`
- `Services/TestHistoryStore.cs`
- `Version.props`
- `ViewModels/TestViewModel.cs`
- `Views/HistoryPage.xaml`
- `Views/TestWindow.xaml`

### File thêm

- `Models/FaultModels.cs`
- `Services/AsyncFileLogService.cs`
- `Services/StartupBootstrapService.cs`
- `V12_9_1_CHANGELOG.txt`
- `docs/V12_9_1_REPAIR_REPORT.md`
- `docs/V12_9_1_STATIC_VALIDATION.txt`
- `VERIFY_BUILD_V12_9_1.cmd`

---

## 2. Nguyên nhân label TestView bị cắt

Layout cũ dùng nhiều độ rộng cố định, trong khi nhãn tiếng Việt dài hơn phần cột cấp cho chúng. Khi chiều rộng khả dụng giảm do DPI/scaling, vùng model/statistics càng dễ ép các label và Border.

Bản V12.9.1 sửa theo nguyên tắc:

- label sản phẩm dùng tên gọn: `Mã hàng`, `Sản phẩm`, `Loại xe`, `Mã KH`;
- cột label dùng `Auto` + `MinWidth` hợp lý;
- cột giá trị dùng `*`;
- các vùng lớn dùng tỷ lệ `*` và MinWidth thấp hơn thay vì width tuyệt đối lớn;
- giá trị động có `ToolTip` để vẫn đọc được toàn bộ nội dung dài;
- không giảm font xuống cỡ quá nhỏ để chữa clipping.

---

## 3. Layout TestView mới

Vùng đầu TestView được chia thành:

```text
LOT / COUNTER | MODEL + THÔNG TIN SẢN PHẨM | ACTIVE FAULT + STATISTICS
```

Bên dưới vẫn giữ:

```text
PROBE SONG SONG
BẢNG CẤU HÌNH / KẾT QUẢ PRODUCTION
```

Điểm quan trọng:

- vùng trạng thái lỗi chính có tiêu đề lớn;
- có riêng `Mong đợi` và `Thực tế`;
- bảng Production không bị Probe thay thế;
- counter tách `Dây chưa kết nối`, `Đấu sai`, `Chập mạch`;
- thống kê phải giữ đủ Border.

---

## 4. Cách hiển thị ngày giờ

`CurrentTimeText` hiện nằm trong `Border` riêng:

- `FontSize=18`;
- `FontWeight=Bold`;
- nền tương phản nhẹ;
- padding riêng;
- không đặt sát mép cửa sổ.

---

## 5. Đổi “Số lỗi hở mạch” thành “Dây chưa kết nối”

Đã đổi ở live UI/fault mapping, không chỉ sửa Text XAML.

Fault chuẩn:

```text
OpenCircuit -> DÂY CHƯA KẾT NỐI
WrongWiring -> ĐẤU SAI
ShortCircuit -> CHẬP MẠCH
ResistanceOutOfRange -> ĐIỆN TRỞ KHÔNG ĐẠT
```

Không còn chuỗi live `Số lỗi hở mạch` trong source vận hành.

---

## 6. Fault model mới

Thêm `Models/FaultModels.cs`:

```text
ProductFaultType
FaultTypeCatalog
FaultDetail
CompletedTestResult
```

`FaultDetail` giữ được:

- fault type/code;
- expected source/target IO;
- actual source/target IO;
- related IO;
- connector/pin mong đợi;
- connector/pin thực tế;
- wire/color;
- message;
- measured/min/max resistance.

`FaultTypeCatalog` là nguồn mapping duy nhất cho tên lỗi tiếng Việt và thứ tự ưu tiên.

---

## 7. Cách xác định Open / Dây chưa kết nối

`TestEngine.BuildRows()` tạo `OpenCircuit` cho network chưa PASS.

Mỗi Open row lưu:

```text
ExpectedSourceIo
ExpectedTargetIo
RelatedIos
```

Status ví dụ:

```text
Chưa kết nối: IO11 <-> IO18
```

CLIP cũng tạo Open fault riêng với common A0/AO và target branch thực tế từ THT.

---

## 8. Cách xác định Wrong Wiring / Đấu sai

Nếu source thực tế là `SourceIo` được khai báo của một network nhưng board trả về target ngoài network đó:

```text
Expected: source -> target cấu hình
Actual:   source -> target board trả về
```

Ví dụ:

```text
THT:      IO11 -> IO18
Board:    IO11 -> IO24
Fault:    ĐẤU SAI
Expected: IO11 -> IO18
Actual:   IO11 -> IO24
```

Dữ liệu này đi vào UI, History JSON và ErrorLog từ cùng snapshot.

---

## 9. Cách xác định Short / Chập mạch

Một relation ngoài expected component, nối hai IO đang thuộc các component/network THT khác nhau và không rơi vào trường hợp source-known/target-wrong ở trên, được phân loại `ShortCircuit`.

Ví dụ:

```text
CHẬP MẠCH
IO11 <-> IO24
```

`FaultKind.Short` hiện có đường tạo thật trong `TestEngine`; counter `ShortCount` không còn chỉ là enum/field không được cấp dữ liệu.

---

## 10. Cách lấy source/target/expected/actual

Nguồn dữ liệu:

```text
ScanFrame.Connections
  -> TestEngine._currentConnections
  -> WiringFaultPair
  -> FaultDetail
  -> CompletedTestResult
```

`WiringFaultPair` hiện chứa:

```text
SourceIo / TargetIo          = actual
ExpectedSourceIo/TargetIo    = expected nếu xác định được
FaultType
Reason
```

Metadata connector/pin được `TestViewModel` enrich từ THT lookup trước khi chốt kết quả.

---

## 11. Màn hình chính hiển thị fault

Các binding mới:

```text
ActiveFaultTitle
ActiveFaultMessage
ActiveFaultExpectedText
ActiveFaultActualText
ActiveFaultBackground
ActiveFaultForeground
```

Ví dụ Wrong Wiring:

```text
ĐẤU SAI
Mong đợi: IO 11 / C05-PIN3 -> IO 18 / C08-PIN2
Thực tế:  IO 11 / C05-PIN3 -> IO 24 / C09-PIN7
```

Ưu tiên fault:

```text
1. CHẬP MẠCH
2. ĐẤU SAI
3. DÂY CHƯA KẾT NỐI
4. ĐIỆN TRỞ KHÔNG ĐẠT
```

Toàn bộ fault detail vẫn được giữ, không chỉ primary fault.

---

## 12. History ghi fault như thế nào

`Result` được chuẩn hóa đúng nghĩa:

```text
PASS
FAIL
```

`Loại lỗi` nằm ở field riêng:

```text
FaultType
FaultCode
```

History thêm:

```text
ExpectedSourceIo
ExpectedTargetIo
ActualSourceIo
ActualTargetIo
FaultDetailsJson
FaultSummary
MeasuredResistance
ResistanceMin
ResistanceMax
```

Do đó cùng record có thể là:

```text
Result = FAIL
FaultType = ĐẤU SAI
ExpectedTargetIo = 18
ActualTargetIo = 24
```

---

## 13. Đồng bộ FaultType giữa UI / History / Log / Export

`RecordCompletedProduct()` chụp `faultDetails` đúng một lần và tạo `CompletedTestResult`.

Từ snapshot này:

```text
CompletedTestResult
  -> statistics
  -> SQLite History
  -> ErrorLog JSON
  -> CSV/XLSX fields
  -> UI/status đã dùng cùng FaultTypeCatalog
```

Không còn mỗi subsystem tự đặt chuỗi `Hở mạch`, `Open`, `FAIL`, `Đấu sai/chập` theo cách riêng.

---

## 14. Config được tạo lúc startup

`StartupBootstrapService` đảm bảo tồn tại:

```text
appsettings.json
production.settings.json
UniversalTester.cfg
Data/History/test-history.db
Data/ErrorLogs/
Data/Models/
```

`AppSettings.Load()` đã có cơ chế backup JSON lỗi. `ProductionConfigService.Load()` được bổ sung backup JSON lỗi tương tự.

Tên backup:

```text
*.invalid_yyyyMMdd_HHmmss.*
```

Sau đó app dùng default hợp lệ thay vì crash im lặng.

---

## 15. Log được tạo lúc startup

Thêm `AsyncFileLogService` và khởi tạo từ `App.OnStartup()`.

Cấu trúc:

```text
Data/Logs/
  Application/
  Board/
  Test/
  Error/
```

File theo ngày:

```text
Application_yyyyMMdd.log
Board_yyyyMMdd.log
Test_yyyyMMdd.log
Error_yyyyMMdd.log
```

Application log bắt đầu từ STARTUP và kết thúc bằng SHUTDOWN.

---

## 16. Path config/log/history

Theo source hiện tại:

```text
appsettings.json             -> AppContext.BaseDirectory
production.settings.json     -> AppContext.BaseDirectory
UniversalTester.cfg          -> AppContext.BaseDirectory
History DB                   -> HistoryDirectory, mặc định Data/History/test-history.db
Async logs                   -> Data/Logs/...
Error detail JSON            -> Data/ErrorLogs/...
Models                       -> Data/Models/...
```

Không tự tạo thêm tên compatibility file giả kiểu `Htdrv3-*.cfg` khi chưa có trace chứng minh format/tác dụng.

---

## 17. Cơ chế log async

`AsyncFileLogService` dùng `System.Threading.Channels`:

```text
producer UI/board/test
  -> Channel
  -> single async writer
  -> append file theo ngày
```

Có level:

```text
Normal
Diagnostic
ProtocolTrace
```

Normal không ghi raw RX/TX liên tục. Các board message bắt đầu `RX frame` / `TX ` được xếp `Diagnostic`, nên mặc định không làm disk I/O nặng theo từng frame.

---

## 18. Regression Probe

Đã giữ router Probe/Production tách biệt:

```text
TestEngine.ProcessFrame
  chỉ nhận frame.Mode == Production
```

Probe không đi vào TestEngine/History/LOT/Statistics/Relay.

Đã sửa thêm regression còn sót:

- không còn `Faults.Clear()` khi vào Probe;
- `Faults`/bảng cấu hình Production giữ nguyên;
- `ProbeContacts` là vùng riêng;
- hỗ trợ tối đa 2 IO đầu dò hiển thị đồng thời;
- release chỉ xóa `ProbeContacts` và highlight card, không reset Production UI.

---

## 19. Regression card mở rộng

Không thay đổi kiến trúc `BoardCapacity`/`BoardAddressMapper` của V12.9.

Static vectors vẫn đúng:

```text
1 module  -> 2 physical cards  -> 64 IO
2 module  -> 4 physical cards  -> 128 IO
4 module  -> 8 physical cards  -> 256 IO
10 module -> 20 physical cards -> 640 IO
```

Boundary arithmetic:

```text
IO32 -> Card1 Local32
IO33 -> Card2 Local1
IO64 -> Card2 Local32
IO65 -> Card3 Local1
```

Probe card activity vẫn đọc từ cùng BoardCapacity.

---

## 20. Kết quả test DPI/layout/build

### Đã thực hiện trong sandbox

- XAML/XML parse: PASS.
- C# structural scan: PASS.
- Không merge conflict marker: PASS.
- Version 12.9.1 đồng bộ: PASS.
- Old label live source removed: PASS.
- ShortCircuit live creation path: PASS.
- Probe path không `Faults.Clear()`: PASS.
- Direct `FrameReceived` app router còn một nơi: PASS.
- Export 30 header / 30 value / 30 width: PASS.
- SQLite migration từ schema V12.9 mô phỏng: PASS, 11 cột mới.
- Card boundaries static: PASS.

### Chưa thể thực hiện trong sandbox

- `dotnet build` / WPF compile: **BLOCKED**, máy sửa không có .NET SDK/MSBuild.
- DPI runtime 100/125/150%: cần chạy trên Windows target.
- FTDI/JBZ hardware, Keysight, relay, master sample: cần máy thật.

Project có `VERIFY_BUILD_V12_9_1.cmd` để thực hiện:

```text
restore
build Release win-x86
publish one-file
```

và tạo `verify_build_V12.9.1.log` trên máy Windows build.

---

# Test acceptance cần chạy trên máy thật

## Fault-1 Open

```text
THT: IO11 <-> IO18
Expected UI: DÂY CHƯA KẾT NỐI / IO11 <-> IO18
History: Result=FAIL, Loại lỗi=DÂY CHƯA KẾT NỐI
```

## Fault-2 Wrong Wiring

```text
THT:    IO11 -> IO18
Actual: IO11 -> IO24
Expected UI:
  ĐẤU SAI
  Mong đợi IO11 -> IO18
  Thực tế  IO11 -> IO24
History:
  Result=FAIL
  FaultType=ĐẤU SAI
  ExpectedTargetIo=18
  ActualTargetIo=24
```

## Fault-3 Short

```text
Actual: IO11 <-> IO24 giữa hai network khác nhau
Expected:
  CHẬP MẠCH
  IO11 <-> IO24
History: Result=FAIL, FaultType=CHẬP MẠCH
```

## Fault-4 Resistance

```text
Limit 100-110 Ω
Measured 125 Ω
Expected:
  ĐIỆN TRỞ KHÔNG ĐẠT
  125 Ω
  Giới hạn 100-110 Ω
History lưu measured/min/max.
```

## Probe

```text
Không chạm: SẴN SÀNG
Chạm IO24: ĐANG DÒ IO(24)
Chạm 2 IO: hiện 2 contact
Nhấc que: chỉ Probe mất
Bảng cấu hình/kết quả Production: vẫn giữ nguyên suốt quá trình
Không tạo FAIL / không tăng LOT / không relay.
```

---

# Kết luận

V12.9.1 đã refactor phần fault/history theo một nguồn dữ liệu chung, sửa TestView theo yêu cầu dễ đọc và không dùng label cột hẹp, tách Open/Wrong/Short/Resistance đúng loại, bổ sung expected/actual vào History, thêm startup bootstrap + async log, và sửa regression Probe để Probe thực sự hiển thị song song không làm mất bảng Production.

Bước còn lại bắt buộc trước khi nghiệm thu production là chạy `VERIFY_BUILD_V12_9_1.cmd` trên Windows có .NET 8 SDK và thực hiện các test DPI + phần cứng liệt kê ở trên.
