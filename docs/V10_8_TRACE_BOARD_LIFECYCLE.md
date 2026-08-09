# V10.8 - Phân tích trace 20260807_162513 và sửa lifecycle board

## 1. Startup của phần mềm gốc

Trace cho thấy cùng một handle FTDI được mở và cấu hình 115200/8N1/no flow.
Ngay sau mở, Htdrv không handshake ngay mà đưa board về trạng thái biết trước:

- 09:25:13.670 — `STOP_SCAN 8D 00 00 00`
- 09:25:13.883 — `FT_Purge`
- 09:25:14.229 — `HANDSHAKE 8A 01 01 01`
- 09:25:14.233 — RX `0F 00`
- 09:25:14.244 — `INIT_1 91 00 00 00`
- 09:25:14.596 — `INIT_2 90 00 00 30`
- 09:25:15.753 — `START_SCAN 8C 00 04 00`

Điểm quan trọng: STOP trước handshake giúp firmware thoát khỏi scan còn sót từ
phiên cũ. V10.8 áp dụng lại bước recovery này.

## 2. Restart scan sau relay

Trong nhiều chu kỳ trace:

`STOP_SCAN -> RESET_CLEAR -> RELAY_1_ON -> RELAYS_OFF -> RELAY_2_ON -> RELAYS_OFF -> START_SCAN`

Không có `INIT_1/INIT_2` giữa RESET_CLEAR và START_SCAN kế tiếp.
Do đó trạng thái `scanPrepared` phải được giữ sau STOP/RESET. V10.7 làm sai ở
điểm này và tạo thêm khoảng chờ INIT ~700 ms ở nhiều lần chuyển mode/restart.

## 3. Shutdown của phần mềm gốc

Cuối trace:

- 09:26:05.787 — `STOP_SCAN`
- 09:26:06.083 — `RESET_CLEAR`
- 09:26:06.257 — `INIT_1`
- 09:26:06.605 — `INIT_2`
- 09:26:06.952 — `STOP_SCAN`
- 09:26:07.083 — `FT_Close`

Đây là sequence idle/close chuẩn được V10.8 dùng khi thoát hẳn app.

## 4. Nguyên nhân board bị treo ở V10.7

Hai vấn đề cùng tồn tại:

1. Scan worker gọi `FT_GetQueueStatus/FT_Read` trực tiếp không dùng `_ioLock`,
   trong khi command thread có thể `FT_Purge/FT_Write` cùng lúc.
2. Nếu worker không dừng trong timeout, code xóa `_scanTask/_scanCts` trước rồi
   ném exception. Một worker cũ có thể tiếp tục giữ/use handle nhưng code đã mất
   reference và sau đó vẫn có thể FT_Close handle.

V10.8 serialize toàn bộ D2XX API và chỉ xóa task/CTS sau khi worker đã kết thúc.

## 5. PASS / relay / sound

V10.7 đổi `State=PASS` + phát DINGDONG trước khi `CompletePassAsync()` kịp
STOP/RESET, vì vậy âm thanh có thể lệch xa thời điểm Relay 1.

V10.8 chuyển callback PASS vào đúng mốc Relay 1 bắt đầu:

`STOP/RESET (nếu cần) -> RELAY_1_ON + PASS xanh + DINGDONG -> pulse -> Relay 2`

Như vậy âm thanh PASS và relay bắt đầu cùng một trigger phần mềm.

## 6. Chặn command muộn sau khi đóng cửa sổ

Ngoài worker D2XX, V10.8 còn gắn CancellationToken riêng cho từng chu kỳ test.
Khi rời TestView, chuyển sang TestPin hoặc thoát app, token chu kỳ bị hủy trước
cleanup board. Vì vậy các Task đang chờ relay/interlock/đo điện trở/restart scan
không thể tỉnh lại sau đó và gửi command xuống handle của phiên mới.

## 7. Timing relay từ trace

Các chu kỳ PASS không đo điện trở trong trace lặp lại rất ổn định:

- STOP_SCAN -> RESET_CLEAR: khoảng 280-296 ms
- RESET_CLEAR -> RELAY_1_ON: khoảng 166-179 ms
- RELAY_1_ON -> RELAYS_OFF: khoảng 230-239 ms
- RELAYS_OFF -> RELAY_2_ON: khoảng 427-434 ms
- RELAY_2_ON -> RELAYS_OFF: khoảng 217-247 ms
- RELAYS_OFF -> START_SCAN: khoảng 118-128 ms

V10.8 phát DINGDONG và đổi nền PASS ngay tại trigger RELAY_1_ON, nên âm PASS
không còn chạy trước relay trong thời gian STOP/RESET.
