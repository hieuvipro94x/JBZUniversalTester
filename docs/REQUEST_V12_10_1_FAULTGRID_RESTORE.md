# BÁO CÁO YÊU CẦU SỬA GIAO DIỆN `JBZUniversalTester_V12_9_5`
## Khôi phục bố cục TestView cũ – bỏ vùng Master lớn – mở rộng TabItem/FaultGrid làm vùng hiển thị chính

---

# 1. MỤC TIÊU SỬA LẦN NÀY

Phiên bản hiện tại đang hiển thị một vùng trạng thái Master rất lớn ở giữa TestView, ví dụ:

```text
ĐANG CHỜ LẮP MẪU MASTER ĐẠT
MASTER GOOD START • TỰ ĐỘNG KIỂM TRA KHI LẮP MẪU
```

với nền vàng chiếm phần lớn diện tích màn hình.

**Không muốn giữ cách hiển thị này.**

Yêu cầu mới:

1. **Backup / khôi phục bố cục TestView về giao diện cũ** trước khi thêm vùng Master lớn.
2. Không để trạng thái Master chiếm vùng giữa màn hình.
3. Không tạo một panel lớn riêng chỉ để hiện:
   ```text
   ĐANG CHỜ LẮP MẪU MASTER ĐẠT
   ```
4. Vùng phải lớn nhất trong TestView là:
   ```text
   DANH SÁCH LỖI / MẠNG I/O
   ```
5. `DataGrid FaultGrid` phải chiếm phần lớn chiều cao còn lại của màn hình.
6. Các dòng IO/lỗi phải rõ, cao, dễ đọc.
7. `TabItem` chứa danh sách lỗi phải được mở rộng ra tối đa.
8. Chỉ chỉnh phần hiển thị, **không phá logic Master Auto đã làm**.

---

# 2. NHỮNG GÌ CẦN GIỮ NGUYÊN

Phải giữ nguyên các logic đã hoàn thiện:

```text
Master Auto
Probe
Card mở rộng
Production
FTDI
THT
Relay
History
LOT
Statistics
Keysight
PASS/FAIL
```

Không rollback logic.

Chỉ rollback / chỉnh lại:

```text
layout TestView
```

---

# 3. VẤN ĐỀ CỦA GIAO DIỆN HIỆN TẠI

Ảnh hiện tại cho thấy:

```text
┌───────────────────────────────────────────────┐
│ Header / model / counters                     │
├───────────────────────────────────────────────┤
│ Probe / Card                                  │
├───────────────────────────────────────────────┤
│                                               │
│                                               │
│      ĐANG CHỜ LẮP MẪU MASTER ĐẠT              │
│                                               │
│                                               │
│             vùng vàng rất lớn                 │
│                                               │
│                                               │
├───────────────────────────────────────────────┤
│ Tab lỗi / DataGrid                            │
└───────────────────────────────────────────────┘
```

Điều này làm:

- vùng IO/lỗi bị thu nhỏ;
- người vận hành phải nhìn vùng trạng thái lớn nhưng không thấy nhiều chi tiết dây;
- DataGrid chỉ còn khoảng nhỏ phía dưới;
- không đúng mục tiêu vận hành thực tế.

---

# 4. BỐ CỤC MONG MUỐN SAU KHI SỬA

TestView phải quay về gần dạng:

```text
┌───────────────────────────────────────────────┐
│ Header / Model / LOT / Statistics             │
├───────────────────────────────────────────────┤
│ Probe / Card                                  │
├───────────────────────────────────────────────┤
│ Trạng thái test nhỏ gọn                       │
├───────────────────────────────────────────────┤
│                                               │
│                                               │
│                                               │
│       DANH SÁCH LỖI / MẠNG I/O                │
│                                               │
│         DataGrid phải lớn nhất                │
│                                               │
│                                               │
│                                               │
├───────────────────────────────────────────────┤
│ Bottom toolbar auto-hide                      │
└───────────────────────────────────────────────┘
```

**DataGrid phải là vùng chiếm diện tích lớn nhất.**

---

# 5. BỎ VÙNG MASTER LỚN HIỆN TẠI

Đoạn Grid kiểu:

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="*"/>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <TextBlock Grid.Row="0"
               Text="{Binding ActiveFaultTitle}"
               ...
               FontSize="44"
               .../>

    <TextBlock Grid.Row="1"
               Text="{Binding ActiveFaultMessage}"
               FontSize="23"
               .../>

    <TextBlock Grid.Row="2"
               Text="{Binding MasterProgressText}"
               FontSize="30"
               .../>

    <StackPanel Grid.Row="3">
        ...
    </StackPanel>
</Grid>
```

không được đặt trong một panel lớn `*` chiếm phần giữa màn hình như hiện tại.

Không cần một vùng riêng cao hàng trăm pixel.

---

# 6. CÁCH GIỮ TRẠNG THÁI MASTER NHƯNG GỌN

Logic:

```text
ActiveFaultTitle
ActiveFaultMessage
MasterProgressText
```

vẫn giữ.

Nhưng hiển thị ở một thanh trạng thái nhỏ.

Ví dụ:

```text
[ ĐANG CHỜ LẮP MẪU MASTER ĐẠT ]     [ MASTER 0/2 ]
```

hoặc:

```text
ĐANG CHỜ LẮP MẪU MASTER ĐẠT • MASTER 0/2
```

Chiều cao gợi ý:

```text
48–70 px
```

không phải:

```text
400–500 px
```

---

# 7. ACTIVE FAULT VẪN PHẢI RÕ NHƯNG KHÔNG CHIẾM CẢ MÀN HÌNH

Khi có lỗi thật:

```text
DÂY CHƯA KẾT NỐI
IO1 ↔ IO7
```

hoặc:

```text
ĐẤU SAI
IO2 → IO8
```

thì có thể dùng:

```text
FontSize 24–30
FontWeight Bold
```

trong thanh trạng thái.

Không cần `FontSize=44` trong panel lớn.

Chi tiết đầy đủ sẽ nằm ở DataGrid bên dưới.

---

# 8. `FAULTGRID` PHẢI LÀ VÙNG CHÍNH

Giữ cấu trúc:

```xml
<DataGrid x:Name="FaultGrid"
          ItemsSource="{Binding Faults}"
          RowHeight="{Binding ItemHeight}">
```

nhưng phải đặt vào Grid row:

```xml
<RowDefinition Height="*"/>
```

và đây phải là row `*` chính của TestView.

Không được để một row `*` khác của vùng Master tranh chiều cao.

---

# 9. KHÔNG COLLAPSE `FAULTGRID` TRONG MASTER BAD PHASE

Đoạn hiện tại:

```xml
<DataGrid.Style>
    <Style TargetType="DataGrid"
           BasedOn="{StaticResource ProductionGrid}">
        <Setter Property="Visibility" Value="Visible"/>
        <Style.Triggers>
            <DataTrigger Binding="{Binding IsMasterBadPhase}"
                         Value="True">
                <Setter Property="Visibility"
                        Value="Collapsed"/>
            </DataTrigger>
        </Style.Triggers>
    </Style>
</DataGrid.Style>
```

**không phù hợp với yêu cầu mới.**

Khi:

```text
IsMasterBadPhase = True
```

chính là lúc cần nhìn rõ nhất từng lỗi Master.

Do đó:

```text
FaultGrid phải VISIBLE
```

trong Master Bad.

Phải bỏ trigger:

```xml
<DataTrigger Binding="{Binding IsMasterBadPhase}" Value="True">
    <Setter Property="Visibility" Value="Collapsed"/>
</DataTrigger>
```

---

# 10. MASTER BAD PHASE PHẢI HIỂN THỊ LỖI TRỰC TIẾP TRONG `FAULTGRID`

Khi test mẫu Master lỗi:

```text
FaultGrid
```

phải hiển thị:

```text
DÂY CHƯA KẾT NỐI
ĐẤU SAI
CHẬP
```

theo từng dòng.

Ví dụ:

| Loại lỗi | I/O | Giắc | Chân | Tên dây | Dây dập nối | Tiết diện | Màu dây | Trạng thái |
|---|---:|---|---|---|---|---|---|---|
| DÂY CHƯA KẾT NỐI | 1 | C01 | 1 | BG01 | | 0.5 | Đỏ | Chưa kết nối IO1 ↔ IO7 |
| DÂY CHƯA KẾT NỐI | 2 | C02 | 2 | BG02 | | 0.5 | Đen | Chưa kết nối IO2 ↔ IO8 |

Đây mới là vùng chính để người vận hành nhìn.

---

# 11. `MASTERPROGRESSTEXT` CHỈ LÀ TRẠNG THÁI PHỤ

Ví dụ:

```text
MASTER LỖI 1/2
```

không cần vùng riêng lớn.

Có thể đặt ngay bên cạnh trạng thái:

```text
ĐANG TEST MẪU SAI DÂY      MASTER 1/2
```

DataGrid bên dưới mới thể hiện:

```text
lỗi thứ 1 là gì
lỗi thứ 2 là gì
IO nào
PIN nào
Wire nào
```

---

# 12. MỞ RỘNG `TABCONTROL`

Phần:

```text
DANH SÁCH LỖI / MẠNG I/O
ĐO ĐIỆN TRỞ
TEST RELAY
NHẬT KÝ THIẾT BỊ
```

phải dùng gần hết phần còn lại của TestView.

`TabControl` cần:

```xml
VerticalAlignment="Stretch"
HorizontalAlignment="Stretch"
```

và Grid row chứa nó:

```xml
<RowDefinition Height="*"/>
```

Không đặt:

```text
Height cố định thấp
MaxHeight thấp
```

---

# 13. `TABITEM` PHẢI STRETCH TOÀN BỘ

Bên trong:

```xml
<TabItem Header="DANH SÁCH LỖI / MẠNG I/O">
    <Grid>
        ...
    </Grid>
</TabItem>
```

Grid phải:

```text
HorizontalAlignment = Stretch
VerticalAlignment = Stretch
```

DataGrid:

```text
HorizontalAlignment = Stretch
VerticalAlignment = Stretch
```

Không bọc DataGrid trong `StackPanel` theo chiều dọc vì StackPanel không cấp phần chiều cao `*` như Grid.

---

# 14. KHÔNG DÙNG `STACKPANEL` LÀM CONTAINER CHÍNH CHO DATAGRID

Nếu hiện tại TabItem có:

```xml
<StackPanel>
    <DataGrid .../>
</StackPanel>
```

phải đổi sang:

```xml
<Grid>
    <DataGrid .../>
</Grid>
```

để DataGrid tự stretch.

---

# 15. CỘT DATAGRID GIỮ TỶ LỆ HIỆN TẠI NHƯNG MỞ RỘNG TỔNG THỂ

Các cột:

```xml
<DataGridTextColumn Header="Loại lỗi" Width="1.15*"/>
<DataGridTextColumn Header="I/O" Width="0.8*"/>
<DataGridTextColumn Header="Giắc" Width="0.8*"/>
<DataGridTextColumn Header="Chân" Width="0.75*"/>
<DataGridTextColumn Header="Tên dây" Width="1.25*"/>
<DataGridTextColumn Header="Dây dập nối" Width="1.35*"/>
<DataGridTextColumn Header="Tiết diện" Width="0.9*"/>
<DataGridTemplateColumn Header="Màu dây" Width="0.9*"/>
<DataGridTextColumn Header="Trạng thái" Width="1.8*"/>
```

có thể giữ.

Nhưng khi TabItem mở rộng:

```text
cột Trạng thái
Tên dây
Dây dập nối
```

sẽ có nhiều không gian hơn.

---

# 16. TĂNG KHẢ NĂNG ĐỌC CÁC DÒNG IO

`RowHeight` hiện bind:

```xml
RowHeight="{Binding ItemHeight}"
```

cần kiểm tra `ItemHeight`.

Không để quá thấp.

Khuyến nghị:

```text
30–38 px
```

tùy độ phân giải.

Nếu DataGrid rộng lớn hơn, có thể:

```text
FontSize 14–16
RowHeight 32–38
```

để người vận hành nhìn rõ.

---

# 17. HEADER DATAGRID PHẢI RÕ

Các header:

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

phải:

```text
FontWeight = SemiBold/Bold
FontSize = 14+
```

không được nhỏ như hiện tại nếu màn hình vận hành từ xa.

---

# 18. HIGHLIGHT LỖI

Các dòng lỗi:

```text
DÂY CHƯA KẾT NỐI
ĐẤU SAI
CHẬP MẠCH
```

phải có style rõ.

Ví dụ:

```text
Foreground đỏ
FontWeight Bold
Background hồng nhạt
```

nhưng vẫn đọc được màu dây ở cột `Màu dây`.

---

# 19. TRÁNH TRÙNG LỖI MASTER

Trong ảnh hiện có:

```text
IO1 ↔ IO7
IO2 ↔ IO8
IO1 ↔ IO7
IO2 ↔ IO8
```

xuất hiện lặp.

Phải kiểm tra lại collection `Faults`.

Nếu cùng một lỗi được scan lặp:

```text
không được thêm dòng mới mỗi frame.
```

DataGrid phải hiển thị mỗi fault duy nhất một lần.

Dùng key:

```text
FaultType + SourceIo + TargetIo + Expected/Actual
```

hoặc tương đương.

---

# 20. MASTER PROGRESS PHẢI TÍNH THEO FAULT UNIQUE

Nếu:

```text
IO1 ↔ IO7
```

xuất hiện 50 frame:

```text
MasterProgress = 1/2
```

không phải:

```text
2/2
```

và DataGrid cũng chỉ một dòng.

---

# 21. GIAO DIỆN TOP HEADER GIỮ GỌN NHƯ HIỆN TẠI

Ảnh V12.9.5 hiện tại phần đầu đã khá gọn:

```text
V12.9.5
M030066701S-CL4
Ngày giờ

Mã hàng
Sản phẩm
Loại xe
Mã KH
LOT

Counters
```

Giữ nguyên hướng này.

Không cần quay lại header cũ quá cao.

---

# 22. HÀNG PROBE/CARD GIỮ GỌN

Hàng:

```text
ĐẦU DÒ
SẴN SÀNG
CARD 1 ... CARD 20
```

giữ.

Không tăng chiều cao.

Dòng:

```text
Sẵn sàng - chạm đầu dò vào chân I/O hoặc chân PIN trên JIG
```

vẫn ở bên phải.

---

# 23. TRẠNG THÁI MASTER NÊN ĐẶT Ở ĐÂU

Khuyến nghị:

Ngay dưới Probe/Card hoặc trong một status strip nhỏ:

```text
┌────────────────────────────────────────────────────────┐
│ ĐANG CHỜ LẮP MẪU MASTER ĐẠT              MASTER 0/2   │
└────────────────────────────────────────────────────────┘
```

Chiều cao:

```text
50–65 px
```

Nếu lỗi:

```text
┌────────────────────────────────────────────────────────┐
│ ĐANG KIỂM TRA MẪU SAI DÂY               MASTER 1/2    │
└────────────────────────────────────────────────────────┘
```

Chi tiết lỗi nằm ở DataGrid.

---

# 24. KHÔNG CẦN NỀN VÀNG LỚN

Có thể dùng màu nền trạng thái nhỏ:

```text
Waiting = vàng nhạt
Testing = xanh nhạt
PASS = xanh
FAIL = đỏ nhạt
```

nhưng chỉ cho status strip.

Không phủ 40–50% màn hình bằng màu vàng.

---

# 25. KHI PRODUCTION

Sau Master hoàn tất, cùng status strip hiển thị:

```text
ĐANG CHỜ LẮP SẢN PHẨM
```

hoặc:

```text
ĐANG KIỂM TRA
```

hoặc:

```text
PASS
```

hoặc:

```text
ĐẤU SAI – IO2 → IO8
```

DataGrid vẫn là vùng chi tiết chính.

---

# 26. GRID ROWDEFINITIONS MONG MUỐN

Không bắt buộc y nguyên, nhưng nên theo hướng:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>  <!-- Header -->
    <RowDefinition Height="Auto"/>  <!-- Probe/Card -->
    <RowDefinition Height="Auto"/>  <!-- Status nhỏ -->
    <RowDefinition Height="*"/>     <!-- TabControl / FaultGrid -->
</Grid.RowDefinitions>
```

Nếu có toolbar overlay:

```text
không cần row riêng chiếm chiều cao khi ẩn.
```

---

# 27. KHÔNG DÙNG 2 ROW `*` CẠNH TRANH NHAU

Đoạn hiện tại có:

```xml
<RowDefinition Height="*"/>
...
<RowDefinition Height="*"/>
```

trong vùng ActiveFault.

Đây là nguyên nhân khiến vùng trạng thái kéo rất lớn.

Phải loại cách chia này.

Chỉ TabControl/DataGrid mới nên nhận `*` chính.

---

# 28. BACKUP GIAO DIỆN TRƯỚC KHI SỬA

Trước khi chỉnh XAML:

Tạo backup:

```text
TestView.xaml.bak_V12_9_5_before_faultgrid_restore
```

hoặc copy:

```text
Backup/TestView_V12_9_5_before_restore.xaml
```

Không ghi đè mất bản hiện tại.

---

# 29. KHÔNG ROLLBACK VIEWMODEL LOGIC

Chỉ rollback layout.

Giữ các property mới:

```text
ActiveFaultTitle
ActiveFaultMessage
MasterProgressText
ActiveFaultExpectedText
ActiveFaultActualText
IsMasterBadPhase
```

Nếu còn dùng.

Không xóa logic Master Auto chỉ vì bỏ panel lớn.

---

# 30. `ISMASTERBADPHASE` CHỈ ĐƯỢC DÙNG ĐỂ STYLE, KHÔNG ẨN DATAGRID

Có thể dùng:

```text
IsMasterBadPhase
```

để:

- đổi màu status strip;
- hiển thị progress;
- style dòng Master.

Không dùng để:

```text
Visibility=Collapsed
```

cho `FaultGrid`.

---

# 31. DATAGRID PHẢI HIỂN THỊ MASTER BAD + PRODUCTION

Cùng một DataGrid:

```text
MasterBad → hiển thị lỗi Master
Production → hiển thị lỗi sản phẩm
```

Không cần DataGrid riêng cho Master.

Phân biệt context trong ViewModel nếu cần.

---

# 32. TABITEM `DANH SÁCH LỖI / MẠNG I/O` PHẢI MỞ MẶC ĐỊNH

Khi vào TestView:

```text
SelectedIndex = 0
```

hoặc bind selected tab.

Không để app tự mở:

```text
ĐO ĐIỆN TRỞ
TEST RELAY
NHẬT KÝ THIẾT BỊ
```

trong vận hành bình thường.

---

# 33. CÁC TAB PHỤ KHÔNG ĐƯỢC LÀM GIẢM KÍCH THƯỚC TAB CHÍNH

Tab header có thể nhỏ.

Nội dung TabControl phải cùng kích thước lớn.

Không dùng:

```text
Height cố định
```

cho nội dung từng Tab.

---

# 34. BOTTOM TOOLBAR AUTO-HIDE GIỮ NGUYÊN

Tiếp tục yêu cầu trước:

- auto hide;
- hot zone dễ nhận chuột;
- animation mượt;
- ẩn hoàn toàn;
- không giữ dải xám.

Toolbar không được lấy chiều cao cố định của DataGrid.

---

# 35. TEST UI BẮT BUỘC

## Test 1 – Waiting Master Good

Expected:

```text
status nhỏ:
ĐANG CHỜ LẮP MẪU MASTER ĐẠT
```

DataGrid vẫn chiếm phần lớn màn hình.

Không có vùng vàng lớn.

---

## Test 2 – Master Bad 1/2

Expected:

```text
status:
ĐANG KIỂM TRA MẪU SAI DÂY
MASTER 1/2
```

DataGrid:

```text
hiện đúng 1 lỗi duy nhất.
```

---

## Test 3 – Master Bad 2/2

Expected:

```text
DataGrid có 2 lỗi unique
```

không 4 dòng duplicate.

Status:

```text
MASTER 2/2
```

---

## Test 4 – Production Fault

Expected:

```text
status strip:
DÂY CHƯA KẾT NỐI
```

DataGrid chi tiết:

```text
IO
Giắc
Chân
Tên dây
Màu
Trạng thái
```

---

## Test 5 – Resize

Maximize / restore.

Expected:

```text
FaultGrid stretch toàn bộ
không còn khoảng trắng lớn phía trên
```

---

# 36. TIÊU CHÍ NGHIỆM THU

Bản sửa chỉ đạt khi:

```text
1. Không còn panel Master lớn màu vàng.
2. TestView quay lại bố cục gần giao diện cũ.
3. DataGrid là vùng lớn nhất.
4. TabItem danh sách lỗi chiếm gần hết chiều cao còn lại.
5. MasterBad không làm FaultGrid bị Collapsed.
6. Lỗi Master hiển thị từng dòng.
7. Không duplicate cùng fault.
8. Status Master chỉ là một strip nhỏ.
9. Header và Probe/Card vẫn gọn.
10. Bottom toolbar không chiếm diện tích khi ẩn.
11. Logic Master Auto không bị rollback.
12. Probe/Card/Production vẫn hoạt động.
```

---

# 37. KẾT QUẢ CUỐI CÙNG MONG MUỐN

Giao diện vận hành phải có cảm giác:

```text
Thông tin đầu trang: gọn
Probe/Card: gọn
Status: rõ nhưng gọn
FaultGrid: RỘNG VÀ CAO NHẤT
```

Người vận hành nhìn vào phải tập trung ngay vào các dòng:

```text
DÂY CHƯA KẾT NỐI
IO1
C01
PIN1
BG01
ĐỎ
Chưa kết nối IO1 ↔ IO7
```

thay vì nhìn vào một khối trạng thái lớn.

**Vùng danh sách chân I/O và lỗi thực tế mới là vùng quan trọng nhất và phải được ưu tiên diện tích tối đa trên TestView.**
