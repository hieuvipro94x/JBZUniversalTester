# JBZUniversalTester — CODEX HANDOFF / WIP CHECKPOINT
Ngày tạo: 2026-09-03
Repo: D:\Code\JBZUniversalTester-NEW\JBZUniversalTester

## Mục tiêu phiên đang làm
Codex đang triển khai tuần tự 8 nhóm:
1. Sửa fresh-frame bằng scan-session generation và stale-frame guard.
2. Khôi phục Start Card xuyên suốt capacity/mapping/config/UI.
3. Kết nối IO Confirm 1/n vào xác nhận frame phù hợp.
4. Bổ sung protocol diagnostics/guard; KHÔNG tự phát minh command khi chưa có board/trace.
5. Hỗ trợ chọn Part khi THT có nhiều Part.
6. Bổ sung barcode COM và kiểm tra Part/trùng sản phẩm.
7. Lưu test đang chạy và tiến trình stage vào SQLite.
8. Sửa SQLite audit, tăng version, build và self-test.

## Nguyên tắc bắt buộc
- Giữ nguyên toàn bộ thay đổi/deletion hiện có trong worktree.
- Không rollback các thay đổi Codex đã làm.
- Không đoán protocol phần cứng khi không có trace/board thật.
- Không hard-code Start Card = 1.
- IO logic vẫn bắt đầu từ 1, nhưng phải ánh xạ đúng physical Start Card.
- Chỉ tăng version sau khi các bước hoàn tất và build/self-test ổn.
- Không tạo file backup .cs/.xaml trong project làm ảnh hưởng build.

## Những thay đổi đã thực hiện trước khi hết quota

### 1) Scan generation / stale frame
Đã sửa:
- Models\TestModels.cs
- Services\D2xxBoardTransport.cs
- ViewModels\TestViewModel.cs

ScanFrame đã có thêm:
- long ScanGeneration = 0

D2xx transport đã được sửa để quản lý scan generation và stale frame.

### 2) Start Card
Đã sửa:
- Models\ProductionSettings.cs
- Services\BoardAddressMapper.cs
- Services\D2xxBoardTransport.cs
- Services\ProductionConfigService.cs
- ViewModels\ProductionSettingsViewModel.cs
- Views\ProductionSettingsPage.xaml
- Views\ProductionSettingsPage.xaml.cs

ProductionSettings có:
- StartCardNumber = 1

ProductionConfigService đã đọc/ghi:
- [StartCardNumber]...

Thiết kế đang theo hướng:
- Start Card tạo physical offset.
- IO logic trong model vẫn bắt đầu từ 1.
- Firmware scan tới card cuối cần dùng.
- Decoder bỏ các card vật lý đứng trước Start Card.

### 3) IO Confirm 1/n
D2xxBoardTransport đã thêm state ổn định:
- _stableFrameSignature
- _stableFrameCount
- _firstStableFrameConfirmed

Ý định:
IO Confirm 1/n phải xác nhận snapshot/frame liên tiếp thật sự, không chỉ lưu CFG.

### 4) Multi-Part THT
Đã sửa:
- Services\ProductionConfigService.cs
- Services\ThtModelParser.cs
- ViewModels\TestViewModel.cs

Đã thêm:
- LastThtPartKey

Đã tạo:
- Views\PartSelectionWindow.xaml
- Views\PartSelectionWindow.xaml.cs

TestViewModel đã được sửa luồng load model để hỗ trợ chọn Part trong một THT nhiều Part.

### 5) SQLite active-cycle
Đã sửa:
- Services\TestHistoryStore.cs
- Services\ProductionPersistenceService.cs
- ViewModels\TestViewModel.cs

Schema:
- CurrentSchemaVersion từ 3 -> 4

Đã bắt đầu thêm:
- UpsertActiveCycleAsync(...)
- lưu stage như TEST_STARTED
- active cycle id
- cycle timestamps

Cần rà soát migration/schema/index/query thật kỹ trước khi coi hoàn tất.

### 6) Barcode scanner COM
Đã thêm:
- Services\BarcodeScannerService.cs

Dùng:
- System.IO.Ports.SerialPort
- StringBuilder buffer
- BarcodeReceived event

ProductionSettings đã thêm:
- BarcodeScannerEnabled
- BarcodeScannerPort
- BarcodeScannerBaudRate = 9600

ProductionSettings UI đã thêm:
- Bật quét barcode
- COM barcode
- Baud rate

TestViewModel đã:
- tạo BarcodeScannerService
- bắt đầu hook barcode
- thêm _acceptedInputBarcode
- reset barcode đầu cycle
- đưa InputBarcode vào history/result.

Models\HistoryModels.cs đã thêm:
- InputBarcode

ProductionPersistenceService/TestHistoryStore đã bắt đầu thêm:
- HasInputBarcodeAsync / HasInputBarcode

### 7) Build/self-test gần nhất
Lệnh đã chạy:
dotnet build JBZUniversalTester.slnx -c Release --no-restore
dotnet run --project Tests\JBZUniversalTester.SelfTests.csproj -c Release --no-build

Kết quả gần nhất được ghi trong phiên:
- Build: PASS
- Self-test: 40/42 PASS

Hai test fail được mô tả là assertion cũ theo thiết kế:
"luôn card 1"
và cần cập nhật sau khi Start Card hoàn tất.

CẦN CHẠY LẠI vì sau đó Codex còn sửa thêm barcode/SQLite/UI nhưng quota hết trước lần build cuối.

## Việc Codex phải làm ngay khi tiếp tục

1. KHÔNG sửa tiếp ngay.
2. Chạy:
   git status --short
   git diff --stat
   dotnet build JBZUniversalTester.slnx -c Release
3. Nếu build lỗi, sửa compile trước.
4. Chạy:
   dotnet run --project Tests\JBZUniversalTester.SelfTests.csproj -c Release --no-build
5. Liệt kê chính xác 42 test và test nào FAIL.
6. Chỉ sửa assertion "card 1" nếu test cũ trái với thiết kế Start Card mới; không làm test pass bằng cách che lỗi runtime.
7. Audit toàn bộ StartCardNumber:
   rg -n "StartCard|StartCardNumber|ExpansionCardCount|CardCount|ScanCardCount|PhysicalCardCount" .
8. Audit ScanGeneration:
   rg -n "ScanGeneration|_scanGeneration|stableFrame|IoConfirm" .
9. Audit multi-Part:
   rg -n "LastThtPartKey|PartSelectionWindow|LoadParts|partKey|PartKey" .
10. Audit barcode:
   rg -n "BarcodeScanner|InputBarcode|acceptedInputBarcode|HasInputBarcode" .
11. Audit active cycle/SQLite:
   rg -n "ActiveCycle|UpsertActiveCycle|TEST_STARTED|CurrentSchemaVersion|TestStartedAt" .
12. Kiểm tra schema migration 3 -> 4:
   - backup trước migration
   - CREATE/ALTER idempotent
   - index/column tồn tại
   - dữ liệu cũ đọc được
   - query dùng COALESCE(t.TestStartedAt,t.StartedAt) không phá report cũ.
13. Rà dispose/unsubscribe:
   - BarcodeScannerService
   - D2xx FrameReceived/Log
   - WaterProof events
   - cancellation token / worker
14. Build Release lần cuối.
15. Self-test phải 42/42 hoặc giải thích test nào bắt buộc hardware.
16. Sau đó mới tăng version một lần.

## Hardware test bắt buộc tại công ty
Không được giả lập command mới để thay thế các test này.

### Scan / Start Card
Test ít nhất:
- StartCard=1, CardCount phù hợp model
- StartCard>1
- model dùng card cuối
- đổi model cùng physical range
- đổi model khác physical range
- IO logic vẫn 1..N trong UI
- physical mapping đúng card thật
- không nhận frame card trước Start Card

### 10 card
- 10 x 64 = 640 IO
- verify IO 1 và IO 640
- IO 641 phải ngoài phạm vi
- scan liên tục lâu
- không freeze UI
- không stale frame sau đổi model/re-arm
- không false PASS/SHORT/WRONG

### IO Confirm
Thử 1/n:
- n=1
- n=2
- n>2
- nhiễu 1 frame không được confirm nếu chưa đủ n
- snapshot thay đổi phải reset counter đúng

### Barcode COM
- mở đúng COM
- reconnect
- CR/LF framing
- scan nhanh liên tiếp
- barcode trùng DB
- barcode sai Part/model
- barcode đúng
- barcode được ghi InputBarcode trong history
- disable barcode phải không chặn test

### SQLite
- mở DB cũ schema v3 và migrate lên v4
- active cycle được lưu khi đang TEST_STARTED
- app crash/kill giữa cycle
- mở lại app kiểm tra dữ liệu không hỏng
- PASS/FAIL commit xong active cycle được xử lý/xóa đúng
- report/history cũ vẫn đọc được

## Dữ liệu tham chiếu Htdrv đã phân tích
Các điểm quan trọng cần giữ:
- Htdrv model THT/THA có dấu vết OLE Compound Storage.
- Stream chính: Contents.
- Có MFC CString-style serialization.
- Không nên coi Htdrv3-KETQU2000.exe chắc chắn là Qt.
- Htdrv/trace board:
  HANDSHAKE: 8A 01 01 01
  STOP:      8D 00 00 00
  RESET:     80 00 00 00
  START:     8C 00 XX 00
- 10 card -> START 8C 00 0A 00
- 10 card x 64 IO = 640 IO.
- Không đóng/mở FTDI chỉ vì đổi model nếu không cần.
- Nếu capacity/range không đổi, ưu tiên reuse scan.
- Short-Circuit Confirmation Time là setting gốc, không nên hard-code debounce theo số frame nếu chưa chứng minh.
- Extension Board Count là setting gốc.

## Yêu cầu cho Codex trong phiên mới
Hãy đọc file này trước khi sửa.
Sau đó đọc AGENTS.md và git diff hiện tại.
Không rollback bất kỳ thay đổi WIP nào.
Tiếp tục đúng từ trạng thái hiện tại, ưu tiên:
1) compile,
2) self-test,
3) audit logic,
4) hardware diagnostics,
5) version cuối.

Khi hoàn tất, trả về:
- danh sách file đã sửa,
- root cause từng lỗi,
- build result,
- self-test result,
- mục nào cần hardware verification,
- tuyệt đối không tuyên bố protocol "đã xác nhận" nếu chỉ là suy luận.
