# TASK: BỎ HOÀN TOÀN PHẦN "XÁC NHẬN HỞ MẠCH (ms)"
## JBZ Universal Tester

**Mức độ:** HIGH / PRODUCTION  
**Phạm vi:** Chỉ phần xác nhận HỞ MẠCH  
**Không thay đổi:** Xác nhận CHẬP MẠCH, sai kết nối và các timing khác

---

# 0. QUY TRÌNH BẮT BUỘC

Trước khi sửa:

1. Đọc `AGENTS.md` có hiệu lực trong repository.
2. Kiểm tra `git status`.
3. Audit đúng code đang xử lý:
   - `OPEN_CIRCUIT`;
   - timer/delay xác nhận HỞ MẠCH;
   - binding Settings;
   - config lưu giá trị xác nhận HỞ MẠCH;
   - state machine bắt đầu test.
4. Không sửa các phần không liên quan.
5. Không tạo file backup `.bak`, `_old`, `_copy` trong source tree.

---

# 1. HIỆN TRẠNG

Log thực tế đang có:

```text
DỪNG TEST do HỞ MẠCH: OPEN_CIRCUIT IO 1 -> IO 7
```

Trong phần Settings hiện còn tham số:

```text
Xác nhận hở mạch (ms)
```

Thực tế tốc độ quét và phát hiện HỞ MẠCH hiện tại đã đủ nhanh.

**Không cần thêm một lớp timer/delay xác nhận HỞ MẠCH nữa.**

---

# 2. YÊU CẦU CHÍNH

Bỏ hoàn toàn chức năng:

```text
Xác nhận hở mạch (ms)
```

Bao gồm cả:

- ô nhập trong giao diện Settings;
- binding/property dùng riêng cho delay xác nhận HỞ MẠCH;
- timer/Stopwatch/timestamp dùng để chờ xác nhận HỞ MẠCH;
- logic kiểu `pending open circuit`;
- điều kiện kiểu `open tồn tại đủ N ms mới FAIL`.

Sau khi sửa, khi hệ thống **đã thực sự ở trạng thái TEST hợp lệ**, nếu scan xác định HỞ MẠCH thật thì xử lý ngay theo logic lỗi hiện hành.

---

# 3. KHÔNG ĐƯỢC CHỈ SET GIÁ TRỊ = 0

Không sửa kiểu:

```text
OpenCircuitConfirmMs = 0
```

rồi giữ nguyên toàn bộ cơ chế timer.

Phải audit và loại bỏ dependency runtime không còn cần thiết của phần xác nhận HỞ MẠCH.

Mục tiêu là code rõ ràng:

```text
TEST ĐÃ BẮT ĐẦU HỢP LỆ
        ↓
SCAN I/O
        ↓
OPEN_CIRCUIT THẬT
        ↓
XỬ LÝ HỞ MẠCH NGAY
```

Không cần:

```text
OPEN_CIRCUIT
→ START TIMER
→ CHỜ N ms
→ KIỂM TRA LẠI
→ FAIL
```

---

# 4. CỰC KỲ QUAN TRỌNG: GIỮ NGUYÊN ĐIỀU KIỆN BẮT ĐẦU TEST

Việc bỏ delay HỞ MẠCH **không được làm hệ thống báo HỞ MẠCH khi công nhân còn đang lắp sản phẩm vào jig**.

Phải giữ nguyên/đảm bảo logic đã có để phân biệt:

```text
CHỜ LẮP SẢN PHẨM
ĐANG LẮP
READY TO TEST
TESTING
```

Chỉ được đánh giá HỞ MẠCH sau khi sản phẩm đã đủ điều kiện bắt đầu test.

Không được biến thay đổi này thành:

```text
cắm mới 1 đầu dây
→ OPEN_CIRCUIT
→ FAIL ngay
```

Nếu hiện tại state machine chưa bảo vệ được trường hợp này, phải báo cáo trước khi thay đổi logic.

---

# 5. KHÔNG ĐỤNG ĐẾN XÁC NHẬN CHẬP MẠCH

Task này **chỉ bỏ XÁC NHẬN HỞ MẠCH**.

Không tự ý xóa hoặc thay đổi:

```text
Xác nhận chập mạch (ms)
Xác nhận sai kết nối (ms)
```

Nếu code đang dùng chung timer/helper cho HỞ và CHẬP:

- phải tách xử lý cẩn thận;
- bỏ phần HỞ;
- giữ nguyên hành vi CHẬP;
- không gây regression cho SHORT_CIRCUIT.

---

# 6. SETTINGS

Xóa khỏi giao diện:

```text
Xác nhận hở mạch (ms)
```

Sau khi xóa:

- bố cục phải tự dồn lại gọn;
- không để khoảng trống thừa;
- không để label còn nhưng textbox mất;
- không để textbox disabled gây hiểu nhầm.

---

# 7. CONFIG CŨ PHẢI VẪN ĐỌC ĐƯỢC

Nếu file config cũ vẫn có key tương đương:

```text
OpenCircuitConfirmMs
```

thì ưu tiên backward compatibility:

- ứng dụng vẫn load config cũ;
- không crash deserialize;
- không yêu cầu sửa thủ công file config Production;
- runtime bỏ qua giá trị này;
- có thể giữ property legacy chỉ để deserialize nếu cần.

Không phá schema config nếu không cần thiết.

---

# 8. RESET / PENDING STATE

Audit và loại bỏ hoặc vô hiệu hóa đúng các state chỉ phục vụ delay HỞ MẠCH, ví dụ:

```text
OpenFaultSince
PendingOpenFault
OpenConfirmTimer
OpenCircuitStartTime
OpenDebounceState
```

hoặc tên tương đương.

Không để:

- timestamp cũ kéo sang cycle mới;
- pending HỞ tồn tại sau khi tháo sản phẩm;
- state legacy ảnh hưởng kết quả mới.

---

# 9. KHÔNG THAY ĐỔI CÁC TIMING KHÁC

Không thay đổi:

- chu kỳ quét I/O;
- thời gian ổn định sau khi lắp;
- JIG contact timing;
- R1 JIG pulse;
- R2 MARKING pulse;
- PASS chờ relay;
- đo điện trở;
- Probe Pin;
- UART/COM timing;
- xác nhận CHẬP;
- xác nhận SAI KẾT NỐI.

---

# 10. TEST BẮT BUỘC

## Test A — Hàng tốt

Lắp đầy đủ sản phẩm tốt.

Expected:

```text
PASS bình thường
```

Không bị ảnh hưởng bởi việc bỏ confirm HỞ.

---

## Test B — HỞ MẠCH thật

Lắp đầy đủ sản phẩm có lỗi HỞ.

Expected:

```text
OPEN_CIRCUIT
→ DỪNG TEST / FAIL ngay theo flow hiện tại
```

Không chờ `Xác nhận hở mạch (ms)`.

---

## Test C — Công nhân đang lắp

Mới lắp một đầu, đầu còn lại chưa lắp.

Expected:

- không FAIL vì HỞ;
- không tăng NG;
- không ghi history lỗi;
- không kích relay lỗi;
- vẫn chờ điều kiện READY TO TEST.

---

## Test D — CHẬP MẠCH

Dùng mẫu có lỗi SHORT nếu có.

Expected:

- logic xác nhận CHẬP vẫn giữ nguyên;
- không bị ảnh hưởng bởi task này.

---

## Test E — Config cũ

Load file config có giá trị `Xác nhận hở mạch`.

Expected:

- load bình thường;
- không crash;
- giá trị legacy không còn ảnh hưởng runtime.

---

# 11. ACCEPTANCE CRITERIA

Task chỉ DONE khi:

- [ ] Đã đọc `AGENTS.md`.
- [ ] Đã xác định đúng code confirm HỞ MẠCH.
- [ ] Đã bỏ field `Xác nhận hở mạch (ms)` khỏi Settings.
- [ ] Runtime không còn chờ timer xác nhận HỞ.
- [ ] OPEN_CIRCUIT thật được xử lý ngay sau khi test hợp lệ.
- [ ] Không FAIL khi sản phẩm vẫn đang trong giai đoạn lắp.
- [ ] Hàng tốt vẫn PASS.
- [ ] Hàng hở thật vẫn FAIL.
- [ ] Xác nhận CHẬP không thay đổi.
- [ ] Xác nhận sai kết nối không thay đổi.
- [ ] Config cũ vẫn load được.
- [ ] Không carry-over pending state HỞ sang cycle mới.
- [ ] Build thành công.
- [ ] Runtime không crash.
- [ ] Không có binding error mới.
- [ ] Git diff chỉ chứa thay đổi liên quan yêu cầu này.

---

# 12. BÁO CÁO SAU KHI SỬA

Codex phải báo cáo:

## A. Root cause
- File:
- Class:
- Method:
- Property confirm HỞ:
- Timer/state confirm HỞ:
- Flow cũ:

## B. Thay đổi
- UI Settings:
- Runtime:
- Config compatibility:
- State reset:

## C. Files changed

| File | Thay đổi | Lý do |
|------|----------|-------|

## D. Validation
- Build:
- Hàng tốt:
- HỞ thật:
- Đang lắp sản phẩm:
- CHẬP:
- Config cũ:

## E. Xác nhận không thay đổi
- SHORT confirmation;
- Wrong connection confirmation;
- I/O scan period;
- relay timings;
- resistance;
- Master/Production flow ngoài phạm vi.

---

# 13. KẾT LUẬN YÊU CẦU

Thực hiện đúng:

```text
READ AGENTS.md
→ AUDIT OPEN_CIRCUIT CONFIRM
→ XÓA "XÁC NHẬN HỞ MẠCH (ms)" KHỎI SETTINGS
→ BỎ TIMER/DELAY CONFIRM HỞ KHỎI RUNTIME
→ GIỮ READY-TO-TEST GATE
→ GIỮ NGUYÊN SHORT / WRONG CONNECTION
→ BUILD
→ TEST THỰC TẾ
→ AUDIT GIT DIFF
→ BÁO CÁO
```
