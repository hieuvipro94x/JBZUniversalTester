# Xác nhận đứt dây / tuột tuýt

V2026.09.228 thêm tùy chọn trong Cài đặt Production: **Xác nhận đứt dây / tuột tuýt**, mặc định tắt. Thời gian chờ mặc định 5 giây, cho phép 1–120 giây.

Chỉ áp dụng trong chu kỳ Production ở pha Continuity, khi Master đã đạt, có sản phẩm và snapshot D2XX hợp lệ đang được quét. Không áp dụng Master, Probe, IO mapping, Manual, Leak, đo điện trở, hoặc chờ tháo sản phẩm. Mỗi cặp expected IO bị thiếu có bộ đếm riêng; nối lại cặp đó xóa thời gian chờ. Thay model/cycle hoặc mất eligibility xóa bộ đếm. Không có connectivity trên bàn thì không xác nhận đứt dây.

Khi quá thời gian, cảnh báo các cặp IO thiếu bằng luồng FAIL hiện có: commit FAIL và child faults trước popup; yêu cầu xác nhận trước JIG, không chạy MARKING PASS. Xác nhận đứt dây vẫn được yêu cầu khi tùy chọn xác nhận lỗi sai dây/chập mạch tắt. ProductRemoved vẫn bắt buộc trước ARM lượt mới. Bộ đếm cập nhật trên frame thay đổi và nhịp đồng hồ TestWindow để frame không thay đổi vẫn được kiểm tra; Khi không có frame thay đổi, việc kiểm tra timeout theo nhịp đồng hồ TestWindow; thời điểm hiện popup còn phụ thuộc khả năng đáp ứng của UI.

Không sửa topology THT, protocol, ADC hay schema SQLite. Các cfg cũ chưa có key mới giữ chức năng tắt. Leak/printer COM độc lập với D2XX như trước.
