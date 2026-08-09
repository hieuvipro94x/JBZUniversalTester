# BÁO CÁO TỔNG HỢP YÊU CẦU SỬA `JBZUniversalTester\_V12\_7\_0`

## Mục tiêu: Probe/Test Pointer giống phần mềm gốc + Card mở rộng hoạt động đúng toàn bộ Production/Probe/TestView

\---

# 1\. MỤC TIÊU TỔNG THỂ

Project cần sửa:

```text
JBZUniversalTester\_V12\_7\_0
```

Mục tiêu là phân tích và sửa **toàn bộ source project**, không chỉ vá một vài hàm, để chức năng:

```text
Probe / Test Pointer / đầu dò chân IO
```

hoạt động giống phần mềm JBZ/Htdrv3 gốc, đồng thời:

* không gây false-short;
* không đi vào fault engine;
* không kích relay lỗi;
* không làm mất trạng thái Production;
* hoạt động được trên toàn bộ card mở rộng đã cấu hình;
* TestView phải kích hoạt đúng toàn bộ card/IO theo Settings;
* Production và Probe phải dùng cùng một cơ chế định địa chỉ card/IO;
* mọi giới hạn 64/128 IO hard-code phải được kiểm tra và loại bỏ nếu không đúng phần cứng;
* việc thay đổi số card trong Settings phải thực sự thay đổi board runtime, scan capacity, decoder, TestView và Probe.

\---

# 2\. NHỮNG THÀNH PHẦN PHẢI PHÂN TÍCH TRƯỚC KHI SỬA

Phải đọc toàn bộ project và đặc biệt kiểm tra các class/file có vai trò tương đương:

```text
D2xxBoardTransport
BoardIoDecoder
TestEngine
TestViewModel
PinProbeViewModel
PinProbeWindow
TestView
ThtModelParser
ProductionSettings
ProductionSettingsViewModel
AppSettings
ProductionConfigService
Board-related models
IO / Pin / Wire / Network models
Relay services
Keysight service
History/statistics/LOT
```

Phải tìm toàn bộ nơi subscribe hoặc xử lý:

```text
FrameReceived
DataReceived
PinChanged
RawDataReceived
ScanFrame
FT\_Read
FT\_Write
```

và xác định:

1. raw bytes vào từ đâu;
2. ghép frame ở đâu;
3. decoder nào xử lý;
4. mode Production/Probe được quyết định ở đâu;
5. fault engine nhận input từ đâu;
6. UI Probe lấy dữ liệu từ đâu;
7. có duplicate subscription hay không;
8. có frame cũ/stale frame khi đổi mode hay không.

\---

# 3\. PHẦN CỨNG VÀ D2XX ĐÃ XÁC NHẬN

Bo JBZ giao tiếp với PC qua:

```text
FTDI / D2XX
VID = 0403
PID = 6001
```

Bản dựng lại V12.7 đã được xác nhận runtime là có:

```text
load ftd2xx.dll
open FTDI device
```

và trên cùng máy đã mở đúng bo serial:

```text
A90764PH
```

giống phần mềm gốc.

Không được tùy tiện thay đổi transport/D2XX đang hoạt động nếu không cần thiết.

Các command đã reverse-engineer trước đây:

```text
8D 00 00 00   STOP / RESET
8A 01 01 01   VERIFY
91 00 00 00   INIT\_1
90 00 00 30   INIT\_2
8C 00 xx 00   START\_SCAN

8E 00 00 01   RELAY 1 ON
8E 00 00 02   RELAY 2 ON
8E 00 00 00   RELAY OFF

80 00 00 00   RESET / CLEAR
```

Phải kiểm tra project hiện build các command trên ở đâu và số card ảnh hưởng tham số `xx` của `START\_SCAN` như thế nào.

\---

# 4\. NGUYÊN TẮC QUAN TRỌNG NHẤT: PRODUCTION VÀ PROBE KHÔNG ĐƯỢC DÙNG CHUNG SEMANTICS

Cùng một byte/frame có thể mang nghĩa khác nhau tùy mode.

## 4.1 Production Mode

Trong Production, dữ liệu được hiểu theo dạng:

```text
SOURCE
  ↓
TARGET
  ↓
TARGET...
  ↓
END FRAME
```

Ví dụ:

```text
80 00
A0 11
C0 00
```

trong Production phải được hiểu là:

```text
SOURCE = IO1
TARGET = IO18
```

tức:

```text
IO1 ↔ IO18
```

Production decoder phải giữ trạng thái `CurrentSource`.

Ví dụ logic:

```text
80/81/... → đặt SOURCE hiện tại
A0/A1/... → thêm TARGET của SOURCE hiện tại
C0        → kết thúc frame/network
```

Dữ liệu Production mới được đưa vào:

```text
continuity
open circuit
wrong wiring
short circuit
PASS/FAIL
```

\---

# 5\. PROBE / TEST POINTER PHẢI GIỐNG PHẦN MỀM GỐC

Trong Probe, cùng byte không được giải mã như Production.

Ví dụ:

```text
A0 01
```

trong Probe phải có thể được hiểu là:

```text
đầu dò đang chạm IO2
```

chứ tuyệt đối không tự động hiểu:

```text
SOURCE IO1 → TARGET IO2
```

Đây là nguyên nhân chính gây false-short trong các version cũ.

\---

# 6\. PROBE KHÔNG ĐƯỢC ĐI VÀO FAULT ENGINE

Pipeline bắt buộc:

```text
FTDI RX
   ↓
Raw Frame Parser
   ↓
Mode Router
   ├───────────────┐
   ↓               ↓
Production       Probe
Decoder          Decoder
   ↓               ↓
TestEngine       ProbeState
   ↓               ↓
Fault/PASS         UI
```

Không được:

```text
Probe Frame
   ↓
Production Decoder
   ↓
TestEngine
   ↓
SHORT / WRONG WIRING
```

Lỗi cũ từng xuất hiện kiểu:

```text
IO45 → IO7
IO46 → IO7
IO47 → IO7
IO48 → IO7
...
```

trong khi thực tế chỉ đang chạm Probe vào:

```text
IO7
```

Không được dùng heuristic kiểu:

```text
nhiều SOURCE cùng TARGET → chắc là Probe
```

Phải sửa decoder/router theo mode rõ ràng, deterministic.

\---

# 7\. CHỈ ĐƯỢC CÓ MỘT ĐIỂM ROUTE RAW RX

Phải kiểm tra toàn project xem có nhiều nơi cùng subscribe:

```text
FrameReceived
DataReceived
PinChanged
```

hay không.

Nếu có, cần refactor về một router trung tâm:

```csharp
void OnBoardFrameReceived(BoardFrame frame)
{
    switch (CurrentBoardMode)
    {
        case BoardOperatingMode.Production:
            \_productionDecoder.Process(frame);
            break;

        case BoardOperatingMode.Probe:
            \_probeDecoder.Process(frame);
            break;
    }
}
```

Không được để cùng frame bị:

```text
Production decoder xử lý
Probe decoder xử lý lại
ViewModel tự parse thêm lần nữa
```

\---

# 8\. MODE SWITCH PHẢI AN TOÀN

Khi chuyển:

```text
Production → Probe
Probe → Production
```

không nên đóng/mở USB liên tục nếu không cần.

Chuỗi mong muốn:

```text
Stop scan
   ↓
Purge RX
   ↓
Invalidate generation/session
   ↓
Reset Production decoder
   ↓
Reset Probe decoder
   ↓
Switch mode
   ↓
Start scan đúng mode
```

Nên có:

```csharp
long \_scanGeneration;
```

Mỗi lần đổi mode:

```text
\_scanGeneration++;
```

Mọi callback/frame cũ không cùng generation phải bị bỏ.

Mục tiêu:

* không stale frame;
* không duplicate worker;
* không double scan;
* không mất FTDI;
* không false-short.

\---

# 9\. PROBE PHẢI LÀ TRẠNG THÁI HIỂN THỊ SONG SONG

Probe không phải một màn hình thay thế Production.

Khi không chạm Probe:

```text
Production/configuration UI vẫn giữ nguyên
Probe state = empty
```

Khi chạm một IO:

```text
ĐANG DÒ: IO (24)
```

Nếu IO có mapping THT:

```text
ĐANG DÒ: IO (11) | BG21 | Đen
```

Có thể hiển thị đầy đủ:

```text
IO (11) | Connector C05 | Pin 3 | Wire BG21 | Màu Đen | 0.5
```

Khi rút Probe:

```text
Probe row biến mất
```

nhưng:

```text
model
PartNumber
Vehicle
THT configuration
Production state
statistics
```

phải giữ nguyên.

\---

# 10\. HỖ TRỢ NHIỀU IO PROBE SONG SONG

Không nên chỉ dùng:

```csharp
int? CurrentProbeIo
```

nếu hardware/protocol có thể cho nhiều trạng thái.

Nên dùng:

```csharp
ObservableCollection<ProbePinState>
```

hoặc cấu trúc tương đương.

Ví dụ:

```text
Probe IO11
Probe IO24
```

Release IO11:

```text
chỉ IO11 mất
IO24 còn
```

Release hết:

```text
Probe collection = empty
```

\---

# 11\. IO KHÔNG CÓ TRONG THT VẪN PHẢI HIỂN THỊ

Ví dụ Probe chạm:

```text
IO124
```

nhưng THT không có IO124.

Vẫn phải hiện:

```text
IO (124)
```

Không:

```text
popup
FAIL
SHORT
WRONG WIRING
relay fault
```

Chỉ đơn giản là không có metadata dây.

\---

# 12\. IO CÓ MAPPING THT PHẢI HIỂN THỊ ĐÚNG METADATA

Khi Probe IO có mapping, lấy từ parser/model:

```text
Global IO
Physical Card
Local IO
Connector
Pin
Wire name
Wire color
Wire section/thickness
Splice/network
Related pins
```

Một IO có thể có nhiều mapping.

Không được dùng:

```csharp
Dictionary<int, PinMapping>
```

nếu làm mất duplicate.

Nên dùng:

```csharp
Dictionary<int, List<PinMapping>>
```

hoặc:

```csharp
ILookup<int, PinMapping>
```

\---

# 13\. PROBE KHÔNG ĐƯỢC KÍCH RELAY/FAIL/LOT/HISTORY

Trong Probe:

* không bật relay eject;
* không bật relay fault;
* không tạo product FAIL;
* không tăng fail statistics;
* không tăng LOT;
* không ghi history sản phẩm FAIL;
* không in label;
* không phát âm thanh fault;
* không mở popup wrong wiring/short.

Dù frame Probe nhìn giống lỗi Production.

\---

# 14\. PRODUCTION VẪN PHẢI GIỮ ĐÚNG FAULT DETECTION

Sửa Probe không được phá Production.

Production vẫn phải:

```text
WAIT PRODUCT
   ↓
detect wiring
   ↓
continuity
   ↓
OPEN / WRONG / SHORT
   ↓
resistance nếu THT yêu cầu
   ↓
PASS
   ↓
relay / marking / JIG
   ↓
product removed
   ↓
reset cycle
```

Short/wrong wiring thật trong Production vẫn phải báo ngay.

Không được “sửa Probe” bằng cách vô hiệu hóa fault detector.

\---

# 15\. STATE PHẢI TÁCH RÕ

Nên có:

```text
BoardOperatingMode
├─ Production
└─ Probe
```

và state riêng:

```text
ProductionScanState
ProbeScanState
```

Không dùng chung:

```text
CurrentSource
CurrentTargets
CurrentFault
```

giữa hai mode.

Có thể tách:

```csharp
ProductionFrameDecoder
ProbeFrameDecoder
```

hoặc:

```csharp
BoardIoDecoder.ProcessProduction(...)
BoardIoDecoder.ProcessProbe(...)
```

miễn state không dùng chung.

\---

# 16\. PHẦN CARD MỞ RỘNG PHẢI ĐƯỢC SỬA CÙNG PROBE

Đây là yêu cầu bắt buộc.

Trong `Production Settings` hiện có các property kiểu:

```text
CardCount
ExpansionCardCount
StartCardNumber
```

hoặc tên tương đương.

Phải tìm toàn project mọi nơi sử dụng:

```text
CardCount
ExpansionCardCount
StartCardNumber
IoCapacity
MaxIo
TotalIo
ScanCardCount
PhysicalCardCount
```

Không được giả định tên biến hiện tại đã đúng nghĩa.

\---

# 17\. CẦN XÁC MINH CHÍNH XÁC CÁCH PHẦN CỨNG TÍNH CARD

Thông tin phần cứng hiện có:

```text
1 card vật lý = 32 IO
```

Nhưng project trước đây từng có sự lẫn lộn giữa:

```text
card vật lý
card scan
module mở rộng
ExpansionCardCount
```

Có trường hợp:

```text
2 card vật lý = 64 IO
```

được UI/config coi như một module mở rộng.

Vì vậy phải xác minh bằng:

```text
source hiện tại
START\_SCAN command
runtime trace
hardware behavior
```

trước khi chốt công thức.

Không được chỉ nhìn `ExpansionCardCount` rồi đoán.

\---

# 18\. CHUẨN HÓA MÔ HÌNH CARD

Sau khi xác minh, nên có các khái niệm rõ ràng:

```csharp
ExpansionModuleCount
PhysicalCardCount
ScanCardCount
IoPerPhysicalCard
TotalIoCapacity
StartCardNumber
```

Nếu phần cứng thật xác nhận:

```text
1 physical card = 32 IO
1 expansion module = 2 physical cards
```

thì mới được dùng:

```text
PhysicalCardCount = ExpansionModuleCount × 2
TotalIoCapacity   = PhysicalCardCount × 32
```

tương đương:

```text
TotalIoCapacity = ExpansionModuleCount × 64
```

Nhưng công thức trên chỉ là candidate, phải kiểm chứng.

\---

# 19\. PHẢI CÓ MỘT NGUỒN SỰ THẬT DUY NHẤT CHO CAPACITY

Không để:

```text
Settings tính một kiểu
BoardTransport tính một kiểu
Probe tính một kiểu
TestView tính một kiểu
THT parser tính một kiểu
```

Nên có model/service:

```csharp
public sealed class BoardCapacity
{
    public int ExpansionModuleCount { get; init; }
    public int PhysicalCardCount { get; init; }
    public int ScanCardCount { get; init; }
    public int IoPerPhysicalCard { get; init; }
    public int TotalIoCapacity { get; init; }
    public int StartCardNumber { get; init; }
}
```

Mọi subsystem phải dùng cùng object này.

\---

# 20\. SETTINGS THAY ĐỔI CARD → TESTVIEW PHẢI THAY ĐỔI

Ví dụ người dùng đổi:

```text
Card mở rộng = 1
```

TestView phải kích hoạt đúng capacity tương ứng.

Nếu đổi:

```text
Card mở rộng = 4
```

TestView phải cập nhật toàn bộ card tương ứng.

Các IO card mới phải trở thành hợp lệ cho:

```text
Production scan
Probe
THT mapping
UI
fault detection
```

Không được chỉ thay số trên Settings nhưng backend vẫn:

```text
MaxIo = 64
```

hoặc:

```text
MaxIo = 128
```

hard-code.

\---

# 21\. TESTVIEW PHẢI SINH CARD ĐỘNG

Phải kiểm tra có hard-code:

```text
Card 1
Card 2
Card 3
Card 4
```

hoặc:

```csharp
for (int i = 1; i <= 128; i++)
```

hay không.

Nếu có, sửa sang dynamic binding:

```csharp
ObservableCollection<CardViewModel> ActiveCards
```

Mỗi card có:

```csharp
CardNumber
PhysicalCardNumber
FirstGlobalIo
LastGlobalIo
IsEnabled
HasProbeActivity
```

XAML dùng:

```text
ItemsControl
ListView
UniformGrid
```

hoặc tương đương để sinh động.

\---

# 22\. PHÂN BIỆT `CardEnabled` VÀ `ProbeActivity`

Không được nhầm hai state.

Ví dụ:

```text
Card 3 Enabled       = true
Card 3 ProbeActivity = false
```

nghĩa là card đang active/scan nhưng chưa có Probe.

Khi chạm IO thuộc Card 3:

```text
Enabled       = true
ProbeActivity = true
```

Khi release:

```text
Enabled       = true
ProbeActivity = false
```

Card không được disable khi Probe release.

\---

# 23\. SETTINGS PHẢI ĐI XUỐNG TẬN BOARD RUNTIME

Data flow phải là:

```text
Production Settings
       ↓
BoardCapacity
       ↓
Board Transport / Scan Command
       ↓
Decoder
       ↓
TestView
       ↓
Probe
       ↓
Production TestEngine
```

Không được xảy ra:

```text
Settings = 4 cards
BoardTransport = 2 cards
TestView = 4 cards
Decoder = 128 IO
```

\---

# 24\. START\_SCAN PHẢI DÙNG ĐÚNG SỐ CARD

Protocol có:

```text
8C 00 xx 00
```

Trong đó `xx` liên quan phạm vi/card scan.

Phải tìm chính xác chỗ build command này.

Không được hard-code:

```csharp
0x02
0x04
```

nếu Settings thay đổi.

Phải xác định và báo cáo:

```text
ExpansionCardCount
       ↓
ExpansionModuleCount
       ↓
PhysicalCardCount
       ↓
ScanCardCount
       ↓
START\_SCAN xx
```

\---

# 25\. PROBE PHẢI HOẠT ĐỘNG TRÊN TOÀN BỘ CARD ACTIVE

Nếu capacity đang là:

```text
IO1 ... IO256
```

Probe phải nhận:

```text
IO1
IO32
IO33
IO64
IO65
...
IO256
```

Không được chỉ nhận đến IO64/128.

Nếu IO nằm trên card đã active:

```text
phải hiển thị
phải map THT
phải xác định Card/Local IO đúng
```

\---

# 26\. PHẢI KIỂM TRA CÁCH FRAME MÃ HÓA CARD/BANK

Không được tính global IO chỉ từ byte thấp.

Phải kiểm tra chính xác:

```text
80 xx
81 xx
82 xx
...

A0 xx
A1 xx
A2 xx
...
```

phần nào đại diện:

```text
bank/card
```

và phần nào đại diện:

```text
local IO/index
```

Phải có một hàm trung tâm:

```csharp
int DecodeGlobalIo(
    byte bank,
    byte index,
    BoardCapacity capacity)
```

hoặc abstraction tốt hơn.

Production và Probe cùng dùng `BoardAddressMapper`, nhưng semantics sau đó khác nhau.

\---

# 27\. KIẾN TRÚC CARD/PROBE MONG MUỐN

```text
FTDI RAW RX
      ↓
Frame Parser
      ↓
BoardAddressMapper
      ↓
Mode Router
    /           \\
Production     Probe
Decoder        Decoder
    |             |
TestEngine     ProbeState
    |             |
Fault/PASS        UI
```

Điểm quan trọng:

```text
raw bank/index → global IO
```

phải dùng chung.

Nhưng:

```text
SOURCE/TARGET semantics
```

chỉ thuộc Production.

```text
touched IO semantics
```

chỉ thuộc Probe.

\---

# 28\. GLOBAL IO / CARD / LOCAL IO PHẢI CENTRALIZE

Nên có:

```csharp
CardAddress GetCardAddress(int globalIo)
```

trả:

```csharp
PhysicalCardNumber
LocalIoNumber
GlobalIoNumber
```

Nếu sau khi xác minh đúng:

```text
32 IO / physical card
```

thì boundary cần test:

```text
IO1  → Card1 Local1
IO32 → Card1 Local32
IO33 → Card2 Local1
IO64 → Card2 Local32
IO65 → Card3 Local1
```

Phải kiểm tra 0-based/1-based rất kỹ.

\---

# 29\. PROBE PHẢI CHECK CAPACITY

Sau khi decode global IO:

```text
1 <= GlobalIo <= TotalIoCapacity
```

Nếu frame ra IO thuộc card chưa enable:

```text
bỏ qua / log diagnostic
```

Không:

```text
crash
fault
fake IO display
```

Nếu card đã enable:

```text
IO phải hoạt động bình thường
```

kể cả THT không có mapping.

\---

# 30\. START CARD NUMBER PHẢI ĐƯỢC XÁC MINH

Settings có:

```text
StartCardNumber
```

Phải xác định rõ đây là:

```text
hardware address
UI numbering offset
scan bank offset
```

hay chức năng nào khác.

Nếu:

```text
StartCardNumber = 3
```

phải kiểm tra ảnh hưởng lên:

```text
START\_SCAN
bank decode
global IO
card label
THT mapping
```

\---

# 31\. THT PARSER KHÔNG ĐƯỢC GIỚI HẠN 64/128 IO

Phải search toàn project:

```text
64
128
224
256
320
640
```

và kiểm tra từng nơi.

Đặc biệt tìm:

```csharp
new Pin\[128]
if (io > 128)
for (... <= 128)
Math.Min(..., 128)
```

Nếu là limit IO hard-code phải sửa.

THT có thể chứa IO lớn, ví dụ:

```text
IO224
```

Nếu capacity cho phép, IO đó phải hoạt động đầy đủ trong:

```text
Production
Probe
THT lookup
UI
network mapping
```

\---

# 32\. KHI THAY ĐỔI SỐ CARD, PHẢI RECONFIGURE TOÀN HỆ THỐNG

Chuỗi mong muốn:

```text
Stop current scan
       ↓
Invalidate generation
       ↓
Purge FTDI RX
       ↓
Reset Production decoder
       ↓
Reset Probe decoder
       ↓
Rebuild BoardCapacity
       ↓
Rebuild ActiveCards
       ↓
Validate/Rebuild THT lookup
       ↓
Send board initialization
       ↓
START\_SCAN với card count mới
```

Không được giữ scan worker cũ.

Nếu giảm số card, Probe state thuộc card bị disable phải bị clear.

\---

# 33\. GIỚI HẠN SETTINGS PHẢI THỐNG NHẤT

Phải tìm:

```text
Minimum
Maximum
Clamp
Math.Min
Math.Max
ValidationRule
NumericUpDown Maximum
```

liên quan `ExpansionCardCount`.

Không được:

```text
UI cho nhập 10
backend clamp 4
```

hoặc:

```text
backend hỗ trợ 10
UI chỉ cho 2
```

\---

# 34\. PHẢI BÁO CÁO CÔNG THỨC CARD CUỐI CÙNG

Sau khi phân tích source + protocol + runtime, phải xuất bảng:

|Settings|Expansion Module|Physical Card|Scan Card|Total IO|START\_SCAN xx|
|-:|-:|-:|-:|-:|-:|
|1|xác minh|xác minh|xác minh|xác minh|xác minh|
|2|xác minh|xác minh|xác minh|xác minh|xác minh|
|4|xác minh|xác minh|xác minh|xác minh|xác minh|
|10|xác minh|xác minh|xác minh|xác minh|xác minh|

Không được điền theo suy đoán.

\---

# 35\. TEST CASE BẮT BUỘC — PROBE

## Test 1 — Probe IO không map

Input:

```text
Probe → IO24
```

Expected:

```text
UI: IO (24)
Fault: none
Relay: none
Statistics: unchanged
LOT: unchanged
```

\---

## Test 2 — Probe IO có mapping

THT:

```text
IO11 → BG21 → Black
```

Input:

```text
Probe → IO11
```

Expected:

```text
IO (11) | BG21 | Đen
```

Không fault.

\---

## Test 3 — Release

```text
touch IO11
release IO11
```

Expected:

```text
IO11 xuất hiện
release → IO11 biến mất
Production/config UI không reset
```

\---

## Test 4 — Hai IO

```text
touch IO11
touch IO24
```

Expected:

```text
IO11
IO24
```

Release IO11:

```text
IO11 mất
IO24 còn
```

\---

## Test 5 — False-short regression

Input Probe từng gây:

```text
IO45 → IO7
IO46 → IO7
IO47 → IO7
```

Expected mới:

```text
Probe IO7
TestEngine fault collection = empty
Relay = none
```

\---

## Test 6 — Production thật

Production:

```text
SOURCE IO45
TARGET IO7
```

Nếu THT không mong đợi:

```text
phải báo fault đúng
```

Đây là test chứng minh không vô hiệu hóa short detector.

\---

## Test 7 — Mode switching

```text
Production
→ Probe
→ Production
→ Probe
```

lặp nhiều lần.

Expected:

```text
no crash
no duplicate event
no stale frame
no double scan
FTDI vẫn connected
no false-short
```

\---

# 36\. TEST CASE BẮT BUỘC — CARD MỞ RỘNG

## Test A — boundary đầu tiên

```text
Probe IO1
Probe IO32
```

Phải đúng card/local IO.

\---

## Test B — qua biên card

```text
IO32
IO33
```

Expected:

```text
IO32 = card trước/local cuối
IO33 = card sau/local đầu
```

Không off-by-one.

\---

## Test C — boundary tiếp theo

```text
IO64
IO65
```

kiểm tra bank/card mapping.

\---

## Test D — IO cuối capacity

Probe IO cuối cùng đang được enable.

Expected:

```text
nhận đúng
hiển thị đúng
THT lookup đúng
```

\---

## Test E — vượt capacity

Ví dụ:

```text
TotalIoCapacity = 128
decoded IO = 129
```

Expected:

```text
reject diagnostic
no crash
no fault
no fake IO
```

\---

## Test F — tăng card count

```text
Expansion = 2
```

sau đó:

```text
Expansion = 4
```

Expected:

```text
capacity tăng đúng
card mới active
Probe hoạt động trên card mới
Production scan dùng card mới
```

\---

## Test G — giảm card count

Đang Probe IO thuộc card cuối rồi giảm số card.

Expected:

```text
stop scan
invalidate generation
clear stale Probe state
rebuild capacity
restart scan
```

Không để IO card đã disable còn treo trên UI.

\---

# 37\. TESTVIEW PHẢI HIỂN THỊ CARD ACTIVE ĐÚNG

Ví dụ:

```text
CARD 1   ACTIVE
CARD 2   ACTIVE
CARD 3   ACTIVE
CARD 4   DISABLED
```

Khi Probe chạm IO thuộc Card 3:

```text
CARD 3 highlight Probe
```

và hiển thị:

```text
IO (...)
```

Nếu mapped:

```text
IO (...) | Connector | Pin | Wire | Color
```

Khi release:

```text
Probe highlight mất
Card 3 vẫn ACTIVE
```

\---

# 38\. UI PROBE MONG MUỐN

Ở vùng hiện đang hiển thị thiết bị có thể thay bằng:

```text
CHẾ ĐỘ ĐẦU DÒ
```

Bình thường:

```text
Sẵn sàng dò chân
```

Khi Probe:

```text
ĐANG DÒ: IO (24)
```

Nếu mapped:

```text
ĐANG DÒ: IO (11) | BG21 | Đen
```

Nếu nhiều IO:

```text
IO (11) | BG21 | Đen
IO (24)
```

Không được dùng overlay che nội dung test.

Probe phải là vùng UI song song.

\---

# 39\. PRODUCTION CONFIGURATION KHÔNG ĐƯỢC BỊ ẨN KHI PROBE

Khi đang hiển thị:

```text
Model
PartNumber
Vehicle
THT configuration
Current test wiring
Statistics
LOT
```

chạm Probe:

```text
mọi thông tin trên giữ nguyên
```

chỉ thêm Probe state.

Release:

```text
Probe state mất
mọi nội dung test giữ nguyên
```

\---

# 40\. CÁC PHẦN KHÔNG ĐƯỢC PHÁ

Giữ nguyên nếu đang hoạt động tốt:

```text
FTDI connection
THT parser
Production continuity
Resistance
Keysight
Relay
PASS/FAIL
History SQLite
Statistics
LOT
Master Sample
Settings
Label printing
```

Chỉ refactor phần cần thiết để:

```text
Probe deterministic
Card capacity dynamic
Address mapping centralized
```

\---

# 41\. KIỂM TRA PRODUCTION PROTOCOL SAU KHI SỬA

Nên capture TX/RX thật để xác nhận:

```text
8D 00 00 00
8A 01 01 01
91 00 00 00
90 00 00 30
8C 00 xx 00
```

và relay:

```text
8E 00 00 01
8E 00 00 02
8E 00 00 00
```

Phải báo cáo:

```text
Settings value
→ ScanCardCount
→ START\_SCAN xx
```

bằng dữ liệu runtime thật.

\---

# 42\. KHÔNG ĐƯỢC CHỈ SỬA CODE MẪU

Yêu cầu thực hiện:

1. giải nén toàn bộ project;
2. đọc toàn bộ source;
3. lập sơ đồ data flow;
4. tìm tất cả hard-code IO/card;
5. tìm duplicate event subscription;
6. xác minh board addressing;
7. sửa Transport/Decoder/Router/ViewModel/XAML khi cần;
8. sửa card capacity;
9. sửa mode switching;
10. sửa Probe state;
11. sửa THT lookup duplicate;
12. compile;
13. sửa compile errors;
14. kiểm tra nullability;
15. kiểm tra async/cancellation;
16. chạy test vectors;
17. đóng gói project hoàn chỉnh.

Không chỉ trả lời:

```text
hãy sửa hàm X như sau
```

mà phải sửa trực tiếp project.

\---

# 43\. BÁO CÁO SAU KHI SỬA PHẢI CÓ

Phải ghi rõ:

```text
1. Nguyên nhân lỗi Probe cũ
2. Những file đã sửa
3. Production decoder mới hoạt động ra sao
4. Probe decoder mới hoạt động ra sao
5. Raw frame router nằm ở đâu
6. BoardAddressMapper tính global IO thế nào
7. ExpansionCardCount thật sự có nghĩa gì
8. PhysicalCardCount tính ra sao
9. ScanCardCount tính ra sao
10. TotalIoCapacity tính ra sao
11. START\_SCAN xx tính thế nào
12. Probe release xử lý thế nào
13. Mapping IO → THT thế nào
14. Duplicate IO mapping được giữ thế nào
15. Stale frame được chống thế nào
16. Probe được chặn khỏi fault/relay ở đâu
17. TestView sinh card động thế nào
18. Kết quả từng test case
19. Những vấn đề còn cần hardware verify
```

\---

# 44\. TÊN BẢN BUILD MONG MUỐN

Có thể đặt:

```text
JBZUniversalTester\_V12\_8\_PROBE\_CARD\_ORIGINAL\_BEHAVIOR
```

hoặc version mới hơn.

\---

# 45\. KẾT QUẢ CUỐI CÙNG MONG MUỐN

Khi người dùng vào Settings chọn số card mở rộng:

```text
Save
```

thì toàn bộ hệ thống phải đồng bộ:

```text
Settings
   ↓
BoardCapacity
   ↓
BoardTransport
   ↓
START\_SCAN
   ↓
BoardAddressMapper
   ↓
Production/Probe Decoder
   ↓
TestView ActiveCards
   ↓
THT lookup
```

Tất cả card được cấu hình phải:

```text
ACTIVE
SCANNED
PROBE-CAPABLE
PRODUCTION-CAPABLE
```

Khi lấy đầu dò chạm vào **bất kỳ IO nào thuộc bất kỳ card đang active**:

```text
nhận đúng Global IO
nhận đúng Physical Card
nhận đúng Local IO
map đúng THT
hiển thị đúng Wire/Color
```

và tuyệt đối:

```text
không false-short
không relay fault
không product FAIL
không tăng LOT
không tăng FAIL statistics
```

Khi rút đầu dò:

```text
chỉ Probe state biến mất
Card vẫn Active
Production/configuration UI vẫn giữ nguyên
```

Production thật vẫn phải phát hiện đúng:

```text
OPEN
WRONG WIRING
SHORT
PASS
```

Đây là tiêu chí cuối cùng để coi phần Probe + Card mở rộng của `JBZUniversalTester\_V12\_7\_0` đã được sửa đúng theo hành vi phần mềm gốc.

