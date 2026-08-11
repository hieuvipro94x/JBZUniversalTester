# Phân tích và chuyển giao diện TesterVN sang Production V10

## Mục tiêu

Bản `JBZUniversalTester_Production_VI_V10` đã có logic làm việc với bo thật ổn định. Thay đổi lần này chỉ nhằm đưa phong cách giao diện của `UniversalTesterVN_Project` sang V10, không thay thế giao thức UART, quy trình tải model, máy trạng thái kiểm tra, xử lý lỗi hay lưu kết quả.

## 1. Phân tích `UniversalTesterVN_Project`

### Kiến trúc

- Framework giao diện: **PySide6**.
- Cửa sổ chính: `QMainWindow` và `QStackedWidget`.
- Mỗi chức năng là một `QWidget` riêng:
  - `HomePage`
  - `TestPage`
  - `BasicSettingsPage`
  - `DeviceSettingsPage`
  - `TestConditionPage`
  - `DiagnosticsPage`
  - `ReportsPage`
- Theme chung nằm trong `gui/style.py`.
- Giao diện dùng nền xám rất nhạt, nút phẳng, đường viền mảnh, font Arial/Segoe UI và bố cục gần như toàn màn hình.

### Đặc điểm giao diện được lấy làm tham chiếu

- Header đơn giản, thông tin phiên bản nằm góc trên.
- Logo nằm giữa màn hình chính.
- Thanh nút chức năng chạy ngang sát đáy màn hình.
- Màn hình test gồm ba khu vực trên cùng: bộ đếm bên trái, thông tin model ở giữa và trạng thái/bộ đếm tổng bên phải.
- Bảng lỗi chiếm phần lớn diện tích.
- Các màn hình cài đặt, lịch sử và chẩn đoán có nút quay về ở góc trái, tiêu đề giữa và form màu trung tính.

## 2. Phân tích `JBZUniversalTester_Production_VI_V10`

### Kiến trúc

- Framework giao diện: **Tkinter/ttk**.
- `MainWindow` là cửa sổ test thật.
- `MainMenuWindow`, `ModelLoadWindow`, `ReportWindow`, `SettingsWindow`, `DiagnosticWindow` và `RejectDialog` là các `Toplevel`.
- Logic bo thật nằm ở các module riêng:
  - `jbz_tester/board.py`
  - `jbz_uart/manager.py`
  - `jbz_tester/protocol.py`
  - `jbz_tester/cycle_state.py`
  - `jbz_tester/probe_state.py`
  - `jbz_tester/model_data.py`
  - `jbz_tester/storage.py`
- V10 có tự tìm UART, tự kết nối lại, đọc model trên bo, xác nhận model, xử lý OPEN/đấu sai, đầu dò GND, marking, chờ tháo dây và lưu SQLite.

### Lý do không chuyển V10 sang PySide6

Chuyển framework sẽ buộc thay event loop, thread callback, `after()`, `StringVar`, `Toplevel`, message box và nhiều điểm liên kết trực tiếp với máy trạng thái. Việc đó không còn là thay giao diện đơn thuần và có nguy cơ làm thay đổi phần logic đang chạy tốt. Vì vậy bản này giữ Tkinter và chỉ dựng lại widget/layout theo phong cách TesterVN.

## 3. Phạm vi thay đổi

### `jbz_tester/ui_theme.py`

Module theme mới, chỉ chứa:

- Bảng màu TesterVN.
- Font và hàm scale.
- Theme cho `Treeview`, `Notebook`, `Progressbar`.
- Helper tạo nút phẳng và ô hiển thị giá trị.

### `jbz_tester/gui.py`

Chỉ thay phần dựng giao diện:

- Menu chính kiểu TesterVN/ReferenceHomePage.
- Logo `VINA / JBZVINA` ở giữa.
- Thanh sáu nút dưới cùng:
  - Bắt đầu kiểm tra
  - Kiểm tra chân pin
  - Lịch sử
  - Cài đặt
  - Nhập model
  - Kết thúc
- Màn hình tải model.
- Màn hình test.
- Màn hình lịch sử.
- Màn hình cài đặt.
- Màn hình chẩn đoán.
- Font, màu, khoảng cách và viền.

Các hàm kết nối bo, tải model, xử lý sự kiện, bắt đầu chu kỳ, kết thúc chu kỳ, marking, reconnect và lưu kết quả không bị sửa.

### `jbz_tester/fault_table.py`

Chỉ đổi màu nền, màu header, đường lưới và font. Cấu trúc hàng, thứ tự ưu tiên lỗi, topology và nội dung bảng giữ nguyên.

### `jbz_model_loader/gui.py`

Chỉ thay bố cục và theme của công cụ Model Downloader độc lập. Các hàm tìm file, compile profile, kết nối UART và upload giữ nguyên.

## 4. Ánh xạ giao diện

| TesterVN | V10 sau chuyển đổi | Logic sử dụng |
|---|---|---|
| Trang chủ | `MainMenuWindow` | Logic model/board của V10 |
| Kiểm tra chính | `MainWindow.build_ui()` | Máy trạng thái kiểm tra V10 |
| Cài đặt | `SettingsWindow` | `AppConfig` V10 |
| Chẩn đoán | `DiagnosticWindow` | `BoardController` V10 |
| Báo cáo | `ReportWindow` | `ResultStore` V10 |
| Chọn/tải model | `ModelLoadWindow` | `SerialSession` và profile V10 |

## 5. Phần logic được giữ nguyên

Không chỉnh sửa nội dung các file sau:

- `jbz_tester/board.py`
- `jbz_uart/manager.py`
- `jbz_tester/protocol.py`
- `jbz_tester/cycle_state.py`
- `jbz_tester/probe_state.py`
- `jbz_tester/model_data.py`
- `jbz_tester/storage.py`
- `jbz_model_loader/serial_session.py`
- `jbz_model_loader/model_compiler.py`
- `jbz_model_loader/profile_io.py`
- Tất cả bài test hiện có.

`docs/UI_ONLY_VERIFICATION.json` ghi kết quả kiểm tra tự động: 63 file gốc không thuộc UI vẫn giống byte-for-byte và 60 phương thức logic trong `jbz_tester/gui.py` có AST giống hoàn toàn bản V10 gốc.

## 6. Kiểm thử đã chạy

```text
36 passed
```

Ngoài pytest, các cửa sổ sau đã được khởi tạo trong Xvfb:

- Menu chính
- Màn hình test
- Cài đặt
- Lịch sử
- Chẩn đoán
- Model Downloader độc lập

Kết quả smoke test:

```text
UI_SMOKE_OK
LOADER_UI_SMOKE_OK
```

## 7. Ảnh xem trước

- `docs/ui_previews/01_menu.png`
- `docs/ui_previews/02_test.png`
- `docs/ui_previews/03_settings.png`
- `docs/ui_previews/04_reports.png`
- `docs/ui_previews/05_model_downloader.png`

Ảnh giao diện tham chiếu của TesterVN nằm trong `docs/reference_testervn/`.

## 8. Chạy chương trình

Cách chạy không đổi:

```bash
cd /home/sa/Desktop/JBZUniversalTester_Production_VI_V10_TesterVN_UI
./install.sh
./run_gui.sh
```

Không cần cài PySide6 vì bản V10 vẫn dùng Tkinter như trước.

## 9. Khôi phục giao diện V10 cũ

Các file giao diện gốc được lưu tại:

- `docs/rollback/jbz_tester_gui_v10_original.py.txt`
- `docs/rollback/jbz_tester_fault_table_v10_original.py.txt`
- `docs/rollback/jbz_model_loader_gui_v10_original.py.txt`

Chỉ cần sao chép lại đúng tên file `.py` tương ứng nếu cần quay về giao diện cũ.
