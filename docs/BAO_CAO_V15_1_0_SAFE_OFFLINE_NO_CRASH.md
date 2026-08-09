# BÁO CÁO V15.1.0 — SAFE OFFLINE MODE / CHỐNG CRASH KHI MẤT BO

## Mục tiêu
Ứng dụng phải chạy được ngay cả khi không có bo D2XX/UART. Mọi thao tác cần hardware phải fail-safe: báo rõ, không gửi lệnh, không thoát app.

## Thay đổi
- Startup không coi mất bo là fatal. MainWindow vẫn sử dụng được.
- Trạng thái offline rõ: `BO CHƯA KẾT NỐI` hoặc `MODEL ĐÃ TẢI - BO CHƯA KẾT NỐI`.
- `AsyncRelayCommand` bắt exception từ `async void Execute`, log và hiển thị cảnh báo. Đây là fix quan trọng vì exception command trước đây có thể đi lên WPF Dispatcher và làm app văng.
- Manual Relay 1/Relay 2/Tắt relay dùng `EnsureManualBoardReady`.
- Chưa kết nối: popup `Chưa kết nối bo mạch test`, không gọi transport.
- Đang dùng UART TTL: các nút Relay D2XX báo `Chức năng không áp dụng cho bo UART TTL`, không ném `NotSupportedException`.
- TEST PROBE PIN offline không rethrow; reset về Background và báo rõ.
- `DispatcherUnhandledException` là lớp bảo vệ cuối cùng: ghi log, `e.Handled=true`, cảnh báo người dùng. Không thay thế việc catch đúng tại service/ViewModel.
- `TaskScheduler.UnobservedTaskException` được log và `SetObserved()`.
- Hardware monitor retry mỗi 2 giây thay vì 500 ms để offline không spam FTDI/COM/log.

## Hành vi mong đợi
1. Mở app không cắm bo → app vẫn vào MainWindow.
2. Có thể vào Settings/History/chọn mã hàng khi offline.
3. Bấm Relay 1/2/Tắt relay → popup rõ `CHƯA KẾT NỐI VỚI BO MẠCH TEST`; app không crash.
4. Bấm TEST PROBE PIN khi offline → popup rõ; app không crash.
5. Cắm bo sau đó → auto reconnect; không cần restart app.
6. UART TTL connected + bấm relay D2XX → báo chức năng không áp dụng; không crash.

## Codex cần rà lại
- Search mọi `async void` ngoài event handler chuẩn WPF.
- Search mọi `throw new` có thể đi trực tiếp từ command/UI.
- Search `Active()`/`Firmware()` của UnifiedBoardTransport và bảo đảm caller UI đã guard/catch.
- Kiểm tra background callback `ProtocolEventReceived`, `FrameReceived`, Dispatcher invoke có catch tại biên.
- Kiểm tra reconnect/disconnect race: cáp bị rút đúng lúc Manual command. Guard `IsConnected` không đủ chống TOCTOU; transport exception vẫn phải được command guard catch.
- Kiểm tra popup không xuất hiện liên tục từ hardware monitor (monitor chỉ log, không popup).
- Kiểm tra trạng thái relay sau exception: D2XX nên cố `AllRelaysOffAsync` trong finally ở các pulse workflow nếu kết nối còn tồn tại.
- Chạy build Release và test rút/cắm USB nhiều lần.

## Test bắt buộc
- Startup không bo.
- Startup D2XX lỗi driver.
- Startup UART COM không tồn tại.
- Relay manual offline.
- Relay manual trên UART.
- Rút D2XX giữa pulse.
- Rút UART giữa cycle.
- TESTPIN offline.
- Cắm lại bo sau khi app đã chạy.
- Chuyển Auto/D2XX/UART khi offline.
- 100 lần connect/disconnect/reconnect không văng app.

## Lưu ý
`AppDomain.UnhandledException` không phải exception nào cũng có thể phục hồi. Các lỗi runtime nghiêm trọng như StackOverflow/AccessViolation vẫn phải sửa root cause; V15.1 không coi global guard là cách che lỗi logic.
