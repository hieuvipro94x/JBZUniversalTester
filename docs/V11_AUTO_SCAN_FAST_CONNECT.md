# V11 Auto Scan / Fast Connect

## Thiết bị được chấp nhận
- Description chứa `FT245R` và `USB FIFO`
- D2XX Device ID = `0x04036001` (VID 0x0403 / PID 0x6001)
- Serial không còn là điều kiện bắt buộc. Ví dụ trace/ảnh thực tế có thể là `A90764PH`.

## Startup
`MainWindow -> enumerate D2XX -> open đúng FT245R -> configure 115200/8N1 -> STOP -> purge -> INIT1 -> 350 ms -> INIT2 -> START_SCAN`

Handshake `8A 01 01 01 / 0F 00` không còn chặn startup.

## Scan nền
Ngay sau connect, `START_SCAN` được gửi theo số card cấu hình và worker đọc RX chạy liên tục. Trong lúc chỉ ở MainWindow, TestEngine production bị disable nên scan nền không thể tự PASS, báo lỗi hay kích relay.

Khi bấm **BẮT ĐẦU KIỂM TRA**, engine được reset + enable và sử dụng ngay frame kế tiếp từ worker đang chạy. Không restart scan nên phản hồi nhanh hơn.

## TestPin
Mở TestPin: Production -> Probe.
Đóng TestPin: Probe -> Production background scan.
Không đóng FTDI.

## Recovery
Nếu FT_Read/FT_GetQueueStatus lỗi thật, transport đóng handle hỏng. Monitor ViewModel kiểm tra 500 ms/lần và tự enumerate/open/start scan lại.

Monitor bị khóa trong các đoạn phần cứng có chủ ý dừng scan: PASS/relay/Keysight và xử lý wiring-fault.
