# Bộ đếm sản xuất và LOT theo ngày

Từ V2026.09.200, sản lượng trên màn hình test và khôi phục LOT từ SQLite dùng đúng `PartId` và ngày kết quả được chọn. Truy vấn bao gồm các đợt LOT của cùng mã hàng trong ngày đó để không bỏ sót sản lượng cũ. LOT lấy đúng PASS cuối cùng của ngày; FAIL không tăng LOT. Số lượng tháng và tích lũy vẫn giữ toàn bộ dữ liệu tương ứng.

Khi ngày Windows thay đổi (tự động qua ngày, chỉnh ngày tiến hoặc lùi), màn hình nạp sản lượng và LOT đã lưu của mã hàng trong ngày được chọn, kể cả khi chuyển mã hàng hoặc mở lại ứng dụng. Không tạo một `HistoryBatchId` mới chỉ vì đổi ngày. Nếu mã hàng chưa có kết quả trong ngày đó thì Tổng/PASS/FAIL về 0, LOT đang chạy về `0`; PASS đầu tiên trong ngày mới là `1` (quy tắc từ V2026.09.423). Đổi mức LOT bắt đầu trong cài đặt vẫn tạo đợt History mới nhưng không xóa hay che sản lượng đã kiểm tra của ngày đó.

Màn hình test kiểm tra đổi ngày bằng timer đồng hồ hiện có; START cũng kiểm tra trước khi cho chu kỳ reserve LOT. Kết quả tải thống kê của ngày/đợt cũ không được ghi đè đợt mới. Reservation chưa commit giữ nguyên chu kỳ hiện tại; chuyển ngày sau khi reservation được giải quyết.

Reset chỉ đổi cấu hình/bộ đếm hiển thị và phạm vi truy vấn. Không xóa hoặc sửa LOT, timestamp, sản lượng hay bản ghi lịch sử cũ. Các bản ghi sai LOT đã lưu ở phiên cũ được giữ nguyên để bảo toàn truy vết. History và export tiếp tục truy vấn các bản ghi cũ.

Từ V2026.09.420, sau khi operator sửa LOT bắt đầu của mã hàng, LOT hiện tại luôn bằng `StartLotNo + PASS` của ngày được chọn, kể cả khi ngày sửa cấu hình khác ngày đang xem. LOT bắt đầu được lưu riêng theo mã hàng; sang ngày chưa có sản lượng vẫn dùng mức bắt đầu đó, PASS đầu tiên cộng 1. Cấu hình cũ chưa có đợt sửa LOT vẫn khôi phục theo LOT lịch sử để giữ tương thích.

Nút in bản sao trên TestView chờ nạp xong thống kê và dùng LOT hiện tại khi LOT đã đổi so với tem gửi thành công trước đó. Ví dụ sửa 2000 thành 5000 khi ngày đó đã có 101 PASS thì bản sao in 5101. Thao tác này không cộng PASS/LOT, không sửa payload hoặc LOT của bản ghi gốc; thông tin in lại ghi LOT bản sao. BCW tiếp tục dùng serial đã cấp cho tem gốc. In lại từ History và retry tem lỗi vẫn giữ snapshot gốc.


## V2026.09.423: mã chưa test trong ngày bắt đầu LOT 0

Khi chọn mã hàng, không kế thừa LOT chung. Trong lúc chờ đọc thống kê SQLite,
TestView hiển thị LOT 0. Nếu đúng file/mã đang chọn chưa có kết quả trong ngày,
runtime lưu LOT 0 và đánh dấu ngày bắt đầu từ 0; PASS đầu dùng LOT 1. Mã đã test
vẫn khôi phục LOT của chính ngày/file đó, kể cả khi đổi mã, mở lại hoặc đổi ngày.

Mức StartLotNo người vận hành đã cấu hình và BulkPrintLotNo được giữ nguyên.
Cờ DailyLotStartsAtZero tách LOT của ngày khỏi mức cấu hình đó, tránh cộng lại
base 123456 sau khi PASS đầu đã ghi LOT 1. Cờ được lưu ở field thứ 7 tùy chọn của
ProductLot trong CFG; cấu hình 6 field cũ vẫn đọc được. Lưu thay đổi LOT bắt đầu
mới một cách rõ ràng vẫn áp dụng cơ chế base + PASS cho ngày đã có sản lượng.

Không có migration SQLite, không sửa/xóa history hoặc timestamp. Reset bị chặn
khi còn reservation chưa commit. Kết quả thống kê cũ vẫn chịu guard mã/ngày/batch;
lưu CFG thất bại hoàn tác cả LOT và cờ ngày. Kiểm thử dùng CFG/SQLite tạm và kiểm
tra integrity_check cùng các LOT lịch sử trước/sau.


## V2026.09.424: ngày chỉ có FAIL vẫn LOT 0

Điều kiện khởi đầu từ 0 dựa trên PASS trong ngày bằng 0, kể cả Tổng/FAIL đã lớn
hơn 0. Trường hợp WH321331 trên log V423 (Tổng 1, PASS 0, FAIL 1) không được
khôi phục base 123456. PASS đầu tiếp theo dùng LOT 1. Số FAIL và LOT đã ghi trong
lịch sử cũ được giữ nguyên; chỉ đồng bộ LOT đang chạy và cờ ngày trong CFG.


## V2026.09.425: không hỏi cấu hình lần đầu; LOT do operator đặt được giữ riêng

Chọn mã lần đầu không mở hộp thoại máy tem/Leak/LOT và không bắt buộc chọn loại
tem. Mã chưa đặt LOT bắt đầu dùng 0; dữ liệu 6/7 field cũ tiếp tục theo quy tắc
V424, không tự phục hồi base123456 khi chưa có PASS. Khi operator đổi LOT bắt
đầu trong cài đặt mã, cờ UseConfiguredStartLot (field8 tùy chọn) được lưu cùng
mã đó. Mức này dùng cả khi PASS0 và sang ngày mới. Lưu cài đặt khác không tự
kích hoạt base cũ. Base2000 + PASS20 = LOT2020; cột Số đếm PASS và CSV/XLSX lấy
đúng LOT đã lưu của từng PASS. Không tính lại hoặc ghi đè LOT lịch sử cũ.

Cột Tiến độ đổi thành KẾT QUẢ, hiển thị PASS/NG trên History và CSV/XLSX.
Trạng thái lưu trong database không đổi.


## V2026.09.427: dọn backup migration khi thoát bình thường

Sau khi production writer đóng xong, worker dọn các file pre-schema-v*.backup
(tên cố định và có timestamp) cạnh database đã khởi tạo thành công trong phiên.
Chỉ xóa khi database chính còn tồn tại, integrity_check đạt và schema hiện tại
hợp lệ. Không xóa DB chính, WAL/SHM, backup thủ công, cấu hình hoặc lịch sử.
Database lỗi/mất, đường dẫn liên kết hoặc file backup bị khóa được giữ lại và
lỗi được ghi log. Không thay đổi đường dẫn RuntimePaths trong bản này.


## V2026.09.428: History dùng ProgramData cố định

Database sản xuất chuyển sang %ProgramData%\JBZUniversalTester\Data\JBZUniversalTester.db.
Startup worker tự sao lưu SQLite từ Data/JBZUniversalTester.db cạnh EXE (hoặc
Data/History/test-history.db cũ) nếu đích chưa tồn tại. Bao gồm WAL, không copy
riêng file DB đang mở. So sánh đầy đủ nội dung mọi bảng và integrity_check trước
khi đổi tên file staging thành DB đích. File nguồn được giữ nguyên. Không đổi
CycleId, khóa ngoại, LOT, timestamps, dữ liệu Master/Leak/đo điện trở.

Nếu không có nguồn cạnh EXE, kiểm tra các thư mục phiên bản cũ: chỉ tự chọn khi
có một nguồn hoặc tất cả nguồn có nội dung giống nhau. Database khác nhau sẽ
chặn startup và yêu cầu chạy EXE mới từ thư mục đang vận hành; không chọn theo
size/ngày sửa, không tự gộp hoặc bỏ lịch sử. Máy đã có CFG nhưng mất nguồn DB
không tự tạo DB rỗng. Cài mới thật sự vẫn được tạo schema mới.

Đích đã tồn tại luôn được giữ nguyên, không dò/nhập lại bản cũ. Thư mục Data
riêng của ứng dụng được cấp Modify cho Windows Users; không sửa quyền chung
của ProgramData. Không đủ quyền hoặc chuyển lỗi thì báo lỗi startup, không chạy
sang một DB rỗng ở đường dẫn khác. CFG, log, PartCnt và nhật ký BCW tiếp tục dùng
vị trí hiện có. Self-tests cô lập DB mặc định vào thư mục tạm.

History: cột File lấy chiều rộng theo tên file ở mỗi trang nạp; không co lại khi
cuộn/nạp thêm, giữ dòng32px. Tooltip của ô hiển thị đủ tên file.
Tooltip History (File, lịch sử kiểm tra, mã vạch đầu ra) chỉ mở khi chữ bị cắt bởi ô hoặc vùng cuộn. Bỏ tooltip chung cả dòng; chỉ đo ô đang rê chuột tại ToolTipOpening, không đo lại bảng khi cuộn.
