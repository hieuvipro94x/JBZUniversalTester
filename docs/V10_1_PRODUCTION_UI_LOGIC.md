# JBZUniversalTester V10.1 - Production UI / Fault flow

## Luồng Start
- MainWindow: chọn model THT.
- Bấm `BẮT ĐẦU KIỂM TRA` đúng 1 lần.
- TestWindow mở, chờ hardware initialize, sau đó tự gọi `StartProductionTestAsync()`.
- TestWindow không còn nút `BẮT ĐẦU QUÉT I/O`.

## Bảng pin động
- Parser giữ toàn bộ PinRecord đọc được từ bảng Pin của THT, kể cả I/O đặc biệt.
- Ban đầu TestView hiển thị toàn bộ map pin.
- Network 2 chân: khi receiver A0 được confirm, cả source + receiver biến mất.
- Receiver về 80: cả hai dòng xuất hiện lại ngay.
- Splice: target nào A0 thì target đó biến mất; source chỉ biến mất khi toàn bộ branch đạt.
- `OpenCount` = số FaultRow Kind=Open đang còn hiển thị, không phải số network.
- `NetworkProgress` vẫn = PassedNetworks / ExpectedNetworks.

## Đấu sai / chập mạch
- `TestEngine.UpdateUnexpectedIo()` coi A0 không thuộc receiver hợp lệ của THT và không nằm trong IgnoredIo là bất thường.
- Bất thường phải tồn tại ít nhất `ProductionSettings.ShortConfirmMs` trước khi latch lỗi.
- Khi latch:
  1. Dòng pin tương ứng chuyển Kind=WrongWiring và được DataGrid tô đỏ.
  2. TESTPOINT.wav phát lặp liên tục.
  3. Production scan dừng, relay PASS/Keysight bị khóa bởi `_cycleActive=false`.
  4. Popup hiển thị I/O, connector, pin, wire.
  5. Người vận hành bấm OK -> dừng âm -> pulse relay mở jig.
  6. Scan được bật lại ở chế độ chờ tháo sản phẩm, không cho PASS/popup lặp.
  7. Khi không còn bất kỳ I/O production nào active -> reset engine và tự re-arm.
  8. Lắp lại sản phẩm -> test tự động, không cần bấm Start lần nữa.

## Relay mở jig sau lỗi
Cấu hình trong appsettings.json:
```json
"FaultEjectRelay": 2,
"FaultEjectPulseMs": 250
```
Mặc định Relay 2. Có thể đổi sang 1 nếu jig thực tế dùng Relay 1.

## Giao thức scan giữ nguyên V10
- `80 nn`: I/O nn normal
- `A0 nn`: I/O nn active
- `C0 00`: end frame
- 128 channel/frame
