# BÁO CÁO YÊU CẦU SỬA LỖI `JBZUniversalTester_V12_9_5`
## Lỗi WPF Binding read-only property `PartNumber` + rà soát toàn bộ TestView binding

---

# 1. LỖI HIỆN TẠI

Phiên bản:

```text
JBZUniversalTester_V12_9_5
```

đang bị lỗi runtime:

```text
System.InvalidOperationException

A TwoWay or OneWayToSource binding cannot work on
the read-only property 'PartNumber'
of type 'JBZUniversalTester.ViewModels.TestViewModel'.
```

Stack trace:

```text
MS.Internal.Data.PropertyPathWorker.CheckReadOnly
MS.Internal.Data.PropertyPathWorker.ReplaceItem
MS.Internal.Data.ClrBindingWorker.AttachDataItem
System.Windows.Data.BindingExpression.Activate
...
```

---

# 2. NGUYÊN NHÂN GẦN NHẤT

Trong `TestViewModel`, property:

```csharp
PartNumber
```

đang là property chỉ đọc, ví dụ:

```csharp
public string PartNumber => _something.PartNumber;
```

hoặc:

```csharp
public string PartNumber { get; }
```

không có:

```csharp
set;
```

Nhưng trong XAML đang tồn tại binding kiểu:

```xml
Text="{Binding PartNumber, Mode=TwoWay}"
```

hoặc:

```xml
Text="{Binding PartNumber}"
```

trên `TextBox`, trong khi binding mặc định của `TextBox.Text` có thể ghi ngược source tùy cấu hình/style.

Khi WPF cố update source:

```text
UI → PartNumber
```

thì property không có setter nên ném:

```text
InvalidOperationException
```

---

# 3. YÊU CẦU SỬA ĐÚNG CHO `PartNumber`

Nếu `PartNumber` chỉ dùng để HIỂN THỊ thông tin model/THT và người vận hành không được sửa trực tiếp trên TestView, phải bind:

```xml
Text="{Binding PartNumber, Mode=OneWay}"
```

Nếu dùng `TextBox`, nên:

```xml
<TextBox
    Text="{Binding PartNumber, Mode=OneWay}"
    IsReadOnly="True"
    IsReadOnlyCaretVisible="False"/>
```

Hoặc tốt hơn, nếu không cần giao diện giống ô nhập liệu:

```xml
<TextBlock
    Text="{Binding PartNumber, Mode=OneWay}"/>
```

Không thêm setter giả vào `PartNumber` chỉ để làm WPF hết lỗi nếu field này vốn không được phép chỉnh sửa.

---

# 4. KHÔNG ĐƯỢC CHỈ SỬA MỖI `PartNumber`

Phải audit toàn bộ `TestView.xaml` và các UserControl liên quan.

Tìm tất cả binding:

```text
Mode=TwoWay
Mode=OneWayToSource
UpdateSourceTrigger=PropertyChanged
```

đặc biệt trên:

```text
TextBox.Text
ComboBox.SelectedItem
CheckBox.IsChecked
Numeric field
```

Sau đó đối chiếu từng property trong:

```text
TestViewModel
MainViewModel
ProductionViewModel
Master-related ViewModel
Probe-related ViewModel
```

xem property đó:

```text
có setter hay không
```

---

# 5. CÁC PROPERTY CẦN KIỂM TRA NGAY

Đặc biệt kiểm tra nhóm thông tin đang hiển thị ở đầu TestView:

```text
PartNumber
PartName
ProductName
VehicleType
CustomerCode
CustomerPartNumber
ModelName
ModelFile
LotNo
VersionText
BoardStatus
CurrentDateTime
```

Tên thực tế trong source có thể khác.

Nếu property chỉ lấy từ model/THT/config và không cho người vận hành sửa trực tiếp trên TestView:

```text
Mode = OneWay
```

---

# 6. NHÓM FIELD THÔNG TIN SẢN PHẨM PHẢI LÀ READ-ONLY

Các field:

```text
Mã hàng
Mã sản phẩm
Tên sản phẩm
Loại xe
Mã KH
Model
```

nên được xem là dữ liệu hiển thị từ model đã chọn.

Không nên để người vận hành sửa chúng trực tiếp ở TestView.

Vì vậy XAML nên thống nhất:

```xml
Text="{Binding ..., Mode=OneWay}"
```

và nếu dùng `TextBox`:

```xml
IsReadOnly="True"
```

---

# 7. FIELD NÀO THỰC SỰ CẦN EDIT THÌ VIEWMODEL PHẢI CÓ SETTER

Ví dụ:

```text
LOT
Settings editable
Manual input
```

nếu được phép sửa từ UI thì property phải đúng MVVM:

```csharp
private string _lotNo;

public string LotNo
{
    get => _lotNo;
    set
    {
        if (_lotNo == value)
            return;

        _lotNo = value;
        OnPropertyChanged();
    }
}
```

XAML:

```xml
Text="{Binding LotNo,
       Mode=TwoWay,
       UpdateSourceTrigger=PropertyChanged}"
```

Không áp dụng `TwoWay` cho read-only property.

---

# 8. KIỂM TRA STYLE DÙNG CHUNG

Phải kiểm tra các style như:

```text
ReadOnlyFieldStyle
InfoFieldStyle
ProductFieldStyle
CompactInfoTextBoxStyle
```

Có thể style đang đặt:

```xml
<Setter Property="TextBox.Text" .../>
```

hoặc binding mặc định TwoWay trong template.

Không được chỉ sửa tại một TextBox nếu style chung vẫn ép TwoWay.

---

# 9. KIỂM TRA `UpdateSourceTrigger`

Các binding hiển thị-only không cần:

```xml
UpdateSourceTrigger=PropertyChanged
```

Ví dụ sai:

```xml
Text="{Binding PartNumber,
       Mode=TwoWay,
       UpdateSourceTrigger=PropertyChanged}"
```

Phải sửa thành:

```xml
Text="{Binding PartNumber, Mode=OneWay}"
```

---

# 10. KHÔNG THÊM SETTER VÔ NGHĨA

Không sửa kiểu:

```csharp
public string PartNumber
{
    get => ...;
    set { }
}
```

hoặc:

```csharp
set => _ = value;
```

chỉ để WPF không crash.

Đây là sửa sai kiến trúc.

Nếu field read-only:

```text
XAML phải OneWay
```

---

# 11. PROPERTY COMPUTED CŨNG PHẢI `OneWay`

Ví dụ:

```csharp
public string PartNumber => CurrentModel?.PartNumber ?? "";
public string VehicleType => CurrentModel?.VehicleType ?? "";
```

đây là computed property.

XAML bắt buộc:

```xml
Mode=OneWay
```

Khi `CurrentModel` thay đổi, ViewModel phải raise:

```csharp
OnPropertyChanged(nameof(PartNumber));
OnPropertyChanged(nameof(VehicleType));
```

---

# 12. KIỂM TRA CẬP NHẬT KHI ĐỔI MODEL

Sau khi sửa OneWay, phải bảo đảm UI vẫn cập nhật khi chọn mã hàng mới.

Ví dụ:

```csharp
CurrentModel = model;

OnPropertyChanged(nameof(PartNumber));
OnPropertyChanged(nameof(PartName));
OnPropertyChanged(nameof(VehicleType));
OnPropertyChanged(nameof(CustomerCode));
```

Không được vì đổi sang `OneWay` mà UI không refresh.

---

# 13. NÊN TẠO STYLE READ-ONLY CHUNG

Khuyến nghị tạo style thống nhất:

```xml
<Style x:Key="ReadOnlyInfoTextBoxStyle"
       TargetType="TextBox">
    <Setter Property="IsReadOnly" Value="True"/>
    <Setter Property="IsTabStop" Value="False"/>
    <Setter Property="Focusable" Value="False"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
</Style>
```

Binding vẫn phải ghi rõ:

```xml
Mode=OneWay
```

Style không thay thế BindingMode.

---

# 14. NẾU FIELD CHỈ HIỂN THỊ THÌ ƯU TIÊN `TextBlock`

Nếu không cần:
- copy text;
- selection;
- border kiểu input;

thì nên dùng:

```xml
<TextBlock Text="{Binding PartNumber}"/>
```

để tránh nhầm field đọc-only với field nhập liệu.

Nếu cần ô có viền, có thể:

```xml
<Border>
    <TextBlock .../>
</Border>
```

thay vì dùng `TextBox` editable.

---

# 15. PHẢI SEARCH TOÀN PROJECT

Search các chuỗi:

```text
PartNumber
Mode=TwoWay
OneWayToSource
UpdateSourceTrigger=PropertyChanged
TextBox
ReadOnlyFieldStyle
```

Không chỉ trong:

```text
TestView.xaml
```

mà cả:

```text
MainWindow.xaml
SettingsView.xaml
HistoryView.xaml
Master-related views
UserControls
ResourceDictionary
Styles.xaml
```

---

# 16. PHẢI KIỂM TRA CẢ BINDING TRONG DATATEMPLATE

Đặc biệt:

```xml
<DataTemplate>
    <TextBox Text="{Binding ...}"/>
</DataTemplate>
```

vì lỗi binding có thể phát sinh muộn khi template được render.

---

# 17. PHẢI KIỂM TRA BINDING TRONG STYLE/CONTROLTEMPLATE

Search:

```xml
<Setter Property="Text" .../>
<Binding ... Mode="TwoWay"/>
```

trong:

```text
App.xaml
Styles.xaml
Themes
ResourceDictionary
```

---

# 18. TEST BẮT BUỘC SAU SỬA

## Test 1 – Khởi động

```text
Start app
```

Expected:

```text
không còn InvalidOperationException
```

---

## Test 2 – Mở TestView

Expected:
- `PartNumber` hiển thị đúng;
- `PartName` hiển thị đúng;
- `VehicleType` hiển thị đúng;
- không binding exception.

---

## Test 3 – Đổi mã hàng

Chọn model A → model B.

Expected:

```text
PartNumber
PartName
VehicleType
CustomerCode
```

cập nhật đúng.

Không cần người dùng sửa các field này.

---

## Test 4 – Kiểm tra Output Window

Visual Studio Output không được có:

```text
System.Windows.Data Error
TwoWay binding cannot work on read-only property
```

---

## Test 5 – TestView full workflow

Sau khi mở được TestView:

```text
Master auto
Probe
Production
History
Settings
```

phải tiếp tục hoạt động.

---

# 19. AUDIT CẢ CÁC PROPERTY READ-ONLY KHÁC

Không được chờ chương trình crash lần lượt từng property.

Phải viết script hoặc kiểm tra source để phát hiện các cặp:

```text
XAML TwoWay binding
↕
C# property không có setter
```

và sửa toàn bộ trong một lần.

---

# 20. KHÔNG ĐƯỢC PHÁ GIAO DIỆN V12.9.5

Sau sửa binding phải giữ nguyên các yêu cầu UI đã chốt:

- TestView vùng vận hành lớn;
- thông tin sản phẩm gọn;
- ngày giờ lớn/đậm;
- không mất chữ;
- không mất viền;
- Probe/Card hoạt động;
- Master tự động;
- lỗi hiển thị rõ;
- toolbar dưới auto-hide.

Không rollback giao diện chỉ để sửa binding.

---

# 21. KHÔNG ĐƯỢC PHÁ MASTER AUTO

V12.9.5 đang theo hướng:

```text
Chọn mã hàng
→ chờ Master đạt
→ test Master đạt
→ relay
→ chờ Master lỗi
→ đủ N/N lỗi
→ relay
→ Production
```

Sửa binding không được quay lại cơ chế manual Master.

---

# 22. KHÔNG ĐƯỢC PHÁ PROBE/CARD

Sửa TestView binding nhưng phải giữ:

```text
Probe phản hồi ngay
Card mở rộng động
Card active/inactive
Probe không vào fault engine
```

---

# 23. KẾT QUẢ SỬA MONG MUỐN

Root cause phải được sửa theo đúng MVVM:

```text
READ-ONLY VIEWMODEL PROPERTY
           ↓
       OneWay Binding
```

và:

```text
EDITABLE VIEWMODEL PROPERTY
           ↓
setter + PropertyChanged
           ↓
       TwoWay Binding
```

Không trộn hai loại.

---

# 24. BÁO CÁO SAU KHI SỬA PHẢI GHI RÕ

```text
1. File XAML nào gây lỗi.
2. Binding chính xác nào đang bind PartNumber TwoWay.
3. PartNumber trong TestViewModel được khai báo thế nào.
4. Đã sửa Mode thành gì.
5. Có đổi TextBox → TextBlock hay không.
6. Danh sách các property read-only khác đã audit.
7. Danh sách các binding TwoWay đã sửa.
8. Các property thực sự editable vẫn giữ TwoWay ở đâu.
9. Kết quả build.
10. Kết quả chạy TestView.
11. Visual Studio Output còn Binding Error hay không.
```

---

# 25. TIÊU CHÍ NGHIỆM THU

Bản sửa chỉ đạt khi:

```text
- App khởi động bình thường.
- TestView mở bình thường.
- Không còn InvalidOperationException PartNumber.
- Không còn lỗi tương tự ở PartName/VehicleType/CustomerCode.
- Không còn WPF Binding Error liên quan read-only property.
- Đổi model làm UI cập nhật đúng.
- Các field read-only không thể bị chỉnh nhầm từ TestView.
- Các field editable thật vẫn lưu được bình thường.
- Master/Probe/Card/Production không bị ảnh hưởng.
```

---

# 26. ĐỊNH HƯỚNG SỬA NGAY

Nếu hiện tại đang có đoạn:

```xml
<TextBox Text="{Binding PartNumber,
                        Mode=TwoWay,
                        UpdateSourceTrigger=PropertyChanged}"/>
```

sửa thành:

```xml
<TextBox Text="{Binding PartNumber, Mode=OneWay}"
         IsReadOnly="True"/>
```

hoặc:

```xml
<TextBlock Text="{Binding PartNumber, Mode=OneWay}"/>
```

Sau đó thực hiện audit tương tự cho toàn bộ các field thông tin sản phẩm.

---

# 27. MỤC TIÊU CUỐI

Không chỉ làm chương trình “hết crash”.

Phải làm đúng bản chất:

```text
TestView = màn hình vận hành / hiển thị
Settings = nơi chỉnh cấu hình
Model/THT = nguồn dữ liệu sản phẩm
```

Những thông tin như:

```text
Mã hàng
PartNumber
Tên sản phẩm
Loại xe
Mã KH
```

được TestView **đọc và hiển thị**, không ghi ngược vào `TestViewModel`.

Đây là nguyên tắc cần áp dụng thống nhất cho toàn bộ V12.9.5.
