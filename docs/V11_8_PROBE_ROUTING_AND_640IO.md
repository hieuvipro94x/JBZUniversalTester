# V11.8 - Probe routing + 640 I/O

## Lỗi gốc TestPin
Ở V11.7, `TestEngine` còn subscribe trực tiếp `IBoardTransport.FrameReceived` trong constructor.
Dù ViewModel đã có RuntimeMode Probe/Production, engine vẫn có một đường nhận frame riêng.
V11.8 bỏ hoàn toàn đường đó. Chỉ còn một router duy nhất trong `TestViewModel.OnBoardFrameReceived`:

- RuntimeMode.Probe + ScanFrame.Probe -> chỉ `PinProbeWindow`.
- RuntimeMode.Production + ScanFrame.Production -> chỉ `TestEngine.ProcessFrame`.
- Background / Shutdown -> không engine nào xử lý.

Do đó đầu dò GND không còn khả năng sinh `WrongWiring`, `Short`, popup hay âm TESTPOINT của production.

## Hành vi Probe
- Không chạm: không có dòng.
- Chạm IO không có THT: hiện riêng `IO(n)`.
- Chạm IO có THT: hiện IO/Giắc/Chân/Tên dây/Màu/Nối với theo THT.
- Nhả: dòng biến mất ở snapshot rỗng tiếp theo.

## Card / I/O
Theo yêu cầu cấu hình mới:
- 1 logical scan-card = 32 I/O.
- Firmware quét theo cặp logical card.
- 2 logical card = 64 I/O.
- 4 logical card = 128 I/O.
- ...
- 20 logical card = 640 I/O.

Giao diện cấu hình dùng `Card mở rộng` từ 1 đến 10:
- 1 card mở rộng = 2 logical scan-card = 64 I/O.
- 10 card mở rộng = 20 logical scan-card = 640 I/O.

Byte thứ ba của `8C 00 xx 00` được tự đồng bộ thành 2,4,6,...,20.
