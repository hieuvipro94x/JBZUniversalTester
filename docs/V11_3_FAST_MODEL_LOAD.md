# V11.3 - FAST MODEL LOAD / AUTO TESTVIEW

## Lỗi gốc

1. MainWindow startup có thể đang parse `LastTestedModelFile` cùng lúc người vận hành chọn một THT mới. Kết quả startup cũ có thể hoàn thành muộn và ghi đè model vừa chọn.
2. `TestWindow_Loaded` chờ `InitializeAsync()`, mà task này bao gồm cả auto-load model startup, khiến Start production và cảm giác hiển thị cấu hình bị kéo dài.
3. Nút `CHỌN MÃ HÀNG` chỉ load model; người vận hành phải bấm `BẮT ĐẦU KIỂM TRA` lần thứ hai.
4. `TestEngine.BuildRows()` trước đây tìm network của từng pin bằng cách quét toàn bộ `model.Nets` ở mỗi refresh (`O(P*N)`).
5. `TestViewModel.SetModel()` dựng bảng hai lần: một lần từ `TestEngine.Reset()->Changed`, rồi gọi `RefreshFaults()` lần nữa.

## Luồng mới

`CHỌN MÃ HÀNG` -> parse THT background -> SetModel/BuildRows -> tự mở TestView -> render DataGrid trước -> đảm bảo hardware -> ARM production.

- Không cần click `BẮT ĐẦU KIỂM TRA` lần thứ hai.
- Model người vận hành chọn có generation cao hơn và luôn thắng model startup cũ.
- TestWindow chỉ chờ `InitializeHardwareAsync()`, không chờ task auto-load model cũ.
- Lookup Pin -> WireNet được dựng một lần khi `SetModel()`.
- DataGrid có sẵn `Faults` trước `TestWindow.Show()` và được ưu tiên render trước D2XX recovery.
