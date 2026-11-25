# PartCnt dùng chung thư mục database — V2026.09.476

Đường dẫn chuẩn: `C:\ProgramData\JBZUniversalTester\Data\PartCnt.txt`, cạnh `JBZUniversalTester.db` và CFG. Khi self-test đổi đường dẫn DB, PartCnt cũng đi theo thư mục DB tạm, không đụng dữ liệu máy sản xuất.

Bootstrap thực hiện ở bước xử lý file/database nền sau khi xác nhận database máy. File PartCnt ở thư mục chuẩn đã có sẽ được giữ lại. Nếu chưa có, file cạnh EXE hiện tại được sao chép một lần, giữ nguyên nguồn. Với database đã hoàn tất import PartCnt, không quét và chọn các bản PartCnt cũ ở các thư mục phiên bản: SQLite tiếp tục là nguồn số đếm, mirror được dựng lại từ đó. Trường hợp nhập lần đầu từ phần mềm cũ có thể dùng nguồn ở phiên bản trước; nhiều nguồn khác nội dung sẽ bị chặn, không đoán theo số đếm lớn nhất vì PIN có thể đã được reset hợp lệ.

Sau bootstrap, tất cả PartCounterStore mặc định ghi đường dẫn chuẩn. File nguồn cũ cạnh EXE được giữ làm dự phòng và không được runtime mới ghi tiếp. THT, các dòng lịch sử và bộ đếm SQLite không bị reset hoặc thay đổi schema bởi việc đổi đường dẫn này.
