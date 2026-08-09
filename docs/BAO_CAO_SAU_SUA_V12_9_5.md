# BÁO CÁO SAU SỬA `JBZUniversalTester V12.9.5`
## Master Sample tự động + TestView ưu tiên vùng vận hành

## 1. Kết quả tổng quát

Project đã được nâng từ V12.9.2 lên **V12.9.5** theo báo cáo `BAO_CAO_V12_9_5_MASTER_AUTO_TOI_UU_TESTVIEW_VAN_HANH.md`.

Các mục chính đã triển khai:

- bỏ toàn bộ nút/command Master thủ công trên TestView;
- Master chuyển thành state machine tự động;
- thêm `Số lỗi Master` theo từng model/mã hàng;
- chống đếm trùng cùng một lỗi qua nhiều frame bằng `HashSet<MasterFaultKey>`;
- Master Good phải PASS continuity và resistance nếu model có resistance;
- Master Bad chỉ xác nhận khi đủ N/N fault dây duy nhất;
- Master không tăng LOT, Total, Pass, Fail production;
- Master Bad không dùng logic Product FAIL/marking;
- xóa Board Status khỏi TestView;
- xóa hẳn hàng `MASTER SAMPLE`;
- vùng trạng thái Master/Production trung tâm trở thành vùng lớn nhất;
- lỗi Master hiển thị từng dòng đỏ riêng với IO/PIN và expected/actual;
- giữ Probe/Card/Bottom Toolbar của V12.9.2;
- nâng version đồng bộ thành 12.9.5.

---

## 2. File đã sửa / thêm

### File đã sửa

1. `Version.props`
2. `JBZUniversalTester.csproj`
3. `Properties/AssemblyInfo.cs`
4. `Models/ProductionSettings.cs`
5. `Services/ProductionConfigService.cs`
6. `Services/TestEngine.cs`
7. `ViewModels/ProductionSettingsViewModel.cs`
8. `ViewModels/TestViewModel.cs`
9. `Views/ProductionSettingsPage.xaml`
10. `Views/ProductionSettingsPage.xaml.cs`
11. `Views/TestWindow.xaml`

### File mới

1. `Models/MasterModels.cs`
2. `VERIFY_BUILD_V12_9_5.cmd`
3. `V12_9_5_CHANGELOG.txt`
4. `docs/REQUEST_V12_9_5_MASTER_AUTO_TESTVIEW.md`
5. `docs/V12_9_5_STATIC_VALIDATION.txt`

---

## 3. Master thủ công đã bị loại bỏ

Đã xóa đường UI/logic sử dụng:

- `StartGoodMasterCommand`
- `StartBadMasterCommand`
- `ConfirmMasterSamplesCommand`
- `MasterSampleMode`
- các nút `TEST MASTER ĐẠT`, `TEST MASTER LỖI`, `XÁC NHẬN 2 MASTER`
- hàng `MASTER SAMPLE` trên TestView.

Không còn manual path chạy song song với Master auto.

`AutoMasterSequence` chỉ còn là field compatibility để đọc config cũ. Từ V12.9.5 `ProductionConfigService.Normalize()` luôn ép giá trị này thành `true`.

---

## 4. State machine Master mới

Tạo enum `MasterSequenceState`:

```text
Disabled
WaitingGoodMaster
TestingGoodMaster
EjectingGoodMaster
WaitingBadMaster
TestingBadMaster
EjectingBadMaster
Completed
```

Luồng runtime:

```text
CHỌN MODEL
→ Reset Master Gate
→ WaitingGoodMaster
→ phát hiện mẫu
→ TestingGoodMaster
→ PASS đầy đủ
→ EjectingGoodMaster
→ xác nhận mẫu đã rời JIG
→ WaitingBadMaster
→ phát hiện mẫu lỗi
→ TestingBadMaster
→ đếm fault duy nhất 0/N ... N/N
→ EjectingBadMaster
→ xác nhận mẫu đã rời JIG
→ Completed
→ ProductionEnabled = true
```

Khi chưa `Completed`, Production Gate luôn khóa.

---

## 5. Good Master

Good Master chỉ được xác nhận khi:

- continuity PASS;
- không Open;
- không Wrong Wiring;
- không Short;
- resistance PASS nếu THT/model có resistance step.

Sau PASS thật, code gọi cùng `CompletePassAsync()` của engine nhưng:

```csharp
markingEnabled: false
```

Vì vậy Good Master:

- không kích Relay 2 MARKING;
- chỉ kích Relay 1 JIG để đẩy mẫu ra;
- không ghi production result;
- không tăng LOT/Pass/Fail.

Nếu Good Master fail resistance/continuity, latch PASS được giữ đến khi mẫu được tháo để tránh tự đo/lặp relay theo từng frame.

---

## 6. Bad Master và chống đếm trùng

Tạo `MasterFaultKey` gồm:

```text
FaultType
SourceIo
TargetIo
ExpectedSourceIo
ExpectedTargetIo
```

Runtime sử dụng:

```csharp
HashSet<MasterFaultKey>
```

Khi cùng một lỗi xuất hiện 100 frame:

```text
HashSet.Add(key) = false
→ không tăng bộ đếm
```

Open và Short được normalize thứ tự hai đầu để:

```text
IO1 ↔ IO7
```

và:

```text
IO7 ↔ IO1
```

được coi là cùng một lỗi.

Bad Master chỉ nhận các product wiring fault:

```text
OpenCircuit
WrongWiring
ShortCircuit
```

Không tính:

- FTDI/device error;
- Keysight error;
- database error;
- THT load error;
- system exception;
- resistance fault mặc định.

---

## 7. Quy tắc N/N

Ví dụ model cấu hình:

```text
Số lỗi Master = 2
```

Runtime:

```text
0/2 → chờ
1/2 → tiếp tục chờ, tuyệt đối không relay
lỗi A lặp lại → vẫn 1/2
lỗi B khác → 2/2
```

Chỉ khi đạt `2/2`:

```text
MASTER LỖI OK
→ khóa collection, không đếm thêm
→ giữ snapshot lỗi
→ Relay 1 JIG eject
→ chờ release thật
→ Master Gate PASS
→ ProductionEnabled = true
```

Không có timeout biến `1/2` thành OK.

---

## 8. Khoảng ổn định khi mới lắp Master Bad

Đã thêm khoảng ổn định rất ngắn riêng cho Master validation:

```text
MasterBadSettleMs = 120 ms
```

Mục đích là tránh đếm các Open tạm thời khi giắc đang được cắm vào JIG.

Đây không phải TTL Probe. Probe vẫn giữ logic V12.9.2 và không bị delay 1–2 giây.

---

## 9. Cấu hình `Số lỗi Master` theo model

`ProductionSettings` có:

```csharp
MasterFaultRequiredCount
MasterFaultCountsByModel
```

`MasterFaultRequiredCount` là fallback/default.

`MasterFaultCountsByModel` lưu riêng từng model/mã hàng.

Ví dụ logic hỗ trợ:

```text
Model A → 2
Model B → 3
Model C → 1
```

Cấu hình được lưu cả trong:

- `production.settings.json`;
- `UniversalTester.cfg`.

CFG dùng dạng:

```text
[MasterFaultRequiredCount]2
[MasterFault.<model-key>]3
```

Trang Cài đặt đã thêm:

```text
Số lỗi Master
Model Master hiện tại
```

và validate 1–99.

---

## 10. Reset Master khi đổi model / restart

`SetModel()` luôn gọi:

```text
ResetMasterGateForModel()
```

Khi đổi model:

- `MasterApproved = false`;
- xóa HashSet fault;
- xóa danh sách fault Master;
- load lại `MasterFaultRequiredCount` của model mới;
- về `WaitingGoodMaster`;
- progress về `0/N`.

VM mới sau restart cũng mặc định gate khóa. Không phục hồi Master PASS của phiên trước.

---

## 11. Master không tính vào sản lượng

Toàn bộ vùng code Master state machine không gọi:

```text
RecordCompletedProduct(...)
```

Do đó Good/Bad Master không:

- tăng `Total`;
- tăng `Pass`;
- tăng `Fail`;
- tăng `LOTNO`;
- ghi như một Production FAIL.

Master có log riêng qua `AsyncFileLogService.Current.Test(...)`.

---

## 12. Bad Master không dùng behavior Product FAIL

Tạo API riêng:

```csharp
TestEngine.EjectMasterSampleAsync()
```

API này:

- AllRelaysOff;
- pulse `JigEjectRelay` (Relay 1);
- AllRelaysOff;
- không dùng `MarkingRelay`.

Bad Master fault là evidence xác nhận master, không gọi `HandleWiringFaultAsync()` của Production.

---

## 13. TestView mới

TestView được chia thành 4 vùng chính:

```text
Header compact
Probe/Card compact
Main Operation Area 3*
Fault/Detail Area 2*
```

Main Operation Area là vùng lớn nhất.

### Header compact

Gồm:

```text
Model + Version + ngày giờ
Mã hàng | Sản phẩm | Loại xe | Mã KH | LOT
Tổng | Đạt | Lỗi | Tỷ lệ
Chưa nối | Đấu sai | Chập | Mạng/Master
```

Không còn Board Status.

### Probe/Card

Giữ:

- toàn bộ card slot bật và tắt;
- ProbeContacts song song ở cột phải;
- trạng thái Probe không che Card;
- logic no-delay V12.9.2.

### Main Operation Area

Master và Production dùng chung một vùng lớn:

```text
ĐANG CHỜ LẮP MẪU MASTER ĐẠT
ĐANG KIỂM TRA MẪU MASTER ĐẠT
MASTER ĐẠT - PASS / ĐANG ĐẨY MẪU RA
ĐANG CHỜ LẮP MẪU SAI DÂY
ĐANG KIỂM TRA MẪU SAI DÂY
LỖI MASTER 1/2
MASTER LỖI OK 2/2
SẴN SÀNG SẢN XUẤT
PASS
DÂY CHƯA KẾT NỐI
ĐẤU SAI
CHẬP MẠCH
```

Font title trung tâm 44 px; Master progress 30 px.

---

## 14. Danh sách lỗi Master

`MasterFaults` là collection riêng.

Mỗi fault unique được render thành một dòng đỏ:

```text
LỖI MASTER 1/2 | DÂY CHƯA KẾT NỐI | C01-PIN1 / IO1 ↔ C03-PIN7 / IO7
```

Wrong Wiring có:

```text
Mong đợi: ...
Thực tế: ...
```

Không gộp tất cả fault thành một chuỗi duy nhất.

---

## 15. Board status

Đã bỏ binding/hiển thị `HardwareStatus` khỏi `TestWindow.xaml`.

Property/service chẩn đoán vẫn còn trong ViewModel để MainWindow/Diagnostics/Log có thể dùng; chỉ không chiếm diện tích TestView vận hành.

---

## 16. Bottom toolbar

Giữ thiết kế overlay/hot-zone V12.9.2:

- không có row cố định giữ chiều cao;
- ẩn hoàn toàn khi không sử dụng;
- hot zone ở đáy;
- animation cũ không bị phá.

Đồng thời đã xóa nút:

```text
XÁC NHẬN PASS + RELAY
```

khỏi bottom toolbar Production để không tạo manual path cạnh workflow tự động.

Relay thủ công vẫn nằm trong tab diagnostic riêng.

---

## 17. Probe/Card regression

Các điểm V12.9.2 vẫn giữ:

- `Cards` chứa toàn bộ card slot, kể cả Enabled/Disabled;
- Probe activity chỉ highlight card, không đổi Enabled;
- ProbeContacts là collection riêng;
- không có `InlineProbeQuarantineMs=700`;
- không có `ProbeRelayLockoutMs=1500`;
- `ProbeRelayReleaseDebounceMs = 40` chỉ là relay interlock ngắn, không giữ UI;
- decoder vẫn nhận release trực tiếp;
- Probe không đi vào Production fault/statistics/history.

---

## 18. Log Master

Đã có các event:

```text
MASTER GOOD START
MASTER GOOD PASS
MASTER GOOD EJECT
MASTER BAD START
MASTER BAD FAULT 1/N
MASTER BAD FAULT N/N
MASTER BAD PASS
MASTER BAD EJECT
MASTER VALIDATION COMPLETED
```

Các log này đi vào Test log, không giả thành Production FAIL.

---

## 19. Version

`Version.props` đã đồng bộ:

```text
VersionPrefix         12.9.5
Version               12.9.5
AssemblyVersion       12.9.5.0
FileVersion           12.9.5.0
InformationalVersion  12.9.5
VersionFileTag        12_9_5
AssemblyTitle         JBZUniversalTester V12.9.5
```

`AssemblyInfo.cs`:

```text
ReleaseFamily = V12.9.5
```

---

## 20. Static validation

Kết quả structural/static validation:

```text
62 / 62 PASS
0 FAIL
```

Bao gồm:

- XML/XAML parse;
- version synchronization;
- C# structural balance cho các file sửa;
- manual Master path removed;
- state machine present;
- per-model config save/load;
- HashSet duplicate protection;
- wiring-only Bad Master filter;
- Good Master continuity/resistance completion;
- Master không Record production;
- Bad Master no marking;
- TestView no Board Status/manual Master;
- main operation area lớn nhất;
- red Master fault rows;
- Probe/Card regression;
- bottom toolbar overlay;
- Master log events.

---

## 21. Giới hạn kiểm tra trong môi trường hiện tại

Môi trường sandbox hiện tại **không có `dotnet`, MSBuild hoặc C# compiler**, nên không thể khẳng định đã chạy build WPF thật.

Không giả lập kết quả phần cứng.

Các bài sau phải chạy trên máy tester Windows:

1. `dotnet restore/build/publish`;
2. FTDI thực;
3. Relay/JIG thực;
4. Keysight thực;
5. mẫu Good Master;
6. Bad Master 1/2;
7. cùng Fault A lặp nhiều frame vẫn 1/2;
8. Fault B xuất hiện thành 2/2;
9. eject và release thật;
10. xác nhận Production mở sau Master;
11. DPI 100/125/150%;
12. Probe/Card thực.

Đã tạo:

```text
VERIFY_BUILD_V12_9_5.cmd
```

để restore → build Release win-x86 → publish one-file trên Windows có .NET 8 SDK.
