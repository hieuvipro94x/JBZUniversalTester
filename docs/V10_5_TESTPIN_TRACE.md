# V10.5 TestPin trace analysis

Trace: `20260807_145543_production_Htdrv3-JBZ27000_RT.zip`

## Kết luận TestPin

Trong chế độ que GND, frame là bảng trạng thái từng I/O chứ không phải bảng SOURCE->TARGET.

Ví dụ ở snapshot có que chạm IO2:

```text
80 00
A0 01
80 02
80 03
...
81 7F
C0 00
```

`A0 01` thay thế word `80 01`; do đó nghĩa là chính IO2 đang chạm GND. Không được hiểu thành IO1 nối IO2.

Các frame 1..24 không có A0/A1. Frame 25..28 có đúng `A0 01`, tương ứng IO(2). Mỗi snapshot đủ 256 word và mất khoảng 300-315 ms.

V10.5 vì vậy dùng `BoardScanMode.Probe` riêng và phát event ngay khi đọc A0/A1 để giảm độ trễ hiển thị.

## Production

Production vẫn giữ decoder SOURCE->TARGET đã rút ra từ trace test hàng trước; hai chế độ không dùng chung semantic.
