# Rà soát code không sử dụng — V2026.09.224

Đã kiểm tra tham chiếu trong C#, XAML, self-tests, HardwareVerification, cấu hình project và các chuỗi tên dùng cho reflection. Rà soát dựa trên mã nguồn hiện tại; không xóa chỉ vì một thành viên public có ít tham chiếu.

## Đã xóa

Hai file không được khởi tạo, đăng ký resource, gọi hoặc tham chiếu trong các project hiện tại:

- `Converters/IntEqualsToVisibilityConverter.cs`: TestWindow dùng DataTrigger để chuyển bảng theo SelectedOperationTabIndex.
- `Services/ErrorLogService.cs`: entry point cũ không có caller; ghi fault hiện tại vẫn do persistence/history và logger đang hoạt động đảm nhiệm.

Tám hàm không có caller, binding hoặc self-test sử dụng:

- `WaterProofSerialService.WaitForPendingCloseBestEffortAsync`.
- `TestViewModel.HasInstalledProductEvidenceForProbe`.
- `TestViewModel.IsMappedProbeIo`.
- `TestViewModel.RemoveInlineProbeFaultRows`.
- `TestViewModel.WaterProofRetryInstruction`.
- `ProductionSettingsPage.SafeFileName`.
- `ProductionSettingsViewModel.PrepareBulkPrintLot`.
- `ProductionSettingsViewModel.CommitBulkPrintedLot`.

Ba khai báo không dùng:

- `D2xxBoardTransport.FT_OPEN_BY_DESCRIPTION`.
- `KeysightVisaService.VI_TMO_INFINITE`.
- `ProductionConfigService.LegacyTimingKeys`.

Sau khi xóa, kiểm tra lại không thấy private method nào chỉ có khai báo theo phép quét tham chiếu tên; đây không phải chứng minh rằng mọi API public đều được sử dụng.

## Giữ nguyên

- Thành viên interface như `WireColorToBrushConverter.ConvertBack`, được WPF gọi qua IValueConverter.
- DTO, thuộc tính binding và các trường cấu hình được đọc/ghi bằng serializer.
- Các API public liên quan nhập/migration lịch sử cũ, D2XX/COM và những entry point tương thích chưa đủ bằng chứng để bỏ.
- `BulkPrintLotNo` và các key timing trong cấu hình vẫn được bảo toàn; chỉ bỏ hàm/khai báo không có nơi gọi.
- Không xóa dữ liệu, file model, template, âm thanh, build output hoặc database. Không đổi protocol, lệnh firmware hay schema SQLite.

Các thay đổi giao diện đang có trong working tree trước đợt dọn này được giữ nguyên.
