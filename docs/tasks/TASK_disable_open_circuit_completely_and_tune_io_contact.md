# TASK: VÔ HIỆU HÓA HOÀN TOÀN LỖI HỞ MẠCH + GIẢM ĐỘ NHẠY TIẾP XÚC I/O
## JBZ Universal Tester

**Mức độ:** CRITICAL / PRODUCTION BLOCKER  
**Phạm vi:** Logic test Production  
**Mục tiêu cuối:** `OPEN_CIRCUIT / HỞ MẠCH` không bao giờ được coi là lỗi, không bao giờ làm FAIL/NG, không khóa test, không mở popup. Hệ thống chỉ bắt các lỗi sản xuất còn được yêu cầu: **SAI DÂY/ĐẤU NHẦM, CHẬP MẠCH, ĐIỆN TRỞ KHÔNG ĐẠT**.

---

# 0. YÊU CẦU NGHIỆP VỤ CUỐI CÙNG

Từ phiên bản sửa này trở đi:

```text
THIẾU KẾT NỐI / HỞ MẠCH
= KHÔNG PHẢI LỖI
= KHÔNG FAIL
= KHÔNG NG
= KHÔNG POPUP
= KHÔNG KHÓA TEST
= KHÔNG TĂNG COUNTER LỖI
= KHÔNG GHI HISTORY FAIL
= KHÔNG KÍCH RELAY NG
```

Kể cả khi:

```text
mới chạm/cắm 1 đầu dây
đầu còn lại chưa nối
```

thì phần mềm **không được hiện HỞ MẠCH và không được đưa sản phẩm sang trạng thái hàng không đạt**.

---

# 1. ĐỌC AGENTS.md TRƯỚC KHI SỬA

Bắt buộc:

1. Tìm và đọc toàn bộ `AGENTS.md` có hiệu lực.
2. Kiểm tra `git status`.
3. Không xóa/sửa thay đổi hiện tại của người dùng nếu không liên quan.
4. Không tạo `.bak`, `_old`, `_copy`, `.tmp` trong source tree.
5. Trace toàn bộ reference của:
   - `OPEN_CIRCUIT`
   - `OpenCircuit`
   - `HỞ MẠCH`
   - `MissingConnection`
   - `Unconnected`
   - `OpenFault`
   - `PendingOpen`
   - popup NG
   - FAIL/NG commit
   - FaultGrid
   - history
   - counters
   - relay FAIL.
6. Không được chỉ sửa UI rồi để runtime vẫn sinh HỞ MẠCH ở phía dưới.

---

# 2. XÓA HỞ MẠCH KHỎI TOÀN BỘ LUỒNG FAULT

Codex phải audit và vô hiệu hóa `OPEN_CIRCUIT` ở tất cả các tầng.

## 2.1 Detection

Nếu code có logic:

```text
expected connection không tồn tại
→ tạo OpenCircuit fault
```

thì không được đưa kết quả này vào danh sách lỗi Production nữa.

Có thể vẫn sử dụng thông tin "thiếu kết nối" nội bộ nếu thuật toán khác cần nó, nhưng:

```text
không tạo FaultRecord OPEN_CIRCUIT
không add vào CurrentFaults
không add vào FaultGrid
không set HasFault
không set IsNg
```

---

## 2.2 FAIL / NG

Không được tồn tại bất kỳ đường code nào kiểu:

```csharp
if (openCircuitDetected)
    CommitFail();
```

hoặc tương đương.

`OPEN_CIRCUIT` phải bị loại khỏi:

- `ShouldFail`
- `HasCriticalFault`
- `IsNg`
- `CommitFail`
- `StopTest`
- `FinishCycle(false)`
- bất kỳ aggregate fault result nào.

---

## 2.3 Popup "XỬ LÝ HÀNG KHÔNG ĐẠT"

HỞ MẠCH tuyệt đối không được trigger:

```text
XỬ LÝ HÀNG KHÔNG ĐẠT
```

Không được mở popup chỉ vì:

```text
thiếu connection
mới chạm 1 đầu
một endpoint chưa active
expected pair chưa hoàn chỉnh
```

Popup NG chỉ được mở bởi các lỗi còn được cho phép:

```text
SAI DÂY / ĐẤU NHẦM
CHẬP MẠCH
ĐIỆN TRỞ KHÔNG ĐẠT
```

và các lỗi khác chỉ khi đã có yêu cầu nghiệp vụ rõ ràng.

---

# 3. FAULTGRID KHÔNG ĐƯỢC HIỂN THỊ HỞ MẠCH

Không được có row:

```text
HỞ MẠCH
Chưa kết nối: IOx <-> IOy
```

trong `FaultGrid`.

Không được hiện banner:

```text
HỞ MẠCH
Dây ... chưa kết nối
```

Không được chuyển nền/banner sang màu lỗi chỉ vì thiếu kết nối.

Nếu connection chưa hoàn thành:

- giữ trạng thái chờ/đang thao tác;
- không hiện lỗi;
- không che luồng test.

---

# 4. KHÔNG KHÓA TEST KHI THIẾU KẾT NỐI

Đây là yêu cầu bắt buộc.

Trường hợp:

```text
đầu A đã nối
đầu B chưa nối
```

phần mềm phải tiếp tục hoạt động bình thường.

Không được:

- stop scan;
- stop cycle;
- lock test;
- latch NG;
- chờ người vận hành bấm XÁC NHẬN;
- chuyển sang waiting-for-removal chỉ vì HỞ MẠCH;
- tăng LOT do FAIL;
- đóng băng state machine.

Thiếu kết nối chỉ là trạng thái chưa hoàn chỉnh, không phải kết quả lỗi.

---

# 5. CHỈ GIỮ 3 NHÓM LỖI CHÍNH

Sau khi sửa, Production phải tập trung bắt:

## A. SAI DÂY / ĐẤU NHẦM

Chỉ báo lỗi khi **thực sự quan sát được một kết nối sai**.

Ví dụ:

```text
expected: IO1 <-> IO7
actual:   IO1 <-> IO9
```

thì đây là `WRONG_CONNECTION`.

Không được suy luận:

```text
expected IO1 <-> IO7 chưa thấy
```

thành `WRONG_CONNECTION`.

**Missing expected connection không phải sai dây.**

---

## B. CHẬP MẠCH

Chỉ báo lỗi khi có bằng chứng dương tính về kết nối/chập không hợp lệ.

Ví dụ:

```text
nhiều endpoint bị nối chung ngoài rule model
```

hoặc điều kiện short thực tế của project.

Không được suy luận chập chỉ từ việc một connection expected đang thiếu.

---

## C. ĐIỆN TRỞ KHÔNG ĐẠT

Giữ nguyên kiểm tra:

```text
Min Ω
Max Ω
actual resistance
```

Nếu ngoài giới hạn thì FAIL theo logic hiện hành.

Không được ảnh hưởng thuật toán đo điện trở.

---

# 6. GIẢM ĐỘ NHẠY TIẾP XÚC I/O

Hiện tượng thực tế:

> Chỉ vừa chạm một đầu I/O đã có phản ứng cảnh báo ngay.

Yêu cầu giảm độ nhạy của **tiếp xúc I/O dương tính** để một lần chạm thoáng qua không lập tức trở thành bằng chứng lỗi.

Codex phải audit:

```text
raw IO frame
input edge
contact detection
debounce
stable frame
scan cadence
JIG contact
```

## Nguyên tắc mới

Không dùng một sample đơn lẻ để commit các lỗi dương tính như:

```text
WRONG_CONNECTION
SHORT_CIRCUIT
```

Phải có cơ chế xác nhận tiếp xúc ổn định nội bộ.

Ưu tiên:

```text
N consecutive valid frames
```

hoặc:

```text
stable contact window nội bộ
```

thay vì expose thêm setting cho operator.

### Yêu cầu triển khai

- Không thêm lại setting timing vào màn hình Settings.
- Không dùng `Thread.Sleep`.
- Không block UI thread.
- Không tạo delay cho HỞ MẠCH vì HỞ MẠCH đã bị vô hiệu hóa hoàn toàn.
- Debounce chỉ nhằm loại bỏ **contact transient** khi phát hiện kết nối dương tính.

Nếu project đã có debounce/stability helper:
- tái sử dụng helper hiện tại;
- điều chỉnh threshold để tiếp xúc thoáng qua không đủ điều kiện commit fault.

Nếu project chưa có:
- bổ sung cơ chế nhỏ, độc lập, non-blocking;
- chỉ áp dụng cho positive contact evidence.

Không được chọn giá trị tùy tiện mà không xem scan cadence thực tế.

Codex phải báo cáo:

```text
scan period thực tế
số frame ổn định yêu cầu
thời gian hiệu dụng
```

---

# 7. QUAN TRỌNG: "MISSING" KHÔNG BAO GIỜ ĐƯỢC LATCH

Từ phiên bản này:

```text
expected pair chưa xuất hiện
```

không được dùng làm bằng chứng để:

- FAIL;
- NG;
- popup;
- history;
- counter;
- relay;
- lock test.

Không cần timer xác nhận missing.

Không cần debounce missing.

Không cần OPEN_CIRCUIT candidate cho Production result.

---

# 8. AUDIT ENUM / MODEL FAULT TYPE

Nếu có enum:

```csharp
FaultType.OpenCircuit
```

không bắt buộc phải xóa enum nếu còn cần để đọc history/config cũ.

Nhưng runtime Production mới phải đảm bảo:

```text
OpenCircuit không được tạo mới
OpenCircuit không được commit
OpenCircuit không được hiển thị
```

Nếu giữ enum chỉ để backward compatibility thì comment rõ:

```text
Legacy/history compatibility only.
Not generated by current Production test flow.
```

---

# 9. HISTORY / COUNTER

Không được ghi mới:

```text
FAIL - HỞ MẠCH
OPEN_CIRCUIT
```

vào history.

Không được tăng:

```text
LỖI
FAIL
NG
HÔM NAY
THÁNG NÀY
LIFETIME FAIL
```

do missing/open.

History cũ có `OPEN_CIRCUIT` vẫn phải đọc được.

Không phá database schema.

---

# 10. RELAY

Không được kích relay xử lý NG do:

```text
OPEN_CIRCUIT
missing connection
partial installation
single-end touch
```

Relay FAIL/NG chỉ chạy sau committed fault hợp lệ của các loại còn được phép.

---

# 11. ONE CYCLE – ONE RESULT

Giữ guard:

```text
mỗi cycle chỉ commit PASS hoặc FAIL đúng 1 lần
```

Không để contact bounce tạo:

- nhiều popup;
- nhiều FAIL;
- nhiều history record;
- nhiều relay pulse;
- nhiều counter increment.

---

# 12. TEST CASE BẮT BUỘC

## Test 1 — JIG trống

Expected:

```text
không HỞ MẠCH
không popup NG
không FAIL
```

---

## Test 2 — Chạm một đầu

Chỉ chạm/cắm một đầu dây vào JIG.

Expected:

```text
không cảnh báo
không HỞ MẠCH
không popup
không khóa test
không NG
```

---

## Test 3 — Giữ một đầu lâu

Giữ một đầu trong:

```text
1s
5s
10s
```

Expected vẫn:

```text
không HỞ MẠCH
không FAIL
```

Không được có timer nào cuối cùng biến nó thành HỞ MẠCH.

---

## Test 4 — Đấu đúng

Nối đúng đầy đủ.

Expected:

```text
không lỗi
test tiếp tục bình thường
```

---

## Test 5 — Bỏ hẳn một dây expected

Cố ý không nối một connection expected.

Expected:

```text
KHÔNG bắt HỞ MẠCH
KHÔNG FAIL vì thiếu kết nối
KHÔNG popup
```

Đây là yêu cầu nghiệp vụ mới.

---

## Test 6 — Đấu nhầm thật

Ví dụ kết nối một đầu sang I/O sai.

Expected:

```text
SAI DÂY / ĐẤU NHẦM
→ FAIL đúng
→ popup đúng 1 lần
```

Nhưng chỉ sau khi actual wrong connection đã ổn định đủ điều kiện debounce.

---

## Test 7 — Chập thật

Tạo short thật theo fixture/model.

Expected:

```text
CHẬP MẠCH
→ FAIL đúng
```

---

## Test 8 — Chạm thoáng qua sai I/O

Chạm thoáng qua rồi bỏ ngay.

Expected:

```text
không commit WRONG_CONNECTION
không commit SHORT
```

nếu chưa đủ điều kiện stable-contact.

---

## Test 9 — Điện trở NG

Expected:

```text
R < Min hoặc R > Max
→ FAIL
```

Giữ nguyên.

---

# 13. SEARCH BẮT BUỘC

Codex phải search toàn repo ít nhất:

```text
OPEN_CIRCUIT
OpenCircuit
OpenFault
MissingConnection
Unconnected
HỞ MẠCH
Chưa kết nối
ConfirmOpen
PendingOpen
FaultType.Open
CommitFail
ShowNg
ShowFault
FaultGrid
History
FailCount
NgCount
Relay
Debounce
Stable
Contact
IO scan
```

Sau đó lập bảng reference:

| Symbol/Method | File | Vai trò hiện tại | Hành động |
|---|---|---|---|
| ... | ... | tạo open fault | remove/disable |
| ... | ... | render FaultGrid | remove open branch |
| ... | ... | commit fail | remove open input |
| ... | ... | history | block new open writes |
| ... | ... | debounce IO | adjust |
| ... | ... | relay | guard |

---

# 14. ACCEPTANCE CRITERIA

Task chỉ DONE khi:

- [ ] Đã đọc `AGENTS.md`.
- [ ] Không còn `OPEN_CIRCUIT` được tạo mới trong Production.
- [ ] Không còn HỞ MẠCH trong `FaultGrid`.
- [ ] Không còn banner HỞ MẠCH.
- [ ] Không còn popup NG do missing connection.
- [ ] Không còn FAIL/NG do missing connection.
- [ ] Không khóa test do thiếu kết nối.
- [ ] Không tăng counter lỗi do thiếu kết nối.
- [ ] Không ghi history FAIL do thiếu kết nối.
- [ ] Không kích relay NG do thiếu kết nối.
- [ ] Một đầu chưa nối đầu còn lại: hoàn toàn không lỗi.
- [ ] Bỏ hẳn một connection expected: không bị OPEN FAIL.
- [ ] Sai dây thật vẫn bắt đúng.
- [ ] Chập thật vẫn bắt đúng.
- [ ] Điện trở NG vẫn bắt đúng.
- [ ] Chạm thoáng qua không commit sai dây/chập.
- [ ] Positive contact có debounce/stable-frame hợp lý.
- [ ] Không thêm lại timing setting vào UI.
- [ ] Không dùng `Thread.Sleep`.
- [ ] Một cycle một result.
- [ ] Build thành công.
- [ ] Runtime không crash.
- [ ] Config/history cũ vẫn đọc được.
- [ ] Git diff chỉ chứa thay đổi liên quan.

---

# 15. BÁO CÁO BẮT BUỘC SAU KHI SỬA

## A. OPEN CIRCUIT REMOVAL

- Nơi tạo OPEN_CIRCUIT cũ:
- Nơi add vào fault list:
- Nơi hiển thị:
- Nơi commit FAIL:
- Nơi popup:
- Nơi counter:
- Nơi history:
- Nơi relay:
- Cách đã vô hiệu hóa từng điểm:

## B. FAULT MATRIX MỚI

| Trạng thái | Có hiển thị lỗi? | Có FAIL? |
|---|---:|---:|
| Thiếu kết nối | NO | NO |
| Chưa cắm đủ đầu | NO | NO |
| Sai dây thật | YES | YES |
| Chập thật | YES | YES |
| Điện trở NG | YES | YES |

## C. I/O CONTACT SENSITIVITY

- Scan cadence:
- Debounce/stable-frame cũ:
- Debounce/stable-frame mới:
- Thời gian hiệu dụng:
- Vì sao không làm chậm Production đáng kể:
- Vì sao loại bỏ được touch transient:

## D. FILES CHANGED

| File | Thay đổi | Lý do |
|---|---|---|

## E. TEST

- Jig trống:
- Một đầu:
- Một đầu giữ lâu:
- Bỏ hẳn một dây:
- Đấu đúng:
- Đấu nhầm:
- Chập:
- Touch transient:
- Điện trở NG:

---

# 16. KẾT LUẬN BẮT BUỘC

Không được diễn giải yêu cầu này thành:

```text
giảm độ nhạy HỞ MẠCH
```

Yêu cầu thực tế là:

```text
HỞ MẠCH = VÔ HIỆU HÓA HOÀN TOÀN
```

và:

```text
MISSING CONNECTION != FAULT
MISSING CONNECTION != FAIL
MISSING CONNECTION != NG
```

Chỉ còn:

```text
WRONG CONNECTION
SHORT CIRCUIT
RESISTANCE NG
```

là các nhóm lỗi chính cần chốt FAIL theo yêu cầu hiện tại.

Quy trình:

```text
READ AGENTS.md
→ TRACE TẤT CẢ OPEN_CIRCUIT REFERENCES
→ DISABLE OPEN FAULT GENERATION
→ REMOVE OPEN FROM UI/FAIL/HISTORY/COUNTER/RELAY
→ KEEP TEST RUNNING WHEN CONNECTION IS MISSING
→ AUDIT POSITIVE IO CONTACT SENSITIVITY
→ ADD/ADJUST NON-BLOCKING STABLE-CONTACT FILTER
→ VERIFY WRONG WIRE
→ VERIFY SHORT
→ VERIFY RESISTANCE
→ BUILD
→ TEST TRÊN JIG
→ AUDIT GIT DIFF
→ REPORT
```
