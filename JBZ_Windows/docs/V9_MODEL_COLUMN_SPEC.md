# Đặc tả cấu trúc cột và parser model – V9

## A. Cấu trúc bảng giao diện

```text
Loại lỗi | I/O | Giắc | Chân | Tên dây | Dây dập nối | Tiết diện | Màu dây | #1 | #2 | #3 | #4
```

### Loại lỗi

Các giá trị chính:

- `Đấu sai S`
- `Đấu sai E`
- `Đầu dò GND`
- `Đầu dây S`
- `Hở mạch`

Thứ tự ưu tiên:

1. Đấu sai S/E.
2. Đầu dò GND.
3. Đầu dây S và Hở mạch.

### I/O

Số pin vật lý mà firmware sử dụng trong `OPEN`, `OTHER`, `TESTPIN`.

### Giắc và Chân

Lấy từ cột 1 và 2 của bản ghi `P<n>`.

### Tên dây

Lấy từ cột 3.

### Dây dập nối

Lấy từ cột 4 và bổ sung quan hệ ngược ở bộ nhớ giao diện.

### Tiết diện

Lấy duy nhất từ cột 5. Nếu trống hoặc không hợp lệ thì để trống.

### Màu dây

Lấy từ cột 6. Giữ mã màu gốc trong cột chữ và tô tối đa bốn ô màu.

## B. Ví dụ đầy đủ

```text
P9=9|4|3|MC21|MC01|0.5|Gr/Br||-1|36
P36=36|4|11|MC01||0.5|B/G||9|
```

Bảng:

| I/O | Giắc | Chân | Tên dây | Dây dập nối | Tiết diện | Màu dây |
|---:|---|---:|---|---|---:|---|
| 9 | 4 | 3 | MC21 | MC01 | 0.5 | Gr/Br |
| 36 | 4 | 11 | MC01 | MC21 | 0.5 | B/G |

Dòng thứ hai không khai báo dập nối nhưng V9 suy ra quan hệ ngược từ dòng thứ nhất.

## C. Thuật toán parser

```text
fields = value.split('|')
physical    = fields[0]
connector   = fields[1]
local_pin   = fields[2]
line_name   = fields[3]
splice_wire = fields[4]
gauge       = fields[5]
color       = fields[6]
special     = fields[7]
parent      = fields[8]
targets     = fields[9]
```

Sau khi đọc toàn bộ pin:

1. Tạo đồ thị liên kết từ `line_name` đến `splice_wire`.
2. Thêm cạnh ngược `splice_wire` đến `line_name`.
3. Loại trùng và loại tự liên kết.
4. Cập nhật cột Dây dập nối dẫn xuất cho mọi pin cùng tên dây.

## D. Quy tắc để trống

- Không có tên dây: Tên dây và Dây dập nối để trống.
- Không có dập nối: Dây dập nối để trống, trừ khi suy ra được chiều ngược.
- Không có tiết diện: Tiết diện để trống.
- Không có màu: Màu dây và `#1..#4` để trống.
- Không dùng dấu `-` trong các ô dữ liệu bảng; dấu `-` chỉ được dùng trên thanh mô tả đầu dò khi cần thông báo nhanh.

## E. Quy tắc hiển thị sản lượng

- `TỔNG = ĐẠT + LỖI` theo dữ liệu lưu của Lot/model hiện tại.
- `Tỷ lệ = ĐẠT / TỔNG × 100`.
- Khi Tổng bằng 0: hiển thị `0.00 %`.
- Ô Tỷ lệ phải hiển thị đủ `100.00 %`.
- Không cắt số khi Tổng/Đạt/Lỗi vượt 9999.
