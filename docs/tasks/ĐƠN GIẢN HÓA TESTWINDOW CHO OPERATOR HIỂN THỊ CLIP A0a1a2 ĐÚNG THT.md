TASK — ĐƠN GIẢN HÓA TESTWINDOW CHO OPERATOR + HIỂN THỊ CLIP A0/a1/a2 ĐÚNG THT

PROJECT: JBZUniversalTester



============================================================

MỤC TIÊU

============================================================



Sửa phần hiển thị Production TestWindow để công nhân chỉ nhìn thấy

thông tin cần thiết để thao tác.



KHÔNG được hiển thị các thuật ngữ kỹ thuật nội bộ như:



Mạng 1

Mạng 2

tới IO2

IO201 <-> IO202

IO1 ↔ IO2

SOURCE

TARGET

NetworkKey

A0(IO201) -> a1 -> IO202

A0(IO201) -> a2 -> IO203



Những dữ liệu này vẫn được phép tồn tại trong backend để test,

nhưng KHÔNG xuất hiện trên màn hình Production.



============================================================

1\. KHÔNG SỬA LOGIC ĐIỆN

============================================================



Task này chủ yếu sửa PRESENTATION.



KHÔNG thay:



\- SOURCE/TARGET decoder;

\- expected electrical network;

\- short/wrong detection;

\- scan I/O;

\- FTDI;

\- CLIP latch memory;

\- resistance;

\- relay;

\- PASS/FAIL engine.



Không xóa NetworkKey khỏi backend chỉ vì không hiển thị.



============================================================

2\. MỖI I/O PHẢI LÀ MỘT DÒNG RIÊNG

============================================================



Sai hiện tại:



IO201 <-> IO202



hoặc:



IO201 ↔ IO202



trong cột I/O.



PHẢI sửa thành mỗi physical endpoint một row.



Ví dụ THT CLIP:



A0 = IO201

a1 = IO202

a2 = IO203

a3 = IO204

a4 = IO205

a5 = IO206



TestWindow phải hiển thị:



I/O     TÊN DÂY

201     A0

202     a1

203     a2

204     a3

205     a4

206     a5



Không:



201 <-> 202

201 <-> 203

201 <-> 204



============================================================

3\. CỘT I/O CHỈ HIỂN THỊ SỐ

============================================================



Ví dụ:



Internal:

IO201



Production UI:



201



Không cần prefix:



IO



nếu giao diện hiện tại của tester dùng số.



Đặc biệt không được hiển thị:



IO201 <-> IO202



Chỉ:



201



hoặc:



202



theo đúng row endpoint.



============================================================

4\. CLIP A0/a1/a2... HIỂN THỊ THEO TÊN THỰC TẾ

============================================================



Trong THT có CLIP:



A0

a1

a2

a3

a4

a5

a8

a9

a10

a11

a13

a14

a15

a16

a17

...



Cột:



TÊN DÂY



phải hiển thị đúng:



A0

a1

a2

a3

a4

a5

...



Giữ nguyên chữ hoa/thường từ THT nếu parser có thể giữ được.



Không tự biến thành:



CLIP a1

CLIP a2



trong cột TÊN DÂY nếu người vận hành chỉ cần:



a1

a2



Nếu có cột LOẠI thì có thể hiển thị:



CLIP



ở cột LOẠI.



Nhưng TÊN DÂY chỉ là:



A0

a1

a2

...



============================================================

5\. A0 COMMON KHÔNG ĐƯỢC GỘP VỚI a1/a2

============================================================



Backend có thể hiểu:



A0(IO201) ↔ a1(IO202)



nhưng UI phải tách:



row:

I/O = 201

Tên dây = A0



row:

I/O = 202

Tên dây = a1



Không biến thành một dòng:



IO201 <-> IO202



============================================================

6\. XÓA NỘI DUNG "MẠNG 1 • TỚI IO..."

============================================================



Production không được hiển thị:



Mạng 1 • tới IO2



Mạng A0 • tới IO202



Mạng 1 • IO1↔IO2



Không operator nào cần hiểu NetworkKey.



Nếu cột KẾT NỐI MẠNG vẫn đang tồn tại,

REMOVE hoàn toàn cột này khỏi TestWindow.



Không chỉ xóa text.



Xóa DataGridColumn khỏi XAML.



============================================================

7\. TRẠNG THÁI CHỈ CẦN 2 VĂN BẢN ĐƠN GIẢN

============================================================



Đối với row đang chờ thao tác:



CHỜ KẾT NỐI



Khi contact đã được xác nhận:



ĐÃ KẾT NỐI



Không hiển thị:



THÔNG MẠCH OK • Mạng 1...

SOURCE IO...

TARGET IO...

tới IO...



Operator chỉ cần biết:



CHỜ KẾT NỐI



hoặc:



ĐÃ KẾT NỐI



============================================================

8\. NẾU ROW PASS ĐANG ĐƯỢC AUTO-HIDE

============================================================



Giữ behavior business/UI hiện có:



normal endpoint:

connected confirmed

→ có thể biến mất theo thiết kế hiện tại.



CLIP:

confirmed once

→ latch

→ biến mất

→ không hiện lại cho đến ProductRemoved/NewCycle.



Nhưng trong thời gian row còn visible,

status chỉ được dùng:



CHỜ KẾT NỐI



hoặc:



ĐÃ KẾT NỐI



Không cần mô tả peer I/O.



============================================================

9\. CỘT DÂY DẬP NỐI

============================================================



Hiện đang có các text kỹ thuật kiểu:



A0(IO201) -> a1 -> IO202



A0(IO201) -> a2 -> IO203



A0(IO201) -> a3 -> IO204



KHÔNG được hiển thị kiểu này trên Production UI.



Xóa formatter/presentation đang tạo chuỗi đó.



Nếu THT có một giá trị "Dây dập nối" thực tế có ý nghĩa cho operator,

thì chỉ hiển thị raw production-friendly value đó.



Nếu không có giá trị thực tế cần thiết:



để trống.



Ví dụ CLIP:



DÂY DẬP NỐI = ""



Không tự sinh:



A0(IO201) -> a1 -> IO202



============================================================

10\. KHÔNG DÙNG CỘT DÂY DẬP NỐI ĐỂ DEBUG NETWORK

============================================================



Các thông tin:



A0 common

peer IO

NetworkKey

SOURCE/TARGET

canonical IO set



phải đưa vào:



Diagnostics

Log

Developer mode



KHÔNG đưa vào cột Dây dập nối.



============================================================

11\. DISPLAY MONG MUỐN CHO CLIP

============================================================



Ví dụ model:



A0 = IO201

a1 = IO202

a2 = IO203

a3 = IO204

a4 = IO205

a5 = IO206



TestWindow mong muốn:



TRẠNG THÁI      I/O   CONNECTOR   PIN   TÊN DÂY   DÂY DẬP NỐI

\----------------------------------------------------------------

CHỜ KẾT NỐI     201   ...         ...   A0

CHỜ KẾT NỐI     202   ...         ...   a1

CHỜ KẾT NỐI     203   ...         ...   a2

CHỜ KẾT NỐI     204   ...         ...   a3

CHỜ KẾT NỐI     205   ...         ...   a4

CHỜ KẾT NỐI     206   ...         ...   a5



KHÔNG:



IO201 <-> IO202

CLIP a1

A0(IO201) -> a1 -> IO202



============================================================

12\. SAU KHI a1 ĐƯỢC NHẬN

============================================================



Theo ClipMemory requirement hiện có:



A0 ↔ a1 confirmed



→ latch a1

→ row a1 được coi hoàn thành

→ auto-hide theo behavior hiện tại.



Không cần hiển thị:



ĐÃ KẾT NỐI • A0 → a1



Không cần peer IO.



============================================================

13\. A0 KHÔNG PHẢI LÀ "MẠNG"

============================================================



Không hiển thị:



Mạng A0



A0 chỉ là tên CLIP/common theo model.



UI:



Tên dây = A0



Backend:

có thể lưu common/reference semantics riêng.



============================================================

14\. SEARCH TOÀN PROJECT CÁC CHUỖI KHÔNG ĐƯỢC HIỂN THỊ

============================================================



Search:



rg -n --hidden --glob "!bin/\*\*" --glob "!obj/\*\*" `

"<->|↔|Mạng |tới IO|SOURCE|TARGET|A0\\\\(|ConnectionText|NetworkText|PeerIo|WireConnection|Dây dập nối|CLIP " .



Phân loại:



A. backend protocol/log:

GIỮ.



B. Production TestWindow presentation:

XÓA / SIMPLIFY.



Không được xóa SOURCE/TARGET decoder.



============================================================

15\. TRACE ĐÚNG PROPERTY TẠO "IO201 <-> IO202"

============================================================



Phải xác định property/method cụ thể.



Ví dụ nếu đang có:



DisplayIo =

&#x20;   $"IO{source} <-> IO{target}";



phải bỏ cách dựng này.



Mỗi endpoint row:



DisplayIo =

&#x20;   endpoint.IoNumber.ToString();



Không dùng peer I/O.



============================================================

16\. TRACE ĐÚNG PROPERTY TẠO DÂY DẬP NỐI

============================================================



Tìm formatter đang tạo:



A0(IO201) -> a1 -> IO202



Ví dụ có thể nằm ở:



BuildRows()

BuildClipRows()

WireConnectionText

SpliceDisplay

ConnectionDescription



Không tạo formatter mới chồng lên formatter cũ.



Sửa đúng source.



============================================================

17\. DATA MODEL BACKEND VẪN GIỮ ĐẦY ĐỦ

============================================================



Có thể vẫn giữ:



SourceIo

TargetIo

NetworkKey

PeerIo

ClipCommonIo



nếu engine cần.



Nhưng Production row không bind/display chúng.



Tách:



Business model



khỏi:



Operator display model.



============================================================

18\. KHÔNG SORT THEO TÊN DÂY

============================================================



Giữ requirement trước:



Không OrderBy(WireName).



Bảng được sắp theo:



Connector Group

→ Connector

→ Pin / OriginalThtOrder



Tên:



A0

a1

a2...



không dùng để alphabet sort toàn bảng.



============================================================

19\. NATURAL ORDER CHO CLIP NẾU CẦN

============================================================



Nếu trong cùng connector/clip group phải sắp theo clip number,

natural sort:



A0

a1

a2

a3

...

a9

a10

a11

...



Không:



a1

a10

a11

a2



Nhưng chỉ áp dụng trong đúng group nếu không phá OriginalThtOrder.



Ưu tiên order từ THT nếu đó là order production.



============================================================

20\. KHÔNG SỬA CLIP MEMORY

============================================================



Task này KHÔNG được phá logic:



a1 latched

→ hidden until product removed.



a2 latched

→ hidden until product removed.



Board reconnect:

không reset latch.



ProductRemoved/NewCycle:

reset toàn bộ CLIP.



============================================================

21\. PERFORMANCE

============================================================



Không rebuild toàn collection mỗi frame chỉ vì đổi text presentation.



Các display property như:



IoText

WireName

Connector

Pin



là dữ liệu model cố định.



Build một lần khi load model.



Chỉ status/visibility thay đổi realtime.



============================================================

22\. UI SAU SỬA PHẢI RÕ RÀNG CHO OPERATOR

============================================================



Operator chỉ cần hiểu:



LOẠI

I/O

CONNECTOR

PIN

TÊN DÂY

CỠ DÂY

MÀU

TRẠNG THÁI



Không cần hiểu:



Network

Source

Target

Peer

CanonicalKey



============================================================

23\. REGRESSION CLIP

============================================================



Input THT:



A0 IO201

a1 IO202

a2 IO203



Expected visual:



201 | A0

202 | a1

203 | a2



Không string nào chứa:



<->



↔



"tới IO"



"A0(IO201)"



============================================================

24\. REGRESSION NORMAL NETWORK

============================================================



Normal:



IO1

IO2

WireName=1



UI cũng phải là:



row 1:

I/O = 1

Tên dây = 1



row 2:

I/O = 2

Tên dây = 1



Không:



IO1 <-> IO2



============================================================

25\. REGRESSION STATUS

============================================================



Waiting:



CHỜ KẾT NỐI



Connected:



ĐÃ KẾT NỐI



Không thêm hậu tố.



============================================================

26\. REGRESSION DÂY DẬP NỐI

============================================================



CLIP:



Không được hiển thị:



A0(IO201) -> a1 -> IO202



Nếu không có raw operator-facing data:

cell phải trống.



============================================================

27\. BUILD

============================================================



dotnet clean



xóa:



bin

obj



dotnet build -c Debug

dotnet build -c Release



============================================================

28\. CODEX PHẢI BÁO

============================================================



ROOT CAUSE:

file/method nào đang tạo:

IO201 <-> IO202



ROOT CAUSE:

file/method nào đang tạo:

Mạng 1 • tới IO...



ROOT CAUSE:

file/method nào đang tạo:

A0(IO201) -> a1 -> IO202



FILES MODIFIED:

...



BACKEND NETWORK LOGIC MODIFIED:

NO



SOURCE/TARGET MODIFIED:

NO



CLIP MEMORY MODIFIED:

NO, trừ khi compiler cần adapter presentation.



FINAL COLUMNS:

liệt kê chính xác.



BUILD DEBUG:

PASS/FAIL



BUILD RELEASE:

PASS/FAIL



============================================================

TIÊU CHÍ HOÀN THÀNH

============================================================



Production TestWindow phải đơn giản:



I/O:

201

202

203

204...



Tên dây:

A0

a1

a2

a3...



Status:

CHỜ KẾT NỐI

ĐÃ KẾT NỐI



Không còn:



IO201 <-> IO202

Mạng 1

tới IO...

SOURCE

TARGET

A0(IO201) -> a1 -> IO202



Những thông tin kỹ thuật chỉ được phép tồn tại trong backend/Diagnostics/Log.

