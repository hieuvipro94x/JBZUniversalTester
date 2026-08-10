# TASK: SỬA LỖI HỞ MẠCH GIẢ – KHÔNG ĐƯỢC BÁO FAIL KHI CÔNG NHÂN ĐANG LẮP SẢN PHẨM
## JBZ Universal Tester

**Mức độ:** CRITICAL / PRODUCTION BLOCKER  
**Phạm vi:** Luồng phát hiện `OPEN_CIRCUIT` → chốt FAIL/NG → mở hộp thoại `XỬ LÝ HÀNG KHÔNG ĐẠT`  
**Mục tiêu:** Không được báo HỞ MẠCH khi sản phẩm mới lắp được một đầu, đầu còn lại chưa kịp nối vào JIG.

---

# 0. HIỆN TƯỢNG THỰC TẾ

Hiện tại phần mềm đang bắt lỗi HỞ MẠCH quá sớm.

Tình huống thực tế:

```text
1. Công nhân bắt đầu lắp sản phẩm vào JIG.
2. Đầu thứ nhất vừa được nối.
3. Đầu thứ hai còn chưa kịp nối.
4. Phần mềm lập tức nhận OPEN_CIRCUIT.
5. Hộp thoại "XỬ LÝ HÀNG KHÔNG ĐẠT" xuất hiện.
6. Sản phẩm bị coi là FAIL/NG mặc dù quá trình lắp còn chưa hoàn thành.
```

Đây là **FAIL giả** và không đúng quy trình sản xuất.

Ảnh thực tế cho thấy popup:

```text
XỬ LÝ HÀNG KHÔNG ĐẠT
```

được mở quá sớm chỉ vì scan thấy HỞ MẠCH trong giai đoạn công nhân đang lắp sản phẩm.

---

# 1. ĐỌC AGENTS.md TRƯỚC KHI LÀM

Bắt buộc:

1. Tìm và đọc toàn bộ `AGENTS.md` có hiệu lực.
2. Kiểm tra `git status`.
3. Không sửa/xóa thay đổi hiện tại của người dùng nếu không liên quan.
4. Không tạo file backup `.bak`, `_old`, `_copy` trong source tree.
5. Không sửa mò bằng delay.
6. Phải trace chính xác luồng từ scan I/O đến popup NG trước khi thay đổi.

---

# 2. ĐÂY KHÔNG PHẢI LỖI "QUÁ NHẠY" ĐƠN THUẦN

Không được sửa bằng:

```text
Task.Delay(...)
Thread.Sleep(...)
tăng debounce
tăng confirm ms
đặt một delay mới
```

Vấn đề chính cần audit là:

> OPEN_CIRCUIT đang được phép trở thành FAIL trong lúc sản phẩm vẫn còn ở trạng thái ĐANG LẮP.

Phải sửa đúng **state gate / test arming / fail commit condition**.

---

# 3. AUDIT TOÀN BỘ LUỒNG OPEN_CIRCUIT

Codex phải search toàn repository theo các từ khóa tương đương:

```text
OPEN_CIRCUIT
OpenCircuit
OpenFault
HỞ MẠCH
HoMach
Fault
Fail
NG
ShowNg
ShowFault
Xử lý hàng không đạt
FaultDialog
NgDialog
ProcessNg
LatchFault
CommitFail
FinishFail
StopTest
ReadyToTest
Testing
CycleActive
ProductPresent
WaitingForInstall
```

Phải tìm chính xác flow:

```text
RX / SCAN I/O
    ↓
Build snapshot/frame
    ↓
Detect missing connection
    ↓
Create OPEN_CIRCUIT candidate
    ↓
???
    ↓
Commit FAIL/NG
    ↓
Show "XỬ LÝ HÀNG KHÔNG ĐẠT"
    ↓
Counter FAIL
    ↓
History
    ↓
Relay xử lý NG
```

Phải chỉ rõ `???` hiện tại là điều kiện gì.

---

# 4. TÌM CHÍNH XÁC TRIGGER MỞ HỘP THOẠI NG

Hộp thoại:

```text
XỬ LÝ HÀNG KHÔNG ĐẠT
```

KHÔNG được mở trực tiếp từ:

```text
Fault candidate
OPEN_CIRCUIT candidate
HasMissingConnection
FaultGrid có row
```

Phải tìm chính xác:

- class popup;
- method mở popup;
- event gọi popup;
- property/state khiến popup bật;
- popup được gọi từ fault detection hay từ committed cycle result.

Yêu cầu đúng:

```text
OPEN_CIRCUIT candidate
        ↓
kiểm tra cycle đã được ARM/READY chưa
        ↓
nếu chưa READY → REJECT / chỉ theo dõi, KHÔNG FAIL
        ↓
nếu đang TEST hợp lệ → mới được commit FAIL
        ↓
sau commit FAIL → mới mở popup NG
```

---

# 5. PHẢI CÓ GATE "READY TO TEST" / "TEST ARMED"

Điểm quan trọng nhất của task:

> Không được đánh giá HỞ MẠCH thành lỗi Production trước khi cycle thực sự được phép kiểm tra.

State machine phải phân biệt tối thiểu:

```text
WAITING_FOR_PRODUCT
INSTALLING
READY_TO_TEST / ARMED
TESTING
RESULT_COMMITTED
WAITING_FOR_REMOVAL
```

Tên state thực tế có thể khác.

Codex phải tìm state/flag tương đương đang tồn tại.

---

# 6. KHÔNG ĐƯỢC TỰ ĐOÁN "LẮP ĐỦ" BẰNG CÁCH YÊU CẦU TẤT CẢ KẾT NỐI ĐÚNG

Cực kỳ quan trọng:

Không được dùng logic:

```text
tất cả connection expected đã xuất hiện
→ coi là Ready To Test
```

vì sản phẩm **hở thật** sẽ không bao giờ đạt điều kiện Ready và do đó không bao giờ FAIL.

Phải tìm một điều kiện độc lập để xác định sản phẩm đã được lắp xong, ví dụ điều kiện start/presence/JIG/state hiện có trong project.

Có thể là:

- tín hiệu JIG;
- test pointer;
- product-present input;
- nút/trigger bắt đầu test;
- state machine hiện có;
- transition đã có trong code;
- điều kiện phần cứng riêng.

**Không được tự phát minh tín hiệu phần cứng không tồn tại.**

Nếu source hiện tại không có điều kiện đáng tin cậy để biết "đã lắp xong", Codex phải:

1. DỪNG việc tự sửa state machine.
2. Báo cáo rõ hiện tại không có independent ready gate.
3. Chỉ ra các lựa chọn kỹ thuật khả thi để người dùng quyết định.

---

# 7. GIAI ĐOẠN INSTALLING TUYỆT ĐỐI KHÔNG COMMIT HỞ MẠCH

Trong lúc:

```text
đầu A đã nối
đầu B chưa nối
```

hoặc bất kỳ trạng thái lắp dở nào:

Cho phép nội bộ scan thấy connection chưa đủ nếu cần.

Nhưng tuyệt đối:

- không commit `OPEN_CIRCUIT`;
- không set result FAIL;
- không tăng NG;
- không tăng FAIL counter;
- không lưu history FAIL;
- không mở popup NG;
- không kích relay NG;
- không advance LOT do FAIL;
- không latch lỗi sang cycle;
- không dừng test như một sản phẩm NG.

---

# 8. BỎ SẠCH CÁC CƠ CHẾ XÁC NHẬN HỞ MẠCH CŨ CÒN SÓT

Trước đó phần `Xác nhận hở mạch (ms)` đã được yêu cầu loại bỏ.

Codex phải kiểm tra xem runtime còn sót bất kỳ thứ gì tương đương:

```text
OpenCircuitConfirmMs
OpenConfirmMs
OpenFaultSince
OpenFaultStart
OpenConfirmTimer
PendingOpen
PendingOpenCircuit
OpenDebounce
ConfirmOpenCircuit()
```

Nếu còn:

- không được dùng để quyết định FAIL;
- loại bỏ dependency runtime nếu không còn cần;
- legacy config property chỉ được giữ để load config cũ nếu cần.

Không được tái tạo lại timer xác nhận HỞ MẠCH dưới một tên khác.

---

# 9. PHÂN BIỆT "FAULT CANDIDATE" VÀ "PRODUCTION FAIL"

Cần tách rõ:

```text
Fault candidate:
scan hiện tại thấy một connection expected đang thiếu.
```

với:

```text
Production FAIL:
cycle hợp lệ đang TEST + frame hợp lệ + OPEN_CIRCUIT thuộc cycle hiện tại + result chưa commit.
```

Không được viết kiểu:

```csharp
if (openFaults.Count > 0)
{
    ShowNgDialog();
    CommitFail();
}
```

nếu chưa có gate trạng thái đầy đủ.

---

# 10. ĐIỀU KIỆN TỐI THIỂU TRƯỚC KHI COMMIT OPEN_CIRCUIT FAIL

Codex phải xác định điều kiện thực tế của project, nhưng tối thiểu phải audit các yếu tố:

```text
Mode == Production
ModelLoaded == true
CycleActive == true
ReadyToTest/TestArmed == true
State == Testing
FrameValid == true
Frame belongs to current cycle/generation
ResultCommitted == false
Product is not in installing/removal transition
OPEN_CIRCUIT rule thật sự match model
```

Nếu thiếu một gate quan trọng, phải bổ sung đúng chỗ.

---

# 11. FRAME I/O KHÔNG HỢP LỆ KHÔNG ĐƯỢC CHỐT FAIL

Audit các log/trạng thái kiểu:

```text
không có I/O active
frame production hoàn chỉnh
mất đồng bộ
bỏ byte
partial frame
stale frame
generation
```

Không được dùng:

- empty frame;
- malformed frame;
- stale frame;
- frame của cycle trước;
- frame trong lúc transition tháo/lắp;

để commit OPEN_CIRCUIT FAIL.

---

# 12. POPUP NG CHỈ ĐƯỢC MỞ SAU KHI RESULT ĐÃ COMMIT

Yêu cầu flow:

```text
VALID TESTING STATE
        ↓
VALID FRAME
        ↓
REAL OPEN_CIRCUIT
        ↓
COMMIT RESULT = FAIL đúng 1 lần
        ↓
SHOW "XỬ LÝ HÀNG KHÔNG ĐẠT"
```

Không được:

```text
OPEN candidate
→ popup ngay
```

---

# 13. ONE CYCLE – ONE RESULT

Mỗi sản phẩm/cycle:

```text
PASS hoặc FAIL
```

chỉ được commit đúng **1 lần**.

Phải audit guard tương đương:

```text
resultCommitted
cycleCompleted
failureCommitted
```

Nếu popup đang xuất hiện liên tục do cùng fault được xử lý qua nhiều scan:

- phải chặn re-entry;
- không mở nhiều popup;
- không tăng FAIL nhiều lần;
- không pulse relay nhiều lần ngoài thiết kế.

---

# 14. RESET SAU KHI THÁO SẢN PHẨM

Khi sản phẩm lỗi đã được xử lý/tháo ra:

Phải reset đúng:

```text
open candidate
pending open
fault latch
cycle armed
result committed
popup guard
frame/generation state cũ
```

Sản phẩm mới phải bắt đầu cycle sạch.

Không carry-over OPEN_CIRCUIT của sản phẩm trước.

---

# 15. LOG TRACE TẠM THỜI ĐỂ TÌM ROOT CAUSE

Bổ sung log có kiểm soát tại đúng các transition.

Ví dụ:

```text
[OPEN-CANDIDATE]
CycleId=...
State=...
Armed=...
FrameValid=...
IOPair=IO2<->IO8
```

Nếu chưa được phép FAIL:

```text
[OPEN-REJECT]
Reason=INSTALLING
CycleId=...
```

Nếu được commit:

```text
[OPEN-COMMIT]
CycleId=...
State=TESTING
Armed=true
FrameValid=true
Pair=...
ResultCommitted=false
```

Popup:

```text
[NG-DIALOG]
CycleId=...
Reason=CommittedOpenCircuit
```

Mục tiêu là nhìn log và biết chính xác **vì sao popup được phép xuất hiện**.

---

# 16. TEST THỰC TẾ BẮT BUỘC

## Test 1 — JIG trống

Expected:

```text
không OPEN FAIL
không popup NG
không tăng FAIL
```

---

## Test 2 — Mới nối đầu thứ nhất

Thực hiện:

```text
nối đầu A
giữ đầu B chưa nối trong vài giây
```

Expected:

```text
KHÔNG popup "XỬ LÝ HÀNG KHÔNG ĐẠT"
KHÔNG commit OPEN_CIRCUIT
KHÔNG tăng NG
KHÔNG history FAIL
KHÔNG relay NG
```

Đây là test quan trọng nhất.

---

## Test 3 — Sau đó nối đầu thứ hai, hàng tốt

Expected:

```text
cycle bắt đầu hợp lệ
test bình thường
PASS
```

---

## Test 4 — Lắp đầy đủ ngay từ đầu, hàng tốt

Expected:

```text
PASS
```

---

## Test 5 — Lắp đầy đủ nhưng hàng HỞ thật

Expected:

```text
sau khi cycle đã Armed/Testing
→ OPEN_CIRCUIT thật
→ FAIL đúng 1 lần
→ popup NG đúng 1 lần
```

---

## Test 6 — Giữ trạng thái lắp dở lâu

Không được phụ thuộc thời gian.

Dù giữ một đầu chưa nối:

```text
1 giây
5 giây
10 giây
```

Expected vẫn là:

```text
INSTALLING
không FAIL
```

Không được tạo một timer mới rồi sau N giây lại FAIL.

---

## Test 7 — Tháo và lắp sản phẩm mới

Expected:

- state sạch;
- không carry-over lỗi;
- không popup cũ;
- cycle mới hoạt động bình thường.

---

# 17. ACCEPTANCE CRITERIA

Task chỉ DONE khi:

- [ ] Đã đọc `AGENTS.md`.
- [ ] Đã trace chính xác nơi sinh `OPEN_CIRCUIT`.
- [ ] Đã trace chính xác nơi commit FAIL.
- [ ] Đã trace chính xác nơi mở popup `XỬ LÝ HÀNG KHÔNG ĐẠT`.
- [ ] Đã xác định vì sao popup hiện khi mới nối một đầu.
- [ ] Đã có gate Ready/Armed/Testing đúng.
- [ ] INSTALLING không commit HỞ MẠCH.
- [ ] Không dùng delay để che lỗi.
- [ ] Không còn timer xác nhận HỞ MẠCH legacy tham gia runtime.
- [ ] Frame invalid/stale/transition không commit FAIL.
- [ ] Một cycle chỉ commit result 1 lần.
- [ ] Popup NG chỉ mở sau committed FAIL.
- [ ] Một đầu nối, đầu còn lại chưa nối: không popup.
- [ ] Hàng tốt vẫn PASS.
- [ ] Hàng hở thật vẫn FAIL.
- [ ] Reset cycle sạch sau khi tháo hàng.
- [ ] Build thành công.
- [ ] Runtime không crash.
- [ ] Git diff chỉ chứa thay đổi liên quan.

---

# 18. BÁO CÁO BẮT BUỘC CỦA CODEX

## A. ROOT CAUSE

Trả lời chính xác:

```text
Tại sao chỉ mới nối một đầu mà OPEN_CIRCUIT đã được commit?
```

- File:
- Class:
- Method:
- Điều kiện sai:
- State lúc popup mở:
- Frame lúc popup mở:

## B. POPUP TRIGGER

- Method mở `XỬ LÝ HÀNG KHÔNG ĐẠT`:
- Ai gọi:
- Điều kiện cũ:
- Điều kiện mới:

## C. READY / ARMED GATE

- Điều kiện xác định đã lắp xong:
- Nguồn tín hiệu/state:
- Vì sao điều kiện này độc lập với lỗi HỞ thật:

## D. OPEN_CIRCUIT FLOW SAU KHI SỬA

```text
INSTALLING
→ open candidate nếu có
→ reject commit

READY/ARMED
→ TESTING
→ valid frame
→ real OPEN
→ commit FAIL
→ popup NG
```

## E. FILES CHANGED

| File | Thay đổi | Lý do |
|---|---|---|

## F. TEST

- JIG trống:
- Một đầu:
- Một đầu giữ lâu:
- Hai đầu hàng tốt:
- Hở thật:
- Reset cycle:

---

# 19. YÊU CẦU CUỐI

**Không được coi việc "scan thấy thiếu kết nối" là đủ điều kiện mở popup NG.**

Phải sửa đúng:

```text
READ AGENTS.md
→ TRACE OPEN_CIRCUIT
→ TRACE FAIL COMMIT
→ TRACE NG POPUP
→ FIND READY/ARMED GATE
→ BLOCK FAIL DURING INSTALLING
→ REMOVE LEGACY OPEN CONFIRM RUNTIME
→ VALIDATE FRAME/CYCLE
→ ONE CYCLE ONE RESULT
→ TEST TRÊN JIG
→ AUDIT GIT DIFF
→ BÁO CÁO ROOT CAUSE
```
