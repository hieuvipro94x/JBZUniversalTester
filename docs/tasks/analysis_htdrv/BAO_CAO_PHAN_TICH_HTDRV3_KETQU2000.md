# Báo cáo phân tích tĩnh Htdrv3-KETQU2000.exe

Phạm vi: phân tích tĩnh, không chạy trực tiếp chương trình và không giao tiếp với phần cứng.

## 1. Nhận dạng chương trình

| Thuộc tính | Kết quả |
|---|---|
| Tệp | `Htdrv3-KETQU2000.exe` |
| SHA-256 | `2C04FF9766B7AF19EFFEFC9D7D5F6C6277F522945E1DA9484AFE072240EB7D9D` |
| Kích thước | 4.005.888 byte |
| Kiểu | PE32 x86, Windows GUI, native C++ |
| Image base / entry point | `0x00400000` / `0x0052BCE0` |
| Toolchain | Microsoft Visual C++ 7.1 / Visual Studio .NET 2003, MFC/ATL liên kết tĩnh |
| PDB gốc | `r:\harn\임시파일\drv\debug-UI2\Htdrv.pdb` |
| Ngày build có trong chuỗi | `Sep 15 2020` |
| Chữ ký số | Không có |

Đây là phần mềm kiểm tra dây điện/harness tester, có UI MFC kiểu Document/View, không phải .NET. Các tên lớp còn sót lại gồm `CCardTestDlg`, `CDeviceDlg`, `CFileSelectDlg`, `CMainFrame`, `CHtdrvDoc`, `CHtdrvView`, `CMainView`, `CTestView`, `CCheckView`, `CEnvirView`, `CManualView`, `CReportView`, `CInquiryDlg` và `CDrvSock`.

Không thấy dấu hiệu packer: không có overlay, entropy `.text` chỉ 6,23 và strings/resources còn rất đầy đủ. Tuy nhiên `.text` và `.rsrc` mang quyền RWX, relocations bị loại, không có ASLR/NX flags hiện đại; đây là đặc điểm build cũ và làm giảm an toàn bộ nhớ, không tự nó chứng minh mã độc.

COFF timestamp là 2007 nhưng debug directory và chuỗi build trỏ đến 15/09/2020. Timestamp PE có vẻ được giữ lại hoặc không đáng tin bằng dấu vết build/PDB.

## 2. Thư viện và thiết bị đi kèm

### Import trực tiếp

- MFC/ATL và CRT được liên kết tĩnh; EXE không cần DLL MFC riêng.
- Windows UI/đồ họa: `USER32`, `GDI32`, `COMCTL32`, `COMDLG32`, `GDIPLUS`, `WINMM`.
- File/INI/thread/serial: `KERNEL32`; có `CreateFileA`, `ReadFile`, `WriteFile`, `SetCommState`, `SetCommTimeouts`, `CreateThread`, `CreateIoCompletionPort`, `GetPrivateProfileStringA`, `WritePrivateProfileStringA`.
- Database/COM: `OLE32`, `OLEAUT32`; mã chứa MFC DAO và Jet/ACE OLEDB.
- Network: `WS2_32`; có socket TCP/IP và mẫu kết nối `SQLOLEDB` đến SQL Server/TMES.
- Registry và shell: `ADVAPI32`, `SHELL32`, `SHLWAPI`.

### Delay-load phần cứng

- `FTD2XX.dll`: `FT_OpenEx`, `FT_Close`, `FT_Read`, `FT_Write`, `FT_Purge`, `FT_GetQueueStatus`, `FT_SetBaudRate`, `FT_SetDataCharacteristics`, `FT_SetFlowControl`, `FT_SetEventNotification`. Đây là đường giao tiếp USB/FTDI với card tester.
- `VISA32.dll`: `viOpenDefaultRM`, `viOpen`, `viPrintf`, `viScanf`. Đây là đường giao tiếp thiết bị đo bằng VISA/SCPI.
- `OLEACC.dll`: accessibility/UI.

Chuỗi lệnh SCPI cho thấy hỗ trợ các thiết bị như `USB-344XX`, `34401`, `LCR-6300`, `HIOKI`, `Chroma`; các lệnh đáng chú ý gồm `*IDN?`, `READ?`, `MEAS:TEMP?`, `:MEASURE:VOLT:DC?`, `CONF:%s`, `SOURce: SAFEty: STARt/STOP` và truy vấn kết quả an toàn điện.

### libxl.dll trong thư mục

`libxl.dll` là LibXL 4.2.0.0 x86 của XLware, SHA-256 `BDF8FA8E5BF5EBC7C470DC142138B6B316B4DC362BEB022D0DD52042D9044FDC`, có chữ ký số hợp lệ của XLware. EXE không import `libxl.dll`, không chứa tên DLL này và không tham chiếu các export `xl*`; vì vậy phiên bản EXE này không có bằng chứng sử dụng LibXL. Phần xuất Excel của EXE dùng COM/OLEDB qua provider Jet 4.0 hoặc ACE 12.0. `libxl.dll` nhiều khả năng thuộc chương trình khác hoặc một bản Htdrv mới hơn nằm cùng thư mục.

## 3. Luồng khởi động

Luồng có thể phục dựng như sau:

1. Entry point `0x0052BCE0` chạy CRT/MFC startup: thiết lập SEH, heap, locale, OLE/COM và MFC runtime, sau đó gọi virtual method của đối tượng application qua `0x0058DF00`.
2. Mã application đọc đường dẫn module, profile `HTDRV/@htdrv.cfg`, rồi tạo đường dẫn CFG cùng tên EXE bằng cách bỏ 4 ký tự `.exe` và nối `.cfg` tại vùng `0x0047CE90–0x0047CF07`.
3. Nạp toàn bộ cấu hình: card, start card, IO confirm, USB delay, barcode COM, thông tin máy/part, thời gian trễ, barcode, đo điện và layout UI.
4. Tính số card hiệu dụng tại `0x004B3E26–0x004B3E8C`: `totalCards = nCard + nExtCard`; `startOffset = n1stCard - 1`; sau đó cấp/đặt vùng I/O theo `totalCards × 64`.
5. Khởi tạo DAO database và phiên chạy. Database mặc định được nhắc đến là `C:\pht20\HtDrv.tdb`; tài nguyên `TEXT/TDB` nhúng sẵn schema để tạo/nâng cấp database. Khi bắt đầu phiên, chương trình `INSERT INTO Run(...)`; khi đóng thì `UPDATE Run SET OffTime...`.
6. Nhận dạng chế độ `CARDTEST`, khởi tạo card/FTDI. `USB Delay`, `IO Confirm 1`, `IO Confirm n` được truyền vào lớp phần cứng tại `0x0047DF56–0x0047DF81`.
7. Khởi tạo barcode COM, VISA/thiết bị đo, socket/TMES tùy option, rồi tạo `CHtdrvDoc`/`CHtdrvView` và các view con.

File `.RPT` cho thấy hai lỗi từng xảy ra:

- `C06D007E` tại thunk `0x00552DC7`: địa chỉ này nằm đúng trong delay-import của `FTD2XX.dll`; nguyên nhân rất có khả năng là thiếu/sai kiến trúc `FTD2XX.dll` khi chạy từ ổ E:.
- `C0000005` tại `0x00485479`: code lấy một COM/UI object rồi gọi vtable `+0x140` mà không kiểm tra object con trả về có null hay không. Đây là null-pointer access trong luồng khởi tạo, không phải lỗi file PE.

## 4. Cơ chế đổi mã hàng / đổi part

1. Người dùng chọn file kiểm tra qua MFC File/Open hoặc dialog `CFileSelectDlg`; filter là `Htdrv Files (*.tht)`.
2. Code tại `0x0048E3AF–0x0048E47B` lấy bốn ký tự cuối và chấp nhận `.THT` hoặc `.XLS`. Nhánh chính của chương trình tester là `.THT`.
3. `.tht` là OLE Compound File/MFC serialized document. File mẫu `M030135100-SP3.tht` có magic `D0 CF 11 E0 A1 B1 1A E1` và chứa part `M030135100`, revision `V1.110`. Nó không phải text thuần.
4. Nếu một file chứa nhiều part, dialog resource 146 (`파트 선택` / Part Select) hiển thị `SysListView32`. Có thêm file ánh xạ tùy chọn `YetechPartMap.txt`.
5. Sau khi chọn, metadata hiện hành gồm `FileName`, `FileRev`, `PartName`, `PartNum`, `Eco`, `Nco`, `Alc`, `Lot`, `CarName`, `Line`, `Worker`, `MachineName/No`.
6. Lớp DB tìm hoặc tạo `File` theo `Path + FileName + FileTime + FileLength`, tìm hoặc tạo `Part` theo `PartName + PartNum + Eco + Nco + Alc`, rồi tạo liên kết `FileToPart`. Khi đọc thành công, `File.Exist=True, File.Read=True`.
7. UI/netlist/drawing và cấu hình test được thay bằng document mới. Chương trình từ chối rời màn hình nếu sản phẩm hiện tại chưa xử lý xong (`Please complete product processing before leaving the screen`).
8. Khi chạy sản phẩm, barcode đọc được so với part/ECO/NCO/ALC và barcode đã in/xuất. Các nhánh lỗi gồm `Part number mismatch`, `Heterogeneous barcode detected`, `Not matched barcode`, `Barcode content is short`, `Tested product` và kiểm tra trùng trong DB.

Vì vậy “đổi mã hàng” không chỉ thay một chuỗi: nó đổi document `.tht`, chọn record Part, cập nhật liên kết File–Part, nạp lại netlist/ảnh/layout/test option, rồi đổi ngữ cảnh ghi kết quả trong DB.

## 5. Đọc/ghi dữ liệu

### CFG/INI

CFG dùng ANSI Korean CP949 và API `GetPrivateProfileStringA/GetPrivateProfileIntA/WritePrivateProfileStringA`. File hiện tại cho thấy `nCard=2`, `nExtCard=0`, `n1stCard=1`, barcode COM 1, `IO Confirm 1=1`, `IO Confirm n=1`, `USB Delay=0`, kiểm tra chập 1000 ms.

### THT/XLS/TXT

- `.tht`: OLE Compound/MFC document, chứa drawing/netlist/part/test data.
- `.xls`: đọc/ghi qua Jet/ACE OLEDB, có `SELECT * FROM [%s$]` và `CREATE TABLE %s(%s)`.
- Text phụ trợ: `Device.txt`, `NetList.txt`, `PartCnt.txt`, `LastBarcode.txt`, `BarTable.txt`, `WorkerList.txt`, `PAT.txt`, `DbLog.txt`, `htdrv.log`, `htdrv.bin`, `htdrvfeed.bin`, `BarForm\*.txt`.
- Lịch sử text có mẫu đường dẫn `%s\Year%4d\Month%02d\Day%02d.txt`.

### Database TDB/DAO

Schema nhúng gồm:

- `Run`: phiên chạy, version, option, thời gian bật/tắt.
- `File`: đường dẫn, tên, timestamp, kích thước, cờ Exist/Read.
- `Part`: PartName/PartNum/Eco/Nco/Alc, FirstUse/LastUse, TotalPass/TotalFail.
- `FileToPart`: liên kết nhiều-nhiều file và part.
- `Config`: snapshot cấu hình text.
- `Test`: Lot, Pass, Rework, Master, Result, StageProc, Worker, Tester, DataMatrix, Load/Pass time, `TimeRec`, `BarRead`, `BarWrite`, `Res`, Memo.
- `Error`: lỗi theo stage/type/connector/pin/IO/circuit/color/width.
- `Prob`: số lần dùng probe và giới hạn thay probe.
- `Worker`: code, tên và cờ manager.

Khi bắt đầu sản phẩm, code `INSERT INTO Test(...)`. Trong quá trình chạy nó cập nhật `StageProc`, `TimeRec`; khi kết thúc cập nhật `Result`, `BarRead`, `BarWrite`, `Res`, `ConfigID`, `Memo`, đồng thời tăng `Part.TotalPass/TotalFail` và thêm các record `Error`.

`TimeRec` là log sự kiện theo đơn vị giây, độ phân giải 0,1 s. Các nhãn được tài nguyên giải thích: bắt đầu/kết thúc stage, lắp sản phẩm, lắp connector cuối, vào đo, barcode đọc/khớp/sai/in/thất bại, stamp, pass, tháo sản phẩm, hoàn tất ghi kết quả, timeout short và ghi lỗi.

## 6. Cơ chế scan 10 card

Các tham số nội bộ:

- `nCard`: 1–80, mặc định 2.
- `nExtCard`: card hộp mở rộng; được cộng vào tổng.
- `n1stCard`: 1–20, mặc định 1.
- `IO Confirm 1` và `IO Confirm n`: số lần xác nhận ổn định sau khi xuất một I/O hoặc khi đọc bình thường.
- `UsbDelay`: thời gian trễ truyền USB.

Mỗi card ánh xạ đúng 64 điểm I/O (`0x0046EB89`: `totalCards × 64`). Với 10 card và không có card mở rộng:

```text
totalPoints = (10 + 0) × 64 = 640 bit
totalBytes  = 640 / 8       = 80 byte
readWords   = 640 / 16      = 40 word 16-bit
startBit    = (StartCard - 1) × 64
```

Đường ghi một điểm ở `0x0046EDA0–0x0046EE49`:

```cpp
if (logicalPin < 0 || logicalPin >= totalCards * 64) return;
if (swapOddEven) logicalPin ^= 1;
physicalPin = logicalPin + (startCard - 1) * 64;
byteAddress = 0x600 + physicalPin / 8;
bitIndex = physicalPin & 7;
writeHardwareBit(byteAddress, bitIndex);
```

Đường đọc ở `0x0046EE80–0x0046EFD7` làm việc theo 16 bit:

```cpp
if (wordIndex * 16 >= totalCards * 64) return 0;
physicalWord = wordIndex + ((startCard - 1) * 64) / 16;
value = readByte(0x600 + 2 * physicalWord)
      | readByte(0x601 + 2 * physicalWord) << 8;
repeat until the same value is observed confirmCount times;
if (swapOddEven) value = ((value & 0x5555) << 1) | ((value & 0xAAAA) >> 1);
return value;
```

Nếu mẫu đọc mới khác mẫu trước, bộ đếm ổn định bị đặt lại về 0. Sau thao tác ghi, code chọn `IO Confirm 1`; các lần đọc bình thường chọn `IO Confirm n`. Do đó “scan 10 card” là quét một không gian I/O liên tục 640 bit theo 40 word, không phải mười lần gọi độc lập theo tên card. Cảnh báo nhúng trong EXE xác nhận nếu đặt số card quá nhỏ thì các điểm Extra nằm sau giới hạn sẽ không được phát hiện.

## 7. Chuỗi, lệnh, tên hàm và tài nguyên liên quan mã nguồn

### Dấu vết source tree

- `c:\harn\gg\drv\htdrv.cpp`
- `c:\harn\gg\drv\htdrvdoc.cpp`
- `c:\harn\gg\drv\htdrvview.cpp`
- `c:\harn\gg\drv\mainfrm.cpp`
- `c:\harn\gg\drv\barcodereaddlg.cpp`
- `c:\harn\gg\drv\partdialog.cpp`
- `c:\harn\gg\drv\testboard.cpp`
- `c:\harn\gg\drv\testlist.cpp`
- `c:\harn\gg\drv\sock.cpp`
- các module `drawobj.cpp`, `drawtool.cpp`, `propset.cpp`, `commondoc.cpp`, `list.cpp`, `jpeg.cpp`.

### Tài nguyên

- 18 dialog, trong đó có đăng ký/manager password, Part Select, barcode input, test history, debug simulator, sample load/save.
- 16 JPG: `BACKJPG`, `STARTJPG`, `TESTJPG`, `PASSJPG`, `FAULTJPG`, `CHECKJPG`, `LOADJPG`, `UNLOADJPG`, `RESIJPG`, `CAPAJPG`, splash/background.
- 12 WAVE: `START`, `STAGEOK`, `TESTPOINT`, `DINGDONG*`, `CLICK`, `TICK`, v.v.
- 60 icon, 28 bitmap, 17 cursor, 30 group icon, 1 menu, 1 accelerator.
- `TEXT/TDB`: schema DB và chú giải `TimeRec`; đây là tài nguyên gần mã nguồn nhất.

### Mức độ có thể phục dựng mã nguồn

EXE đã bỏ COFF symbols và không kèm PDB, nên không thể lấy lại nguyên bản tên mọi hàm/biến hoặc source C++. Có thể decompile thành C-like code và gán tên lại dựa trên RTTI, dialog IDs, strings, SQL và PDB/source paths. Nếu tìm được đúng `Htdrv.pdb` GUID `{38ECF801-25D6-4158-A29D-F3DBBD4F5392}`, age 10, chất lượng phục dựng tên hàm sẽ tăng rất lớn.

## 8. Artifact đã trích xuất

- `pe_summary.json`: header, sections, import/delay-import, debug info và index tài nguyên.
- `strings_ascii.tsv`, `strings_cp949.tsv`, `strings_utf16.tsv`: chuỗi kèm file offset/RVA/VA.
- `resource_index.tsv`: danh sách tài nguyên và file đã tách.
- `resource_strings.tsv`: MFC string table kèm ID.
- `dialogs.json`: caption, control ID, class và text của 18 dialog.
- `resources/TEXT_TDB_1042.txt`: schema DB/chú giải TimeRec nguyên bản CP949; `TEXT_TDB_1042_utf8.txt` là bản chuyển UTF-8 dễ đọc.
- `resources/*.jpg`, `*.wav`, `*.dib`, `*.bin`: tài nguyên nhị phân đã tách.

## 9. Kết luận ngắn

Đây là harness/circuit tester x86 đời cũ, viết bằng VC++/MFC, dùng FTDI cho card I/O, VISA/SCPI cho thiết bị đo, COM serial cho barcode, DAO/Jet cho DB và có thể nối SQL Server/TMES. Cơ chế 10 card là 640 điểm I/O, đọc theo 40 word 16-bit có lọc ổn định. Đổi mã hàng đi qua document `.tht` và cập nhật quan hệ File–Part–Test trong DB. Phần lớn tên lớp, cấu hình, SQL, schema, resource và đường dẫn source đã được phục hồi; phần còn thiếu chủ yếu là tên hàm C++ nội bộ do không có PDB.
