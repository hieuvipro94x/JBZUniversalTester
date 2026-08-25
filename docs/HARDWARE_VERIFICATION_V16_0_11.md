# HARDWARE VERIFICATION V16.0.11

Thời điểm kiểm tra: 2026-08-24, múi giờ Asia/Bangkok. Kiến trúc chạy: Windows x86.

## 1. Thông tin FTDI và D2XX

- D2XX library version: `0x00030221`.
- Bo mục tiêu: `FT245R USB FIFO`.
- Serial: `AI050MBB`.
- VID/PID ID: `0x04036001` (`VID 0403`, `PID 6001`).
- Location khi kiểm tra chính: `0x00000017`.
- Trạng thái trước khi mở: `Open=False`.
- Các FTDI UART khác được enumerate nhưng không được coi là bo D2XX tester.
- 30/30 chu kỳ connect/disconnect: **PASS**.
- 100/100 chu kỳ start/stop production scan: **PASS**.
- Smoke test từ bản HardwareVerification đã publish: **PASS**.

Phần runtime được sửa để dừng và liệt kê thiết bị nếu có nhiều FT245R phù hợp nhưng không có `FtdiSerial` khớp duy nhất. Thiết bị đang mở được báo là occupied thay vì “không tìm thấy bo”.

## 2. Keysight/VISA

- Discover thấy một resource: `USB0::10893::4865::my57222903::0::INSTR`.
- `viOpen` trả `0xBFFF0060`; không lấy được `*IDN?`.
- Không cưỡng đóng Keysight IO Libraries/Connection Expert và không chiếm thiết bị bằng cách khác.
- Đo resistance thực tế, raw response, open-input và thời gian VISA: **CHƯA XÁC MINH**.

## 3. TX/RX đã xác minh

Trace trên bo thật xác nhận:

- Startup/handshake có `8D 00 00 00`, `8A 01 01 01`, RX handshake `0F 00`.
- Recovery/release có `91 00 00 00`, `90 00 00 30`.
- Relay OFF an toàn có `8E 00 00 00`; không kích Relay 1/2 trong bài tự động.
- Start scan 128 I/O dùng `8C 00 02 00`.
- RX frame bị chia qua nhiều lần `FT_Read` nhưng decoder giữ buffer đến `C0 00`.
- Có các lần đọc chứa phần cuối source cùng `C0 00`, ví dụ `80 7F C0 00`.
- Trong soak không có incomplete frame hoặc unknown byte.

Không gửi opcode Probe `0x81`.

## 4. Kết quả CH1-CH10

| Channel | TX route A | TX route B | Kết quả bo thật |
|---|---|---|---|
| CH1 | `90 00 00 01` | `91 00 00 01` | PASS |
| CH2 | `90 00 00 01` | `91 00 00 02` | PASS |
| CH3 | `90 00 00 01` | `91 00 00 03` | PASS |
| CH4 | `90 00 00 01` | `91 00 00 04` | PASS |
| CH5 | `90 00 00 01` | `91 00 00 05` | PASS |
| CH6 | `90 00 00 01` | `91 00 00 06` | PASS |
| CH7 | `90 00 00 01` | `91 00 00 07` | PASS |
| CH8 | `90 00 00 01` | `91 00 00 08` | PASS |
| CH9 | `90 00 00 01` | `91 00 00 09` | PASS |
| CH10 | `90 00 00 01` | `91 00 00 0A` | PASS |

Trace chứng minh selector trực tiếp, không phải bitmask. Sau bài test có ba recovery cycle đúng lệnh release hiện có. Mỗi channel mới được kích một lần; bài lặp 20 lần/channel được để **CHƯA XÁC MINH** nhằm tránh làm mòn relay khi chưa có xác nhận của người vận hành.

Self-test bổ sung xác nhận cấu hình `R1→CH8`, `R2→CH2`, `R3→CH10`, `R4→CH4`, `R5 Disabled`, `R6 Enabled/CH0`, `R7→CH7`, `R8→CH7`: thứ tự route đúng R1-R10; R5/R6 không route, không gọi Keysight và không tạo result; CH7 trùng vẫn được đo hai slot riêng.

## 5. Phạm vi I/O 1-128

- Trace thật quan sát frame NORMAL tuần tự từ protocol address `00` đến `7F`, tương ứng UI/model I/O 1 đến 128.
- Các biên 1, 8, 16, 32, 64, 65, 127 và 128 đều xuất hiện trong frame.
- I/O 128 xuất hiện dưới dạng source `80 7F`, sau đó là `C0 00`; không tràn hoặc mất chân 128.
- Dữ liệu split-read được tái lập thành frame hoàn chỉnh.
- Nhiều frame trong cùng một lần đọc: **CHƯA QUAN SÁT ĐƯỢC trên trace thật**; vector self-test đã PASS.
- ACTIVE/target `A0 nn` tại các chân do jig tác động: **CHƯA XÁC MINH ĐỦ 128 chân**, vì không tự thay đổi dây vật lý.

## 6. OPEN, WRONG, SHORT và Probe

- Self-test engine cho OPEN/WRONG/SHORT, Probe không che fault và Probe không tạo FAIL: PASS.
- Xác minh bằng thay đổi dây trên bo thật: **CHƯA XÁC MINH**, cần người vận hành tạo lần lượt từng trạng thái.
- Probe TOUCH/RELEASE trên bo thật: **CHƯA XÁC MINH**.
- Opcode `0x81`: **CHƯA XÁC MINH payload/ACK**, không được gửi thử.

## 7. Relay JIG/MARKING

- Mapping `0x8E` trong production không thay đổi.
- Relay OFF `8E 00 00 00` được ghi nhận trong cleanup.
- Pulse Relay 1 JIG và Relay 2 MARKING trên máy thật: **CHƯA XÁC MINH**, vì có thể làm chuyển động jig/van khí và cần người vận hành xác nhận vùng cơ khí an toàn.
- Self-test lifecycle PASS/FAIL và interlock relay: PASS.

## 8. Soak test

Thời lượng thụ động thực tế: `600.375 s`.

- RX bytes: `1,000,654`.
- TX bytes: `36`.
- Complete frames: `3,878`.
- Incomplete frames: `0`.
- Unknown bytes: `0`.
- Khoảng cách frame lớn nhất: `166.496 ms`.
- CPU trung bình của verifier: `0.657%`.
- Reconnect trong bài riêng: 30/30 PASS.
- Start/stop scan trong bài riêng: 100/100 PASS.
- Final-publish smoke: 10.38 s, 16,698 RX byte, 64 complete frame, 0 incomplete, 0 unknown, CPU 0.753%.

Không quan sát thấy handle kẹt, event RXCHAR dừng, decoder giữ byte rác, frame trộn, exception cleanup hoặc lỗi reconnect.

## 9. Lỗi thực tế và thay đổi

Các lỗi/rủi ro tìm thấy:

1. `TestEngine` và `TestViewModel` tự dựng danh sách resistance riêng, có nguy cơ lệch slot/channel.
2. Normalize dùng `ToDictionary`, cấu hình trùng tên R có thể ném exception.
3. Slot trống tên, đảo thứ tự, channel/Min/Max lỗi chưa được normalize đầy đủ.
4. Runtime trước đây có thể chọn bo đầu tiên nếu nhiều FT245R cùng loại và serial không khớp.
5. Chưa có chế độ verifier riêng và trace TX/RX độ phân giải cao.

Đã sửa:

- Thêm `ResistanceMeasurementPlan` làm nguồn duy nhất cho normalize và danh sách bước R1-R10.
- `D2xxResistanceRouting` là nguồn duy nhất dựng route/select/release.
- Engine và UI chỉ lấy danh sách từ helper chung.
- Duplicate giữ bản ghi hợp lệ đầu tiên; cấu hình lỗi được clamp và cảnh báo.
- UI cố định R1-R10, dropdown Không dùng/CH1-CH10.
- Loại RouteA/RouteB legacy khỏi `appsettings.example.json`.
- Thêm enumerate, phát hiện occupied/ambiguous và protocol trace tùy chọn.
- Thêm console HardwareVerification x86 tách khỏi production UI.

## 10. Tổng hợp trạng thái

| Hạng mục | Trạng thái |
|---|---|
| Build Release x86 | PASS |
| Self-test | 24/24 PASS |
| Audit WPF binding | PASS |
| Enumerate đúng bo | PASS |
| 30 connect/disconnect | PASS |
| 100 start/stop | PASS |
| Soak 10 phút | PASS |
| RX 1-128 NORMAL/source | PASS |
| Route CH1-CH10 | PASS |
| Resistance đo bằng Keysight | CHƯA XÁC MINH (`viOpen 0xBFFF0060`) |
| OPEN/WRONG/SHORT trên dây thật | CHƯA XÁC MINH |
| Probe TOUCH/RELEASE thật | CHƯA XÁC MINH |
| Relay JIG/MARKING thật | CHƯA XÁC MINH – cần xác nhận an toàn cơ khí |
| Rút/cắm USB | CHƯA XÁC MINH – cần người vận hành |

## 11. Trace và bản publish

Trace:

- `Data/HardwareVerification/scan_cycles_v16_0_11.trace` — SHA-256 `381B8BD82ED1F0F4FD1E330E17E5C0A960A8EB05C688619055F03E811E692621`.
- `Data/HardwareVerification/passive_10min_v16_0_11.trace` — SHA-256 `2A89279B54920619481583DF24A39AEBBB684AD704C6841739BEA2587391A788`.
- `Data/HardwareVerification/resistance_ch1_ch10_v16_0_11.trace` — SHA-256 `D2D2F7033B41AB4E73A6A40715A7A23F60FE2D19F8BBA60D69C1347EC1D6CF89`.

Bản production:

- `bin/Release/net8.0-windows/win-x86/publish/JBZUniversalTester_V16_0_11.exe`
- File version: `16.0.11.0`.
- Product version: `16.0.11`.
- SHA-256: `70A62471B5FE3D3CD08B5FF319AE5D9D3CE761C40AF2F709E0E35F82422C23FD`.

Hardware verifier:

- `HardwareVerification/bin/Release/net8.0-windows/win-x86/publish/JBZUniversalTester.HardwareVerification.exe`
- File version: `16.0.11.0`.
- Product version: `16.0.11`.
- SHA-256: `30CE3ABB584739ADD4929BDC46F1CDD0FE599AEA69E348F85C340200DD2CDA0F`.
