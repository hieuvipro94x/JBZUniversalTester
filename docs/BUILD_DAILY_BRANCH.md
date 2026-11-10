# Nhánh build theo ngày

`BUILD_ONE_FILE.cmd` kiểm tra source mới bằng `Scripts/Sync-BuildSource.ps1`, sau đó gọi `Scripts/Set-DailyBuildBranch.ps1` trước khi xác nhận version/build.

- Tự fetch repository đã xác minh và tìm phiên bản UniversalTester lớn nhất trên các nhánh `wip/yyyyMMdd-new`, `main`, `master`; nếu bằng version thì chọn commit có thời gian mới hơn. Bỏ qua nhánh tool cấp key.
- Hiện version trên máy, version Git mới nhất, tên nhánh, commit và thời gian commit trước khi cập nhật file.
- Nếu working tree sạch và có thể fast-forward, tự lấy source mới rồi chạy lại file `BUILD_ONE_FILE.cmd` vừa cập nhật. Khối lệnh đồng bộ được CMD đọc trước khi thay file đang chạy để không tiếp tục ở sai vị trí trong script.
- Nếu source hiện tại đã dựa trên bản Git mới nhất, giữ nguyên các sửa đổi đang làm để tiếp tục build. Nếu còn thiếu code mới nhưng working tree có thay đổi, hoặc lịch sử tách nhánh, dừng và giữ nguyên công việc trên máy; không tự stash/reset/rebase.
- Sau build, kiểm tra lại source mới nhất trên các nhánh phát hành. Nếu có source mới chưa tích hợp, dừng commit/push để chạy lại bằng code mới.
- Kiểm thử bằng `powershell.exe -NoProfile -ExecutionPolicy Bypass -File Scripts/Test-SyncBuildSource.ps1`; toàn bộ repo dùng cho kiểm thử nằm trong thư mục tạm, không truy cập GitHub hoặc thiết bị test.

- Tên nhánh dùng ngày local của máy tại thời điểm này: `wip/yyyyMMdd-new`.
- Ví dụ: ngày 15/10/2026 dùng `wip/20261015-new`; ngày 16/10/2026 dùng `wip/20261016-new`.
- Chạy lại cùng ngày giữ nguyên tên nhánh. Build qua nửa đêm vẫn push vào nhánh đã chọn lúc bắt đầu.
- Đổi tên bằng `git branch -m`, giữ nguyên HEAD, staged/unstaged changes và file untracked.
- Nếu nhánh đích đã tồn tại ở local và trỏ đúng commit HEAD, chuyển sang nhánh đó, giữ nguyên thay đổi staged/unstaged và file untracked. Nếu khác commit, đang được worktree khác sử dụng hoặc HEAD detached, script dừng; không force/reset/xóa nhánh.
- Nếu remote của nhánh cũ có commit chưa được tích hợp, script dừng trước khi đổi tên. Sau đổi tên, kiểm tra đồng bộ remote của nhánh mới vẫn được thực hiện.
- Build và push vẫn cần các xác nhận có sẵn. Push thành công dùng `-u` để thiết lập upstream của nhánh mới.
- Không đổi tên/xóa nhánh cũ trên GitHub. Nhánh ngày mới được tạo trên remote khi người dùng xác nhận push thành công.

Đổi tên nhánh không thay đổi file EXE, database hoặc lịch sử sản xuất.
