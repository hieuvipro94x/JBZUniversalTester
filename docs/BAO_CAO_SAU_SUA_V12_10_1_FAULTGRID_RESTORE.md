# BÁO CÁO SAU SỬA `JBZUniversalTester V12.10.1`
## Khôi phục TestView compact + mở rộng FaultGrid + giữ nguyên Master Auto

## 1. Phiên bản

- Phiên bản nguồn trước sửa: `V12.9.5 READONLY_BINDING_FIX`.
- Phiên bản sau sửa: `V12.10.1`.
- `Version`: `12.10.1`.
- `AssemblyVersion/FileVersion`: `12.10.1.0`.
- `VersionFileTag`: `12_10_1`.
- Assembly build theo cấu hình hiện tại: `JBZUniversalTester_V12_10_1`.

## 2. Backup trước khi chỉnh giao diện

Đã tạo trước khi sửa:

```text
Backup/TestWindow_V12_9_5_before_faultgrid_restore.xaml
Backup/TestViewModel_V12_9_5_before_faultgrid_restore.cs
```

Nhờ vậy có thể đối chiếu/rollback riêng layout nếu cần mà không mất logic hiện tại.

## 3. Nguyên nhân FaultGrid bị nhỏ

`Views/TestWindow.xaml` trước sửa chia root layout:

```text
Header       = 128 px
Probe/Card   = Auto
Master area  = 3*
Tab/Fault    = 2*
```

Bên trong Master area lại có hai row `*` quanh các TextBlock `ActiveFaultTitle`, `ActiveFaultMessage`, `MasterProgressText`, vì vậy khối trạng thái vàng lấy phần lớn chiều cao màn hình.

Ngoài ra `FaultGrid` còn có `DataTrigger`:

```text
IsMasterBadPhase=True -> Visibility=Collapsed
```

nên đúng lúc kiểm Master lỗi, DataGrid chính lại bị ẩn và một `ItemsControl MasterFaults` riêng được dùng thay thế.

## 4. Layout TestView V12.10.1

Root layout mới:

```text
Header       = 128 px
Probe/Card   = Auto
Status strip = 68 px
Tab/Fault    = *
```

Chỉ `TabControl/FaultGrid` nhận chiều cao `*` chính.

Không còn panel Master lớn hàng trăm pixel và không còn `FontSize=44` trong TestView.

## 5. Status Master/Production mới

Master Auto vẫn giữ nguyên logic, chỉ thay cách hiển thị.

Status hiện nằm trong strip cao `68 px`:

```text
[ ActiveFaultTitle ]  [ ActiveFaultMessage ]  [ MASTER N/N ]
```

Thông số chính:

```text
ActiveFaultTitle   FontSize 26 Bold
ActiveFaultMessage FontSize 16 SemiBold
MasterProgress     FontSize 20 Bold
```

Các trạng thái Master có newline được normalize thành `•` ở `ActiveFaultTitle` để không làm strip tự cao lên.

## 6. FaultGrid trở thành vùng lớn nhất

`TabControl` và tab `DANH SÁCH LỖI / MẠNG I/O` đã được đặt:

```text
HorizontalAlignment = Stretch
VerticalAlignment   = Stretch
```

`FaultGrid` cũng stretch toàn bộ.

Đã bỏ hoàn toàn trigger làm:

```text
MasterBad -> FaultGrid Collapsed
```

Vì vậy FaultGrid luôn hiển thị trong:

```text
MasterBad
Production
```

## 7. Master Bad dùng cùng FaultGrid với Production

Đã xóa `ItemsControl ItemsSource={Binding MasterFaults}` khỏi XAML của tab chính.

MasterBad không còn một panel danh sách riêng. Các fault Master đã xác nhận được đưa vào cùng collection `Faults`, là nguồn của `FaultGrid`.

## 8. Chống duplicate fault Master trong DataGrid

Logic đếm Master trước đó đã có:

```csharp
HashSet<MasterFaultKey>
```

V12.10.1 giữ nguyên và bổ sung:

```csharp
Dictionary<MasterFaultKey, FaultDetail> _masterDetectedFaultDetails
```

Quy trình:

```text
fault live
  -> MasterFaultKey
  -> HashSet.Add(key)
      false: bỏ qua frame lặp
      true : lưu FaultDetail unique
  -> dựng 1 FaultRow
  -> cập nhật FaultGrid
```

Do đó:

```text
IO1 ↔ IO7 lặp 100 frame
```

vẫn chỉ là:

```text
MasterDetectedFaultCount = 1
FaultGrid = 1 dòng
```

## 9. Không làm mất OpenCircuit mới

Đây là phần được xử lý riêng để tránh regression.

Trong MasterBad, `Faults` giờ là snapshot unique để HIỂN THỊ. Nếu `CaptureFaultDetails()` tiếp tục chỉ đọc `Faults`, các Open mới từ engine có thể không được nhìn thấy.

V12.10.1 tách hai nguồn:

```text
Nguồn xác nhận live: TestEngine.BuildRows()
Nguồn hiển thị:       _masterDetectedFaultDetails unique
```

Nhờ vậy:

- Open mới vẫn được phát hiện;
- Wrong Wiring/Short vẫn được phát hiện;
- DataGrid không duplicate.

## 10. RowKey được tăng độ chính xác

`SynchronizeFaultRows()` trước đây nhận diện row chủ yếu bằng:

```text
Kind + IO + Connector + Pin + Wire + Splice
```

V12.10.1 bổ sung:

```text
ExpectedSourceIo
ExpectedTargetIo
ActualSourceIo
ActualTargetIo
```

để hai lỗi Wrong Wiring khác nhau không bị merge nhầm chỉ vì cùng source/pin.

## 11. Khả năng đọc DataGrid

Giữ:

```text
FontSize = 15
ColumnHeaderHeight = 40
```

và chỉnh `ItemHeight` thành:

```csharp
Math.Clamp(ItemHeight, 30, 44)
```

nên row vận hành không còn bị cấu hình xuống dưới 30 px.

Các tỷ lệ cột vẫn giữ:

```text
Loại lỗi
I/O
Giắc
Chân
Tên dây
Dây dập nối
Tiết diện
Màu dây
Trạng thái
```

## 12. Giữ nguyên header và Probe/Card

Không rollback header compact V12.9.5.

Vẫn giữ:

- Mã hàng / Sản phẩm / Loại xe / Mã KH / LOT;
- ngày giờ lớn/đậm;
- statistics compact;
- toàn bộ Cards active/inactive;
- ProbeContacts song song;
- hướng dẫn Probe bên phải;
- phản hồi Probe không TTL dài.

## 13. Giữ nguyên Bottom Toolbar

Không thay logic toolbar V12.9.2:

- overlay đáy;
- hot zone 24 px;
- auto hide;
- không giữ row chiều cao;
- không rollback animation/hit test.

## 14. Giữ nguyên fix ReadOnly Binding

Các field sau vẫn là `Mode=OneWay`:

```text
PartNumber
ProductName
VehicleType
CustomerCode
Lot
```

Không thêm setter giả và không rollback bản sửa runtime `PartNumber`.

## 15. Giữ nguyên Master Auto

Không khôi phục bất kỳ command Master thủ công nào.

Vẫn giữ state machine:

```text
WaitingGoodMaster
TestingGoodMaster
EjectingGoodMaster
WaitingBadMaster
TestingBadMaster
EjectingBadMaster
Completed
```

Master không đi qua `RecordCompletedProduct`, nên không cộng LOT/Pass/Fail Production.

## 16. File chính đã sửa/thêm

### Sửa

```text
Views/TestWindow.xaml
ViewModels/TestViewModel.cs
Version.props
Properties/AssemblyInfo.cs
JBZUniversalTester.csproj (comment version)
```

### Thêm

```text
Backup/TestWindow_V12_9_5_before_faultgrid_restore.xaml
Backup/TestViewModel_V12_9_5_before_faultgrid_restore.cs
Scripts/Validate-V12.10.1.ps1
VERIFY_BUILD_V12_10_1.cmd
V12_10_1_CHANGELOG.txt
docs/REQUEST_V12_10_1_FAULTGRID_RESTORE.md
docs/V12_10_1_STATIC_VALIDATION.txt
```

## 17. Static validation

Đã chạy kiểm tra tĩnh trong môi trường sửa source:

```text
66/66 PASS
0 FAIL
```

Bao gồm:

- parse toàn bộ XAML/XML;
- version 12.10.1 đồng bộ;
- backup tồn tại;
- không còn root `3* / 2*` cũ;
- status strip 68 px;
- không còn `FontSize=44`;
- TabControl/FaultGrid stretch;
- FaultGrid không collapse MasterBad;
- không còn MasterFaults ItemsControl riêng;
- HashSet + Dictionary unique Master;
- live engine source vẫn được dùng cho Open;
- RowKey có expected/actual;
- binding OneWay cũ còn nguyên;
- Probe/Card/Bottom Toolbar còn nguyên;
- không có Master manual command;
- brace balance các file C# chính bằng 0.

## 18. Build/runtime

Sandbox hiện tại không cài `.NET SDK/MSBuild`, do đó không thể trung thực xác nhận:

```text
dotnet restore
dotnet build
WPF runtime
Visual Studio Output
DPI thực tế
FTDI hardware runtime
```

Đã thêm:

```text
VERIFY_BUILD_V12_10_1.cmd
```

Trên Windows có .NET 8 SDK, script chạy theo thứ tự:

```text
1. Validate-V12.10.1.ps1
2. Audit-ReadOnlyBindings.ps1
3. dotnet --version
4. restore win-x86
5. build Release win-x86
6. publish one-file
```

## 19. Kết quả theo tiêu chí báo cáo

| Yêu cầu | Kết quả |
|---|---|
| Không còn panel Master lớn | Đạt |
| TestView quay về hướng giao diện cũ | Đạt |
| DataGrid là vùng lớn nhất | Đạt |
| Tab lỗi dùng chiều cao còn lại | Đạt |
| MasterBad không collapse FaultGrid | Đạt |
| Master fault hiển thị cùng FaultGrid | Đạt |
| Không duplicate cùng Master fault | Đạt ở source/static logic |
| Status Master chỉ là strip nhỏ | Đạt, 68 px |
| Header/Probe/Card vẫn compact | Đạt |
| Bottom toolbar không chiếm row | Giữ nguyên |
| Master Auto không rollback | Đạt |
| ReadOnly PartNumber fix không rollback | Đạt |
| Build/runtime Windows | Chưa xác nhận do sandbox thiếu SDK |

## 20. Kết luận

V12.10.1 chuyển trọng tâm TestView từ khối Master lớn sang đúng vùng vận hành cần thiết:

```text
Header        : gọn
Probe/Card    : gọn
Status        : 68 px
FaultGrid     : phần lớn màn hình
Toolbar       : overlay auto-hide
```

Master Auto vẫn chạy như V12.9.5, nhưng trạng thái chỉ nằm ở strip nhỏ. Khi Master Bad, từng lỗi unique được đưa trực tiếp vào chính `FaultGrid`, đồng thời nguồn engine live vẫn tiếp tục được dùng để phát hiện lỗi mới.
