# BÁO CÁO TỔNG HỢP CODEX REVIEW — JBZUniversalTester V15.0.0

## 1. Mục tiêu V15

V15 hợp nhất một phần mềm Windows để vận hành hai dòng bo JBZ khác nhau nhưng không trộn hai protocol:

- **JBZ D2XX**: FTDI/D2XX + frame scan + `.tht` + TestEngine PC.
- **JBZ UART TTL/Pi firmware**: Windows COM 115200 8N1 CRLF + `.model` + `.setup` + firmware trả OPEN/OTHER/TESTPIN/CIRCUIT.

Settings giữ:

```text
LOẠI BO MẠCH
● Tự động nhận dạng
○ JBZ D2XX
○ JBZ UART TTL
```

Card mở rộng chỉ thuộc D2XX. UART lấy physical pin/model từ `.model`/firmware.

---

## 2. Phát hiện mới từ cặp `1.model` + `1.setup`

### `1.model`
Là legacy INI text:

- `[Common]`
- `[Connector]`
- `[Pin]`

File mẫu:
- filename: `1.model`
- `[Common]/Model = 111`
- `[Common]/Name = 222`
- connector count = 1
- physical pin count = 2
- P1 source physical 1, target 2
- P2 parent 1

**Golden Pi compiler dùng tên file (`1`) làm firmware model key**, không dùng `[Common]/Model=111`.

Vì vậy command đúng bắt đầu:

```text
:MODEL,1
```

không phải `:MODEL,111`.

Expected compile cho file mẫu:

```text
:MODEL,1
:PINCOUNT,1
:PINDATA,0,1,0,1,0,0
:ARRAYCOUNT,1
:ARRAY,0,1,2
:CONCOUNT,1
:CON,0,4,0,0,5000,65535
:CONNECTORCOUNT,1
:CONNECTOR,0,1,2
:FINISH
```

### `1.setup`
Là legacy INI runtime configuration:

- `[Barcode]`
- `[Common]`
- `[Current]`
- `[InnerVoltage]`
- `[PinChange]`
- `[PreTest]`
- `[Resistor]`
- `[Sequence]`
- `[Voltage]`
- `[WaterProof]`

`[Common]/Model=/home/pi/Models/1.model`, nên setup này liên kết đúng với `1.model`.

Ví dụ:
- Barcode Use=1
- PinChange Count=1000000
- PinChange Current=119193
- PreTest Count=2, Use=0

**Kết luận:** `.setup` không phải model topology và không được gửi nguyên xuống bo. Nó là cấu hình vận hành PC/Pi. `.model` mới là nguồn topology để compiler tạo command firmware.

---

## 3. Thay đổi V15 so với V14

1. Thêm parser `.model` gốc của Pi.
2. Port compiler `.model → MODEL/PINCOUNT/PINDATA/ARRAY/CON/CONNECTOR/FINISH`.
3. Thêm parser `.setup` và giữ section/value ở tầng runtime PC.
4. File picker nhận:
   - `.jbzproduct.json`
   - `.model`
   - `.tht`
5. Thêm Product Bundle để một mã hàng có hai backend profile:
   ```json
   {
     "partNumber": "ABC123",
     "d2xx": { "tht": "D2XX/ABC123.tht" },
     "uartTtl": {
       "model": "UART/ABC123.model",
       "setup": "UART/ABC123.setup"
     }
   }
   ```
6. D2XX tiếp tục dùng `ThtModelParser`, CardCount và TestEngine hiện tại.
7. UART TTL không dùng CardCount D2XX.
8. UART hỏi `:MODELNAME?` trước khi upload.
9. Nếu model khác, compile `.model` và upload theo ACK từng command.
10. Sau upload, nếu firmware hỗ trợ query ngay, verify lại `MODELNAME`.
11. `.profile.json/.uart.txt/.protocol.txt` V14 chỉ còn fallback compatibility.
12. Không convert `.tht ↔ .model` bằng suy đoán.

---

## 4. Kiến trúc model V15

```text
                         MÃ HÀNG
                            |
                   *.jbzproduct.json
                            |
             +--------------+--------------+
             |                             |
          D2XX                           UART TTL
             |                             |
          *.tht                    *.model + *.setup
             |                             |
      ThtModelParser              PiLegacyModelParser
             |                             |
       ProductModel                  ProductModel
             |                             |
       TestEngine PC              PiLegacyModelCompiler
                                           |
                               firmware command sequence
```

UI dùng `ProductModel` chung để hiển thị:
- I/O
- Giắc
- Pin
- Wire
- Splice
- Section
- Color

Nhưng protocol/backend vẫn độc lập.

---

## 5. Golden UART model rules cần Codex giữ

- Model key gửi xuống firmware = **filename stem** của `.model`.
- Connector count phải khớp tổng pin.
- Mỗi `Pn` cần đúng 10 field.
- Physical pin không trùng.
- Parent/target phải tham chiếu pin tồn tại.
- `PINDATA` source order phải giữ đúng thứ tự source trong file.
- ARRAY target giữ đúng thứ tự.
- CON dùng connector index 0-based và thêm sentinel `5000,65535`.
- Payload chunk tối đa 64 item.
- Không tự đoán special channel chưa có trace.
- `:MAXEXT,0` chỉ gửi sau `:MEASURE`.

---

## 6. Setup rules

`.setup` được load local:

- Common/Model: liên kết model.
- Barcode: cấu hình barcode/printer.
- PinChange: counter/config.
- PreTest: cấu hình pretest.
- Current/Voltage/Resistor/InnerVoltage/WaterProof: thiết bị đo.

Codex cần:
1. không gửi legacy setup raw xuống firmware;
2. kiểm tra setup đang liên kết đúng model;
3. nếu bundle chỉ rõ setup thì ưu tiên bundle;
4. nếu chọn `.model` trực tiếp thì tìm `<same-stem>.setup`;
5. không tự bật thiết bị đo chỉ vì section tồn tại — phải tôn trọng `Use`;
6. không port các path Linux `/dev/usbtmc0` sang Windows như thể chúng là VISA/COM hợp lệ; cần adapter riêng nếu sau này hỗ trợ thiết bị đo Pi trên Windows.

---

## 7. TEST PROBE PIN

- D2XX: `ProbeContactClassifier`.
- UART: firmware `:TESTPIN,<physical>,ON/OFF`.
- Cả hai map lên UI chung.
- Probe tuyệt đối không tạo FAIL/Short/WrongWiring, không tăng sản lượng, không kích relay.

UART TESTPIN phải lookup theo `.model`; không dùng `BoardAddressMapper` D2XX.

---

## 8. PASS/FAIL/JIG

### D2XX FAIL
- popup xác nhận;
- sau XÁC NHẬN chỉ Relay 1 JIG được pulse;
- Relay 2 MARKING cấm;
- chờ product removed;
- mới re-arm.

### UART FAIL
```text
:CIRCUIT,1
→ popup
→ XÁC NHẬN
→ :UNCONNECT
→ :REMOVAL
→ :UNCONNECT
→ re-arm
```

### UART PASS
```text
:CIRCUIT,0
→ PASSPEN
→ PEN
→ UNCONNECT
→ REMOVAL
→ UNCONNECT
→ re-arm
```

---

## 9. Các regression lịch sử không được tái phạm

Codex phải audit:

- StackOverflowException trong TestViewModel.
- recursive property/setter/event.
- read-only WPF binding phải `OneWay`.
- file `.bak/.backup/copy.cs` không được Compile.
- stale callback khi đổi model/board/mode.
- nhiều reader trên cùng FTDI/COM.
- dispose handle khi worker còn đọc.
- Probe lọt vào FaultEngine.
- một IO mất rồi có lại bị coi là tháo toàn bộ sản phẩm.
- Master duplicate physical edge tăng count sai.
- FAIL vô tình chạy Relay 2.
- AUTO cùng lúc active D2XX và UART.

---

## 10. Những file mới Codex phải rà

- `Models/PiLegacyModel.cs`
  - `PiLegacyModelParser`
  - `PiLegacyModelCompiler`
  - `PiSetupProfile`
  - `IniLite`
- `Models/ProductBundle.cs`
- `Models/UartModelProfile.cs`
- `ViewModels/MainViewModel.cs`
- `ViewModels/TestViewModel.cs`
- `ViewModels/HomeViewModel.cs`
- `Services/UartTtlBoardTransport.cs`
- `Services/UnifiedBoardTransport.cs`

---

## 11. Checklist build/test Codex

### Build
- `dotnet clean`
- `dotnet restore`
- `dotnet build -c Release`
- chạy `VERIFY_BUILD_V15_0_0.cmd`
- kiểm tra WPF binding output
- không warning compile nghiêm trọng

### Model
- load `.tht`
- load `.model`
- load `.jbzproduct.json`
- bundle thiếu D2XX → báo rõ
- bundle thiếu UART → báo rõ
- setup sai model → warning/block theo policy
- malformed Pn → chặn
- duplicate physical pin → chặn
- target/parent không tồn tại → chặn

### UART board
- `*IDN?`
- `:MODELNAME?`
- model giống → không upload
- model khác → upload + ACK
- verify model
- TESTPIN
- OPEN snapshot
- OTHER normalize
- PASS
- FAIL
- unplug/replug COM
- 100+ cycle
- soak test

### D2XX
Không regression:
- scan/frame
- Probe classifier
- resistance/Keysight
- Master
- Relay1/Relay2
- ProductRemoved
- 100+ cycle

---

## 12. Firmware Downloader Windows

Firmware flasher phải là tool riêng, không trộn vào production cycle.

Protocol đã phục dựng:

```text
:DOWNLOAD\r\n
← BOOT
CFT
← OK,CONNECT
P + LE32 address + LE16 length + data + checksum
← OK,PROGRAM
...
F
← OK,FINISH
```

HEX mẫu là Intel HEX, application base `0x08008000`.

Không flash nếu:
- HEX checksum lỗi;
- image chạm bootloader;
- bo không handshake;
- ACK program sai;
- user đang chạy production test.

---

## 13. Tiêu chí nghiệm thu V15

V15 chỉ được coi production-ready khi:

- build Windows thật PASS;
- D2XX regression PASS;
- UART model compiler được đối chiếu với compiler Pi;
- `.setup` được đọc đúng nhưng không gửi xuống firmware;
- Product Bundle chọn đúng backend;
- TESTPIN hai backend hiển thị tương đương;
- FAIL/JIG đúng backend;
- ProductRemoved bắt buộc trước lượt mới;
- không stale callback/reader leak;
- 100+ cycle mỗi backend;
- soak test với bo thật.

---

## 14. Kết luận

V15 thay đổi trọng tâm model từ “UART profile thủ công” sang **đọc trực tiếp dữ liệu gốc của hệ Pi**:

> D2XX giữ `.tht`; UART giữ `.model + .setup`; một Product Bundle liên kết hai cấu hình dưới cùng một mã hàng.

Đây là hướng ít rủi ro nhất vì không phá semantics của hai hệ và không tạo converter `.tht ↔ .model` dựa trên suy đoán.
