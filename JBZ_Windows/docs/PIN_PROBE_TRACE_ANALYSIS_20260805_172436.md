# Phân tích trace đầu dò GND – 2026-08-05 17:24:36

Nguồn: `JBZ_TRACE_20260805_172436.tar.gz`, phần mềm gốc `UniversalTesterRev1.42`, model `WH321798`.

## 1. Kết luận giao thức

Trong toàn bộ phiên vận hành, PC chỉ gửi các lệnh test chính:

```text
:START
:MAXEXT,0
:STOP
```

Không có bất kỳ lệnh TX `:PINTEST,...` nào.

Sau khi nhận `:START`, firmware tự giám sát đầu dò GND và phát:

```text
:TESTPIN,<physical_pin>,ON
:TESTPIN,<physical_pin>,OFF
```

Do đó đầu dò không cần bật bằng một command riêng. Nút GUI chỉ được phép ẩn/hiện thông tin, không được thay đổi trạng thái bo.

## 2. Dữ liệu thực tế

Trace có 28 bản tin `TESTPIN`, tạo thành 14 cặp ON/OFF sạch trên 14 pin vật lý:

```text
125, 10, 6, 189, 190, 113, 126, 114,
200, 123, 77, 76, 78, 39
```

Thời gian giữ đầu dò:

- nhỏ nhất: khoảng 0,435 giây;
- trung vị: khoảng 1,401 giây;
- lớn nhất: khoảng 2,279 giây.

Không có ON trùng liên tục và không có OFF mồ côi trong trace.

## 3. Nhiều chân đồng thời

Trace chứng minh firmware có thể báo nhiều chân ON cùng lúc:

```text
:TESTPIN,189,ON
:TESTPIN,190,ON
...
:TESTPIN,189,OFF
:TESTPIN,190,OFF
```

và:

```text
:TESTPIN,113,ON
:TESTPIN,126,ON
:TESTPIN,114,ON
```

Giao diện không được lưu chỉ một pin. Nó phải giữ một tập pin active và hiển thị tất cả, pin chạm mới nhất đứng trước.

## 4. Quan hệ với OPEN

Khi đầu dò chạm một pin thuộc mạng đang hở, firmware có thể đồng thời cập nhật OPEN của mạng đó.

Ví dụ pin 125 thuộc mạng nguồn 82:

```text
:TESTPIN,125,ON
:OPEN,82
:TESTPIN,125,OFF
:OPEN,82,82,125
```

Ví dụ pin 10:

```text
:TESTPIN,10,ON
:OPEN,10
:TESTPIN,10,OFF
:OPEN,10,10,185
```

`TESTPIN` dùng để xác định pin đầu dò đang chạm. `OPEN` vẫn tiếp tục là snapshot trạng thái điện hiện tại của mạng và phải cập nhật bộ đếm hở mạch theo quy tắc V6.

Không được suy ra pin đầu dò chỉ từ OPEN, vì một số pin như 6, 200 và 123 có TESTPIN hợp lệ nhưng trace không có OPEN thay đổi tương ứng trong khoảng chạm.

## 5. Đánh số pin

Các giá trị TESTPIN khớp trực tiếp với số physical ở trường đầu tiên của dòng `P...` trong file `.model`.

Ví dụ model `WH321798`:

```ini
P125=125|HOLDER 07|7|M3C3|||P||82|
P189=189|HOLDER 13|5|M9C5|||Gr||18|
P200=200|BAND|5||||||-1|
```

Vì vậy không trừ hoặc cộng 1. Pin được hiển thị đúng số firmware gửi.

## 6. Quy tắc phần mềm V8

1. Không gửi `:PINTEST`.
2. Chỉ lắng nghe TESTPIN trong kết nối UART vận hành.
3. ON thêm pin vào trạng thái active; OFF xóa pin đó.
4. Hỗ trợ nhiều pin active đồng thời.
5. Chân mới chạm đứng đầu danh sách đầu dò.
6. TESTPIN không được gộp với lịch sử; trạng thái phải biến mất ngay khi OFF.
7. CLEAR, STOP, về menu và bắt đầu chu kỳ mới phải xóa toàn bộ pin đầu dò active.
8. OPEN tiếp tục cập nhật độc lập theo network snapshot.
