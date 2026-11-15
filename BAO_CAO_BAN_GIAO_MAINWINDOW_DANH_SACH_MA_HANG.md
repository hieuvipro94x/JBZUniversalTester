# Báo cáo bàn giao: Danh sách mã hàng trên MainWindow

## Mục tiêu

Thêm một vùng cuộn hiển thị danh sách mã hàng ngay trên MainWindow khi mở ứng dụng, để người vận hành thường xuyên đổi mã có thể chọn nhanh. Chọn một mục phải nạp đúng file `.tht` bằng luồng tải model hiện có, sau đó giữ hành vi hiện tại: model được nạp và ứng dụng tự mở TestView.

## Cập nhật triển khai V2026.09.421

Đã triển khai `Views/MainProductCatalog.xaml(.cs)` ở bên phải MainWindow: danh sách cuộn độc lập, tìm theo tên file/thư mục, ưu tiên mã vừa test, đánh dấu file đang chọn, nút mở, Enter hoặc nhấp đúp. Danh mục dùng lại `ProductFileCatalog` và `RecentTestedProductService` với truy cập lịch sử chỉ đọc. Nạp danh mục sau frame đầu; cập nhật khi trở về từ TestView và bằng nút Làm mới. Hai đường chọn mã dùng chung `HomeViewModel.LoadProductFileAsync`; thư mục gốc dùng chung `CatalogDirectory`.

Đã bổ sung kiểm tra giao diện với 1.001 file, tên trùng ở hai thư mục, cuộn, lọc, lựa chọn đường dẫn chính xác và xác nhận không nạp model chỉ vì hiển thị danh sách. Chi tiết xác minh và phát hành được báo trong chat sau khi hoàn tất.

## Trạng thái lúc bàn giao ban đầu (trước triển khai)

- Nhánh: `wip/20261112-new`
- Commit gần nhất: `c628432` — `Release V2026.09.420 - auto publish`
- Không có thay đổi mã nào được thực hiện cho yêu cầu danh sách mã hàng.
- MainWindow hiện có nút `CHỌN MÃ HÀNG`; nút gọi `Home.LoadModelCommand`.
- Nút này mở `ProductPickerWindow`, vốn đã có danh mục `.tht`, tìm kiếm, danh sách mã vừa test và chọn file.
- Nguồn danh mục hiện được dùng mặc định là `C:\Item` nếu thư mục tồn tại. Nếu không, bộ chọn dùng thư mục model hiện tại hoặc thư mục ứng dụng.
- Đã xác nhận `ProductFileCatalog.Read(root)` quét đệ quy `.tht`, bỏ qua `ThtBackups` và liên kết thư mục; `RecentTestedProductService` lấy các mã vừa test từ SQLite.

## Các file liên quan

- `Views/MainWindow.xaml`: giao diện MainWindow; vùng nội dung là `Grid.Row="1"`, hiện có một `Viewbox` với logo và thẻ model đang chọn. Thanh hành động ở `Grid.Row="2"` có nút chọn mã hàng.
- `Views/MainWindow.xaml.cs`: sự kiện cửa sổ và khởi động; `ViewModel_ExplicitModelLoaded` tự mở TestWindow sau khi model được nạp.
- `ViewModels/HomeViewModel.cs`: `LoadModelCommand`, chọn thư mục gốc và mở `ProductPickerWindow`; `Refresh()` cập nhật thông tin model.
- `Views/ProductPickerWindow.xaml` và `.xaml.cs`: cửa sổ danh mục hiện có, tìm kiếm, mã vừa test, làm mới, chọn và yêu cầu duyệt file.
- `Services/ProductFileCatalog.cs`: model dữ liệu `ProductFileEntry` và quét danh sách `.tht`.
- `Services/RecentTestedProductService.cs` (xác nhận tồn tại qua tham chiếu trong bộ chọn): đọc mã hàng vừa test.
- `ViewModels/MainViewModel.cs`: `LoadModelAsync(path)` nạp `.tht` hoặc Product Bundle; phát `ExplicitModelLoaded` sau khi nạp thành công.

## Hướng tích hợp đề nghị

1. Tái sử dụng danh mục và bộ chọn hiện có; tránh tạo quy tắc quét `.tht` thứ hai.
2. Thêm vùng cuộn có chiều cao giới hạn trong phần nội dung MainWindow. Hiển thị mã hàng dễ đọc; nếu không gian cho phép, kèm tên model/thư mục để phân biệt file trùng tên.
3. Ưu tiên danh sách mã vừa test để đổi nhanh, đồng thời có cách tìm/xem toàn bộ danh mục. Có thể nhúng lại bộ chọn hiện tại trong vùng cuộn hoặc mở bộ chọn đó khi bấm nút; chọn mục phải đi qua cùng `Home.LoadModelAsync`.
4. Tải danh mục nền sau lần render đầu để không làm chậm splash/khởi động và không khởi tạo thêm chủ sở hữu FTDI/COM.
5. Giữ nguyên rào chắn tháo sản phẩm, kiểm tra dung lượng card, xử lý lỗi nạp, và hành vi tự mở TestView hiện tại.
6. Giữ giao diện hoạt động khi `C:\Item` vắng mặt/không đọc được; hiện bộ chọn đã có fallback và báo lỗi phù hợp.

## Việc còn phải làm

- Chọn thiết kế gọn phù hợp với MainWindow toàn màn hình, không làm biến dạng logo/thẻ model/thanh nút hiện có.
- Nối danh sách cuộn vào nguồn catalog/recent hiện hữu và chọn mã bằng luồng nạp model hiện tại.
- Xử lý trạng thái chưa có mã, đang quét, lỗi đọc thư mục, và thao tác làm mới.
- Rà soát mọi đường chọn để chắc chắn chọn một mã ở MainWindow không bỏ qua `ProductRemovalPending` hoặc gây test tự chạy sai thời điểm.
- Build, chạy self-test thích hợp, xem diff và kiểm tra XAML. Không thử phần cứng thì ghi rõ.
- Theo `AGENTS.md`, mọi sửa mã phải tăng đồng bộ toàn bộ trường version trong `Version.props`. Quy trình phát hành tự động đã được ủy quyền: chạy `BUILD_ONE_FILE.cmd`, hoàn tất các xác nhận build/commit/push, chép nguyên thư mục version lên `\\192.168.10.150\public\NAM\<version>`, đối chiếu SHA256 các file và báo commit/nhánh/đường dẫn. Không ghi đè thư mục release đã có dữ liệu production.

## Quy tắc và rủi ro cần giữ

- `.tht` là nguồn topology D2XX; không chuyển đổi định dạng topology khác.
- Danh sách mã hàng chỉ chọn/nạp model, không được tạo chu kỳ TEST, FAIL, tăng sản lượng hoặc kích relay.
- Không đưa thêm reader/owner cho FTDI hoặc thiết bị COM trong MainWindow.
- Giữ nguyên cơ chế `ProductRemoved` và chặn nạp mã nếu đang chờ tháo sản phẩm.
- Đọc `AGENTS.md` trong workspace trước khi sửa; đây là hướng dẫn dự án hiện hành.

## Gợi ý bắt đầu phiên tiếp theo

Mở workspace `D:\NAM\JBZUniversalTesterTeahung`, đọc báo cáo này cùng `AGENTS.md`, sau đó xem đầy đủ `Views/MainWindow.xaml`, `Views/MainWindow.xaml.cs`, `ViewModels/HomeViewModel.cs`, `Views/ProductPickerWindow.xaml(.cs)` và `Services/ProductFileCatalog.cs`. Tiếp tục triển khai trực tiếp; báo cáo này chưa bao gồm bản sửa giao diện.
