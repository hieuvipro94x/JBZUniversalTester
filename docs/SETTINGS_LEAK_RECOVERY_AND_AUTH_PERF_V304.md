# V2026.09.304 — phục hồi trang Cài đặt và kiểm tra độ trễ xác thực

## Nguyên nhân thực tế

Log Debug ngày 23/10/2026 ghi `Open Settings failed: InvalidDataException` do
`C:\ITEM\1.tht` không khớp SHA256 trong `1.tht.leak.json`. Cài đặt tự đọc mã gần
nhất từ CFG dù MainWindow chưa nạp được model. Hộp lỗi chung nhắc thông tin truy
cập khiến người dùng hiểu nhầm lỗi mật khẩu.

## Sửa trang Cài đặt

ViewModel bắt lỗi đọc/xác thực cấu hình Leak chỉ tại đường mở editor. Nó hiển thị
cảnh báo rõ ở nhóm Leak và khóa form/chạy Leak thủ công. Các nhóm IO, Relay, điện
trở, in tem vẫn mở theo quyền đã cấp. Fallback chỉ là bản sao profile đã lưu để
hiển thị; không lấy gán kênh từ file lỗi, không tự tắt Leak trong CFG.

Lưu thay đổi nhóm khác bỏ qua cập nhật profile Leak bị lỗi. Guard cũng chặn sửa
trực tiếp dữ liệu Leak của editor này. Validation phần còn lại không bị chặn bởi
cấu hình Leak đang cách ly. File THT, sidecar và lịch sử sản xuất không bị sửa.

`ProductionConfigService.GetWaterProofProfileForPath` và `ModelLeakLayoutService`
giữ nguyên validation nghiêm ngặt khi nạp mã sản xuất. Muốn sử dụng mã bị lỗi phải
khôi phục đúng cặp THT/sidecar rồi mở lại Cài đặt; bản sửa không tự sửa hash hoặc
cho phép sản xuất bằng gán connector chưa xác minh.

## Xác thực và khả năng phản hồi

Nạp credential vẫn chạy bằng Task.Run; xác thực có API VerifyAdminAsync. PBKDF2
giữ nguyên salt/210.000 vòng, chạy worker. Việc đọc quyền của operator dùng record
bất biến và Volatile.Read, không chờ khóa trong lúc băm hoặc ghi credential. Đổi
mật khẩu/cấp quyền vẫn serialize các writer và lưu nguyên tử như V303. Xác thực
từ snapshot chỉ thành công nếu credential chưa đổi khi phép băm hoàn tất.

Thêm log `AUTH_PERF phase=ACCESS_LOAD` và `AUTH_PERF phase=VERIFY_ADMIN` ghi thời
gian, không ghi mật khẩu. Hash chỉ được tính khi xác thực/đổi mật khẩu; không có
vòng tính mật khẩu trong scan/PASS/FAIL.

Kiểm thử độc lập trên máy hiện tại: khởi tạo credential khoảng 65 ms, xác thực
trung bình 50 ms / lớn nhất 62 ms trong 5 lượt, khoảng cách Dispatcher Input tick
lớn nhất 22 ms, đọc quyền lớn nhất 0,082 ms. Đây là số đo fixture WPF với credential
tạm, không phải đo độ trễ thiết bị thật hoặc cam kết cho mọi máy.

Regression dùng file THT/sidecar và credential tạm: hash không khớp, JSON hỏng,
editor vẫn mở, form Leak bị khóa, nhóm khác được lưu, profile/file gốc không đổi,
đường production vẫn từ chối cặp file sai, Dispatcher tiếp tục nhận input khi băm.
