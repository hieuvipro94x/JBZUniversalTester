# Audit logic và phần cứng V16.0.46

Ngày kiểm tra: 2026-08-26 (Asia/Bangkok).

## Phạm vi và giới hạn

- Đối chiếu mã hiện tại với báo cáo phân tích tĩnh `Htdrv3-JBZ27000_RT.exe`, trace D2XX đã lưu và reference Pi/UART trong `docs/reference`.
- Build solution, self-test, audit WPF binding, parse các model thật trong `C:\item`.
- Kiểm tra thật bo D2XX FT245R serial `AI050MBB` trong phạm vi không kích Relay 1 JIG hoặc Relay 2 MARKING.
- Không thay dây/jig để tạo OPEN/WRONG/SHORT. Không đo resistance thật vì VISA không mở được Keysight.
- Không xác nhận có đủ 10 module vật lý đang mắc trên chain. Bài `xx=10` chỉ xác nhận board nhận lệnh và trả một frame hoàn chỉnh.

## Kết quả đã xác minh

- D2XX library `0x00030221`; đúng FT245R `VID/PID 0403:6001`; handle rảnh trước và sau test.
- 5/5 connect/disconnect PASS.
- `START_SCAN 8C 00 0A 00`: supervisor nhận frame đầu sau khoảng 6.508 giây; timeout mới 15 giây; PASS, không recovery sai.
- Scan `xx=4`: 32 complete frame / 10.374 giây, 0 incomplete, 0 unknown, gap lớn nhất 309.866 ms.
- Scan `xx=5`: 4 complete frame / 10.377 giây, 0 incomplete, 0 unknown, gap lớn nhất 2244.086 ms.
- Scan `xx=10`: frame đầu kết thúc khoảng 6.122 giây trong trace; 0 incomplete, 0 unknown.
- Selector resistance CH1..CH10 gửi thành công; cleanup gửi release, relay OFF, STOP và đóng handle.
- 11/11 file `.tht` trong `C:\item` parse thành công.
- Build Release: 0 warning, 0 error. Self-test: 29/29 PASS. Read-only binding audit: PASS.

## Sai khác/rủi ro so với phần mềm gốc

### Mức nghiêm trọng

1. D2XX OPEN hiện bị vô hiệu hóa như lỗi sản phẩm. `ProductionFaultConfirmationGate.UpdateOpenCandidates()` luôn xóa OPEN và `TestEngine.BuildConfirmedOpenFaults()` luôn trả rỗng. Phần mềm gốc có logic OPEN; bản hiện tại chỉ hiển thị chân/mạng còn thiếu và không thể chốt sản phẩm FAIL vì OPEN.
2. UART TTL/Pi không còn transport/runtime trong app hiện tại. `.model` vẫn xuất hiện trong hộp chọn file nhưng `MainViewModel` luôn từ chối. Chưa có app `JBZ.PiBoard.PC.exe` riêng, nên chức năng Pi/UART của phần mềm gốc chưa được thay thế đầy đủ.
3. `StartCardNumber > 1` chỉ dịch mapping ở PC. Frame START_SCAN hiện vẫn là `[8C,00,count,00]`, không truyền start-card xuống firmware. Chưa có trace chứng minh cách này tương đương phần mềm gốc.

### Mức cao

4. Probe D2XX vẫn dùng heuristic `ProbeContactClassifier` trên stream Production, chưa dùng opcode gốc `0x81 TstPnt`. False-positive có thể làm một frame lỗi mới không đi vào `UpdateWiringFaults`; cần trace TOUCH/RELEASE và fault thật trước khi thay đổi.
5. `IoConfirm1` và `IoConfirmN` có trên màn Cài đặt nhưng không có consumer runtime. `RequiredStableFrames` hiện chỉ xuất hiện trong log product-detect, không điều khiển confirmation. Đây là UI/config chết và không tương đương Htdrv.
6. Timing runtime cố định (SHORT 100 ms, WRONG 100 ms, settle 200 ms), trong khi config Htdrv gốc đã quan sát có short-confirm 1000 ms. Chưa có trace jig để khẳng định timing tối ưu hiện tại an toàn.
7. Khi scan 10 unit, một frame khoảng 6.1–6.5 giây. Các debounce dựa trên lần gọi `ProcessFrame` cần ít nhất frame kế tiếp, nên PASS hoặc fault confirmation có thể mất khoảng 12 giây. Cần xác định người vận hành nói “10 card” là 10 card vật lý (có thể tương ứng 5 scan-unit) hay 10 scan-unit/20 card vật lý.

### Mức trung bình

8. `HomeViewModel` vẫn quảng cáo `.model legacy` dù app D2XX-only.
9. Publish script tắt ReadyToRun dù `.csproj` bật ReadyToRun; bản publish nhỏ nhưng cold-start/JIT không theo chủ đích tối ưu ghi trong project.
10. `WH322244.tht` có hai cảnh báo connector pin-count: connector 9 khai báo 2 nhưng pin map lớn nhất 14; connector 10 khai báo 2 nhưng pin map lớn nhất 12.
11. `1189508-AAO.tht` parse được nhưng PartNumber bên trong là `1174051-AC`; cần xác nhận dữ liệu model, không nên tự sửa bằng suy luận.
12. Validator V15.2 cũ đã lệch UI (`ProbeCycleCount` đổi thành `ProbeCycleText`); validator binding đã được cập nhật trong V16.0.46.

## Thiết bị chưa xác minh hoàn chỉnh

- FT231X COM6 không trả lời `*IDN?`; không được coi là Universal Tester UART.
- Keysight resource `USB0::10893::4865::my57222903::0::INSTR` được tìm thấy nhưng `viOpen` lỗi `0xBFFF0060`.
- Chưa thử Relay 1/Relay 2 vật lý, PASS/FAIL thật, rút USB giữa pulse, OPEN/WRONG/SHORT bằng dây thật, probe TOUCH/RELEASE, máy leak và máy in.

## Trace chính

- `Data/HardwareVerification/supervisor_v16_0_46_cards10.trace` — SHA-256 `B916C2B9FEAF8A74EA1EE34F03EE6DE0A2C6EEE3711550A9CE693971461A465A`.
- `Data/HardwareVerification/passive_10s_v16_0_46_cards4.trace` — SHA-256 `DE8688B4FC97424A697EEAFC189240C7B87BDD5485098658D1B115405B080B6B`.
- `Data/HardwareVerification/passive_10s_v16_0_46_cards5.trace` — SHA-256 `FC9F3CCD914027FA0067992540A52B0A22D67C2158D91E12B02BDC57A68B5989`.
- `Data/HardwareVerification/resistance_routes_v16_0_46.trace` — SHA-256 `7D4FF9D628CACA6B2D4C2B52687A8DAE025DE0BEAD9C93DA4A70B1050D8B82D3`.
