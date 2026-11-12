# Rà soát hiệu năng, History và bắt lỗi — 11/10/2026

## Kết quả

Có ảnh hưởng thực tế tới hiệu năng History và độ chính xác/bảo toàn lịch sử. Hai vấn đề P1 đã được tái hiện bằng code hiện tại trên database tạm. Không có bằng chứng từ các kiểm tra đã chạy rằng logic WRONG/SHORT bị bỏ lọt; chưa kiểm chứng bằng phần cứng thật. Build đạt nhưng self-tests chưa xanh: **74/78 PASS**.

Phạm vi: nhánh `wip/leak-window-20260920`, HEAD `d29a418` (V2026.09.224), so với `origin/main`/merge-base `ba63a1c`. Kiểm kê diff toàn repository: 267 commit, 566 file thay đổi; 121 file C#/XAML/csproj. Số này bao gồm tài liệu, binary, trace và phần Python cũ bị bỏ; không đồng nghĩa 566 file chạy trong ứng dụng. Rà soát tập trung các đường thực thi .NET: parser/mapping/decoder, engine và confirmation gate, D2XX lifecycle, orchestration/UI, History/SQLite/migration/export/persistence, Leak và printer COM. Đây là rà soát source và kiểm thử phần mềm; không phải xác nhận mọi tình huống UI/phần cứng hay benchmark trước/sau toàn ứng dụng.

## Các phát hiện cần xử lý

### P1 — Kế thừa database qua phiên bản có thể mất những dòng đã commit trong WAL

Vị trí: `Services/ProductionDataUpgradeService.cs:88,125–135`; `Services/TestHistoryStore.cs:84`.

Repository sử dụng WAL, nhưng `CopyWithoutOverwrite` chỉ `File.Copy` file `Data/JBZUniversalTester.db`, rồi đổi tên file tạm. Không lấy snapshot SQLite và không bảo toàn các transaction còn ở file `-wal`. Khi bản cũ còn mở, hoặc WAL chưa checkpoint sau kết thúc bất thường, bản mới có thể nhận một database hợp lệ nhưng thiếu lịch sử gần nhất. Target đã tồn tại sau đó bị bỏ qua, nên lần mở tiếp theo không tự lấy lại những dòng thiếu.

Đã tái hiện bằng .NET/Microsoft.Data.Sqlite và gọi trực tiếp `MigrateMissingProductionData`: tạo database tạm có bảng Tests, bật WAL và tắt auto-checkpoint, commit một dòng rồi giữ connection nguồn mở; gọi kế thừa sang thư mục phiên bản mới.

```text
WAL source committed rows: 1
WAL present: True
Files migrated: Data\JBZUniversalTester.db
Upgrade destination rows: 0
Upgrade destination integrity: ok
```

Đây là giảm dữ liệu trong bản được kế thừa; không xóa dòng khỏi database nguồn. `integrity_check` một mình không phát hiện việc thiếu transaction. Cần dùng SQLite Backup API cho bản snapshot nhất quán, kiểm tra integrity và số dòng các bảng quan trọng trước/sau; không sửa bằng cách xóa/reset database sản xuất. Kịch bản này chưa nằm trong self-test kế thừa dữ liệu hiện tại.

### P1 — History cũ thay đổi thông tin và hash model sau commit mới

Vị trí: `Services/TestHistoryStore.cs:977–980,1059–1084,1097–1140,2079–2094`.

History đọc tên sản phẩm/xe từ Parts và thông tin model từ Models bằng JOIN. `UpsertPart` cập nhật các trường dùng chung; `UpsertModel` còn tìm lại model bằng cùng FilePath dù hash khác, rồi ghi đè FileHash/ModelName. Các Test cũ vẫn giữ PartId/ModelId nhưng thông tin trả về đã thành thông tin mới. Điều này ảnh hưởng cả UI, tìm kiếm và export, và mất khả năng truy vết đúng revision `.tht` dùng lúc kiểm tra.

Đã tái hiện bằng `ProductionResultCommitRequest.Capture` và `TestHistoryStore.CommitResult`:

```text
Old row before: NAME-V1 | MODEL-V1
Old row after NEW commit: NAME-V2 | MODEL-V2 | VEHICLE-V2
Old model hash after NEW commit: BBBB...BBBB (trước đó AAAA...AAAA)
Rows: 2
Integrity: ok
Orphans: none
NEW row after duplicate OLD replay: NAME-V1 | MODEL-V1
Rows after duplicate replay: 2
```

Replay CycleId không tạo thêm Test, nhưng Upsert chạy trước FindTestId nên snapshot cũ có thể ghi đè metadata chung của bản mới. Vì vậy exactly-once về số dòng chưa đảm bảo bất biến nội dung lịch sử. Cần lưu snapshot metadata theo Test/revision và giữ revision model riêng khi hash thay đổi; xem xét kiểm tra duplicate trước mutation metadata. Mọi migration phải additive, giữ nguyên Test và child rows; cần thiết kế cách diễn giải legacy trước khi sửa.

### P2 — Mỗi trang History tính số thứ tự trên toàn bộ Tests sản phẩm

Vị trí: `Services/TestHistoryStore.cs:2095–2101`; được dùng bởi SearchSummary và export.

Derived table `ROW_NUMBER() OVER (PARTITION BY PartId,ProductionBatchKey ...)` không giới hạn theo trang hay ngày. Query plan materialize toàn bộ ordinal trước JOIN vào trang 200 dòng. Keyset và index thời gian đúng không loại bỏ phần công việc này.

Đo chẩn đoán: Python SQLite 3.50.4, database trong RAM, schema/index và câu SELECT lấy từ source hiện tại; dữ liệu tổng hợp một part, nhiều batch, lọc 200 dòng cuối. Median ba lần, đơn vị ms:

| Tổng Test | SELECT hiện tại | Cùng SELECT bỏ JOIN ordinal để đối chứng |
|---:|---:|---:|
| 100.000 | 104,16 | 1,29 |
| 1.000.000 | 1.024,47 | 1,35 |

Query plan có `MATERIALIZE ordinal` và `SCAN Tests USING INDEX IX_Tests_ProductBatch_HistoryAt_Id`. Đây là đo nguyên nhân SQL, không phải thời gian UI hay so sánh bản main với bản mới trên DB sản xuất. Bỏ ordinal chỉ là đối chứng chẩn đoán, không phải đề xuất bỏ yêu cầu số thứ tự. Cần tính/đọc ordinal theo các batch liên quan hoặc lưu giá trị ổn định mà vẫn giữ semantics hiện tại. Benchmark trong `Tests/DatabaseBenchmark.cs` dùng truy vấn paging đơn giản theo ResultAt, không thực thi SELECT History này nên không chứng minh đường mới nhanh.

### P2 — Lỗi và lifecycle của tải thêm History chưa được xử lý đầy đủ

Vị trí: `Views/HistoryPage.xaml.cs:41–55,143–151,154–188`.

`LoadMore_Click` là async void, có finally nhưng không catch lỗi SQLite/đọc file. Exception đi tới Dispatcher handler chung, thay vì lỗi History có ngữ cảnh và trạng thái retry. Không khẳng định ứng dụng crash: App có global Dispatcher exception handler.

Các kết quả page thành công đã có generation gate, ngăn append vào bộ lọc mới. Tuy nhiên catch của Reload không kiểm tra generation: một request cũ thất bại có thể ghi đè counters/hiện lỗi sau khi request mới thành công. `HistoryPage_Loaded` cũng không kiểm tra lifecycle sau await trước khi gọi Reload, nên trang đã release vẫn có thể khởi động lại truy vấn. Cần giữ generation/disposed guard trên cả nhánh success, failure và khởi tạo. Đây là kết luận theo source; chưa chạy tương tác UI để tái hiện.

## Ảnh hưởng tới bắt lỗi và hiệu năng đường điện

- WRONG/SHORT hiện debounce 100 ms cho cùng một cạnh, không chờ thêm product-settle 200 ms; cạnh ngoài topology cũng được đánh giá. Đây là thay đổi có chủ đích ở confirmation gate so với main, giảm độ trễ phát hiện nhưng cần thử nhiễu thực tế.
- Engine từ chối frame Probe, incomplete, unknown-byte và sequence/generation cũ. Self-tests về decoder/probe isolation, inline probe giữ wiring faults, engine continuity/wrong/short, model evidence, 500-cycle stress, relay và removal đạt.
- OPEN sản phẩm không tự tạo FAIL trong source hiện tại: BuildConfirmedOpenFaults trả rỗng, missing continuity không được PASS. **Hành vi này cũng đã có trên origin/main**, không phải regression mới của nhánh. Master OPEN có đường riêng và test riêng đạt.
- Cache topology/so sánh frame giảm công việc trên frame lặp. Kết quả đo trong self-tests: 10.000 frame giống nhau khoảng 10 ms/1.200.972 byte cấp phát; 2.000 frame giống nhau với 500 IO/250 network khoảng 32 ms/240.028 byte. Stress decoder 10 card/500 frame: 42 ms/20.147.444 byte. Các số này là đo hiện tại, không chứng minh phần trăm cải thiện so với main hoặc thông lượng FTDI thực.
- Production commit được serialized ở background writer; PASS được commit trước relay. Những kiểm tra relay ordering, transaction crash recovery và duplicate CycleId đạt. COM Leak/printer có lifecycle riêng, không ghép với handle D2XX. Tests về Leak late-open cleanup, progress cũ và suspended D2XX reader đạt.
- Giới hạn coverage: self-tests console không tạo WPF Application/Dispatcher, nên TestViewModel chọn đường process frame inline. Đường hàng đợi production thật, dispatcher coalescing và latency khi UI/SQL bận chưa được kiểm chứng end-to-end. Queue frame hiện không giới hạn độ dài; cần đo queue depth/age khi chạy thật trước khi kết luận không trễ bắt lỗi.

## History đã được kiểm chứng và các giới hạn

Các self-tests History đạt: thứ tự test-start thay vì result-time; fallback TestStartedAt NULL; tie-breaker Id; nhiều trang không trùng/mất dòng; full filtered summary; 450 dòng đi qua nhiều trang; CSV/XLSX đủ dataset; legacy schema và initialization; transaction atomicity/exactly-once; child rows và orphan check; SQLite integrity trên DB test. Source export hiện streaming, DataGrid có virtualization. Migration schema hiện có backup SQLite, transaction và guard số dòng.

Những điểm tốt này không loại trừ hai vấn đề P1 ở trên: integrity/số dòng có thể giữ nguyên trong khi metadata bị thay đổi; và bản sao thiếu WAL vẫn có thể integrity `ok`.

Không mở, sửa, reset hoặc migrate database sản xuất trong đợt rà soát này. Không kết nối FTDI/Leak/printer thật. Chưa đo lịch sử trên quy mô/disk của máy sản xuất; chưa xác nhận durability khi mất điện.

## Build và self-tests

`dotnet build -c Release`: PASS, 0 warning, 0 error.

`dotnet run --project Tests -c Release`: exit 1, **74/78 PASS**. Bốn kiểm tra thất bại:

| Test | Điểm thất bại và đối chiếu source |
|---|---|
| Learned diagnostic topology normalization and persistence | Assert chuỗi tiêu đề `CHUẨN ĐOÁN IO` và `KẸT / CHƯA THÁO`; UI hiện đổi tiêu đề/casing nhưng vẫn có ActiveIoRows, stuck count và virtualization. |
| D2XX resistance selectors and ten-slot configuration | Assert có `MinHeight="290"`; UI hiện dùng ItemsControl result lines và không có giá trị này. Các assert logic phía trước đạt, đoạn test phía sau điểm throw chưa chạy. |
| Final TestView status/master/device fault guards | Mong `LẮP MẪU MASTER`, thực tế `KIỂM TRA MẪU ĐẠT`; phần còn lại của test sau assert chưa chạy. |
| Production fault debounce and jig contact state | Mong XAML `Text="{Binding Summary}"`; UI hiện tạo rich-text trong FaultSummary_Loaded. Source vẫn map toàn bộ orderedFaults vào ItemsSource; không có bằng chứng chỉ hiển thị lỗi đầu. Các assert phía sau chưa chạy. |

Bốn fail đều tại kiểm tra presentation/contract. Không tự kết luận chúng là bug điện hoặc test được phép bỏ: cần xác nhận yêu cầu UI hiện hành rồi sửa UI/test tương ứng, và chạy toàn bộ suite lại. Không thay đổi/giảm test để làm xanh trong cuộc rà soát này.

## Thứ tự xử lý đề xuất

1. Snapshot database WAL khi kế thừa phiên bản và thêm regression về dòng commit chưa checkpoint.
2. Giữ metadata/history revision bất biến, kể cả duplicate callback; regression đổi tên/xe/hash tại cùng đường dẫn.
3. Tối ưu ordinal query theo đúng semantics và benchmark trực tiếp SearchSummary/EnumerateForExport.
4. Hoàn thiện generation/error guard History; xác nhận và sửa bốn UI regression test.
5. Chạy end-to-end trên FTDI/Leak/printer thật, bao gồm SQL/export đồng thời, probe, WRONG/SHORT, tháo một phần/toàn bộ và reconnect.

Chỉ thêm báo cáo Markdown vào repository; không sửa source, không bump version và không commit/push.
