# TASK: KHÔI PHỤC HIỂN THỊ CHÂN CHƯA KẾT NỐI TRÊN `FaultGrid` NHƯ TRẠNG THÁI GIÁM SÁT, KHÔNG PHẢI FAIL
## JBZ Universal Tester

**Mức độ:** HIGH / PRODUCTION  
**Phạm vi:** `FaultGrid` + luồng cập nhật trạng thái kết nối trực tiếp từ scan I/O  
**Không thay đổi yêu cầu trước:** `OPEN_CIRCUIT / HỞ MẠCH` vẫn **KHÔNG được phép làm FAIL/NG**, không mở popup, không khóa test, không ghi history lỗi, không tăng counter lỗi.

---

# 0. YÊU CẦU NGHIỆP VỤ CHÍNH

Sau task trước, phần mềm đã vô hiệu hóa HỞ MẠCH khỏi luồng FAIL. Tuy nhiên hiện tại `FaultGrid` cũng bị ẩn luôn các chân chưa kết nối.

Điều này không đúng yêu cầu hiển thị.

Yêu cầu mới:

> `FaultGrid` phải tiếp tục hiển thị các chân / kết nối expected đang CHƯA KẾT NỐI để người vận hành biết còn chân nào chưa lắp.

Nhưng:

> Các dòng này chỉ là **TRẠNG THÁI GIÁM SÁT**, tuyệt đối không phải lỗi Production.

---

# 1. HÀNH VI MONG MUỐN

Khi vừa chọn mã hàng / `.model`:

- tất cả các connection expected của model chưa có kết nối thực tế phải xuất hiện trên `FaultGrid`;
- hiển thị chân / I/O / tên dây / màu dây / trạng thái theo dữ liệu model;
- trạng thái có thể ghi:

```text
CHƯA KẾT NỐI
IO1 <-> IO7
```

hoặc wording hiện có tương đương.

Nhưng **không được ghi loại lỗi là HỞ MẠCH theo nghĩa FAIL** nếu logic UI hiện tại dùng `FaultKind.Open` để kích NG.

Nên tách thành loại hiển thị trung tính, ví dụ:

```text
CHƯA KẾT NỐI
WAITING CONNECTION
PENDING CONNECTION
```

Tên thực tế phải phù hợp architecture hiện tại.

---

# 2. KẾT NỐI ĐÚNG → DÒNG PHẢI TỰ MẤT

Ví dụ model có expected:

```text
IO1 <-> IO7
```

Ban đầu chưa nối:

```text
FaultGrid:
IO1 <-> IO7    CHƯA KẾT NỐI
```

Khi người vận hành nối đúng dây:

```text
IO1 <-> IO7
```

thì dòng này phải tự động **BIẾN MẤT KHỎI `FaultGrid`** ngay sau khi scan xác nhận kết nối ổn định.

Không cần bấm refresh.

Không cần restart test.

Không cần popup.

---

# 3. THÁO KẾT NỐI RA → DÒNG PHẢI HIỆN LẠI

Nếu connection đã đúng và dòng đã biến mất:

```text
IO1 <-> IO7 connected
```

sau đó người vận hành tháo một đầu ra:

```text
IO1 <-> IO7 no longer connected
```

thì dòng:

```text
IO1 <-> IO7    CHƯA KẾT NỐI
```

phải **HIỆN LẠI trên `FaultGrid`**.

Đây là danh sách trạng thái động theo scan I/O hiện tại.

Không được chỉ build một lần khi chọn model rồi giữ nguyên.

---

# 4. `FaultGrid` PHẢI TRỞ THÀNH "DANH SÁCH KẾT NỐI CHƯA HOÀN THÀNH + LỖI THẬT"

Sau khi sửa, `FaultGrid` có thể chứa 2 nhóm dữ liệu khác nhau:

## Nhóm A — Trạng thái chưa kết nối

```text
CHƯA KẾT NỐI
```

- chỉ để hướng dẫn thao tác;
- không FAIL;
- không NG;
- không popup;
- không history lỗi;
- không counter lỗi;
- không relay NG.

## Nhóm B — Lỗi thật

Chỉ các lỗi được phép chốt NG:

```text
SAI DÂY / ĐẤU NHẦM
CHẬP MẠCH
ĐIỆN TRỞ KHÔNG ĐẠT
```

các lỗi này vẫn theo flow FAIL hiện hành.

---

# 5. TUYỆT ĐỐI KHÔNG DÙNG `FaultKind.Open` NẾU NÓ CÒN ĐI VÀO FAIL FLOW

Task trước đã vô hiệu hóa `OPEN_CIRCUIT`.

Không được khôi phục bằng cách:

```csharp
FaultKind.Open
```

rồi add lại vào `FaultGrid` nếu object đó vẫn có thể đi vào:

```text
HasFault
ShouldFail
NG dialog
History
Counter
Relay
```

Phải tách rõ:

```text
DISPLAY-ONLY MISSING CONNECTION
```

với:

```text
REAL PRODUCTION FAULT
```

Có thể dùng:

- DTO riêng;
- row kind riêng;
- display status riêng;
- `IsFailure = false`;
- collection projection riêng;

tùy architecture hiện tại.

Nhưng điều kiện bắt buộc là:

> Dòng "chưa kết nối" có thể hiển thị trên `FaultGrid` mà không thể vô tình làm FAIL.

---

# 6. AUDIT `TestEngine.BuildRows()` / `BuildConfirmedOpenFaults()`

Theo báo cáo task trước, OPEN từng liên quan:

```text
ProductionFaultConfirmationGate
TestEngine.BuildRows()
TestEngine.BuildConfirmedOpenFaults()
TestViewModel
```

Codex phải audit lại.

Mục tiêu:

- không khôi phục `BuildConfirmedOpenFaults()` theo nghĩa lỗi;
- chỉ xây dựng danh sách **missing connection display rows**;
- danh sách này phải dựa trên expected connections của model và snapshot I/O hiện tại;
- update real-time.

Có thể cần method mới, ví dụ:

```text
BuildMissingConnectionDisplayRows()
BuildPendingConnectionRows()
BuildUnconnectedExpectedRows()
```

Tên tùy source.

---

# 7. QUY TẮC TÍNH DÒNG CHƯA KẾT NỐI

Với mỗi expected connection trong model:

```text
ExpectedPair(A, B)
```

nếu scan hiện tại chưa xác nhận:

```text
A connected to B
```

thì hiển thị một row "CHƯA KẾT NỐI".

Khi scan xác nhận đúng pair:

```text
A <-> B
```

thì remove row đó.

Khi pair mất đi:

```text
A !<-> B
```

thì add row trở lại.

---

# 8. KHÔNG DUPLICATE 2 DÒNG CHO CÙNG MỘT KẾT NỐI

Hiện trước đây có tình trạng một lỗi vật lý có thể hiện:

```text
IO1
IO7
```

thành 2 row.

Yêu cầu lần này:

> Một expected connection chỉ nên có **1 row hiển thị trạng thái** nếu về mặt model nó là một connection duy nhất.

Ví dụ:

```text
IO1 <-> IO7
```

chỉ hiển thị 1 row:

```text
CHƯA KẾT NỐI | IO1 <-> IO7
```

Không cần 2 row đối xứng:

```text
IO1 -> IO7
IO7 -> IO1
```

trừ khi data model thực sự định nghĩa là 2 connection độc lập.

---

# 9. GIỮ ĐÚNG THÔNG TIN MODEL

Row "CHƯA KẾT NỐI" phải tiếp tục hiển thị được các cột hiện có nếu dữ liệu tồn tại:

```text
Loại trạng thái
I/O
Chân
Tên dây
Dây dập nối
Tiết diện
Màu dây
Trạng thái
```

Không làm mất:

- tên dây;
- màu dây;
- pin/chân;
- connector;
- splice/dập nối;
- thông tin model.

---

# 10. MÀU HIỂN THỊ KHÔNG ĐƯỢC GÂY HIỂU NHẦM LÀ FAIL

Vì đây không còn là lỗi, nên không nên dùng toàn bộ style đỏ của lỗi NG.

Khuyến nghị:

```text
CHƯA KẾT NỐI = màu trung tính / vàng / cam nhẹ
REAL FAULT = đỏ
```

Nhưng không thay toàn bộ theme.

Phải giữ rõ ràng:

```text
pending connection ≠ fail
```

---

# 11. KHÔNG ĐƯỢC MỞ POPUP NG DO ROW "CHƯA KẾT NỐI"

Dù `FaultGrid` có 1, 10 hay 100 row chưa kết nối:

```text
không popup "XỬ LÝ HÀNG KHÔNG ĐẠT"
không set FAIL
không set NG
không stop test
không lock test
```

---

# 12. KHÔNG ĐƯỢC ĐƯA DISPLAY ROW VÀO HISTORY / COUNTER

Các row "CHƯA KẾT NỐI":

- không tăng `LỖI`;
- không tăng `FAIL`;
- không tăng `NG`;
- không ghi `test-history.db` như lỗi;
- không advance LOT do lỗi;
- không relay NG.

Nếu history UI cần ghi trạng thái test khác thì đó là task khác.

---

# 13. UPDATE REAL-TIME THEO SCAN

Danh sách phải refresh theo scan I/O nhưng không gây UI giật mạnh.

Audit:

```text
ObservableCollection
CollectionView
dispatcher update
diff update
ReplaceAll
PropertyChanged
```

Ưu tiên cập nhật theo diff:

```text
missing row mới → add
connection đã đúng → remove
```

thay vì clear/add toàn bộ collection mỗi 2 ms nếu điều đó làm UI flicker hoặc tốn CPU.

Nếu scan cadence là 2 ms:

> Không được rebuild toàn bộ DataGrid 500 lần/giây.

Có thể throttle UI projection hợp lý nhưng **không được làm logic test chậm đi**.

---

# 14. CONTACT STABILITY

Task trước đã có stable window cho positive contact.

Phải giữ nguyên:

```text
connection chỉ được coi là connected sau khi positive contact ổn định
```

Khi vừa chạm thoáng qua:

- không nên làm row biến mất rồi hiện lại liên tục;
- chỉ remove row sau khi connection stable;
- khi mất kết nối thật thì row hiện lại theo cơ chế ổn định tương ứng nếu architecture hỗ trợ.

Không tái tạo open-fault debounce.

Đây chỉ là **UI state stabilization**, không phải xác nhận lỗi HỞ.

---

# 15. CLIP / DẬP NỐI / NHIỀU ENDPOINT

Phải audit rule đặc biệt của model.

Nếu một nhóm connector/CLIP/dập nối có nhiều endpoint dùng chung một node:

- không được coi từng endpoint đồng nghĩa với một lỗi riêng;
- row hiển thị phải theo logical connection/node của model;
- khi connection hợp lệ theo rule của nhóm thì các row tương ứng phải biến mất đúng logic.

Không được phá logic CLIP/splice hiện có.

Nếu source có grouping/canonicalization helper thì phải tái sử dụng.

---

# 16. TEST CASE BẮT BUỘC

## Test 1 — Chọn mã hàng, chưa lắp gì

Expected:

```text
FaultGrid hiển thị tất cả expected connections chưa được nối.
```

Nhưng:

```text
FAIL = 0
NG popup = không
History FAIL = không
```

---

## Test 2 — Nối đúng 1 connection

Ví dụ:

```text
IO1 <-> IO7
```

Expected:

- row IO1 <-> IO7 biến mất;
- các row chưa nối khác vẫn còn;
- không FAIL.

---

## Test 3 — Tháo connection vừa nối

Expected:

- row IO1 <-> IO7 hiện lại;
- không FAIL.

---

## Test 4 — Nối dần toàn bộ sản phẩm

Expected:

```text
mỗi connection đúng → row tương ứng mất dần
```

Cuối cùng nếu tất cả expected connection đã đúng:

```text
FaultGrid không còn row "CHƯA KẾT NỐI"
```

---

## Test 5 — Đấu nhầm

Ví dụ:

```text
expected IO1 <-> IO7
actual IO1 <-> IO9
```

Expected:

- row pending/missing có thể phản ánh expected pair chưa hoàn thành;
- đồng thời lỗi `SAI DÂY / ĐẤU NHẦM` phải xuất hiện theo logic fault thật;
- FAIL chỉ đến từ `WRONG_CONNECTION`, không phải missing row.

---

## Test 6 — Chập mạch

Expected:

- SHORT vẫn FAIL đúng;
- missing rows không tự làm FAIL.

---

## Test 7 — Điện trở NG

Expected:

- resistance NG vẫn FAIL đúng;
- không ảnh hưởng display missing connection.

---

## Test 8 — Chạm thoáng qua

Expected:

- row chưa kết nối không flicker mất/hiện liên tục;
- chỉ thay đổi khi positive contact đạt stable gate.

---

# 17. ACCEPTANCE CRITERIA

Task chỉ DONE khi:

- [ ] Đã đọc `AGENTS.md`.
- [ ] `FaultGrid` lại hiển thị các expected connection chưa nối.
- [ ] Dòng chưa nối là display-only, không phải fault.
- [ ] Nối đúng → row tự biến mất.
- [ ] Tháo ra → row tự hiện lại.
- [ ] Không cần refresh thủ công.
- [ ] Không dùng `FaultKind.Open` theo đường FAIL cũ.
- [ ] Không popup NG do missing row.
- [ ] Không tăng FAIL/NG counter do missing row.
- [ ] Không ghi history FAIL do missing row.
- [ ] Không relay NG do missing row.
- [ ] Không khóa test.
- [ ] Wrong connection vẫn FAIL.
- [ ] Short vẫn FAIL.
- [ ] Resistance NG vẫn FAIL.
- [ ] Không duplicate 2 row cho cùng logical connection.
- [ ] UI không flicker mạnh.
- [ ] Không rebuild DataGrid theo mỗi raw scan 2 ms.
- [ ] Stable positive-contact gate vẫn hoạt động.
- [ ] Build thành công.
- [ ] Runtime không crash.
- [ ] Git diff chỉ chứa thay đổi liên quan.

---

# 18. BÁO CÁO BẮT BUỘC SAU KHI SỬA

## A. Current architecture
- Collection nào đang bind vào `FaultGrid`:
- `FaultGrid` hiện nhận rows từ đâu:
- OPEN trước đây bị remove ở đâu:

## B. New display-only flow

```text
Model expected connections
→ current stable I/O snapshot
→ calculate missing logical connections
→ build display-only rows
→ FaultGrid
```

## C. Separation from FAIL

Trả lời rõ:

```text
DisplayMissingConnectionRow có thể đi vào ShouldFail không?
```

Expected:

```text
NO
```

## D. Row lifecycle
- Khi nào add:
- Khi nào remove:
- Khi nào re-add:
- Stable-contact rule:

## E. Files changed

| File | Thay đổi | Lý do |
|---|---|---|

## F. Test
- Model loaded/no connection:
- Connect one:
- Disconnect one:
- Connect all:
- Wrong wire:
- Short:
- Resistance:
- Contact transient:

---

# 19. KẾT LUẬN YÊU CẦU

Không quay lại logic cũ:

```text
MISSING CONNECTION = OPEN FAULT
```

Mà phải là:

```text
MISSING CONNECTION = DISPLAY STATUS ONLY
```

Flow mong muốn:

```text
CHỌN MODEL
→ HIỆN CÁC CONNECTION CHƯA NỐI
→ NỐI ĐÚNG CONNECTION NÀO
→ ROW ĐÓ BIẾN MẤT
→ THÁO CONNECTION
→ ROW ĐÓ HIỆN LẠI
```

và hoàn toàn độc lập với:

```text
FAIL / NG / POPUP / HISTORY / COUNTER / RELAY
```

Các lỗi Production vẫn giữ:

```text
SAI DÂY / ĐẤU NHẦM
CHẬP MẠCH
ĐIỆN TRỞ KHÔNG ĐẠT
```
