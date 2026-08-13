TASK RIÊNG — RÀ SOÁT VÀ SỬA TRIỆT ĐỂ AUTO RESISTANCE SAU CONTINUITY PASS

PROJECT: JBZUniversalTester



============================================================

MỤC TIÊU DUY NHẤT

============================================================



Hiện tại đã có code liên quan Resistance và Keysight 34461A,

nhưng test thực tế cho thấy:



\- Trong Cài đặt đã bật kiểm tra điện trở.

\- Test thông mạch đã ĐẠT.

\- Nhưng TestWindow KHÔNG chuyển sang bước kiểm tra điện trở.

\- Không thấy bảng Resistance xuất hiện.

\- Không thấy quá trình đo các channel như yêu cầu.



Task này KHÔNG yêu cầu làm giao diện giống ảnh 100%.



Ảnh tester thật chỉ dùng làm tham khảo về cách hoạt động:



CONTINUITY ĐẠT

&#x20;   ↓

chuyển sang bước KIỂM TRA ĐIỆN TRỞ

&#x20;   ↓

hiển thị bảng kết quả Resistance

&#x20;   ↓

đo từng channel

&#x20;   ↓

PASS / FAIL

&#x20;   ↓

sau đó mới quyết định kết quả cuối cùng sản phẩm



MỤC TIÊU QUAN TRỌNG:

Chức năng phải THỰC SỰ HOẠT ĐỘNG trên runtime.



============================================================

1\. KHÔNG ĐƯỢC TIN BÁO CÁO CŨ — PHẢI RÀ SOÁT LẠI TỪ ĐẦU

============================================================



Báo cáo trước nói:



OnEngineChanged

→ RunAutomaticPostContinuityAsync()

→ EnsureKeysightConnectedAsync()

→ \_engine.MeasureResistanceAsync()



và:



SelectedOperationTabIndex = 1



Nhưng test thực tế cho thấy Resistance KHÔNG xuất hiện.



Vì vậy:



KHÔNG được kết luận "flow đã hoạt động" chỉ vì method tồn tại.



Phải trace runtime thực tế.



============================================================

2\. TRACE TOÀN BỘ CALL GRAPH

============================================================



Search:



rg -n --hidden --glob "!bin/\*\*" --glob "!obj/\*\*" `

"RunAutomaticPostContinuityAsync|OnEngineChanged|MeasureResistanceAsync|EnsureKeysightConnectedAsync|ResistanceChannels|ResistanceSteps|SelectedOperationTabIndex|ResistanceRows|ContinuityPass|ContinuityComplete|CompletePassAsync|PassAsync|Relay|Keysight" .



Phải lập call graph THỰC TẾ:



Board frames

&#x20;↓

TestEngine continuity evaluation

&#x20;↓

continuity COMPLETE?

&#x20;↓

event/property nào phát ra?

&#x20;↓

TestViewModel.OnEngineChanged?

&#x20;↓

điều kiện nào gọi RunAutomaticPostContinuityAsync?

&#x20;↓

Resistance Enabled?

&#x20;↓

selected channels?

&#x20;↓

Resistance UI

&#x20;↓

Stop Scan

&#x20;↓

Keysight

&#x20;↓

MeasureResistance

&#x20;↓

Final PASS/FAIL



Phải xác định chính xác flow đang bị dừng ở bước nào.



============================================================

3\. THÊM DEVELOPMENT TRACE TẠM THỜI

============================================================



Khi chạy một cycle phải log rõ:



\[AUTO-R] Continuity state = ...

\[AUTO-R] Continuity complete = true/false

\[AUTO-R] Continuity passed = true/false



\[AUTO-R] Resistance setting enabled = true/false



\[AUTO-R] Model resistance step count = N



\[AUTO-R] Selected channel count = N

\[AUTO-R] Selected channels = CH...



\[AUTO-R] Trigger automatic resistance = YES/NO



\[AUTO-R] Switching TestWindow to resistance view



\[AUTO-R] Keysight connecting

\[AUTO-R] Keysight connected



\[AUTO-R] Starting CH...

\[AUTO-R] Measurement = ...

\[AUTO-R] PASS/FAIL



Không log từng FTDI frame.



Log chỉ dùng để tìm flow.



============================================================

4\. KIỂM TRA CÀI ĐẶT ĐIỆN TRỞ THỰC SỰ ĐƯỢC LƯU

============================================================



Phải xác định:



ProductionSettings.ResistanceChannels



và tất cả property liên quan.



Kiểm tra từ UI Settings:



người dùng tick bật Resistance

&#x20;↓

Save

&#x20;↓

ProductionSettings

&#x20;↓

TestViewModel



có thực sự nhận giá trị mới hay không.



Không được có lỗi:



Settings UI = ON

nhưng runtime object = OFF



do:



\- chưa Save;

\- clone settings;

\- stale settings instance;

\- load config cũ;

\- binding OneWay;

\- wrong property;

\- property không notify;

\- ViewModel giữ settings snapshot từ startup.



============================================================

5\. TEST SETTINGS RELOAD

============================================================



Scenario:



1\. Mở Settings.

2\. Bật Resistance.

3\. Chọn CH3 và CH4.

4\. Save.

5\. Quay lại TestWindow.



Immediately verify:



ResistanceEnabled = true

SelectedChannels = CH3, CH4



Không cần restart application nếu thiết kế hiện tại không yêu cầu.



============================================================

6\. ĐIỀU KIỆN AUTO RESISTANCE PHẢI RÕ RÀNG

============================================================



Auto Resistance chỉ chạy khi:



Test cycle đang active



AND



Continuity thực sự COMPLETE



AND



Continuity PASS



AND



không có Wrong/Short active



AND



Resistance setting = ENABLED



AND



có ít nhất một channel hợp lệ để đo



AND



Resistance operation chưa chạy trong cycle hiện tại.



Logic tương đương:



if (!cycleActive)

&#x20;   return;



if (!continuityComplete)

&#x20;   return;



if (!continuityPassed)

&#x20;   return;



if (hasActiveFault)

&#x20;   return;



if (!resistanceEnabled)

&#x20;   return;



if (selectedChannels.Count == 0)

&#x20;   return;



if (\_resistanceRunning)

&#x20;   return;



await RunAutomaticResistanceAsync();



Dùng property thực tế của project.



============================================================

7\. KHÔNG ĐƯỢC FINAL PASS TRƯỚC RESISTANCE

============================================================



Đây là điểm phải kiểm tra RẤT KỸ.



Hiện tại có thể flow đang:



Continuity complete

&#x20;↓

CompletePassAsync()

&#x20;↓

counter PASS

&#x20;↓

relay

&#x20;↓

cycle kết thúc



TRƯỚC khi Resistance được trigger.



Nếu có thì đây là ROOT CAUSE.



Khi Resistance ENABLED:



Continuity PASS

KHÔNG PHẢI FINAL PRODUCT PASS.



Phải đổi state thành:



ContinuityPassed

&#x20;↓

ResistancePending

&#x20;↓

MeasuringResistance

&#x20;↓

ResistancePassed

&#x20;↓

FINAL PASS



Chỉ FINAL PASS mới:



\- tăng PASS counter;

\- chạy final PASS relay;

\- complete cycle;

\- chuyển sang sản phẩm tiếp theo.



============================================================

8\. FLOW KHI RESISTANCE OFF

============================================================



Nếu Resistance setting = OFF:



Continuity PASS

&#x20;↓

FINAL PASS như flow cũ.



Không vào Keysight.



Không stop scan vì Resistance.



============================================================

9\. FLOW KHI RESISTANCE ON

============================================================



Nếu Resistance = ON:



Continuity PASS

&#x20;↓

DO NOT FINAL PASS YET

&#x20;↓

Resistance phase

&#x20;↓

measure selected channels

&#x20;↓

all resistance PASS?

&#x20;   YES → FINAL PASS

&#x20;   NO  → FAIL



============================================================

10\. KHÔNG ĐƯỢC PHỤ THUỘC CHỈ VÀO ResistanceSteps.Count

============================================================



Kiểm tra logic hiện tại có kiểu:



if (\_engine.ResistanceSteps.Count == 0)

&#x20;   return;



Nếu user đã bật Resistance trong Settings nhưng `ResistanceSteps` không được resolve đúng,

flow sẽ không chạy.



Phải xác định source thật của:



\- channel enabled;

\- channel number;

\- MIN;

\- MAX.



============================================================

11\. SOURCE CỦA CHANNEL / MIN / MAX

============================================================



Phải báo rõ architecture hiện tại:



A. Settings chọn channel nào?



B. THT cung cấp channel/min/max hay Settings cung cấp?



C. Nếu cả hai đều có thì precedence hiện tại là gì?



Không được tự đoán.



Sau trace, phải đảm bảo:



SelectedChannels

=

các channel thật sự Enabled trong Cài đặt

và hợp lệ cho model hiện tại.



Nếu THT là source MIN/MAX:

giữ THT.



Nếu Settings hiện tại thực sự chứa MIN/MAX:

dùng đúng implementation hiện hữu.



Không hard-code.



============================================================

12\. RESISTANCE UI PHẢI HIỆN TRƯỚC KHI BẮT ĐẦU ĐO

============================================================



Ngay sau khi continuity PASS và Resistance được xác định sẽ chạy:



1\. Build ResistanceRows.

2\. Chuyển TestWindow sang Resistance view.

3\. UI render bảng Resistance.

4\. Sau đó bắt đầu đo.



Không đo xong hết rồi mới đổi giao diện.



Expected operator experience:



Continuity rows

&#x20;↓

biến mất / view được swap

&#x20;↓

Resistance table xuất hiện

&#x20;↓

CH3 đang chờ

CH4 đang chờ

&#x20;↓

CH3 được cập nhật

&#x20;↓

CH4 được cập nhật



============================================================

13\. KHÔNG ĐƯỢC DÙNG TAB INDEX NẾU KHÔNG CÒN TABCONTROL

============================================================



Hiện báo cáo nói:



SelectedOperationTabIndex = 1



Nhưng trước đó TabControl đã bị bỏ.



Phải kiểm tra TestWindow.xaml thật.



Nếu không còn TabControl:



SelectedOperationTabIndex = 1



tự nó KHÔNG làm giao diện thay đổi.



Phải có visual binding thật.



============================================================

14\. TẠO 2 VIEW TRONG CÙNG VÙNG DATA

============================================================



TestWindow cần có:



<Grid>



&#x20;   CONTINUITY DATAGRID



&#x20;   RESISTANCE DATAGRID



</Grid>



Visibility điều khiển theo operation phase.



Ví dụ:



Continuity view:



Visibility =

Visible khi OperationMode == Continuity



Resistance view:



Visibility =

Visible khi OperationMode == Resistance



Có thể reuse:



SelectedOperationTabIndex



nếu property đó thực sự notify và được bind.



Tốt hơn nếu source đã có enum state:



OperationMode.Continuity

OperationMode.Resistance



Không cần tạo enum mới nếu không cần.



============================================================

15\. NẾU DÙNG SelectedOperationTabIndex

============================================================



Bắt buộc property setter có:



if (\_selectedOperationTabIndex == value)

&#x20;   return;



\_selectedOperationTabIndex = value;

OnPropertyChanged();



Và XAML thật phải bind nó.



Không để property chỉ tồn tại trong ViewModel nhưng không ai sử dụng.



============================================================

16\. RESISTANCE VIEW KHÔNG PHẢI POPUP

============================================================



Không cần mở cửa sổ mới.



Không cần người vận hành bấm tab.



TestWindow chính tự chuyển.



============================================================

17\. BẢNG RESISTANCE

============================================================



Không cần giống ảnh 100%.



Nhưng phải rõ, gọn và hoạt động đúng.



Chỉ cần:



LOẠI | KÊNH | MIN | GIÁ TRỊ | MAX | KẾT QUẢ



Không có cột NHIỆT ĐỘ.



Ví dụ:



KIỂM TRA ĐIỆN TRỞ | CH3 | 3000 | —      | 5000 | —

KIỂM TRA ĐIỆN TRỞ | CH4 | 3000 | —      | 5000 | —



Sau đo:



KIỂM TRA ĐIỆN TRỞ | CH3 | 3000 | 4127.2 | 5000 | PASS

KIỂM TRA ĐIỆN TRỞ | CH4 | 3000 | 6230.1 | 5000 | FAIL



============================================================

18\. PASS/FAIL UI

============================================================



PASS:



chữ PASS

center

nền trắng



FAIL:



chữ FAIL

đỏ

center

nền trắng



OPEN:



GIÁ TRỊ = OPEN

KẾT QUẢ = FAIL



Không cần badge.



============================================================

19\. KEYSIGHT 34461A

============================================================



Service hiện báo:



Services/KeysightVisaService.cs



Phải verify runtime:



ConnectAutomatic()



có thực sự được gọi không.



Log:



\[AUTO-R] VISA resource found = ...

\[AUTO-R] IDN = ...

\[AUTO-R] Keysight ready



Không được gọi ConnectAutomatic nếu Resistance chưa đến phase.



============================================================

20\. KEYSIGHT KHÔNG ĐƯỢC CONNECT TỪ LÚC CONTINUITY ĐANG DÒ

============================================================



Không để Keysight connection làm chậm đầu dò.



Chỉ sau:



Continuity Complete

\+

Resistance Enabled



mới bắt đầu connect/use instrument.



============================================================

21\. HARDWARE RESISTANCE FLOW

============================================================



Giữ protocol hiện có đã xác minh.



Stop scan:



8D 00 00 00



Reset route:



80 00 00 00



Enable:



90 00 00 01



Channel:



91 00 00 MASK



CH1 = 01

CH2 = 02

CH3 = 04

CH4 = 08



CH5 = 10 nếu implementation hiện hỗ trợ;

ghi chú inferred nếu chưa trace trực tiếp.



Release:



91 00 00 00

90 00 00 30



Không sửa protocol.



============================================================

22\. ĐO TỪNG CHANNEL

============================================================



Ví dụ selected:



CH3

CH4



Flow phải thật sự là:



select CH3

&#x20;↓

settle

&#x20;↓

Keysight MeasureResistance()

&#x20;↓

update row CH3



select CH4

&#x20;↓

settle

&#x20;↓

Keysight MeasureResistance()

&#x20;↓

update row CH4



Không đo channel disabled.



============================================================

23\. KHÔNG REBUILD RESISTANCE TABLE MỖI MEASUREMENT

============================================================



Build rows một lần.



Sau đó:



row.Value = measurement

row.Result = PASS/FAIL



Không:



Rows.Clear()

BuildRows()

Rows.Add()



mỗi lần đo.



============================================================

24\. FIRST RESISTANCE SCREEN UPDATE

============================================================



Ngay khi bước resistance bắt đầu:



State status lớn trên TestWindow phải chuyển từ:



ĐẠT / CONTINUITY OK



sang trạng thái đang đo, ví dụ text hiện có:



KIỂM TRA ĐIỆN TRỞ



hoặc equivalent hiện có.



Không để màn hình hiển thị FINAL PASS trong lúc Resistance chưa xong.



============================================================

25\. FINAL PASS CHỈ SAU KHI TẤT CẢ CHANNEL PASS

============================================================



Ví dụ:



CH3 PASS

CH4 PASS



sau đó:



FINAL PASS



mới:



Pass++

Total++

relay pass sequence

cycle complete



Nếu CH3 PASS, CH4 FAIL:



không được Final PASS.



============================================================

26\. RESISTANCE FAIL

============================================================



Nếu một channel FAIL:



final state = FAIL



hiển thị kết quả từng channel.



Sau fault confirmation:



board phải restore/recover.



Không mất kết nối BO.



============================================================

27\. KEYSIGHT ERROR KHÁC RESISTANCE FAIL

============================================================



Nếu:



VISA fail

Keysight not found

query timeout

instrument disconnect



không được giả:



Resistance FAIL



nếu architecture có HardwareError.



Phải báo Measurement/Instrument Error đúng logic hiện tại.



Nhưng luôn release board route.



============================================================

28\. FINALLY BẮT BUỘC

============================================================



Resistance operation phải có recovery tương đương:



try

{

&#x20;   stop scan;

&#x20;   configure resistance;

&#x20;   measure channels;

}

finally

{

&#x20;   release resistance route;

&#x20;   restore production scan;

}



Không có exit path bỏ qua recovery.



============================================================

29\. SCAN KHÔNG ĐƯỢC BỊ ẢNH HƯỞNG TRƯỚC RESISTANCE

============================================================



Trong continuity:



FramesParsed == FramesProcessed

DroppedFrames == 0



Resistance flow chỉ được StopScan sau khi continuity phase đã thực sự complete.



============================================================

30\. TEST THỰC TẾ QUAN TRỌNG NHẤT

============================================================



TEST A:



Model có resistance.

Settings Resistance = ON.

CH3 + CH4 enabled.



Start test.



Trước continuity complete:



\- probe I/O phải hoạt động;

\- Keysight chưa đo;

\- Resistance screen chưa hiện.



Continuity complete:



\- KHÔNG Final PASS ngay;

\- Resistance screen phải hiện;

\- sau đó Keysight đo CH3;

\- sau đó CH4;

\- cuối cùng mới Final PASS/FAIL.



============================================================

31\. TEST RESISTANCE OFF

============================================================



Model vẫn có resistance configuration.



Settings:



Resistance = OFF



Expected:



Continuity PASS

→ Final PASS bình thường.



Không đo Resistance.



Không gọi Keysight.



Đây là test bắt buộc.



============================================================

32\. TEST UI SWITCH

============================================================



Phải test trực tiếp:



SelectedOperationTabIndex / OperationMode trước:

Continuity



sau continuity complete:

Resistance



Và verify visual element thật:



ContinuityGrid.Visibility = Collapsed

ResistanceGrid.Visibility = Visible



Không chỉ assert property = 1.



============================================================

33\. TEST CH3 + CH4

============================================================



Expected rows thật trên màn hình:



CH3

CH4



Không CH1

Không CH2

Không CH5.



============================================================

34\. TEST OPEN

============================================================



Keysight returns open/overflow.



Expected:



GIÁ TRỊ = OPEN

KẾT QUẢ = FAIL



Final product FAIL.



============================================================

35\. TEST FINAL PASS ORDER

============================================================



Log phải chứng minh order:



Continuity Complete

→ Resistance Start

→ CH3

→ CH4

→ Resistance Complete

→ Final PASS

→ Relay



KHÔNG:



Continuity Complete

→ Final PASS

→ Relay

→ Resistance



============================================================

36\. TEST MULTIPLE CYCLE

============================================================



Chạy ít nhất mock/replay:



100 cycles



Resistance ON.



Expected:



\- Resistance trigger chính xác 1 lần/cycle;

\- không trigger duplicate;

\- không tạo multiple async task;

\- không stale row;

\- không mất board;

\- không chậm dần.



============================================================

37\. SINGLE-FLIGHT AUTO RESISTANCE

============================================================



OnEngineChanged có thể fire nhiều lần.



Không được gọi Resistance nhiều lần.



Dùng:



\_resistanceRunning



hoặc SemaphoreSlim/current task guard.



Ví dụ:



if (\_automaticResistanceTask != null \&\&

&#x20;   !\_automaticResistanceTask.IsCompleted)

&#x20;   return;



Không block UI thread.



============================================================

38\. RESET CYCLE

============================================================



Cycle mới:



Resistance phase guard reset.



ResistanceRows reset.



UI trở lại Continuity.



Keysight session có thể reuse nếu khỏe.



Không giữ PASS/FAIL resistance của sản phẩm trước.



============================================================

39\. CODEX PHẢI ĐÍNH KÈM TRACE MỘT CYCLE

============================================================



Sau sửa, báo sample runtime log kiểu:



\[AUTO-R] ContinuityComplete=True

\[AUTO-R] ContinuityPassed=True

\[AUTO-R] ResistanceEnabled=True

\[AUTO-R] SelectedChannels=CH3,CH4

\[AUTO-R] ShowResistanceView

\[AUTO-R] StopScan

\[AUTO-R] KeysightConnected=True

\[AUTO-R] CH3 start

\[AUTO-R] CH3 value=...

\[AUTO-R] CH3 PASS

\[AUTO-R] CH4 start

\[AUTO-R] CH4 value=...

\[AUTO-R] CH4 PASS

\[AUTO-R] ReleaseRoute

\[AUTO-R] RestoreScan

\[AUTO-R] FINAL PASS



Nếu không tạo được log này bằng test/runtime thì chưa được coi là hoàn thành.



============================================================

40\. ROOT CAUSE PHẢI TRẢ LỜI

============================================================



Codex phải xác định chính xác:



1\. Resistance setting có thật sự được đọc không?



2\. Auto Resistance method có thật sự được gọi không?



3\. Nó bị return ở condition nào?



4\. Final PASS có đang xảy ra trước Resistance không?



5\. ResistanceRows có được tạo không?



6\. SelectedOperationTabIndex/OperationMode có đổi không?



7\. TestWindow có binding tới property đó không?



8\. Resistance DataGrid có thật sự tồn tại trong visual tree không?



9\. Visibility có chuyển Visible không?



10\. Keysight service có được gọi không?



============================================================

41\. KHÔNG ĐƯỢC BÁO "PASS" CHỈ VÌ BUILD THÀNH CÔNG

============================================================



dotnet build PASS

không chứng minh Resistance hoạt động.



Self-test PASS

không chứng minh TestWindow thật chuyển view.



Phải có:



\- runtime state test;

\- visual binding verification;

\- flow order verification.



============================================================

42\. BUILD

============================================================



dotnet clean



xóa:



bin

obj



dotnet build -c Debug

dotnet build -c Release



Chạy self-tests.



============================================================

43\. REPORT CUỐI

============================================================



Trả về:



ROOT CAUSE AUTO RESISTANCE NOT STARTING:

...



ROOT CAUSE RESISTANCE VIEW NOT APPEARING:

...



ROOT CAUSE FINAL PASS ORDER:

...



SETTINGS VALUE AT RUNTIME:

...



SELECTED CHANNELS:

...



CONTINUITY → RESISTANCE:

...



RESISTANCE → FINAL RESULT:

...



KEYSIGHT CALLED:

YES/NO



RESISTANCE DATAGRID VISIBLE:

YES/NO



FILES MODIFIED:

...



PROTOCOL MODIFIED:

NO



SCAN RATE MODIFIED:

NO



BUILD DEBUG:

...



BUILD RELEASE:

...



SELF TEST:

...



RUNTIME FLOW TEST:

...



HARDWARE JIG/KEYSIGHT TEST:

PASS hoặc NOT TESTED

không được giả đã test nếu chưa có hardware.



============================================================

44\. SAU KHI HOÀN TẤT

============================================================



CHỈ GỬI CÁC FILE THỰC SỰ ĐÃ SỬA.



Không gửi toàn bộ project.



============================================================

TIÊU CHÍ ĐẠT CUỐI CÙNG

============================================================



Resistance ON:



CONTINUITY PASS

→ KHÔNG FINAL PASS NGAY

→ TESTWINDOW HIỆN RESISTANCE TABLE

→ KEYSIGHT TỰ ĐỘNG ĐO

→ ĐO ĐÚNG CÁC CHANNEL ĐƯỢC CHỌN

→ HIỂN THỊ KẾT QUẢ

→ TẤT CẢ R PASS MỚI FINAL PASS



Resistance OFF:



CONTINUITY PASS

→ FINAL PASS NHƯ CŨ.



Đó mới được coi là chức năng AUTO RESISTANCE hoạt động đúng.



============================================================

BỔ SUNG BẮT BUỘC — AUTO STABILIZATION KHI ĐO ĐIỆN TRỞ

============================================================



YÊU CẦU:



Không được đo Resistance theo kiểu:



Select CH1

→ Delay cố định

→ đọc 1 lần

→ Select CH2

→ Delay cố định

→ đọc 1 lần



Cách này không đủ tin cậy trên jig thật.



Phần mềm phải tự xác định giá trị điện trở đã ỔN ĐỊNH sau mỗi lần chuyển

relay/MUX sang một channel mới.



MỤC TIÊU:



Select CH1

↓

chờ relay/MUX vật lý ổn định tối thiểu

↓

đọc Keysight nhiều lần

↓

tự đánh giá các sample đã ổn định chưa

↓

nếu chưa ổn định → tiếp tục đọc

↓

nếu ổn định → chốt giá trị CH1

↓

PASS/FAIL CH1

↓

Select CH2

↓

lặp lại quá trình ổn định cho CH2



Không được chuyển sang CH2 khi CH1 chưa có giá trị ổn định hoặc chưa timeout.



============================================================

1\. KHÔNG HARD-CODE MỘT DELAY DUY NHẤT

============================================================



Rà soát các code hiện tại dạng:



await Task.Delay(ResistanceSettleMs);



hoặc:



Thread.Sleep(...);



Nếu hiện tại chỉ chờ X ms rồi đọc đúng 1 lần thì phải nâng cấp.



ResistanceSettleMs chỉ nên là:



MINIMUM HARDWARE SETTLE TIME



sau khi relay/MUX vừa chuyển.



Nó KHÔNG phải bằng chứng measurement đã ổn định.



============================================================

2\. FLOW CHO MỖI CHANNEL

============================================================



Ví dụ CH1:



Select CH1:

91 00 00 01



↓

Minimum relay settle

↓

Keysight sample 1

↓

Keysight sample 2

↓

Keysight sample 3

↓

...

↓

StabilityEvaluator

↓



Stable?

&#x20;├── NO  → tiếp tục sample

&#x20;└── YES → chốt Resistance CH1



↓

đánh giá MIN/MAX

↓

update UI CH1

↓

Select CH2



CH2:



91 00 00 02



↓

BẮT ĐẦU stability process MỚI cho CH2.



Không reuse sample của CH1.



============================================================

3\. MỖI LẦN CHUYỂN CHANNEL PHẢI RESET STABILITY STATE

============================================================



Khi:



CH1 → CH2



phải reset:



sample buffer

stable counter

min observed

max observed

last value

stability timer



CH2 phải tự ổn định độc lập.



Tương tự:



CH2 → CH3

CH3 → CH4

...



============================================================

4\. DISCARD GIAI ĐOẠN ĐẦU SAU KHI SWITCH RELAY/MUX

============================================================



Sau khi gửi:



91 00 00 MASK



không chốt ngay reading đầu tiên.



Relay/MUX/contact có thể đang transient.



Flow:



Select channel

↓

MinimumSettleTime

↓

bắt đầu lấy sample stabilization



Nếu implementation thực tế cho thấy reading đầu tiên sau switch thường bất thường,

cho phép discard 1 hoặc một số sample đầu,

nhưng phải cấu hình được và phải xác minh bằng log jig thật.



Không hard-code tùy tiện.



============================================================

5\. STABILITY EVALUATOR

============================================================



Tạo một component/helper duy nhất, ví dụ:



ResistanceStabilityEvaluator



Không rải logic stability ở nhiều file.



Input:



double resistanceOhm

timestamp



Output:



NotStable

Stable

Open

Timeout/Error



============================================================

6\. KHÔNG DÙNG CHỈ 2 SAMPLE

============================================================



Không coi:



R1 == R2



là đủ.



Cần một cửa sổ nhiều sample liên tiếp.



Ví dụ kiến trúc:



RecentSamples\[N]



và chỉ chấp nhận khi toàn bộ cửa sổ measurement đủ ổn định.



Các thông số N/threshhold phải cấu hình,

không hard-code sâu trong TestEngine.



============================================================

7\. ĐÁNH GIÁ STABILITY THEO ĐỘ DAO ĐỘNG

============================================================



Có thể đánh giá theo:



Range =

Max(recent samples) - Min(recent samples)



và/hoặc:



Relative variation =

Range / representative resistance



Channel chỉ Stable khi variation nằm trong tolerance được cấu hình

liên tiếp đủ số sample.



Không sử dụng một tolerance tuyệt đối duy nhất cho mọi dải điện trở nếu nó làm

sai các model khác nhau.



Nên hỗ trợ:



AbsoluteToleranceOhm



và:



RelativeTolerancePercent



hoặc cơ chế tương đương.



============================================================

8\. KHÔNG TỰ CHỌN NGƯỠNG SẢN XUẤT MÀ KHÔNG XÁC MINH

============================================================



Codex KHÔNG được tự quyết định rằng:



3 samples

0.5%

500 ms



là chuẩn sản xuất.



Nếu project hiện có các setting tương đương thì reuse.



Nếu chưa có, tạo các parameter cấu hình rõ ràng:



ResistanceMinimumSettleMs

ResistanceSampleIntervalMs

ResistanceStableSampleCount

ResistanceStableAbsoluteToleranceOhm

ResistanceStableRelativeTolerancePercent

ResistanceStabilityTimeoutMs



Giá trị mặc định phải được ghi:



NEEDS HARDWARE VERIFICATION



cho tới khi đo trên jig thật.



============================================================

9\. PHẦN MỀM PHẢI TỰ TÍNH THỜI GIAN ỔN ĐỊNH THỰC TẾ

============================================================



Không cần người vận hành biết CH1 phải chờ 300 ms hay 800 ms.



Software tự làm:



T0 = thời điểm switch channel



đến khi StabilityEvaluator xác nhận Stable:



StableTime = Tstable - T0



Log:



\[AUTO-R] CH1 switch

\[AUTO-R] CH1 stable after xxx ms

\[AUTO-R] CH1 samples=N

\[AUTO-R] CH1 final=xxxx.xx Ohm



Vì mỗi channel/jig có thể khác nhau:



CH1 stable after 240 ms

CH2 stable after 410 ms

CH3 stable after 275 ms



Phần mềm phải tự thích nghi trong giới hạn timeout.



============================================================

10\. MINIMUM SETTLE VÀ AUTO STABILITY LÀ HAI THỨ KHÁC NHAU

============================================================



Phải tách:



A. MinimumSettleTime



để relay/MUX có thời gian vật lý chuyển mạch.



B. StabilityDetection



để xác nhận Keysight reading đã ổn định.



Flow đúng:



Switch

↓

MinimumSettle

↓

Repeated measurement

↓

Stable detection



Không bỏ MinimumSettle chỉ vì có stability detection.



============================================================

11\. KHÔNG CHỜ THÊM CỐ ĐỊNH SAU KHI ĐÃ STABLE

============================================================



Khi channel đã đạt Stable:



không:



Stable

→ chờ thêm 1 giây

→ next channel



Phải chuyển sang bước tiếp theo ngay.



Mục tiêu vừa:



CHÍNH XÁC



vừa:



NHANH.



============================================================

12\. MAXIMUM TIMEOUT

============================================================



Không được chờ vô hạn khi:



\- dây tiếp xúc kém;

\- điện trở dao động;

\- Keysight reading không ổn định;

\- relay lỗi;

\- jig contact lỗi.



Mỗi channel phải có StabilityTimeout.



Nếu timeout:



Channel result phải là trạng thái rõ ràng:



UNSTABLE



hoặc equivalent internal state.



Không fake PASS.



Không dùng reading cuối cùng làm PASS chỉ vì hết thời gian.



============================================================

13\. PHÂN BIỆT FAIL LIMIT VÀ UNSTABLE

============================================================



Ví dụ:



MIN = 3000 Ω

MAX = 5000 Ω



Case A:



readings ổn định quanh:



6000 Ω



=> Stable measurement

=> Resistance FAIL



Case B:



readings:



3200

4700

3600

5100

3400

4900

...



không ổn định



=> UNSTABLE / measurement error



Không được coi đơn giản là FAIL limit dựa trên reading cuối.



Engine nên phân biệt nguyên nhân để log/debug.



Production UI có thể vẫn hiển thị FAIL theo business rule,

nhưng diagnostics phải biết:



OutOfRange



khác:



UnstableMeasurement.



============================================================

14\. OPEN PHẢI ĐƯỢC XỬ LÝ RIÊNG

============================================================



Keysight trả OPEN/overflow:



9.9E+37 hoặc equivalent



không đưa giá trị đó vào average/range calculation như số bình thường.



Nếu OPEN liên tục sau thời gian xác nhận phù hợp:



Value = OPEN

Result = FAIL



Nếu transition:



OPEN

→ value

→ value ổn định



thì evaluator tiếp tục cho đến khi tín hiệu thực sự stable hoặc timeout.



============================================================

15\. FINAL VALUE

============================================================



Khi cửa sổ sample đã Stable:



không nhất thiết chốt sample cuối cùng.



Ưu tiên dùng representative value của stable window:



median



hoặc:



average



tùy implementation phù hợp.



Median có ưu điểm chống một sample nhiễu.



Nhưng phải giữ implementation đơn giản và deterministic.



Báo rõ Codex chọn phương pháp nào.



============================================================

16\. KHÔNG FILTER QUÁ MỨC ĐỂ CHE LỖI

============================================================



Không được dùng moving average dài tới mức:



contact lỗi thực tế

→ bị làm mượt thành PASS.



Stability evaluator chỉ loại transient hợp lý,

không được che dao động thật của jig/product.



============================================================

17\. UI TRONG KHI ĐANG ỔN ĐỊNH

============================================================



Resistance table:



LOẠI | KÊNH | MIN | GIÁ TRỊ | MAX | KẾT QUẢ



Ví dụ vừa chuyển CH1:



KIỂM TRA ĐIỆN TRỞ | CH1 | 3000 | — | 5000 | ĐANG ĐO



Trong quá trình stabilization:



KẾT QUẢ:

ĐANG ĐO



hoặc text hiện có tương đương.



Không hiển thị PASS trước khi Stable.



============================================================

18\. KHI CH1 STABLE

============================================================



Ví dụ:



samples stable quanh 4125 Ω.



UI update:



CH1

MIN = 3000

GIÁ TRỊ = 4125.x Ω

MAX = 5000

KẾT QUẢ = PASS



Sau đó mới:



Select CH2.



============================================================

19\. KHI CH1 FAIL

============================================================



Cần kiểm tra business behavior hiện tại:



Nếu original tester đo tất cả selected channels rồi tổng hợp FAIL:

→ tiếp tục CH2...



Nếu original tester stop ngay channel đầu FAIL:

→ giữ behavior đó.



KHÔNG tự quyết định.



Codex phải trace logic hiện tại/original trước.



Nhưng dù chọn behavior nào:



CH1 phải ổn định trước khi kết luận FAIL do MIN/MAX.



============================================================

20\. KEYSIGHT QUERY KHÔNG ĐƯỢC BLOCK UI

============================================================



Repeated measurements phải chạy trên measurement worker/async flow.



Không:



Dispatcher

→ VISA query

→ wait



Không:



Thread.Sleep



trên UI thread.



UI chỉ nhận measurement state delta.



============================================================

21\. SAMPLE INTERVAL

============================================================



Không hammer Keysight bằng query vô hạn nhanh hơn instrument có thể đáp ứng.



Dùng instrument/service throughput thực tế.



Nếu `MeasureResistance()` tự block tới khi reading sẵn sàng,

sample cadence nên dựa trên completion của query.



Nếu cần thêm interval, dùng cấu hình.



Không spin loop.



============================================================

22\. KHÔNG RECONNECT KEYSIGHT GIỮA CÁC SAMPLE

============================================================



Trong một Resistance sequence:



Connect once

↓

CH1 multiple readings

↓

CH2 multiple readings

↓

CH3...

↓

finish



Không:



sample

→ disconnect

→ reconnect

→ sample



============================================================

23\. KHÔNG RECONNECT KEYSIGHT GIỮA CHANNEL

============================================================



Giữ VISA session nếu healthy.



Chỉ reconnect khi communication error thật.



Điều này rất quan trọng cho tốc độ.



============================================================

24\. CHANNEL SWITCH FLOW

============================================================



Ví dụ CH1 → CH2:



CH1 Stable

↓

save CH1 result

↓

send CH2 selector

91 00 00 02

↓

reset StabilityEvaluator

↓

minimum settle

↓

sample CH2

↓

Stable

↓

save CH2 result



Không release/re-initialize toàn Keysight giữa hai channel.



============================================================

25\. BOARD COMMAND FLOW

============================================================



Giữ protocol đã trace.



Bắt đầu Resistance:



8D 00 00 00

80 00 00 00



Enable:



90 00 00 01



CH1:

91 00 00 01



CH2:

91 00 00 02



CH3:

91 00 00 04



CH4:

91 00 00 08



Sau tất cả:



91 00 00 00

90 00 00 30



Không tự thêm command mới.



============================================================

26\. KHÔNG DÙNG `Task.Delay` ĐỂ GIẢ STABLE

============================================================



Nếu code hiện tại:



await Task.Delay(settleMs);

double value = MeasureResistance();



phải thay thành:



await MinimumSettleAsync();



var stableResult =

&#x20;   await MeasureUntilStableAsync(...);



Không phải chỉ tăng settleMs từ:



200

→ 1000

→ 2000



để chữa lỗi.



============================================================

27\. METHOD GỢI Ý

============================================================



Không bắt buộc tên, nhưng architecture nên tương đương:



MeasureChannelStableAsync(

&#x20;   channel,

&#x20;   min,

&#x20;   max,

&#x20;   cancellationToken)



bên trong:



SelectChannel

WaitMinimumSettle

MeasureUntilStable

EvaluateLimit

Return ResistanceResult



và:



MeasureUntilStableAsync(...)



chịu trách nhiệm:



\- sampling;

\- stability;

\- timeout;

\- OPEN;

\- communication error.



============================================================

28\. RESULT MODEL

============================================================



ResistanceResult nên có đủ diagnostics nếu phù hợp:



Channel



ValueOhm



Passed



IsOpen



IsStable



MeasurementStatus



SampleCount



StabilizationTimeMs



Không bắt buộc expose tất cả lên UI.



Nhưng log/debug nên có.



============================================================

29\. SETTINGS/UI CẤU HÌNH

============================================================



Trong Cài đặt, nếu cần expose advanced parameters:



Minimum settle

Stable samples

Stability tolerance

Maximum stabilization time



nên đặt trong phần kỹ thuật/advanced.



Không làm rối màn hình operator.



Nếu hiện tại đã có:



R settle



phải xác định lại ý nghĩa:



R settle = Minimum relay settle



không phải toàn bộ thời gian đo.



============================================================

30\. LOG MỘT CHANNEL

============================================================



Development log phải đủ để phân tích:



\[AUTO-R] CH3 select mask=0x04

\[AUTO-R] CH3 minimum settle start

\[AUTO-R] CH3 minimum settle complete

\[AUTO-R] CH3 sample#1 = 4102.3

\[AUTO-R] CH3 sample#2 = 4129.8

\[AUTO-R] CH3 sample#3 = 4124.6

\[AUTO-R] CH3 sample#4 = 4125.1

\[AUTO-R] CH3 stable

\[AUTO-R] CH3 stable\_time=XXX ms

\[AUTO-R] CH3 final=4125.x

\[AUTO-R] CH3 PASS

\[AUTO-R] next=CH4



Có thể giảm chi tiết sample log trong Release.



============================================================

31\. TỰ TỐI ƯU THỜI GIAN

============================================================



Sau khi implementation hoạt động:



Chạy trên jig thật nhiều lần.



Thu thập:



CH1 stabilization ms

CH2 stabilization ms

CH3 stabilization ms

CH4 stabilization ms



Min

Average

P95

Max



Mục tiêu xác định:



\- MinimumSettle có quá dài không;

\- timeout có phù hợp không;

\- channel nào chậm hơn.



Không hard-code các số này trước khi đo hardware.



============================================================

32\. KHÔNG ĐƯỢC HỌC THỜI GIAN THEO KIỂU NGUY HIỂM

============================================================



"Auto calculate" KHÔNG có nghĩa software tự giảm safety threshold sau vài lần PASS.



Stability criteria vẫn phải được cấu hình/validated.



Software chỉ tự xác định:



channel hiện tại đã stable lúc nào.



Không tự thay tolerance sản xuất âm thầm.



============================================================

33\. REGRESSION — FAST STABLE CHANNEL

============================================================



Sample:



4000.1

4000.2

4000.1

...



Channel phải được xác nhận sớm sau khi đủ criteria.



Không chờ hết MaxTimeout.



============================================================

34\. REGRESSION — SLOW STABLE CHANNEL

============================================================



Sample:



3700

3900

4050

4110

4120

4121

4120



Không chốt sớm ở 3700.



Phải chờ stable window cuối.



============================================================

35\. REGRESSION — UNSTABLE

============================================================



Sample:



3000

5000

3500

4800

3200

...



cho đến timeout.



Expected:



UNSTABLE



không fake một final value PASS.



============================================================

36\. REGRESSION — OUT OF RANGE BUT STABLE

============================================================



Sample:



6100

6099

6101

6100



Stable.



MIN=3000

MAX=5000



Expected:



Stable = true

Value ≈ 6100

Result = FAIL



============================================================

37\. REGRESSION — OPEN

============================================================



OPEN liên tục.



Expected:



Value = OPEN

FAIL



theo confirmation rule.



============================================================

38\. REGRESSION — CH1 → CH2

============================================================



CH1 samples stable.



Sau switch CH2, evaluator phải reset hoàn toàn.



Không để CH1 sample tham gia stability CH2.



============================================================

39\. REGRESSION — SELECTED CHANNELS ONLY

============================================================



Settings:



CH1

CH3



Expected:



CH1 stabilize + measure

↓

CH3 stabilize + measure



Không đo CH2.



============================================================

40\. REGRESSION — PERFORMANCE

============================================================



Tổng thời gian Resistance phải là:



thời gian thực cần để từng channel ổn định



không phải:



SelectedChannelCount × một delay cố định rất lớn.



Log total sequence duration.



============================================================

41\. CODEX PHẢI BÁO KẾT QUẢ

============================================================



ROOT CAUSE CURRENT SETTLING:

hiện đang delay bao nhiêu?

đang đọc bao nhiêu sample?

có stability detection không?



NEW STABILITY ALGORITHM:

...



CONFIG PARAMETERS:

...



FINAL VALUE METHOD:

median / average / khác



TIMEOUT BEHAVIOR:

...



OPEN BEHAVIOR:

...



FILES MODIFIED:

...



TEST RESULTS:

Fast stable

Slow stable

Unstable

Stable out of range

OPEN

Channel switching



HARDWARE TEST:

nếu chưa có Keysight+jig thì ghi NOT TESTED.



Không được giả đã xác minh timing hardware.



============================================================

TIÊU CHÍ ĐẠT

============================================================



Mỗi kênh:



SWITCH CHANNEL

→ MINIMUM SETTLE

→ SAMPLE

→ SAMPLE

→ ...

→ AUTO DETECT STABLE

→ FINAL VALUE

→ PASS/FAIL

→ NEXT CHANNEL



Phần mềm phải tự xác định channel cần 200 ms, 400 ms hay lâu hơn

dựa trên reading thực tế.



Không chuyển channel quá sớm.

Không chờ cố định quá lâu.

Không hy sinh độ chính xác để tăng tốc.

