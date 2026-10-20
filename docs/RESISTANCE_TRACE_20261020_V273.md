# Đối chiếu đo điện trở — V2026.09.273

Nguồn: session gốc `20261020_141444_production_Htdrv3-JBZ27000_RT`, file `d2xx_raw_io.csv` và `events.jsonl`. Đọc trace, không sửa dữ liệu session hay lịch sử sản xuất. Giờ dưới đây đã đổi từ UTC sang UTC+7.

| Chọn CH1 bằng 91 00 00 01 | Đến lệnh khôi phục đầu tiên | Sáu lệnh khôi phục |
|---|---:|---:|
| 14:14:53.003 | 928 ms | 1779 ms |
| 14:15:03.180 | 794 ms | 1790 ms |
| 14:15:09.981 | 810 ms | 1771 ms |
| 14:15:16.436 | 801 ms | 1778 ms |
| 14:15:22.770 | 794 ms | 1770 ms |

Cả năm lượt đều gửi ba cặp `91 00 00 00` → `90 00 00 30`, cách nhau khoảng 350 ms. Khoảng 1,8 giây khôi phục cũng có ở phần mềm gốc; không có căn cứ bỏ vòng hoặc giảm thời gian phần cứng. Phần mềm hiện tại giữ nguyên các lệnh và số vòng này.

V273 tính khoảng 350 ms từ thời điểm native FT_Write hoàn tất, trừ thời gian đã trôi qua trên đường trả kết quả/log. Phần chọn kênh và khôi phục tiếp tục trên worker thay vì chờ Dispatcher ở từng await. Không thêm chờ sau INIT_2 cuối, không gửi thêm INIT trước lần START_SCAN đã được chuẩn bị. Giữ cơ chế owner/khóa native I/O, kiểm tra ghi thiếu byte, cancellation và lỗi truyền lên caller; chỉ đánh dấu scan prepared khi toàn bộ chuỗi hoàn tất.

Thêm log `RESISTANCE_TIMING phase=SELECT/RECOVERY_BEGIN/RECOVERY_END` để đo mức tiết kiệm thực tế ở máy. Đây là tối ưu chi phí điều phối, không hứa loại bỏ 1,8 giây phần cứng. PASS cuối bảng điện trở và ô trạng thái vẫn xuất hiện cùng nhau sau khi commit kết quả; relay vẫn chạy sau commit.

Session không có event đọc/ghi VISA/GPIB, nên thời gian từ chọn CH1 đến khôi phục không đủ chứng minh số mẫu đo, lệnh SCPI hoặc thời điểm vẽ PASS của phần mềm gốc. Không đổi số mẫu ổn định, thời gian settle, giới hạn điện trở, công thức đo hay cấu hình đồng hồ dựa trên suy đoán. Kết quả timing trên phần cứng cần được đo lại bằng log V273.
