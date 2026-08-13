MASTER TASK RIÊNG — KHÔI PHỤC ĐẦU DÒ REALTIME + XÓA CỘT KẾT NỐI MẠNG + TỐI ƯU SCAN NHIỀU CARD

PROJECT: JBZUniversalTester



============================================================

MỤC TIÊU

============================================================



Hiện tại chức năng kiểm tra bằng ĐẦU DÒ đã bị regression và không còn hoạt động đúng.



Yêu cầu chính xác:



1\. Khi đầu dò chạm vào bất kỳ chân/I/O nào:

&#x20;  → nhận ngay I/O đó.

&#x20;  → tìm endpoint tương ứng trong model/THT.

&#x20;  → hiện đúng MỘT DÒNG của endpoint đó trên TestWindow.



2\. Dòng phải hiển thị các thông tin có thật:

&#x20;  - I/O

&#x20;  - CONNECTOR

&#x20;  - PIN

&#x20;  - TÊN DÂY

&#x20;  - CỠ DÂY

&#x20;  - #1/#2/#3/#4 màu dây nếu có

&#x20;  - trạng thái/loại hiện có nếu cần.



3\. Khi nhấc đầu dò khỏi chân:

&#x20;  → dòng đầu dò biến mất.



4\. Đầu dò KHÔNG được:

&#x20;  - khóa TestWindow;

&#x20;  - hiện popup lỗi;

&#x20;  - báo CHẬP MẠCH;

&#x20;  - báo ĐẤU SAI;

&#x20;  - tăng bộ đếm lỗi;

&#x20;  - ghi fault history;

&#x20;  - kích relay;

&#x20;  - dừng scan;

&#x20;  - kết thúc cycle;

&#x20;  - thay đổi ClipMemory;

&#x20;  - làm ảnh hưởng PASS/FAIL của sản phẩm.



5\. Trong khi đầu dò hoạt động:

&#x20;  → board vẫn scan liên tục ở tốc độ tối đa.

&#x20;  → lỗi dây thật / chập thật của sản phẩm vẫn phải phát hiện nhanh và đúng.



6\. XÓA HOÀN TOÀN cột:

&#x20;  KẾT NỐI MẠNG



&#x20;  khỏi TestWindow.



7\. Khi model có nhiều card mở rộng:

&#x20;  → tốc độ scan không được giảm rõ rệt do UI;

&#x20;  → không lag;

&#x20;  → không drop frame;

&#x20;  → wrong connection / short detection vẫn phản ứng nhanh.



============================================================

PHẦN 1 — RÀ SOÁT REGRESSION ĐẦU DÒ TRƯỚC KHI SỬA

============================================================



Không được tự viết một ProbeMode mới trước khi tìm logic cũ.



Search toàn solution:



rg -n --hidden --glob "!bin/\*\*" --glob "!obj/\*\*" `

"Probe|ProbeIo|ProbeInput|TestProbe|Inspection|ContactProbe|CurrentIo|ActiveIo|RawIo|IOState|FaultConfirmation|Short|Wrong|BuildRows|VisibleRows|KẾT NỐI MẠNG|ConnectionText" .



Nếu repository có git:



git log --all --oneline

git diff

git blame



Tìm version trước đây mà đầu dò còn hoạt động.



Phải xác định:



Board frame

&#x20;↓

Probe contact được nhận ở đâu?

&#x20;↓

I/O nào được coi là probe/common?

&#x20;↓

endpoint được lookup bằng cách nào?

&#x20;↓

row được đưa lên TestWindow bằng collection nào?

&#x20;↓

tại sao bản hiện tại lại đi vào Short/Wrong fault?



Báo ROOT CAUSE thật trước khi sửa.



============================================================

PHẦN 2 — KHÔNG ĐƯỢC NHẦM PROBE VỚI SHORT THẬT

============================================================



Đây là yêu cầu cực kỳ quan trọng.



KHÔNG được chữa bằng:



DisableShortDetection = true



hoặc:



if (probeActive)

&#x20;   ignore all shorts;



SAI.



Chỉ được suppress Fault đối với electrical relation được xác định chắc chắn là do ĐẦU DÒ.



Short/Wrong thật giữa các dây sản phẩm vẫn phải phát hiện.



Phải trace cách bản gốc hoặc project cũ phân biệt:



PROBE CONTACT



với:



PRODUCT SHORT / WRONG CONNECTION.



Có thể dựa trên:

\- Probe common I/O;

\- dedicated probe input;

\- configuration;

\- endpoint type;

\- special hardware channel;

\- hoặc mechanism thực tế đang có.



KHÔNG ĐƯỢC ĐOÁN.



Nếu source hiện tại không có đủ thông tin để phân biệt probe với short thật:

ghi rõ:



NEEDS VERIFICATION



và tìm implementation cũ/trace trước khi bỏ fault.



============================================================

PHẦN 3 — PROBE LÀ PRESENTATION REALTIME, KHÔNG PHẢI PRODUCT FAULT

============================================================



Tách hai luồng:



&#x20;                ┌→ Production Fault Evaluation

BOARD → PARSER → ENGINE

&#x20;                └→ Probe Observation



Probe Observation chỉ phục vụ:



ProbeEndpointChanged



→ TestWindow.



Không đưa Probe endpoint vào:



ProductionFaultConfirmationGate

WrongConnectionConfirmed

ShortConfirmed

Fail()

FaultHistory

RelayFaultSequence



============================================================

PHẦN 4 — HÀNH VI KHI CHẠM ĐẦU DÒ

============================================================



Ví dụ model có:



IO23

Connector = 5

Pin = 7

WireName = BG1

Gauge = 0.5

Color = R/Y



Đầu dò chạm IO23.



TestWindow phải hiện:



LOẠI | I/O | CONNECTOR | PIN | TÊN DÂY | CỠ DÂY | #1 | #2 | #3 | #4

...

&#x20;      23       5          7      BG1       0.5      R    Y



Không hiện:



IO23 <-> ProbeIO



Không hiện:



SOURCE → TARGET



Không hiện:



CHẬP MẠCH



Không hiện popup.



============================================================

PHẦN 5 — NHẤC ĐẦU DÒ RA

============================================================



Probe contact present:

→ row visible.



Probe contact lost:

→ sau debounce rất ngắn/confirmation phù hợp

→ row removed.



Đây là transient behavior.



KHÔNG giống CLIP MEMORY.



ĐẦU DÒ:

touch → show

release → hide



CLIP:

confirmed → latched until product removed



NORMAL NETWORK:

connected → hide expected row

open → restore expected row



Ba loại state này phải tách riêng.



============================================================

PHẦN 6 — KHÔNG RESET TEST KHI NHẤC ĐẦU DÒ

============================================================



Nhấc probe chỉ:



ProbeCurrentEndpoint = null



Không:



ResetCycle()

ClearClipMemory()

ResetCounters()

RestartBoard()

ReloadModel()



============================================================

PHẦN 7 — ĐẦU DÒ KHÔNG ĐƯỢC DỪNG SCAN

============================================================



Trong probe operation:



ScanRunning phải luôn true.



Không gọi:



StopScanAsync()

8D 00 00 00



do probe.



Không vào ExclusiveOperation.



Probe detection phải hoạt động trực tiếp từ continuous Production scan.



============================================================

PHẦN 8 — KHÔNG POPUP

============================================================



Search toàn bộ call path từ probe relation tới:



FaultConfirmationWindow

MessageBox

ShowDialog

Show

FaultPopup

NG dialog



Không được gọi bất kỳ modal dialog nào chỉ do probe.



TestWindow luôn phải thao tác được.



============================================================

PHẦN 9 — KHÔNG KHÓA UI THREAD

============================================================



Probe contact có thể thay đổi rất nhanh.



Không:



Dispatcher.Invoke(...)

cho từng byte.



Không:



DataGrid.Items.Refresh()



Không:



CollectionView.Refresh()



Không rebuild toàn table.



Probe chỉ phát delta:



old probe = IO23

new probe = IO40



UI:



remove IO23 probe row

add IO40 probe row



============================================================

PHẦN 10 — CACHE IO → MODEL ENDPOINT

============================================================



Khi load model, build một lần:



Dictionary<int, EndpointRecord> EndpointByIo



hoặc mapping phù hợp.



Sau đó probe detect IO23:



EndpointByIo.TryGetValue(23, out endpoint)



O(1).



Không:



model.Pins.FirstOrDefault(...)

OrderBy(...)

Where(...)

ToList()



mỗi scan frame.



============================================================

PHẦN 11 — NẾU I/O CÓ TRONG THT

============================================================



Hiển thị chính xác dữ liệu THT:



I/O

Connector

Pin

WireName

Gauge

Colors

PinType nếu UI dùng.



Không biến đổi tên dây theo alphabet.



Không ghép với endpoint khác.



============================================================

PHẦN 12 — NẾU BOARD PHÁT HIỆN I/O KHÔNG CÓ TRONG MODEL

============================================================



Phải vẫn nhận raw I/O nếu hardware/protocol hỗ trợ.



Ví dụ:



Probe = IO117



nhưng IO117 không có trong current THT.



Không được crash.



Không được tạo tên dây giả.



Có thể hiện:



I/O = 117



và để các field model không tồn tại trống,

hoặc dùng text hiện có của project cho unmapped I/O.



KHÔNG tự bịa:



Connector

Pin

WireName.



Báo diagnostics:



PROBE IO117 NOT MAPPED IN CURRENT MODEL



nhưng không popup Fault.



============================================================

PHẦN 13 — NẾU NHIỀU PROBE CONTACT XUẤT HIỆN

============================================================



Phải trace behavior phần cứng thật.



Nếu đầu dò thiết kế chỉ cho phép một endpoint:

→ hiển thị endpoint hiện hành.



Nếu protocol có thể trả nhiều endpoint do contact/chập thật:

→ KHÔNG che short thật.



Không tự coi toàn bộ relation có Probe common là hợp lệ nếu điều đó có thể làm mất short detection.



Phải giữ đúng behavior gốc.



============================================================

PHẦN 14 — XÓA HOÀN TOÀN CỘT KẾT NỐI MẠNG

============================================================



TestWindow hiện không cần cột:



KẾT NỐI MẠNG



Phải REMOVE DataGridColumn khỏi XAML.



Không chỉ:



Visibility="Collapsed"



nếu column không còn cần.



Sau sửa bảng chính chỉ còn các cột cần thiết như:



LOẠI

I/O

CONNECTOR

PIN

TÊN DÂY

CỠ DÂY

\#1

\#2

\#3

\#4



và TRẠNG THÁI nếu thiết kế hiện tại còn cần.



Không còn:



KẾT NỐI MẠNG

Mạng 1 • tới IO...

CHỜ KẾT NỐI...

SOURCE...

TARGET...

IO1↔IO2...



============================================================

PHẦN 15 — XÓA PRESENTATION PROPERTY KHÔNG CÒN DÙNG

============================================================



Search:



ConnectionText

NetworkConnectionText

PeerIoText

ConnectionDisplay

"KẾT NỐI MẠNG"



Nếu property chỉ tồn tại để phục vụ cột đã xóa:

có thể loại bỏ sau khi xác nhận không dùng nơi khác.



Nhưng KHÔNG xóa NetworkKey / electrical network backend.



Chỉ xóa presentation thừa.



============================================================

PHẦN 16 — CONNECTOR GROUPING VẪN GIỮ

============================================================



Production row ordering:



Connector group

→ Connector

→ Pin / Original model order.



Không quay lại sort theo WireName.



Probe row phải xuất hiện đúng vị trí display tương ứng nếu bảng hiện có nhiều row.



Nếu probe view được thiết kế là single current row thì phải giữ visual thống nhất.



============================================================

PHẦN 17 — PERFORMANCE VỚI NHIỀU CARD

============================================================



Rà chính xác board configuration.



Known trace hiện có từng dùng:



8C 00 04 00



cho cấu hình đã xác minh.



NHƯNG nếu project hỗ trợ số card mở rộng khác,

không được hard-code protocol mới dựa trên suy đoán.



Phải tìm source/config hiện có xác định:



CardCount

BankCount

MaxIo

ExpansionCards



và xác minh board command tương ứng.



Nếu command byte cho >4 card chưa có trace:

ghi:



NEEDS VERIFICATION



Không tự phát minh command.



============================================================

PHẦN 18 — NHIỀU CARD KHÔNG ĐƯỢC LÀM UI CHẬM SCAN

============================================================



Board scan rate và UI update phải tách hoàn toàn.



Đúng:



FTDI

&#x20;↓

read continuous

&#x20;↓

parse all frames

&#x20;↓

TestEngine

&#x20;↓

fault/probe state delta

&#x20;↓

UI



Sai:



FTDI

&#x20;↓

frame

&#x20;↓

Dispatcher

&#x20;↓

render all IO

&#x20;↓

next frame



============================================================

PHẦN 19 — KHÔNG RENDER TOÀN BỘ I/O MỖI FRAME

============================================================



Nếu model có:



32

64

128

256

hoặc nhiều I/O hơn theo hardware support,



Production UI không được repaint từng I/O mỗi frame.



Chỉ update:



\- endpoint state changed;

\- probe endpoint changed;

\- fault state changed;

\- counters theo timer nhẹ.



============================================================

PHẦN 20 — FRAME BUSINESS KHÔNG ĐƯỢC DROP

============================================================



Trong active continuity:



FramesParsed == FramesProcessed



DroppedFrames = 0



UI có thể render ít hơn.



Ví dụ:



Engine:

150 frame/s



UI:

20 update/s



hoàn toàn đúng nếu trạng thái không đổi.



Không được skip engine frame để giữ UI mượt.



============================================================

PHẦN 21 — WRONG WIRE / SHORT PHẢI CÓ ĐỘ ƯU TIÊN CAO

============================================================



Probe display là secondary presentation.



Fault detection là business critical.



Hot path order phải đảm bảo:



Parse

↓

Electrical network evaluation

↓

Wrong/Short confirmation

↓

Probe presentation delta



Không để việc vẽ probe row chậm fault engine.



============================================================

PHẦN 22 — WRONG/SHORT VẪN DÙNG CONFIRMATION GATE

============================================================



Không giảm accuracy chỉ để nhanh.



Reuse:



stable frames

debounce

confirmation



đã được xác minh.



Mục tiêu:



FAST

\+

STABLE

\+

NO FALSE FAIL



Không:



fault ngay từ một transient frame.



============================================================

PHẦN 23 — NHƯNG PROBE PHẢI NHANH

============================================================



Probe display cũng dùng debounce đủ chống nhiễu,

nhưng không cần fault confirmation/popup.



Khi contact chắc chắn:



show ngay.



Khi release chắc chắn:



hide ngay.



Không thêm delay 500ms/1s gây cảm giác chậm.



Nếu project đã có contact debounce phù hợp:

reuse.



============================================================

PHẦN 24 — TÁCH PROBE STATE KHỎI ENGINE PRODUCT STATE

============================================================



Đề xuất state riêng:



ProbeObservation

{

&#x20;   IsActive

&#x20;   IoNumber

&#x20;   ModelEndpoint

&#x20;   Timestamp

}



Không thêm Probe network vào:



ObservedExpectedNetworks

ConfirmedProductNetworks

UnexpectedNetworks



nếu implementation cũ không làm vậy.



============================================================

PHẦN 25 — PROBE KHÔNG LÀM THAY ĐỔI COUNTER

============================================================



Trong lúc probe:



OpenCount

WrongCount

Fail

Pass

Total



không được thay đổi chỉ vì thao tác probe.



============================================================

PHẦN 26 — PROBE KHÔNG LÀM KÍCH RELAY

============================================================



Không:



probe relation

→ FAIL

→ confirmation

→ relay.



Test relay chỉ chạy theo product fault/final result đúng logic hiện tại.



============================================================

PHẦN 27 — PROBE KHÔNG LÀM MẤT BOARD

============================================================



Không stop/start scan vì probe.



Do đó probe không được làm đi qua:



ExclusiveOperation

Relay recovery

Resistance recovery



Board phải tiếp tục RX bình thường.



============================================================

PHẦN 28 — DATA GRID UPDATE O(1)

============================================================



Nếu probe view dùng single row:



ProbeRows tối đa 1 row.



Khi probe thay đổi:



replace/update row duy nhất.



Nếu yêu cầu dùng main table:



chỉ Insert/Remove đúng endpoint.



Không rebuild collection.



============================================================

PHẦN 29 — UI VIRTUALIZATION

============================================================



DataGrid vẫn phải:



EnableRowVirtualization="True"

EnableColumnVirtualization="True"

ScrollViewer.CanContentScroll="True"

VirtualizingPanel.IsVirtualizing="True"

VirtualizingPanel.VirtualizationMode="Recycling"



Không outer ScrollViewer.



============================================================

PHẦN 30 — KHÔNG AUTOSIZE CỘT LIÊN TỤC

============================================================



Không:



Width="Auto"



cho các cột thay đổi nội dung liên tục nếu làm layout remeasure nhiều.



Dùng width/star ổn định.



Đặc biệt khi nhiều card/model có nhiều row.



============================================================

PHẦN 31 — CACHE BRUSH MÀU DÂY

============================================================



Không parse màu mỗi probe frame.



Wire color mapping load một lần.



Endpoint chứa/cache presentation color.



Probe row chỉ bind.



============================================================

PHẦN 32 — DEVELOPMENT PERFORMANCE COUNTERS

============================================================



Thêm diagnostics nhẹ:



RxBytesPerSecond

FramesParsedPerSecond

FramesProcessedPerSecond

DroppedFrames



ProbeChangesPerSecond

FaultTransitionsPerSecond



EngineFrameLatency

UIProbeLatency



CardCount

MaxIo



Không spam Production UI.



============================================================

PHẦN 33 — ĐO LATENCY THỰC

============================================================



Development log:



\[PROBE] detected IO23

\[PROBE] UI row IO23 visible

\[PROBE] released IO23

\[PROBE] UI row removed



Đo:



hardware frame → engine detection

engine detection → UI



Không cần một con số giả.



Báo p50/p95 khi test.



============================================================

PHẦN 34 — TEST MODEL ÍT CARD

============================================================



Model/config ít card.



Probe lần lượt nhiều IO.



Expected:



touch → row show

release → row hide



không popup.

không fault.

không scan stop.



============================================================

PHẦN 35 — TEST MODEL NHIỀU CARD

============================================================



Dùng model có số card mở rộng lớn nhất mà hardware/project thực sự support.



Expected:



FramesParsed == FramesProcessed

DroppedFrames = 0



Probe vẫn responsive.



Wrong/Short vẫn responsive.



UI không lag rõ khi scroll/move window.



============================================================

PHẦN 36 — TEST PROBE TRÊN ENDPOINT ĐÃ MAPPED

============================================================



THT:



IO32

Connector 3

Pin 5

Wire BG2



Probe IO32.



Expected:



đúng row:



IO32

C3

Pin5

BG2



release:

row gone.



============================================================

PHẦN 37 — TEST PROBE TRÊN RAW IO KHÔNG MAPPED

============================================================



Probe một raw IO board biết nhưng THT không có.



Expected:



không crash

không short popup

không fake model data



phải báo/display raw IO theo behavior được xác minh.



============================================================

PHẦN 38 — TEST PROBE KHÔNG ẢNH HƯỞNG SHORT THẬT

============================================================



Trong lúc Probe feature hoạt động:



tạo một short thật giữa hai product I/O không liên quan probe.



Expected:



Short/Wrong engine vẫn phát hiện đúng.



Không bị suppress bởi Probe mode.



Đây là regression bắt buộc.



============================================================

PHẦN 39 — TEST PROBE KHÔNG ẢNH HƯỞNG CLIP MEMORY

============================================================



A1 clip đã latched.



Dùng probe.



Expected:



A1 vẫn latched.



Probe release không reset A1.



============================================================

PHẦN 40 — TEST PROBE KHÔNG ẢNH HƯỞNG AUTO RESISTANCE

============================================================



Trước continuity complete:



probe hoạt động bình thường.



Probe không được trigger Resistance.



Resistance chỉ chạy theo exact completion rule.



============================================================

PHẦN 41 — TEST UI KHÔNG KHÓA

============================================================



Trong lúc liên tục chạm/nhấc probe:



\- kéo cửa sổ;

\- scroll;

\- bấm control hợp lệ;

\- xem status.



TestWindow không freeze.



Không modal xuất hiện do probe.



============================================================

PHẦN 42 — TEST 1000 PROBE TRANSITIONS

============================================================



Mock/replay:



touch/release

1000 lần.



Expected:



\- không tăng thread count;

\- không memory leak;

\- không duplicate row;

\- không stale row;

\- không giảm scan rate;

\- không popup.



============================================================

PHẦN 43 — TEST WRONG/SHORT LATENCY

============================================================



Với Probe feature bật:



inject wrong/short thật.



Đo detection latency.



So sánh trước/sau refactor.



Không được có regression đáng kể do UI/probe.



============================================================

PHẦN 44 — KHÔNG CHỈ BUILD PASS

============================================================



Build PASS không chứng minh probe hoạt động.



Phải có runtime/replay test xác nhận:



Touch IO

→ Probe detected

→ row appears



Release

→ row disappears



No Fault generated.



============================================================

PHẦN 45 — ROOT CAUSE REPORT

============================================================



Codex phải trả lời:



1\. Probe logic cũ nằm file/method nào?



2\. Vì sao hiện tại probe không nhận nữa?



3\. Vì sao probe hiện tại đi vào Short/Wrong nếu có?



4\. Cách phân biệt Probe electrical relation với product short?



5\. Có command riêng cho Probe không?



6\. Probe common I/O/config nằm đâu?



7\. UI collection/property nào hiển thị Probe?



8\. Có StopScan nào được gọi do Probe không?



9\. KẾT NỐI MẠNG được remove ở file nào?



10\. Model/card count được xác định ở đâu?



11\. Scan command thay đổi thế nào theo card count?

&#x20;   Chỉ trả lời nếu source/trace chứng minh được.

&#x20;   Nếu không → NEEDS VERIFICATION.



12\. Performance trước/sau.



============================================================

PHẦN 46 — FILES MODIFIED

============================================================



Chỉ sửa file cần thiết.



Không tạo:



ProbeService2

TestEngine2

TestWindow2

BoardTransport2



Không:



\*.bak

\*.old

\*.backup



============================================================

PHẦN 47 — BUILD

============================================================



dotnet clean



xóa:



bin

obj



dotnet build -c Debug

dotnet build -c Release



Run self-tests.



============================================================

PHẦN 48 — TIÊU CHÍ ĐẠT CUỐI CÙNG

============================================================



PROBE:



không chạm

→ không có probe row



chạm IO23

→ hiện row IO23 ngay



nhấc

→ row IO23 biến mất



chạm IO55

→ hiện row IO55



Không popup.

Không khóa.

Không lỗi chập do chính đầu dò.

Không dừng scan.

Không relay.

Không counter.



PRODUCT SHORT THẬT:



vẫn detect nhanh và đúng.



PRODUCT WRONG WIRE THẬT:



vẫn detect nhanh và đúng.



NHIỀU CARD:



scan vẫn chạy tối đa theo khả năng board.

100% parsed frames đi vào engine.

DroppedFrames = 0.

UI không được là bottleneck.



TESTWINDOW:



không còn hiển thị KẾT NỐI TỪ ĐẦU ĐẾN ĐÂU NỮA TRONG cột KẾT NỐI MẠNG.



Đây mới được coi là hoàn thành.

