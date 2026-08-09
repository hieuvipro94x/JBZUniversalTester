# JBZUniversalTester V13.0.0 DUAL BOARD

## Mục tiêu
Một phần mềm Windows chạy được hai họ bo:

1. **JBZ D2XX** – FT245R/D2XX binary scan như V12.
2. **JBZ UART TTL** – firmware đã chạy trên Raspberry Pi, protocol ASCII 115200 8N1 CRLF.

## Cài đặt LOẠI BO MẠCH

- Tự động nhận dạng
- JBZ D2XX
- JBZ UART TTL
- COM ưu tiên cho UART TTL (có thể để trống để tự dò)

AUTO ưu tiên D2XX đúng VID/PID/description hiện có. Nếu D2XX không có, phần mềm dò COM và chỉ nhận UART khi `*IDN?` trả `Universal Tester...`.

## UART TTL

- 115200 baud
- 8 data bits
- parity none
- 1 stop bit
- CRLF
- flow control off
- DTR/RTS off

Handshake:

```
TX *IDN?
RX Universal Tester...
TX :MODELNAME?
RX :MODELNAME,...
```

Background chỉ lắng nghe; không tự gửi `:START` khi vừa kết nối.

Chu kỳ:

```
TX :START
RX :START,ON
RX :MEASURE
TX :MAXEXT,0
RX :CLEAR
RX :OPEN,... / :OTHER,... / :TESTPIN,...
RX :CIRCUIT,0|1
```

PASS:

```
:CIRCUIT,0
~300 ms
TX :PASSPEN,500,<pin_count>
RX :PEN
TX :UNCONNECT,500,<pin_count>
RX :REMOVAL
RX :UNCONNECT
TX :START (chu kỳ mới)
```

FAIL:

```
:CIRCUIT,1
Popup XỬ LÝ HÀNG KHÔNG ĐẠT
XÁC NHẬN
TX :UNCONNECT,500,<pin_count>
RX :REMOVAL
RX :UNCONNECT
TX :START (chu kỳ mới)
```

## TEST PROBE PIN

- UART TTL: dùng trực tiếp `:TESTPIN,<io>,ON/OFF` từ firmware, không suy đoán.
- Một số firmware dùng `:PIN,<io>,0/1` cũng được quy đổi sang TESTPIN.
- D2XX: giữ `ProbeContactClassifier` hiện tại.
- Hai backend cùng hiển thị chung card Probe trên TestView.
- Card Probe V13 hiển thị: I/O, Giắc, Chân, Dây, Dập nối, Tiết diện, Màu.
- Probe không bao giờ tự tạo FAIL hay kích relay.

## FAIL / JIG

### D2XX
Sau operator xác nhận FAIL:

- Relay 1 JIG được pulse theo StampDelay R1.
- Relay 2 MARKING luôn OFF.
- Sau eject, scan lại để bắt buộc nhận biết sản phẩm đã tháo.

### UART TTL
Sau operator xác nhận FAIL:

- Không giả lập relay D2XX.
- Gửi `:UNCONNECT` để firmware/bo thực hiện đúng chuỗi Pi.
- Chờ `:REMOVAL` rồi `:UNCONNECT` trước chu kỳ mới.

## Lưu cấu hình
`production.settings.json` có thêm:

- `BoardMode`
- `UartPort`

`UniversalTester.cfg` cũng ghi/đọc:

- `[BoardMode]Auto|D2xx|UartTtl`
- `[UartPort]COMx`

Khi đổi BoardMode/UartPort trong Settings, runtime disconnect và nhận dạng lại bo, không cần restart ứng dụng.

## Không thay đổi

- D2XX command/frame decoder.
- TestEngine continuity D2XX.
- Resistance/Keysight D2XX.
- Product history/statistics/printing.
- Probe classifier D2XX.
- Master state machine D2XX.

UART TTL bỏ Master state-machine D2XX vì firmware trả kết quả cuối bằng `:CIRCUIT`; status Master được chuyển sang `UART TTL • KẾT QUẢ THEO FIRMWARE (:CIRCUIT)`.

## Lưu ý phần cứng UART
Xác nhận mức logic của bo trước khi nối. Nếu MCU/bo là TTL 3.3 V, dùng USB-UART 3.3 V. Nối chung GND và đấu chéo TX/RX.
