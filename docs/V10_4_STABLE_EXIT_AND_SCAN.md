# V10.4 - Stable Exit / Fast Scan

## Lỗi từ log 2026-08-07
Log kết thúc bằng `System.IO.InvalidDataException` với thông báo
`Không thể mở OLE storage của file THT/THA.`. Đây là lỗi parser model, không phải
lỗi FTDI. V10.4:
- đọc file với FileShare.ReadWrite/Delete;
- retry khi file vừa được copy/ghi;
- bắt lỗi tại màn chọn model để app không crash.

## Cleanup
Mọi đường thoát đều chờ:
1. StopScan
2. Relay OFF
3. Reset/Clear board
4. FT_Close
5. VISA close
6. Audio/TestPin stop
7. Event unsubscribe

TestWindow và PinProbeWindow không đóng UI trước khi scan worker dừng.

## Pin Probe
Đã bỏ nút `DỪNG AN TOÀN`.
Dùng `VỀ TRANG CHÍNH` hoặc nút X; cleanup chạy tự động.

## Scan optimization
- D2XX reader dùng một LongRunning worker.
- FTDI latency theo USB delay (1 ms trong cấu hình trace).
- `FT_Read` feed trực tiếp Span, không cấp phát byte[] cho từng read.
- DataGrid fault rows đồng bộ vi sai, không Clear/Add lại hàng trăm dòng mỗi thay đổi.
- RX log được throttle, nhưng TestEngine vẫn nhận toàn bộ frame.
- Worker exception/cancel không làm UI bị kẹt khi đóng.
