Bây giờ hãy tạo `AGENTS.md` tại root project.



QUAN TRỌNG:



\* Không scan lại toàn bộ repository.

\* Không thực hiện lại review.

\* Sử dụng context đã có trong session hiện tại.

\* Sử dụng `docs/BAO\_CAO\_TONG\_HOP\_CODEX\_REVIEW\_V15\_0\_0.md` làm nguồn kỹ thuật chính.

\* Chỉ đọc lại một source file cụ thể nếu thật sự cần xác minh một chi tiết chưa chắc chắn.



Mục tiêu:

`AGENTS.md` phải là project operating manual ngắn gọn để các Codex session sau có thể hiểu project nhanh mà không cần đọc lại toàn repo.



AGENTS.md cần có:



\# Project Overview



\* mục đích project;

\* framework/công nghệ;

\* kiến trúc tổng quát;

\* D2XX backend;

\* UART TTL backend.



\# Architecture Boundaries



\* D2XX và UART TTL là hai backend/protocol riêng;

\* không được trộn semantics giữa hai backend;

\* ghi rõ source-of-truth của `.tht`, `.model`, `.setup` và Product Bundle.



\# Important Modules



\* các module/file quan trọng đã được xác định trong review;

\* mô tả ngắn vai trò từng phần.



\# Golden Rules / Invariants



\* protocol rules;

\* model/setup rules;

\* pin mapping;

\* TESTPIN;

\* PASS/FAIL;

\* ProductRemoved;

\* JIG;

\* hardware lifecycle;

\* những giá trị Codex tuyệt đối không được tự suy đoán.



\# Historical Regressions



Tóm tắt các regression quan trọng đã phát hiện và phải tránh tái phạm.



\# Coding Safety Rules



\* minimal change;

\* root cause trước;

\* không refactor ngoài task;

\* không tự đổi protocol/API/data format;

\* không nâng dependency nếu không được yêu cầu;

\* không che lỗi bằng empty catch;

\* không sửa generated/build output;

\* không tự xóa source.



\# Hardware Safety Rules



\* một owner/reader phù hợp cho FTDI/COM;

\* tránh multiple readers;

\* lifecycle/dispose/reconnect;

\* stale callback;

\* không đoán firmware behavior.



\# Bug Fix Workflow



1\. Đọc AGENTS.md.

2\. Hiểu task.

3\. Xác định file liên quan.

4\. Chỉ đọc code cần thiết.

5\. Tìm root cause.

6\. Minimal fix.

7\. Build/test.

8\. Kiểm tra regression.

9\. Review diff.

10\. Báo cáo rõ kết quả.



\# Feature Workflow



\* xác định integration point;

\* giữ architecture boundary;

\* không phá backend còn lại;

\* build/test;

\* cập nhật documentation nếu invariant thay đổi.



\# Build / Verification



Ghi các command đã xác minh, bao gồm nếu đúng:



\* `dotnet clean`

\* `dotnet restore`

\* `dotnet build -c Release`

\* `VERIFY\_BUILD\_V15\_0\_0.cmd`



\# Definition of Done



Không được coi task hoàn thành chỉ vì compile PASS.

Phải có verification phù hợp và nói rõ phần chưa test được bằng hardware thật.



\# Detailed Technical Reference



Tham chiếu:

`docs/BAO\_CAO\_TONG\_HOP\_CODEX\_REVIEW\_V15\_0\_0.md`



Ghi rõ:



\* task thông thường chỉ đọc AGENTS.md + source liên quan;

\* chỉ mở báo cáo kỹ thuật khi cần investigation sâu;

\* source code hiện tại là source of truth cuối cùng;

\* nếu documentation và source mâu thuẫn phải điều tra, không tự đoán.



Giữ AGENTS.md ngắn gọn, không copy nguyên báo cáo review.



Sau khi tạo xong:



\* hiển thị nội dung tóm tắt AGENTS.md;

\* cho biết file nằm ở đâu;

\* không review lại project.



