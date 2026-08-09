# BÁO CÁO YÊU CẦU SỬA CHỮA `JBZUniversalTester_V12_9`
## Tự động hóa Master Sample + Tối ưu TestView theo hướng vùng vận hành lớn nhất

---

# 1. MỤC TIÊU CHÍNH

Tiếp tục sửa `JBZUniversalTester_V12_9` theo nguyên tắc:

> **Màn hình TestView là màn hình vận hành chính, nên vùng trạng thái test và lỗi thực tế phải chiếm diện tích lớn nhất.**

Các phần phụ như:

```text
LOT
Thông tin sản phẩm
Card
Thống kê
Master controls
Board status
Các nút thao tác phụ
```

phải được thu gọn tối đa nhưng vẫn đủ đọc.

Người vận hành phải nhìn ngay được:

```text
Đang ở trạng thái nào?
Đang chờ mẫu nào?
Đang test mẫu đạt hay mẫu lỗi?
Có lỗi gì?
Sai dây ở chân nào?
Mong đợi chân nào?
Thực tế đang nối chân nào?
Master lỗi đã đạt bao nhiêu điểm?
Sản phẩm đã được phép vào Production hay chưa?
```

---

# 2. BỎ TOÀN BỘ THAO TÁC MASTER BẰNG NÚT

Không cần người vận hành bấm:

```text
TEST MASTER ĐẠT
TEST MASTER LỖI
XÁC NHẬN 2 MASTER
```

Không cần checkbox:

```text
Mẫu đạt OK
Mẫu lỗi OK
```

Hàng:

```text
MASTER SAMPLE
```

hiện tại phải được loại bỏ khỏi TestView.

Không chỉ ẩn tạm bằng `Visibility=Collapsed`.

Phải refactor logic để Master trở thành **state machine tự động**.

---

# 3. QUY TRÌNH MASTER MỚI – TỰ ĐỘNG HOÀN TOÀN

Sau khi chọn mã hàng/model:

```text
CHỌN MÃ HÀNG
    ↓
LOAD THT / CONFIG
    ↓
CHỜ LẮP MẪU MASTER ĐẠT
    ↓
TỰ ĐỘNG TEST MẪU ĐẠT
    ↓
PASS ĐẦY ĐỦ
    ↓
KÍCH RELAY / JIG ĐẨY MẪU ĐẠT RA
    ↓
CHỜ LẮP MẪU MASTER LỖI
    ↓
TỰ ĐỘNG TEST MẪU LỖI
    ↓
ĐẾM CÁC ĐIỂM LỖI THỰC TẾ
    ↓
ĐỦ N/N
    ↓
KÍCH RELAY / JIG ĐẨY MẪU LỖI RA
    ↓
MASTER GATE = PASS
    ↓
CHO PHÉP PRODUCTION
```

Không cần xác nhận bằng chuột.

---

# 4. THÊM CẤU HÌNH `SỐ LỖI MASTER`

Trong phần cài đặt theo mã hàng/model phải thêm:

```text
Số lỗi Master
```

Ví dụ:

```text
Số lỗi Master = 2
```

Ý nghĩa:

Mẫu Master lỗi thật có:

```text
2 điểm lỗi sai dây chủ động được tạo sẵn
```

Chương trình chỉ xác nhận:

```text
MASTER LỖI OK
```

khi phát hiện đủ:

```text
2/2
```

điểm lỗi khác nhau.

---

# 5. KHÔNG ĐƯỢC ĐẾM TRÙNG CÙNG MỘT LỖI

Đây là yêu cầu rất quan trọng.

Board scan có thể phát lại cùng một lỗi qua nhiều frame.

Ví dụ cùng một lỗi:

```text
IO1 ↔ IO7
```

xuất hiện 100 frame.

Không được tính:

```text
100 lỗi
```

Phải chỉ tính:

```text
1 điểm lỗi Master
```

Cần có định danh lỗi ổn định, ví dụ:

```csharp
MasterFaultKey
{
    FaultType,
    SourceIo,
    TargetIo,
    ExpectedSourceIo,
    ExpectedTargetIo
}
```

hoặc cấu trúc tương đương.

Dùng:

```csharp
HashSet<MasterFaultKey>
```

hoặc collection unique.

---

# 6. MẠNG ĐÃ ĐẠT `0/N`, `1/N`, `2/N`

Khu hiện đang hiển thị:

```text
Mạng đã đạt 0/2
```

có thể tiếp tục sử dụng nhưng phải đổi semantics đúng với Master lỗi.

Ví dụ:

```text
0/2
```

= chưa phát hiện điểm lỗi Master nào.

```text
1/2
```

= đã phát hiện đúng 1 điểm lỗi.

```text
2/2
```

= đã phát hiện đủ lỗi Master.

Khi đang:

```text
1/2
```

phải **giữ trạng thái chờ**, không kích relay.

Chỉ khi:

```text
2/2
```

mới:

```text
MASTER LỖI OK
→ kích relay
→ đẩy mẫu lỗi ra
→ mở khóa Production
```

---

# 7. TRẠNG THÁI CHÍNH PHẢI HIỂN THỊ LỚN NHẤT

Toàn bộ trạng thái Master phải đưa lên **ô trạng thái lớn chính của TestView**.

Không dùng một dòng chữ nhỏ nằm cạnh button như hiện tại.

Các trạng thái:

```text
ĐANG CHỜ LẮP MẪU MASTER ĐẠT
```

```text
ĐANG KIỂM TRA MẪU MASTER ĐẠT
```

```text
MASTER ĐẠT - PASS
ĐANG ĐẨY MẪU RA
```

```text
ĐANG CHỜ LẮP MẪU SAI DÂY
```

```text
ĐANG KIỂM TRA MẪU SAI DÂY
LỖI MASTER: 1/2
```

```text
MASTER LỖI OK
2/2
ĐANG ĐẨY MẪU RA
```

```text
SẴN SÀNG SẢN XUẤT
```

Font phải lớn, đậm, dễ nhìn từ xa.

---

# 8. TESTVIEW PHẢI ƯU TIÊN DIỆN TÍCH CHO VÙNG VẬN HÀNH

Bố cục mới nên theo nguyên tắc:

```text
┌──────────────────────────────────────────────────────────────┐
│ Thông tin sản phẩm + LOT + ngày giờ + thống kê (GỌN)         │
├──────────────────────────────────────────────────────────────┤
│ Card / Probe status (GỌN)                                    │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│                TRẠNG THÁI TEST CHÍNH                         │
│                                                              │
│                LỖI ĐANG XẢY RA                               │
│                                                              │
│                CHI TIẾT IO / PIN                             │
│                                                              │
│                MASTER PROGRESS                               │
│                                                              │
│                VÙNG NÀY PHẢI LỚN NHẤT                        │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│ Danh sách lỗi chi tiết / mạng IO                             │
├──────────────────────────────────────────────────────────────┤
│ Bottom toolbar ẩn/hiện                                      │
└──────────────────────────────────────────────────────────────┘
```

Không để các hàng phụ chiếm nhiều chiều cao.

---

# 9. THU GỌN KHU THÔNG TIN SẢN PHẨM

Các field:

```text
Mã hàng
Sản phẩm
Loại xe
Mã KH
LOT
```

phải gọn.

Ví dụ có thể bố trí 1–2 hàng:

```text
Mã hàng: M030066701S
Sản phẩm: CL4
Loại xe: ...
Mã KH: ...
LOT: 2000
```

Không cần các TextBox kéo dài quá mức.

Không được để thông tin sản phẩm chiếm 25–30% chiều cao màn hình.

---

# 10. THU GỌN THỐNG KÊ

Các ô:

```text
Tổng số
Số đạt
Số lỗi
Tỷ lệ đạt
Dây chưa kết nối
Chập mạch
Mạng đã đạt
```

phải giữ nhưng gọn.

Có thể bố trí dạng compact:

```text
Tổng 0 | Đạt 0 | Lỗi 0 | 0.00%
```

hoặc một grid nhỏ.

Không để mỗi counter chiếm một hàng cao riêng nếu không cần.

---

# 11. XÓA DÒNG BOARD STATUS KHỎI TESTVIEW

Xóa:

```text
Bo: FT245R USB FIFO [A90764PH] - ĐÃ KẾT NỐI
```

khỏi vùng chính.

Thông tin này chỉ nên tồn tại ở:

```text
Settings
Diagnostics
Device Log
```

hoặc icon nhỏ.

Diện tích phải trả lại cho vùng vận hành.

---

# 12. HÀNG CARD / PROBE PHẢI GỌN

Hàng:

```text
ĐẦU DÒ
SẴN SÀNG
CARD 1
CARD 2
...
```

giữ chiều cao thấp.

Card bật/tắt vẫn nhìn thấy đầy đủ.

Dòng:

```text
Sẵn sàng - chạm đầu dò vào chân I/O hoặc chân PIN trên JIG
```

đẩy sang phải.

Không để hàng Probe chiếm quá nhiều chiều cao.

---

# 13. DANH SÁCH LỖI MASTER PHẢI HIỂN THỊ TỪNG DÒNG ĐỎ

Khi test Master lỗi:

Mỗi điểm lỗi thực tế phát hiện được phải hiển thị thành một dòng riêng.

Ví dụ:

```text
DÂY CHƯA KẾT NỐI
IO1 ↔ IO7
```

```text
ĐẤU SAI
IO2 → IO8
```

Mỗi dòng lỗi Master:

```text
Foreground = đỏ
FontWeight = Bold
```

hoặc style fault rõ ràng.

Không gộp tất cả thành một text duy nhất.

---

# 14. MỖI LỖI MASTER PHẢI HIỂN THỊ RÕ VỊ TRÍ

Ví dụ:

```text
LỖI MASTER 1/2
DÂY CHƯA KẾT NỐI
IO1 ↔ IO7
```

Nếu có metadata THT:

```text
C01-PIN1 / IO1
↔
C03-PIN7 / IO7
```

Nếu đấu sai:

```text
LỖI MASTER 2/2
ĐẤU SAI

Mong đợi:
IO2 → IO5

Thực tế:
IO2 → IO8
```

Người vận hành phải biết ngay vị trí lỗi.

---

# 15. TRẠNG THÁI MASTER VÀ DANH SÁCH LỖI PHẢI TÁCH NHAU

Ô trạng thái chính:

```text
ĐANG KIỂM TRA MẪU SAI DÂY
LỖI MASTER: 1/2
```

Danh sách bên dưới:

```text
1. DÂY CHƯA KẾT NỐI IO1 ↔ IO7
```

Khi lỗi thứ 2 xuất hiện:

```text
2. ĐẤU SAI IO2 → IO8
```

Sau đó:

```text
MASTER LỖI OK
2/2
```

Không xóa dòng lỗi ngay khi vừa phát hiện nếu người vận hành vẫn cần xem.

---

# 16. SAU MASTER LỖI OK

Khi đủ:

```text
N/N
```

phải:

1. khóa việc đếm thêm;
2. giữ snapshot lỗi Master vừa xác nhận;
3. hiển thị:
   ```text
   MASTER LỖI OK
   ```
4. kích relay/JIG;
5. chờ mẫu được tháo/đẩy ra;
6. reset runtime Master state;
7. chuyển:
   ```text
   ProductionEnabled = true
   ```

Không cần người dùng bấm xác nhận.

---

# 17. MASTER ĐẠT PHẢI PASS ĐÚNG TOÀN BỘ TEST

Mẫu Master đạt chỉ được coi OK nếu:

```text
continuity PASS
không open
không wrong wiring
không short
resistance PASS nếu THT có resistance
```

Không được chỉ dựa vào:

```text
không thấy fault trong vài frame
```

Phải sử dụng cùng Completion logic với Production PASS.

---

# 18. MASTER LỖI KHÔNG ĐƯỢC YÊU CẦU PASS

Mẫu lỗi được thiết kế để cố ý có fault.

Do đó logic phải khác Master đạt.

Không được:

```text
MASTER LỖI → đợi PASS
```

Mà phải:

```text
MASTER LỖI
→ xác minh đúng số fault cần thiết
→ Master lỗi OK
```

---

# 19. KHÔNG ĐƯỢC ĐẾM LỖI HỆ THỐNG VÀO MASTER

Không tính các lỗi:

```text
FTDI disconnect
Keysight error
THT load error
database error
system exception
```

vào:

```text
Số lỗi Master
```

Chỉ product wiring fault thực tế.

Ví dụ candidate:

```text
OpenCircuit
WrongWiring
ShortCircuit
```

Nếu Master lỗi resistance cũng được dùng ở nhà máy thì phải có cấu hình riêng, không tự suy luận.

---

# 20. `SỐ LỖI MASTER` PHẢI ĐƯỢC LƯU THEO MODEL

Ví dụ:

```text
Model A → 2 lỗi Master
Model B → 3 lỗi Master
Model C → 1 lỗi Master
```

Không dùng global setting nếu mỗi mã hàng khác nhau.

Có thể thêm:

```csharp
MasterFaultRequiredCount
```

vào model/production config.

---

# 21. KHI CHỌN MODEL MỚI

Khi người dùng chọn mã hàng khác:

```text
Reset Master Gate
Clear Master fault collection
Load MasterFaultRequiredCount
Set state = WaitingGoodMaster
```

Không giữ Master state từ model trước.

---

# 22. MASTER PHẢI RESET KHI RESTART APP

Sau khi restart:

Nếu policy nhà máy yêu cầu kiểm Master lại:

```text
Master Gate = Locked
```

và bắt đầu:

```text
CHỜ LẮP MẪU MASTER ĐẠT
```

Không tự ghi nhớ Master pass từ phiên trước nếu không có yêu cầu cụ thể.

---

# 23. STATE MACHINE ĐỀ XUẤT

Nên có enum:

```csharp
public enum MasterSequenceState
{
    Disabled,
    WaitingGoodMaster,
    TestingGoodMaster,
    EjectingGoodMaster,
    WaitingBadMaster,
    TestingBadMaster,
    EjectingBadMaster,
    Completed
}
```

Không dùng nhiều bool rời:

```text
IsGoodMaster
GoodMasterOk
BadMasterOk
WaitingMaster
Confirmed
```

vì dễ sai trạng thái.

---

# 24. DATA MODEL ĐỀ XUẤT

Ví dụ:

```csharp
public sealed class MasterValidationState
{
    public MasterSequenceState State { get; set; }

    public int RequiredFaultCount { get; set; }

    public int DetectedFaultCount { get; set; }

    public HashSet<MasterFaultKey> DetectedFaults { get; } = new();

    public bool GoodMasterPassed { get; set; }

    public bool BadMasterPassed { get; set; }
}
```

---

# 25. VÙNG VẬN HÀNH LỚN PHẢI TỰ THAY ĐỔI THEO STATE

## Chờ Master đạt

Hiển thị lớn:

```text
ĐANG CHỜ LẮP MẪU MASTER ĐẠT
```

## Đang test Master đạt

```text
ĐANG KIỂM TRA MASTER ĐẠT
```

## Good Master PASS

```text
MASTER ĐẠT - PASS
```

## Chờ Master lỗi

```text
ĐANG CHỜ LẮP MẪU SAI DÂY
```

## Đang test Master lỗi

```text
ĐANG KIỂM TRA MẪU SAI DÂY

1 / 2 LỖI
```

## Completed

```text
MASTER HOÀN TẤT
SẴN SÀNG SẢN XUẤT
```

---

# 26. VÙNG LỖI PRODUCTION CŨNG DÙNG CÙNG KHU LỚN

Sau khi Master hoàn tất, vùng lớn đó chuyển sang Production.

Ví dụ:

```text
ĐANG CHỜ LẮP SẢN PHẨM
```

```text
ĐANG KIỂM TRA
```

```text
PASS
```

```text
DÂY CHƯA KẾT NỐI
IO1 ↔ IO7
```

```text
ĐẤU SAI
Mong đợi: IO2 → IO5
Thực tế: IO2 → IO8
```

```text
CHẬP MẠCH
IO11 ↔ IO24
```

Tức là chỉ có **một khu vực trạng thái lớn duy nhất**, không tạo nhiều panel lớn cạnh tranh diện tích.

---

# 27. TỐI ƯU CHIỀU CAO CÁC KHU PHỤ

Mục tiêu gợi ý:

```text
Header + product info       ≈ 10–15%
Probe/Card row              ≈ 5–7%
Main operation area         ≈ 50–60%
Fault/detail grid           ≈ 20–25%
Bottom toolbar              overlay / auto-hide
```

Không bắt buộc đúng % tuyệt đối, nhưng nguyên tắc:

```text
Main operation area phải lớn nhất.
```

---

# 28. BOTTOM TOOLBAR KHÔNG ĐƯỢC CHIẾM CHIỀU CAO KHI ẨN

Các nút:

```text
ĐO ĐIỆN TRỞ
XÁC NHẬN PASS + RELAY
DỪNG AN TOÀN
VỀ TRANG CHÍNH
```

phải overlay ở đáy.

Khi ẩn:

```text
không giữ Row Height
không còn viền
không còn dải xám
```

Diện tích phía dưới phải trả lại cho vùng vận hành/lỗi.

---

# 29. KHÔNG CẦN NÚT `XÁC NHẬN PASS + RELAY` TRONG MASTER AUTO

Trong giai đoạn Master:

```text
relay được kích tự động theo state machine
```

Không yêu cầu người dùng bấm.

Nếu nút này còn cần cho Manual/Diagnostic thì chỉ hiện ở chế độ phù hợp.

Không cho nút manual làm rối workflow Production.

---

# 30. MASTER RESULT KHÔNG ĐƯỢC TÍNH VÀO LOT SẢN XUẤT

Good Master và Bad Master:

```text
không tăng Total production
không tăng Pass production
không tăng Fail production
không tăng LOT
```

Có thể ghi riêng:

```text
MasterValidationHistory
```

hoặc log riêng.

---

# 31. MASTER LỖI KHÔNG ĐƯỢC KÍCH FAULT EJECT THEO LOGIC PRODUCT FAIL

Trong lúc test mẫu lỗi:

fault là **mong đợi**.

Không được mỗi khi thấy một fault lại:

```text
FaultEjectRelay
```

hoặc:
- popup production failure;
- tăng fail stats;
- phát âm thanh lỗi sản phẩm.

Chỉ khi:

```text
DetectedFaultCount == RequiredFaultCount
```

mới kích relay Master eject theo sequence.

---

# 32. PHẢI PHÂN BIỆT `EXPECTED MASTER FAULT` VÀ `PRODUCTION FAULT`

Có thể cùng `FaultResult` nhưng context khác:

```csharp
TestRunContext.MasterBad
TestRunContext.MasterGood
TestRunContext.Production
```

MasterBad:

```text
fault = evidence để validate Master
```

Production:

```text
fault = sản phẩm FAIL
```

Không dùng cùng completion behavior.

---

# 33. MASTER LỖI CẦN SO KHỚP ĐÚNG LOẠI LỖI HAY CHỈ ĐẾM SỐ LƯỢNG?

Yêu cầu hiện tại của người dùng:

```text
Số lỗi Master = số điểm lỗi sai thực tế trên sản phẩm
```

Mức tối thiểu phải xác minh:

```text
N điểm fault duy nhất
```

Khuyến nghị tốt hơn nếu project có thể lưu cấu hình:

```text
MasterFaultDefinitions
```

ví dụ:

```text
Open IO1 ↔ IO7
Wrong IO2 expected IO5 actual IO8
```

Khi đó Master validation không chỉ đếm `2 lỗi bất kỳ`,
mà xác minh đúng **2 lỗi đã biết**.

Nếu chưa có dữ liệu cấu hình cụ thể, trước mắt hỗ trợ `RequiredFaultCount`, nhưng architecture nên cho phép nâng lên exact fault definitions.

---

# 34. TEST BẮT BUỘC – GOOD MASTER

## Test M1

Model:

```text
RequiredMasterFaultCount = 2
```

Lắp mẫu đạt.

Expected:

```text
WaitingGoodMaster
→ TestingGoodMaster
→ PASS
→ EjectingGoodMaster
→ WaitingBadMaster
```

Relay chỉ kích sau PASS thật.

---

# 35. TEST BẮT BUỘC – BAD MASTER 1/2

Lắp mẫu lỗi.

Phát hiện:

```text
Fault A
```

Expected:

```text
Detected = 1
Required = 2

Status:
ĐANG KIỂM TRA MẪU SAI DÂY
1/2
```

Không relay.

---

# 36. TEST BẮT BUỘC – LỖI A LẶP 100 FRAME

Board phát lại cùng Fault A.

Expected:

```text
Detected vẫn = 1
```

Không được lên:

```text
2/2
```

---

# 37. TEST BẮT BUỘC – BAD MASTER 2/2

Sau Fault A, phát hiện Fault B khác.

Expected:

```text
Detected = 2/2
MASTER LỖI OK
```

Sau đó:

```text
EjectingBadMaster
→ Completed
→ ProductionEnabled
```

---

# 38. TEST BẮT BUỘC – SAI MASTER LỖI

Nếu mẫu lỗi chỉ có:

```text
1/2
```

Expected:

```text
chờ vô thời hạn cho tới khi đúng lỗi thứ 2 xuất hiện
```

Không auto timeout thành OK.

Nếu cần timeout cảnh báo, chỉ báo:

```text
MASTER LỖI CHƯA ĐỦ
```

không mở Production.

---

# 39. TEST BẮT BUỘC – MODEL KHÁC

Chuyển model:

```text
A: 2 lỗi
B: 3 lỗi
```

Expected khi chọn B:

```text
Master progress reset = 0/3
WaitingGoodMaster
```

Không giữ 2/2 từ model A.

---

# 40. TEST BẮT BUỘC – UI

Sau khi xóa hàng MASTER SAMPLE:

Expected:

- vùng main operation tăng chiều cao rõ rệt;
- không còn khoảng trắng do row cũ;
- trạng thái Master nằm giữa/vùng lớn;
- các lỗi Master đỏ dễ nhìn;
- thông tin sản phẩm vẫn gọn và đủ;
- không mất border;
- không mất chữ.

---

# 41. XÓA CODE/UI MANUAL MASTER KHÔNG CÒN DÙNG

Search:

```text
TestMasterGoodCommand
TestMasterBadCommand
ConfirmMasterCommand
GoodMasterOk
BadMasterOk
MasterConfirm
```

hoặc tên tương đương.

Nếu đã thay bằng auto state machine:
- xóa code chết;
- bỏ command binding;
- bỏ XAML;
- bỏ boolean dư thừa.

Không để manual path và auto path cùng tồn tại gây double-completion.

---

# 42. HISTORY / LOG MASTER

Master nên có log riêng:

```text
MASTER GOOD START
MASTER GOOD PASS
MASTER GOOD EJECT

MASTER BAD START
MASTER BAD FAULT 1/2
MASTER BAD FAULT 2/2
MASTER BAD PASS
MASTER BAD EJECT
MASTER VALIDATION COMPLETED
```

Không ghi Master bad thành:

```text
Production FAIL
```

---

# 43. TIÊU CHÍ NGHIỆM THU CUỐI CÙNG

Bản sửa đạt khi:

1. Chọn mã hàng xong không cần bấm nút Master.
2. UI lớn hiển thị:
   ```text
   ĐANG CHỜ LẮP MẪU MASTER ĐẠT
   ```
3. Mẫu đạt PASS → relay tự kích.
4. Sau đó:
   ```text
   ĐANG CHỜ LẮP MẪU SAI DÂY
   ```
5. Mẫu lỗi có cấu hình 2 lỗi:
   - 1 lỗi → `1/2`, không relay;
   - cùng lỗi lặp → vẫn `1/2`;
   - lỗi thứ 2 khác → `2/2`.
6. `2/2` → relay tự kích.
7. Sau khi mẫu lỗi ra:
   ```text
   SẴN SÀNG SẢN XUẤT
   ```
8. Master không tăng LOT/Pass/Fail Production.
9. Dòng board status bị xóa khỏi TestView.
10. Hàng Master Sample manual bị xóa.
11. Vùng vận hành chính trở thành vùng lớn nhất màn hình.
12. Các phần phụ được làm gọn nhưng không mất chữ/viền.
13. Lỗi Master và lỗi Production đều hiển thị rõ IO/PIN cụ thể.
14. Probe/Card/Bottom Toolbar vẫn hoạt động đúng theo các yêu cầu đã chốt trước đó.

---

# 44. MỤC TIÊU GIAO DIỆN CUỐI CÙNG

Khi đứng trước máy, người vận hành chỉ cần nhìn vào vùng trung tâm lớn là biết:

```text
ĐANG CHỜ LẮP MẪU MASTER ĐẠT

hoặc

ĐANG CHỜ LẮP MẪU SAI DÂY

hoặc

MASTER LỖI: 1/2

hoặc

DÂY CHƯA KẾT NỐI
IO1 ↔ IO7

hoặc

ĐẤU SAI
Mong đợi: IO2 → IO5
Thực tế:  IO2 → IO8

hoặc

PASS
```

Các khu vực còn lại chỉ làm nhiệm vụ cung cấp thông tin phụ.

**Ưu tiên số 1 của TestView là khả năng vận hành trực quan, nhanh, dễ nhìn và không yêu cầu thao tác chuột không cần thiết.**
KHI HOÀN THÀNH TẠO PHIỂN BẢN MỚI v12_9_5
