# BÁO CÁO CODEX REVIEW – JBZUniversalTester V14.0.0

## 1. Mục tiêu V14
Một ứng dụng Windows chạy hai họ bo nhưng không trộn cấu trúc phần cứng/model:
- JBZ D2XX: FTDI/D2XX + THT + TestEngine + BoardCapacity/Card mở rộng.
- JBZ UART TTL: COM 115200 8N1 CRLF + firmware Pi + model/profile protocol riêng.

Settings giữ đúng:
- Tự động nhận dạng
- JBZ D2XX
- JBZ UART TTL

## 2. Thay đổi quan trọng so với V13
### 2.1 Card mở rộng
Card mở rộng là cấu hình D2XX, không phải cấu hình chung.
- Forced UART: nhóm Card/I/O D2XX bị disable.
- AUTO: vẫn cho cấu hình D2XX để dùng nếu AUTO nhận D2XX; khi runtime active UART thì kiểm tra capacity D2XX không được chặn model.
- Không dùng công thức CardCount * 64 để suy ra pin count UART.

### 2.2 Model
V14 tách hai profile:
- D2XX: `LastThtPath`, parse bởi `ThtModelParser`, chạy `TestEngine`.
- UART: `LastUartModelPath`, parse bởi `UartModelProfile`.

Một mã hàng ở UI vẫn là một mã hàng, nhưng backend lấy profile riêng. Không convert THT sang UART command bằng suy đoán.

UART profile được hỗ trợ an toàn:
- `.profile.json`
- `.uart.txt`
- `.protocol.txt`

Nếu `LastUartModelPath` trống, app tìm sidecar cạnh THT cùng stem:
- `<model>.profile.json`
- `<model>.uart.txt`
- `<model>.protocol.txt`

### 2.3 Đồng bộ model UART
Sau khi model D2XX/UI được chọn và backend active là UART:
1. Load UART profile.
2. TX `:MODELNAME?`.
3. Nếu model trên board giống profile: không upload.
4. Nếu khác: upload tuần tự profile firmware.
5. Mỗi command phải nhận ACK đúng mới đi tiếp.

Các family được cho phép theo golden protocol:
`MODEL`, `PINCOUNT`, `PINDATA`, `ARRAYCOUNT`, `ARRAY`, `CONCOUNT`, `CON`, `CONNECTORCOUNT`, `CONNECTOR`, `FINISH`.

ACK mặc định:
- MODEL -> `:OK,MODEL`
- PINCOUNT -> `:OK,PINCOUNT`
- PINDATA,n -> `:OK,PINDATA,n`
- ARRAYCOUNT -> `:OK,ARRAYCOUNT`
- ARRAY,n -> `:OK,ARRAY,n`
- CONCOUNT -> `:OK,CONCOUNT`
- CON,n -> `:OK,CON,n`
- CONNECTORCOUNT -> `:OK,CONNECTORCOUNT`
- CONNECTOR,n -> `:OK,CONNECTOR,n`
- FINISH -> prefix `:OK,FINISH,`

Parser validate count và index liên tục trước upload. Không hỗ trợ family chưa có golden ACK bằng cách tự đoán.

## 3. Những phần V13 phải giữ nguyên
- AUTO ưu tiên D2XX rồi fallback UART.
- UART handshake `*IDN?` / `Universal Tester...`.
- UART không tự `:START` chỉ vì connect.
- `:MAXEXT,0` chỉ gửi sau `:MEASURE`.
- UART TESTPIN dùng firmware `:TESTPIN,<io>,ON/OFF`.
- D2XX TESTPIN dùng `ProbeContactClassifier`.
- Probe không tạo fault, không relay, không Keysight, không tăng sản lượng.
- UART OPEN là snapshot; OTHER normalize cặp.
- PASS UART: CIRCUIT,0 -> 300ms -> PASSPEN -> PEN -> UNCONNECT -> REMOVAL -> UNCONNECT.
- FAIL UART: CIRCUIT,1 -> operator confirm -> UNCONNECT -> REMOVAL -> UNCONNECT.
- FAIL D2XX: operator confirm -> chỉ Relay 1 eject; Relay 2 marking cấm.
- Không re-arm trước ProductRemoved.

## 4. File V14 mới/sửa chính
- `Models/UartModelProfile.cs` – profile firmware UART riêng.
- `Models/ProductionSettings.cs` – thêm `LastUartModelPath`.
- `Services/IFirmwareProtocolBoard.cs` – QueryModelName/UploadModelProfile.
- `Services/UartTtlBoardTransport.cs` – upload ACK transaction.
- `Services/UnifiedBoardTransport.cs` – forward model operations.
- `ViewModels/MainViewModel.cs` – board-specific capacity + model sync.
- `ViewModels/ProductionSettingsViewModel.cs` – enable/disable setting theo board.
- `Views/ProductionSettingsPage.xaml` – Card D2XX + UART profile.
- `Services/ProductionConfigService.cs` – persist UART profile.
- `Version.props` – 14.0.0.

## 5. Codex bắt buộc rà soát
1. Build Release trên Windows/.NET SDK.
2. Audit compile errors từ các interface mới.
3. Audit XAML binding `IsD2xxSettingsEnabled`, `IsUartSettingsEnabled`, `LastUartModelPath`.
4. Kiểm tra AUTO khi active UART: `HasEnoughCardsForModel` không bị Card D2XX chặn.
5. Kiểm tra AUTO khi active D2XX: Card capacity vẫn bắt buộc như V12/V13.
6. Kiểm tra model sync chỉ chạy khi `UsesFirmwareCycleResult=true`.
7. Kiểm tra model profile sai/missing phải block test rõ ràng, không fallback THT sang UART.
8. Kiểm tra ACK queue không tích runtime event vô hạn.
9. Kiểm tra ACK cũ được drain trước command mới.
10. Kiểm tra ERROR/NAK trong upload dừng ngay.
11. Kiểm tra `:MODELNAME?` parse tên model đúng firmware thực.
12. Kiểm tra sau `:FINISH` firmware thực có yêu cầu `:RESET`/reconnect hay không. V14 hiện chưa tự RESET sau upload vì cần Codex đối chiếu golden trace và hardware trước khi thêm hành vi reset production.
13. Kiểm tra model-name normalization tránh upload lặp vì khác ký tự format.
14. Kiểm tra LastUartModelPath không làm profile mã hàng A bị dùng nhầm cho mã hàng B. Nếu có nguy cơ, sửa resolver ưu tiên sidecar cùng mã hàng trước saved path hoặc lưu mapping theo PartNumber.
15. Kiểm tra Master Auto trên UART: nếu chưa có protocol Master xác nhận thì disable rõ, không chạy state machine D2XX nửa vời.
16. Audit relay caller table: FAIL D2XX không Relay2; Probe không relay.
17. Audit stale callbacks/generation khi đổi board/model.
18. Audit SerialPort reader/dispose/deadlock/reconnect.
19. Audit StackOverflow regression trong TestViewModel.
20. Audit backup `.cs`/duplicate compile và read-only WPF binding.

## 6. Điểm cần Codex ưu tiên sửa nếu phát hiện
### P0
- Có thể marking Relay2 khi FAIL.
- Re-arm khi chưa tháo sản phẩm.
- UART upload nhầm model.
- D2XX regression.
- Hai backend cùng active/cùng phát event.
- Reader/handle race gây crash.

### P1
- AUTO chọn sai bo.
- Card D2XX chặn UART.
- UART model mismatch không được phát hiện.
- TESTPIN mapping sai profile.
- callback cũ cập nhật model/cycle mới.

### P2
- UI/status/log chưa rõ.
- resolver profile chưa tối ưu.

## 7. Test hardware bắt buộc
### D2XX
- Forced D2XX, AUTO D2XX.
- 100+ PASS cycles.
- FAIL confirm -> Relay1 only.
- Probe isolation.
- removal interlock.
- Master Good/Bad.
- resistance/Keysight.

### UART
- Forced UART, AUTO fallback.
- IDN/MODELNAME.
- model board giống -> không upload.
- model board khác -> upload full + ACK.
- profile malformed -> block.
- START/MEASURE/MAXEXT.
- TESTPIN.
- OPEN/OTHER.
- PASS/FAIL/removal.
- rút/cắm USB UART giữa cycle.
- 100+ cycles + soak 1–8 giờ.

### Hai bo cùng cắm
- AUTO phải chọn D2XX theo policy hiện tại.
- đổi forced UART phải đóng D2XX an toàn rồi mở UART.
- đổi lại D2XX không còn UART reader cũ.

## 8. Kết luận
V14 đặt ranh giới đúng: **một UI/mã hàng ở tầng vận hành, hai profile phần cứng ở tầng backend**. D2XX giữ THT/Card/TestEngine; UART giữ firmware profile/ACK/model sync. Codex không được hợp nhất hai model bằng công thức hoặc mapping suy đoán. Sau review phải xuất báo cáo build + bugs + regression + hardware tests, và chỉ tăng version tiếp khi build/test bo thật đạt.
