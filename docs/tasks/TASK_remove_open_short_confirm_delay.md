# TASK: LOẠI BỎ DELAY XÁC NHẬN HỞ MẠCH / CHẬP MẠCH
## JBZ Universal Tester

**Mức độ:** CRITICAL / PRODUCTION BLOCKER  
**Phạm vi:** Logic kiểm tra Production liên quan xác nhận HỞ MẠCH / CHẬP MẠCH  
**Không bao gồm:** Các yêu cầu UI trước đó đã hoàn thành

---

# 0. QUY TRÌNH BẮT BUỘC TRƯỚC KHI THỰC HIỆN

Trước khi sửa bất kỳ code nào:

1. Tìm và đọc toàn bộ `AGENTS.md` có hiệu lực trong repository.
2. Nếu có nhiều `AGENTS.md`:
   - đọc từ root;
   - đọc tiếp file trong thư mục con liên quan;
   - tuân thủ file có scope gần file đang sửa nhất.
3. Kiểm tra `git status`.
4. Không sửa/xóa thay đổi hiện có của người dùng nếu không liên quan task này.
5. Audit chính xác:
   - state machine của quá trình test;
   - điều kiện bắt đầu cycle;
   - trạng thái chờ lắp sản phẩm;
   - trạng thái sản phẩm đã sẵn sàng test;
   - scan loop I/O;
   - logic phát hiện HỞ MẠCH;
   - logic phát hiện CHẬP MẠCH;
   - timer/delay xác nhận lỗi;
   - latch lỗi;
   - counter PASS/FAIL/NG;
   - relay liên quan sau khi kết luận lỗi.
6. Trước khi sửa phải báo cáo ngắn:
   - file dự kiến sửa;
   - method/class liên quan;
   - nguyên nhân;
   - cách sửa;
   - rủi ro ảnh hưởng Production.

**Không được sửa trước khi đọc `AGENTS.md`.**

---

# 1. VẤN ĐỀ THỰC TẾ

Hiện tại hệ thống có các tham số:

```text
Xác nhận hở mạch (ms)
Xác nhận chập mạch (ms)
```

Trong quá trình công nhân lắp sản phẩm vào jig:

1. Công nhân mới lắp được một đầu dây/connector.
2. Đầu còn lại chưa kịp lắp vào jig.
3. Trạng thái điện lúc này chỉ là trạng thái chuyển tiếp trong thao tác lắp.
4. Nếu hệ thống bắt đầu timer xác nhận HỞ MẠCH / CHẬP MẠCH ngay lúc này thì có thể kết luận sai sản phẩm NG.

Điều này **không đúng yêu cầu sản xuất**.

---

# 2. YÊU CẦU CHÍNH

Phải **LOẠI BỎ cơ chế delay xác nhận riêng cho HỞ MẠCH và CHẬP MẠCH** khỏi logic quyết định PASS/FAIL.

Không được tiếp tục dùng:

```text
Open Circuit Confirm Delay
Short Circuit Confirm Delay
```

hoặc property/timer có tên tương đương để chờ một khoảng thời gian rồi mới chốt lỗi.

---

# 3. CỰC KỲ QUAN TRỌNG: KHÔNG ĐƯỢC CHỈ ĐỔI DELAY VỀ 0 ms

Không được sửa đơn giản:

```text
150 ms -> 0 ms
100 ms -> 0 ms
```

rồi kết luận lỗi ngay lập tức.

Cách làm đó vẫn sai vì khi công nhân mới lắp một đầu dây thì mạch chưa hoàn chỉnh.

Logic đúng phải là:

```text
JIG TRỐNG / ĐANG CHỜ LẮP
        ↓
CÔNG NHÂN ĐANG LẮP SẢN PHẨM
        ↓
KHÔNG CHỐT HỞ / CHẬP
        ↓
SẢN PHẨM ĐÃ ĐỦ ĐIỀU KIỆN BẮT ĐẦU TEST
        ↓
ĐÁNH GIÁ TRẠNG THÁI MẠCH
        ↓
HỞ / CHẬP THẬT → FAIL
MẠCH ĐÚNG        → TIẾP TỤC TEST / PASS
```

---

# 4. TRẠNG THÁI ĐANG LẮP SẢN PHẨM KHÔNG ĐƯỢC COI LÀ LỖI

Khi công nhân:

```text
đã cắm đầu A
nhưng chưa cắm đầu B
```

hoặc sản phẩm chưa hoàn thiện việc lắp vào jig:

Hệ thống phải:

- không latch HỞ MẠCH;
- không latch CHẬP MẠCH;
- không ghi FAIL Production;
- không tăng counter NG;
- không kích relay NG;
- không kết thúc cycle test;
- không ghi lỗi transient thành lỗi sản phẩm;
- tiếp tục chờ điều kiện hợp lệ để bắt đầu đánh giá.

---

# 5. PHẢI XÁC ĐỊNH ĐÚNG ĐIỀU KIỆN "READY TO TEST"

Codex phải audit và xác định điều kiện hiện tại mà hệ thống dùng để biết:

> sản phẩm đã được lắp đầy đủ và đủ điều kiện bắt đầu kiểm tra.

Không được tự đoán.

Phải tìm các state/flag/method tương đương như:

```text
Ready
WaitingForInstall
ProductPresent
HarnessPresent
IsInstalled
StartTest
Testing
CycleActive
StableAfterInstall
```

hoặc tên thực tế trong source.

Chỉ khi state machine xác nhận sản phẩm đã vào trạng thái hợp lệ để test thì mới được đánh giá HỞ/CHẬP.

Nếu source hiện tại **không có điều kiện đủ tin cậy để phân biệt "đang lắp" và "đã lắp xong"**, phải dừng và báo cáo rõ trước khi tự tạo logic mới.

---

# 6. HÀNH VI MONG MUỐN SAU KHI SỬA

## Case A — Jig trống

Expected:

- ở trạng thái chờ lắp;
- không báo HỞ MẠCH như lỗi sản phẩm;
- không báo CHẬP MẠCH như lỗi sản phẩm;
- không tăng NG;
- không kích relay lỗi.

---

## Case B — Công nhân chỉ mới lắp một đầu

Ví dụ:

```text
Đầu 1: đã cắm
Đầu 2: chưa cắm
```

Expected:

- hệ thống hiểu là đang lắp;
- không chốt HỞ MẠCH;
- không chốt CHẬP MẠCH;
- không FAIL;
- không tăng counter NG;
- không kích relay NG;
- tiếp tục chờ.

Dù công nhân giữ trạng thái này lâu hơn giá trị delay cũ thì **vẫn không được biến thành lỗi sản phẩm**.

---

## Case C — Lắp đầy đủ, hàng tốt

Expected:

- hệ thống nhận biết sản phẩm sẵn sàng test;
- bắt đầu đánh giá mạch;
- hàng tốt kiểm tra bình thường;
- không bị ảnh hưởng bởi timer HỞ/CHẬP cũ;
- PASS theo quy trình hiện tại.

---

## Case D — Lắp đầy đủ, hàng thực sự hở mạch

Expected:

- khi đã vào trạng thái kiểm tra hợp lệ;
- phát hiện HỞ MẠCH theo logic điện hiện tại;
- không cần chờ thêm `Xác nhận hở mạch (ms)`;
- xử lý FAIL theo quy trình hiện hành.

---

## Case E — Lắp đầy đủ, hàng thực sự chập mạch

Expected:

- khi đã vào trạng thái kiểm tra hợp lệ;
- phát hiện CHẬP MẠCH theo logic điện hiện tại;
- không cần chờ thêm `Xác nhận chập mạch (ms)`;
- xử lý FAIL theo quy trình hiện hành.

---

# 7. XÓA HAI THAM SỐ KHỎI SETTINGS

Loại bỏ khỏi giao diện Settings:

```text
Xác nhận hở mạch (ms)
Xác nhận chập mạch (ms)
```

Không để lại textbox disabled khiến người vận hành hiểu rằng hai tham số vẫn còn tác dụng.

Nếu tên binding/property thực tế khác, phải xác định đúng property tương ứng trước khi xóa.

---

# 8. TƯƠNG THÍCH CONFIG CŨ

Nếu file config Production hiện tại có key cũ tương đương:

```text
OpenCircuitConfirmMs
ShortCircuitConfirmMs
```

thì:

- config cũ vẫn phải load được;
- không crash deserialize;
- không bắt buộc người dùng sửa tay file config;
- runtime không còn dùng hai giá trị delay này để quyết định lỗi;
- không phá schema một cách không cần thiết.

Có thể giữ property legacy để tương thích đọc config nếu cần, nhưng phải đảm bảo **logic runtime không còn phụ thuộc vào nó**.

---

# 9. AUDIT TIMER / TIMESTAMP / LATCH

Phải tìm và audit các biến tương đương:

```text
OpenFaultSince
ShortFaultSince
OpenConfirmTimer
ShortConfirmTimer
PendingOpenFault
PendingShortFault
CurrentFault
LatchedFault
FaultStartTime
LastFaultTime
IsFaultConfirmed
```

Yêu cầu:

- không carry-over timer cũ sang sản phẩm mới;
- không giữ pending HỞ/CHẬP từ trạng thái đang lắp;
- không để timestamp bắt đầu từ lúc công nhân mới cắm một đầu;
- không để lỗi transient trở thành lỗi Production.

---

# 10. RESET Ở ĐẦU CYCLE

Khi bắt đầu sản phẩm/cycle mới phải đảm bảo reset đúng các trạng thái tạm liên quan:

- pending open;
- pending short;
- timestamp open cũ;
- timestamp short cũ;
- fault debounce state cũ nếu thuộc logic này;
- fault latch của cycle trước;
- temporary scan state không còn hợp lệ.

Không được reset nhầm:

- tổng counter;
- lifetime counter;
- model đang chọn;
- Master data;
- Production settings khác.

---

# 11. KHÔNG ĐỤNG NHẦM CÁC TIMING KHÁC

Task này chỉ yêu cầu loại bỏ:

```text
Xác nhận hở mạch
Xác nhận chập mạch
```

Không tự ý thay đổi:

- chu kỳ quét I/O;
- ổn định sau khi lắp;
- đánh giá tiếp xúc JIG;
- R1 JIG pulse;
- R2 MARKING pulse;
- PASS chờ R2 -> R1;
- trễ đo điện trở;
- Probe Pin;
- UART/COM timing;
- debounce phần cứng khác;
- `Xác nhận sai kết nối (ms)`.

Nếu `Xác nhận sai kết nối` đang dùng chung implementation với HỞ/CHẬP thì phải tách rõ, không được xóa nhầm.

---

# 12. KHÔNG THAY ĐỔI THUẬT TOÁN ĐIỆN

Không tự ý thay đổi:

- mapping I/O;
- chân PIN;
- định nghĩa open circuit;
- định nghĩa short circuit;
- wrong connection;
- CLIP;
- điểm dập nối;
- Master Good;
- Master NG;
- PASS/FAIL criteria;
- relay sequence ngoài phần liên quan trực tiếp;
- đo điện trở;
- printer;
- Probe Pin.

Task này chỉ sửa **thời điểm được phép đánh giá HỞ/CHẬP** và loại bỏ delay xác nhận riêng.

---

# 13. TEST BẮT BUỘC

## Test 1 — Jig trống

Expected:
- không FAIL;
- không tăng NG;
- không relay NG;
- không latch HỞ/CHẬP như lỗi sản phẩm.

## Test 2 — Chỉ cắm một đầu dây

Giữ trạng thái này lâu hơn delay cũ.

Expected:
- không FAIL;
- không tăng NG;
- không relay NG;
- không ghi Production lỗi.

## Test 3 — Cắm tiếp đầu còn lại đúng

Expected:
- hệ thống chuyển sang trạng thái đủ điều kiện test;
- hàng tốt PASS bình thường.

## Test 4 — Hàng hở thật

Lắp đầy đủ sản phẩm có lỗi open thật.

Expected:
- phát hiện HỞ MẠCH;
- không chờ delay xác nhận hở cũ;
- FAIL đúng.

## Test 5 — Hàng chập thật

Lắp đầy đủ sản phẩm có lỗi short thật.

Expected:
- phát hiện CHẬP MẠCH;
- không chờ delay xác nhận chập cũ;
- FAIL đúng.

## Test 6 — Tháo hàng / cycle mới

Expected:
- pending fault cũ không kéo sang sản phẩm tiếp theo.

## Test 7 — Config cũ

Load config đang chứa hai giá trị delay cũ.

Expected:
- load bình thường;
- không crash;
- runtime không sử dụng hai delay đó.

---

# 14. BUILD / RUNTIME VALIDATION

Sau khi sửa:

1. Build theo đúng quy trình trong `AGENTS.md`.
2. Không tự ý đổi framework/SDK/NuGet.
3. Không phát sinh warning/error mới do task này.
4. Chạy runtime thực tế.
5. Nếu có thể phải test bằng jig/bo thật.
6. Nếu môi trường Codex không có hardware thì:
   - phải mô phỏng các state;
   - ghi rõ phần nào chưa thể xác nhận trên hardware.

Không được coi `Build succeeded` là hoàn thành.

---

# 15. ACCEPTANCE CRITERIA

Task chỉ DONE khi đạt toàn bộ:

- [ ] Đã đọc `AGENTS.md`.
- [ ] Đã xác định đúng logic delay HỞ MẠCH.
- [ ] Đã xác định đúng logic delay CHẬP MẠCH.
- [ ] Runtime không còn dùng hai delay này để quyết định lỗi.
- [ ] Hai field delay không còn trong Settings.
- [ ] Config cũ vẫn load bình thường.
- [ ] Jig trống không bị tính FAIL sản phẩm.
- [ ] Cắm một đầu dây không bị chốt HỞ/CHẬP.
- [ ] Không tăng NG khi đang lắp sản phẩm.
- [ ] Không kích relay NG khi đang lắp sản phẩm.
- [ ] Lắp đủ hàng tốt vẫn PASS.
- [ ] Hàng hở thật vẫn FAIL.
- [ ] Hàng chập thật vẫn FAIL.
- [ ] Không carry-over pending fault/timer sang cycle mới.
- [ ] Không thay đổi `Xác nhận sai kết nối`.
- [ ] Không thay đổi timing khác ngoài phạm vi.
- [ ] Không thay đổi thuật toán điện.
- [ ] Build thành công.
- [ ] Runtime không crash.
- [ ] Git diff chỉ chứa thay đổi liên quan task này.
- [ ] Không tạo file backup `.bak`, `_old`, `_copy` trong source tree.

---

# 16. BÁO CÁO SAU KHI HOÀN THÀNH

Codex phải trả báo cáo:

## A. AGENTS.md
- Các file đã đọc:
- Quy tắc đã áp dụng:

## B. Root cause
- File:
- Class:
- Method:
- Property/timer HỞ cũ:
- Property/timer CHẬP cũ:
- Timer bắt đầu ở đâu:
- Timer reset ở đâu:
- Vì sao trạng thái đang lắp có thể bị hiểu sai thành lỗi:

## C. State machine
- Trạng thái chờ lắp:
- Trạng thái đang lắp:
- Điều kiện `Ready To Test`:
- Trạng thái Testing:
- Điều kiện kết luận HỞ/CHẬP mới:

## D. Files changed

| File | Thay đổi | Lý do |
|------|----------|-------|

## E. Validation
- Build:
- Runtime:
- Jig trống:
- Chỉ lắp một đầu:
- Lắp đủ hàng tốt:
- Open thật:
- Short thật:
- Config cũ:
- Cycle mới/reset:

## F. Xác nhận không thay đổi
- I/O mapping;
- relay sequence ngoài phạm vi;
- wrong connection;
- Master;
- Production counters ngoài lỗi này;
- resistance;
- printer;
- Probe Pin;
- `.model` schema.

---

# 17. YÊU CẦU CUỐI CÙNG

Đây là thay đổi Production quan trọng.

Không được sửa theo kiểu:

```text
set delay = 0
```

rồi coi là hoàn thành.

Phải thực hiện đúng:

```text
READ AGENTS.md
→ AUDIT STATE MACHINE
→ TÌM ĐÚNG TIMER HỞ/CHẬP
→ XÁC ĐỊNH READY-TO-TEST
→ LOẠI BỎ DELAY HỞ/CHẬP
→ KHÔNG FAIL TRONG GIAI ĐOẠN ĐANG LẮP
→ GIỮ PHÁT HIỆN LỖI THẬT
→ BUILD
→ RUNTIME TEST
→ AUDIT GIT DIFF
→ BÁO CÁO
```
