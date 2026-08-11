# Windows V11 - sửa lỗi không tìm thấy bo

## Nguyên nhân phần mềm đã sửa

1. Windows V10 dùng `uart_probe_timeout_ms=280`. Mốc 280 ms phù hợp UART trực tiếp trên Pi nhưng có thể quá ngắn khi qua USB-UART trên Windows.
2. Parser vận hành chấp nhận cả `Universal Tester...` và `UniversalTester...`, nhưng auto-discovery V10 chỉ nhận dạng có dấu cách.
3. Ngay sau khi mở COM, V10 gửi `*IDN?` gần như lập tức. Một số driver USB-UART Windows cần khoảng ổn định ngắn.

## Thay đổi

- Windows ép timeout discovery tối thiểu 1800 ms.
- Sau khi mở COM chờ 120 ms trước handshake.
- `*IDN?` được thử lại một lần trong cùng cửa sổ timeout.
- Nhận cả `Universal Tester` và `UniversalTester`.
- Config Windows V10/V11 được tự migrate lên config version 12 với timeout 1800 ms.
- `tools/check_uart_windows.py` nay in đầy đủ COM, VID/PID/HWID, lỗi COM bận, TX/RX thô và kết luận dây/driver.

Không thay đổi protocol 115200 8N1 CRLF, command test, model, fault logic hoặc GUI Production.
