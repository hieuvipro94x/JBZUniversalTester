# Rà soát khựng giao diện và D2XX — 18/10/2026

## Kết luận

Chưa thể xác nhận mọi thao tác đều không khựng. Đã đo được một khoảng làm việc đồng bộ đáng chú ý khi tạo/render trang Cài đặt, và tìm thấy các đường gọi FTDI, parser THT và SQLite tem BCW có thể giữ Dispatcher. Không có bằng chứng từ các bài test đã chạy rằng chúng làm sai logic bắt lỗi hoặc khiến bo mất kết nối; chưa đo trên D2XX/COM thật.

Phạm vi source: khởi động App/MainWindow, navigation, tạo/lưu/đóng Cài đặt, chọn mã và tìm kiếm, chọn COM, thao tác tay, xem trước/in tem, History/export, D2XX lifecycle và luồng scan/presentation. Workspace gồm commit `5818737` V2026.09.254 và thay đổi viền TestView V2026.09.255 đang local. Cuộc rà soát chỉ thêm báo cáo; giữ nguyên thay đổi local có sẵn.

## 1. Mở Cài đặt vẫn có một đợt CPU/layout trên UI — đã đo

`Views/MainWindow.xaml.cs:382` tạo ProductionSettingsViewModel bằng Task.Run, nhưng sau await vẫn phải tạo ProductionSettingsPage trên Dispatcher. `Views/ProductionSettingsPage.xaml.cs:68` chạy InitializeComponent, tạo controls/bindings và setup các editor đồng bộ. Phần render này không được đưa ra background chỉ bằng việc chuyển ViewModel sang Task.Run.

Đo bằng harness WPF riêng, x86, tham chiếu code hiện tại: cửa sổ 1800×1050, MainViewModel=null, không kết nối bo/COM, không đọc/ghi DB sản xuất. Vô hiệu handler Loaded của trang để loại phần quét COM/catalog, chỉ đo constructor và layout; ViewModel được tạo bằng worker. Một lần chạy gồm một lần mở lạnh và năm lần mở tiếp theo trong cùng process:

| Lần | Constructor trang, ms | Từ constructor tới layout đầu, ms | 200 thay đổi lựa chọn card + layout, ms |
|---:|---:|---:|---:|
| 1 | 372,5 | 607,1 | 8,0 |
| 2 | 13,6 | 131,9 | 6,4 |
| 3 | 12,4 | 132,0 | 11,2 |
| 4 | 33,3 | 178,7 | 19,9 |
| 5 | 14,6 | 149,7 | 7,8 |
| 6 | 14,2 | 142,6 | 7,7 |

Đây là đo tạo/render trang trong harness, không phải thời gian click-to-ready của ứng dụng sản xuất. Lần lạnh gồm JIT/cache/theme; harness không nạp toàn bộ App.xaml của ứng dụng. Số liệu xác nhận UI còn phải làm một khối công việc có thể cảm nhận được; không dùng số này để cam kết thời gian trên máy khác. Các lựa chọn card đơn giản không tạo nghẽn đáng kể trong phép đo này.

Hướng xử lý: đo OPEN_VIEW/UI_STALL trên máy vận hành; giảm controls/binding cần dựng ở lần đầu hoặc chia phần nặng ra sau lần render đầu. Không đưa việc tạo WPF controls sang Task.Run.

## 2. Một số lệnh D2XX async vẫn thực thi native I/O trên luồng gọi — rủi ro theo driver

`Services/D2xxBoardTransport.cs:1585–1694`: WriteAsync, PurgeAsync và ReadAvailableAsync lấy semaphore bất đồng bộ nhưng gọi FT_Write/FT_Purge/FT_Read đồng bộ. Khi lệnh xuất phát từ thao tác UI và gate sẵn sàng, WaitAsync có thể hoàn thành ngay; khi phải chờ, continuation vẫn có thể trở lại UI. Tên phương thức async không bảo đảm phần P/Invoke chạy ngoài Dispatcher.

Các đường relay/reset/START/STOP và đổi cấu hình scan đi qua những hàm này. Disconnect cũng còn FT_Close đồng bộ sau khi chờ gate/reader. Driver chậm có thể giữ UI tại các lời gọi này. Source đặt timeout read/write 50/150 ms khi mở handle, nhưng đó không phải giới hạn đã đo cho mọi API native, đặc biệt purge/close.

Ảnh hưởng tới scan: _ioLock được chia sẻ với reader; _controlWaiters chủ động nhường quyền cho lệnh điều khiển. Nếu native call kéo dài, reader cũng phải chờ lock/command. Đây là serialization cần thiết cho một chủ sở hữu, không nên giải quyết bằng cách bỏ lock hoặc tạo reader thứ hai.

Điểm đã có: nhận dạng/mở FTDI dùng Task.Run; ScanLoopWorker có LongRunning task riêng; Dispose đợi reader kết thúc trước đóng handle. UI/logic xử lý frame không chạy trực tiếp toàn bộ trên D2XX reader trong ứng dụng WPF.

Hướng xử lý: nếu sửa, dùng một ranh giới worker cho native control I/O, giữ nguyên lock/generation, thứ tự STOP/RESET/relay, timestamp TX và chính sách hủy. Cần regression về timing và thử phần cứng trước khi phát hành; không chỉnh firmware command/timing chỉ để giao diện có vẻ nhanh hơn.

## 3. Xem trước/in tem vẫn có parser và SQLite đồng bộ trên UI — xác nhận theo source

- PreviewLabel_Click tại `Views/ProductionSettingsPage.xaml.cs:436` và TestPrintLabel_Click tại dòng 1279 gọi BuildSettingsLabelRequest trước await. Hàm tại dòng 1477 gọi ThtModelParser.Load trực tiếp khi không truyền loadedModel.
- BatchPrintLabel_Click phân tích THT trực tiếp tại dòng 1373 trước khi chạy vòng in. File lớn, USB/ổ mạng hoặc antivirus có thể kéo dài thao tác này.
- BuildSettingsLabelRequest còn gọi BcwSerialStore.Peek ở dòng 1506; BcwReprint_Click đọc Latest trực tiếp ở dòng 1245.
- `Services/LabelPrintService.cs:97–112` gọi BcwSerialStore.Prepare/Outcome đồng bộ trong phương thức async. Khi gate in sẵn sàng, đường này có thể chạy trên UI. BcwSerialStore.Open/transaction sử dụng SQLite đồng bộ và busy timeout 2000 ms, nên journal bị khóa là một nguồn khựng có điều kiện. Chưa khóa DB thật để đo.

COM máy in có Open/Close chạy qua worker; điều đó không tự chuyển parser/journal chạy trước hoặc sau await sang background. Leak và printer vẫn là COM độc lập, không dùng FTDI handle.

Hướng xử lý: snapshot dữ liệu editor trên Dispatcher, parse/template/journal ở worker, rồi trả kết quả về UI với lifecycle/generation guard. Giữ nguyên serial/tem/production LOT và exactly-once; không chạy thử in thực tế chỉ để kiểm tra UI.

## 4. History và hàng đợi scan cần đo tải dài hạn

History truy vấn/tổng số/export đã dùng Task.Run trong `Views/HistoryPage.xaml.cs`, nên chờ SQLite không trực tiếp chặn Dispatcher. Tuy nhiên `Services/TestHistoryStore.cs:2096` vẫn có ROW_NUMBER trên toàn bộ sản phẩm trước JOIN trang; database lớn có thể đọc chậm và tăng tải CPU. Không benchmark database sản xuất trong đợt này.

`ViewModels/TestViewModel.cs:4173–4252` enqueue frame vào ConcurrentQueue và tiêu thụ bởi một worker. Queue chưa có giới hạn độ dài; source có log frame_queue_depth/frame_queue_high. Nếu consumer chậm lâu hơn reader, có thể tăng RAM và độ trễ xử lý. Đây là rủi ro khi quá tải, chưa được tái hiện trên FTDI thật. Không được tự bỏ complete frame hoặc thay debounce vì có thể ảnh hưởng bắt lỗi/removal.

Presentation engine và LED có coalescing để tránh xếp một lần render cho mỗi frame; InvokeUi dùng BeginInvoke, không dùng Dispatcher.Invoke chờ UI trong reader. Self-tests console dùng đường frame inline khi Application.Current=null, nên stress engine không thay thế bài kiểm tra toàn bộ pipeline WPF/FTDI trong lúc bấm Cài đặt.

## Các đường đã có bảo vệ và được kiểm thử

| Thao tác | Source/kiểm thử hiện tại | Giới hạn |
|---|---|---|
| Khởi động | Config/audio preload, deferred data và mở bo được tách nền; MainWindow render trước hardware init | Chưa đo full cold start trên máy sản xuất |
| Mở Cài đặt | ViewModel chuẩn bị ở worker; navigation có generation guard | BAML/layout vẫn có khoảng UI work như số đo trên |
| Lưu Cài đặt | Snapshot editor, Task.Run ghi file, serialization/atomic config; lỗi giữ phần đang nhập | UI vẫn phải clone DTO và refresh bindings; driver control là rủi ro riêng |
| Đóng Cài đặt/Manual | Cancel, đợi cleanup; test giữ COM/relay ownership và giữ Dispatcher responsive | Chờ phần cứng xong không đồng nghĩa app bị treo; chưa đo driver thật |
| Chọn COM máy in | Port names/Open/Close chạy worker, selection cancellation và stale-generation guard | BCW journal còn đồng bộ |
| Chọn/tìm mã | Catalog/recents đọc nền, filter debounce; test 1000 file và tránh chọn kết quả cũ đạt | Filter collection vẫn chạy UI; chưa đo catalog thực tế lớn hơn |
| History/xuất | SQL/export nền, paging và virtualization | ROW_NUMBER toàn dữ liệu có thể làm đọc chậm |
| Scan/bắt lỗi | Reader riêng, frame worker riêng trong WPF, UI snapshots/LED coalescing | Native control lock và queue backlog cần đo FTDI thực |

## Build và kiểm chứng

- `dotnet build -c Release`: đạt, 0 warning/error.
- `dotnet run --project Tests -c Release --no-build`: **94/98 PASS**. Bốn fail đã tồn tại: tiêu đề topology diagnostics, layout điện trở 10 kênh, nhãn chờ Master, assert XAML popup lỗi. Không bỏ/giảm các kiểm tra này.
- `--test-settings-lifecycle`: exit 0; kiểm tra slow save không giữ Dispatcher, writer snapshot không sửa editor trong khi ghi, atomic save, hủy/cleanup thao tác tay và đóng COM giả lập chậm ngoài UI.
- `--test-app-responsiveness`: exit 0; drain log không giữ Dispatcher và tìm mã debounce/không chọn stale result.
- Đo engine trong suite: 10.000 frame lặp ~10 ms; 2.000 frame 500 IO/250 network ~31 ms. Decoder stress 10 card/500 frame ~83 ms. Các số này không phải latency bo thật hay thao tác chuột toàn ứng dụng.
- UI harness: sáu lần tạo/layout trang Cài đặt và tổng 1200 thay đổi lựa chọn card, không mở bo/COM.

Không chỉnh code D2XX, settings, query hay database trong cuộc rà soát. Không mở/sửa DB sản xuất hoặc kết nối/in lên phần cứng thật. Không có cơ sở kết luận “tất cả thao tác đều không lag”; ưu tiên cần xử lý là UI layout lần đầu, native control I/O và parser/journal tem còn đồng bộ. Khi thử máy thật, đối chiếu UI_STALL, SETTINGS_PERF, D2XX/scan metrics, HOT_PATH_WARNING và frame_queue_depth thay vì chỉ nhìn đồng hồ thời gian test.
