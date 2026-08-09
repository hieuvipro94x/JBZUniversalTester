# V11.2 - Probe GND và state machine sản xuất

## TestPin
Que TestPin là một dây GND riêng từ bo. Ở cửa sổ PinProbeWindow, stream Probe
chỉ dùng để xác định I/O vật lý đang bị chạm. Không dùng quan hệ đó để suy ra
short/wrong-wiring. Khi nhận IO(n), UI tra `ProductModel.Pins`; nếu pin có tên
dây, UI tra `ProductModel.Nets` để liệt kê các I/O còn lại cùng network.

## Production state
- Không có quan hệ continuity: `CHỜ LẮP SẢN PHẨM`.
- Có ít nhất một quan hệ nhưng chưa đủ PASS: `ĐANG KIỂM TRA...`.
- Đủ continuity + điện trở (nếu có): `PASS` nền xanh, âm thanh và relay cùng trigger.
- Sau PASS, khi bất kỳ expected connection nào mất: chuyển ngay về `CHỜ LẮP SẢN PHẨM`.
- Khi harness mới bắt đầu xuất hiện connection: chuyển ngay sang `ĐANG KIỂM TRA...`.
