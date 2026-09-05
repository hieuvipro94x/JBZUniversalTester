# CARD SYNC AUDIT — 2026-09-05

## Mục tiêu
- Số CARD trong Production Settings là cấu hình phần cứng của máy.
- Không co dải scan theo `model.MaxIo`.
- Đổi CARD trong Settings phải có hiệu lực ngay ở runtime.
- Không Disconnect/Connect FTDI chỉ vì đổi CARD.
- `model.MaxIo` vẫn dùng để kiểm tra model có vượt capacity đã cài hay không.
- Giữ nguyên `ScanSupervisor.cs` gốc.

## File 1 — Services/D2xxBoardTransport.cs
Marker tìm kiếm:

`CARD_SYNC_2026-09-05`

Vị trí trong file bản sửa: khoảng dòng 620.

### Sửa duy nhất về policy capacity
Trước:
```csharp
_scanCapacity = BoardScanCapacity.Create(
    _production,
    maxIo,
    scanAllInstalledIo: _production.UseTestPointer);
```

Sau:
```csharp
_scanCapacity = BoardScanCapacity.Create(
    _production,
    maxIo,
    scanAllInstalledIo: true);
```

Dòng `scanAllInstalledIo: true` ở khoảng dòng 631.

Ý nghĩa:
- Production và Probe đều scan toàn bộ card đã cài.
- Ví dụ máy 2 CARD = 128 I/O thì model chỉ dùng 20 I/O vẫn scan 128 I/O.
- `maxIo` KHÔNG bị bỏ: `BoardScanCapacity` vẫn dùng nó cho
  `IsModelWithinInstalledCapacity`, nên model vượt capacity vẫn bị chặn.
- Không thay lệnh relay, resistance, decoder, handshake hoặc FTDI lifecycle.

### Không sửa StartScanAsync
Giữ nguyên cơ chế sẵn có:
- so requested configuration với active configuration;
- nếu capacity/mode thay đổi thì STOP stream cũ;
- nếu prepared capacity khác thì tự RESET_CLEAR + INIT lại;
- configure decoder theo `_capacity` mới;
- gửi `8C 00 <StartScanParameter> 00`;
- cập nhật `AppliedScanCapacity`.

Đây là reset/reprepare NỘI BỘ tự động của transport, không phải operator
reset BO và không phải Disconnect/Connect FTDI.

## File 2 — ViewModels/TestViewModel.cs
Marker tìm kiếm:

`CARD_SYNC_2026-09-05`

Vị trí marker: khoảng dòng 6977.
`RefreshProductionConfigurationAsync`: khoảng dòng 6990.

### Sửa trong RefreshProductionConfigurationAsync
- Giữ `int maxIo = _model?.MaxIo ?? 0`.
- Giữ `_board.ConfigureActiveScanRange(maxIo)`.
- So sánh `AppliedScanCapacity` với requested capacity gồm:
  `StartScanParameter`, `StartCardNumber`, `TotalIoCapacity`.
- Nếu đang scan và capacity đổi thì yêu cầu Start/Verify lại.
- BỎ fallback tự `ReconnectBoardForSettingsAsync()` khi đổi CARD.
- BỎ `StopScanAsync()` và `AllRelaysOffAsync()` thủ công ở ViewModel trước khi
  start lại; `D2xxBoardTransport.StartScanAsync()` sở hữu sequence này.
- Nếu đang không scan, chỉ cập nhật requested capacity. Lần START tiếp theo tự
  dùng số CARD mới.

Reconnect còn trong method đổi `BoardMode` là đúng và KHÔNG được xóa:
đổi loại bo là thay backend/lifecycle, khác hoàn toàn đổi số CARD.

Kiểm tra reconnect còn lọt vào method CARD refresh: `False`.
Expected: `False`.

## File 3 — Services/ScanSupervisor.cs
KHÔNG SỬA.

Bản gốc đã đúng:
- gọi `ConfigureActiveScanRange(maxIo)` trước;
- so `AppliedScanCapacity` với `Capacity`;
- nếu capacity khác thì không reuse stream cũ;
- gọi `StartScanAsync`;
- dùng `CompleteFramesReceived` và frame hợp lệ để verify.

Không dùng bản ScanSupervisor_LIVE_CARD_SYNC tạo trước đây.

## File 4 — ViewModels/MainViewModel.cs
KHÔNG SỬA trong patch này.

Đã có flow đúng:
`ReloadProductionSettingsAsync()`
-> phát hiện `ExpansionCardCount / StartCardNumber / UsbDelay` thay đổi
-> gọi `Test.RefreshProductionConfigurationAsync()`.

Do đó Save Settings đã có đường đồng bộ runtime.

## Checklist Codex rà soát
1. `rg -n "CARD_SYNC_2026-09-05" Services/D2xxBoardTransport.cs ViewModels/TestViewModel.cs`
2. Xác nhận `ConfigureActiveScanRange` dùng `scanAllInstalledIo: true`.
3. Xác nhận `RefreshProductionConfigurationAsync` KHÔNG gọi `DisconnectAsync`
   hoặc `ReconnectBoardForSettingsAsync`.
4. Xác nhận `ReconnectBoardForSettingsAsync` vẫn chỉ được dùng khi đổi BoardMode
   hoặc recovery thật sự cần reconnect.
5. Giữ nguyên `ScanSupervisor.cs` gốc.
6. Build solution Release.
7. Test runtime:
   - 2 CARD, model <=64 I/O -> START_SCAN vẫn parameter/capacity của 2 CARD.
   - đổi 2 -> 4 CARD tại Settings -> Save -> scan chuyển ngay 4 CARD.
   - đổi 4 -> 2 CARD -> Save -> scan chuyển ngay 2 CARD.
   - không có FT_Close/FT_OpenEx trong trace khi chỉ đổi CARD.
   - model vượt capacity đã cài vẫn bị chặn.
   - Probe/Production đều map đúng Global IO.
