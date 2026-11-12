# Kiểm tra Master trên máy sản xuất — V2026.09.155

## Chuẩn bị

1. Dùng EXE V2026.09.155, giữ nguyên CFG, model THT và database của máy sản xuất. Đóng bản cũ trước khi mở bản mới để chỉ một chương trình giữ bo.
2. Trong cài đặt bật **Log hệ thống**, **Kiểm tra mẫu đầu ca** và các mẫu NG cần thử. Ghi lại số điểm sai dây/đứt dây, kiểu đấu relay và thời gian mở JIG. Bấm lưu.
3. Dùng mẫu thật đã xác định lỗi và đúng mã hàng; bố trí người vận hành quan sát chuyển động JIG. Không mở công cụ HardwareVerification đồng thời với ứng dụng.

## Các bài cần chạy

| Bài | Thao tác | Kết quả yêu cầu |
|---|---|---|
| Mẫu đạt | Lắp toàn bộ mẫu đạt; chạy điện trở/Leak nếu mã hàng bật | Lưu MASTER_GOOD; kích JIG; relay về OFF; tháo hết mới bắt đầu NG |
| Mẫu đạt bị lỗi | Lắp mẫu có một lỗi dây ở bước mẫu đạt | Hiện KHÔNG ĐẠT, giữ khóa; tháo hết rồi thử lại mẫu đạt |
| Sai dây | Lắp đúng số điểm đã cài cùng lúc | Bộ đếm/bảng đúng số lỗi hiện tại; đủ điểm mới lưu và mở JIG |
| Lỗi sai dây thay đổi | Khi chưa đủ điểm, bỏ một lỗi rồi tạo lỗi khác mà chưa tháo hết mẫu | Lỗi cũ biến mất; không cộng dồn thành đủ điểm |
| Chập mạch | Lắp mẫu có cầu chập xác định | Nhận CHẬP; lưu MASTER_BAD và mở JIG |
| Tuột tuýt | Lắp mẫu với N−1/N kết nối khi số điểm đứt = 1 | Nhận đúng một kết nối hở; N/N và N−2/N không được chấp nhận |
| NG và Leak bật | Chạy cả ba loại NG ở mã hàng bật Leak | Các mẫu NG chỉ xác nhận điện; mẫu đạt vẫn phải chạy Leak |
| Chưa tháo hết | Sau PASS mẫu, giữ một kết nối hoặc chân active | Giữ bước; không mở sản xuất hoặc nhận mẫu tiếp theo |
| Mẫu cuối | Đạt mẫu cuối và tháo hết sau khi JIG về OFF | Hai frame hoàn chỉnh xác nhận tháo, rồi hiện LẮP SẢN PHẨM |
| Chọn ít mẫu | Chỉ chọn mẫu đạt + một NG | Bỏ qua các NG không chọn; hoàn tất mẫu đã chọn rồi mở sản xuất |

Master không tăng LOT/PASS/FAIL sản xuất hoặc bộ đếm Probe. Mỗi mẫu có lịch sử riêng; kiểm tra cả trường hợp FAIL và PASS trong Lịch sử.

## Log cần gửi để phân tích tiếp

- `JBZUniversalTester.log` trong thư mục EXE chạy thực tế; kiểm tra Log hệ thống đã bật trước khi thử.
- `JBZUniversalTester.cfg` và đúng file `.tht` đang dùng để đối chiếu số điểm và IO.
- Ghi giờ thử, loại mẫu, cặp IO thực tế gây lỗi, JIG có mở/trở về đúng không; chụp màn hình nếu trạng thái khác bảng trên.
- Các mốc cần có: `MASTER LIVE FAULTS`, `MASTER NG ELECTRICAL ONLY`, `MASTER JIG SAFE-OFF`, `MASTER VALIDATION COMPLETED` và các dòng History commit.

IO chỉ quan sát thông mạch; connector chưa lắp tạo cùng một cạnh hở sẽ không phân biệt được với tuột tuýt nếu không có tín hiệu xác nhận connector riêng.
