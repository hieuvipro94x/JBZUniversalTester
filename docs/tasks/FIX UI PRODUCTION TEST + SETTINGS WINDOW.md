# TASK: FIX UI PRODUCTION TEST + SETTINGS WINDOW
## JBZ Universal Tester

Mức ưu tiên: HIGH  
Loại thay đổi: UI / WPF / DataGrid / Layout  
Không thay đổi thuật toán kiểm tra sản phẩm nếu không thực sự cần thiết.

---

# 0. QUY TRÌNH BẮT BUỘC TRƯỚC KHI SỬA

TRƯỚC KHI THỰC HIỆN BẤT KỲ THAY ĐỔI CODE NÀO:

1. Tìm và đọc toàn bộ file `AGENTS.md` có hiệu lực trong repository.
2. Nếu có nhiều `AGENTS.md`:
   - đọc từ root project;
   - đọc tiếp các `AGENTS.md` trong thư mục con liên quan;
   - tuân thủ file có scope gần file đang sửa nhất.
3. Đọc các tài liệu liên quan trong:
   - `docs/`
   - `docs/tasks/`
   - README hoặc tài liệu architecture nếu có.
4. Kiểm tra `git status`.
5. Không sửa/xóa thay đổi đang tồn tại của người dùng nếu không liên quan task này.
6. Xác định đúng View / UserControl / ResourceDictionary / ViewModel đang điều khiển:
   - `FaultGrid`
   - cột `Màu dây`
   - cửa sổ `CÀI ĐẶT CẤU HÌNH`
   - phần `ĐO ĐIỆN TRỞ R1 – R5`
   - phần `TEM / MÁY IN / MODEL GẦN NHẤT`
7. Trước khi sửa phải báo cáo ngắn:
   - file dự kiến sửa;
   - nguyên nhân;
   - cách sửa;
   - rủi ro ảnh hưởng.

KHÔNG bắt đầu chỉnh code trước khi đọc `AGENTS.md`.

---

# 1. LỖI FAULTGRID – NỀN PHẢI LUÔN TRẮNG

## Hiện trạng

Ở màn hình Production/Test có:

```xml
<DataGrid x:Name="FaultGrid" ... />
```

Khi chọn/load file cấu hình `.model`, vùng nền bên trong `FaultGrid`
có thể bị ảnh hưởng bởi màu/style của trạng thái lỗi hoặc resource khác.

Yêu cầu mới:

> PHẦN NỀN TRỐNG BÊN TRONG `FaultGrid` PHẢI MÀU TRẮNG.

Không để nền tổng thể của DataGrid chuyển:
- đỏ nhạt;
- hồng;
- vàng;
- màu trạng thái;
- Transparent dẫn tới lấy màu container bên dưới.

## Yêu cầu

Phải kiểm tra đồng thời:

- `Background`
- `RowBackground`
- `AlternatingRowBackground`
- `DataGridRow`
- `DataGridCell`
- `DataGridColumnHeader`
- container/Border bao ngoài
- Trigger theo lỗi
- Trigger theo selection
- DynamicResource / StaticResource
- style được merge từ ResourceDictionary.

### Kết quả mong muốn

Khu vực không có row:

```text
WHITE
```

Khu vực nền mặc định của grid:

```text
WHITE
```

Không được vì load `.model` mà toàn bộ DataGrid trở thành màu lỗi.

---

# 2. GIỮ MÀU TRẠNG THÁI LỖI Ở TỪNG ROW/CELL

Việc chuyển nền `FaultGrid` về trắng KHÔNG có nghĩa xóa màu cảnh báo.

Ví dụ hiện tại:

```text
HỞ MẠCH
Chưa kết nối: IO21 <-> IO22
```

có thể tiếp tục dùng:
- chữ đỏ;
- background row/cell hồng nhạt nếu thiết kế hiện tại cần.

Nhưng:

> Chỉ row/cell có dữ liệu lỗi được phép mang màu trạng thái.

Phần nền DataGrid bên ngoài dữ liệu phải trắng.

Không được sửa khiến trạng thái lỗi khó quan sát hơn.

---

# 3. LỖI CỘT "MÀU DÂY" – CÁC Ô MÀU ĐANG MẤT VIỀN

## Hiện trạng

Trong `FaultGrid`, cột:

```text
Màu dây
```

hiển thị các thanh màu như:

- đỏ
- vàng
- đen
- xanh
- cam
- xám
- trắng
- hồng
- ...

Hiện tại một số thanh màu bị mất/khó thấy Border.

Đặc biệt:

```text
BLACK
WHITE
GRAY
YELLOW
```

có thể hòa vào nền hoặc không nhận biết rõ giới hạn ô màu.

## Yêu cầu

Mỗi mẫu màu dây phải có Border rõ ràng.

Cấu trúc mong muốn tương đương:

```xml
<Border
    BorderBrush="..."
    BorderThickness="1"
    CornerRadius="0 hoặc 1"
    Background="{Binding WireColor...}">
</Border>
```

Có thể dùng hình chữ nhật/Border hiện có, KHÔNG bắt buộc thay control nếu không cần.

### Quy tắc

- Thanh màu phải giữ nguyên màu thực tế của dây.
- Mọi màu đều có viền.
- Màu trắng vẫn phải nhìn thấy.
- Màu đen vẫn phải nhìn thấy biên.
- Không dùng Border cùng màu với Background.
- Không làm thanh màu quá lớn.
- Không làm thay đổi chiều cao row hiện tại một cách đáng kể.

Ưu tiên Border:

```text
#808080
hoặc màu trung tính tương đương
```

nhưng phải kiểm tra resource/theme hiện tại trước khi hard-code.

---

# 4. KHÔNG ĐƯỢC LÀM MẤT GRIDLINE

Kiểm tra:

```xml
GridLinesVisibility
HorizontalGridLinesBrush
VerticalGridLinesBrush
BorderBrush
BorderThickness
```

Các cột/row phải còn đường phân cách rõ ràng.

Đặc biệt cột `Màu dây` không được có cảm giác:

```text
thanh màu nổi tự do
```

mà phải nằm đúng trong cell của DataGrid.

---

# 5. SETTINGS – PHẦN ĐO ĐIỆN TRỞ R1–R5 ĐANG HIỂN THỊ KHÔNG HẾT

## Hiện trạng

Ở cửa sổ:

```text
CÀI ĐẶT CẤU HÌNH
```

phần dưới bên trái:

```text
ĐO ĐIỆN TRỞ R1 – R5
```

đang có vấn đề khi ứng dụng chạy thực tế:

- chiều cao vùng quá nhỏ;
- DataGrid/list điện trở bị cắt;
- không hiển thị hết nội dung;
- có khả năng chỉ nhìn thấy một phần row;
- resize/DPI làm vùng này càng dễ bị cắt.

## Yêu cầu

Phải audit layout của toàn màn hình Settings.

Kiểm tra:

```xml
Grid.RowDefinitions
Height="..."
MinHeight
MaxHeight
VerticalAlignment
HorizontalAlignment
Margin
Padding
ScrollViewer
DataGrid Height
Auto / *
SharedSizeGroup
```

### Không được giải quyết bằng cách chỉ tăng chiều cao cửa sổ vô hạn.

Phải sửa đúng Grid layout.

Ưu tiên:

```text
Auto
*
MinHeight
```

thay vì hard-code nhiều pixel không cần thiết.

---

# 6. SETTINGS – TEM / MÁY IN / MODEL GẦN NHẤT BỊ CẮT

## Hiện trạng

Phần dưới bên phải:

```text
TEM / MÁY IN / MODEL GẦN NHẤT
```

khi ứng dụng chạy đang không hiển thị hết toàn bộ thành phần.

Hiện thấy các mục như:

```text
Máy in Windows
Tự in khi PASS
```

nhưng cần kiểm tra xem còn các control bên dưới/bên phải đang bị clip hoặc mất.

## Yêu cầu

Tìm toàn bộ control thuộc section này và đảm bảo:

- hiển thị đầy đủ;
- không bị cắt bottom;
- không bị cắt right;
- label không bị mất chữ;
- textbox/combobox/checkbox không chồng nhau;
- hoạt động đúng khi scale Windows >100%.

Nếu nội dung section thực sự nhiều hơn vùng hiện tại:

Ưu tiên:
1. bố trí lại Grid;
2. tăng row bằng `Auto`/`*`;
3. dùng ScrollViewer cho vùng settings nếu cần.

Không ưu tiên:
- ép font nhỏ;
- ép control cực thấp;
- đặt negative margin;
- che control.

---

# 7. RESPONSIVE / DPI

Phải kiểm tra giao diện ít nhất ở:

```text
100%
125%
150%
```

Windows Display Scale nếu môi trường cho phép.

Tối thiểu phải đảm bảo code layout không phụ thuộc hoàn toàn vào pixel cố định.

Các phần bắt buộc còn đọc được:

```text
ĐO ĐIỆN TRỞ R1–R5
TEM / MÁY IN / MODEL GẦN NHẤT
THỜI GIAN QUÉT / XÁC NHẬN LỖI / BẢO TRÌ
THÔNG TIN PRODUCTION
THIẾT BỊ / I/O
```

---

# 8. KHÔNG THAY ĐỔI LOGIC FILE `.model`

Task này KHÔNG yêu cầu sửa format `.model`.

Không được tự ý thay đổi:

- deserialize `.model`;
- serialize `.model`;
- mapping I/O;
- wire definition;
- màu dây trong dữ liệu;
- chân PIN;
- dập nối;
- CLIP;
- Master;
- Production;
- tiêu chuẩn PASS/FAIL.

Nếu phát hiện lỗi UI bắt nguồn từ model property:
chỉ sửa phần binding/converter/style nếu đủ an toàn.

Nếu cần thay đổi data model:
DỪNG và báo cáo trước.

---

# 9. KHÔNG ẢNH HƯỞNG LOGIC TEST THỰC TẾ

Đây là máy đang test với BO THẬT tại Production.

TUYỆT ĐỐI không thay đổi ngoài phạm vi task này:

- scan I/O;
- open circuit;
- short circuit;
- wrong connection;
- relay JIG;
- relay MARKING;
- R1–R5 measurement logic;
- Master Good;
- Master NG;
- PASS;
- FAIL;
- Auto print;
- Probe Pin;
- Production counter;
- card expansion;
- COM/UART;
- timing hardware.

Task này ưu tiên UI/layout.

---

# 10. KHÔNG REFACTOR LAN RỘNG

Không thực hiện:

- đổi toàn bộ theme;
- đổi architecture;
- đổi MVVM framework;
- rename hàng loạt;
- di chuyển file hàng loạt;
- formatting toàn project;
- sửa code unrelated;
- tạo bản `.bak` trong source tree.

Không để các file như:

```text
*.bak
*.backup
*_old.xaml
*_copy.cs
*.tmp
```

tham gia build.

Nếu cần backup thì dùng Git, không tạo file backup trong project.

---

# 11. GIAO DIỆN FAULTGRID MONG MUỐN

Khi load `.model` và bắt đầu test:

## Background

```text
FaultGrid base background = WHITE
```

## Rows lỗi

Có thể:

```text
Background = màu lỗi nhạt
Foreground = đỏ
```

theo style hiện tại.

## Cột màu dây

Ví dụ:

```text
[ viền ][ RED    ][ viền ]
[ viền ][ YELLOW ][ viền ]
[ viền ][ BLACK  ][ viền ]
[ viền ][ WHITE  ][ viền ]
[ viền ][ GRAY   ][ viền ]
```

Tất cả phải phân biệt được với nền cell.

---

# 12. KIỂM TRA LOAD MODEL

Test ít nhất:

### Case A
Mở ứng dụng chưa chọn `.model`.

Expected:
- FaultGrid trắng;
- không lỗi binding.

### Case B
Chọn `.model`.

Expected:
- dữ liệu được load;
- background FaultGrid vẫn trắng;
- màu dây xuất hiện đúng.

### Case C
Model có nhiều màu dây khác nhau.

Expected:
- Border của tất cả swatch hiển thị.

### Case D
Model chứa màu WHITE.

Expected:
- vẫn nhận biết được swatch nhờ Border.

### Case E
Model chứa màu BLACK.

Expected:
- swatch đen không hòa vào gridline.

---

# 13. TEST SETTINGS

Mở Settings và kiểm tra:

```text
CÀI ĐẶT CẤU HÌNH
```

### Case 1 – màn hình bình thường

Tất cả section hiển thị đủ.

### Case 2 – R1–R5

Phải thấy đầy đủ:
- checkbox Sử dụng;
- Tên;
- Kênh;
- Min Ω;
- Max Ω;
- tất cả row điện trở được cấu hình.

### Case 3 – Printer

Phải thấy đầy đủ:
- Máy in Windows;
- lựa chọn máy in;
- Tự in khi PASS;
- các field/model liên quan nếu có.

### Case 4 – resize

Nếu cửa sổ hỗ trợ resize:
không được cắt control.

### Case 5 – DPI

Không mất label và input.

---

# 14. BUILD VALIDATION

Sau khi sửa:

1. Clean.
2. Restore nếu cần.
3. Build đúng configuration mà repository đang dùng.
4. Không tự ý đổi target framework.
5. Không tự ý nâng NuGet.
6. Không tự ý thay SDK.
7. Không được để warning/error mới do thay đổi này.

Nếu repo có script build chính thức trong `AGENTS.md`,
PHẢI sử dụng script đó.

---

# 15. RUNTIME VALIDATION

Không được coi:

```text
Build succeeded
```

là hoàn thành.

Phải chạy ứng dụng và kiểm tra UI thật.

Cần xác nhận:

- Settings mở được.
- Production page mở được.
- Load `.model` được.
- FaultGrid trắng.
- Row lỗi vẫn hiển thị đúng.
- Wire color có Border.
- Settings không bị clip.
- Save settings vẫn hoạt động.
- Quay lại trang chính không crash.

---

# 16. KIỂM TRA BINDING / EXCEPTION

Theo dõi Output/Debug log khi mở hai màn hình.

Không được phát sinh mới:

```text
BindingExpression path error
Cannot find resource
XamlParseException
ArgumentException
NullReferenceException
InvalidOperationException
```

Đặc biệt kiểm tra resource của DataGrid style.

---

# 17. TIÊU CHÍ NGHIỆM THU

Task chỉ DONE khi đạt toàn bộ:

- [ ] Đã đọc `AGENTS.md` trước khi sửa.
- [ ] `FaultGrid` có nền trắng sau khi chọn/load `.model`.
- [ ] Vùng trống bên dưới các row cũng trắng.
- [ ] Row/cell lỗi vẫn giữ cảnh báo dễ nhìn.
- [ ] Cột `Màu dây` hiển thị đúng màu.
- [ ] Tất cả thanh màu có viền rõ.
- [ ] WHITE swatch nhìn thấy rõ.
- [ ] BLACK swatch nhìn thấy rõ.
- [ ] Không mất gridline.
- [ ] `ĐO ĐIỆN TRỞ R1–R5` hiển thị đầy đủ.
- [ ] `TEM / MÁY IN / MODEL GẦN NHẤT` hiển thị đầy đủ.
- [ ] Không có control bị clip.
- [ ] Không làm nhỏ font để che lỗi layout.
- [ ] Save Settings hoạt động.
- [ ] Load `.model` hoạt động.
- [ ] Không thay đổi format `.model`.
- [ ] Không thay đổi thuật toán test.
- [ ] Không thay đổi timing/relay/I/O.
- [ ] Build thành công.
- [ ] Runtime không crash.
- [ ] Không phát sinh binding error mới.
- [ ] Không tạo file backup trong source.
- [ ] Git diff chỉ chứa thay đổi cần thiết.

---

# 18. BÁO CÁO SAU KHI HOÀN THÀNH

Codex phải trả báo cáo theo format:

## A. AGENTS.md
- Các AGENTS.md đã đọc:
- Các quy tắc quan trọng đã tuân thủ:

## B. Root cause
1. FaultGrid background:
2. Wire color border:
3. Resistance section clipping:
4. Printer/model section clipping:

## C. Files changed
| File | Thay đổi | Lý do |
|------|----------|-------|

## D. Chi tiết sửa
- FaultGrid:
- Wire color:
- Settings layout:
- Resistance:
- Printer/model:

## E. Validation
- Build:
- Runtime:
- Load `.model`:
- Settings:
- DPI/layout:
- Binding errors:

## F. Không thay đổi
Xác nhận không thay đổi:
- test algorithm;
- I/O scan;
- relay;
- Master;
- Production;
- `.model` schema;
- resistance measurement algorithm;
- printer PASS logic.

## G. Git diff audit
Liệt kê tất cả file thay đổi và xác nhận không có file ngoài phạm vi.

---

# 19. YÊU CẦU CUỐI CÙNG

Ưu tiên sửa nhỏ, chính xác, an toàn cho máy Production.

KHÔNG sửa theo kiểu:

> thấy giao diện lỗi nên viết lại toàn bộ

Mà phải thực hiện đúng quy trình:

```text
READ AGENTS.md
→ AUDIT
→ XÁC ĐỊNH ROOT CAUSE
→ SỬA TỐI THIỂU
→ BUILD
→ CHẠY THỰC TẾ
→ LOAD .MODEL
→ KIỂM TRA SETTINGS
→ AUDIT GIT DIFF
→ BÁO CÁO
```
