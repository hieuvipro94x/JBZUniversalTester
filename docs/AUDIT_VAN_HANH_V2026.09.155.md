# Rà soát vận hành JBZUniversalTester V2026.09.155

Ngày rà soát: **03/10/2026**. Phạm vi: phát hiện lỗi dây, điều kiện PASS, điện trở, Leak/retest, Master, JIG/relay, vòng đời thiết bị và lưu kết quả.

Đây là báo cáo audit. Chưa sửa logic ứng dụng hoặc cấu hình sản xuất. Kết luận dựa trên source hiện tại và các mô phỏng riêng gọi assembly Release; kết quả mô phỏng không thay thế kiểm tra bằng mẫu thật.

## 1. Kết luận và thứ tự ưu tiên

| Mức | Điểm cần xử lý | Bằng chứng | Tác động vận hành |
|---|---|---|---|
| P1 | Leak chốt kết quả khi trường số cuối chưa truyền hết | Source và mô phỏng evaluator | Có thể trả PASS thay vì FAIL |
| P1 | Leak chấp nhận dòng RESULT có ô trống hoặc dữ liệu dư | Mô phỏng xác nhận | Có thể lệch kênh và PASS với dữ liệu không hợp lệ |
| P2 | Retest Leak kiểm tra phiên chạy quá muộn | Thứ tự xử lý trong source | Có thể cập nhật UI/history của phiên khác khi đổi model hoặc hủy đúng lúc |
| P2 | Điện trở cho phép bật R nhưng chọn kênh “Không dùng” | Source và mô phỏng | Bước đo bị bỏ qua dù checkbox vẫn bật |
| P2 | Cấu hình Leak thiếu kênh hoặc giới hạn không hữu hạn | Source và mô phỏng | CFG không hợp lệ có thể bỏ qua Leak; Infinity có thể làm mất giới hạn rò |
| P2, cần chốt quy trình | Dây đứt trong Production chưa có bước chốt NG | Source và mô phỏng | Chờ thông mạch kéo dài; lỗi hở không được tự chốt như sai dây/chập |
| P3 | Tắt log hệ thống cũng tắt file log lỗi | Source | Khó đối chiếu sự cố và phục vụ audit |
| P3 | Các hàng đợi không giới hạn dung lượng | Source; chưa stress test dài ca | Có nguy cơ tăng độ trễ/bộ nhớ khi ghi log, lưu DB hoặc xử lý frame chậm |

P1 cần ưu tiên trước các thay đổi giao diện và tối ưu tốc độ. P2 cần xử lý hoặc thống nhất quy trình trước khi coi kết quả kiểm tra trên máy là đã được xác minh đầy đủ.

## 2. Các phát hiện cụ thể

### 2.1. Leak nhận RESULT trước khi trường cuối hoàn tất — P1

Vị trí: `Services/WaterProofSerialService.cs:282`, nhánh xử lý buffer chưa có CR/LF tại dòng 284–291.

Service đọc từng đoạn bằng `ReadExisting()`. Chỉ cần buffer bắt đầu bằng `:RESULT` và có đủ dấu phẩy là gọi `ProcessLine` rồi trả kết quả. Đủ dấu phẩy không chứng minh chữ số cuối đã đến hết.

Mô phỏng CH3 bật, áp tối thiểu 35 và giới hạn rò 20:

```text
Đoạn nhận trước: :RESULT,84,80,84,80,40,40    -> CH3 PASS
Byte nhận sau:   0
Dòng đầy đủ:     :RESULT,84,80,84,80,40,400   -> CH3 FAIL, rò 360
```

Evaluator đã xác nhận hai kết quả khác nhau. Điều kiện buffer hiện tại cho phép chốt ngay ở đoạn đầu. Chưa có trace máy Leak thực tế chứng minh tình huống này đã xảy ra trong sản xuất.

**Cách sửa đề xuất:** chỉ chốt khi xác định được ranh giới bản tin theo giao thức đã xác minh. Dùng CR/LF nếu máy thực tế gửi CR/LF. Với máy không gửi ký tự kết thúc, cần trace hoặc tài liệu giao thức xác định cách kết thúc bản tin; không tự đặt khoảng chờ hoặc dựa vào số dấu phẩy.

### 2.2. Parser Leak bỏ ô trống và bỏ dữ liệu dư — P1

Vị trí: `Services/WaterProofSerialService.cs:776`, đặc biệt `RemoveEmptyEntries`, kiểm tra `< 6` và vòng lặp chỉ đọc sáu phần tử đầu.

Hai đầu vào này đều được evaluator hiện tại trả PASS với các kênh bật và giới hạn mặc định:

```text
:RESULT,40,,40,40,40,40,40
:RESULT,40,40,40,40,40,40,400
```

Dòng thứ nhất mất vị trí trường trống, dẫn đến dịch dữ liệu kênh. Dòng thứ hai bỏ qua giá trị thứ bảy. Trong khi đó chính parser yêu cầu RESULT gồm sáu giá trị.

**Cách sửa đề xuất:** kiểm tra đúng header, đúng sáu trường theo giao thức hiện hành, không bỏ trường trống, không chấp nhận dữ liệu dư và chỉ nhận số hữu hạn. Dữ liệu sai phải báo lỗi giao tiếp/thiết bị, không tạo PASS sản phẩm. Giữ nguyên công thức và đơn vị đo cho đến khi có bằng chứng từ thiết bị.

### 2.3. Retest Leak cập nhật kết quả trước khi kiểm tra phiên — P2

Vị trí:

- `ViewModels/TestViewModel.cs:9613`: chạy Leak với callback `ApplyWaterProofProgress` trực tiếp.
- `ViewModels/TestViewModel.cs:9619`: áp dụng kết quả.
- `ViewModels/TestViewModel.cs:9620`: lưu history retest.
- `ViewModels/TestViewModel.cs:9632`: sau đó mới kiểm tra cancellation/generation/model.
- `ViewModels/TestViewModel.cs:9210`: helper áp kết quả sửa các trường của cycle và dispatch UI mà không nhận context để kiểm tra.
- `ViewModels/TestViewModel.cs:9740`: history lấy parent từ `_activeCycleId` hiện tại, không phải CycleId được chụp khi bắt đầu retest.

Nếu phiên thay đổi trong khoảng Leak vừa kết thúc đến khi UI/history xử lý, kết quả cũ có thể được áp vào trạng thái mới. Có guard run ID ở service COM và guard restart scan ở `finally`; các guard này không thay thế kiểm tra model/cycle tại thời điểm áp kết quả.

**Cách sửa đề xuất:** chụp context bất biến khi bắt đầu gồm model, CycleId, profile, generation và token; kiểm tra sau mỗi await và bên trong callback UI. History dùng đúng context của lần chạy cũ. Kết quả hợp lệ đã hoàn tất có thể vẫn được lưu dưới cycle gốc, nhưng không được ghép với cycle mới.

Đây là rủi ro xác định từ source, chưa tái hiện bằng thao tác đổi model trên máy thật.

### 2.4. Điện trở bật nhưng chọn kênh “Không dùng” — P2

Vị trí:

- `ViewModels/ProductionSettingsViewModel.cs:76`: kênh 0 hiển thị “Không dùng”.
- `ViewModels/ProductionSettingsViewModel.cs:730`: `Enabled` và `ChannelSelection` độc lập.
- `Views/ProductionSettingsPage.xaml.cs:1677`: cho phép kênh 0 ngay cả khi checkbox bật.
- `Services/ResistanceMeasurementPlan.cs:102`: lọc bỏ mọi slot có kênh 0.
- `Services/TestEngine.cs:3727`: nếu số bước thực tế bằng 0, không yêu cầu kết quả điện trở để PASS.

Mô phỏng R1 `Enabled=true`, `Channel=0`: editor giữ checkbox bật, kế hoạch đo có 0 bước, continuity đạt thì `CanCompletePass` trả true với danh sách kết quả điện trở rỗng. Đây là cấu hình mâu thuẫn, không phải lỗi đo ở các kênh CH1–CH10 hợp lệ.

**Cách sửa đề xuất:** khi chọn “Không dùng” phải thể hiện rõ bước đã tắt, hoặc chặn lưu nếu R bật mà chưa chọn CH1–CH10. Kiểm tra lại kế hoạch đo khi bắt đầu sản xuất, hiển thị danh sách phép đo thực tế cho đúng mã hàng. CFG lỗi cần báo rõ, không âm thầm đổi kênh hoặc giới hạn rồi tiếp tục kiểm tra.

### 2.5. Leak cần validation ở runtime và giới hạn hữu hạn — P2

Vị trí:

- `ViewModels/TestViewModel.cs:8998`: `Enabled=true` nhưng không có kênh bật bị xem là không bật Leak.
- `ViewModels/TestViewModel.cs:10010`: nếu không bật thì bỏ qua bước Leak.
- `Views/ProductionSettingsPage.xaml.cs:1625`: UI đã chặn trường hợp không chọn kênh khi lưu.
- `Views/ProductionSettingsPage.xaml.cs:1663`: giới hạn áp/rò chỉ kiểm tra số âm.
- `Services/ProductionConfigService.cs:934`: normalization chưa kiểm tra số hữu hạn hoặc số kênh khi Leak bật.

Mô phỏng xác nhận hai tình huống:

1. Profile được nạp có `Enabled=true` và cả ba CH tắt bị xem là không bật Leak. Rủi ro nằm ở CFG cũ/sửa ngoài ứng dụng; UI lưu hiện tại đã có guard.
2. `LeakLimit=Infinity` cho phép rò 360 vẫn PASS nếu áp đạt. Kiểm tra `< 0` và `Math.Max(0, value)` không loại Infinity. Chưa thử nhập Infinity qua giao diện WPF; kết luận đã xác nhận ở evaluator và validation source.

**Cách sửa đề xuất:** dùng chung validation khi lưu, khi nạp và khi bắt đầu cycle. Cấu hình yêu cầu Leak nhưng không thể tạo kế hoạch hợp lệ phải khóa kiểm tra và báo lỗi cấu hình; không được tự xem là tắt. Áp/rò phải là số hữu hạn và hợp lệ theo thông số máy/mã hàng đã được xác nhận.

### 2.6. Production thiếu quy trình chốt dây đứt — P2, cần thống nhất vận hành

Vị trí: `Services/ProductionFaultConfirmationGate.cs:139` luôn xóa danh sách OPEN; `ViewModels/TestViewModel.cs:7447` ghi rõ missing/open không phải lỗi sản phẩm trong flow Production hiện tại.

Mô phỏng 2/3 kết nối đúng và giữ một kết nối hở sau 60 giây vẫn không có OPEN được xác nhận. Master tuột tuýt/đứt dây là flow riêng, đã kiểm tra số kết nối thiếu theo cài đặt.

Điều này không cho sản phẩm thiếu thông mạch đạt PASS, nhưng sản phẩm đứt dây có thể ở trạng thái chờ kiểm tra mà không tự chốt NG như sai dây/chập. Thống kê và history lỗi hở vì vậy chưa tương đương các lỗi khác.

**Cách tối ưu đề xuất:** xác định bằng chứng “đã lắp xong” trước khi chốt OPEN: tín hiệu connector/JIG đã có và được xác minh, hoặc quy trình thao tác được phê duyệt. Không tự thêm timeout để kết luận dây đứt trong lúc công nhân còn lắp. Chỉ dữ liệu thông mạch cũng không phân biệt được connector chưa lắp với một dây đứt tạo cùng trạng thái điện.

### 2.7. Log lỗi phụ thuộc công tắc log hệ thống — P3

Vị trí: `Services/AsyncFileLogService.cs:110`. Khi `FileLoggingEnabled=false`, cả `Error()` cũng không ghi vào file runtime. History và crash report có cơ chế riêng nên không đồng nghĩa mất toàn bộ lịch sử.

**Cách tối ưu đề xuất:** luôn giữ log lỗi thiết bị, lỗi lưu kết quả và các chuyển trạng thái quan trọng; công tắc chỉ điều khiển trace chi tiết. Giới hạn/luân phiên file để không làm đầy ổ đĩa. Log cần gắn CycleId, model, generation, phase, nguyên nhân chốt PASS/FAIL và mốc relay OFF.

### 2.8. Theo dõi tải hàng đợi trong ca dài — P3

Vị trí: `Services/AsyncFileLogService.cs:36`, `Services/ProductionPersistenceService.cs:34` dùng `CreateUnbounded`; `ViewModels/TestViewModel.cs:210` dùng `ConcurrentQueue` cho frame.

Ứng dụng đã có các chỉ số hiệu năng, worker tuần tự và coalescing UI. Chưa có bằng chứng thực tế về tăng bộ nhớ hoặc backlog kéo dài; đây là điểm cần stress test trước khi tối ưu.

**Cách tối ưu đề xuất:** đo backlog, tuổi frame, thời gian commit và bộ nhớ trong ca dài; coalesce/throttle UI hoặc trace trước. Không tùy tiện bỏ frame quyết định điện hoặc history. Nếu DB không lưu kịp, giữ cơ chế khóa kết quả/relay thay vì bỏ kết quả để chạy nhanh hơn.

## 3. Những cơ chế đã có, cần giữ

- Engine không xử lý frame TestPin như Production; preview/probe không phải nguồn quyết định PASS/FAIL.
- Sai dây/chập có debounce riêng, không đếm lặp cùng cạnh thành nhiều điểm lỗi Master.
- Luồng lỗi giữ việc nhận và cập nhật kết nối trong các pha được phép; xác nhận tháo toàn bộ trước khi mở cycle mới.
- PASS được commit SQLite trước khi phát tín hiệu PASS và chạy chuỗi relay (`TestViewModel.cs:10401`). Commit dùng transaction và chống trùng CycleId (`TestHistoryStore.cs:925`, `:951`).
- Luồng FAIL dùng relay được cấu hình cho JIG, không dùng chuỗi MARKING (`TestEngine.cs:3158`). Cần đối chiếu kiểu đấu với JIG thực tế.
- Master V155 có guard trong thời gian lưu/mở JIG; chỉ mở sản xuất sau relay OFF và xác nhận tháo. Mẫu đạt chạy điện trở/Leak nếu bật; các mẫu NG kiểm tra điện theo lựa chọn.
- Sau PASS có `MarkPassRemovalScanReady` (`TestViewModel.cs:9383`, gọi tại `:10502`): không bắt buộc sản phẩm đã mở JIG phải xuất hiện lại đủ topology mới nhận tháo.
- D2XX có reader và guard scan generation; Leak/printer có owner COM riêng. Các cơ chế này cần tiếp tục kiểm tra khi mất USB/COM, đổi mã và đóng ứng dụng.

## 4. Các bài thử thực tế cần ưu tiên

| Bài | Kết quả cần xác nhận |
|---|---|
| Leak với RX chia thành nhiều đoạn, kể cả chữ số cuối bị tách | Không chốt trước bản tin đầy đủ; sau khi đầy đủ mới so giới hạn |
| RESULT thiếu/dư trường, ô trống, giá trị không hữu hạn | Báo lỗi thiết bị/giao tiếp; không PASS |
| Leak FAIL rồi tháo/lắp từng connector và retest | Pin hiển thị lại đúng; kết quả/history giữ đúng cycle; không cộng LOT/sản lượng của retest |
| R1 bật nhưng chọn “Không dùng”; CFG Leak bật nhưng tắt hết CH | Kế hoạch hiển thị đúng hoặc khóa vì cấu hình mâu thuẫn |
| Điện trở tại Min/Max, dưới/trên giới hạn, OPEN, mẫu không ổn định, mất VISA | Phân loại đúng; không PASS với dữ liệu thiếu; route và output được trả về trạng thái ban đầu |
| Thay đổi một dây ngoài kênh đo trong lúc đo R | Đối chiếu cơ chế giữ sản phẩm/JIG và snapshot continuity; xác định có cần kiểm tra lại topology trước PASS |
| Sai dây/chập xuất hiện, thêm/bớt lỗi khi vẫn còn sản phẩm | Lỗi và âm thanh cập nhật; chưa tháo hết thì không mở cycle mới |
| Mỗi loại Master; chỉ chọn một NG; đổi mã; qua đầu ca 07:00 | Đủ mẫu đã chọn, đúng history, JIG về OFF và tháo hết mới mở sản xuất |
| Mất bo/Leak COM, hủy hoặc đổi model sát lúc trả kết quả | Không callback cũ sửa phiên mới; một owner/reader; không relay ngoài cycle |
| Chạy hết ca với log bật và ổ đĩa chậm | Không tăng backlog/bộ nhớ kéo dài; thời gian đáp ứng và commit có số đo |

Continuity ở cuối bước điện trở hiện dùng snapshot đã xác nhận vì D2XX phải dừng scan để đo (`TestViewModel.cs:10381`). Chưa xác nhận đây là lỗi: cần thử với cơ chế giữ sản phẩm/JIG thật trước khi thay đổi thứ tự START/STOP hoặc thêm phép kiểm tra.

## 5. Kiểm chứng đã thực hiện

- `dotnet build -c Release --nologo`: **PASS, 0 warning, 0 error**, vẫn `net8.0-windows / win-x86`.
- Harness tạm trong `%TEMP%` tham chiếu assembly Release hiện tại: **8 quan sát được xác nhận** gồm RESULT bị tách, RESULT ô trống, RESULT dữ liệu dư, Infinity, editor R bật/kênh 0, PASS không có kết quả R khi kế hoạch rỗng, Leak bật/không CH và Production OPEN sau 60 giây.
- Harness dùng board giả; không mở COM, không kích relay, không ghi database hoặc CFG sản xuất.
- Không chạy self-test suite và không sửa `Tests/Program.cs`, theo yêu cầu trước của người dùng.
- Không thay đổi schema/history. Không chạy `PRAGMA integrity_check` trên DB sản xuất trong lần audit này vì không thực hiện migration hay ghi DB.
- Chưa chạy bộ mẫu thực tế điện trở/Leak trong lần audit này. Phép thử D2XX ở phiên trước đã nhận 19 frame hoàn chỉnh và gửi pulse R1 ON/OFF; chưa đủ để xác nhận mọi mẫu hoặc chuyển động cơ khí JIG.

## 6. Kế hoạch sửa đề xuất

1. Sửa validation parser RESULT và xác minh framing Leak bằng trace máy thực tế; kiểm tra các đầu vào trên không còn PASS sai.
2. Chặn callback retest cũ trước khi sửa UI; lưu dưới context cycle/model/profile gốc.
3. Thống nhất validation kế hoạch R/Leak ở UI, lúc load và đầu cycle; thể hiện rõ các phép kiểm tra thực sự sẽ chạy.
4. Thống nhất tín hiệu/quy trình đã lắp xong để xử lý OPEN Production, giữ việc lắp dở không bị chốt NG.
5. Giữ log lỗi quan trọng, đo tải ca dài và chỉ tối ưu các nút nghẽn có số liệu.

Mỗi bước sửa source cần tăng version đồng bộ, giữ kiến trúc x86 và kiểm tra regression tương ứng. Không đổi giao thức, ánh xạ chân, công thức hoặc timing phần cứng khi chưa có bằng chứng từ source/trace thiết bị.
