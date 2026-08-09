# FINAL PRODUCTION TASK — Stability, Fault Confirmation, Jig/Probe Reliability & Per-Model Maintenance Counters

## Mục tiêu cuối cùng

Đây là task production-hardening cuối cùng cho phần mềm.

**Bắt buộc đọc `AGENTS.md` trước khi sửa.**

Ưu tiên theo thứ tự:

1. Độ chính xác PASS/FAIL.
2. Không tạo FAIL giả do JIG/Probe Pin tiếp xúc không ổn định.
3. Phần mềm chạy lâu dài ổn định, không lag/treo/leak.
4. Logic test D2XX/UART giữ đúng semantics đã xác minh.
5. Có timing/debounce cấu hình được cho IO/fault confirmation.
6. Có bộ đếm test riêng theo từng mã hàng/model.
7. Có bộ đếm chu kỳ sử dụng Probe Pin/JIG và cảnh báo bảo trì.
8. Reset chu kỳ Probe Pin chỉ được thực hiện bởi quản trị viên có xác thực.

Không audit/refactor toàn repository một cách lan man. Chỉ trace các luồng production-critical và các file liên quan trực tiếp.

---

# 1. NGUYÊN TẮC QUAN TRỌNG VỀ JIG / PROBE PIN

Trong vận hành lâu dài, Probe Pin vật lý có thể:

- mòn;
- bẩn;
- oxy hóa;
- lún;
- yếu lò xo;
- tiếp xúc chập chờn;
- cần người vận hành lay/ấn JIG để tiếp xúc lại.

Một mất continuity ngắn hoặc trạng thái OPEN không ổn định do tiếp xúc JIG **không được tự động kết luận là lỗi hở mạch của sản phẩm**.

Phải phân biệt:

1. **Product Fault**
   - lỗi thực tế thuộc sản phẩm/dây/mạch.

2. **Jig/Probe Contact Instability**
   - tín hiệu không ổn định có khả năng do fixture/probe/contact;
   - không được ghi ngay là OPEN CIRCUIT của sản phẩm;
   - không được tính FAIL sản phẩm nếu chưa đủ điều kiện xác nhận.

Không được đổi mọi OPEN thành lỗi JIG.

Chỉ chuyển sang trạng thái nghi ngờ JIG khi behavior thực tế phù hợp với rule xác nhận bên dưới.

---

# 2. FAULT STATE MACHINE

Tách rõ:

`Raw Observation`
→ `Candidate Fault`
→ `Confirmed Product Fault`

và thêm nhánh:

`Raw Observation`
→ `Contact Instability Candidate`
→ `Jig/Probe Contact Warning`

Mục tiêu:

- transient ngắn không tạo FAIL;
- contact bounce do Probe Pin không tạo FAIL sản phẩm;
- OPEN ổn định thật sự vẫn phải được phát hiện;
- không dùng delay quá lớn để che bug logic.

---

# 3. THỜI GIAN XÁC NHẬN HỞ MẠCH

Setting:

**Thời gian xác nhận hở mạch**

Internal semantic:
`OpenCircuitConfirmMs`

OPEN chỉ trở thành lỗi sản phẩm khi trạng thái hở tồn tại liên tục đủ thời gian.

Ví dụ:

- OPEN 10 ms rồi trở lại → không FAIL.
- OPEN 50 ms → normal → OPEN 50 ms → không cộng dồn thành 100 ms.
- OPEN liên tục vượt threshold → mới được xem xét xác nhận.

Candidate timer phải reset khi continuity trở lại.

Không dùng wall clock không ổn định; ưu tiên elapsed/monotonic time.

---

# 4. LOẠI TRỪ FAIL GIẢ DO JIG/PROBE CONTACT

Ngoài OpenCircuitConfirmMs, hãy thiết kế logic phân biệt contact instability an toàn.

Mục tiêu behavior:

Nếu một connection/pin:
- liên tục mất/có lại trong thời gian ngắn;
- recovery xảy ra khi contact/JIG được ổn định lại;
- chưa thỏa điều kiện confirmed product fault;

thì:

- không tạo FAIL sản phẩm;
- không tăng Fail count;
- không ghi customer history là OPEN CIRCUIT;
- không chạy FAIL/JIG reject lifecycle;
- có thể hiển thị cảnh báo operator:
  **“TIẾP XÚC JIG/PROBE KHÔNG ỔN ĐỊNH — KIỂM TRA PROBE PIN/JIG”**

Không dùng từ này nếu engine không có bằng chứng đủ để phân biệt.

Nếu không thể chắc chắn product fault hay fixture contact:
- ưu tiên state `CONTACT UNSTABLE / CẦN KIỂM TRA JIG`;
- yêu cầu operator kiểm tra/ổn định JIG;
- sau đó re-evaluate bằng một chu kỳ xác nhận sạch;
- không tự PASS sản phẩm chỉ vì tín hiệu quay lại.

---

# 5. JIG CONTACT RETRY / RECHECK

Nếu architecture hiện tại cho phép an toàn, có thể thêm cơ chế recheck có giới hạn:

Ví dụ semantic:

1. phát hiện OPEN candidate;
2. chưa đủ điều kiện confirmed;
3. contact phục hồi;
4. đánh dấu contact instability;
5. yêu cầu/tự thực hiện re-evaluation;
6. chỉ quyết định PASS/FAIL sau khi tín hiệu ổn định.

Không tạo vòng retry vô hạn.

Không dùng retry để che lỗi sản phẩm thật.

Nếu sau recheck:
- OPEN vẫn ổn định vượt threshold → FAIL sản phẩm;
- contact tiếp tục chập chờn → cảnh báo JIG/PROBE;
- cần ghi diagnostic nội bộ để kỹ thuật bảo trì.

---

# 6. SETTINGS — THỜI GIAN QUÉT & XÁC NHẬN LỖI

Trong Settings, có nhóm:

## THỜI GIAN QUÉT & XÁC NHẬN LỖI

Các setting:

### 6.1. Chu kỳ quét IO
`IoScanIntervalMs`

Label:
**Chu kỳ quét IO**

### 6.2. Xác nhận hở mạch
`OpenCircuitConfirmMs`

Label:
**Thời gian xác nhận hở mạch**

### 6.3. Xác nhận chập mạch
`ShortCircuitConfirmMs`

Label:
**Thời gian xác nhận chập mạch**

### 6.4. Xác nhận sai kết nối
`WrongConnectionConfirmMs`

Label:
**Thời gian xác nhận sai kết nối**

### 6.5. Ổn định sau khi kết nối sản phẩm
`ProductSettleTimeMs`

Label:
**Thời gian ổn định sau khi kết nối**

Chỉ thêm nếu flow hiện tại thực sự cần.

### 6.6. Xác nhận contact JIG không ổn định

Nếu implementation cần một tham số riêng:

`JigContactUnstableWindowMs`

Label:
**Thời gian đánh giá tiếp xúc JIG**

Chỉ thêm nếu có semantic rõ ràng.

Không dùng một biến `Delay` chung cho tất cả.

---

# 7. VALIDATION SETTINGS

Timing setting phải:

- có đơn vị ms;
- có Min/Max;
- không cho giá trị âm;
- không cho scan interval quá thấp gây CPU cao;
- không cho timeout vô lý;
- persist/reload đúng;
- backward-compatible với config cũ;
- missing field → dùng default an toàn.

Không tự chọn “default production tối ưu” nếu chưa có test hardware thật.

Nếu chưa có dữ liệu:
- dùng default bảo thủ;
- ghi rõ cần tuning bằng JIG/board thật.

---

# 8. BỘ ĐẾM SẢN PHẨM THEO TỪNG MODEL / MÃ HÀNG

Mỗi model/mã hàng phải có bộ đếm riêng.

Không dùng một counter chung cho toàn bộ sản phẩm.

Ví dụ key:

- PartNumber
- ModelId / model filename stem
- hoặc canonical product identity hiện tại

Phải giữ ổn định khi restart app.

Không phụ thuộc chỉ vào text display dễ đổi.

---

# 9. CÁC BỘ ĐẾM CẦN LƯU

Cho mỗi mã hàng/model, tối thiểu lưu:

## 9.1. Số sản phẩm test hôm nay

`DailyTestCount`

Hiển thị:
**Sản lượng kiểm tra hôm nay**

## 9.2. Số sản phẩm test trong tháng

`MonthlyTestCount`

Hiển thị:
**Sản lượng kiểm tra tháng này**

## 9.3. Tổng số sản phẩm đã test

`LifetimeTestCount`

Hiển thị:
**Tổng số lần kiểm tra**

Nếu history hiện tại đã đủ để tính daily/monthly:
- có thể query/aggregate an toàn;
- không duplicate source-of-truth không cần thiết.

Nhưng nếu query history mỗi scan gây nặng:
- dùng counter cache/persistence hiệu quả;
- đảm bảo reconcile được khi cần.

---

# 10. PROBE PIN MAINTENANCE COUNTER

Mỗi mã hàng/model có thêm counter bảo trì Probe Pin/JIG:

`ProbeCycleCount`

Label:
**Chu kỳ sử dụng Probe Pin**

Default maintenance threshold:

`ProbeReplacementThreshold = 200000`

Label:
**Chu kỳ thay Probe Pin**

Default:
**200.000 lần kiểm tra**

Threshold phải cấu hình được trong Settings/admin nếu phù hợp.

Không hard-code 200000 ở nhiều file.

---

# 11. QUY TẮC TĂNG PROBE CYCLE

Probe wear liên quan số lần JIG/Probe thực sự tham gia kiểm tra.

Ưu tiên:

- tăng `ProbeCycleCount` đúng **một lần cho mỗi test cycle thực sự bắt đầu/được thực hiện trên sản phẩm**;
- không tăng theo mỗi scan IO;
- không tăng theo mỗi frame;
- không tăng nhiều lần do cùng một confirmed fault;
- không tăng do mở màn hình/Probe/TestPin debug.

Nếu một sản phẩm được test lại thực sự bằng JIG:
- đây vẫn là một chu kỳ cơ khí mới và có thể tăng ProbeCycleCount;
- nhưng production quantity counter phải tuân theo semantics sản lượng hiện tại để tránh double-count.

Do đó cần phân biệt nếu cần:

- `ProductTestCount` = số sản phẩm/cycle theo production semantics.
- `ProbeCycleCount` = số lần fixture/probe thực sự thực hiện test.

Không ép hai counter phải giống nhau nếu thực tế re-test có xảy ra.

---

# 12. CẢNH BÁO THAY PROBE PIN

Khi:

`ProbeCycleCount >= ProbeReplacementThreshold`

phải cảnh báo rõ:

**ĐẾN CHU KỲ THAY PROBE PIN**

Ví dụ:

- Mã hàng: ABC123
- Chu kỳ hiện tại: 200.000
- Chu kỳ thay thế: 200.000
- Trạng thái: CẦN THAY PROBE PIN

Cảnh báo phải:
- dễ thấy;
- không spam popup mỗi scan;
- xuất hiện theo transition/state;
- vẫn còn trạng thái maintenance due cho tới khi được admin xác nhận reset.

Nếu tiếp tục test sau threshold:
- behavior phải theo policy an toàn hiện tại;
- mặc định có thể cảnh báo nhưng không tự khóa production nếu chưa được yêu cầu rõ;
- nếu project muốn hard-block sau một mức khác thì phải là setting/policy riêng, không tự suy đoán.

---

# 13. ADMIN RESET PROBE COUNTER

Reset `ProbeCycleCount` chỉ được phép sau khi người có quyền quản trị xác nhận.

Flow:

1. Operator/technician chọn:
   **Xác nhận đã thay Probe Pin**
2. Phần mềm yêu cầu xác thực quản trị viên.
3. Chỉ khi authentication PASS mới cho reset.
4. Reset `ProbeCycleCount` về 0.
5. Lưu:
   - thời gian reset;
   - model/mã hàng;
   - giá trị counter trước reset;
   - người thực hiện/admin identity nếu hệ thống có;
   - lý do/action: `PROBE PIN REPLACED`.

Không reset:
- DailyTestCount;
- MonthlyTestCount;
- LifetimeTestCount;
- test history.

Chỉ reset maintenance counter.

---

# 14. MẬT KHẨU QUẢN TRỊ

Không lưu password plaintext trong source/config nếu có thể tránh.

Ưu tiên:
- dùng authentication/admin mechanism hiện có;
- hoặc lưu hash + salt;
- không log password;
- không đưa password vào Git;
- không hard-code password production.

Nếu project đã có admin password:
- tái sử dụng đúng cơ chế hiện tại;
- không tạo hệ authentication thứ hai không cần thiết.

Nếu chưa có:
- triển khai tối thiểu, an toàn, có thể cấu hình;
- không làm lộ secret.

---

# 15. MAINTENANCE HISTORY

Mỗi lần reset Probe Pin nên có maintenance record.

Ví dụ fields:

- Timestamp
- PartNumber / Model
- PreviousProbeCycleCount
- NewProbeCycleCount
- ReplacementThreshold
- Admin/User
- Action = ProbePinReplacement
- Station nếu có

Customer production history và maintenance history nên phân biệt.

Không ghi event reset Probe Pin thành product FAIL/PASS.

---

# 16. HIỂN THỊ COUNTER TRÊN GIAO DIỆN

Ở màn hình vận hành, hiển thị gọn, không làm nặng UI.

Gợi ý:

- Hôm nay: 1.245
- Tháng này: 28.430
- Probe Pin: 187.520 / 200.000

Khi gần threshold:
- có trạng thái cảnh báo nhẹ.

Khi đạt threshold:
- hiển thị rõ **CẦN THAY PROBE PIN**.

Không refresh toàn bộ UI mỗi scan.

Counter chỉ update khi cycle count thay đổi.

---

# 17. NGƯỠNG CẢNH BÁO SỚM

Nếu phù hợp, hỗ trợ warning threshold trước maintenance due.

Ví dụ:
`ProbeWarningPercent = 90%`

Tức:
- >= 180.000 / 200.000 → cảnh báo sắp đến chu kỳ thay;
- >= 200.000 → maintenance due.

Không bắt buộc nếu làm tăng complexity không cần thiết.

Nếu thêm:
- phải configurable;
- không hard-code nhiều nơi.

---

# 18. PERSISTENCE / DATA INTEGRITY

Counter phải tồn tại sau:

- đóng/mở phần mềm;
- reboot Windows;
- model switch;
- board reconnect;
- app crash hợp lý nếu persistence architecture hỗ trợ atomic write.

Không để mất counter vì app shutdown bất thường nếu tránh được.

Ưu tiên:
- persistence transactional/atomic phù hợp;
- không write file mỗi IO scan;
- chỉ persist khi counter/state thay đổi;
- tránh corrupt JSON/config.

Nếu dùng database/history:
- dùng transaction phù hợp;
- không migration destructive.

---

# 19. DAILY / MONTHLY RESET

Daily/monthly counter phải đổi kỳ theo ngày/tháng local production time.

Không xóa history.

Có thể:
- reset logical counter khi date/month thay đổi;
- hoặc aggregate từ stored records.

Phải xử lý:
- app tắt qua đêm;
- app tắt qua đầu tháng;
- mở lại sau nhiều ngày.

Không cần app phải chạy đúng 00:00 mới reset.

LifetimeTestCount và ProbeCycleCount không reset theo ngày/tháng.

---

# 20. PER-MODEL ISOLATION

Counter của model A không được ảnh hưởng model B.

Ví dụ:

ABC123:
- Today 1.000
- Month 20.000
- Probe 150.000 / 200.000

XYZ456:
- Today 300
- Month 8.000
- Probe 40.000 / 200.000

Khi đổi mã hàng:
- UI load đúng counter của mã hàng mới;
- không mang state maintenance/candidate của model cũ sang model mới.

---

# 21. FAIL / PASS COUNT SEMANTICS

Không thay đổi production count hiện tại một cách vô tình.

Phải xác định:
- PASS count tăng ở đâu;
- FAIL count tăng ở đâu;
- Total test count tăng ở đâu;
- re-test xử lý thế nào.

Không double count do:
- event callback lặp;
- reconnect;
- dialog confirm;
- frame duplicate;
- re-arm.

ProbeCycleCount phải có rule riêng như section trên.

---

# 22. JIG CONTACT WARNING KHÔNG PHẢI PRODUCT FAIL

Trạng thái:

**JIG/PROBE CONTACT UNSTABLE**

không được:

- tăng Fail count;
- ghi sản phẩm FAIL;
- tạo customer defect record;
- pulse reject relay;
- chạy FAIL lifecycle;
- tăng defect statistics.

Có thể:
- log diagnostic;
- tăng maintenance/contact-instability counter nếu sau này cần;
- hiển thị operator warning;
- yêu cầu reseat/check JIG.

Nếu sau khi re-evaluate vẫn có confirmed product fault:
- lúc đó xử lý FAIL bình thường.

---

# 23. OPTIONAL — CONTACT INSTABILITY COUNTER

Nếu dễ triển khai và có giá trị bảo trì, có thể thêm per-model:

`JigContactWarningCount`

Mục đích:
- biết model/JIG nào thường xuyên bị contact instability;
- hỗ trợ quyết định thay Probe Pin sớm.

Không dùng counter này thay thế `ProbeCycleCount`.

Không bắt buộc nếu tăng complexity lớn.

---

# 24. PERFORMANCE / LONG-RUN STABILITY

Rà soát:

- CPU;
- RAM;
- handle;
- thread;
- timer;
- event subscriber;
- COM/FTDI reader;
- DataGrid refresh;
- log growth;
- counter persistence frequency.

Không:
- query full history mỗi scan;
- write counter file mỗi frame;
- trigger UI refresh liên tục;
- dùng blocking IO trên UI thread.

---

# 25. D2XX / UART BOUNDARY

Giữ nguyên:

- D2XX `.tht` / current engine semantics.
- UART `.model + .setup` / firmware semantics.
- không trộn protocol.
- không tạo duplicate readers.
- debounce/contact evaluation ở layer phù hợp phía trên raw transport.

---

# 26. PROBE / TESTPIN DEBUG MODE

TEST PROBE PIN / TESTPIN:

- không tăng ProductTestCount;
- không tăng ProbeCycleCount nếu chỉ là diagnostic đọc pin không chạy production test cycle;
- không tạo PASS/FAIL;
- không tạo customer history;
- không kích relay.

Nếu diagnostic thật sự actuate fixture theo cách làm mòn probe và project muốn tính cycle, phải là rule explicit; mặc định không tính.

---

# 27. FAULT DISPLAY

Operator UI vẫn theo chuẩn:

- tiếng Việt;
- “Tiêu chuẩn / Thực tế / Sai lệch”;
- không dùng “mong muốn”.

Customer report/export:
- English technical terminology chuẩn.

Jig contact warning nội bộ không được xuất thành product defect cho khách hàng.

Có thể xuất maintenance report riêng nếu cần sau này.

---

# 28. TEST CASES BẮT BUỘC

## Fault debounce

1. OPEN ngắn hơn threshold → không FAIL.
2. OPEN chập chờn → không cộng dồn.
3. OPEN ổn định vượt threshold → FAIL.
4. SHORT transient → không FAIL.
5. SHORT ổn định → FAIL.
6. Wrong connection transient → không FAIL.
7. Wrong connection ổn định → FAIL.

## Jig/Probe contact

8. Contact mất/có nhanh nhiều lần → Jig/Probe warning, không product FAIL.
9. Operator ổn định lại JIG → re-evaluate sạch.
10. Sau re-evaluate vẫn OPEN ổn định → product FAIL.
11. Jig warning không tăng Fail count/customer defect.

## Counters

12. Test model A → chỉ counter A tăng.
13. Đổi model B → counter B độc lập.
14. Restart app → counter giữ nguyên.
15. Qua ngày mới → Daily đổi kỳ đúng.
16. Qua tháng mới → Monthly đổi kỳ đúng.
17. ProbeCycleCount không reset theo ngày/tháng.
18. Probe cycle đạt 200000 → cảnh báo đúng một state, không spam.
19. Operator thường không reset được.
20. Admin auth PASS → reset ProbeCycleCount.
21. Reset Probe không xóa production history.
22. Maintenance record được lưu.
23. Re-test thực sự → ProbeCycleCount tăng đúng rule.
24. Duplicate event → không tăng counter hai lần.

---

# 29. SOAK TEST

Nếu có hardware:

- chạy nhiều giờ;
- ít nhất 100+ cycle mỗi backend;
- repeated PASS/FAIL;
- repeated connect/remove;
- repeated JIG contact disturbance;
- repeated reconnect;
- model switching;
- theo dõi CPU/RAM/thread/handle.

Không claim production-ready nếu chưa chạy hardware soak thật.

---

# 30. BUILD / VERIFICATION

Theo `AGENTS.md`:

- `dotnet clean`
- `dotnet restore`
- `dotnet build -c Release`
- verify script hiện có
- WPF binding output
- relevant tests

Nếu NuGet restore lỗi do sandbox/TLS:
- không sửa code để né;
- build ngoài sandbox nếu được phép;
- chỉ xác nhận PASS khi compile thật sự PASS.

---

# 31. PRIORITY OF FIXES

Phân loại:

## BLOCKER
- sai PASS/FAIL;
- product fault bị bỏ;
- Jig contact bị ghi thành product FAIL;
- duplicate counter;
- counter mất dữ liệu;
- multiple reader;
- crash/hang;
- relay/JIG sai.

## HIGH
- contact warning/retry sai;
- memory/thread/handle leak;
- reconnect stale state;
- settings persistence sai;
- admin reset security yếu.

## MEDIUM
- UX/counter display;
- report wording;
- maintenance history presentation.

## LOW
- cleanup/refactor không ảnh hưởng production.

Chỉ sửa LOW nếu không tăng regression risk.

---

# 32. DEFINITION OF DONE

Không coi hoàn thành chỉ vì build PASS.

Cần:

- fault confirmation semantics đúng;
- Jig/Probe contact instability không tạo FAIL giả;
- confirmed product OPEN vẫn phát hiện được;
- timing settings hoạt động/persist;
- per-model counters hoạt động;
- ProbeCycleCount hoạt động;
- threshold 200000 hoạt động;
- admin reset an toàn;
- maintenance history lưu đúng;
- không double count;
- UI không lag;
- không known BLOCKER;
- relevant HIGH issue được xử lý hoặc ghi rõ;
- build Release PASS;
- hardware soak status được báo rõ.

---

# 33. GIT WORKFLOW

Tuân theo `AGENTS.md`.

Trước task:
- `git status`
- `git fetch origin`
- sync nếu an toàn.

Sau task:
- build/test;
- review diff;
- stage đúng file;
- commit;
- fetch lại;
- push nếu an toàn.

Không force push.

Gợi ý tách commit nếu cần:

`fix: harden jig contact fault confirmation`

`feat: add per-model production and probe maintenance counters`

`feat: add admin-controlled probe replacement reset`

---

# 34. BÁO CÁO CUỐI

Báo cáo ngắn:

## Fault Detection
- Open confirmation:
- Short confirmation:
- Wrong connection confirmation:
- Jig/Probe instability handling:

## Counters
- per-model identity:
- daily:
- monthly:
- lifetime:
- probe cycle:
- threshold:

## Maintenance
- warning:
- admin reset:
- persistence:
- history:

## Stability
- CPU/UI:
- memory/thread/handle:
- reader lifecycle:

## Verification
- build:
- tests:
- hardware:
- soak:

## Unverified
Những gì vẫn cần test trên JIG/board thật.

## Git
- branch:
- commits:
- push:
- sync status:
