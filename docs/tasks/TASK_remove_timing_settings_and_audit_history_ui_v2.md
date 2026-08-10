# TASK: XÓA TOÀN BỘ NHÓM SETTING TIMING TRÊN GIAO DIỆN VÀ AUDIT MỌI THAM CHIẾU TRONG CODE
## JBZ Universal Tester

**Mức độ:** CRITICAL / PRODUCTION  
**Phạm vi:** Chỉ nhóm setting timing đang hiển thị trong ảnh hiện tại và toàn bộ code/config tham chiếu trực tiếp đến các setting này  
**Yêu cầu quan trọng:** Không được xóa mù quáng. Phải audit toàn bộ reference trước khi sửa runtime.

---

# 0. CÁC SETTING PHẢI XÓA

Trong màn hình Settings hiện đang có 5 mục:

```text
Chu kỳ quét IO (ms)
Xác nhận chập mạch (ms)
Xác nhận sai kết nối (ms)
Ổn định sau khi lắp (ms)
Đánh giá tiếp xúc JIG (ms)
```

Yêu cầu:

> XÓA TOÀN BỘ 5 SETTING NÀY KHỎI GIAO DIỆN SETTINGS.

Sau khi hoàn thành, người vận hành không còn nhìn thấy và không còn chỉnh được 5 giá trị này.

---

# 1. QUY TRÌNH BẮT BUỘC TRƯỚC KHI SỬA

Trước khi thay đổi bất kỳ code nào:

1. Đọc toàn bộ `AGENTS.md` có hiệu lực.
2. Kiểm tra `git status`.
3. Không sửa/xóa thay đổi hiện có không liên quan.
4. Tìm chính xác View/XAML chứa 5 setting trên.
5. Tìm toàn bộ Binding/Property/Config Key liên quan.
6. Tìm toàn bộ nơi đọc/ghi/sử dụng các giá trị này trong runtime.
7. Tìm mọi unit test/integration test liên quan.
8. Tìm mọi tài liệu/config mẫu/default config có chứa key tương ứng.
9. Trước khi sửa phải báo cáo danh sách reference.

**Không được chỉ xóa 5 TextBox/Label trong XAML rồi kết thúc task.**

---

# 2. AUDIT BẮT BUỘC TOÀN BỘ REFERENCE

Với từng setting, Codex phải lập bảng:

| Setting | Property/Field | Config key | Nơi load | Nơi save | Nơi runtime sử dụng | Có ảnh hưởng thuật toán không |
|---|---|---|---|---|---|---|
| Chu kỳ quét IO | ... | ... | ... | ... | ... | ... |
| Xác nhận chập mạch | ... | ... | ... | ... | ... | ... |
| Xác nhận sai kết nối | ... | ... | ... | ... | ... | ... |
| Ổn định sau khi lắp | ... | ... | ... | ... | ... | ... |
| Đánh giá tiếp xúc JIG | ... | ... | ... | ... | ... | ... |

Phải search toàn repository theo:

- tên label;
- tên Binding;
- tên property;
- tên field;
- tên config key;
- tên JSON key;
- tên method liên quan;
- default value;
- validation logic;
- serializer/deserializer;
- migration logic.

---

# 3. CHU KỲ QUÉT IO (ms)

Phải xác định chính xác setting này đang điều khiển gì.

Audit:

```text
scan loop
poll timer
PeriodicTimer
DispatcherTimer
Task.Delay
Thread.Sleep
hardware polling cadence
UART polling
IO refresh
```

Yêu cầu:

- xóa khỏi UI;
- không để người vận hành chỉnh;
- xác định runtime sau đó dùng gì;
- nếu runtime vẫn cần một chu kỳ quét kỹ thuật thì phải dùng **giá trị nội bộ an toàn** hoặc cơ chế hiện có đã được code cố định;
- không được để giá trị `0`, null hoặc missing key làm vòng quét chạy vô hạn / 100% CPU;
- không được làm chậm scan production.

Nếu muốn xóa luôn dependency runtime của giá trị này thì phải chứng minh scan loop không còn cần nó.

---

# 4. XÁC NHẬN CHẬP MẠCH (ms)

Phải audit toàn bộ flow:

```text
SHORT candidate
→ confirm timing
→ committed SHORT
→ FAIL
```

Yêu cầu:

- xóa setting khỏi UI;
- tìm toàn bộ timer/timestamp/property liên quan;
- xác định code có còn dùng giá trị này hay không.

Không được tự ý làm thay đổi logic SHORT mà chưa audit.

Nếu yêu cầu cuối là bỏ khả năng cấu hình nhưng logic vẫn cần timing kỹ thuật:
- timing phải trở thành internal behavior;
- báo cáo giá trị hoặc cơ chế thay thế;
- không còn expose ở Settings.

Nếu logic xác nhận này thực tế không còn cần thiết:
- đề xuất loại bỏ dependency runtime;
- chỉ thực hiện khi đủ bằng chứng và test không gây FAIL giả.

---

# 5. XÁC NHẬN SAI KẾT NỐI (ms)

Phải audit flow:

```text
WRONG_CONNECTION candidate
→ confirm timing
→ committed fault
→ FAIL
```

Yêu cầu tương tự:

- xóa khỏi UI;
- audit mọi reference;
- không để binding/config chết;
- không để runtime đọc key đã mất rồi sinh default sai.

Không được vô tình thay đổi OPEN/SHORT hoặc mapping I/O.

---

# 6. ỔN ĐỊNH SAU KHI LẮP (ms)

Đây là mục đặc biệt quan trọng vì có thể liên quan trực tiếp đến việc phân biệt:

```text
ĐANG LẮP SẢN PHẨM
vs
READY TO TEST
```

Phải tìm chính xác:

- khi nào timer bắt đầu;
- khi nào timer reset;
- state nào sử dụng;
- có liên quan tới việc chống FAIL giả hay không;
- có đang chặn scan/fault commit không.

**Không được xóa logic runtime chỉ vì xóa setting UI.**

Nếu timing này vẫn cần để đảm bảo sản phẩm lắp ổn định:
- chuyển thành internal constant/automatic logic;
- không cho chỉnh từ Settings;
- ghi rõ giá trị/cơ chế trong báo cáo.

Nếu logic này thực sự không còn cần:
- phải chứng minh bằng state machine và test thực tế trước khi bỏ.

---

# 7. ĐÁNH GIÁ TIẾP XÚC JIG (ms)

Phải tìm chính xác setting này được dùng ở đâu:

- xác định JIG contact;
- debounce;
- probe contact;
- ready state;
- hardware verification;
- relay sequence;
- test start gate.

Yêu cầu:

- xóa khỏi UI;
- audit toàn bộ reference;
- không làm mất điều kiện bảo vệ phần cứng;
- không tạo FAIL giả;
- không làm test bắt đầu quá sớm.

Nếu runtime vẫn cần thời gian kỹ thuật này:
- giữ bằng internal constant/logic;
- không expose cho operator.

---

# 8. YÊU CẦU VỀ CONFIG

Phải kiểm tra các file tương đương:

```text
production.settings.json
UniversalTester.cfg
appsettings.json
*.json
*.cfg
*.ini
```

hoặc file thực tế.

Với từng key cũ:

- xác định có còn được serialize không;
- xác định có còn được deserialize không;
- xác định có default value không;
- xác định config cũ có load được không.

## Backward compatibility

Ưu tiên:

> Config cũ có chứa 5 key này vẫn phải load bình thường.

Không được làm app crash chỉ vì file Production cũ còn key legacy.

Có thể:

- giữ property legacy chỉ cho deserialize;
- bỏ khỏi UI;
- bỏ khỏi save mới;
- hoặc migrate an toàn.

Nhưng phải báo cáo rõ.

---

# 9. KHÔNG ĐỂ DEAD CODE / DEAD BINDING

Sau khi xóa UI phải kiểm tra:

- không còn BindingExpression error;
- không còn converter chỉ dùng cho 5 field nếu không còn cần;
- không còn validation rule chết;
- không còn command handler chết;
- không còn property notification vô nghĩa;
- không còn code-behind refer tới control đã xóa;
- không còn x:Name của control bị gọi;
- không còn save/load UI field.

Nếu property vẫn cần cho backward compatibility thì ghi comment rõ:

```text
Legacy config compatibility only
```

không để người sau hiểu nhầm là runtime setting còn hoạt động.

---

# 10. KHÔNG ĐƯỢC DÙNG GIÁ TRỊ 0 MỘT CÁCH MÙ QUÁNG

Cấm kiểu sửa:

```text
setting = 0
```

cho tất cả 5 mục rồi coi là hoàn thành.

Lý do:

- `Chu kỳ quét IO = 0` có thể gây busy loop;
- `Ổn định sau khi lắp = 0` có thể gây test quá sớm;
- `Đánh giá tiếp xúc JIG = 0` có thể làm sai state;
- confirm timing = 0 có thể gây latch fault quá nhanh.

Phải audit từng logic riêng.

---

# 11. MỤC TIÊU SAU KHI SỬA

Giao diện Settings không còn 5 mục:

```text
Chu kỳ quét IO (ms)
Xác nhận chập mạch (ms)
Xác nhận sai kết nối (ms)
Ổn định sau khi lắp (ms)
Đánh giá tiếp xúc JIG (ms)
```

Nhưng runtime vẫn phải:

- scan I/O ổn định;
- không busy loop;
- không tăng CPU bất thường;
- không FAIL giả;
- không test quá sớm;
- SHORT thật vẫn phát hiện;
- WRONG CONNECTION thật vẫn phát hiện;
- READY TO TEST vẫn đúng;
- JIG contact vẫn đúng;
- hàng tốt vẫn PASS.

---

# 12. TEST BẮT BUỘC

## Test 1 — Mở Settings

Expected:
- không còn cả 5 setting;
- layout tự dồn gọn;
- không có khoảng trắng bất thường;
- không binding error.

## Test 2 — Load config cũ

Config có đủ 5 key legacy.

Expected:
- app load bình thường;
- không crash.

## Test 3 — Scan I/O

Expected:
- scan vẫn hoạt động ổn định;
- CPU không tăng bất thường;
- không loop vô hạn.

## Test 4 — Hàng tốt

Expected:
- PASS bình thường.

## Test 5 — Hàng chập thật

Expected:
- SHORT vẫn được phát hiện đúng.

## Test 6 — Sai kết nối thật

Expected:
- WRONG CONNECTION vẫn được phát hiện đúng.

## Test 7 — Đang lắp sản phẩm

Expected:
- không FAIL giả chỉ vì timing UI đã bị bỏ.

## Test 8 — JIG contact

Expected:
- state JIG/READY vẫn đúng;
- không bắt đầu test khi tiếp xúc chưa hợp lệ.

---

# 13. ACCEPTANCE CRITERIA

Task chỉ DONE khi:

- [ ] Đã đọc `AGENTS.md`.
- [ ] Đã xóa cả 5 setting khỏi UI.
- [ ] Đã audit toàn bộ reference của từng setting.
- [ ] Đã lập bảng property/config/runtime usage.
- [ ] Không còn dead binding/control reference.
- [ ] Config cũ vẫn load được.
- [ ] Không dùng `0 ms` mù quáng.
- [ ] Scan I/O vẫn ổn định.
- [ ] Không tăng CPU bất thường.
- [ ] Không phát sinh FAIL giả mới.
- [ ] SHORT thật vẫn FAIL đúng.
- [ ] WRONG CONNECTION thật vẫn FAIL đúng.
- [ ] READY TO TEST không bị phá.
- [ ] JIG contact logic không bị phá.
- [ ] Hàng tốt vẫn PASS.
- [ ] Build thành công.
- [ ] Runtime không crash.
- [ ] Không có BindingExpression error mới.
- [ ] Git diff chỉ chứa thay đổi liên quan.

---

# 14. BÁO CÁO BẮT BUỘC SAU KHI HOÀN THÀNH

## A. AGENTS.md
- File đã đọc:
- Rule áp dụng:

## B. Reference audit

| Setting | Property | Config key | Runtime methods | Quyết định cuối |
|---|---|---|---|---|
| Chu kỳ quét IO | ... | ... | ... | removed/internal/legacy |
| Xác nhận chập mạch | ... | ... | ... | ... |
| Xác nhận sai kết nối | ... | ... | ... | ... |
| Ổn định sau khi lắp | ... | ... | ... | ... |
| Đánh giá tiếp xúc JIG | ... | ... | ... | ... |

## C. Runtime behavior sau khi xóa UI
- Scan IO dùng cơ chế gì:
- SHORT dùng cơ chế gì:
- Wrong connection dùng cơ chế gì:
- Ready-after-install dùng cơ chế gì:
- JIG contact dùng cơ chế gì:

## D. Config compatibility
- Key legacy còn đọc:
- Key mới có còn save:
- Migration nếu có:

## E. Files changed

| File | Thay đổi | Lý do |
|---|---|---|

## F. Validation
- Build:
- Settings:
- Config cũ:
- Scan:
- Hàng tốt:
- Short:
- Wrong connection:
- Install state:
- JIG:

---

# 15. YÊU CẦU CUỐI

Không chỉ xóa giao diện.

Phải thực hiện đúng:

```text
READ AGENTS.md
→ SEARCH 5 SETTINGS TRÊN TOÀN REPOSITORY
→ LẬP REFERENCE MAP
→ XÁC ĐỊNH ẢNH HƯỞNG RUNTIME
→ XÓA UI
→ XỬ LÝ PROPERTY/CONFIG/LEGACY AN TOÀN
→ GIỮ LOGIC PRODUCTION ỔN ĐỊNH
→ BUILD
→ RUNTIME TEST
→ AUDIT GIT DIFF
→ BÁO CÁO
```

---

# 16. BỔ SUNG: AUDIT TOÀN BỘ UI TAB LỊCH SỬ / HISTORY

Ngoài phạm vi xóa 5 setting timing ở trên, yêu cầu Codex **rà soát lại toàn bộ giao diện tab Lịch sử / History** để phát hiện các lỗi hiển thị đang tồn tại.

## 16.1 Mục tiêu

Kiểm tra toàn bộ tab Lịch sử và sửa các vấn đề như:

- chữ bị che;
- label bị cắt;
- TextBlock/TextBox/ComboBox/Button không hiển thị hết nội dung;
- cột DataGrid quá hẹp làm mất chữ;
- header DataGrid bị cắt;
- nút chức năng sát nhau;
- control chồng lên nhau;
- margin/padding không đều;
- khu vực filter quá chật;
- khoảng trắng dư thừa;
- UI chưa gọn;
- layout chưa cân đối;
- scrollbar che nội dung;
- chiều cao row/header không phù hợp;
- text bị truncate không hợp lý;
- resize cửa sổ làm mất control;
- Windows DPI 125%/150% làm giao diện bị clip.

---

## 16.2 Quy trình audit tab Lịch sử

Codex phải tìm đúng View/UserControl/XAML của tab:

```text
Lịch sử
History
Test History
Production History
```

hoặc tên thực tế trong source.

Sau đó audit:

```text
Grid.RowDefinitions
Grid.ColumnDefinitions
Width
Height
MinWidth
MinHeight
MaxWidth
MaxHeight
Margin
Padding
HorizontalAlignment
VerticalAlignment
TextWrapping
TextTrimming
ScrollViewer
DataGrid
Column Width
HeaderStyle
CellStyle
Button Style
FontSize
```

Không được chỉ nhìn một control riêng lẻ.

Phải xem toàn bộ bố cục của tab.

---

## 16.3 DataGrid lịch sử

Nếu tab có DataGrid, phải kiểm tra:

- tên cột có hiển thị đủ không;
- dữ liệu có bị cắt không;
- các cột quan trọng có đủ rộng không;
- cột thời gian/ngày giờ có đọc rõ không;
- cột mã hàng/model/LOT/result/fault có hợp lý không;
- cột quá rộng gây lãng phí không gian;
- cột quá hẹp gây che chữ không;
- horizontal scrollbar có xuất hiện không cần thiết không;
- vertical scrollbar có che cell cuối không;
- row height có quá thấp không;
- header height có quá thấp không.

Ưu tiên dùng:

```text
Auto
*
MinWidth
Width="SizeToHeader"
Width="SizeToCells"
```

một cách hợp lý.

Không hard-code width tùy tiện nếu layout có thể responsive tốt hơn.

---

## 16.4 Khu vực filter / tìm kiếm

Nếu tab Lịch sử có các control như:

```text
Từ ngày
Đến ngày
Mã hàng
LOT
Model
PASS/FAIL
Tìm kiếm
Làm mới
Xuất dữ liệu
Xóa lọc
```

phải đảm bảo:

- label không bị che;
- textbox/combobox/date picker đủ rộng;
- nút không che chữ;
- các nút có khoảng cách hợp lý;
- thứ tự thao tác rõ ràng;
- không dùng font quá nhỏ;
- không để hàng filter quá cao hoặc quá chật.

Nếu cửa sổ hẹp:
- cho wrap hợp lý;
- hoặc dùng Grid responsive;
- không chồng control.

---

## 16.5 Không được sửa theo kiểu "giảm font để vừa"

Không giải quyết lỗi UI bằng cách:

```text
FontSize = nhỏ hơn
```

cho toàn bộ tab.

Ưu tiên sửa:

- Grid;
- width;
- Auto/*;
- margin;
- padding;
- min width;
- text wrapping;
- bố cục nhóm control.

Font chỉ thay đổi khi thực sự cần và phải giữ khả năng đọc tốt trên máy Production.

---

## 16.6 DPI / Display Scale

Kiểm tra tối thiểu:

```text
100%
125%
150%
```

Nếu không có môi trường chạy DPI khác nhau thì phải audit XAML để tránh fixed size dễ bị clip.

Các control quan trọng phải không bị mất chữ khi Windows scale >100%.

---

## 16.7 Không làm thay đổi logic lịch sử

Task UI này không được làm ảnh hưởng:

- truy vấn database;
- filter logic;
- export;
- delete history;
- load history;
- pagination;
- sorting;
- PASS/FAIL data;
- LOT;
- timestamp;
- test-history.db;
- schema database.

Chỉ sửa logic nếu phát hiện lỗi UI bắt nguồn trực tiếp từ binding và có bằng chứng rõ ràng.

---

## 16.8 Acceptance Criteria bổ sung cho tab Lịch sử

- [ ] Đã xác định đúng file View/XAML của tab Lịch sử.
- [ ] Đã rà soát toàn bộ control trong tab.
- [ ] Không còn chữ bị che.
- [ ] Không còn label bị cắt.
- [ ] Button hiển thị đủ text.
- [ ] Filter controls không chồng nhau.
- [ ] DataGrid header hiển thị đủ.
- [ ] Các cột chính có chiều rộng hợp lý.
- [ ] Không có khoảng trắng dư thừa lớn.
- [ ] UI gọn và cân đối hơn.
- [ ] Resize không làm mất control.
- [ ] DPI 125%/150% không gây clip nghiêm trọng.
- [ ] Không thay đổi logic history/database ngoài phạm vi.

---

## 16.9 Báo cáo bổ sung

Trong báo cáo cuối, thêm mục:

### HISTORY UI AUDIT

| Khu vực | Vấn đề phát hiện | File/XAML | Cách sửa |
|---|---|---|---|
| Filter | ... | ... | ... |
| DataGrid | ... | ... | ... |
| Buttons | ... | ... | ... |
| Header | ... | ... | ... |
| Layout | ... | ... | ... |

Kèm xác nhận:

```text
Không còn control bị che chữ hoặc clip trong tab Lịch sử ở kích thước cửa sổ Production tiêu chuẩn.
```



---

# 17. QUY TRÌNH CUỐI CÙNG SAU KHI BỔ SUNG

```text
READ AGENTS.md
→ SEARCH 5 SETTINGS TRÊN TOÀN REPOSITORY
→ LẬP REFERENCE MAP
→ XÓA 5 SETTING KHỎI UI
→ XỬ LÝ PROPERTY/CONFIG/LEGACY AN TOÀN
→ AUDIT TAB LỊCH SỬ / HISTORY
→ SỬA CHỮ BỊ CHE / CONTROL BỊ CLIP / LAYOUT CHƯA GỌN
→ GIỮ NGUYÊN LOGIC HISTORY
→ BUILD
→ RUNTIME TEST
→ AUDIT GIT DIFF
→ BÁO CÁO
```
