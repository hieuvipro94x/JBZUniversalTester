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

# Git / GitHub Workflow

## At Task Start

Trước mỗi coding task:

1. Đọc `AGENTS.md`.
2. Chạy `git status`.
3. Xác định current branch.
4. Chạy `git fetch origin`.
5. Kiểm tra local branch so với remote:

   * synchronized;
   * ahead;
   * behind;
   * diverged.

Nếu working tree sạch và remote có commit mới:

* đồng bộ an toàn bằng `git pull --rebase`.

Không pull/rebase mù quáng nếu working tree có local modifications.

Không tự stash, reset hoặc discard thay đổi của user.

## During Task

* Chỉ sửa file thuộc phạm vi task.
* Không trộn unrelated changes.
* Tuân theo Bug Fix / Feature Workflow trong `AGENTS.md`.

## Before Commit

Sau khi sửa:

1. Build/test phù hợp.
2. Chạy `git diff`.
3. Kiểm tra diff chỉ chứa task hiện tại.
4. Kiểm tra secret/credential.
5. Không stage build output/temp file.
6. Chỉ stage file thuộc task.

## Auto Commit

Nếu task coding đã hoàn thành và verification phù hợp PASS:

Codex được phép tự tạo commit mà không cần hỏi lại.

Dùng Conventional Commits:

* `fix: ...`
* `feat: ...`
* `docs: ...`
* `refactor: ...`
* `test: ...`
* `chore: ...`

Không dùng `git add .` mù quáng khi working tree chứa unrelated changes.

## Before Push

Ngay trước push:

1. `git fetch origin`
2. Kiểm tra remote branch có commit mới hay không.

Nếu remote thay đổi:

* integrate an toàn;
* không force push;
* nếu resolve conflict thì build/test lại.

## Auto Push

Nếu:

* commit thành công;
* verification phù hợp PASS;
* remote an toàn;
* không có conflict;
* không có secret;

Codex được phép tự `git push` branch hiện tại lên `origin` mà không cần hỏi lại.

Nếu branch chưa có upstream:

`git push -u origin <branch>`

## Branch Policy

Thay đổi nhỏ, an toàn:

* có thể làm trên branch hiện tại nếu phù hợp.

Thay đổi lớn liên quan:

* architecture;
* D2XX/UART protocol;
* hardware transport;
* PASS/FAIL lifecycle;
* firmware interaction;
* database/schema;
* refactor nhiều module;

ưu tiên branch:

* `feature/<name>`
* `fix/<name>`
* `refactor/<name>`

Không tự merge branch lớn vào `main` nếu task không yêu cầu.

## Forbidden Git Actions

Codex tuyệt đối không tự động:

* `git push --force`
* `git push -f`
* `git push --force-with-lease`
* `git reset --hard`
* `git clean -fd`
* rewrite shared history
* delete repository
* delete remote branch
* delete tag
* delete release
* đổi PRIVATE thành PUBLIC
* discard unrelated user changes
* commit secrets.

## Exceptions

Không auto commit/push nếu:

* tôi nói không commit;
* tôi nói không push;
* task chỉ review/phân tích;
* verification fail;
* conflict chưa giải quyết;
* có unrelated changes chưa rõ;
* phát hiện secret;
* remote/repository không đúng như dự kiến.

## Task Completion

Coding task bình thường:

`status`
→ `fetch/sync`
→ `edit`
→ `build/test`
→ `diff review`
→ `commit`
→ `fetch`
→ `push`
→ `status`

Cuối task phải báo:

* branch;
* files changed;
* verification;
* commit hash ngắn;
* commit message;
* push status;
* local/remote synchronized hay không.

## Task Instruction Files

Detailed task specifications may be stored under:

`docs/tasks/`

When the user references a task file:

1. Read AGENTS.md first.
2. Read only the referenced task file.
3. Do not scan other task files.
4. Read only source files required by that task.
5. Do not repeat the entire task specification in responses.
6. Keep progress/status responses concise.
7. At completion, report only:
   - root cause;
   - files changed;
   - verification;
   - commit;
   - push status.

## Token / Context Efficiency

Keep context usage efficient.

- Do not repeatedly summarize AGENTS.md.
- Do not repeat the user's full requirements.
- Do not dump large source files unless necessary.
- Do not print large diffs unless requested.
- Do not print full build logs when build succeeds.
- On build failure, show only relevant error sections.
- Do not rescan the whole repository for normal tasks.
- Reuse already established project knowledge.
- Read only files relevant to the current task.
- Keep progress messages concise.
- Final reports should be concise and actionable.
# Multi-PC Git Workflow

GitHub is the synchronization source between development PCs.

## Before starting any coding task

Always:

1. Run `git status`.
2. Run `git fetch origin`.
3. Check whether the current branch is:
   - synchronized;
   - ahead;
   - behind;
   - diverged.

If the working tree is clean and remote has newer commits:

`git pull --rebase`

Do this BEFORE editing source code.

Never start editing from an outdated local branch when a newer remote version exists.

## After completing a coding task

Before commit:

1. Build/test the relevant changes.
2. Review `git diff`.
3. Ensure only task-related files are included.
4. Check for secrets, build output and temporary files.

If verification passes:

1. Stage only relevant files.
2. Create a Conventional Commit.
3. Run `git fetch origin` again.
4. Check whether remote changed while the task was being performed.

If remote did not change:

`git push`

If remote changed:
- integrate the remote changes safely;
- prefer rebase when appropriate;
- resolve conflicts carefully;
- build/test again;
- then push.

## End-of-work rule

Before finishing work on a PC for the day:

- all completed work must be committed;
- completed and verified commits must be pushed to GitHub;
- run `git status`;
- confirm local branch and remote branch are synchronized.

Do not leave completed work only on one PC.

## Moving to another PC

At the beginning of work on another PC:

1. `git status`
2. `git fetch origin`
3. `git pull --rebase`

Only start editing after synchronization succeeds.

## Unfinished work

Do NOT automatically commit incomplete or broken code just because the work session is ending.

If work is incomplete:
- do not push broken changes to `main`;
- preferably create/use a work branch such as:
  `work/<task-name>`
  or
  `feature/<task-name>`;
- commit incomplete work only when necessary to transfer it between PCs;
- clearly mark the commit as WIP.

Example:

`git commit -m "wip: continue UART reconnect investigation"`

Push the WIP branch, not stable `main`.

On the other PC:
- fetch;
- checkout the same branch;
- pull;
- continue the task.

When completed and verified:
- replace future commits with proper task commits as appropriate;
- merge through the normal repository workflow.

## Safety

Never automatically:
- force push;
- reset --hard;
- clean -fd;
- discard user changes;
- overwrite remote changes;
- resolve conflicts by blindly choosing ours/theirs.

If the local and remote branches diverge unexpectedly, stop automatic synchronization and report the situation before destructive actions.



