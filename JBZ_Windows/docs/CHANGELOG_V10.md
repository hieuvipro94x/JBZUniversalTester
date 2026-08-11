# Changelog V10

## UART

- Loại bỏ cổng cố định `/dev/ttyAMA4` khỏi GUI Production.
- Thêm package `jbz_uart`.
- Tự quét `serial0`, `serial1`, `ttyAMA*`, `ttyS*`, `ttyUSB*`, `ttyACM*`.
- Probe song song để giảm thời gian tìm cổng.
- Xác nhận đúng bo bằng `*IDN?` và `:MODELNAME?`.
- Lưu `last_uart` để lần mở sau kết nối nhanh.
- Tự kết nối lại khi reader UART báo mất cổng.
- Delay reconnect có backoff 250–2000 ms.
- Settings không còn ô nhập tên UART.
- Model Loader GUI không còn ô cổng và nút kết nối lại.
- Model Loader CLI mặc định `--port auto`.

## Pi 4 32-bit

- Thêm `build_native.sh` tự nhận `armv7l` thành `arm32`.
- Thêm script `tools/check_uart_devices.sh`.
- `install.sh` tự thêm người dùng vào nhóm `dialout`.
- Tài liệu riêng cho Pi 4 32-bit.

## Cấu hình

- `config_version` tăng lên 10.
- Thêm `last_uart`.
- Tự migrate trường `port` của V9.
- Thêm `reconnect_delay_ms` và `uart_probe_timeout_ms`.

## Giữ nguyên chức năng V9

- Cấu trúc bảng 12 cột.
- Dây dập nối hai chiều.
- Tiết diện và màu dây.
- OPEN trạng thái sống.
- OTHER ưu tiên trên đầu.
- Đầu dò GND tự phát TESTPIN.
- Marking và xử lý PASS/FAIL theo trace thật.

## Kiểm thử

- 32 test V9 giữ nguyên.
- 4 test mới cho UART Manager.
- Tổng: 36 test.
