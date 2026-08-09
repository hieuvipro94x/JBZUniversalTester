# V10.6 - TestPin Continuous Probe

## Hành vi đúng

- Mở cửa sổ **KIỂM TRA CHÂN PIN** là tự động bắt đầu scan Probe.
- Scan chạy liên tục cho đến khi đóng cửa sổ.
- Không có nút `BẮT ĐẦU DÒ CHÂN`.
- `80/81 nn`: I/O bình thường, không hiển thị.
- `A0/A1 nn`: chính I/O đó đang bị que GND chạm; hiển thị ngay một I/O duy nhất.
- Không cộng dồn nhiều A0/A1 trong một vòng thành lỗi chập.
- Nếu hết một vòng (`C0 00`) mà không có A0/A1 thì xóa I/O cũ: que đã nhả.
- Khi model THT đã nạp, I/O được tra ngược để hiển thị tên dây, giắc, chân, tiết diện và màu dây.
- Probe không được đưa vào production TestEngine: không Short/Wrong, không PASS/FAIL, không Keysight, không relay.

## Ví dụ

`80 00  A0 01  80 02 ... C0 00`

=> chỉ hiển thị `IO(2)`.

Vòng kế tiếp không có A0/A1:

`80 00 80 01 80 02 ... C0 00`

=> xóa `IO(2)` và trở về trạng thái `ĐANG QUÉT LIÊN TỤC - CHƯA CHẠM I/O`.
