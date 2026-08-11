\# YÊU CẦU CODEX — REFACTOR JBZ V15\_2\_0 THÀNH 2 ỨNG DỤNG WINDOWS ĐỘC LẬP



\## 0. Mục tiêu bắt buộc



Audit toàn bộ source hiện tại của `V15\_2\_0`.



Hiện tại source đang có quá nhiều logic gộp chung giữa:



\* bo Universal Tester nguyên bản dùng trên Raspberry Pi;

\* phần cứng/phần mềm Windows native hiện tại;

\* nhiều nhánh `Pi / Windows / Legacy / Production` nằm chung ViewModel, Service và state machine.



Kiến trúc này phải được loại bỏ.



\### Không được tiếp tục làm kiểu:



```csharp

if (IsPiBoard)

{

&#x20;   ...

}

else

{

&#x20;   ...

}

```



hoặc:



```csharp

if (Platform == "Pi")

...

else if (Platform == "Windows")

...

```



rải rác trong:



\* TestViewModel

\* SerialService

\* ProductionService

\* ResistanceService

\* BoardService

\* ModelService

\* MainWindow

\* TestWindow



Không dùng một executable rồi chọn:



```text

Pi Mode

Windows Mode

```



\## 1. Phải tách thành HAI ứng dụng Windows độc lập



Cùng nằm trong một Solution nhưng build thành hai `.exe`.



Đề xuất:



```text

JBZ.sln

│

├── JBZ.PiBoard.PC

│   └── JBZ.PiBoard.PC.exe

│

├── JBZ.Windows.Integrated

│   └── JBZ.Windows.Integrated.exe

│

├── JBZ.Common

│

├── JBZ.Serial

│

├── JBZ.Logging

│

└── Tests

```



Tên project có thể điều chỉnh theo convention hiện tại, nhưng kiến trúc phải tương đương.



\---



\# PHẦN A — JBZ.PiBoard.PC



Đây là phần mềm chạy trên Windows PC nhưng điều khiển \*\*bo Universal Tester nguyên bản của hệ thống Raspberry Pi\*\*.



Nó phải tái tạo đúng behavior của phần mềm Production trên Pi.



Không sử dụng `/dev/ttyAMA2` trên Windows.



Bo Pi cũ phải được đưa ra PC qua USB-UART/COM phù hợp.



\## A1. Board discovery



Trace thực tế xác nhận bo trả:



```text

Universal Tester V 1.19 Beta III

```



Protocol:



```text

115200 baud

8 data bits

No parity

1 stop bit

No flow control

CRLF

```



Discovery:



```text

PC → \*IDN?\\r\\n

BOARD → Universal Tester V 1.19 Beta III\\r\\n

```



Cho phép nhận các ID hợp lệ dạng:



```text

Universal Tester...

UniversalTester...

```



Không dùng:



```csharp

File.Exists("COM3")

```



để xác định COM trên Windows.



Dùng `SerialPort.GetPortNames()` hoặc Windows serial enumeration.



Mở từng COM ứng viên → gửi `\*IDN?` → xác nhận board.



Không được nhận nhầm:



\* WP-100

\* GT800

\* thiết bị USB serial khác.



\---



\# A2. UART parser bắt buộc dùng persistent buffer



Trace chứng minh hai trường hợp đều xảy ra.



Một command bị chia thành nhiều lần read:



```text

READ #1:

:MODELNAME,32213



READ #2:

7,231\\r\\n

```



phải ghép thành:



```text

:MODELNAME,322137,231

```



Ngược lại, một read có thể chứa nhiều command:



```text

:START,ON\\r\\n:MEASURE\\r\\n:CLEAR\\r\\n

```



Phải parse thành ba message riêng.



WP-100 cũng có trường hợp:



```text

READ #1

:RESULT,-83.5,-73.



READ #2

4,-83.5,-82.0,0.0,0.0\\r\\n

```



Do đó implement:



```text

persistent byte/text buffer

&#x20;     ↓

append data

&#x20;     ↓

tìm CRLF

&#x20;     ↓

extract complete frames

&#x20;     ↓

giữ remainder

```



Không xử lý `SerialPort.DataReceived` như một command hoàn chỉnh.



\---



\# A3. Model handshake



Trace xác nhận:



```text

PC → \*IDN?

BOARD → Universal Tester V 1.19 Beta III



PC → :MODELNAME?

BOARD → :MODELNAME,322137,231

```



Sau đó software Production download model khi cần.



Các command Production đã trace được gồm:



```text

:MODEL

:PINDATA

:ARRAY

:CON

:CONNECTOR

:FINISH

:RESET

```



Board xác nhận ví dụ:



```text

:OK,MODEL

:OK,FINISH,...

```



Không thay đổi formatter/model serialization hiện tại nếu V15\_2\_0 đã match trace.



Audit lại từng command trước khi refactor.



\---



\# A4. Production START sequence



Trace xác nhận thứ tự:



```text

PC → :START



BOARD → :START,ON



BOARD → :MEASURE



PC → :MAXEXT,0

```



Không gửi `:MAXEXT,0` ngay sau `:START`.



Phải đợi firmware gửi:



```text

:MEASURE

```



mới gửi `:MAXEXT,...`.



Firmware còn có thể gửi:



```text

:CLEAR

```



trong cùng packet.



\---



\# A5. Circuit test



Các message Production cần được giữ:



```text

:OPEN,...

:SHORT,...

:OTHER,...

:CIRCUIT,...

:SEQ,...

:REMOVAL

:UNCONNECT

```



Ví dụ trace lỗi nối chéo thực tế:



Model mong:



```text

19 ↔ 283

20 ↔ 284

```



nhưng board báo:



```text

:OTHER,283,20

:OTHER,284,19

:CIRCUIT,1

```



Đây là Wrong Connection / Cross Connection.



Không được quy tất cả thành OPEN.



\---



\# A6. `:OPEN` không tự động đồng nghĩa Final FAIL



Trace Production chứa rất nhiều:



```text

:OPEN,pin

```



trong quá trình live monitoring.



Do đó không viết:



```csharp

if (message.StartsWith(":OPEN"))

&#x20;   FinalFail();

```



Phải xử lý dựa vào:



\* current production state;

\* expected pin;

\* connector state;

\* current sequence;

\* fault priority;

\* CLIP/common-pin behavior.



\---



\# A7. CLIP / common point



Giữ riêng logic CLIP.



Các pin kiểu:



```text

A0

A1

A2

A3

...

```



trong nhóm CLIP có thể chia sẻ điểm common.



Ví dụ khi:



```text

A1 ↔ A0

```



đã được nối bởi sản phẩm, các A2/A3... có thể biến mất khỏi live OPEN list.



Không được coi việc chúng biến mất là nhiều connection độc lập.



Một điểm CLIP/common chỉ được tính một lần theo logic model.



Khi tháo toàn bộ sản phẩm khỏi jig, toàn bộ pin CLIP phải hiện trở lại.



Không sửa behavior này trong Phase 1 nếu logic hiện tại đang đúng.



\---



\# A8. Fault/removal sequence



Sau sản phẩm NG hoặc kết thúc cycle, trace có:



```text

PC → :UNCONNECT,500,200

```



Board trả:



```text

:REMOVAL

...

:UNCONNECT

```



Chỉ sau khi xác nhận tháo sản phẩm mới chuyển về READY cycle tiếp theo.



Relay/output phải được trả về trạng thái ban đầu sau mỗi cycle.



Không để output/relay giữ trạng thái kích qua cycle tiếp theo.



\---



\# A9. Waterproof trong Production Pi-board



Production sử dụng WP-100 riêng.



Trace Pi:



```text

/dev/ttyUSB1

115200

```



Trên Windows phải map thành COM riêng.



Command:



```text

:TEST,1,1,0,0,1000,500

```



Response sequence:



```text

:PRESS,...

:WAIT,...

:RESULT,...

```



Setup thực tế:



```ini

\[SPEC]

PressMin=25

min=5



\[Time]

Press=1

Wait=0.5

```



Hai tham số:



```text

1000 = 1 second

500 = 0.5 second

```



Kết quả ví dụ:



```text

:RESULT,-83.5,-73.4,-83.5,-82.0,0.0,0.0

```



Với threshold:



```text

abs(before - after) > 5

```



thì channel đó FAIL.



Trace chứng minh Production cho phép:



```text

Waterproof FAIL

→ retry

→ PASS

→ tiếp tục cycle

```



Không tự động Final NG ngay khi lần test waterproof đầu tiên FAIL nếu workflow gốc cho retry.



\---



\# A10. GT800



Trace setup:



```ini

\[Barcode]

Machine=GT800

Port=/dev/ttyUSB0

Use=1

```



Pi dùng:



```text

/dev/ttyUSB0

9600

```



Trên Windows phải là một COM role riêng.



Không dùng COM của board Universal Tester.



Không dùng COM của WP-100.



Nếu có FTDI serial-number thì ưu tiên hardware identity thay vì COM number cố định.



Trace trước đã thấy các FTDI serial như:



```text

A9F6EJLN

A10JQXSO

```



Nhưng không hard-code serial này nếu chưa có config máy thực tế.



Tạo configurable hardware mapping.



\---



\# A11. Resistance INTERNAL — Production protocol



Đây là dữ liệu đã được trace trực tiếp từ Production V3.



Sau:



```text

:SEQ,END

:CIRCUIT,0

```



Production đọc nhiệt độ:



```text

PC → :TEMPER

BOARD → :TEMPER,1711

```



sau đó đo resistance.



Trace thực tế:



```text

PC → :RESISTORTEST,6,0,1

BOARD → :READRESISTOR

BOARD → :RESISTOR,3962



PC → :RESISTORTEST,7,0,1

BOARD → :READRESISTOR

BOARD → :RESISTOR,3962



PC → :RESISTORTEST,2,0,1

BOARD → :READRESISTOR

BOARD → :RESISTOR,2599



PC → :RESISTORTEST,3,0,1

BOARD → :READRESISTOR

BOARD → :RESISTOR,2708

```



Không gửi:



```text

:READRESISTOR

```



từ PC.



Đó là response/status của board.



Production command format là:



```text

:RESISTORTEST,{channel},0,1

```



Không dùng formatter của BoardDiags.



\---



\# A12. Công thức RAW ADC → Ohm



Trace + production history xác nhận:



```text

R = 10000 \* raw / (4095 - raw)

```



Ví dụ:



```text

raw = 3961

```



cho:



```text

295597.01 Ω

```



khớp production CSV:



```text

R1: 20000.00 < 295597.01 < 650000.00

```



Implement bằng `double`.



Phải xử lý an toàn:



```text

raw <= 0

raw >= 4095

division by zero

invalid ADC

timeout

```



Không được coi:



```text

:RESISTOR,3961

```



là 3961 Ω.



\---



\# A13. Resistance channels của model 322137



Production trace xác nhận:



```text

R1 → board channel 6

R2 → board channel 7

R3 → board channel 2

R4 → board channel 3

```



Không giả định:



```text

R1 = channel 0

R2 = channel 1

R3 = channel 2

R4 = channel 3

```



Mapping phải lấy từ model/setup/logic gốc.



Audit kỹ V15\_2\_0 xem hiện đang transform pin/channel như thế nào.



Không hard-code `6,7,2,3` cho mọi model.



\---



\# A14. Thermistor



Model 322137 có:



```text

R3 = SEMITEC

R4 = AMPENOL

```



và Production đọc:



```text

:TEMPER

```



trước khi đo.



Các bảng thermistor/config hiện có phải được giữ.



Không thay bằng một MIN/MAX resistance cố định.



Phải audit chính source/database/table hiện tại để xác nhận chính xác:



```text

TEMPER raw

→ board temperature

→ thermistor lookup/interpolation

→ tolerance

→ MIN/MAX

→ PASS/FAIL

```



Trace cho thấy temperature có liên quan trực tiếp đến evaluation R3/R4.



Không tự nghĩ ra công thức mới nếu source gốc đã có table.



\---



\# A15. Production sequence phải là state machine



Không dùng nhiều Boolean rời rạc:



```text

\_isTesting

\_isCircuit

\_isWaterproof

\_isResistance

\_isRemoval

...

```



để điều khiển flow phức tạp.



Tạo state rõ ràng, ví dụ:



```csharp

enum ProductionState

{

&#x20;   Idle,

&#x20;   DiscoveringBoard,

&#x20;   LoadingModel,

&#x20;   Ready,

&#x20;   Starting,

&#x20;   CircuitTesting,

&#x20;   WaterproofTesting,

&#x20;   ResistanceTemperature,

&#x20;   ResistanceTesting,

&#x20;   EvaluatingResult,

&#x20;   WaitingRemoval,

&#x20;   Resetting,

&#x20;   Fault

}

```



Tên có thể thay đổi nhưng concept phải rõ.



State transition phải log được.



\---



\# PHẦN B — JBZ.Windows.Integrated



Đây là phần mềm Windows chính dành cho kiến trúc phần cứng Windows native hiện tại.



Nó phải được \*\*tách hoàn toàn khỏi PiBoard protocol\*\*.



Không được reference:



```text

PiBoardProductionService

PiBoardResistanceProtocol

PiBoardModelDownloader

PiBoardWaterproofCoordinator

PiBoardBoardDiscovery

```



trừ các interface trung tính ở Common.



Không được có runtime switch:



```text

UsePiBoard

LegacyPi

PiCompatible

PiMode

```



trong executable Windows Integrated.



Giữ toàn bộ hardware/service/state machine Windows-native hiện tại của V15\_2\_0 trong project này.



Nếu một class hiện chứa cả Pi và Windows:



```csharp

public class TestViewModel

{

&#x20;   Pi logic...

&#x20;   Windows logic...

}

```



thì phải tách thành hai implementation.



Ví dụ:



```text

JBZ.PiBoard.PC

&#x20;   PiProductionViewModel

&#x20;   PiProductionCoordinator



JBZ.Windows.Integrated

&#x20;   WindowsProductionViewModel

&#x20;   WindowsProductionCoordinator

```



Không dùng inheritance phức tạp chỉ để tránh duplicate vài chục dòng.



Ưu tiên separation rõ ràng.



\---



\# PHẦN C — Những gì được phép dùng chung



Chỉ chia sẻ code thật sự platform-neutral.



Ví dụ:



```text

CRC/checksum helper

logging abstraction

date/time helper

basic model DTO

result DTO

CSV helper

SQLite abstraction

CRLF frame decoder

serial transport primitive

validation utilities

```



Có thể có:



```csharp

ISerialTransport

ILineFramer

ILogger

IClock

```



Nhưng:



```text

Production flow

Board commands

Resistance commands

Waterproof workflow

Fault state

Model download state

```



không được đưa vào Common nếu hai platform có behavior khác nhau.



Nguyên tắc:



> Chỉ reuse phần đã được chứng minh giống nhau. Không reuse dựa trên suy đoán.



\---



\# PHẦN D — Cấu hình phải tách hoàn toàn



PiBoard PC:



```text

%APPDATA%\\JBZ\\PiBoardPC\\

%LOCALAPPDATA%\\JBZ\\PiBoardPC\\

```



Windows Integrated:



```text

%APPDATA%\\JBZ\\WindowsIntegrated\\

%LOCALAPPDATA%\\JBZ\\WindowsIntegrated\\

```



Không đọc chung:



```text

settings.json

uart\_cache.json

last\_model.json

device\_mapping.json

```



trừ dữ liệu business thực sự cần share và có schema rõ.



Không để Pi-board COM cache làm Windows Integrated mở nhầm thiết bị.



Không để Windows Integrated config làm PiBoard PC bỏ qua board discovery.



\---



\# PHẦN E — Port ownership



Mỗi physical COM phải có role rõ:



```text

UniversalTesterBoard

WP100

GT800

Other

```



Tạo hardware inventory/runtime diagnostics.



Ví dụ:



```text

COM3

Role: UniversalTesterBoard

115200

ID: Universal Tester V 1.19 Beta III



COM5

Role: WP100

115200



COM7

Role: GT800

9600

```



Không cho hai service mở cùng COM.



Khi COM bị software khác giữ phải báo rõ:



```text

COMx đang được ứng dụng khác sử dụng

```



thay vì:



```text

Không tìm thấy board

```



\---



\# PHẦN F — BoardDiags không được trộn với Production



Trace BoardDiags đã xác nhận API manual riêng.



Ví dụ resistance:



```text

:RESISTORTEST,0,200,1,0

...

:RESISTORTEST,7,200,1,0

```



trong khi Production dùng:



```text

:RESISTORTEST,6,0,1

```



Do đó không tạo chung một formatter sai.



BoardDiags còn có:



```text

:VOLTAGETEST

:READVOLTAGE

:VOLTAGE



:AMPARETEST

:READAMPARE

:AMPARE



:OUTPUTTEST

:OUTPUT



:INPUTTEST

:ACK

:INPUT



:TEST,6,...    // OPEN diagnostic

:TEST,5,...    // SHORT diagnostic

```



Lưu ý firmware dùng chữ:



```text

AMPARE

```



Không tự sửa thành `AMPERE`.



Trong Phase 1:



\* không nhập BoardDiags vào Production;

\* không nhập BoardDiags vào Windows Integrated;

\* giữ code/protocol riêng để Phase sau tạo `JBZ.BoardDiags.exe`.



\---



\# PHẦN G — Các ứng dụng Pi khác cũng phải giữ độc lập



Hệ Pi gốc có các chương trình riêng:



```text

FirmwareDownloader

CreatorModel

BoardDiags

Waterproof

UniversalTester Production

```



Không được biến chúng thành một MainWindow chứa 5 tab rồi dùng chung state machine.



Phase sau sẽ tách thành:



```text

JBZ.FirmwareDownloader.exe

JBZ.CreatorModel.exe

JBZ.BoardDiags.exe

JBZ.Waterproof.exe

```



Trong Phase 1 chỉ cần:



1\. không làm hỏng source/protocol của chúng;

2\. loại bỏ dependency ngược vào Production;

3\. chuẩn bị Core/Transport đủ sạch để port sau.



\---



\# PHẦN H — Waterproof standalone khác Production waterproof



Không được mặc định config của `Waterproof.exe` standalone giống config Waterproof bên trong Production.



Trace cho thấy device path/role có thể khác nhau giữa ứng dụng.



Mỗi app phải có config riêng.



Protocol có thể reuse nếu đã xác nhận giống, nhưng connection profile không được share cứng.



\---



\# PHẦN I — Logging bắt buộc



Mỗi ứng dụng tạo log riêng.



PiBoard PC phải log:



```text

timestamp

thread

state

port

TX/RX

raw frame

parsed frame

model

part number

current resistor

waterproof attempt

fault

state transition

exception

```



Ví dụ:



```text

17:12:04.519 \[COM3]\[TX]\[Resistance:R1]

:RESISTORTEST,6,0,1



17:12:05.278 \[COM3]\[RX]\[Resistance:R1]

:RESISTOR,3962



17:12:05.279 \[Resistance:R1]

RAW=3962

OHM=297894.74

MIN=20000

MAX=650000

RESULT=PASS

```



Không log password/secret nếu có.



\---



\# PHẦN J — UI



Phase 1 là refactor architecture.



Không redesign giao diện lớn.



Hai app phải nhìn rõ ngay application đang chạy:



```text

JBZ — Pi Board PC

```



và:



```text

JBZ — Windows Integrated

```



Không dùng dropdown `Platform`.



Không cho operator đổi Pi ↔ Windows trong lúc runtime.



\---



\# PHẦN K — Build



Mỗi app build/publish riêng:



```text

dist/

├── PiBoardPC/

│   └── JBZ.PiBoard.PC.exe

│

└── WindowsIntegrated/

&#x20;   └── JBZ.Windows.Integrated.exe

```



Nếu có installer:



```text

JBZ-PiBoardPC-Setup.exe

JBZ-WindowsIntegrated-Setup.exe

```



Không đóng gói hai executable thành một file rồi runtime chọn mode.



\---



\# PHẦN L — Không để backup/source rác trong project



Không tạo:



```text

\*.bak

\*.backup

\*\_old.cs

\*\_copy.cs

\*\_fixed.cs

TestViewModel\_2.cs

```



trong project compile.



Nếu Codex cần backup thì dùng Git.



Các thư mục:



```text

bin/

obj/

publish/

trace/

temp/

backup/

```



không được đưa vào compile item.



Clean warnings liên quan duplicate class/resource.



\---



\# PHẦN M — Test bắt buộc



Sau refactor phải có test riêng.



\## PiBoard PC



Test:



```text

IDN fragmented response

multiple CRLF messages in one read

MODELNAME fragmented

START → MEASURE → MAXEXT

OTHER wrong connection

OPEN live state

CIRCUIT=0

CIRCUIT=1

REMOVAL

UNCONNECT

TEMPER

RESISTORTEST

READRESISTOR

RESISTOR

ADC → Ohm

WP100 fragmented RESULT

WP FAIL → retry → PASS

COM occupied

COM disconnected mid-cycle

board reconnect

```



\## Windows Integrated



Chạy toàn bộ test hiện có của nhánh Windows.



Không được để việc xóa Pi logic làm thay đổi behavior của Windows Integrated.



\---



\# PHẦN N — Acceptance criteria quan trọng nhất



Không coi công việc hoàn thành nếu chỉ:



```text

move file

rename namespace

```



nhưng vẫn còn:



```csharp

if (IsPi)

```



trong shared production flow.



Sau refactor, tìm toàn solution:



```text

IsPi

PiMode

LegacyPi

UsePiBoard

PlatformMode

```



Các tên này chỉ được tồn tại bên trong project `JBZ.PiBoard.PC` hoặc migration layer thật sự cần thiết.



`JBZ.Windows.Integrated` không được phụ thuộc Pi production implementation.



Ngược lại `JBZ.PiBoard.PC` không được gọi Windows-native board implementation.



Hai app phải có thể:



```text

build riêng

run riêng

config riêng

log riêng

test riêng

release riêng

```



\---



\# PHẦN O — Trình tự Codex phải thực hiện



Trước khi sửa code:



1\. Audit dependency graph V15\_2\_0.

2\. Liệt kê class nào đang chứa mixed Pi/Windows logic.

3\. Liệt kê protocol/service nào chỉ thuộc Pi.

4\. Liệt kê service nào chỉ thuộc Windows Integrated.

5\. Liệt kê code thật sự có thể share.

6\. Đề xuất project tree sau refactor.

7\. Sau đó mới sửa.



Ưu tiên refactor theo thứ tự:



```text

Transport

→ Protocol

→ Hardware discovery

→ Configuration

→ Production coordinator/state machine

→ Resistance

→ Waterproof

→ ViewModel

→ UI wiring

→ tests

→ build scripts

```



Không bắt đầu bằng sửa XAML.



\---



\# PHẦN P — Báo cáo cuối cùng Codex phải trả về



Sau khi hoàn tất, báo cáo:



```text

1\. Danh sách file tạo mới

2\. Danh sách file xóa

3\. Danh sách file di chuyển

4\. Danh sách mixed Pi/Windows logic đã loại bỏ

5\. Dependency graph mới

6\. Project reference graph mới

7\. PiBoard protocol implementation

8\. Windows Integrated implementation

9\. COM discovery

10\. Resistance implementation

11\. Waterproof implementation

12\. Các test đã chạy

13\. Kết quả build

14\. Warning/error còn lại

15\. Hạng mục chưa đủ trace — KHÔNG được tự suy đoán

```



Nếu một protocol chưa đủ dữ liệu trace:



> đánh dấu TODO/UNKNOWN rõ ràng.



Không tự chế command để làm cho chương trình “có vẻ chạy”.



\---



\# NGUYÊN TẮC CUỐI CÙNG



Mục tiêu của Phase 1:



```text

&#x20;                 JBZ V15\_2\_0

&#x20;                      │

&#x20;            REMOVE MIXED LOGIC

&#x20;                      │

&#x20;         ┌────────────┴────────────┐

&#x20;         │                         │

&#x20;JBZ.PiBoard.PC            JBZ.Windows.Integrated

&#x20;         │                         │

&#x20;Original Pi board          Windows native system

&#x20;protocol preserved         native logic preserved

&#x20;         │                         │

&#x20;independent EXE            independent EXE

&#x20;independent config         independent config

&#x20;independent state          independent state

```



Không cố gắng làm cho hai hệ thống giống nhau.



Không hợp nhất chỉ vì cùng có khái niệm:



```text

START

PASS

FAIL

Resistance

Waterproof

```



Chỉ chia sẻ infrastructure khi implementation thực sự giống nhau.



\*\*Ưu tiên số 1: cô lập hoàn toàn hai hệ thống trước. Sau khi hai executable build/run ổn định mới tiếp tục Phase 2 với FirmwareDownloader, CreatorModel, BoardDiags và Waterproof standalone.\*\*



