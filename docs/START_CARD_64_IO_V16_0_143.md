# Chuẩn hóa card mở rộng 64 I/O - V16.0.145

Kết luận sau khi rà soát lại source, trace đã lưu và yêu cầu phần mềm gốc:

- Operator chỉ chọn `ExpansionCardCount` từ 1 đến 10.
- 1 card mở rộng = 64 IO = 2 port nội bộ x 32 IO.
- `TotalIoCapacity = ExpansionCardCount * 64`.
- Port 32 IO là chi tiết nội bộ, không phải lựa chọn trên Production Settings.
- `StartCardNumber` cũ không có tham số protocol/trace phần cứng đủ chứng minh;
  CFG cũ vẫn đọc được nhưng runtime normalize về 1 và CFG mới không ghi key này.

Mapping chuẩn:

- Card 1 / Port 1: IO1-32.
- Card 1 / Port 2: IO33-64.
- Card 2 / Port 1: IO65-96.
- Card 2 / Port 2: IO97-128.
- Card 10 / Port 2: IO609-640.

Frame protocol không đổi: `8C 00 xx 00`, trong đó `xx` là số card/scan-unit
64 IO operator đã cấu hình, không nhân đôi theo hai port và không tự giảm theo
model. Model chỉ dùng để validate dung lượng tối thiểu. Ví dụ operator cấu hình
10 card, model dùng tới IO224 (cần 4 card) thì vẫn gửi `8C 00 0A 00`.

## Hardware regression bắt buộc

1. Lần lượt cấu hình 1, 2, 4 và 10 card; xác nhận frame đủ 64/128/256/640 source.
2. Xác nhận TX `START_SCAN` lần lượt có `xx=1/2/4/10`, không thành 2/4/8/20.
3. Test model chạm biên IO32/33, IO64/65, IO96/97 và IO639/640.
4. Probe tại các biên trên phải giữ đúng global IO.
5. Chạy lại PASS/FAIL, Master, Wrong, Short, Resistance, Leak và Relay trên máy thật.
