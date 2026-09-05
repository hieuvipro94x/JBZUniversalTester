# ALWAYS PROBE AUDIT — 2026-09-05

## Yêu cầu cố định
- Xóa tùy chọn `Test pointer` khỏi Production Settings.
- Que dò/TestPin luôn hoạt động, không có trạng thái ON/OFF do người dùng chọn.
- Production vẫn kiểm tra PASS/FAIL bình thường; Probe chỉ là lớp quan sát song song.
- Relay interlock của Probe luôn có hiệu lực.
- Quét toàn bộ CARD đã cấu hình để dò được mọi Global IO.

## 1. Views/ProductionSettingsPage.xaml
- Xóa TextBlock `Test pointer`.
- Xóa CheckBox binding `Settings.UseTestPointer`.
- Grid.Row 12 được collapse `Height=0` để không để khoảng trống và không phải đổi toàn bộ Grid.Row phía sau.
- Còn reference Test pointer/UseTestPointer trong XAML: []

## 2. ViewModels/TestViewModel.cs
Marker: `ALWAYS_PROBE_2026-09-05`.

Đã sửa các điểm:
- Constructor: force `_productionSettings.UseTestPointer = true` để tương thích config/code legacy.
- `PrepareProbeUiMode()`: bỏ nhánh clear khi setting false.
- `OnBoardFrameReceived()`: luôn gọi `TryDetectInlineProbeContacts(...)`.
- RELEASE probe: luôn cập nhật/clear tracker theo frame, không phụ thuộc setting.
- `IsProbeRelayInterlockActive()`: bỏ return false theo setting.
- `StartProbeScanAsync()`: bỏ nhánh `Probe đang tắt`.
- `RefreshProductionUiSettings()`: luôn force legacy property true; không clear Probe vì config false.

Các reference UseTestPointer còn lại trong TestViewModel chỉ là compatibility assignment/comment: [956, 958, 7093, 7095]

## 3. Services/D2xxBoardTransport.cs
- Dùng policy `scanAllInstalledIo: true`.
- Không còn để `UseTestPointer` quyết định dải scan.
- Probe luôn có dữ liệu toàn bộ CARD đã cài.
- Reference UseTestPointer còn lại: []

## Không xóa property khỏi ProductionSettings ngay
Giữ `UseTestPointer` trong model/config schema để tương thích file cấu hình cũ và tránh migration không cần thiết.
UI không hiển thị nó và runtime luôn ép `true`, nên về hành vi nó không còn là setting nữa.

## Codex rà soát
```powershell
rg -n "ALWAYS_PROBE_2026-09-05|UseTestPointer|Test pointer" .
```

Yêu cầu sau rà soát:
1. ProductionSettingsPage không còn checkbox Test pointer.
2. Không có `if (...UseTestPointer...)` nào khóa Probe runtime.
3. `D2xxBoardTransport.ConfigureActiveScanRange()` dùng `scanAllInstalledIo: true`.
4. Không thay logic `TryDetectInlineProbeContacts`, `ProbeStateTracker`, suppression SHORT/WRONG hoặc relay debounce 40 ms.
5. Không thay PASS/FAIL, Master, resistance, leak, relay sequence.
