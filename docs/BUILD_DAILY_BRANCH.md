# Nhánh build theo ngày

`BUILD_ONE_FILE.cmd` gọi `Scripts/Set-DailyBuildBranch.ps1` sau bước fetch và trước khi xác nhận version/build.

- Tên nhánh dùng ngày local của máy tại thời điểm này: `wip/yyyyMMdd-new`.
- Ví dụ: ngày 15/10/2026 dùng `wip/20261015-new`; ngày 16/10/2026 dùng `wip/20261016-new`.
- Chạy lại cùng ngày giữ nguyên tên nhánh. Build qua nửa đêm vẫn push vào nhánh đã chọn lúc bắt đầu.
- Đổi tên bằng `git branch -m`, giữ nguyên HEAD, staged/unstaged changes và file untracked.
- Nếu nhánh đích đã tồn tại ở local hoặc HEAD đang detached, script dừng; không force rename, checkout, reset hay xóa nhánh.
- Nếu remote của nhánh cũ có commit chưa được tích hợp, script dừng trước khi đổi tên. Sau đổi tên, kiểm tra đồng bộ remote của nhánh mới vẫn được thực hiện.
- Build và push vẫn cần các xác nhận có sẵn. Push thành công dùng `-u` để thiết lập upstream của nhánh mới.
- Không đổi tên/xóa nhánh cũ trên GitHub. Nhánh ngày mới được tạo trên remote khi người dùng xác nhận push thành công.

Đổi tên nhánh không thay đổi file EXE, database hoặc lịch sử sản xuất.
