TASK — Product Picker + Operator Fault Display + Customer History Terminology

Mục tiêu chung

Thực hiện các thay đổi UI/UX và cách trình bày lỗi theo các yêu cầu dưới đây.

Bắt buộc đọc AGENTS.md trước khi sửa.

Không audit lại toàn bộ repository. Chỉ đọc các file/source liên quan trực tiếp đến task.

Không thay đổi protocol D2XX/UART, PASS/FAIL decision, JIG/relay, ProductRemoved lifecycle, firmware semantics hoặc business logic không liên quan.

1. MAINWINDOW — CHỌN MÃ HÀNG

1.1. Bỏ custom WPF product picker hiện tại

Cửa sổ custom “Chọn mã hàng” hiện tại khó sử dụng và không đẹp.

Yêu cầu:

Bỏ/không sử dụng custom WPF file picker đã tạo cho chức năng “Chọn mã hàng”.

Khôi phục lại Windows standard Open File Dialog như giao diện cũ.

Không tạo lại một custom file explorer riêng.

Không thay đổi luồng load model/backend hiện tại chỉ vì đổi dialog.

1.2. File filter

Chức năng “Chọn mã hàng” chỉ cho phép chọn:

*.tht

*.model

Không hiển thị/chọn:

*.json

*.jbzproduct.json

*.setup

các file cấu hình khác.

Filter tương đương:

Mã hàng JBZ (*.tht;*.model)|*.tht;*.model

Nếu có thêm filter kiểu “All supported files” thì vẫn chỉ gồm .tht và .model.

1.3. Backend mapping

Giữ nguyên semantics:

.tht → flow D2XX hiện tại.

.model → flow UART TTL hiện tại.

Không:

convert .tht <-> .model;

đổi Product Bundle semantics;

đổi protocol;

đổi logic test.

1.4. Dialog behavior

Windows standard Open File Dialog phải:

được mở theo kiểu modal với MainWindow làm owner;

khi dialog mở, MainWindow phía sau không thao tác được;

xuất hiện ở giữa/relative-to-owner một cách ổn định;

nút X của dialog có behavior tương đương Cancel/Hủy;

không minimize/maximize.

Không cho kéo dialog sang vị trí khác

Tôi muốn standard dialog không thể bị người dùng kéo bằng chuột sang vị trí khác.

Ưu tiên:

Giữ Windows standard Open File Dialog.

Nếu API chuẩn không hỗ trợ khóa vị trí trực tiếp, dùng giải pháp Win32 hook/subclass tối thiểu, an toàn để:

giữ dialog ở vị trí đã center theo owner;

ngăn/hoàn tác thao tác di chuyển bằng chuột;

không phá hành vi chọn file;

không biến dialog thành custom WPF window.

Không dùng:

custom file browser mới;

hack polling/timer liên tục nếu không cần;

giải pháp làm treo UI;

giải pháp ảnh hưởng keyboard navigation/accessibility của dialog.

Nếu việc khóa di chuyển trên standard dialog có giới hạn kỹ thuật, phải báo rõ trong kết quả; không được tự thay bằng custom dialog mà không nói.

1.5. Kích thước

Ưu tiên kích thước compact hơn giao diện cũ nếu có thể kiểm soát an toàn bằng Win32.

Tuy nhiên:

không hy sinh standard Windows dialog chỉ để ép kích thước;

nếu kích thước standard dialog không thể kiểm soát ổn định mà không hack rủi ro, giữ size chuẩn của Windows;

ưu tiên usability/stability hơn ép size.

2. FAIL DIALOG — HIỂN THỊ LỖI CHI TIẾT CHO OPERATOR

Popup “XỬ LÝ HÀNG KHÔNG ĐẠT” không chỉ hiển thị thông báo chung.

Phải hiển thị rõ:

lỗi gì;

lỗi ở đâu;

tiêu chuẩn là gì;

thực tế là gì;

sai lệch/kết luận nếu có.

Nguồn dữ liệu phải lấy từ structured data hiện có trong TestEngine/FaultEngine/TestViewModel/ProductModel hoặc lớp tương đương.

Không tự suy đoán dữ liệu không tồn tại.

Không parse chuỗi bằng regex nếu structured fault data đã có.

3. THUẬT NGỮ OPERATOR — TIẾNG VIỆT

Trong giao diện vận hành, không dùng từ “mong muốn”.

Chuẩn hóa:

Màu mong muốn → Màu tiêu chuẩn

Vị trí mong muốn → Vị trí tiêu chuẩn

Kết nối mong muốn → Kết nối tiêu chuẩn

Giá trị mong muốn → Giá trị tiêu chuẩn

Điện trở mong muốn → Giá trị điện trở tiêu chuẩn

Actual → Thực tế hoặc Giá trị đo

Deviation → Sai lệch

Các fault label dành cho operator:

OPEN CIRCUIT → HỞ MẠCH

SHORT CIRCUIT → CHẬP MẠCH

WRONG POSITION → SAI VỊ TRÍ

WRONG WIRE COLOR → SAI MÀU DÂY

TERMINAL MISPOSITION → TERMINAL SAI VỊ TRÍ

CROSSED TERMINALS → ĐẢO VỊ TRÍ TERMINAL

WRONG CONNECTION → SAI KẾT NỐI

RESISTANCE OUT OF SPECIFICATION → ĐIỆN TRỞ KHÔNG ĐẠT

VOLTAGE OUT OF SPECIFICATION → ĐIỆN ÁP KHÔNG ĐẠT

CURRENT OUT OF SPECIFICATION → DÒNG ĐIỆN KHÔNG ĐẠT

Không hiển thị enum/debug text nội bộ trực tiếp cho operator nếu có thể map sang tiếng Việt.

4. PHÂN LOẠI LỖI CHI TIẾT

Không gộp mọi lỗi thành “Sai dây” nếu dữ liệu hiện tại đủ để phân biệt.

4.1. HỞ MẠCH

Hiển thị:

connector/chân đầu A;

connector/chân đầu B;

màu dây tiêu chuẩn nếu có;

trạng thái thực tế: không có kết nối.

Ví dụ:

HỞ MẠCH

Kết nối tiêu chuẩn:CN1 - Chân 4 ↔ CN3 - Chân 6

Màu dây: TRẮNG

Thực tế: KHÔNG CÓ KẾT NỐI

4.2. SAI VỊ TRÍ DÂY / TERMINAL

Hiển thị:

dây/terminal;

màu tiêu chuẩn;

vị trí tiêu chuẩn;

vị trí thực tế.

Ví dụ:

TERMINAL SAI VỊ TRÍ

Dây: W12Màu tiêu chuẩn: ĐỎVị trí tiêu chuẩn: CN1 - Chân 3Vị trí thực tế: CN1 - Chân 5

4.3. SAI MÀU DÂY

Hiển thị:

connector/chân;

màu tiêu chuẩn;

màu thực tế.

Ví dụ:

SAI MÀU DÂY

Vị trí: CN2 - Chân 7Màu tiêu chuẩn: XANHMàu thực tế: ĐỎ

4.4. ĐẢO VỊ TRÍ TERMINAL

Nếu structured data đủ để xác định, hiển thị rõ hai terminal bị đảo.

Không tự suy đoán trường hợp “đảo terminal” nếu engine không có đủ dữ liệu.

4.5. CHẬP MẠCH

Hiển thị rõ hai vị trí đang có kết nối ngoài tiêu chuẩn.

Ví dụ:

CHẬP MẠCH

Phát hiện kết nối ngoài tiêu chuẩn:CN4 - Chân 2 ↔ CN6 - Chân 9

4.6. ĐIỆN TRỞ KHÔNG ĐẠT

Hiển thị:

vị trí đo;

giá trị tiêu chuẩn;

Min;

Max;

giá trị đo thực tế;

đơn vị;

sai lệch;

kết luận dưới giới hạn / trên giới hạn.

Ví dụ:

ĐIỆN TRỞ KHÔNG ĐẠT

Vị trí: CN3 Chân 4 ↔ CN5 Chân 2Giá trị tiêu chuẩn: 1.00 kΩGiới hạn: 0.95 – 1.05 kΩGiá trị đo: 1.37 kΩSai lệch: +0.37 kΩKết luận: CAO HƠN GIỚI HẠN

5. FAIL DIALOG UI

Popup “XỬ LÝ HÀNG KHÔNG ĐẠT” phải:

giữ header chính;

dòng phụ hiển thị:

PHÁT HIỆN 1 LỖI

hoặc PHÁT HIỆN N LỖI;

vùng nội dung hiển thị danh sách fault chi tiết;

danh sách dài phải scroll;

không che nút XÁC NHẬN;

dễ đọc cho operator;

ưu tiên tiếng Việt;

không hiển thị debug/internal identifiers nếu operator không cần.

Mỗi fault item nên theo cấu trúc:

LOẠI LỖIVị trí: ...Tiêu chuẩn: ...Thực tế: ...Sai lệch/Kết luận: ...

Không đổi nghiệp vụ của nút XÁC NHẬN.

6. OPERATOR UI VS CUSTOMER HISTORY/EXPORT

6.1. Nguyên tắc kiến trúc

Cùng một structured fault/result object phải dùng được cho:

Operator UI → tiếng Việt.

Customer history/report/export → tiếng Anh kỹ thuật chuẩn.

Không lưu câu tiếng Việt làm canonical source-of-truth rồi dịch ngược sang English.

Ưu tiên lưu dữ liệu có cấu trúc:

FaultType

Connector

Pin

WireId

StandardColor

ActualColor

StandardPosition

ActualPosition

NominalValue

MinLimit

MaxLimit

MeasuredValue

Unit

Deviation

Timestamp

PartNumber

SerialNumber

LotNumber

Station

Backend

Chỉ thêm field thật sự có nguồn dữ liệu.

Nếu schema hiện tại chưa hỗ trợ đầy đủ:

giữ backward compatibility;

không migration destructive;

không phá history cũ.

7. CUSTOMER HISTORY / REPORT / EXPORT — ENGLISH

Khi xuất lịch sử/báo cáo gửi khách hàng phải dùng English technical terminology chuẩn.

Mapping:

OpenCircuit → OPEN CIRCUIT

ShortCircuit → SHORT CIRCUIT

WrongPosition → INCORRECT WIRE POSITION

WrongWireColor → INCORRECT WIRE COLOR

TerminalMisposition → TERMINAL MISPOSITION

CrossedTerminals → CROSSED TERMINALS

WrongConnection → INCORRECT CONNECTION

ResistanceOutOfRange → RESISTANCE OUT OF SPECIFICATION

VoltageOutOfRange → VOLTAGE OUT OF SPECIFICATION

CurrentOutOfRange → CURRENT OUT OF SPECIFICATION

Unknown → UNCLASSIFIED FAULT

Customer-facing labels ưu tiên:

Test Result

Fault Type

Fault Location

Standard

Actual

Standard Position

Actual Position

Standard Wire Color

Actual Wire Color

Nominal Value

Lower Limit

Upper Limit

Measured Value

Tolerance

Deviation

Assessment

Connector

Terminal

Pin

Wire

Timestamp

Part Number

Serial Number

Lot Number

Operator

Test Station

Không dùng các từ customer-facing kiểu:

Bad

Wrong Resistance

Error Resistance

NG

nếu có thể dùng thuật ngữ kỹ thuật chuẩn hơn.

PASS/FAIL trong dữ liệu/export giữ:

PASS

FAIL

Operator UI có thể map:

PASS → ĐẠT

FAIL → KHÔNG ĐẠT

8. CUSTOMER REPORT EXAMPLES

8.1. Terminal misposition

Operator:

TERMINAL SAI VỊ TRÍ

Dây: W12Màu tiêu chuẩn: ĐỎVị trí tiêu chuẩn: CN1 - Chân 3Vị trí thực tế: CN1 - Chân 5

Customer:

Fault Type: TERMINAL MISPOSITIONWire: W12Standard Wire Color: REDStandard Position: CN1 - Pin 3Actual Position: CN1 - Pin 5

8.2. Open circuit

Customer:

Fault Type: OPEN CIRCUITStandard Connection: CN1 Pin 4 ↔ CN3 Pin 6Standard Wire Color: WHITEActual Condition: NO CONTINUITY

8.3. Resistance

Customer:

Fault Type: RESISTANCE OUT OF SPECIFICATIONLocation: CN3 Pin 4 ↔ CN5 Pin 2Nominal Value: 1.00 kΩLower Limit: 0.95 kΩUpper Limit: 1.05 kΩMeasured Value: 1.37 kΩDeviation: +0.37 kΩAssessment: ABOVE UPPER LIMIT

9. DISPLAY/FORMATTER LAYER

Ưu tiên một mapping/formatter tập trung, ví dụ:

FaultDisplayFormatter

FaultTextProvider

resource/localization layer hiện có

Có khả năng render:

vi-VN cho operator;

en-US cho customer report/export.

Không duplicate fault classification business logic giữa hai nơi.

Không rải mapping Việt/Anh ở nhiều ViewModel nếu tránh được.

10. COLOR MAPPING

Không string-replace tùy tiện.

Giữ raw/original color value nếu cần traceability.

Ví dụ display mapping:

Red → ĐỎ / RED

Blue → XANH DƯƠNG / BLUE

Green → XANH LÁ / GREEN

Yellow → VÀNG / YELLOW

Black → ĐEN / BLACK

White → TRẮNG / WHITE

Brown → NÂU / BROWN

Orange → CAM / ORANGE

Gray/Grey → XÁM / GRAY

Violet/Purple → TÍM / VIOLET

Nếu model dùng mã màu riêng thì giữ code gốc và map display riêng.

11. SCOPE / SAFETY

Không thay đổi ngoài phạm vi:

D2XX protocol

UART protocol

firmware commands

PASS/FAIL decision

relay/JIG

ProductRemoved

TESTPIN

test sequence

model compiler semantics

Không refactor rộng.

Không đổi public API/schema/protocol chỉ để đổi text UI.

Không đổi internal enum/property name nếu rename có nguy cơ phá compatibility.

12. VERIFICATION

Sau khi sửa, kiểm tra:

Product picker

standard Windows Open File Dialog được dùng lại;

custom WPF picker không còn được dùng;

modal với MainWindow;

chỉ .tht và .model;

.json/.jbzproduct.json/.setup không xuất hiện;

.tht load đúng D2XX;

.model load đúng UART;

X tương đương Cancel;

dialog giữ vị trí theo yêu cầu;

không kéo dialog sang vị trí khác nếu implementation hỗ trợ an toàn.

FAIL dialog

1 Open Circuit;

1 Short Circuit;

Wrong Position;

Wrong Wire Color;

Terminal Misposition;

Crossed Terminals nếu có dữ liệu thật;

Resistance below Min;

Resistance above Max;

nhiều fault cùng lúc;

field thiếu dữ liệu;

list dài scroll được;

Probe không trở thành FAIL.

Language

Operator UI: tiếng Việt.

Không còn text “mong muốn” trong operator UI, trừ nơi có lý do kỹ thuật rõ ràng.

Customer export/history report: English.

Không dịch ngược từ câu tiếng Việt để tạo customer report.

Cùng một fault giữ cùng semantic code.

Build

Build Release.

Kiểm tra XAML compile.

Kiểm tra relevant tests.

Kiểm tra WPF binding errors liên quan.

13. GIT WORKFLOW

Tuân theo AGENTS.md.

Nếu verification PASS:

review diff;

chỉ stage file thuộc task;

fetch remote;

commit;

push nếu an toàn.

Không force push.

Commit message gợi ý:

feat: improve product picker and fault reporting

Nếu task quá lớn để một commit coherent, có thể tách:

fix: restore standard product file dialog

feat: show detailed operator fault information

feat: add customer-facing English fault reporting

14. COMPLETION REPORT

Cuối task chỉ báo ngắn:

files changed;

standard dialog implementation;

file filter;

cách khóa/giữ vị trí dialog;

operator fault types hỗ trợ;

customer English mappings;

history hiện lưu structured data hay formatted text;

backward compatibility;

build/test result;

commit hash/message;

push status;

các phần chưa xác minh bằng hardware thật.