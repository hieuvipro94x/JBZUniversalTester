# JBZ Universal Tester Production VI V10

> **Biến thể giao diện TesterVN:** Bản này giữ nguyên logic Production V10 và chỉ thay giao diện. Xem `docs/UI_MIGRATION_TESTERVN_V10.md` và ảnh trong `docs/ui_previews/`.


Ứng dụng kiểm tra dây thật trên Raspberry Pi. V10 được sửa để chạy trên Raspberry Pi 4 Raspberry Pi OS 32-bit và vẫn dùng được trên Pi 4/Pi 5 64-bit.

## Điểm mới quan trọng nhất

V10 **không còn khóa cứng `/dev/ttyAMA4`** và không có nút chọn/quét UART.

Khi mở ứng dụng, phần mềm tự thực hiện:

1. Đọc cổng đã kết nối thành công lần trước từ `app.json`.
2. Thử cổng đó trước để kết nối nhanh.
3. Nếu không đúng hoặc cổng đã đổi, quét song song:
   - `/dev/serial0`, `/dev/serial1`
   - `/dev/ttyAMA*`
   - `/dev/ttyS*`
   - `/dev/ttyUSB*`
   - `/dev/ttyACM*`
4. Gửi `*IDN?` đến các cổng.
5. Chỉ nhận cổng trả về `Universal Tester`.
6. Gửi thêm `:MODELNAME?` để đọc model hiện tại.
7. Lưu cổng vừa tìm được để lần mở sau kết nối nhanh hơn.
8. Khi UART bị mất, tự quét và kết nối lại; người vận hành không cần bấm nút.

## Yêu cầu hệ thống

- Raspberry Pi OS 32-bit hoặc 64-bit.
- Python 3.10 trở lên. Khuyến nghị Raspberry Pi OS Bookworm.
- Tài khoản vận hành: `sa`.
- UART bo test: `115200 8N1`, CRLF.
- `pyserial` 3.5 trở lên.

## Cài đặt từ source ZIP

Giả sử file nằm tại:

```text
/home/sa/Downloads/JBZUniversalTester_Production_VI_V10.zip
```

Chạy từng lệnh:

```bash
cd /home/sa/Desktop
```

```bash
rm -rf /home/sa/Desktop/JBZUniversalTester_Production_VI_V10
```

```bash
unzip -o /home/sa/Downloads/JBZUniversalTester_Production_VI_V10.zip -d /home/sa/Desktop
```

```bash
cd /home/sa/Desktop/JBZUniversalTester_Production_VI_V10
```

```bash
chmod +x install.sh run_gui.sh install_desktop.sh build_native.sh tools/check_uart_devices.sh
```

```bash
./install.sh
```

Nếu `install.sh` vừa thêm người dùng vào nhóm `dialout`, khởi động lại:

```bash
sudo reboot
```

## Chạy phần mềm

```bash
cd /home/sa/Desktop/JBZUniversalTester_Production_VI_V10
```

```bash
./run_gui.sh
```

Khi mở, giao diện sẽ hiển thị một trong các trạng thái:

```text
ĐANG TỰ TÌM BO UNIVERSAL TESTER...
```

Sau đó:

```text
ĐÃ KẾT NỐI /dev/serial0 | Universal Tester ...
```

Tên cổng có thể là `serial0`, `ttyAMA0`, `ttyAMA4`, `ttyS0`, `ttyUSB0` hoặc tên khác. Người vận hành không cần chọn.

## Tạo biểu tượng Desktop

```bash
cd /home/sa/Desktop/JBZUniversalTester_Production_VI_V10
```

```bash
./install_desktop.sh
```

## Thư mục model và setup

```text
/home/sa/Models
/home/sa/Setups
```

Ví dụ:

```text
/home/sa/Models/WH322110.model
/home/sa/Setups/WH322110.setup
```

## Kiểm tra Pi 4 32-bit

Kiểm tra kiến trúc:

```bash
uname -m
```

Pi OS 32-bit thường trả:

```text
armv7l
```

Kiểm tra số bit:

```bash
getconf LONG_BIT
```

Kết quả cần là:

```text
32
```

Kiểm tra Python:

```bash
python3 --version
```

Python phải từ 3.10 trở lên.

## Kiểm tra UART nếu phần mềm vẫn chưa tìm được bo

Chạy script chẩn đoán:

```bash
cd /home/sa/Desktop/JBZUniversalTester_Production_VI_V10
```

```bash
./tools/check_uart_devices.sh
```

Hoặc kiểm tra trực tiếp:

```bash
ls -l /dev/serial* /dev/ttyAMA* /dev/ttyS* /dev/ttyUSB* /dev/ttyACM* 2>/dev/null
```

Nếu không có bất kỳ cổng nào, cần bật UART phần cứng trong Raspberry Pi OS:

```bash
sudo raspi-config
```

Trong giao diện cấu hình:

1. Vào **Interface Options**.
2. Chọn **Serial Port**.
3. Không bật login shell trên serial.
4. Bật phần cứng serial.
5. Khởi động lại Raspberry Pi.

```bash
sudo reboot
```

> Tự quét chỉ tìm những cổng mà hệ điều hành đã tạo. Nó không thể tự tạo `/dev/ttyAMA*` nếu UART hoặc overlay chưa được bật.

## Kiểm tra quyền UART

```bash
groups
```

Phải có nhóm:

```text
dialout
```

Nếu chưa có:

```bash
sudo usermod -aG dialout sa
```

Sau đó:

```bash
sudo reboot
```

## Kiểm tra cổng có bị ứng dụng khác giữ hay không

```bash
sudo fuser -v /dev/serial0 /dev/ttyAMA* /dev/ttyS* /dev/ttyUSB* /dev/ttyACM* 2>/dev/null
```

Chỉ một ứng dụng được dùng cổng của bo Universal Tester tại một thời điểm.

## Build native trên Pi 4 32-bit

Chạy build trực tiếp trên chính Pi 4 32-bit:

```bash
cd /home/sa/Desktop/JBZUniversalTester_Production_VI_V10
```

```bash
./build_native.sh
```

File đầu ra nằm trong:

```text
release/
```

Tên ví dụ:

```text
JBZUniversalTester_V10_pi4_arm32_bookworm_py311.tar.gz
```

Không dùng bản `arm64` cho hệ điều hành 32-bit.

## Cơ chế cache cổng

Cổng kết nối gần nhất được lưu tại:

```text
/home/sa/.config/JBZUniversalTesterProduction/app.json
```

Ví dụ:

```json
{
  "last_uart": "/dev/serial0",
  "baudrate": 115200
}
```

`last_uart` chỉ là cache tăng tốc. Nếu cổng không còn tồn tại hoặc không trả đúng `Universal Tester`, phần mềm bỏ cache và tự quét lại.

## Tự kết nối lại

Nếu reader UART phát hiện mất cổng:

1. Dừng chu kỳ đang chạy.
2. Hiện `MẤT KẾT NỐI BO - ĐANG TỰ KẾT NỐI LẠI...`.
3. Thử kết nối lại sau 250 ms.
4. Nếu chưa được, tăng khoảng chờ tối đa 2 giây để không chiếm CPU.
5. Khi bo xuất hiện lại, tự nhận diện và dùng tiếp.

## Chạy kiểm thử source

```bash
cd /home/sa/Desktop/JBZUniversalTester_Production_VI_V10
```

```bash
python3 -m pytest -q
```

Kết quả của gói phát hành V10:

```text
36 passed
```

## Tài liệu kỹ thuật

- `docs/UART_AUTO_DISCOVERY_V10.md`
- `docs/CHANGELOG_V10.md`
- `docs/BUILD_PI4_32BIT_V10.md`
- `docs/PROTOCOL.md`
- `docs/V9_MODEL_COLUMN_SPEC.md`
- `docs/PIN_PROBE_TRACE_ANALYSIS_20260805_172436.md`
