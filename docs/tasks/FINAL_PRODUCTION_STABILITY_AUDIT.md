# FINAL TASK — Production Stability, Long-Run Reliability, Fault Detection Robustness & IO Timing Settings

## Mục tiêu cuối cùng

Đây là vòng rà soát cuối trước khi coi phần mềm đủ ổn định để vận hành sản xuất lâu dài.

**Ưu tiên cao nhất không phải thêm tính năng mới, mà là:**

1. Giao diện vận hành ổn định.
2. Logic test mạch hoạt động đúng.
3. Không lag, không treo UI, không tăng CPU/RAM bất thường khi chạy lâu.
4. Phát hiện lỗi phải chính xác, không bỏ lỗi thật.
5. Giảm tối đa FAIL giả do nhiễu, contact bounce, trạng thái chuyển tiếp hoặc mẫu IO chưa ổn định.
6. Không phá các semantics đã xác minh trong `AGENTS.md`.
7. D2XX và UART TTL vẫn là hai backend độc lập.
8. Không thay đổi protocol/firmware semantics dựa trên suy đoán.

**Bắt buộc đọc `AGENTS.md` trước khi thực hiện.**

Không audit lại toàn repository một cách mù quáng. Hãy tập trung vào các luồng production-critical và những file liên quan trực tiếp.

---

# 1. PHẠM VI ƯU TIÊN CAO NHẤT

Rà soát các khu vực có ảnh hưởng trực tiếp tới vận hành production:

- TestWindow / TestViewModel
- MainViewModel nếu tham gia lifecycle
- TestEngine / FaultEngine
- D2XX transport / scan / frame processing
- UART TTL transport / read loop / reconnect
- IO state processing
- ProductRemoved detection
- PASS / FAIL state machine
- Probe/TestPin flow
- relay / JIG lifecycle
- model switching
- cancellation / dispose
- timers / async loops / worker threads
- event subscriptions / callbacks
- settings liên quan timing
- history/result capture
- FAIL dialog data delivery

Không refactor rộng chỉ để làm code đẹp.

---

# 2. MỤC TIÊU ỔN ĐỊNH GIAO DIỆN

UI production phải:

- luôn responsive;
- không block UI thread bằng IO, polling hoặc hardware wait;
- không dùng `Thread.Sleep` trên UI thread;
- không cập nhật UI với tần suất quá cao nếu không cần;
- không tạo DataGrid refresh toàn bộ liên tục;
- không tạo object/collection mới mỗi scan nếu có thể cập nhật tối thiểu;
- không spam PropertyChanged cho giá trị không thay đổi;
- không tạo timer/event subscription lặp lại sau mỗi reconnect/model change;
- không giữ stale callback từ board/model/session cũ;
- không có recursive setter/event gây StackOverflow;
- read-only binding phải đúng mode;
- popup/dialog không được làm treo test worker;
- lỗi hardware phải được xử lý mà UI vẫn sử dụng được.

Kiểm tra đặc biệt khi chạy lâu:

- CPU usage;
- memory growth;
- handle/thread growth;
- số lượng timer;
- event subscriber leak;
- COM/FTDI handle leak;
- reader duplication;
- log growth bất thường;
- UI response sau nhiều giờ.

Không cần micro-optimize code không ảnh hưởng production.

---

# 3. NGUYÊN TẮC PHÁT HIỆN LỖI

Phát hiện lỗi là chức năng production-critical.

Phải phân biệt rõ:

## 3.1. Raw observation

Trạng thái đọc tức thời từ hardware/transport.

Ví dụ:
- pin vừa mất continuity trong một sample;
- xuất hiện connection bất thường trong một sample;
- IO vừa chuyển trạng thái;
- board đang trong quá trình reconnect;
- fixture/contact đang rung.

## 3.2. Candidate fault

Một lỗi nghi ngờ đã xuất hiện nhưng chưa đủ điều kiện xác nhận FAIL.

## 3.3. Confirmed fault

Chỉ tạo FAIL khi lỗi thỏa điều kiện xác nhận đã cấu hình.

Không được coi một sample bất thường duy nhất là FAIL nếu thiết kế hardware/protocol có khả năng sinh transient.

Tuy nhiên:

**Không được dùng debounce/delay để che lỗi thật hoặc bỏ qua fault tồn tại ổn định.**

---

# 4. SETTINGS — TIMING / DEBOUNCE / FAULT CONFIRMATION

Trong phần Cài đặt, bổ sung hoặc chuẩn hóa một nhóm:

## `THỜI GIAN QUÉT & XÁC NHẬN LỖI`

Các setting phải có tên rõ ràng cho kỹ thuật viên.

### 4.1. Chu kỳ quét IO

UI label:

**Chu kỳ quét IO**

English/internal semantic:
`IoScanIntervalMs`

Ý nghĩa:
- khoảng thời gian giữa hai vòng xử lý/poll IO nếu backend hiện tại dùng polling;
- không áp dụng máy móc cho backend event/frame-driven nếu không phù hợp;
- không được thêm polling thứ hai nếu transport đã có reader riêng.

Đơn vị:
`ms`

Phải có:
- minimum hợp lý;
- maximum hợp lý;
- validation;
- tooltip/mô tả.

Không cho nhập 0 hoặc giá trị cực thấp gây 100% CPU.

### 4.2. Thời gian xác nhận hở mạch

UI label:

**Thời gian xác nhận hở mạch**

Internal semantic:
`OpenCircuitConfirmMs`

Ý nghĩa:
- OPEN chỉ được xác nhận FAIL khi trạng thái hở tồn tại liên tục đủ thời gian;
- nếu continuity trở lại trước timeout thì candidate OPEN bị hủy;
- không cộng dồn các xung hở rời rạc thành một lỗi nếu state đã trở lại bình thường ở giữa.

### 4.3. Thời gian xác nhận chập mạch

UI label:

**Thời gian xác nhận chập mạch**

Internal semantic:
`ShortCircuitConfirmMs`

Ý nghĩa:
- short candidate phải tồn tại ổn định đủ thời gian trước khi xác nhận;
- tránh transient connection/contact bounce tạo short giả;
- không trì hoãn quá mức một short thật.

### 4.4. Thời gian xác nhận sai kết nối / sai dây

UI label:

**Thời gian xác nhận sai kết nối**

Internal semantic:
`WrongConnectionConfirmMs`

Áp dụng cho:
- sai vị trí;
- wrong connection;
- crossed terminal;
- unexpected connection;

nếu FaultEngine hiện tại có semantic tương ứng.

Không gộp sai màu vật lý vào debounce điện nếu màu dây là metadata/model chứ không phải tín hiệu điện đo được.

### 4.5. Thời gian ổn định sau kết nối sản phẩm

Nếu architecture hiện tại cần:

UI label:

**Thời gian ổn định sau khi kết nối**

Internal:
`ProductSettleTimeMs`

Ý nghĩa:
- sau khi phát hiện product/fixture vừa được kết nối, cho tín hiệu ổn định trước khi bắt đầu đánh giá fault;
- không làm thay đổi ProductRemoved semantics;
- không tự PASS trong thời gian settle;
- không bỏ qua fault sau khi settle kết thúc.

Chỉ thêm nếu flow hiện tại thực sự cần và có source logic phù hợp.

### 4.6. Thời gian ổn định sau reconnect board

Nếu cần:

`BoardReconnectSettleMs`

Chỉ dùng để tránh xử lý frame/state cũ trong giai đoạn transport vừa reconnect.

Không dùng thay thế cho correct lifecycle/cancellation.

---

# 5. QUY TẮC QUAN TRỌNG VỀ TIMING

Không dùng một biến `Delay` chung cho mọi thứ.

Phân biệt:

- Scan interval
- Product settle time
- Open confirmation
- Short confirmation
- Wrong-connection confirmation
- Hardware command timeout
- Retry interval

Các khái niệm này không được dùng thay thế cho nhau.

Không thay đổi timeout protocol chỉ vì muốn debounce fault.

Không dùng `Task.Delay`/`Thread.Sleep` để “chờ rồi đọc lại” trong UI logic nếu có thể dùng state machine/timestamp.

Ưu tiên state machine dựa trên thời gian:

`Normal`
→ `CandidateFault`
→ nếu tồn tại đủ thời gian → `ConfirmedFault`
→ nếu trở lại bình thường trước timeout → `Normal`

Dùng monotonic elapsed time phù hợp (`Stopwatch` hoặc mechanism tương đương), tránh phụ thuộc trực tiếp vào wall clock cho debounce.

---

# 6. DEBOUNCE PHẢI THEO TỪNG FAULT KEY

Candidate timer không được dùng chung cho toàn hệ thống nếu có nhiều pin/fault đồng thời.

Key phải đủ phân biệt fault, ví dụ semantic tương đương:

- FaultType
- Source connector/pin
- Target connector/pin
- physical pin nếu cần
- backend/session/model identity nếu cần

Ví dụ:
- CN1 Pin 3 OPEN có timer riêng;
- CN4 Pin 7 SHORT với CN6 Pin 2 có timer riêng.

Không để fault A reset hoặc xác nhận nhầm fault B.

Khi đổi:
- model;
- board;
- backend;
- test cycle;
- product;
- session;

candidate fault cũ phải được clear/cancel đúng cách.

---

# 7. FAIL CHỈ ĐƯỢC PHÁT MỘT LẦN CHO CÙNG EVENT

Tránh:

- popup FAIL lặp liên tục mỗi scan;
- tăng fail count nhiều lần cho cùng một sản phẩm;
- pulse JIG/relay nhiều lần;
- ghi history duplicate;
- nhiều event handler cùng nhận một confirmed fault.

Sau khi đã vào FAIL state:
- giữ lifecycle hiện tại;
- chờ xác nhận operator;
- backend-specific removal flow;
- ProductRemoved;
- re-arm.

Không re-enter FAIL cho tới khi state machine cho phép.

---

# 8. PRODUCT REMOVED / TRANSIENT IO

Đặc biệt kiểm tra regression:

- một IO mất rồi trở lại không được tự động coi là toàn bộ product removed;
- product removal phải dùng rule đã xác minh;
- transient OPEN trong lúc tháo hàng không được tạo thêm fault/history sau khi hệ thống đã chuyển sang removal state;
- stale frame từ cycle trước không được áp dụng cho cycle mới.

---

# 9. PROBE / TESTPIN

Probe/TestPin:

- không tạo PASS/FAIL;
- không tăng sản lượng;
- không chạy relay/JIG;
- không đi qua normal production FaultEngine nếu AGENTS.md quy định tách biệt;
- debounce production không được làm thay đổi semantics Probe nếu không có yêu cầu.

---

# 10. D2XX VS UART TTL

Timing/debounce layer phải tôn trọng backend boundary.

## D2XX

- giữ frame/scan semantics hiện tại;
- không thêm reader thứ hai;
- không poll song song nếu transport đã có loop;
- debounce nằm ở fault interpretation layer phù hợp, không phá frame parser.

## UART TTL

- giữ một read ownership;
- không tạo nhiều reader COM;
- giữ ACK/model upload/test command semantics;
- reconnect phải cancel reader cũ trước khi tạo reader mới;
- stale line/callback từ connection cũ không được tạo fault.

Nếu raw data semantics hai backend khác nhau:
- normalize thành observation chung ở layer phù hợp;
- không ép hai protocol thành một implementation.

---

# 11. SETTINGS UI / PERSISTENCE

Các timing setting phải:

- hiển thị rõ đơn vị `ms`;
- có giá trị mặc định an toàn;
- có Min/Max;
- không cho text invalid làm crash app;
- được persist;
- reload đúng khi mở lại phần mềm;
- có tooltip/mô tả ngắn;
- có nút/default reset nếu settings architecture hiện tại hỗ trợ.

Không hard-code magic number trong nhiều file.

Dùng một source-of-truth config/settings.

Nếu config cũ chưa có field:
- backward compatible;
- missing value → dùng default;
- không làm file settings cũ load fail.

---

# 12. KHÔNG CHO PHÉP CẤU HÌNH NGUY HIỂM

Validation phải ngăn các giá trị có thể làm hệ thống không ổn định.

Ví dụ:
- scan interval quá thấp;
- confirm timeout âm;
- timeout cực lớn làm operator phải chờ vô lý;
- overflow;
- malformed JSON/config.

Không tự chọn con số “chuẩn” nếu chưa có dữ liệu hardware thực tế.

Nếu chưa có bằng chứng để chọn default production tối ưu:
- chọn default bảo thủ dựa trên behavior hiện tại;
- ghi rõ cần tuning bằng board thật;
- không tuyên bố đã tối ưu nếu chưa đo.

---

# 13. RAW FAULT VS CONFIRMED FAULT LOGGING

Để debug production, nếu logging architecture cho phép:

Ghi mức debug/diagnostic:
- candidate fault start;
- candidate cleared;
- confirmed fault;
- elapsed confirmation time;
- relevant connector/pin;
- backend;
- cycle/session id.

Không spam log mỗi scan.

Ví dụ chỉ log transition:

`OPEN candidate started`
`OPEN candidate cleared after 18 ms`
`OPEN confirmed after 80 ms`

Không log secret hoặc dữ liệu không cần thiết.

Customer report chỉ chứa confirmed production result, không chứa transient candidate debug.

---

# 14. PERFORMANCE

Rà soát hot path.

Không được:
- LINQ nặng trên toàn model mỗi scan nếu có thể cache/index;
- lookup connector/pin bằng full collection scan lặp lại ở tần suất cao;
- tạo hàng nghìn string display mỗi scan;
- update ObservableCollection toàn bộ mỗi frame;
- serialize settings/history trên UI thread trong hot loop;
- ghi file đồng bộ mỗi IO sample.

Ưu tiên:
- precomputed mapping/index;
- update-on-change;
- bounded logging;
- background IO;
- minimal allocations trong scan loop.

Chỉ optimize nơi profiling/code inspection cho thấy thực sự nằm trên hot path.

---

# 15. THREADING / ASYNC / CANCELLATION

Kiểm tra:

- cancellation token ownership;
- loop kết thúc khi đổi model/board/window;
- dispose không xảy ra khi reader vẫn đang dùng handle;
- no fire-and-forget task không được theo dõi nếu có thể gây exception;
- exception trong worker phải được observe/log;
- UI update phải marshal đúng Dispatcher;
- không deadlock `.Result` / `.Wait()` trên UI thread;
- reconnect không tạo overlapping worker.

---

# 16. LONG-RUN / SOAK STABILITY

Rà soát và chuẩn bị verification cho vận hành dài.

Tối thiểu logic/software test:

- 100+ test cycles D2XX nếu môi trường cho phép;
- 100+ test cycles UART nếu môi trường cho phép;
- repeated product connect/remove;
- repeated PASS;
- repeated FAIL;
- alternating PASS/FAIL;
- repeated model change;
- repeated board disconnect/reconnect;
- repeated opening/closing relevant dialogs;
- FAIL popup nhiều lần;
- history logging lâu dài.

Nếu có hardware thật:
- soak test nhiều giờ;
- theo dõi memory/CPU/handle/thread;
- không để UI chậm dần theo thời gian.

Nếu không có hardware:
- ghi rõ phần nào mới chỉ software-simulated/static verified.

Không claim production-ready nếu hardware soak chưa chạy.

---

# 17. FAULT CONFIRMATION TEST CASES

Phải có test/verification cho debounce semantics.

## OPEN

A. OPEN 10 ms, threshold 80 ms
→ không FAIL.

B. OPEN 50 ms → normal 10 ms → OPEN 50 ms
→ không cộng thành 100 ms; không FAIL nếu yêu cầu continuous confirmation.

C. OPEN liên tục > threshold
→ FAIL đúng một lần.

## SHORT

A. transient short dưới threshold
→ không FAIL.

B. short ổn định quá threshold
→ FAIL.

## WRONG CONNECTION

A. transient connection change khi fixture đang settle
→ không FAIL trong settle window nếu rule áp dụng.

B. wrong connection tồn tại sau settle + vượt confirm timeout
→ FAIL.

## MULTIPLE FAULTS

Hai fault khác nhau xuất hiện:
→ timer/candidate độc lập.

## RECOVERY

Candidate fault tự hết:
→ state trở lại normal sạch;
→ không để stale timer xác nhận về sau.

---

# 18. KHÔNG DÙNG DEBOUNCE ĐỂ CHE ROOT CAUSE

Nếu phát hiện FAIL giả do:

- duplicate reader;
- stale callback;
- parser sai;
- race condition;
- incorrect edge handling;
- ProductRemoved logic sai;
- model mapping sai;

thì phải sửa root cause.

Không tăng delay lên rất lớn để che bug logic.

Timing/debounce chỉ dùng cho transient vật lý/hardware hợp lệ.

---

# 19. DEFAULTS VÀ TUNING

Không tự đoán các giá trị production cuối cùng nếu chưa test board thật.

Nếu hiện tại chưa có số liệu thực nghiệm:

- giữ default tương thích gần với behavior hiện tại;
- thêm cấu hình;
- cung cấp safe range;
- đánh dấu cần hardware tuning.

Khi hardware testing có dữ liệu, có thể tinh chỉnh:
- IoScanIntervalMs
- OpenCircuitConfirmMs
- ShortCircuitConfirmMs
- WrongConnectionConfirmMs
- ProductSettleTimeMs

Mọi thay đổi default phải được ghi rõ trong changelog/report nếu ảnh hưởng production behavior.

---

# 20. OPERATOR UX

Trong Settings, phần timing phải dễ hiểu.

Gợi ý nhóm:

## THỜI GIAN QUÉT & XÁC NHẬN LỖI

- Chu kỳ quét IO: `[   ] ms`
- Xác nhận hở mạch: `[   ] ms`
- Xác nhận chập mạch: `[   ] ms`
- Xác nhận sai kết nối: `[   ] ms`
- Ổn định sau khi kết nối: `[   ] ms` (chỉ nếu cần)

Tooltip ví dụ:

**Xác nhận hở mạch**
> Lỗi hở phải tồn tại liên tục đủ thời gian này trước khi hệ thống xác nhận FAIL. Dùng để giảm lỗi giả do tiếp xúc chuyển tiếp.

Không dùng wording khiến operator nghĩ tăng delay càng lớn càng tốt.

---

# 21. BUILD / VERIFICATION

Sau khi thực hiện:

1. `dotnet clean`
2. `dotnet restore`
3. `dotnet build -c Release`
4. script verify hiện có theo AGENTS.md
5. kiểm tra WPF binding output
6. relevant tests

Nếu NuGet restore fail do network/TLS/sandbox:
- không sửa source để né lỗi;
- chạy build ngoài sandbox nếu workflow cho phép;
- chỉ báo PASS khi compile thật sự PASS.

---

# 22. CODE REVIEW CHECKLIST CUỐI

Trước khi kết luận:

- Không multiple reader.
- Không stale callback.
- Không recursive event/property.
- Không blocking UI.
- Không unbounded collection/log growth.
- Không duplicate FAIL.
- Không false ProductRemoved.
- Probe không lọt vào production FAIL.
- Candidate fault được reset đúng cycle/model/backend.
- Settings persist/reload đúng.
- Invalid settings không crash.
- Defaults backward-compatible.
- D2XX không regression.
- UART không regression.
- PASS/FAIL/JIG lifecycle không đổi ngoài yêu cầu.
- Customer history chỉ ghi confirmed result.
- UI operator vẫn responsive.

---

# 23. PHÂN LOẠI KẾT QUẢ REVIEW

Cuối cùng phân loại findings:

## BLOCKER
Có thể gây:
- sai PASS/FAIL;
- bỏ lỗi thật;
- FAIL giả đáng kể;
- relay/JIG sai;
- crash/hang;
- data corruption;
- multiple reader;
- production unsafe.

## HIGH
Có thể gây:
- lag đáng kể;
- reconnect không ổn định;
- memory/thread leak;
- stale state;
- lỗi sau nhiều cycle.

## MEDIUM
Ảnh hưởng usability/maintainability nhưng chưa làm sai test trực tiếp.

## LOW
Cleanup nhỏ, không cần sửa nếu tăng rủi ro regression.

Ưu tiên xử lý BLOCKER/HIGH trước.

Không refactor LOW-priority trong vòng production-hardening nếu không cần.

---

# 24. DEFINITION OF DONE

Không được kết luận “ổn định” chỉ vì build PASS.

Chỉ coi software-side verification hoàn thành khi:

- critical logic review hoàn tất;
- timing/debounce semantics rõ ràng;
- settings validation/persistence hoạt động;
- relevant test cases PASS;
- no known BLOCKER;
- no unresolved HIGH issue ảnh hưởng test correctness;
- build Release PASS;
- Git diff sạch/phù hợp;
- các phần chưa hardware-verify được ghi rõ.

**Production-ready cuối cùng vẫn yêu cầu hardware regression + long-run soak test.**

---

# 25. GIT WORKFLOW

Tuân theo `AGENTS.md`.

Trước sửa:
- status;
- fetch;
- sync nếu an toàn.

Sau sửa:
- build/test;
- diff review;
- chỉ stage task-related changes;
- commit;
- fetch lại;
- push nếu an toàn.

Không force push.

Gợi ý commit nếu thay đổi coherent:

`feat: add configurable fault confirmation timing`

hoặc tách:

`fix: harden production fault detection lifecycle`
`feat: add IO scan and fault confirmation settings`

---

# 26. BÁO CÁO CUỐI

Không dump log dài.

Báo cáo ngắn theo format:

## Stability Review
- BLOCKER:
- HIGH:
- MEDIUM:
- LOW:

## Fault Detection
- raw observation flow:
- candidate fault flow:
- confirmed fault flow:
- debounce implementation:

## Timing Settings
- IO scan:
- Open confirm:
- Short confirm:
- Wrong connection confirm:
- Product settle:
- defaults/ranges:

## Performance
- UI thread:
- scan loop:
- allocations:
- memory/thread/handle concerns:

## Verification
- build:
- tests:
- 100+ cycles:
- reconnect:
- hardware soak:

## Unverified
Liệt kê rõ điều gì cần board thật.

## Git
- branch:
- commit:
- push:
- sync status:
