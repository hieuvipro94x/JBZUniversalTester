# Giao thức Production V5

UART: tự động phát hiện trong `/dev/serial*`, `/dev/ttyAMA*`, `/dev/ttyS*`, `/dev/ttyUSB*`, `/dev/ttyACM*`; `115200 8N1`, CRLF.

## Nhận dạng

```text
TX *IDN?
RX Universal Tester ...
TX :MODELNAME?
RX :MODELNAME,<model>,<source_record_count>
```

## Tải model

```text
:MODEL
:PINCOUNT
:PINDATA
:ARRAYCOUNT
:ARRAY
:CONCOUNT
:CON
:CONNECTORCOUNT
:CONNECTOR
:FINISH
:RESET
```

Sau reset:

```text
RX BootLoader
TX :STOP
RX BOOT
```

Đóng UART, chờ khoảng 2,5 giây rồi mở phiên vận hành.

## Test

```text
TX :START
RX :START,ON
RX :MEASURE
TX :MAXEXT,0
RX :CLEAR
RX :OPEN,...       # snapshot/update theo network id
RX :OTHER,...      # đấu sai
RX :CIRCUIT,0|1   # kết luận cuối
```

### OPEN replacement

```text
:OPEN,10,10,11  # active[10] = (10,11)
:OPEN,10,11     # active[10] = (11)
:OPEN,10        # xóa active[10]
```

Không cộng dồn lịch sử OPEN.

### OTHER pair

```text
:OTHER,113,123
:OTHER,123,113
```

Hai hướng là một cặp lỗi; hiển thị hai dòng S/E nhưng bộ đếm tăng một.

## PASS + marking

```text
RX :CIRCUIT,0
~300 ms
TX :PASSPEN,500,<physical_pin_count>
RX :PEN
TX :UNCONNECT,500,<physical_pin_count>
RX :REMOVAL
RX :UNCONNECT
TX :START
```

## FAIL + xác nhận

```text
RX :CIRCUIT,1
GUI hiện hộp lỗi
Người vận hành bấm XÁC NHẬN
TX :UNCONNECT,500,<physical_pin_count>
RX :REMOVAL
RX :UNCONNECT
TX :START
```

Không STOP và không tải lại model trong luồng này.

## Dừng/về menu

```text
TX :STOP
RX :STOP     # firmware có thể ACK
```

Sau đó tắt OUTPUT 0–4 an toàn.

## Đầu dò GND — trace thật 2026-08-05 17:24

Không có command TX để bật đầu dò. Khi chu kỳ START đang chạy, firmware tự phát:

```text
RX :TESTPIN,<physical_pin>,ON
RX :TESTPIN,<physical_pin>,OFF
```

Có thể có nhiều pin ON đồng thời. Số pin là physical pin trực tiếp trong file `.model`, không cộng hoặc trừ 1.

Đầu dò có thể làm OPEN của mạng liên quan thay đổi trong lúc chạm, ví dụ:

```text
RX :TESTPIN,125,ON
RX :OPEN,82
RX :TESTPIN,125,OFF
RX :OPEN,82,82,125
```

Phần mềm phải xử lý TESTPIN và OPEN độc lập: TESTPIN hiển thị vị trí que dò, OPEN cập nhật snapshot hở mạch.
