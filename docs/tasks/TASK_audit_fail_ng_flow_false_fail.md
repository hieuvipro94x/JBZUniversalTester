# TASK: AUDIT TOÀN BỘ LUỒNG FAIL / NG VÀ LOẠI BỎ FAIL GIẢ
## JBZ Universal Tester

**Mức độ:** CRITICAL / PRODUCTION BLOCKER  
**Phạm vi:** Audit và sửa điều kiện sinh FAIL / NG / lưu lịch sử lỗi / tăng counter lỗi / popup xử lý hàng không đạt  
**Không mở rộng:** Không tự ý sửa thuật toán phần cứng, relay, mapping I/O, `.model` nếu chưa chứng minh liên quan

---

# 0. BỐI CẢNH LỖI THỰC TẾ

Hiện tại máy đang xuất hiện trường hợp **FAIL giả**.

Ảnh runtime cho thấy:

```text
HỞ MẠCH
PHÁT HIỆN 1 LỖI
Kết nối tiêu chuẩn: 1 - Chân 2 ↔ 3 - Chân 2
Màu dây tiêu chuẩn: B
Thực tế: KHÔNG CÓ KẾT NỐI
```

DataGrid đồng thời hiển thị:

```text
HỞ MẠCH
I/O 2
I/O 8
Chưa kết nối: IO2 <-> IO8
```

và hệ thống đã chuyển sang popup:

```text
XỬ LÝ HÀNG KHÔNG ĐẠT
```

Trong thực tế đây có trường hợp bị đánh giá **NG/FAIL không đúng trạng thái thực của sản phẩm**.

Yêu cầu lần này KHÔNG phải sửa giao diện.

Yêu cầu là:

> RÀ SOÁT TOÀN BỘ LUỒNG TÍNH FAIL / NG, XÁC ĐỊNH CHÍNH XÁC ĐIỀU KIỆN NÀO ĐANG LÀM HỆ THỐNG CHỐT FAIL, VÀ LOẠI BỎ FAIL GIẢ.

---

# 1. QUY TRÌNH BẮT BUỘC TRƯỚC KHI SỬA

Trước khi thay đổi code:

1. Đọc toàn bộ `AGENTS.md` có hiệu lực.
2. Kiểm tra `git status`.
3. Không sửa/xóa thay đổi hiện có của người dùng nếu không liên quan.
4. Trace từ đầu đến cuối luồng:
   - scan I/O;
   - build snapshot/frame;
   - detect open circuit;
   - detect short circuit;
   - detect wrong connection;
   - determine test ready;
   - determine cycle active;
   - determine FAIL;
   - latch FAIL;
   - show fault popup;
   - save history;
   - increase FAIL/NG counters;
   - advance LOT;
   - relay action;
   - wait product removal;
   - reset next cycle.
5. Trước khi sửa phải báo cáo:
   - file;
   - class;
   - method;
   - state/flag liên quan;
   - điều kiện FAIL hiện tại;
   - nghi vấn root cause;
   - phạm vi thay đổi dự kiến.

**Không được sửa mò trước khi trace được luồng FAIL từ đầu đến cuối.**

---

# 2. MỤC TIÊU AUDIT

Codex phải trả lời bằng code evidence:

## 2.1 Khi nào hệ thống coi một cycle là "đang test"?

Phải tìm chính xác điều kiện tương đương:

```text
CycleActive == true
Testing == true
ReadyToTest == true
ProductPresent == true
```

hoặc tên thực tế.

Phải làm rõ:

- ai set trạng thái này;
- set ở method nào;
- reset khi nào;
- có bị set quá sớm khi mới lắp một phần sản phẩm hay không.

---

## 2.2 Khi nào một lỗi được coi là "candidate fault"?

Phải xác định:

```text
OPEN_CIRCUIT candidate
SHORT_CIRCUIT candidate
WRONG_CONNECTION candidate
```

được sinh từ đâu.

Không được đánh đồng:

```text
scan thấy trạng thái khác expected
```

với:

```text
đủ điều kiện chốt FAIL
```

---

## 2.3 Khi nào candidate fault biến thành FAIL thật?

Phải tìm chính xác code kiểu:

```csharp
if (...)
{
    Fail();
}
```

hoặc:

```text
SetFault
ConfirmFault
LatchFault
StopTest
SetNg
FinishCycle(false)
CompleteFail
```

Phải ghi rõ mọi điều kiện boolean dẫn tới FAIL.

---

# 3. AUDIT TẤT CẢ ĐIỀU KIỆN FAIL / NG

Phải lập bảng đầy đủ:

| Loại | Điều kiện phát hiện | Điều kiện chốt FAIL | Có latch? | Có tăng NG? | Có lưu history? |
|------|---------------------|---------------------|-----------|-------------|-----------------|
| Hở mạch | ... | ... | ... | ... | ... |
| Chập mạch | ... | ... | ... | ... | ... |
| Sai kết nối | ... | ... | ... | ... | ... |
| Điện trở | ... | ... | ... | ... | ... |
| Probe pin | ... | ... | ... | ... | ... |
| Relay/JIG nếu có | ... | ... | ... | ... | ... |
| Lỗi khác | ... | ... | ... | ... | ... |

Không được bỏ sót bất kỳ đường code nào có thể làm:

```text
FAIL++
NG++
SaveHistory(FAIL)
ShowNgDialog()
AdvanceLot()
```

---

# 4. PHẢI PHÂN BIỆT 4 TRẠNG THÁI

State machine tối thiểu phải phân biệt rõ:

```text
1. JIG TRỐNG
2. ĐANG LẮP SẢN PHẨM
3. READY TO TEST / TESTING
4. TEST COMPLETE
```

Không được để trạng thái:

```text
đang lắp sản phẩm
```

bị xử lý như:

```text
test đang chạy hợp lệ
```

Đây là điểm cần audit đặc biệt.

---

# 5. ĐIỀU KIỆN FAIL HỢP LỆ

Một lỗi chỉ được phép trở thành **FAIL / NG Production** khi đồng thời thỏa đủ các điều kiện logic cần thiết.

Ít nhất phải kiểm tra các nhóm điều kiện:

```text
A. Đúng mode Production
B. Có model hợp lệ
C. Cycle hiện tại đã được bắt đầu hợp lệ
D. Sản phẩm ở trạng thái Ready To Test / Testing
E. Snapshot/frame I/O hiện tại hợp lệ
F. Không phải frame rỗng / mất đồng bộ / transient
G. Fault thuộc cycle hiện tại
H. Fault chưa bị reset bởi trạng thái tháo/lắp
I. Fault đúng theo rule của model
J. Chưa hoàn tất cycle trước
```

Codex phải xác định điều kiện thực tế nào trong source đang thiếu hoặc sai.

---

# 6. AUDIT FRAME / SNAPSHOT I/O

Trong log trước đó đã xuất hiện các trạng thái kiểu:

```text
RX frame: không có I/O active
frame production hoàn chỉnh
bỏ 1 byte mất đồng bộ
```

Phải audit:

- frame nào được coi là valid;
- frame nào được phép dùng để quyết định FAIL;
- frame rỗng có được dùng như bằng chứng HỞ MẠCH không;
- frame vừa mất đồng bộ có bị đưa vào matcher không;
- frame partial có bị coi là production-complete không;
- stale frame có bị tái sử dụng;
- generation/cycle id có khớp không.

Đặc biệt:

> Không được dùng frame lỗi, frame rỗng, frame mất đồng bộ hoặc frame chưa ổn định để chốt FAIL Production.

Nếu có cơ chế generation/cycle token thì phải đảm bảo fault thuộc đúng generation hiện tại.

---

# 7. AUDIT OPEN CIRCUIT

Phải trace:

```text
expected connection
→ scan result
→ missing connection
→ open candidate
→ open confirmed
→ fail
```

Kiểm tra:

- hai đầu expected có thực sự thuộc cùng connection không;
- mapping IO có bị duplicate không;
- một endpoint có đang tạm chưa present vì đang lắp không;
- current snapshot có đủ source không;
- open được tính theo pair hay theo từng endpoint;
- cùng một lỗi có bị tạo thành 2 row như IO2 và IO8 hay không;
- popup nói `PHÁT HIỆN 1 LỖI` nhưng grid có 2 row có phải là biểu diễn 2 endpoint của 1 lỗi hay là duplicate bug.

Nếu một connection lỗi tạo 2 record nhưng chỉ là 1 lỗi vật lý, phải làm rõ:
- UI representation;
- counter logic;
- fail count logic;
- history logic.

Không được để duplicate row làm tăng số lỗi/NG nhiều lần.

---

# 8. AUDIT COUNTER FAIL / NG

Phải xác định rõ:

```text
TỔNG
ĐẠT
LỖI
HÔM NAY
THÁNG NÀY
LIFETIME
```

được tăng ở đâu.

Kiểm tra nguy cơ:

```text
Fail() được gọi nhiều lần cho cùng cycle
```

hoặc:

```text
mỗi fault row làm FAIL++
```

thay vì:

```text
mỗi sản phẩm/cycle chỉ FAIL đúng 1 lần
```

Yêu cầu:

> Một sản phẩm NG chỉ được tăng counter FAIL/NG đúng 1 lần cho mỗi cycle.

Dù có 1 hay nhiều lỗi chi tiết trong sản phẩm.

Phải có guard tương đương:

```text
if (!cycleResultCommitted)
```

hoặc cơ chế thực tế tương đương.

---

# 9. AUDIT HISTORY

Phải tìm code lưu:

```text
test-history.db
```

hoặc storage thực tế.

Kiểm tra:

- history có lưu ngay khi mới thấy candidate fault không;
- hay chỉ lưu sau khi cycle thực sự được commit FAIL;
- cùng cycle có bị ghi nhiều record;
- LOT có advance trước khi xác nhận cycle kết thúc không.

Yêu cầu:

> Chỉ ghi history FAIL khi kết quả FAIL của cycle đã được commit hợp lệ.

---

# 10. AUDIT POPUP "XỬ LÝ HÀNG KHÔNG ĐẠT"

Popup này chỉ được mở khi:

```text
cycle FAIL đã được xác nhận hợp lệ
```

Không được mở chỉ vì:

```text
fault candidate xuất hiện trong 1 scan/frame
```

Phải tìm chính xác trigger mở popup.

Kiểm tra:

- popup mở từ event fault;
- hay event cycle complete;
- hay property `HasFault`.

Nếu mở quá sớm phải sửa để popup bám theo **committed FAIL**, không bám theo transient fault.

---

# 11. AUDIT RELAY KHI FAIL

Phải xác định:

- relay nào được kích khi FAIL;
- trigger nằm ở đâu;
- có bị kích bởi candidate fault không;
- có bị kích nhiều lần nếu nhiều row fault.

Yêu cầu:

> Relay xử lý NG chỉ được chạy theo đúng flow FAIL đã commit.

Không được kích do transient/open giả.

---

# 12. NGUYÊN TẮC "ONE CYCLE – ONE RESULT"

Mỗi cycle chỉ được có một kết quả cuối:

```text
PASS
hoặc
FAIL
```

Không được:

```text
FAIL nhiều lần
PASS rồi FAIL
FAIL rồi PASS
history ghi lặp
counter tăng lặp
LOT advance lặp
relay pulse lặp ngoài thiết kế
```

Codex phải audit biến hoặc cơ chế đảm bảo idempotency của result commit.

Nếu chưa có, cần bổ sung guard tối thiểu và an toàn.

---

# 13. KHÔNG ĐƯỢC CHE LỖI BẰNG DELAY

Không được sửa FAIL giả bằng cách:

```text
tăng delay
Thread.Sleep
Task.Delay
debounce rất dài
```

Mục tiêu là sửa **đúng điều kiện state / frame / cycle / commit FAIL**.

Delay không phải root fix nếu logic đang đánh giá sai trạng thái.

---

# 14. TRACE LOG BẮT BUỘC

Bổ sung log đủ để audit một cycle, nhưng tránh spam vô hạn.

Mỗi lần chuẩn bị commit FAIL cần có log dạng tương đương:

```text
[FAIL-AUDIT]
CycleId=...
Generation=...
Mode=Production
State=Testing
ReadyToTest=true
FrameValid=true
FrameId=...
FaultType=OPEN_CIRCUIT
FaultCount=...
ResultCommitted=false
Reason=...
```

Ngay sau commit:

```text
[FAIL-COMMIT]
CycleId=...
Result=FAIL
FaultType=...
CounterIncremented=true
HistorySaved=true
NgDialogShown=true
```

Nếu reject candidate:

```text
[FAULT-REJECT]
Reason=NotReadyToTest / InvalidFrame / StaleCycle / Installing / ...
```

Không log dữ liệu nhạy cảm không cần thiết.

---

# 15. TEST CASE BẮT BUỘC

## Test 1 — Jig trống

Expected:
- không FAIL;
- không tăng NG;
- không popup NG;
- không history FAIL.

## Test 2 — Đang lắp sản phẩm

Lắp chưa hoàn chỉnh.

Expected:
- có thể scan thấy missing I/O;
- nhưng không commit FAIL.

## Test 3 — Hàng tốt

Lắp đầy đủ hàng tốt.

Expected:
- PASS đúng 1 lần;
- PASS counter +1;
- FAIL không tăng.

## Test 4 — Hở thật

Lắp đầy đủ mẫu hở thật.

Expected:
- FAIL đúng 1 lần;
- popup NG đúng 1 lần;
- history đúng 1 record;
- FAIL counter +1.

## Test 5 — Nhiều lỗi trên một sản phẩm

Expected:
- hiển thị nhiều lỗi chi tiết nếu cần;
- nhưng sản phẩm chỉ FAIL +1 cycle.

## Test 6 — Frame rỗng / mất đồng bộ

Expected:
- không dùng frame đó để commit FAIL;
- phải reject hoặc chờ frame hợp lệ tiếp theo.

## Test 7 — Tháo sản phẩm

Expected:
- cycle reset đúng;
- pending fault cũ không kéo sang cycle mới.

## Test 8 — Cycle kế tiếp

Expected:
- không carry-over fault/result/history/counter từ cycle trước.

---

# 16. ACCEPTANCE CRITERIA

Task chỉ DONE khi:

- [ ] Đã đọc `AGENTS.md`.
- [ ] Đã trace đầy đủ từ scan I/O đến FAIL commit.
- [ ] Đã liệt kê chính xác mọi điều kiện FAIL/NG hiện tại.
- [ ] Đã xác định root cause của FAIL giả.
- [ ] Đã phân biệt candidate fault và committed FAIL.
- [ ] Đang lắp sản phẩm không commit FAIL.
- [ ] Frame invalid/rỗng/mất đồng bộ không commit FAIL.
- [ ] Một cycle chỉ commit result đúng 1 lần.
- [ ] Một sản phẩm NG chỉ tăng FAIL counter đúng 1.
- [ ] History FAIL chỉ lưu đúng 1 record/cycle.
- [ ] Popup NG chỉ mở sau committed FAIL.
- [ ] Relay NG không bị kích bởi transient fault.
- [ ] Hàng tốt vẫn PASS.
- [ ] Hàng lỗi thật vẫn FAIL.
- [ ] Không dùng delay để che root cause.
- [ ] Không phát sinh regression logic khác.
- [ ] Build thành công.
- [ ] Runtime không crash.
- [ ] Git diff chỉ chứa thay đổi liên quan.

---

# 17. BÁO CÁO SAU KHI HOÀN THÀNH

Codex phải trả báo cáo theo format:

## A. FAIL / NG FLOW HIỆN TẠI

```text
Scan
→ ...
→ Fault candidate
→ ...
→ FAIL condition
→ ...
→ Counter
→ History
→ Popup
→ Relay
→ Reset
```

## B. ĐIỀU KIỆN FAIL

Liệt kê chính xác boolean/state condition.

## C. ROOT CAUSE FAIL GIẢ

- File:
- Class:
- Method:
- State:
- Frame:
- Điều kiện sai:
- Vì sao gây FAIL giả:

## D. SỬA ĐỔI

- State gate:
- Frame validation:
- Cycle guard:
- Result commit guard:
- Counter guard:
- History guard:
- Popup guard:
- Relay guard:

## E. FILES CHANGED

| File | Thay đổi | Lý do |
|------|----------|-------|

## F. TEST

- Jig trống:
- Đang lắp:
- Hàng tốt:
- Hở thật:
- Nhiều lỗi:
- Frame invalid:
- Cycle mới:

## G. KẾT LUẬN

Trả lời rõ:

```text
Điều kiện để một sản phẩm bị tính FAIL/NG hiện nay là gì?
Sau khi sửa, điều kiện mới là gì?
Điểm nào trước đây gây FAIL giả?
```

---

# 18. YÊU CẦU CUỐI

Không được chỉ sửa symptom.

Phải thực hiện:

```text
READ AGENTS.md
→ TRACE TOÀN BỘ FAIL/NG FLOW
→ XÁC ĐỊNH CANDIDATE FAULT
→ XÁC ĐỊNH COMMIT FAIL
→ AUDIT STATE
→ AUDIT FRAME
→ AUDIT COUNTER
→ AUDIT HISTORY
→ AUDIT POPUP
→ AUDIT RELAY
→ FIX ROOT CAUSE
→ TEST THỰC TẾ
→ AUDIT GIT DIFF
→ BÁO CÁO
```
