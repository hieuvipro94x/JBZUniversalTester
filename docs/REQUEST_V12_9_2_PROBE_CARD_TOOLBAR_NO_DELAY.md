# BÁO CÁO BỔ SUNG YÊU CẦU SỬA `JBZUniversalTester_V12_9`
## Probe/Card status row + thanh công cụ ẩn/hiện + loại bỏ delay Probe Pin

---

# 1. MỤC TIÊU BỔ SUNG

Tiếp tục sửa `JBZUniversalTester_V12_9` ở ba khu vực:

1. Hàng `ĐẦU DÒ`:
   - đẩy dòng hướng dẫn sang bên phải;
   - dành vùng trung tâm để hiển thị đầy đủ Card;
   - Card bật và Card tắt đều phải nhìn thấy;
   - ProbeContacts hiển thị song song, không chen/che Card.

2. Thanh công cụ ẩn/hiện ở đáy:
   - rê chuột xuống dễ nhận hơn;
   - animation mượt;
   - không chớp;
   - khi ẩn phải biến mất hoàn toàn, không còn viền/dải xám phía dưới.

3. `Probe Pin`:
   - chạm que dò phải hiện gần như ngay;
   - nhấc que dò phải mất gần như ngay;
   - tuyệt đối không giữ trạng thái thêm 1–2 giây;
   - không dùng debounce/TTL dài gây cảm giác phần mềm phản hồi chậm.

---

# 2. PHÂN TÍCH HÀNG `ĐẦU DÒ` HIỆN TẠI

Hiện hàng Probe có dạng gần như:

```text
ĐẦU DÒ | SẴN SÀNG | CARD 1 | CARD 2 | CARD 3 | CARD 4 | CARD 5 | CARD 6 | Sẵn sàng - chạm đầu dò...
```

Vấn đề:

- dòng hướng dẫn Probe đang chiếm vùng ngay sau Card;
- khi số Card tăng, Card dễ bị chật/clip;
- không có vùng độc lập cho status/hướng dẫn Probe;
- khó hiển thị đồng thời toàn bộ card bật/tắt và các ProbeContact.

---

# 3. BỐ CỤC HÀNG PROBE MỚI

Phải tách thành bốn vùng:

```text
┌─────────┬───────────┬────────────────────────────────────┬──────────────────────────────────────────┐
│ ĐẦU DÒ  │ SẴN SÀNG  │ CARD 1 CARD 2 ... CARD N          │ Trạng thái / ProbeContacts               │
└─────────┴───────────┴────────────────────────────────────┴──────────────────────────────────────────┘
```

Khuyến nghị Grid:

```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="Auto"/>
    <ColumnDefinition Width="Auto"/>
    <ColumnDefinition Width="Auto"/>
    <ColumnDefinition Width="*"/>
</Grid.ColumnDefinitions>
```

Ý nghĩa:

```text
Column 0 = ĐẦU DÒ
Column 1 = SẴN SÀNG
Column 2 = danh sách Card
Column 3 = dòng hướng dẫn hoặc ProbeContacts
```

Không để Card và text hướng dẫn trong cùng một `StackPanel`.

---

# 4. ĐẨY DÒNG HƯỚNG DẪN SANG BÊN PHẢI

Dòng:

```text
Sẵn sàng - chạm đầu dò vào chân I/O hoặc chân PIN trên JIG
```

phải nằm ở cột cuối:

```xml
HorizontalAlignment="Right"
TextAlignment="Right"
VerticalAlignment="Center"
Margin="16,0,12,0"
```

Mục tiêu:

```text
CARD 1 CARD 2 CARD 3 ... CARD N              Sẵn sàng - chạm đầu dò...
```

Không để text hướng dẫn bắt đầu ngay sát Card cuối.

---

# 5. KHI CÓ PROBE CONTACT, ẨN HƯỚNG DẪN VÀ HIỆN CONTACT

Logic:

```text
HasInlineProbeContacts = false
    → hiện dòng hướng dẫn

HasInlineProbeContacts = true
    → ẩn dòng hướng dẫn
    → hiện ProbeContacts
```

`ProbeContacts` phải nằm cùng vùng cột phải, không làm danh sách Card thay đổi kích thước.

---

# 6. ĐOẠN `ItemsControl` HIỆN TẠI

Đoạn hiện có:

```xml
<ItemsControl ItemsSource="{Binding ProbeContacts}">
    ...
</ItemsControl>
```

có thể giữ ItemTemplate, nhưng cần:

- chuyển vào vùng status bên phải;
- không đặt trong vùng Card;
- hỗ trợ nhiều ProbeContact;
- nếu nhiều contact, có thể đổi ItemsPanel từ:

```xml
<StackPanel Orientation="Horizontal"/>
```

sang:

```xml
<WrapPanel Orientation="Horizontal"/>
```

để không tràn màn hình.

---

# 7. CARD BẬT VÀ CARD TẮT ĐỀU PHẢI HIỂN THỊ

Không chỉ render Card đang active.

Nên dùng:

```csharp
ObservableCollection<CardStatusViewModel> Cards
```

mỗi Card có:

```csharp
CardNumber
IsEnabled
IsScanning
HasProbeActivity
FirstIo
LastIo
```

TestView phải hiển thị tất cả Card slot đã được hệ thống hỗ trợ/xác minh.

Không hard-code chỉ:

```text
CARD 1 ... CARD 6
```

nếu cấu hình/hardware hỗ trợ nhiều hơn.

---

# 8. STYLE CARD BẬT/TẮT

Card bật:

```text
nền xanh nhạt
border xanh
chữ đậm
```

Card tắt:

```text
nền xám nhạt
border xám
chữ xám
```

Card có Probe activity:

```text
IsEnabled = true
HasProbeActivity = true
```

thì dùng highlight riêng.

Không nhầm:

```text
CardEnabled
```

với:

```text
HasProbeActivity
```

Nhấc Probe ra chỉ xóa highlight Probe, không làm Card thành Disabled.

---

# 9. PROBE PIN KHÔNG ĐƯỢC CÓ DELAY 1–2 GIÂY

Đây là yêu cầu bắt buộc mới.

Hiện tượng hiện tại:

```text
chạm que dò
→ mất một khoảng thời gian mới hiện

nhấc que dò
→ vẫn giữ trạng thái thêm khoảng 1–2 giây mới mất
```

Hành vi này **không đạt yêu cầu**.

Mục tiêu:

```text
TOUCH → UI cập nhật gần như tức thời
RELEASE → UI xóa gần như tức thời
```

Cảm giác sử dụng phải giống phần mềm gốc.

---

# 10. KHÔNG ĐƯỢC DÙNG TTL 1–2 GIÂY ĐỂ GIỮ PROBE

Phải search toàn project các từ khóa:

```text
Probe
PinProbe
Touch
Release
Debounce
Delay
Task.Delay
DispatcherTimer
Timer
Timeout
TTL
Hold
Stable
StableFrames
LastSeen
LastProbeSeen
ProbeRelease
ProbeTimeout
ContactTimeout
```

Đặc biệt tìm code kiểu:

```csharp
await Task.Delay(1000);
await Task.Delay(1500);
await Task.Delay(2000);
```

hoặc:

```csharp
if (DateTime.UtcNow - lastSeen > TimeSpan.FromSeconds(2))
```

hoặc:

```csharp
ProbeTimeoutMs = 1500;
```

Nếu đang dùng để giữ Probe trên UI thì phải sửa.

---

# 11. PHÂN BIỆT DEBOUNCE CHỐNG NHIỄU VÀ DELAY UI

Nếu hardware có rung/nhiễu, có thể cần debounce rất ngắn.

Nhưng không được dùng debounce dài đến mức người vận hành cảm thấy trễ.

Khuyến nghị mục tiêu sau khi đo runtime thật:

```text
Touch debounce:     0–30 ms
Release debounce:   0–50 ms
```

Chỉ tăng nếu trace phần cứng chứng minh thật sự cần.

Tuyệt đối không mặc định:

```text
500 ms
1000 ms
2000 ms
```

cho Probe UI.

---

# 12. PROBE UI KHÔNG ĐƯỢC ĐỢI `RequiredStableFrames` CỦA PRODUCTION

Phải kiểm tra xem Probe có đang dùng chung:

```csharp
RequiredStableFrames
```

hoặc logic xác nhận frame ổn định của Production hay không.

Probe phải có đường riêng.

Ví dụ:

```text
Production:
có thể cần stable-frame logic

Probe:
touch/release cần phản hồi trực tiếp
```

Không dùng cùng bộ đệm nếu làm Probe chậm.

---

# 13. RELEASE PHẢI DỰA VÀO TÍN HIỆU THỰC, KHÔNG DỰA VÀO TIMER DÀI

Nếu protocol board có event/frame release rõ:

```text
Probe ON
Probe OFF
```

phải xử lý trực tiếp event OFF.

Không được:

```text
không thấy Probe trong 2 giây
→ mới coi là release
```

nếu hardware đã cung cấp trạng thái release.

Nếu protocol chỉ cung cấp snapshot, phải cập nhật theo snapshot mới nhất.

---

# 14. NẾU BOARD CHỈ GỬI `ACTIVE SET`, PHẢI SO SÁNH SNAPSHOT

Ví dụ frame mới báo active:

```text
{ IO11, IO24 }
```

frame tiếp theo:

```text
{ IO24 }
```

thì phải suy ra ngay:

```text
IO11 released
```

không giữ IO11 thêm TTL 1–2 giây.

Nên có:

```csharp
previousProbeSet
currentProbeSet
```

và:

```text
added   = current - previous
removed = previous - current
```

UI cập nhật ngay theo `added/removed`.

---

# 15. NẾU PROTOCOL LÀ EVENT-BASED, DÙNG EVENT TRỰC TIẾP

Nếu board gửi event:

```text
ProbeContact(io)
ProbeRelease(io)
```

thì:

```csharp
OnProbeContact(io)
{
    AddOrUpdateProbe(io);
}

OnProbeRelease(io)
{
    RemoveProbe(io);
}
```

Không dùng timer để trì hoãn UI.

---

# 16. KHÔNG ĐƯỢC DÙNG `Task.Delay` TRÊN ĐƯỜNG RX

Tuyệt đối tránh:

```csharp
OnFrameReceived(...)
{
    await Task.Delay(...);
    ...
}
```

nếu delay đó nằm trên đường xử lý FTDI/Probe.

Raw RX phải:

```text
parse nhanh
route nhanh
update state
post UI update
return
```

Không block scan thread.

---

# 17. UI UPDATE PHẢI ĐƯỢC DISPATCH NGAY

Nếu dùng WPF:

```csharp
Application.Current.Dispatcher.BeginInvoke(...)
```

hoặc abstraction UI dispatcher hiện có.

Không cần chờ `DispatcherTimer` 500–2000 ms để flush Probe state.

Probe collection update phải được đẩy lên UI ngay khi decoder có state mới.

---

# 18. TRÁNH `ObservableCollection` UPDATE BỊ BATCH QUÁ CHẬM

Nếu project đang gom event rồi flush định kỳ:

```text
1 giây/lần
2 giây/lần
```

thì Probe sẽ có cảm giác trễ.

Production/history có thể batch.

Probe UI thì không.

Có thể tách:

```text
FastProbeUiChannel
BackgroundHistoryChannel
```

---

# 19. KHÔNG ĐƯỢC GIỮ `ProbeContacts` CHỈ ĐỂ CHỐNG NHẤP NHÁY

Nếu mục tiêu chống flicker, không dùng TTL 1–2 giây.

Thay bằng:

- debounce rất ngắn;
- snapshot diff;
- generation;
- sequence number;
- suppress duplicate identical frame.

Ví dụ:

```csharp
if (sameProbeStateAsPrevious)
    return;
```

không cần trì hoãn.

---

# 20. PHẢI ĐO LATENCY THỰC TẾ

Thêm diagnostic timestamp tạm thời:

```text
RX timestamp
Decoded timestamp
ProbeState updated timestamp
UI rendered/requested timestamp
```

Tính:

```text
RX → decoder
decoder → ViewModel
ViewModel → UI
```

Mục tiêu:

```text
< 100 ms cảm nhận được là tức thời
```

Lý tưởng thấp hơn nếu scan frame cho phép.

Không cần ghi diagnostic này ở chế độ Production bình thường sau khi xác minh.

---

# 21. TEST BẮT BUỘC CHO DELAY PROBE

## Test Probe-Latency-1 — Touch

Thao tác:

```text
chạm IO11
```

Expected:

```text
IO11 xuất hiện gần như ngay
```

Không được đợi 1–2 giây.

---

## Test Probe-Latency-2 — Release

Thao tác:

```text
nhấc que khỏi IO11
```

Expected:

```text
IO11 biến mất gần như ngay
```

Không được giữ 1–2 giây.

---

## Test Probe-Latency-3 — Chạm nhanh

```text
touch IO11
release
touch IO24
release
```

nhanh liên tục.

Expected:

- không giữ IO cũ;
- không trộn IO11 với IO24;
- không hiện stale contact.

---

## Test Probe-Latency-4 — Chuyển chân

```text
IO11 → IO12 → IO13
```

Expected:

```text
UI cập nhật theo chân hiện tại
```

không để:

```text
IO11 IO12 IO13
```

cùng tồn tại vì timeout cũ nếu thực tế chỉ có một que dò.

---

## Test Probe-Latency-5 — Hai contact thật

Nếu hardware thực sự hỗ trợ hai contact:

```text
IO11 + IO24
```

Expected:

```text
hiện cả hai
```

release IO11:

```text
IO11 mất ngay
IO24 vẫn còn
```

---

# 22. PROBE MODE PHẢI DÙNG GENERATION ĐỂ CHỐNG STALE FRAME

Khi switch:

```text
Production → Probe
Probe → Production
```

tăng:

```csharp
_scanGeneration++;
```

Frame cũ không đúng generation phải bỏ.

Không dùng timeout 1–2 giây để “chờ frame cũ tự hết”.

---

# 23. THANH CÔNG CỤ DƯỚI CÙNG – YÊU CẦU SỬA MƯỢT

Toolbar:

```text
ĐO ĐIỆN TRỞ
XÁC NHẬN PASS + RELAY
DỪNG AN TOÀN
VỀ TRANG CHÍNH
```

phải là overlay ở đáy.

Không đặt trong `Grid.Row Height="..."` cố định nếu row đó vẫn giữ khoảng trống khi toolbar ẩn.

---

# 24. TOOLBAR PHẢI ẨN HOÀN TOÀN

Hide theo:

```text
TranslateY = ActualHeight + ExtraOffset
```

không hard-code:

```text
Y=40
Y=45
```

sau hide:

```text
Opacity = 0
IsHitTestVisible = false
```

Container:

```xml
ClipToBounds="True"
```

để không còn dải/viền ở góc dưới.

---

# 25. TẠO `HOT ZONE` RIÊNG

Tạo vùng trong suốt:

```xml
<Border
    Height="24"
    VerticalAlignment="Bottom"
    Background="Transparent"
    Panel.ZIndex="200"/>
```

Hot zone:

- không nhìn thấy;
- luôn nhận chuột;
- không phụ thuộc toolbar đang ẩn/hiện.

Khi MouseEnter:

```text
ShowBottomToolbar()
```

---

# 26. KHÔNG HIDE NGAY KHI CHUỘT RỜI HOT ZONE

Dùng hai state:

```text
IsMouseOverHotZone
IsMouseOverToolbar
```

Chỉ hide khi cả hai false.

Có thể thêm delay rất ngắn cho toolbar:

```text
150–250 ms
```

để tránh chớp khi chuột đi qua mép.

Lưu ý:
- delay toolbar 150–250 ms là để UX mượt;
- **không áp dụng delay này cho Probe Pin**.

---

# 27. ANIMATION TOOLBAR

Show:

```text
TranslateY: ActualHeight → 0
Opacity: 0 → 1
Duration: ~180–220 ms
```

Hide:

```text
TranslateY: 0 → ActualHeight + offset
Opacity: 1 → 0
Duration: ~160–200 ms
```

Dùng easing nhẹ:

```text
CubicEase / QuadraticEase
```

Không dùng animation quá dài.

---

# 28. TRÁNH XUNG ĐỘT ANIMATION

Nếu MouseEnter xảy ra khi animation Hide chưa xong:

```text
cancel/replace animation cũ
→ animate từ giá trị hiện tại
```

Không reset transform đột ngột về đầu.

Nếu MouseLeave rồi MouseEnter liên tục:

```text
toolbar chuyển hướng mượt
```

không giật.

---

# 29. TEST TOOLBAR BẮT BUỘC

## Toolbar-1
Rê chuột xuống mép dưới.

Expected:
- dễ kích hoạt;
- không cần đưa đúng 1–2 px.

## Toolbar-2
Đưa chuột từ hot zone lên button.

Expected:
- toolbar không tự hide giữa đường.

## Toolbar-3
Rời khỏi toolbar.

Expected:
- hide mượt.

## Toolbar-4
Sau khi hide.

Expected:
- biến mất hoàn toàn;
- không còn dải xám/viền góc dưới.

## Toolbar-5
Mouse vào/ra nhanh nhiều lần.

Expected:
- không giật;
- không chớp;
- không bị kẹt nửa ẩn nửa hiện.

---

# 30. TIÊU CHÍ NGHIỆM THU CUỐI

Bản sửa chỉ đạt khi:

### Probe/Card
- toàn bộ Card bật/tắt đều nhìn thấy;
- dòng hướng dẫn ở bên phải;
- ProbeContacts không che Card;
- Card active/probe state phân biệt rõ.

### Probe Latency
- chạm que → hiện gần như tức thời;
- nhấc que → mất gần như tức thời;
- không còn delay 1–2 giây;
- không stale contact;
- không dùng TTL dài để giữ UI.

### Bottom Toolbar
- dễ gọi bằng chuột;
- hot zone đủ lớn;
- animation mượt;
- không chớp;
- hide hoàn toàn;
- không còn dải/viền ở đáy màn hình.
