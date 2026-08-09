# JBZUniversalTester V12.9.0 — Báo cáo sửa Probe / Card mở rộng / Single-window UI

## 1. Phạm vi

V12.9.0 được phát triển tiếp từ V12.8.0. Mục tiêu của bản này:

- Cài đặt và Lịch sử chạy **trong cùng MainWindow**, không tạo Window/ShowDialog riêng và không sinh thêm mục Alt+Tab.
- Giữ Probe là lớp hiển thị song song với Production.
- Chỉ có một đường nhận frame trực tiếp từ `IBoardTransport.FrameReceived`.
- Chuẩn hóa card/I/O thành một nguồn sự thật `BoardCapacity`.
- Production decoder và explicit Probe decoder dùng chung `BoardAddressMapper`, nhưng giữ semantics khác nhau.
- Khi lưu số card trong Settings, runtime scan/decoder/TestView được reconfigure ngay mà không đóng FTDI.
- TestView hiển thị toàn bộ card vật lý động; card ngoài capacity hiện `DISABLED`, card đang scan hiện `ACTIVE`, card có Probe được highlight riêng.
- Giữ lớp fail-safe của V12.7/V12.8: Probe/fault không điều khiển relay.

## 2. Nguyên nhân vấn đề cũ

### 2.1 Probe

Production sử dụng semantics `SOURCE -> TARGET`, trong khi TestPin/Probe sử dụng A0/A1 như trạng thái I/O đang chạm. Nếu cùng byte bị đưa vào Production fault engine thì một điểm Probe có thể bị suy diễn thành nhiều quan hệ sai/chập.

V11.8 trở đi đã bỏ subscription trực tiếp từ `TestEngine`. V12.9 tiếp tục giữ nguyên nguyên tắc này: `TestViewModel.OnBoardFrameReceived` là router duy nhất phía application.

### 2.2 Card

Các version cũ tồn tại nhiều khái niệm lẫn nhau: logical scan-card 32 I/O, cặp card 64 I/O, `CardCount`, `ExpansionCardCount`, và byte `xx` của `8C 00 xx 00`. V12.9 gom việc tính toán vào `BoardCapacity` + `BoardAddressMapper` để Transport/Decoder/ViewModel không tự tính riêng.

### 2.3 UI Settings/History

V12.8 dùng `ProductionSettingsWindow`, `HistoryWindow` và `PasswordPromptWindow`. Đây là các WPF `Window`, nên Windows có thể coi chúng là cửa sổ riêng và xuất hiện khi Alt+Tab. V12.9 xóa các cửa sổ này và thay bằng `ProductionSettingsPage`, `HistoryPage`, password gate nhúng trực tiếp trong `MainWindow`.

## 3. Những file chính đã sửa/thêm

- `Version.props`
- `JBZUniversalTester.csproj`
- `Properties/AssemblyInfo.cs`
- `Models/BoardCapacity.cs` **mới**
- `Models/BoardCardState.cs` **mới**
- `Models/ProductionSettings.cs`
- `Services/BoardAddressMapper.cs` **mới**
- `Services/BoardIoDecoder.cs`
- `Services/D2xxBoardTransport.cs`
- `Services/IBoardTransport.cs`
- `Services/ProbeContactClassifier.cs`
- `Services/ProductionConfigService.cs`
- `ViewModels/MainViewModel.cs`
- `ViewModels/TestViewModel.cs`
- `Views/MainWindow.xaml/.cs`
- `Views/ProductionSettingsPage.xaml/.cs` **mới**
- `Views/HistoryPage.xaml/.cs` **mới**
- `Views/TestWindow.xaml`
- `docs/UniversalTester.cfg.example`

Đã loại khỏi V12.9:

- `Views/ProductionSettingsWindow.xaml/.cs`
- `Views/HistoryWindow.xaml/.cs`
- `Views/PasswordPromptWindow.xaml/.cs`
- `Views/PinProbeWindow.xaml/.cs`

Probe vận hành không còn có màn hình riêng; vùng Probe nằm trực tiếp trên TestWindow.

## 4. Raw RX router

Direct subscriber tới board chỉ còn:

```text
TestViewModel
  _board.FrameReceived += OnBoardFrameReceived
```

Không có direct `FrameReceived +=` trong `TestEngine` hoặc View khác.

Pipeline:

```text
FTDI D2XX RX
   ↓
D2xxBoardTransport.ScanLoopWorker
   ↓
BoardIoDecoder (decoder riêng theo scan generation/mode)
   ↓
ScanFrame
   ↓
TestViewModel.OnBoardFrameReceived   <--- application router duy nhất
   ├─ explicit Probe -> diagnostic Probe state only
   ├─ Production + inline Probe signature -> ProbeContacts only
   └─ Production bình thường -> TestEngine.ProcessFrame
```

## 5. Production decoder

`BoardIoDecoder.FeedProduction` giữ state `CurrentSource` riêng:

```text
80/81/... -> SOURCE
A0/A1/... -> TARGET của SOURCE hiện tại
C0 00     -> kết thúc frame
```

Production frame mới chứa `Connections`, `TargetHits`, `ActiveIo` và mới được phép đi vào `TestEngine`.

## 6. Explicit Probe decoder

`BoardIoDecoder.FeedProbe` không dùng `CurrentSource` để dựng wiring graph.

```text
80/81/... -> I/O normal
A0/A1/... -> I/O touched
C0 00     -> snapshot hoàn chỉnh
```

V12.9 giữ `_probeActive` là một tập I/O, không còn ép explicit Probe về một `CurrentProbeIo`. Snapshot có thể chứa IO11 + IO24; snapshot sau chỉ còn IO24 thì IO11 được coi là release; snapshot rỗng là release hết.

Explicit Probe API vẫn tồn tại cho diagnostic/service, nhưng không còn màn hình PinProbe riêng trong UI vận hành.

## 7. Inline Probe song song với Production

Yêu cầu vận hành hiện tại là Production/configuration vẫn hiển thị trong khi người vận hành chạm que. V12.9 giữ cơ chế inline của V12.8:

- `ProbeContacts` là collection riêng.
- Không xóa `Faults`/THT/config khi contact xuất hiện.
- Có thể hiện tối đa hai contact cùng lúc theo UX hiện tại.
- Release chỉ xóa contact tương ứng/Probe display.
- Probe contact không gọi `TestEngine.ProcessFrame` cho frame đã được nhận dạng là Probe.
- Interlock relay sau Probe vẫn được giữ.

**Giới hạn cần hardware verify:** raw Production và TestPin dùng cùng các marker 80/A0 nhưng firmware hiện không có một mode-bit riêng đã được xác minh cho “Probe song song”. Vì vậy lớp inline song song vẫn phải nhận dạng signature từ frame Production. Không thể tuyên bố phần này deterministic 100% như explicit Probe mode nếu chưa có thêm trace/protocol marker từ bo gốc. V12.9 không che giấu giới hạn này.

## 8. BoardCapacity — nguồn sự thật duy nhất

V12.9 định nghĩa:

```text
IoPerPhysicalCard = 32
PhysicalCardsPerExpansionModule = 2
IoPerExpansionModule = 64
MaxExpansionModuleCount = 10
MaxPhysicalCardCount = 20
MaxGlobalIo = 640
```

`BoardCapacity` chứa:

```text
ExpansionModuleCount
PhysicalCardCount
ScanCardCount
TotalIoCapacity
StartCardNumber
StartScanParameter
FirstGlobalIo
LastGlobalIo
```

Transport, Decoder, MainViewModel, TestView và Probe cùng đọc object này.

## 9. Ý nghĩa `ExpansionCardCount`, `PhysicalCardCount`, `ScanCardCount`

Trong V12.9:

```text
ExpansionCardCount (UI) = số module 64 I/O
PhysicalCardCount       = ExpansionCardCount × 2
TotalIoCapacity         = PhysicalCardCount × 32
ScanCardCount           = ExpansionCardCount
START_SCAN xx           = ScanCardCount
```

Lý do sửa `START_SCAN xx` khác V12.8: trace đã lưu trong project có:

```text
[카드 수]4
START_SCAN 8C 00 04 00
Diagnostic round = 256 source/I/O
```

256 / 4 = 64 I/O cho mỗi giá trị `xx`. Vì vậy V12.9 không còn nhân đôi byte `xx` thành 2,4,...20 như V12.8.

### Bảng runtime V12.9

| Settings `Card mở rộng` | Expansion module | Physical card | Scan card / `xx` | Total IO | START_SCAN |
|---:|---:|---:|---:|---:|---|
| 1 | 1 | 2 | 1 | 64 | `8C 00 01 00` |
| 2 | 2 | 4 | 2 | 128 | `8C 00 02 00` |
| 4 | 4 | 8 | 4 | 256 | `8C 00 04 00` |
| 10 | 10 | 20 | 10 | 640 | `8C 00 0A 00` |

Mức `xx=4 -> 256 I/O` có trace lưu trong project. Mức 1/2/10 dùng cùng công thức tuyến tính đã centralize; đặc biệt `xx=10/640` vẫn cần capture thật với hardware mở rộng để xác nhận firmware hỗ trợ đầy đủ.

## 10. BoardAddressMapper

Production và Probe cùng dùng `BoardAddressMapper.TryDecode` để biến marker bank + index thành Global I/O.

Protocol bank hiện giữ theo trace:

```text
80/A0 + 00..7F -> bank 0, relative IO 1..128
81/A1 + 00..7F -> bank 1, relative IO 129..256
82/A2 ...      -> bank tiếp theo nếu capacity cho phép
```

Sau decode, mapper kiểm tra:

```text
1) relative < TotalIoCapacity
2) GlobalIo thuộc FirstGlobalIo..LastGlobalIo
3) GlobalIo <= 640
```

I/O ngoài card active bị reject ở decoder, không tạo fake Probe/fault.

## 11. Global IO -> physical card/local IO

Với StartCardNumber=1:

```text
IO1  -> Physical Card 1 / Local 1
IO32 -> Physical Card 1 / Local 32
IO33 -> Physical Card 2 / Local 1
IO64 -> Physical Card 2 / Local 32
IO65 -> Physical Card 3 / Local 1
```

`BoardAddressMapper.GetCardAddress()` là hàm trung tâm thực hiện phép tính này.

## 12. StartCardNumber

Project chỉ có trace chắc chắn với `StartCardNumber=1`. V12.9 cô lập interpretation hiện tại tại `BoardCapacity`/`BoardAddressMapper` thay vì rải phép offset ở nhiều subsystem.

**Cần hardware verify:** với StartCardNumber > 1 chưa có capture chứng minh đây là hardware address, bank offset hay chỉ numbering offset. Vì vậy chưa nên coi behavior >1 là reverse-engineered hoàn chỉnh.

## 13. Settings -> board runtime

Khi Save trang Cài đặt:

```text
ProductionSettingsPage.Save
   ↓
ProductionConfigService.Save
   ↓
MainViewModel.ReloadProductionSettingsAsync
   ↓
TestViewModel.RefreshProductionConfigurationAsync
   ↓
StopScan (nếu đang scan)
   ↓
AllRelaysOff
   ↓
ConfigureScanRange -> BoardCapacity mới
   ↓
RebuildActiveCards
   ↓
StartScan lại cùng runtime mode
```

FTDI không bị Close/Open chỉ vì đổi số card.

## 14. Chống stale frame / duplicate worker

`D2xxBoardTransport` có `_scanGeneration`.

- Stop/reconfigure tăng generation trước khi reader cũ kết thúc.
- START tạo decoder mới và generation mới.
- Worker chỉ publish khi `generation == _scanGeneration` và `decoded.Mode == mode`.
- `WriteAsync` purge RX/TX trước command; START scan bắt đầu ở biên RX sạch.
- `StartScanAsync` luôn `StopScanCoreAsync` trước khi tạo worker mới.

## 15. TestView card động

`TestViewModel.ActiveCards` được dựng từ `BoardCapacity`.

V12.9 hiển thị đủ 20 physical card để người vận hành nhìn được trạng thái:

```text
CARD n
ACTIVE / DISABLED
IO first-last
```

- Card trong capacity: xanh `ACTIVE`.
- Card ngoài capacity: xám `DISABLED`.
- Probe chạm I/O thuộc card active: card đó highlight vàng.
- Release Probe: highlight mất, `ACTIVE` vẫn giữ nguyên.

## 16. THT lookup và duplicate I/O

Parser hiện giữ nguyên tất cả pin record; không ép mỗi I/O về một record duy nhất.

V12.9 bổ sung `_pinsByIoLookup` kiểu `ILookup<int, PinRecord>` trong `TestViewModel`:

```text
Global IO -> 0..N PinRecord
```

Do đó cùng IO xuất hiện ở nhiều connector/pin không bị mất mapping. Probe UI chọn metadata phù hợp để hiển thị, còn danh sách đầy đủ vẫn tồn tại.

CLIP A0/AO + a1/a2/... tiếp tục dùng `ClipTopology` riêng, không lấy số `aN` làm I/O đích; I/O đích lấy từ cột I/O của row aN trong THT.

## 17. Probe bị chặn khỏi Fault/Relay/LOT/History ở đâu

- Board frame chỉ vào `TestEngine.ProcessFrame` trong nhánh RuntimeMode.Production bình thường.
- Explicit Probe frame chỉ phát về Probe state/diagnostic.
- Inline frame đã nhận dạng Probe return trước `TestEngine.ProcessFrame`.
- `TestEngine.EjectFaultProductAsync()` vẫn là fail-safe `AllRelaysOffAsync()`.
- Relay tự động PASS vẫn giữ R2 MARKING -> OFF -> R1 JIG -> OFF.
- Manual relay test buttons vẫn là thao tác chủ động của kỹ thuật viên, không phải fault action.

## 18. THT hard-code 64/128

Rà `ThtModelParser.cs` thấy các số 64/128 còn lại thuộc cấu trúc file Excel/OLE (record/view/sector), không phải limit Global I/O. `ProductModel.MaxIo` lấy max thực tế từ Pins/CLIP, sau đó capacity được kiểm tra bằng `BoardCapacity`.

## 19. UI Single Window

V12.9 thay đổi luồng:

```text
MainWindow
  ├─ Home
  ├─ ContentControl InternalPageHost
  │    ├─ ProductionSettingsPage
  │    └─ HistoryPage
  └─ SettingsPasswordGate (inline)
```

Không còn:

```text
new ProductionSettingsWindow().ShowDialog()
new HistoryWindow().ShowDialog()
new PasswordPromptWindow().ShowDialog()
```

Do đó CÀI ĐẶT/LỊCH SỬ không tạo task/window thứ hai để Alt+Tab.

## 20. Static validation đã chạy trong môi trường sửa source

- Tất cả XAML/csproj/Version.props parse XML: PASS.
- Kiểm tra event handler XAML -> code-behind: PASS.
- Direct `_board.FrameReceived +=`: đúng 1 vị trí.
- Không còn call site của ProductionSettingsWindow/HistoryWindow/PasswordPromptWindow/PinProbeWindow.
- Version đồng bộ: 12.9.0 / 12.9.0.0 / `12_9_0`.
- Address boundary theo công thức central: PASS cho IO1/32/33/64/65 và reject vượt capacity.

Môi trường sửa source hiện không có .NET SDK/MSBuild, vì vậy chưa thể thực hiện compile WPF/Publish EXE tại đây. Khi build trên máy Windows có .NET 8 SDK, target version validation của project sẽ chặn build nếu version metadata lệch.

## 21. Test case trạng thái

| Test | Static/code-path result | Hardware verify |
|---|---|---|
| Probe IO không map -> chỉ IO | Implemented | cần test bo |
| Probe IO có THT -> wire/color | Implemented | cần test bo |
| Probe release không reset Production | Implemented | cần test bo |
| Hai Probe contact | Inline collection + explicit Probe set implemented | cần test bo |
| False-short regression | Probe branch returns trước TestEngine | cần trace thật |
| Production wrong/short thật | Production decoder/TestEngine vẫn giữ | cần jig lỗi thật |
| Mode generation/stale frame | generation + stop/restart implemented | cần stress test USB |
| IO1/32/33/64/65 boundary | static formula PASS | cần card thật |
| IO cuối capacity | bounds implemented | cần card thật |
| Vượt capacity | decoder reject | cần injected trace/card |
| Tăng/giảm card Settings | async runtime reconfigure implemented | cần test board |
| 10 module / 640 IO | code path implemented | **chưa có trace hardware để xác minh** |

## 22. Version/build identity

```text
Version              = 12.9.0
AssemblyVersion      = 12.9.0.0
FileVersion          = 12.9.0.0
InformationalVersion = 12.9.0
VersionFileTag       = 12_9_0
EXE                   = JBZUniversalTester_V12_9_0.exe
```

Publish output theo script:

```text
PublishSingle/V12.9.0/JBZUniversalTester_V12_9_0.exe
```

