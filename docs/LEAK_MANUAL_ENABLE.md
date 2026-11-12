# Bật/tắt Leak và chạy Manual — V2026.09.231

File `.tht.leak.json` cung cấp cấu hình kênh ban đầu cho mã hàng chưa có profile đã lưu. Khi profile đã được lưu trong Cài đặt, lựa chọn `Enabled`, bật/tắt từng kênh và connector đã chọn của người vận hành được ưu tiên. Connector còn trống có thể được điền từ sidecar; kênh chưa có connector không được tự bật. Kiểm tra định dạng sidecar, hash THT và connector trong sidecar vẫn được giữ nguyên. Không ghi lại hoặc đổi format của THT/sidecar.

Checkbox **BẬT TEST LEAK** điều khiển bước Leak tự động của mã hàng. Sau khi bỏ chọn và lưu, đọc lại cấu hình hoặc chọn lại mã hàng không tự bật trở lại. Khi muốn bật lại, người vận hành chọn checkbox rồi lưu.

Nút **CHẠY TEST** trong Cài đặt là chạy Manual. Luồng Manual clone profile rồi bật riêng bản clone cho lượt chạy; giữ nguyên các kênh và áp suất/thời gian đã chọn. Không sửa flag Leak tự động đã lưu, không tăng sản lượng, không ghi kết quả Production và không pulse relay PASS/JIG. Các interlock Production/Manual, một owner COM và cleanup hiện có vẫn được giữ. Cần chọn COM hợp lệ, khác cổng máy in, và ít nhất một kênh.

Self-tests kiểm tra bật/tắt sau reload/cfg roundtrip, khởi tạo profile từ ModelCreator, và chạy Manual với automatic OFF đến cancellation gate trước khi mở bất kỳ COM thật nào. Chưa kiểm chứng trên phần cứng thật.
