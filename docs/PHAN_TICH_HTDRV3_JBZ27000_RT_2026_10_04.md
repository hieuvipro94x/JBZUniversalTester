# Phân tích Htdrv3-JBZ27000_RT.exe và đối chiếu JBZ Universal

Ngày phân tích: 04/10/2026. Phiên bản Universal đối chiếu: V2026.09.167.

## 1. Phạm vi và xác thực đầu vào

- EXE gốc: `C:\PHT20\Htdrv3-JBZ27000_RT.exe`.
- Kích thước: 5.091.328 byte; PE32, x86, image base `0x00400000`; dùng Windows/MFC.
- SHA-256: `b10c94e99153eb8ca6294cf7ea4416e3e38c9861502c071a510ca8b810692c7e`.
- Hash này trùng EXE ghi trong phiên trace `20261004_111756_production_Htdrv3-JBZ27000_RT.zip`. Vì vậy có thể đối chiếu mã tĩnh với hành vi trong đúng phiên trace đó.
- BITMAP 144, language 1042, có 3.224 byte, trùng từng byte với `raw_resources/050_BITMAP_144_1042.dib` trong thư mục ảnh người dùng cung cấp. SHA-256 tài nguyên: `42a6c2396426a402a2f01888948771956091badbc5ef29923194eb75d2d30710`.

Phương pháp: đọc PE/resources, giải mã lệnh x86, lần các lời gọi trực tiếp, đối chiếu trace và source hiện tại. Không chạy EXE gốc, không gửi lệnh xuống bo, không sửa source vận hành hoặc cơ sở dữ liệu.

Tên hàm diễn giải bên dưới là tên đặt để mô tả chức năng, không phải tên symbol khôi phục từ source gốc. Địa chỉ là VA theo image base trên. Mã tĩnh chứng minh logic gọi/timer; thời gian màn hình thực tế còn phụ thuộc lịch chạy UI của Windows.

## 2. Kết luận về bốn đèn

**Đã xác định được cơ chế điều khiển bốn đèn. Universal hiện chưa tương đương hoàn toàn.** Đặc biệt, trắng và xanh không dùng chung một sự kiện nhận frame như Universal đang làm.

| Đèn | Logic tìm thấy trong EXE gốc | Universal hiện tại | Đánh giá |
|---|---|---|---|
| Vàng | Gọi xung sáng 200 ms khi vào hàm xử lý kiểm tra `0x004BD2E0`; lời gọi tại `0x004BD436`. | Frame Production hoàn chỉnh, không có byte lạ → xung 180 ms. | Khác thời lượng; chưa chứng minh nguồn kích hoạt tương đương ở mọi nhánh. |
| Trắng | UI kiểm tra bộ đếm thay đổi dữ liệu; khi đổi thì sáng 100 ms. | Mỗi frame được công bố → sáng 90 ms, kể cả dữ liệu giống frame trước. | Khác rõ nguồn kích hoạt và thời lượng. |
| Xanh | UI kiểm tra bộ đếm vòng quét; khi đổi thì sáng 50 ms rồi tắt. Có thêm các nhánh bật/tắt trực tiếp. | Thường sáng khi bo sẵn sàng; mỗi frame tạo xung tắt 90 ms rồi sáng lại. PASS còn nháy riêng ba lần. | Khác chiều xung, thời lượng và cách gắn với trạng thái. |
| Đỏ | Trong nhánh phân tích lỗi: số lỗi cục bộ > 0 → bật; bằng 0 → tắt. | Theo trạng thái lỗi sản phẩm/FAIL hiện hữu; loại trừ lỗi thiết bị và chuỗi Master; xóa ở chu kỳ mới. | Chưa chứng minh tương đương khi cắm dở, Probe, lỗi tạm thời hoặc tháo sản phẩm. |

### 2.1. Tài nguyên ảnh và trạng thái sáng/tắt

Lớp đèn tại `0x004245A0` nạp BITMAP 144. Bitmap 48×130 gồm các ô 16×16:

- Ba cột: sáng, tối, xám.
- Bốn hàng đầu: đỏ, xanh, vàng, trắng dạng tròn.
- Bốn hàng tiếp: cùng các màu dạng vuông.
- Màu magenta `0xFF00FF` làm nền trong suốt.

Hàm vẽ `0x00424750` chọn tọa độ nguồn `X = state × 16`, `Y = colorOffset + shapeOffset`. Giá trị state 0 chọn ảnh sáng, 1 chọn ảnh tối. Đây là kết quả đọc mã và tài nguyên, không chỉ suy đoán từ hình.

Panel khởi tạo cả bốn đèn tối ở các lời gọi:

| Offset đối tượng trong panel | Màu / colorOffset | Lời gọi khởi tạo |
|---|---|---|
| `+0x54` | Vàng / `0x20` | `0x004F779D` |
| `+0xC4` | Đỏ / `0x00` | `0x004F77B1` |
| `+0x134` | Xanh / `0x10` | `0x004F77C5` |
| `+0x1A4` | Trắng / `0x30` | `0x004F77D9` |

### 2.2. Xung trong phần mềm gốc luôn khởi động lại timer

Hàm xung `0x00424BE0` thực hiện:

1. Nếu đang có xung, hủy timer ID 1.
2. Đặt đèn sáng (`state = 0`).
3. Đặt timer ID 1 với khoảng thời gian người gọi truyền vào.
4. Khi timer đến hạn, `0x00424C50` đặt đèn tối, hủy timer và xóa cờ xung.

Vì vậy, xung mới kéo dài trạng thái sáng đến hết khoảng chờ tính từ lần gọi mới nhất. Universal hiện bỏ qua xung mới khi timer đang chạy (`PulseYellowLed`, `PulseWhiteLed`, `PulseGreenLed`). Hai cách này tạo hình ảnh khác nhau.

Ví dụ có điều kiện: nếu hàm xử lý vàng được gọi đều khoảng 154 ms như nhịp frame trong trace, timer 200 ms liên tục bị khởi động lại; đèn có thể nhìn như sáng liên tục. Không thể kết luận đèn phải tắt giữa từng frame. Trace chưa ghi trực tiếp các lần gọi hàm vàng để xác nhận ví dụ này trong phiên đã thu.

### 2.3. Đèn trắng theo thay đổi dữ liệu, không phải mọi lượt USB nhận

Panel có timer ID 2 đặt 10 ms tại `0x004F7A80…0x004F7A89`. Nhánh timer `0x004F7AA0` kiểm tra panel đang hiển thị rồi so sánh hai bộ đếm.

Đối với trắng:

- Hàm so sánh: `0x00408140`.
- Đối tượng dữ liệu toàn cục: `0x007D68F8`.
- Bộ đếm 64 bit ở offset `+0x00`, getter `0x004081A0`.
- Khi bộ đếm đổi: gọi xung 100 ms tại `0x004F7BBD`.
- `0x004105D0` tăng bộ đếm và đặt cờ dữ liệu cần xử lý.
- `0x00410580` so sánh vùng dữ liệu trước khi copy; chỉ gọi `0x004105D0` khi nội dung khác.
- `0x00480730` cũng chỉ ghi giá trị và gọi `0x004105D0` khi ô dữ liệu khác.
- `0x00410620` thực hiện tính toán lại và cũng tăng bộ đếm này.

Kết luận chắc chắn: trắng phản ánh phiên bản/thay đổi dữ liệu kiểm tra và tính toán lại. Không phải bộ đếm từng byte RX, từng lệnh TX hay mọi frame không đổi. Cập nhật mô hình cũng có đường gọi tăng bộ đếm; vì thế không được coi mỗi lần trắng sáng là một thay đổi sản phẩm vật lý.

Snapshot theo dõi trắng khởi tạo bằng -1, nên lần kiểm tra đầu có thể tạo xung dù chưa có thay đổi mới sau khởi tạo.

### 2.4. Đèn xanh theo bộ đếm vòng, có nhánh trạng thái bổ sung

- Getter `0x00411720` trả địa chỉ bộ đếm 64 bit tại `0x007D68F8 + 0x08`.
- Hàm `0x00411140` tăng bộ đếm và cập nhật thời gian hoạt động.
- Parser tại `0x00503A05…0x00503A78` tách loại bản ghi từ hai byte; nhánh loại 2 gọi tăng bộ đếm. Đường parser khác cũng gọi tại `0x0050435B`.
- Ngoài parser, có đường timer/mô phỏng và khởi tạo gọi cùng hàm. Vì vậy không nên đặt tên nó là “số gói USB RX” hoặc dùng làm số sản phẩm PASS.
- Hàm `0x004981A0` phát hiện bộ đếm thay đổi; panel gọi xung xanh 50 ms tại `0x004F7BDB`.
- Có bật trực tiếp tại `0x004B5735` và tắt trực tiếp tại `0x004BC0B7`.

Diễn giải có căn cứ: xanh là hoạt động vòng quét/cập nhật vòng của hệ thống, kết hợp một số trạng thái điều khiển. Ngữ nghĩa đầy đủ của hai nhánh bật/tắt trực tiếp chưa được xác nhận bằng trace UI. Trong các lời gọi trực tiếp đã lần, chưa tìm thấy cơ chế riêng “PASS nháy xanh ba lần” giống Universal; điều này không phải bằng chứng rằng mọi đường chạy có thể có đều đã được loại trừ.

### 2.5. Đèn vàng và đỏ thuộc nhánh xử lý kiểm tra

Hàm `0x004BD2E0` khởi tạo danh sách/count lỗi, chuẩn bị mảng IO, phát xung vàng rồi thực hiện phân tích kết nối. Nó được gọi tại `0x004BC901`.

Đỏ bật/tắt ở `0x004BF0D9…0x004BF10D` theo biến count `[ebp - 0x5058]`:

```text
count > 0 → panel.red.On()
count = 0 → panel.red.Off()
```

Count được tăng trong nhiều nhánh tạo kết quả lỗi. Đây là count của lần phân tích hiện tại, không phải bằng chứng về một kết quả FAIL đã ghi lịch sử. Hàm có đường trả về sớm trước đoạn bật/tắt đỏ; do đó cũng không được khẳng định đỏ luôn cập nhật ở mọi lượt gọi, hoặc luôn tắt ngay khi rút dây.

Muốn gán chính xác các tình huống Probe, cắm dở, chập/sai dây và Master cho đỏ phải có trace các tình huống đó; không được đưa sự kiện Probe vào FAIL chỉ để bắt chước hình ảnh đèn.

## 3. Hành vi bo và relay đã quan sát được trong trace

Phiên dài khoảng 36,8 giây, 1.689 sự kiện, dropped = 0. Có 128 frame hoàn chỉnh, mỗi frame 128 IO, không có byte không nhận diện trong các frame đó.

- 97 frame rỗng.
- 27 frame có active IO `[8]`, cạnh kết nối `4-8`.
- 4 frame có active IO `[4]`, cạnh kết nối `3-4`.

Nhịp 120 khoảng frame sau khi loại các đoạn dừng có chủ đích: trung bình 154,07 ms, trung vị 158,43 ms, khoảng 142,01–175,36 ms. Đây là nhịp frame quan sát ở cấu hình/phiên này, không phải thông số firmware cố định cho mọi model.

Có bảy chuỗi giống nhau:

```text
STOP      8D 00 00 00
RESET     80 00 00 00
Relay 1   8E 00 00 01
OFF       8E 00 00 00
Relay 2   8E 00 00 02
OFF       8E 00 00 00
START     8C 00 02 00
```

| Hai lần TX liên tiếp | Khoảng trung bình |
|---|---:|
| STOP → RESET | 287,09 ms |
| RESET → Relay 1 ON | 175,25 ms |
| Relay 1 ON → OFF | 238,76 ms |
| OFF → Relay 2 ON | 430,86 ms |
| Relay 2 ON → OFF | 238,72 ms |
| OFF cuối → START | 127,48 ms |

Toàn chuỗi khoảng 1,496–1,502 giây. Các số này là khoảng TX đo được, không phải thời gian tiếp điểm relay vật lý được đo bằng thiết bị ngoài.

Universal cấu hình đang đối chiếu đặt RelayWiringMode = 0: marking là Relay 2, JIG là Relay 1; luồng PASS ưu tiên marking nên thứ tự vật lý là R2 → R1. Trace gốc là R1 → R2. **Chưa giống thứ tự relay.** Tuy nhiên trace chỉ chứng minh số relay, chưa chứng minh R1 gốc thực sự là marking hay JIG. Không tự đổi RelayWiringMode khi chưa xác minh dây đấu và chức năng ngoài máy.

Universal còn có các lệnh OFF an toàn và logic ghi kết quả/chờ tháo sản phẩm của riêng ứng dụng. Không thể tuyên bố tương đương toàn bộ chu kỳ chỉ từ chuỗi lệnh trên.

## 4. Kết nối, in tem và độ trễ

Trace không có FTDI status lỗi. Hai bản ghi FT_OPEN cùng handle không đủ để kết luận hai owner vật lý; các lời gọi API có thể lồng nhau. Không có bằng chứng lỗi giao tiếp trong phiên này.

Có bảy job EPL qua COM1, kết thúc bằng `P1`; tổng 476 byte ghi, không thấy write lỗi. Chưa có ACK hoặc kiểm tra tem vật lý nên kết luận là phần mềm đã gửi lệnh in, không phải đã xác nhận máy in hoàn tất. D2XX bo và COM máy in là hai luồng riêng.

Các khoảng STOP/RESET/relay khoảng 1,5 giây là thời gian điều khiển quan sát được. Chúng không tự chứng minh UI bị đứng hoặc ứng dụng lag. Trace hiện thiếu thời gian chặn UI, draw-state đèn và độ trễ xử lý input để đưa ra kết luận đó.

Trong EXE gốc có mục `UsbDelay`; đoạn tạo mô tả cài đặt tại `0x00654947…0x00654962` truyền các hằng 1, 0, 127 vào hàm mô tả. Chưa lần đủ đường sử dụng để khẳng định các hằng này là miền giá trị nào hoặc UsbDelay tác động FTDI latency/sleep cụ thể ra sao. Trong transport Universal hiện tại chưa tìm thấy áp dụng UsbDelay qua `FT_SetLatencyTimer`; không được coi việc lưu giá trị là bằng chứng độ trễ USB đã thay đổi thực tế.

## 5. Mức xác nhận và giới hạn

Đã xác nhận trực tiếp: danh tính EXE, tài nguyên đèn, màu/state, thời lượng xung và timer restart, hai bộ đếm khác nhau cho trắng/xanh, nhánh count điều khiển đỏ, chuỗi relay và thời gian TX trong trace.

Chưa xác nhận đầy đủ: mapping mọi trạng thái vận hành sang bốn đèn, FAIL/Master/Probe/Leak/điện trở, vai trò vật lý R1/R2, thời điểm ProductRemoved của phần mềm gốc, công thức ADC, tác động UsbDelay gốc, độ trễ UI và in thành công trên máy thật.

Để kiểm chứng tương đương bằng thực nghiệm, phiên trace tiếp theo cần ghi trạng thái LED/vẽ bitmap hoặc video đồng bộ thời gian, đi qua chờ rỗng → cắm dở → đúng → lỗi → xác nhận → rút hết, kèm Probe và Master riêng. Khi sửa Universal, nguồn sự kiện đèn phải tách khỏi quyết định FAIL/relay và không thay đổi các invariant an toàn hiện có.

## 6. File Universal dùng để đối chiếu

- `Views/TestWindow.xaml.cs`: timer LED, frame activity, lỗi hiện hữu và PASS blink.
- `ViewModels/TestViewModel.cs`: xuất sự kiện frame, chu kỳ sản phẩm và mapping relay.
- `Models/ProductionSettings.cs`: UsbDelay, RelayWiringMode và cấu hình relay.
- `Services/D2xxBoardTransport.cs`: giao tiếp FTDI.
- `Services/ProductionTimingPolicy.cs` và luồng relay hiện tại: timing điều khiển.

Báo cáo này chỉ bổ sung tài liệu. Source vận hành, phiên bản và lịch sử sản xuất giữ nguyên. Không cần build hoặc chạy self-tests cho việc đọc EXE và bổ sung báo cáo; chưa thử bo/relay/máy in thật.

## 7. Cập nhật triển khai V2026.09.168

Sau báo cáo, người dùng yêu cầu sửa bốn đèn. V2026.09.168 áp dụng cơ chế đã xác định:

- Vàng: xung sáng 200 ms từ scan Production hoàn chỉnh.
- Trắng: xung sáng 100 ms khi tập ActiveIo hoặc ma trận kết nối đổi; bỏ qua timestamp, sequence và raw bytes. Snapshot mới sau đổi generation/mode/dải IO hoặc reconnect cũng tạo xung.
- Xanh: xung sáng 50 ms từ scan hoàn chỉnh, sau đó tắt; bỏ nền sáng theo kết nối và nháy PASS ba lần.
- Cả ba xung đều khởi động lại timeout khi có hoạt động mới.
- Đỏ: theo `CurrentFaultCount` của phần phân tích/hiển thị hiện tại, tắt khi count về 0; không dựa vào nhãn FAIL lưu lại. Count bao gồm hàng thiếu kết nối trong quá trình lắp; bật đèn không biến các hàng đó thành FAIL và không tạo sự kiện relay/lịch sử.
- Scan chưa hoàn chỉnh, terminator không rõ hoặc có unknown bytes không tạo các xung trên. Mất kết nối/lỗi thiết bị tắt đèn và xóa snapshot.

Thay đổi chỉ ở lớp trình bày và bộ so sánh snapshot chỉ đọc. Không đổi TestEngine, giao thức, timing bo, relay, Probe interlock hoặc lưu sản xuất.

Kiểm chứng: build Release không lỗi/cảnh báo; fixture riêng ngoài repo đạt 29 kiểm tra, gồm dữ liệu lặp, thay đổi cạnh với ActiveIo giữ nguyên, frame lỗi/preview, reset generation/reconnect, snapshot không bị input sửa ngược, timer WPF thực tế kéo dài xung, đỏ theo count và mất kết nối tắt cả bốn đèn. Không chạy self-test harness theo yêu cầu đã có của phiên làm việc. Chưa thử phần cứng.

Giới hạn còn lại: đây là triển khai theo cơ chế đã chứng minh, không phải xác nhận tương đương 100% ở mọi trạng thái của EXE gốc. Nhánh xanh bật/tắt trực tiếp và mapping đầy đủ Master/Probe/Leak cần thêm trace trạng thái. Universal dùng sự kiện scan hoàn chỉnh và count hiện có làm điểm tích hợp tương ứng; không thêm các trạng thái chưa được chứng minh để mô phỏng chúng.

## 8. Đính chính theo quan sát máy gốc và triển khai V2026.09.169

Người dùng xác nhận trên phần mềm gốc: lỗi sai dây/chập mạch làm xanh và đỏ cùng sáng; test OK làm xanh và vàng sáng giữ đến khi tháo sản phẩm. Người dùng cũng đính chính tên màu “đen” là nhầm. Quan sát này bổ sung hành vi giữ trạng thái mà các xung timer đơn lẻ ở mục 2 chưa giải thích đầy đủ.

V2026.09.169 sửa phần chiếu trạng thái:

- Đỏ chỉ theo `WiringFaultCount` (sai dây/chập), không theo `CurrentFaultCount` bao gồm các hàng chưa kết nối. Thiếu kết nối khi đang lắp hoặc khi tháo dở không tự làm đèn đỏ sáng.
- Có lỗi dây: xanh và đỏ giữ sáng, vàng tắt; xung frame không ghi đè trạng thái này.
- PASS: xanh và vàng giữ sáng, đỏ tắt. Đèn không tắt chỉ vì timer hết hạn hoặc chữ PASS chuyển sang hướng dẫn tháo sản phẩm.
- Getter chỉ đọc `IsPassStatusLedHeld` dựa trên removal gate PASS hiện có (`_waitForProductRelease`, `_passRemovalArmed`, `_passProductRemoved`), loại trừ lỗi thiết bị Leak. Không thêm bộ xác nhận tháo sản phẩm riêng.
- Tháo một phần vẫn giữ kết quả đèn; xác nhận ProductRemoved đầy đủ hoặc mất kết nối/lỗi thiết bị giải phóng trạng thái giữ. Các xung hoạt động thông thường vẫn áp dụng khi không giữ kết quả.
- Đèn trắng và nguồn so sánh dữ liệu của V2026.09.168 giữ nguyên.

Kiểm chứng mở rộng: build Release 0 lỗi, 0 cảnh báo; 37 kiểm tra fixture ngoài repo đạt, bao gồm lắp thiếu, lỗi chập, xung không ghi đè kết quả, PASS vượt thời gian timer, tháo dở, tháo hoàn toàn và removal wait do lỗi thiết bị. Không đổi TestEngine, relay, persistence hoặc self-test harness. Chưa thử máy thật.

## 9. Mô tả vận hành được đính chính và triển khai V2026.09.170

Người dùng đính chính rõ hơn: khi chờ chỉ xanh nháy theo nhịp; lúc lắp/kết nối ổn định và hoàn tất PASS cả xanh, vàng, trắng sáng đứng yên; PASS xong vàng/trắng tắt và xanh trở lại nháy. Trắng chỉ nháy khi có tiếp xúc IO. Mô tả này thay thế giả định giữ PASS đến ProductRemoved ở mục 8.

V2026.09.170:

- Frame hoàn chỉnh rỗng chỉ tạo xung xanh 50 ms. Không phát vàng hoặc trắng từ frame khởi tạo/rỗng/reconnect rỗng.
- Thay đổi dữ liệu có tiếp xúc IO tạo xung trắng 100 ms và xung xử lý vàng 200 ms trong Production; frame lặp không làm hai đèn này sáng lại. Ma trận chỉ có self-edge không được coi là tiếp xúc giữa hai IO.
- Sau khi PASS đã commit thành công, giữ cả xanh/vàng/trắng trong phần hoàn tất PASS và relay/restart scan của workflow hiện có. Khi workflow kết thúc (kể cả hủy/lỗi), nhả trạng thái giữ trong `finally`, vàng/trắng tắt và xanh nháy theo các frame tiếp theo. Không giữ theo chữ PASS hoặc theo chờ tháo sản phẩm.
- Token và runtime generation bảo vệ trạng thái đèn khỏi completion cũ; không cho callback cũ xóa trình bày mới.
- Red tiếp tục chỉ theo lỗi sai dây/chập; không dùng hàng chưa kết nối để bật đỏ.
- Việc nhả đèn không nhả interlock ProductRemoved, không bắt đầu chu kỳ mới hoặc thay đổi relay/persistence.

Build Release 0 lỗi/cảnh báo. Fixture ngoài repo đạt 45 kiểm tra, bổ sung trường hợp chỉ xanh ở frame rỗng, trắng tắt sau tiếp xúc, cùng sản phẩm không đổi trở lại chỉ xanh, ba đèn trong workflow PASS, workflow kết thúc khi sản phẩm vẫn trên JIG và bảo vệ completion/generation cũ. Không chạy self-test harness theo yêu cầu trước đó; chưa thử máy thật.
