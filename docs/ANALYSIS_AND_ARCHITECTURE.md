# Kiến trúc phần mềm WPF production

## Nguồn dữ liệu đã dùng
- Trace FTDI/D2XX của chương trình JBZ gốc.
- Trace VISA Keysight IO Libraries của 34461A.
- File model `.tht` và quy tắc các chân cùng tên dây thuộc cùng một network.
- Giao diện tham chiếu từ project `JBZ.tar` chỉ dùng để dựng UI/luồng vận hành, không dùng giao thức UART của project đó.

## Luồng phần cứng

```text
WPF TestEngine
├── D2xxBoardTransport
│   ├── handshake 8A 01 01 01 / RX 0F 00
│   ├── reset 80 00 00 00
│   ├── start scan 8C 00 02 00
│   ├── stop scan 8D 00 00 00
│   ├── route resistance 90/91
│   └── relay 8E
└── KeysightVisaService
    ├── USB VISA resource
    └── :MEASURE:RES?
```

## Đánh giá network
Mỗi frame được ghép đến `C0 00`. Các record `A0 xx` tạo tập I/O active. Tập này được so sánh với network trong `.tht`. Network chỉ đạt sau số frame ổn định cấu hình. Cặp active không cùng network được ghi là chập/đấu sai.

## Điều kiện PASS
- Đủ toàn bộ network.
- Không có chập/đấu sai.
- Đủ toàn bộ bước điện trở và từng bước nằm trong Min/Max.
- Không có overrange `∞`.

Sau đó kích Relay 1 rồi Relay 2, luôn tắt relay ở cuối.
