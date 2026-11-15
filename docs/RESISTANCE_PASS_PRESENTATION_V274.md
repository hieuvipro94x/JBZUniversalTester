# Hiển thị PASS điện trở — V2026.09.274

Thay quy tắc V272: callback giá trị điện trở cuối cùng đã ổn định và đạt sẽ hiển thị giá trị, PASS của dòng cuối và PASS ở ô trạng thái ngay trong cùng lượt cập nhật giao diện, trước khi TestEngine khôi phục routing D2XX. Các kênh trước vẫn cập nhật trực tiếp; bảng điện trở tiếp tục hiển thị trong pha Resistance, không bị chữ PASS giữa màn hình che lên.

Điều kiện hiển thị sớm: đang sản xuất đúng chu kỳ, đã chốt thông mạch, đủ số kênh điện trở được bật và tất cả đã đo ổn định/đạt, không có lỗi dây/thiết bị/lưu dữ liệu/Probe và các kênh Leak cần thiết đã PASS. Callback cũ sau khi đổi model/generation hoặc hủy chu kỳ bị bỏ qua. Master/manual vẫn dùng luồng hiển thị riêng.

PASS ở đây là kết quả đo đã xác nhận, chưa phải cam kết rằng chuỗi xuất hàng hoàn tất. Khôi phục bo, kiểm tra gate cuối và commit lịch sử vẫn chạy theo thứ tự cũ. Âm thanh hoàn tất, gửi tem, tăng sản lượng và MARKING/JIG vẫn đi sau nhánh commit thành công. Lỗi khôi phục/lưu dữ liệu vẫn được xử lý như lỗi tương ứng, không được dùng nhãn PASS để bỏ qua gate hoặc kích relay.

Bỏ cờ UI `IsPassPresentationPending` của V272 vì không còn cần trì hoãn kết quả đo. Không đổi dữ liệu đo, số mẫu ổn định, thời gian chờ phần cứng, cấu trúc SQLite hoặc dữ liệu lịch sử cũ. Quy tắc timing phần cứng được đối chiếu trong `RESISTANCE_TRACE_20261020_V273.md` vẫn giữ nguyên; chỉ thời điểm hiển thị PASS được thay đổi.
