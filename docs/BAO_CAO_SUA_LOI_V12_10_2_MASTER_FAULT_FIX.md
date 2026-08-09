# BÁO CÁO SỬA LỖI `JBZUniversalTester V12.10.2`
## Sửa duplicate Master fault + gia cố FaultGrid, giữ nguyên bố cục V12.10.1

## 1. Kết luận audit bản V12.10.1

Đã đọc và đối chiếu:

- `docs/REQUEST_V12_10_1_FAULTGRID_RESTORE.md`
- `docs/BAO_CAO_SAU_SUA_V12_10_1_FAULTGRID_RESTORE.md`
- `docs/V12_10_1_STATIC_VALIDATION.txt`
- `Views/TestWindow.xaml`
- `ViewModels/TestViewModel.cs`
- `Models/MasterModels.cs`
- `Services/TestEngine.cs`
- code-behind và resource của toàn bộ `Views/*.xaml`.

Bố cục FaultGrid của V12.10.1 đúng hướng yêu cầu: không còn panel Master lớn,
status chỉ còn strip 68 px và FaultGrid nhận chiều cao `*` chính.

Tuy nhiên validation 66/66 của V12.10.1 chỉ là kiểm tra tĩnh theo chuỗi và không
build C#. Có một rủi ro logic thực tế ở Master fault key: cùng một cạnh điện có
thể xuất hiện theo hai hướng frame khác nhau, hoặc một hướng được phân loại
`WrongWiring` còn hướng kia là `ShortCircuit`. Khi đó cùng một lỗi vật lý có thể
trở thành hai key và làm `MasterDetectedFaultCount` tăng sai.

## 2. Sửa duplicate theo cạnh điện vật lý

V12.10.2 sửa `MasterFaultKey.From()` theo nguyên tắc:

```text
một cạnh điện vật lý IO-A <-> IO-B = một lỗi Master
```

Với `WrongWiring` và `ShortCircuit`:

- ưu tiên `ActualSourceIo/ActualTargetIo`;
- fallback sang `RelatedIos` nếu metadata thiếu;
- chuẩn hóa `A-B` và `B-A` thành cùng một cặp;
- dùng key canonical chung cho Wrong/Short của cùng cạnh vật lý.

Ví dụ:

```text
Frame 1: WrongWiring IO1 -> IO7
Frame 2: ShortCircuit IO7 -> IO1
```

V12.10.1 có khả năng coi là hai lỗi khác nhau.
V12.10.2 chỉ tính:

```text
MASTER = 1 lỗi
FaultGrid = 1 dòng
```

## 3. Không tăng count khi detail tốt hơn xuất hiện

Nếu lỗi đã được đếm nhưng frame sau có thêm `ExpectedSourceIo`,
`ExpectedTargetIo`, connector/pin/wire rõ hơn, V12.10.2:

- không thêm key mới;
- không tăng `MasterDetectedFaultCount`;
- thay detail đang hiển thị bằng detail chất lượng cao hơn;
- đồng bộ lại FaultGrid.

Do đó progress không thể nhảy giả từ `1/2` lên `2/2` chỉ vì cùng một lỗi được
engine mô tả tốt hơn ở frame sau.

## 4. Ổn định thứ tự FaultGrid Master

`BuildMasterFaultGridRows()` được sắp xếp theo:

1. priority loại lỗi;
2. source/primary IO;
3. target IO.

Mục tiêu là tránh các dòng Master đổi vị trí khó theo dõi giữa các lần refresh.

## 5. Header DataGrid

Bổ sung `ColumnHeaderStyle`:

```text
FontSize = 15
FontWeight = Bold
HorizontalContentAlignment = Center
VerticalContentAlignment = Center
```

Đáp ứng yêu cầu header phải rõ khi vận hành từ xa.

## 6. Những phần giữ nguyên

Không rollback:

- Master Auto state machine;
- Probe chạy song song;
- Cards mở rộng;
- Production PASS/FAIL;
- FTDI/D2XX protocol;
- Keysight/Resistance;
- History/LOT/Statistics;
- ReadOnly `PartNumber/ProductName/VehicleType/CustomerCode/Lot` OneWay;
- Bottom toolbar overlay auto-hide;
- TestView compact của V12.10.1.

## 7. Backup

Đã tạo:

```text
Backup/TestWindow_V12_10_1_before_master_fault_fix.xaml
Backup/TestViewModel_V12_10_1_before_master_fault_fix.cs
Backup/MasterModels_V12_10_1_before_master_fault_fix.cs
```

## 8. Static audit

Audit mở rộng trong sandbox:

```text
79/79 PASS
0 FAIL
```

Bao gồm parse toàn bộ XAML, class/event handler, StaticResource, version,
ReadOnly binding, layout FaultGrid, Master key, snapshot, RowKey, brace balance
và mô phỏng các ca duplicate A-B/B-A.

## 9. Phần chưa thể xác nhận trong sandbox

Môi trường hiện tại không có `.NET SDK/MSBuild`, nên chưa thể tuyên bố:

```text
dotnet restore/build WPF = PASS
NuGet restore = PASS
ftd2xx.dll runtime = PASS
visa32.dll runtime = PASS
hardware FTDI/Keysight = PASS
```

Project có P/Invoke tới:

```text
ftd2xx.dll
visa32.dll
winspool.drv
```

`winspool.drv` là Windows component; `ftd2xx.dll` và `visa32.dll` cần đúng bản
32-bit tương ứng vì project đang build `win-x86`.

Đã thêm `VERIFY_BUILD_V12_10_2.cmd` để chạy static validation, binding audit,
restore, build Release win-x86 và publish one-file trên máy Windows có .NET 8 SDK.

## 10. Phiên bản

```text
Version              12.10.2
AssemblyVersion      12.10.2.0
FileVersion          12.10.2.0
InformationalVersion 12.10.2
VersionFileTag       12_10_2
ReleaseFamily        V12.10.2
```
