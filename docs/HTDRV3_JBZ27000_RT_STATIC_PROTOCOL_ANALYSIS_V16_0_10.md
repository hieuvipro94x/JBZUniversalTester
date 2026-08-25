# Phân tích tĩnh Htdrv3-JBZ27000_RT.exe cho V16.0.10

## Phạm vi và nguyên tắc an toàn

Tệp được phân tích: `C:\PHT20\Htdrv3-JBZ27000_RT.exe`.

- SHA-256: `B10C94E99153EB8CA6294CF7EA4416E3E38C9861502C071A510CA8B810692C7E`
- Kích thước: 5.091.328 byte
- PE32 x86 native, không phải .NET, không có chữ ký số hay trường phiên bản.
- Image base `0x00400000`, entry point `0x0054E100`.
- Dấu vết build: MFC/ATL Visual C++ .NET 2003; tên nguồn còn lại gồm `Htdrv.cpp`, `htdrvView.cpp`, `htdrvDoc.cpp`.

Đây là phân tích tĩnh, không chạy chương trình gốc. Cách này tránh phát relay/đầu ra ngoài ý muốn trên máy sản xuất. Không sao chép mật khẩu, đường dẫn máy chủ hoặc dữ liệu vận hành tìm thấy trong binary/cấu hình.

## Phần D2XX đã xác minh

### Khởi tạo FTDI

Tại vùng mã quanh `0x00502D44`, chương trình gốc thực hiện:

1. `FT_OpenEx("FT245R USB FIFO", FT_OPEN_BY_DESCRIPTION)`.
2. `FT_SetBaudRate(..., 115200)`.
3. `FT_SetDataCharacteristics(..., 8, 0, 0)` — 8 data bit, 1 stop bit, không parity.
4. `FT_SetFlowControl(..., 0, 0, 0)` — tắt flow control.
5. Tạo Win32 event và gọi `FT_SetEventNotification(..., FT_EVENT_RXCHAR, event)`.
6. `FT_Purge(..., FT_PURGE_RX | FT_PURGE_TX)`.

JBZUniversalTester vẫn ưu tiên mở đúng VID/PID/description rồi theo serial number. Cách này chặt hơn việc mở thiết bị đầu tiên chỉ theo description của bản gốc và tránh chiếm nhầm FTDI.

### Gửi lệnh

Hàm gửi quanh `0x005031E0` có các đặc điểm:

- nghỉ khoảng 1 ms trước khi ghi;
- purge RX/TX trước `FT_Write`;
- với opcode từ `0x8D` trở lên, bản gốc có khoảng nghỉ 100 ms sau khi gửi.

Hàm chuyển trạng thái quanh `0x00502FA0` dừng scan bằng `[8D 00 00 00]`, thu hồi worker/event, gửi reset `[80 00 00 00]`, rồi mới chuyển lệnh trong một số nhánh. Đây là bằng chứng về yêu cầu một owner/reader và chuyển trạng thái tuần tự; không phải căn cứ để chèn sleep vào mọi nhánh của phần mềm mới.

### Bảng opcode có tên trong binary

| Opcode | Nhãn gốc | Mức xác minh |
|---|---|---|
| `80` | `ClrSys_` | Reset/clear; frame 4 byte đã thấy |
| `81` | `TstPnt_` | Có opcode; payload và vòng đời chưa xác minh |
| `82` | `NoCmd ` | Chỉ có tên |
| `83` | `TstNet?` | Chỉ có tên |
| `84` | `CrdNum?` | Chỉ có tên |
| `85` | `BlkChk?` | Chỉ có tên |
| `86` | `CurChk?` | Chỉ có tên |
| `87` | `RedLog?` | Chỉ có tên |
| `88` | `SetAll_` | Chỉ có tên |
| `89` | `ScnAll_` | Chỉ có tên |
| `8A` | `DlyVal?` | Đã thấy cấu trúc frame và phản hồi 2 byte |
| `8B` | `UsbSpd?` | Chỉ có tên |
| `8C` | `TstWhl?` | Bắt đầu scan; frame 4 byte đã thấy |
| `8D` | `TstStp_` | Dừng scan `[8D 00 00 00]` |
| `8E` | `RlySet_` | Relay; frame `[8E 00 00 state]` đã thấy |
| `8F` | `VolSet_` | Chỉ có tên |
| `90` | `DgtOu1_` | Định tuyến/output; cấu trúc 4 byte đã thấy |
| `91` | `DgtOu2_` | Định tuyến/output; cấu trúc 4 byte đã thấy |
| `92` | `DgtIn ` | Chỉ có tên |
| `93` | `WrtByt_` | Chỉ có tên |
| `94` | `DbgSys_` | Chỉ có tên |
| `95` | `RedRam_` | Chỉ có tên |
| `96` | `FirVer?` | Chỉ có tên |
| `97` | `IOPow ?` | Chỉ có tên |
| `98` | `FilCon?` | Chỉ có tên |
| `99` | `ClrCon?` | Chỉ có tên |
| `9A` | `SldTst?` | Chỉ có tên |

Không được gửi opcode chỉ dựa vào nhãn trên. Đặc biệt, chưa có bằng chứng đủ để đưa lệnh `0x81` vào production.

### Frame bắt tay, scan và relay

- Constructor của `0x8A` quanh `0x00411670` tạo `[8A, arg3 & 7F, arg2 & 1F, arg1 & 7F]`, sau đó đọc đúng 2 byte.
- Frame hiện dùng `[8A 01 01 01]` phù hợp trace và cấu hình hiện có.
- Start scan quanh `0x004115C0` tạo `[8C, highBits, low5Bits, 00]`; trong data có mẫu `[8C 00 01 00]`.
- Stop scan là `[8D 00 00 00]`.
- Relay có dạng `[8E 00 00 state]`.
- Định tuyến `0x90` quanh `0x00480B20` mã hóa tham số thành 4 byte. Trình tự resistance đang dùng `[90 00 00 01]`, tiếp theo `0x91` theo channel là phù hợp trace hiện có.

## Bộ giải mã dữ liệu I/O đã xác minh

Hai worker nhận dữ liệu quanh `0x00503680` và `0x00503EF0`:

- chờ event RXCHAR thay vì polling liên tục;
- gọi `FT_GetQueueStatus` rồi `FT_Read`;
- duy trì buffer lâu dài qua nhiều lần đọc;
- đồng bộ lại luồng khi gặp byte không hợp lệ;
- giải mã theo từng từ 2 byte.

Điều kiện và công thức:

```text
byte 1: bit 7 phải bằng 1
byte 2: bit 7 phải bằng 0
type = (byte1 >> 5) & 0x03
ioZeroBased = ((byte1 & 0x1F) << 7) | (byte2 & 0x7F)
```

Ý nghĩa đã đối chiếu:

- type `0`: SOURCE;
- type `1`: TARGET;
- type `2`: biên/kết thúc frame; trace hiện tại dùng `C0 00`;
- SOURCE theo sau bởi các TARGET tạo danh sách tiếp xúc điện của source đó.

`BoardIoDecoder` hiện thực đúng mô hình buffer liên tục, resync từng byte, SOURCE/TARGET/end-frame và không coi một lần `FT_Read` là một frame. `BoardAddressMapper` ánh xạ byte giao thức về I/O một-based trong giới hạn card cấu hình.

## Cách phát hiện PASS, OPEN, WRONG và SHORT

Binary gốc xây ma trận/đồ thị tiếp xúc từ các cặp SOURCE-TARGET. Phần mềm hiện tại áp dụng cùng nguyên lý điện học trên topology `.tht`:

- PASS: toàn bộ network mong đợi có kết nối hai chiều và không có cạnh ngoài topology.
- OPEN: network mong đợi thiếu liên tục, nhưng chỉ được xác nhận khi đã đủ điều kiện sản phẩm/coverage; jig trống hoặc đang lắp không được suy diễn thành hàng OPEN.
- WRONG: một đầu đang nối sang I/O không thuộc network mong đợi hoặc phía không ánh xạ được.
- SHORT: có cầu nối giữa hai component/network hợp lệ khác nhau.
- cạnh vật lý được chuẩn hóa vô hướng để dữ liệu đảo SOURCE/TARGET không làm nhân đôi lỗi.
- Probe là lớp hiển thị song song; frame vẫn đi qua `TestEngine`, nên Probe không được che SHORT/WRONG thật.

## Probe/Test Point

Đã xác minh được:

- chương trình gốc có tùy chọn Test Pointer và opcode mang tên `TstPnt_` (`0x81`);
- `.tht` có dấu vết mục Test Point;
- decoder frame vẫn dùng các I/O vật lý của bo;
- Probe không được phép ghi sản lượng, tạo FAIL hoặc kích relay.

Chưa xác minh được bằng static analysis:

- payload chính xác của `0x81`;
- phản hồi/ACK;
- lúc chuyển production scan sang test-point mode;
- quy tắc release/debounce chính xác của firmware/card từng phiên bản.

Vì vậy V16.0.10 giữ classifier Probe hiện có, vốn dựa trên topology và dấu hiệu tiếp xúc trong frame, đồng thời vẫn chuyển nguyên frame cho fault engine. Để thay bằng mode `0x81` giống tuyệt đối cần trace USB D2XX của một chu kỳ TOUCH/RELEASE trên máy gốc với relay và tải được kiểm soát.

## Cấu hình gốc liên quan hiệu năng

Từ `Htdrv3-JBZ27000_RT.cfg` (CP949):

- Card count: 4; start card: 1.
- USB delay: 1.
- Test pointer: bật.
- Waterproof serial port: 0 (tắt trong cấu hình được cung cấp).
- Good confirmation: 0 ms; short confirmation: 1000 ms.
- Stamp delay: `100,100` ms.
- Shield delay: 1; resistance delay: 0; alarm delay: 0.
- Item height: 31; scroll delay: 15; page delay: 30.
- Screen: 1920 x 1080.

Các giá trị này mô tả đúng tệp cấu hình đã cung cấp, không chứng minh chúng phù hợp với mọi jig/card. Không tự động chép timing sang production khi chưa có trace phần cứng.

## Tối ưu áp dụng trong V16.0.10

- D2XX đăng ký `FT_EVENT_RXCHAR`; worker scan ngủ theo event/cancellation thay vì thức mỗi 2 ms khi bo không có dữ liệu.
- Giữ một worker và một owner FTDI; các thao tác control tiếp tục đi qua khóa I/O và generation guard.
- Sửa log PASS gate: bỏ sequence khỏi khóa chống trùng, tránh ghi hai dòng cho mỗi frame khi trạng thái không đổi.
- Logger gom tối đa 256 dòng trong cửa sổ 25 ms và append theo từng file, giảm open/close file và I/O nền.
- Bật ReadyToRun khi publish để giảm JIT/cold-start trên CPU yếu; đổi lại gói publish lớn hơn.
- Không thay opcode/payload chưa chứng minh, không trộn UART TTL vào D2XX.

## Những gì chưa thể kết luận an toàn

- Công thức ADC resistance đầy đủ và hiệu chuẩn theo card.
- Payload/vòng đời `0x81` Probe.
- Ý nghĩa chính xác của mọi opcode chỉ có tên.
- Timing tối ưu cho từng đời card/jig.
- Waterproof/COM của máy gốc trong cấu hình này đang tắt, nên không thể dùng binary này làm bằng chứng cho giao thức máy leak đang tích hợp.

Muốn đạt tương đương tuyệt đối ở các phần trên cần trace D2XX thật, cặp input-output và trạng thái jig biết trước. Mọi frame mới phải được thêm thành vector regression trước khi đưa vào production.
