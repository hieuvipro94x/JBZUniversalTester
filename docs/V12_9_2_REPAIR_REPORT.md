# BÁO CÁO SAU SỬA `JBZUniversalTester V12.9.2`
## Probe/Card status row + Bottom Toolbar + Probe Pin no-delay

## 1. Phạm vi

Bản V12.9.2 tiếp tục từ V12.9.1 và xử lý đúng ba nhóm yêu cầu bổ sung:

1. Hàng `ĐẦU DÒ`: tách Card khỏi vùng hướng dẫn/ProbeContacts, hiển thị cả Card bật/tắt.
2. Bottom Toolbar: chuyển sang overlay + hot zone, animation mượt, ẩn hoàn toàn.
3. Probe Pin: bỏ TTL/quarantine 700–1500 ms, TOUCH/RELEASE cập nhật trực tiếp.

Không thay đổi fault model/history/config-log của V12.9.1 ngoài các phần cần thiết cho Probe/Card/UI.

---

## 2. File đã sửa/thêm

### Mã nguồn

- `Views/TestWindow.xaml`
- `Views/TestWindow.xaml.cs`
- `ViewModels/TestViewModel.cs`
- `Services/BoardIoDecoder.cs`
- `Models/BoardCardState.cs`
- `Version.props`
- `Properties/AssemblyInfo.cs`

### Build / tài liệu

- `VERIFY_BUILD_V12_9_2.cmd`
- `V12_9_2_CHANGELOG.txt`
- `docs/REQUEST_V12_9_2_PROBE_CARD_TOOLBAR_NO_DELAY.md`
- `docs/V12_9_2_STATIC_VALIDATION.txt`
- `docs/V12_9_2_REPAIR_REPORT.md`

---

## 3. Hàng `ĐẦU DÒ` mới

`TestWindow.xaml` được đổi thành 4 vùng độc lập:

```text
ĐẦU DÒ | SẴN SÀNG/ĐANG DÒ | CARD 1 ... CARD N | Hướng dẫn / ProbeContacts
```

Thay đổi chính:

- bỏ cột Card cố định `Width="380"`;
- vùng Card dùng cột co giãn `2.25*`;
- vùng trạng thái/ProbeContacts dùng cột riêng `1.35*`;
- hàng Probe đổi từ `Height="48"` cố định sang `Auto` + `MinHeight="48"`;
- dòng hướng dẫn được căn phải:
  - `HorizontalAlignment="Right"`;
  - `TextAlignment="Right"`;
  - `Margin="16,0,12,0"`;
- khi có `ProbeContacts`, hướng dẫn `Collapsed`;
- `ProbeContacts` dùng `WrapPanel`, không chen vào vùng Card.

---

## 4. Card bật/tắt đều hiển thị

`TestViewModel` có collection:

```csharp
ObservableCollection<BoardCardState> Cards
```

Alias `ActiveCards` được giữ lại để tương thích source cũ nhưng XAML V12.9.2 bind trực tiếp vào `Cards`.

`RebuildActiveCards()` tạo động toàn bộ slot từ:

```csharp
BoardCapacity.MaxPhysicalCardCount
```

không hard-code `CARD 1 ... CARD 6`.

Mỗi `BoardCardState` hiện có:

```text
CardNumber
IsEnabled
IsScanning
HasProbeActivity
FirstGlobalIo
LastGlobalIo
```

Style:

- bật: nền xanh nhạt / border xanh;
- tắt: nền xám / chữ xám;
- đang có Probe activity: vàng/cam;
- nhấc Probe chỉ xóa `HasProbeActivity`, không đổi `IsEnabled`.

Card dùng `WrapPanel` để tự xuống dòng và không cần horizontal scroll cố định như V12.9.1.

---

## 5. Nguyên nhân delay Probe trong V12.9.1

Audit source phát hiện hai timeout dài:

```csharp
InlineProbeQuarantineMs = 700;
ProbeRelayLockoutMs = 1500;
```

Trong `TryDetectInlineProbeContacts()`, khi frame mới không còn Probe, code cũ vẫn trả lại contact trước đó nếu chưa hết 700 ms. Vì vậy `ProbeContacts` bị giữ trên UI sau khi nhấc que.

Ngoài ra `BoardIoDecoder.FeedProbe()` đã phân biệt được:

```text
active = true
active = false
```

nhưng code V12.9.1 chỉ phát frame ngay khi `active=true`; nhánh `active=false` không cập nhật UI ngay và có thể phải chờ kết thúc snapshot `C0`.

---

## 6. Probe Pin V12.9.2: loại bỏ delay UI

### 6.1. Bỏ quarantine 700 ms

Đã xóa hoàn toàn:

```text
InlineProbeQuarantineMs
```

Frame Production mới không còn chữ ký Probe thì:

```text
ClearInlineProbeContactsState()
→ ClearInlineProbeDisplay()
```

được yêu cầu lên Dispatcher ngay.

Không còn giữ contact cũ để chống flicker.

### 6.2. Relay interlock giảm còn debounce rất ngắn

`ProbeRelayLockoutMs = 1500` được thay bằng:

```csharp
ProbeRelayReleaseDebounceMs = 40;
```

40 ms chỉ là interlock relay chống rung sau RELEASE, **không giữ ProbeContacts trên UI**.

### 6.3. Không dùng stable-frame Production cho Probe

`OnBoardFrameReceived()` của Probe không gọi `RequiredStableFrames`.

`RequiredStableFrames` vẫn nằm trong `TestEngine` cho Production network confirmation.

### 6.4. Không `Task.Delay`/timer trên đường RX Probe

`OnBoardFrameReceived()` không có:

```text
await Task.Delay(...)
DispatcherTimer
TTL giữ contact
```

Luồng xử lý là:

```text
RX
→ decoder
→ update Probe state
→ Dispatcher.BeginInvoke
→ UI
```

### 6.5. TOUCH/RELEASE event trực tiếp trong decoder

`BoardIoDecoder.FeedProbe()` hiện xử lý:

```text
TARGET word → TOUCH/ON  → Add(io)
SOURCE word → RELEASE/OFF → Remove(io)
```

Sau mỗi event đều phát `ScanFrame` ngay, không chờ `C0`.

`C0` vẫn phát snapshot đầy đủ để xác nhận trạng thái, nhưng không còn là điều kiện bắt buộc cho UI phản hồi.

### 6.6. Hai contact thật

State vẫn là `HashSet<int>` và ViewModel lấy tối đa 2 contact:

```text
IO11 + IO24
```

Nếu RELEASE IO11:

```text
IO11 bị remove ngay
IO24 vẫn còn
```

không reset toàn bộ nếu IO24 thực tế vẫn active.

---

## 7. Chống stale frame bằng generation

Hai lớp generation hiện được giữ:

### Transport

`D2xxBoardTransport` có `_scanGeneration` và bỏ frame nếu:

```csharp
generation != Volatile.Read(ref _scanGeneration)
```

### ViewModel/UI

`TestViewModel.OnBoardFrameReceived()` capture `_runtimeGeneration` và trước khi callback UI chạy sẽ kiểm tra lại:

```text
RuntimeMode.Probe + generation hiện tại
hoặc
RuntimeMode.Production + generation hiện tại
```

Frame/callback cũ không được phép ghi lại Probe UI sau khi đổi mode.

---

## 8. Diagnostic latency

Khi Probe state thực sự đổi, V12.9.2 có diagnostic:

```text
PROBE_LATENCY TOUCH IO11; RX->VM=... ms; VM->UI=... ms; seq=...
PROBE_LATENCY RELEASE; RX->VM=... ms; VM->UI=... ms; seq=...
```

Log dùng `AppLogLevel.Diagnostic`, vì vậy Production `Normal` không bị ghi dày ra disk.

Dữ liệu này cho phép đo trên máy thật:

```text
RX → ViewModel
ViewModel → UI request/render callback
```

Mục tiêu nghiệm thu vẫn là cảm nhận gần tức thời, ưu tiên `<100 ms` nếu tốc độ scan/phần cứng cho phép.

---

## 9. Bottom Toolbar mới

### 9.1. Bỏ row 10 px cũ

Đã loại cơ chế V12.8:

```text
Grid.Row="4"
Height="10"
animate Height 10 → 66 → 10
```

Đây là nguyên nhân khi ẩn vẫn còn dải xám/viền dưới.

Root Grid V12.9.2 chỉ còn 4 row nội dung chính.

### 9.2. Overlay

Toolbar hiện là overlay:

```text
BottomToolbarOverlay
BottomButtonPanel
```

không chiếm một row layout riêng.

Khi ẩn:

```text
TranslateY = ActualHeight + 8
Opacity = 0
IsHitTestVisible = false
```

`ClipToBounds="True"` bảo đảm phần đã translate ra ngoài không còn nhìn thấy.

### 9.3. Hot zone 24 px

Có vùng:

```text
BottomToolbarHotZone
Height = 24
Background = Transparent
```

Root Grid dùng bottom margin 0 để hot zone chạm đúng mép dưới cửa sổ, không phải rê chuột chính xác vào một dải 1–2 px.

### 9.4. Chống chớp

Code-behind giữ hai state:

```text
_isMouseOverBottomHotZone
_isMouseOverBottomToolbar
```

Chỉ hide khi cả hai đều false.

Grace delay:

```text
200 ms
```

chỉ dành cho toolbar UX, không liên quan Probe.

### 9.5. Animation

Show:

```text
TranslateY → 0
Opacity → 1
200 ms
QuadraticEase EaseOut
```

Hide:

```text
TranslateY → ActualHeight + 8
Opacity → 0
180 ms
QuadraticEase EaseIn
```

`HandoffBehavior.SnapshotAndReplace` được dùng để khi MouseEnter/Leave nhanh, animation mới tiếp tục từ trạng thái đang render thay vì nhảy về đầu.

---

## 10. Static validation

Kết quả:

```text
PASS 42/42
FAIL 0
```

Đã kiểm tra:

- Version 12.9.2 đồng bộ;
- toàn bộ XAML/XML parse hợp lệ;
- Cards và ProbeContacts dùng WrapPanel;
- hướng dẫn căn phải;
- Card model có IsEnabled / IsScanning / HasProbeActivity;
- không còn `InlineProbeQuarantineMs`;
- không còn `ProbeRelayLockoutMs = 1500`;
- release debounce = 40 ms;
- không có `Task.Delay`/`DispatcherTimer` trong đường RX Probe;
- RELEASE xóa UI trực tiếp;
- runtime generation guard tồn tại;
- decoder TOUCH/RELEASE trực tiếp;
- Probe không dùng `RequiredStableFrames`;
- transport scan generation vẫn hoạt động;
- `BottomMenuArea` cũ đã bị xóa;
- hot zone 24 px;
- hide offset lấy `ActualHeight`;
- opacity/hit test bị tắt khi ẩn;
- show/hide animation đúng 200/180 ms;
- XAML event handlers đều tồn tại;
- delimiter balance của các file C# sửa chính hợp lệ.

Chi tiết: `docs/V12_9_2_STATIC_VALIDATION.txt`.

---

## 11. Phần chưa thể xác nhận trong sandbox

Sandbox hiện không có:

```text
dotnet
msbuild
```

nên chưa thể chạy build WPF thật, launch giao diện, test DPI hoặc đo latency FTDI trên hardware.

Project có sẵn:

```text
VERIFY_BUILD_V12_9_2.cmd
```

để chạy trên máy Windows có .NET 8 SDK:

```text
restore
build Release win-x86
publish one-file
```

Sau build, test hardware bắt buộc:

1. TOUCH IO11 → phải hiện gần như ngay.
2. RELEASE IO11 → phải mất gần như ngay.
3. IO11 → IO24 nhanh → không giữ stale IO11.
4. IO11 → IO12 → IO13 → UI theo chân hiện tại.
5. Hai contact IO11 + IO24 → release một chân chỉ xóa đúng chân đó.
6. Rê chuột xuống đáy → toolbar dễ xuất hiện.
7. Hot zone → toolbar → không hide giữa đường.
8. Rời toolbar → hide mượt.
9. Hide xong → không còn dải xám/viền.
10. Mouse vào/ra nhanh → không kẹt nửa ẩn/nửa hiện.

---

## 12. Version phát hành

```text
JBZUniversalTester V12.9.2
AssemblyVersion 12.9.2.0
FileVersion 12.9.2.0
VersionFileTag 12_9_2
```

Tên output theo versioning hiện tại:

```text
JBZUniversalTester_V12_9_2.exe
```
