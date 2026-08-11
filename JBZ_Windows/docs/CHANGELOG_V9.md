# Danh sách thay đổi JBZ Universal Tester Production VI V9

## 1. Mục tiêu V9

V9 sửa lỗi ánh xạ dữ liệu bảng test và mở rộng vùng hiển thị sản lượng. Bản này giữ nguyên giao thức UART, tải model, trạng thái OPEN sống, OTHER, TESTPIN, marking và luồng menu của V8.

## 2. Thứ tự cột mới

Thứ tự cột bắt buộc trên màn hình test:

```text
Loại lỗi | I/O | Giắc | Chân | Tên dây | Dây dập nối | Tiết diện | Màu dây | #1 | #2 | #3 | #4
```

Thay đổi so với V8:

- Thêm cột **Dây dập nối** ngay sau **Tên dây**.
- Chuyển **Tiết diện** lên trước **Màu dây**.
- Không hiển thị tên dây dập nối trong cột Tiết diện.
- Bốn ô `#1..#4` vẫn tô màu theo mã màu dây.

## 3. Sửa ánh xạ trường Pin trong file `.model`

Bản ghi chuẩn:

```text
P<n>=physical|connector|connector_pin|line_name|splice_wire|gauge|color|type|parent|targets
```

Ánh xạ V9:

| Chỉ số | Ý nghĩa | Ví dụ |
|---:|---|---|
| 0 | I/O vật lý | `9` |
| 1 | Giắc | `4` |
| 2 | Chân trên giắc | `3` |
| 3 | Tên dây | `MC21` |
| 4 | Dây dập nối | `MC01` |
| 5 | Tiết diện | `0.3`, `0.5`, `1.25` |
| 6 | Màu dây | `Gr/Br`, `B/G` |
| 7 | Kiểu đặc biệt | `A`, `2` hoặc trống |
| 8 | Chân nguồn/parent | `-1`, `15` |
| 9 | Chân đích/targets | `16`, `80/79` |

Ví dụ:

```text
P9=9|4|3|MC21|MC01|0.5|Gr/Br||-1|36
```

Hiển thị:

```text
I/O: 9
Giắc: 4
Chân: 3
Tên dây: MC21
Dây dập nối: MC01
Tiết diện: 0.5
Màu dây: Gr/Br
```

## 4. Quy tắc tiết diện

Các giá trị hợp lệ:

```text
0.3
0.5
0.75
0.85
1.0
1.25
2.0
0,5
0.5 mm²
1.25 SQ
```

- Dấu phẩy thập phân được đổi thành dấu chấm khi hiển thị.
- Nếu cột 5 trống: giao diện để trống.
- Nếu cột 5 chứa dữ liệu không phải tiết diện: giao diện để trống.
- Không dùng cột 4 làm tiết diện.
- Không dùng tên dây như `MC01`, `MC21` làm tiết diện.

## 5. Dây dập nối hai chiều

Nếu model chỉ khai báo:

```text
MC21 -> MC01
```

thì giao diện dẫn xuất quan hệ ngược:

```text
MC01 -> MC21
```

Quy tắc:

- Dòng `MC21` hiển thị `MC01` ở cột Dây dập nối.
- Dòng `MC01` cũng hiển thị `MC21`.
- Không ghi thay đổi ngược vào file `.model`.
- Không tạo tên trùng.
- Nếu một dây dập nối với nhiều dây, các tên được phân cách bằng `/`.
- Không hiển thị dây tự liên kết với chính nó.

## 6. Màu dây

- Mã màu lấy từ cột 6.
- Cột chữ hiển thị mã gốc, ví dụ `Gr/Br`.
- Các ô `#1..#4` hiển thị màu trực quan.
- Không đọc cột tiết diện làm màu.
- Một số model cũ có thể dùng cột 7 cho màu; V9 chỉ dùng cột 7 làm fallback khi nhận đúng mã màu.

## 7. Mở rộng vùng sản lượng

Vùng bên phải gồm:

```text
ĐANG ĐO | TỔNG | ĐẠT | LỖI | Tỷ lệ
```

V9 tăng kích thước:

- Khung bên phải: từ khoảng `350` lên `445` đơn vị theo tỷ lệ màn hình.
- Ô trạng thái: từ khoảng `190` lên `205`.
- Nhãn TỔNG/ĐẠT/LỖI/Tỷ lệ: từ khoảng `65` lên `78`.
- Ô số: từ khoảng `82` lên `125`.
- Ô phần trăm đủ chỗ cho `100.00 %` và số sản lượng nhiều chữ số.
- Toàn bộ kích thước vẫn nhân theo `ui_scale`, nên tự co theo độ phân giải thực tế.

## 8. Đầu dò GND

Thông tin đầu dò được đổi thứ tự thành:

```text
I/O | Giắc | Chân | Dây | Dập nối | Tiết diện | Màu
```

Dữ liệu đầu dò dùng cùng parser với bảng lỗi nên không còn nhầm Dây dập nối thành Tiết diện.

## 9. Tương thích model cũ

- Model không có cột Dây dập nối: để trống.
- Model không có Tiết diện: để trống.
- Model chỉ có màu ở cột 6: hoạt động bình thường.
- Model AO/A1 hoặc kênh đặc biệt vẫn giữ trường loại ở cột 7.
- Cấu trúc parent/targets và tải model xuống bo không thay đổi.

## 10. Kiểm thử V9

V9 có các kiểm thử mới:

- đúng thứ tự 12 cột;
- tách riêng `splice_wire`, `gauge`, `color`;
- tiết diện `0.3`, `0.5`, `1.25`;
- dấu phẩy `0,5` đổi thành `0.5`;
- thiếu tiết diện thì để trống;
- dữ liệu không hợp lệ không được đưa vào cột tiết diện;
- liên kết dập nối một chiều được hiển thị hai chiều;
- không tạo liên kết trùng.

Kết quả hiện tại:

```text
32 passed
```
