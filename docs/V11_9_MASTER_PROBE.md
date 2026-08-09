# V11.9 MASTER SAMPLE / PROBE

## Đầu dò GND
Hai lớp bảo vệ được dùng:
- PinProbeWindow dùng BoardScanMode.Probe độc quyền.
- Khi TestView đang ở Production nhưng firmware trả chữ ký fan-out dày đặc trưng của que GND, frame được phân loại là ProbeContact trước TestEngine. Vì vậy không sinh WrongWiring/popup.

I/O không có map THT vẫn hiển thị `IO(n)`. I/O có map hiển thị thêm Connector/Pin/Wire/Color và I/O liên quan theo THT.

## Master Sample Gate
Mỗi lần nạp model, gate reset. Người vận hành phải:
1. Bấm TEST MASTER ĐẠT, lắp mẫu đạt và để máy xác nhận PASS hoàn chỉnh.
2. Bấm TEST MASTER LỖI, lắp mẫu lỗi/chập hoặc mẫu có lỗi điện trở và để máy phát hiện lỗi.
3. Khi hai ô Mẫu đạt OK / Mẫu lỗi OK đều được tick, bấm XÁC NHẬN 2 MASTER.
4. Production mới được mở khóa.

Master sample không ghi production.statistics.json và không tăng LOTNO.
