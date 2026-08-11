# UART Auto Discovery – V10

## Mục tiêu

Một source chạy trên Pi 4/Pi 5 và không phụ thuộc tên cổng cụ thể. Không hiển thị nút quét UART cho người vận hành.

## Module

```text
jbz_uart/
├── __init__.py
└── manager.py
```

## Danh sách cổng

`candidate_ports()` thu thập từ glob `/dev` và `serial.tools.list_ports`:

```text
/dev/serial0
/dev/serial1
/dev/ttyAMA*
/dev/ttyUSB*
/dev/ttyACM*
/dev/ttyS*
```

Các alias trỏ cùng thiết bị thật được loại trùng bằng `os.path.realpath()`.

## Thứ tự ưu tiên

1. `last_uart` đã lưu.
2. `/dev/serial0`.
3. `/dev/serial1`.
4. `ttyAMA*`.
5. `ttyUSB*`.
6. `ttyACM*`.
7. `ttyS*`.

## Nhận diện bo

Mỗi cổng được mở tạm với:

```text
115200, 8N1, no flow control
```

Trình tự:

```text
TX *IDN?
RX phải chứa Universal Tester
TX :MODELNAME?
RX :MODELNAME,... nếu firmware đã có model
```

Cổng không trả đúng IDN bị đóng ngay.

## Quét nhanh

- Cổng cache được thử riêng trước.
- Nếu cache sai, các cổng còn lại được probe bằng `ThreadPoolExecutor`.
- Probe chạy song song, không chờ timeout tuần tự.
- Khi có kết quả đúng đầu tiên, hủy các probe chưa chạy và dùng cổng đó.
- Timeout mặc định IDN: 280 ms.
- Timeout MODELNAME: 180–200 ms.

## Kết nối vận hành

`MainWindow._connect_worker()`:

1. Gọi `UartManager.discover()`.
2. Tạo `BoardController` trên cổng đã tìm.
3. Dùng identity từ probe, tránh gửi lại handshake không cần thiết.
4. Lưu `last_uart` bằng `save_config()`.

## Kết nối Model Loader

`SerialSession` nhận `port=None` và tự gọi `UartManager`. Vì vậy tải model cũng không phụ thuộc `/dev/ttyAMA4`.

CLI mặc định:

```bash
--port auto
```

Vẫn có thể chỉ định cổng thủ công trong CLI để chẩn đoán, nhưng giao diện sản xuất không có lựa chọn cổng.

## Mất kết nối

`BoardController` gọi `disconnect_callback` khi `pyserial` báo `SerialException`.

GUI:

```text
connection_lost
→ board = None
→ dừng trạng thái testing
→ tự schedule reconnect
```

Delay reconnect tăng từ 250 ms đến tối đa 2 giây.

## Cache cấu hình

```text
~/.config/JBZUniversalTesterProduction/app.json
```

Trường V10:

```json
{
  "last_uart": "/dev/serial0",
  "baudrate": 115200,
  "config_version": 10
}
```

Khi đọc cấu hình V9, trường cũ `port` được tự chuyển sang `last_uart`.
