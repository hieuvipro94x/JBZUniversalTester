# Trạng thái OPEN sống – V6

Mỗi mạng dây được quản lý độc lập theo số đầu tiên trong bản tin `:OPEN`.

Ví dụ mạng hai chân RET1 nguồn 15, đích 16:

```text
:OPEN,15,15,16   -> hiện 2 dòng: nguồn 15 và chân hở 16; Hở mạch = 1
:OPEN,15         -> xóa cả 2 dòng; Hở mạch giảm 1
:OPEN,15,15,16   -> hiện lại cả 2 dòng; Hở mạch tăng 1
```

Mạng nhiều nhánh dùng topology trong file `.model`. Ví dụ nguồn 16 nối tới 63 và 75:

```text
:OPEN,16,16,63,75 -> hiện 16, 63, 75; Hở mạch = 2
:OPEN,16,16       -> vẫn hiện 16, 63, 75 nhờ topology model; Hở mạch = 2
:OPEN,16,63       -> hiện 16 và 63; Hở mạch = 1
:OPEN,16          -> xóa toàn bộ mạng; Hở mạch = 0
```

Giao diện luôn hiện dòng nguồn `Đầu dây S` trước để người vận hành biết mạng nào đang được đối chiếu.
