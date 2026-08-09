# BÁO CÁO SAU SỬA `JBZUniversalTester_V12_9_5`
## Sửa WPF read-only binding `PartNumber` + audit toàn bộ binding TestView

## 1. File XAML gây lỗi

Lỗi nằm tại:

```text
Views/TestWindow.xaml
```

Các `TextBox` thông tin sản phẩm dùng `ReadOnlyFieldStyle` nhưng binding `Text` không ghi `Mode` rõ ràng.

## 2. Binding chính xác gây lỗi PartNumber

Trước sửa:

```xml
<TextBox Grid.Column="1"
         Text="{Binding PartNumber}"
         ToolTip="{Binding PartNumber}"
         Style="{StaticResource ReadOnlyFieldStyle}"/>
```

Mặc dù không viết `Mode=TwoWay`, `TextBox.Text` của WPF mặc định là TwoWay. Vì vậy WPF cố ghi UI ngược vào `PartNumber` và ném `InvalidOperationException`.

Sau sửa:

```xml
<TextBox Grid.Column="1"
         Text="{Binding PartNumber, Mode=OneWay}"
         ToolTip="{Binding PartNumber, Mode=OneWay}"
         Style="{StaticResource ReadOnlyFieldStyle}"/>
```

## 3. `PartNumber` trong `TestViewModel`

Property thực tế là computed read-only:

```csharp
public string PartNumber =>
    _model?.PartNumber ?? string.Empty;
```

Không có setter và **không thêm setter giả** sau sửa.

Các property cùng nhóm cũng là computed read-only:

```csharp
public string ProductName => _model?.ProductName ?? string.Empty;
public string VehicleType => _model?.VehicleType ?? string.Empty;
public string CustomerCode => _model?.CustomerCode ?? string.Empty;
```

`Lot` có setter private:

```csharp
public string Lot
{
    get => _lot;
    private set => Set(ref _lot, value);
}
```

Do TestView chỉ hiển thị LOT, binding của `Lot` cũng được chuyển sang OneWay.

## 4. Binding đã sửa thành gì

Các binding `TextBox.Text` trong header TestView được sửa:

```text
PartNumber   -> Mode=OneWay
ProductName  -> Mode=OneWay
VehicleType  -> Mode=OneWay
CustomerCode -> Mode=OneWay
Lot          -> Mode=OneWay
```

ToolTip của bốn field thông tin sản phẩm cũng ghi rõ `Mode=OneWay`.

## 5. Có đổi TextBox thành TextBlock hay không

Không đổi sang `TextBlock` để giữ nguyên giao diện V12.9.5 đã chốt.

Thay vào đó `ReadOnlyFieldStyle` được tăng bảo vệ:

```xml
<Setter Property="IsReadOnly" Value="True"/>
<Setter Property="IsReadOnlyCaretVisible" Value="False"/>
<Setter Property="IsTabStop" Value="False"/>
<Setter Property="Focusable" Value="False"/>
```

Như vậy field vẫn giữ border/kiểu hiển thị cũ nhưng không bị hiểu nhầm là ô nhập liệu.

## 6. Property read-only khác đã audit

Đã audit nhóm TestView:

```text
ModelName
PartNumber
ProductName
VehicleType
CustomerCode
Eco
Nco
Alc
Lot (private setter)
ItemHeight
ScrollDelay
PageDelay
ShowTitle
ShowConnector
Total/Pass/Fail/Rate và các counter hiển thị
MasterProgressText
ActiveFaultTitle / Message / Expected / Actual
Probe/Card display properties
```

Các property chỉ dùng bởi `TextBlock` không có đường update source. Riêng các field dùng `TextBox` ở header đều đã bắt buộc OneWay.

## 7. Các binding TwoWay đã sửa

Không có chuỗi `Mode=TwoWay` trực tiếp trên `PartNumber` trước sửa. Lỗi là **implicit TwoWay** của `TextBox.Text`.

Đã sửa toàn bộ 5 implicit TextBox bindings ở TestView thành explicit `Mode=OneWay`:

```text
PartNumber
ProductName
VehicleType
CustomerCode
Lot
```

Project không có binding `OneWayToSource` trong XAML sau audit.

## 8. Property thực sự editable vẫn giữ TwoWay

`TestWindow` chỉ còn TwoWay cho:

```text
SelectedOperationTabIndex
```

và property này có public setter.

`ProductionSettingsPage` vẫn giữ TwoWay cho các cấu hình người dùng được phép chỉnh, bao gồm:

```text
ExpansionCardCount
IoConfirm1 / IoConfirmN
UsbDelay
StartCardNumber
PrinterCom
UseTestPointer
MasterFaultRequiredCount
WaterproofSerialPort
LotNo
DeviceName / DeviceNumber
OperatorCompany / ProductionLine
TemperatureTolerance
MinimumErrorLogValue
AutoSaveErrors
Password
StampDelay / OversizeWaitSeconds / ShieldDelay
ResistanceDelayMs
ItemHeight / ScrollDelay / PageDelay
ShowTitle / ShowConnector
Resistance channel Enabled/Name/Channel/MinOhm/MaxOhm
PrinterName / BaudRate / Copies / WriteTimeoutMs
WidthMm / HeightMm / FormatName
AutoPrintLabelOnPass
HistoryDirectory
```

Các target trên đều có setter công khai trong `ProductionSettings`, `LabelSettings`, `ResistanceChannelSetting` hoặc `ProductionSettingsViewModel`.

`Settings.LastThtPath` vẫn là field hiển thị và bind `Mode=OneWay`.

## 9. Cập nhật UI khi đổi model

Đã xác minh `SetModel`/luồng load model tiếp tục gọi:

```csharp
Raise(nameof(ModelName));
Raise(nameof(PartNumber));
Raise(nameof(ProductName));
Raise(nameof(VehicleType));
Raise(nameof(CustomerCode));
```

Do đó chuyển binding sang OneWay **không làm mất khả năng refresh UI khi đổi mã hàng**.

`Lot` dùng `Set(ref _lot, value)` nên tiếp tục phát `PropertyChanged` khi giá trị LOT thay đổi.

## 10. Audit style / DataTemplate / ControlTemplate

Đã search toàn project cho:

```text
Mode=TwoWay
Mode=OneWayToSource
UpdateSourceTrigger=PropertyChanged
<TextBox
<ComboBox
<CheckBox
<Setter Property="Text"
Binding trong DataTemplate/ControlTemplate
```

Kết quả:

- không có `OneWayToSource`;
- không có shared `Setter` chèn binding Text/SelectedItem/SelectedValue/SelectedIndex/IsChecked;
- không còn `TextBox.Text` implicit binding nào trong TestWindow;
- TwoWay trong Settings đều trỏ tới property writable.

Đã thêm script:

```text
Scripts/Audit-ReadOnlyBindings.ps1
```

để chạy lại audit trước build.

## 11. Regression V12.9.5

Không thay đổi logic trong các file chính:

```text
ViewModels/TestViewModel.cs
Services/BoardIoDecoder.cs
Services/D2xxBoardTransport.cs
Services/TestEngine.cs
Models/MasterModels.cs
Views/TestWindow.xaml.cs
Views/ProductionSettingsPage.xaml
ViewModels/ProductionSettingsViewModel.cs
Models/ProductionSettings.cs
```

Do đó không rollback:

```text
Master Auto
Probe phản hồi nhanh
Card mở rộng
Production fault engine
Bottom toolbar auto-hide
```

TestView vẫn không có hàng Master manual và không đưa Board Status trở lại.

## 12. Kết quả static validation

Static validation:

```text
45/45 PASS
```

Bao gồm:

- parse toàn bộ XAML;
- 5 field TestView explicit OneWay;
- ViewModel vẫn read-only đúng kiến trúc;
- PropertyChanged khi đổi model;
- không có OneWayToSource;
- TwoWay TestWindow chỉ còn `SelectedOperationTabIndex`;
- TwoWay Settings đều có setter;
- core Master/Probe/Card/TestEngine không bị thay đổi.

## 13. Kết quả build / chạy TestView / Visual Studio Output

Môi trường sandbox hiện tại **không cài .NET SDK/MSBuild**, nên không thể trung thực xác nhận ba bước runtime sau trong môi trường này:

```text
dotnet build WPF
mở TestView thật
Visual Studio Output không còn System.Windows.Data Error
```

Đã thêm:

```text
VERIFY_BUILD_V12_9_5_READONLY_BINDING_FIX.cmd
```

Script trên Windows sẽ chạy:

```text
Audit-ReadOnlyBindings.ps1
-> dotnet restore
-> dotnet build Release win-x86
-> publish one-file
```

Sau build trên máy Windows, cần chạy đúng 5 test runtime trong báo cáo yêu cầu, đặc biệt mở TestView, đổi Model A -> B và quan sát Visual Studio Output.

## 14. Kết luận

Root cause đã được sửa đúng MVVM:

```text
READ-ONLY VIEWMODEL PROPERTY
        ↓
EXPLICIT OneWay Binding
```

Không thêm setter giả, không biến TestView thành nơi chỉnh dữ liệu model, và không ảnh hưởng workflow Master Auto/Probe/Card/Production của V12.9.5.
