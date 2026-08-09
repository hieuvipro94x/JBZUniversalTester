# Trạng thái triển khai V2

## Đã triển khai
- WPF/MVVM.
- Parser `.tht` CP949, chọn revision cuối.
- Nhóm network theo tên dây, hỗ trợ splice.
- FTDI D2XX thật, handshake/reset/start/stop scan.
- Ghép frame đến `C0 00`, giải mã `A0 xx`.
- Đánh giá network đúng và cặp chập/đấu sai.
- VISA Keysight 34461A, `:MEASURE:RES?`, hiển thị `∞` khi overrange.
- Chọn route điện trở qua lệnh `90/91` cấu hình được.
- Relay 1/2 tự động sau PASS và test thủ công.
- Không còn UART ASCII.

## Phải xác minh trên phần cứng trước production
- Cấu trúc card thứ hai và I/O trên 128.
- Route điện trở R2-R5.
- Số frame ổn định tối ưu.
- Thời gian settle và pulse relay.
