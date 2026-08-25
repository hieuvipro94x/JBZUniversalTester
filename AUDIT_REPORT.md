# AUDIT REPORT — JBZUniversalTester V15.2.0

Ngày audit: 2026-08-09  
Baseline: `JBZUniversalTester_V15_2_0_RELAY_SAFE_PULSE`  
Target: WPF `net8.0-windows`, x86, RID `win-x86`

## 1. Phạm vi và tài liệu đã đọc

Đã đọc toàn bộ `CODEX_REVIEW_HANDOFF_JBZUniversalTester_V12_2_V2.md` và toàn bộ nhóm báo cáo hiện có trong baseline V15.2:

- các báo cáo `BAO_CAO_*` từ V12.9.5 đến V15.2.0;
- các `V12_9*_REPAIR_REPORT.md`;
- các `VALIDATION_V13_0_0.txt` đến `VALIDATION_V15_2_0.txt`;
- báo cáo tổng hợp V15.0.0 ở thư mục workspace gốc (trùng nội dung bản trong `docs`).

Các báo cáo lịch sử chỉ từng chạy kiểm tra tĩnh vì môi trường cũ thiếu .NET SDK. Audit này dùng build WPF thật trên Windows.

## 2. Baseline build và kết quả cuối

### Build đầu tiên

```text
dotnet restore JBZUniversalTester.csproj -r win-x86
RESTORE: PASS

dotnet build JBZUniversalTester.csproj -c Release -r win-x86 --no-restore
BUILD: FAIL
72 error, 2 warning
```

Lỗi chính:

- thiếu `using System.IO` trong bốn file V14/V15;
- `TestEngine` gọi API log `Info()` không tồn tại;
- nullable warning ở lookup Master fault;
- UART khai báo field-event `FrameReceived` nhưng backend này chỉ phát protocol event.

### Verification cuối

Chạy `VERIFY_BUILD_V15_2_0.cmd`:

```text
Static validation: PASS
Read-only binding audit: PASS
Clean: PASS, 0 warning, 0 error
Restore solution: PASS
Build Release solution: PASS, 0 warning, 0 error
Self-tests: 7/7 PASS
```

Output chính:

```text
bin/Release/net8.0-windows/win-x86/JBZUniversalTester_V15_2_0.dll
Tests/bin/Release/net8.0-windows/win-x86/JBZUniversalTester.SelfTests.dll
```

SDK chạy audit: `.NET SDK 10.0.302`; app vẫn target .NET 8 và máy có Windows Desktop Runtime 8.0.29.

## 3. Bug đã sửa và nguyên nhân

| Bug | Nguyên nhân gốc | Sửa |
|---|---|---|
| 72 compile error `Path/File/InvalidDataException` | File V14/V15 dùng `System.IO` nhưng thiếu namespace | Thêm `using System.IO` đúng bốn file |
| `AsyncFileLogService.Info` không tồn tại | V15.2 thêm relay log nhưng gọi nhầm API | Đưa relay log vào category `Test()` |
| Build còn 2 warning | Nullable out-value Master và field-event UART không bao giờ phát | Nullable guard rõ ràng; UART `FrameReceived` thành no-op event có tài liệu |
| Có khả năng ghi side effect trùng khi callback D2XX/UART chạy đồng thời | `_resultRecordedThisCycle` là `bool` thường | Đổi thành interlocked gate; chỉ một caller được tăng LOT/statistics, ghi history và gọi in nhãn |
| PASS có thể mất nhãn khi model đổi ngay sau chốt kết quả | Hàm in nhãn kiểm tra `_model` lần hai dù dữ liệu đã snapshot trong history | In từ snapshot `TestHistoryRecord`, không phụ thuộc model mutable |
| XLSX xuất tất cả ô dưới dạng text | Exporter có ba mapping rời và `inlineStr` cho mọi cell | Một `HistoryColumn[]` duy nhất; DateTime/LOT/I/O/count/resistance dùng native Excel types |
| Bốn setting hiển thị nhưng không có consumer | Field legacy được bind UI dù chưa có protocol/runtime đã xác minh | Ẩn khỏi UI, vẫn giữ load/save để tương thích: Waterproof, Temperature, Oversize, Shield |
| Các control `Grid.Row=10/11` chồng vào row cuối | Grid Timing chỉ khai báo row 0..9 | Sau khi bỏ field dead, sắp lại toàn bộ control vào row 0..9 |
| `.model` UTF-8 BOM báo thiếu `[Common]` | BOM đứng trước ký tự `[` ở dòng đầu | `IniLite` loại `\uFEFF` trước khi parse |
| Có thể dùng profile UART đã lưu của mã A cho mã B | `LastUartModelPath` được thêm làm fallback chỉ vì file tồn tại | Chỉ fallback khi stem profile khớp source stem/PartNumber/ModelName hiện tại |
| Báo cáo V15.2 nói có validation nhưng source không có script | Thiếu `Validate-V15.2.0.ps1` | Thêm static validator và tích hợp vào verify script |

## 4. Audit kiến trúc và logic

### Frame routing

Đạt ở source/test:

```text
D2xxBoardTransport -> BoardIoDecoder -> TestViewModel
                                      -> TestEngine (Production)
                                      -> Probe UI (Probe)
```

- `TestEngine` không subscribe trực tiếp `FrameReceived`.
- Application subscriber trực tiếp là `TestViewModel`; `UnifiedBoardTransport` chỉ forward event của backend đang active.
- `TestEngine.ProcessFrame()` reject frame không phải `BoardScanMode.Production` và frame chưa complete.
- `ConfigureMode()` reset decoder state; self-test xác nhận source cũ không lọt qua lần switch mode.
- UART không phát binary `ScanFrame`; dùng `ProtocolEventReceived` cho TESTPIN/OPEN/OTHER/CIRCUIT.

Giới hạn: explicit Probe mode đã tách deterministic. Inline Probe song song khi D2XX vẫn dùng `ProbeContactClassifier` fan-in/fan-out vì source/log chưa có mode-bit firmware được xác minh. Trạng thái này là `NEEDS_HARDWARE_VERIFY`, không mở rộng heuristic trong lần sửa này.

### Product side effects

- `RecordCompletedProduct()` là cổng duy nhất cho statistics/history/error log/LOT/label.
- Interlocked gate bảo đảm một cycle chỉ chốt một lần kể cả callback khác thread.
- Master state machine không gọi `RecordCompletedProduct()`.
- Device/transport exception trong relay/Keysight không tự ghi Product FAIL nếu chưa có kết luận sản phẩm.
- UART `CIRCUIT` có `_uartResultHandlingStarted`; chỉ unlock tại ranh giới `UNCONNECT` sạch.

### Relay

- PASS: R2 MARKING đúng một pulse, OFF, delay cấu hình, R1 JIG đúng một pulse, OFF.
- FAIL: chỉ R1 JIG; không có call R2.
- Master: chỉ R1 JIG; không marking.
- Manual R1/R2 dùng API safe pulse trong `TestEngine`.
- Safe pulse serialize bằng semaphore, OFF trước, ON một lần, OFF trong `finally`, retry OFF tối đa ba lần.

### Card/I/O

Code dùng một nguồn sự thật `BoardCapacity`:

```text
1 module = 2 physical card = 64 I/O; START_SCAN xx=1
2 module = 4 physical card = 128 I/O; START_SCAN xx=2
5 module = 10 physical card = 320 I/O; START_SCAN xx=5
10 module = 20 physical card = 640 I/O; START_SCAN xx=10
```

Self-test đạt IO1/32/33/64/65/640 và reject IO641. Trace trong project chỉ chứng minh trực tiếp `xx=4 -> 256 I/O`; mức 10/640 và `StartCardNumber > 1` vẫn `NEEDS_HARDWARE_VERIFY`.

### History/label

- SQLite search theo date/LOT/product/result: self-test PASS.
- CSV UTF-8 BOM: PASS.
- XLSX ZIP/OpenXML hợp lệ ở mức cấu trúc; DateTime và number là native cell: PASS.
- `HtdrvName` chứa version + card + USB + ba timing relay.
- EPL sequence theo `ALL6_LABEL_SAMPLE.txt`: PartNumber → Eco → PartName → Serial → Barcode: PASS.
- Workspace không có file gốc `ALL6.xls`, chỉ có sample text; exact template/layout là `NEEDS_HARDWARE_VERIFY`/cần cung cấp file mẫu.

## 5. Audit ProductionSettings

Mọi property được JSON load/save tự động. `ProductionConfigService` đồng thời đọc/ghi legacy CFG và normalize/migrate các key chính.

| Property | Default | UI | Runtime consumer / trạng thái |
|---|---:|---|---|
| BoardMode | Auto | Có | `UnifiedBoardTransport`, Main/Test VM — đạt |
| UartPort | rỗng | Có | UART port resolver — đạt |
| LastUartModelPath | rỗng | Có | Safe profile resolver/model sync — đạt |
| CardCount | 1 | Ẩn/derived | Compatibility scan-unit, decoder/transport — đạt |
| ExpansionCardCount | 1 | Có | `BoardCapacity`, transport, identity — đạt |
| IoConfirm1 | 1 | Có | Stable frame cho net một target — đạt |
| IoConfirmN | 1 | Có | Stable frame cho net nhiều target — đạt |
| UsbDelay | 1 | Có | D2XX timing + identity — đạt |
| StartCardNumber | 1 | Có | Capacity/address mapper — code đạt, hardware >1 cần verify |
| UseTestPointer | true | Có | D2XX/UART Probe display — đạt |
| AutoMasterSequence | true | Ẩn | Compatibility, normalize luôn true — đạt |
| MasterFaultRequiredCount | 2 | Có | Master Bad N/N — đạt |
| MasterFaultCountsByModel | rỗng | Qua field N/N | Config service + Master lookup theo model — đạt |
| WaterproofSerialPort | 0 | Không | Legacy-only, không tự đoán protocol |
| LotNo | 2000 | Có | History/statistics/label/LOT increment — đạt |
| Lot | rỗng | Không | Legacy migration only — đạt |
| DeviceName | rỗng | Có | History/export — đạt |
| DeviceNumber | rỗng | Có | History/export — đạt |
| OperatorCompany | rỗng | Có | History/export — đạt |
| ProductionLine | rỗng | Có | History/export — đạt |
| TemperatureTolerance | 0 | Không | Legacy-only, không có sensor consumer |
| MinimumErrorLogValue | 0 | Có | `ErrorLogService` filter — đạt |
| AutoSaveErrors | false | Có | `ErrorLogService` — đạt |
| ShortConfirmMs | 0 | Không | Normalize 0, fault báo ngay — đạt |
| Relay1JigPulseMs | 250 | Có | Safe R1 pulse — đạt |
| Relay2MarkingPulseMs | 250 | Có | Safe R2 pulse — đạt |
| PassMarkingToJigDelayMs | 430 | Có | PASS R2→R1 interlock — đạt |
| StampDelay | 250,250 | Không | Migration compatibility — đạt |
| OversizeWaitSeconds | 0 | Không | Legacy-only, không có semantics xác minh |
| ShieldDelay | 1 | Không | Legacy-only, không có hardware consumer xác minh |
| ResistanceDelayMs | 0 | Có | Keysight settle delay — đạt |
| Password | rỗng | Có | Settings password gate — đạt |
| ItemHeight | 31 | Có | Fault/DataGrid row height — đạt |
| ScrollDelay | 15 | Có | TestWindow scrolling — đạt |
| PageDelay | 30 | Có | Post-continuity UI delay — đạt |
| ShowTitle | true | Có | TestWindow visibility — đạt |
| ShowConnector | false | Có | TestWindow visibility — đạt |
| LastThtPath | rỗng | Chỉ đọc | Startup/model reload — đạt |
| ResistanceChannels R1-R5 | disabled | Có | Danh sách bước đo, Channel và Min/Max lấy từ Production Settings; không phụ thuộc block resistance THT — đạt |
| AutoPrintLabelOnPass | true | Có | PASS label gate — đạt |
| HistoryDirectory | Data/History | Có | SQLite/bootstrap/export — đạt |
| Label.PrinterName | rỗng | Có | Windows print path — đạt |
| Label.PrinterCom | rỗng | Có | COM print path — đạt |
| Label.WidthMm/HeightMm | 90/15 | Có | EPL dimensions — đạt |
| Label.FormatName | KS91 | Có | EPL format — đạt |
| Label.BaudRate | 9600 | Có | COM printer — đạt |
| Label.WriteTimeoutMs | 3000 | Có | COM printer — đạt |
| Label.Copies | 1 | Có | Print copies — đạt |

## 6. Test cases tự động

`Tests/JBZUniversalTester.SelfTests.csproj` không dùng test package ngoài; chạy được bằng `dotnet run` và trả exit code khác 0 nếu có lỗi.

1. BoardCapacity/address: 64/128/320/640 I/O và boundaries.
2. Decoder: Production IO1→IO18; Probe touch/release IO5; unmapped IO113; mode switch không giữ source cũ.
3. TestEngine: expected pair, wrong pair, Open, splice IO5/20/33, Probe frame không tạo fault.
4. Relay: PASS R2 một lần trước R1; OFF xen giữa; FAIL chỉ R1 và kết thúc OFF.
5. History: SQLite insert/search; CSV BOM/cột; XLSX DateTime/number native.
6. Label: thứ tự dữ liệu ALL6 sample.
7. Pi compiler: `.model` UTF-8 BOM và golden command sequence từ `1.model` trong báo cáo V15.

Kết quả cuối: `7/7 PASS`.

## 7. File source đã sửa/thêm

### Sửa

- `Models/UartModelProfile.cs`
- `Models/PiLegacyModel.cs`
- `Models/ProductBundle.cs`
- `Services/HistoryExportService.cs`
- `Services/TestEngine.cs`
- `Services/UartTtlBoardTransport.cs`
- `ViewModels/MainViewModel.cs`
- `ViewModels/TestViewModel.cs`
- `Views/ProductionSettingsPage.xaml`
- `JBZUniversalTester.csproj`
- `JBZUniversalTester.slnx`
- `VERIFY_BUILD_V15_2_0.cmd`

### Thêm

- `Scripts/Validate-V15.2.0.ps1`
- `Tests/JBZUniversalTester.SelfTests.csproj`
- `Tests/Program.cs`
- `AUDIT_REPORT.md`

## 8. NEEDS_HARDWARE_VERIFY

Không tuyên bố production-ready nếu chưa chạy các mục sau:

1. D2XX explicit Probe và inline Probe trên bo thật; xác nhận không false-short và không làm mất wrong/short thật.
2. `START_SCAN xx=1/2/10`, 640 I/O và `StartCardNumber > 1` bằng trace thật.
3. FTDI unplug/replug, mode switch, 100+ cycle và soak 1–8 giờ.
4. UART TTL: AUTO fallback, model query/upload/ACK, OPEN/OTHER/TESTPIN/CIRCUIT, unplug/replug.
5. Relay vật lý: 20 PASS, 20 FAIL, spam click, rút USB giữa pulse; watchdog/default-OFF của firmware nếu yêu cầu fail-safe tuyệt đối.
6. Master Good/Bad N/N, resistance Master, model change reset gate.
7. Keysight/VISA x86, route R1-R5 và delay thực tế.
8. THT/THA OLE thật, duplicate pin, CLIP AO/aN và model I/O lớn.
9. File `ALL6.xls` gốc và máy in EPL thật để đối chiếu layout chính xác.

## 9. Kết luận

Source hiện build sạch và có regression self-tests cho các đường logic có thể xác minh không cần hardware. Các thay đổi không đoán thêm command/protocol. Những kết luận phụ thuộc bo, jig, Keysight, printer hoặc file mẫu gốc được giữ rõ là `NEEDS_HARDWARE_VERIFY`.
