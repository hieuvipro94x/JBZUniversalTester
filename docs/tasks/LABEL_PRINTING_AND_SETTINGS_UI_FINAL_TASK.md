# TASK — Harden PASS Label Printing + Fix Settings Timing/Maintenance UI

## Mục tiêu chung

Thực hiện hai nhóm công việc trong cùng một task:

1. Review và harden toàn bộ chức năng IN TEM sau khi sản phẩm PASS để đảm bảo 1 PASS cycle = đúng tem của đúng sản phẩm/model, không in nhầm, không in lặp, có traceability.
2. Chỉnh lại giao diện Settings phần “THỜI GIAN QUÉT / XÁC NHẬN LỖI / BẢO TRÌ” để khôi phục viền, căn chỉnh và khả năng đọc, nhưng không thay đổi logic/binding/default hiện tại.

Bắt buộc đọc `AGENTS.md` trước khi sửa.

Không audit toàn repository. Chỉ trace các file/luồng liên quan trực tiếp.

Không thay đổi protocol D2XX/UART, PASS/FAIL decision, ProductRemoved, JIG/relay, Probe/TestPin hoặc business logic không liên quan.

---

# PHẦN A — HARDEN CHỨC NĂNG IN TEM SAU PASS

## A1. Invariant production

Bắt buộc đảm bảo:

`1 confirmed PASS cycle = 1 first-print transaction`

Tem phải:
- đúng mã hàng/model;
- đúng dữ liệu cấu hình;
- đúng barcode/QR/serial/lot;
- không dùng dữ liệu model cũ/mới sai cycle;
- không in duplicate;
- không in khi FAIL;
- không in khi Probe/TestPin;
- không in khi Jig/Probe Contact Unstable;
- không in từ stale callback;
- không in từ cycle bị cancel.

## A2. Trace flow hiện tại

Chỉ trace luồng:

`PASS confirmed`
→ cycle identity
→ label/profile data
→ barcode/QR/sequence
→ printer
→ print status
→ history/traceability

Xác định rõ:
- trigger in hiện tại nằm ở đâu;
- D2XX lấy label config từ `.tht` hay profile/config nào;
- UART lấy label config từ `.model/.setup` hay runtime profile nào;
- printer name;
- template;
- copies/pass;
- width/height;
- orientation;
- barcode/QR rule;
- sequence/serial;
- LOT/date;
- part number/model.

Không tự suy đoán field nếu source/config hiện tại không có.

## A3. Không dùng CurrentModel mutable để in sau PASS

Kiểm tra có pattern nguy hiểm kiểu:

`PASS A`
→ operator đổi model B
→ print job đọc `CurrentModel`
→ in tem B cho sản phẩm A

Nếu có, harden bằng snapshot/print request thuộc đúng cycle.

Semantic gợi ý:

`LabelPrintRequest`
- CycleId
- PartNumber
- ModelKey
- Lot
- Serial/Sequence
- BarcodeValue
- ProductionDate
- Template/Profile
- Printer
- Copies

Print job phải dùng snapshot của PASS cycle, không đọc lại UI/current model mutable sau đó.

## A4. Idempotency / chống in lặp

Mỗi PASS cycle chỉ được first-print một lần.

Có state/guard tương đương:
- NotRequested
- Pending
- Printed
- Failed
- Unknown

Nếu cùng PASS callback tới nhiều lần:
→ vẫn chỉ có một first-print transaction.

Không retry printer timeout mù quáng nếu có khả năng tem vật lý đã được in.

## A5. Auto Print

Review setting `Tự in khi PASS`.

Nếu bật:
`PASS confirmed`
→ create print request
→ print
→ update PrintStatus

Nếu tắt:
→ không tự in.

Review `Số tem / PASS`.

Production hiện tại ưu tiên:
`1 sản phẩm PASS = 1 tem`

Nếu một số model tương lai cần nhiều tem:
- phải là config rõ ràng;
- không để global + model config cộng dồn gây duplicate.

## A6. Printer failure

Nếu printer offline/hết tem/lỗi driver/timeout/spooler lỗi:
- không đổi Product Test Result từ PASS thành FAIL;
- tách `TestResult` và `PrintStatus`;
- không block UI thread;
- operator phải nhận thông báo rõ;
- không tạo cycle mới chỉ vì retry print.

Nếu flow production yêu cầu phải in tem trước khi tháo sản phẩm, review khả năng state:

`PASS_CONFIRMED`
→ `LABEL_PENDING`
→ `LABEL_PRINTED`
→ `PRODUCT_REMOVAL`

Nhưng không tự thay đổi lifecycle lớn nếu chưa xác minh flow hiện tại.

## A7. Barcode / QR / Sequence

Xác định rule hiện tại từ source/config thật.

Không hard-code theo một bản vẽ mẫu cho mọi model.

Barcode/QR/sequence phải:
- đúng model;
- deterministic theo rule;
- không duplicate vì race;
- không reuse sản phẩm trước;
- không quay lại sequence cũ sau restart;
- không phụ thuộc UI textbox stale.

Nếu sequence increment:
- phải atomic;
- không để hai callback lấy cùng sequence.

## A8. Per-model label configuration

Mỗi mã hàng/model có thể khác:
- text lines;
- barcode/QR rule;
- size;
- orientation;
- printer;
- template;
- date format;
- lot/sequence format;
- copies.

D2XX và UART có thể lấy config khác nhau nhưng phải map đúng vào cùng luồng print PC-side.

Không đổi protocol để phục vụ in tem.

## A9. History / traceability

Mỗi PASS cycle nên trace được, nếu architecture hiện tại cho phép:
- CycleId
- Timestamp
- PartNumber
- Model
- TestResult
- Lot
- Serial/Sequence
- BarcodeValue
- LabelProfile/Template
- PrintStatus
- PrintTimestamp
- Printer
- Copies
- ReprintCount

Reprint phải phân biệt với first print nếu flow hỗ trợ.

Không biến reprint thành một PASS cycle mới.

## A10. Reprint

Nếu có manual reprint:
- đánh dấu rõ là REPRINT;
- giữ original label identity;
- log reprint time;
- operator/admin nếu hệ thống có;
- không tăng production count;
- không tăng PASS count;
- không làm thay đổi test result.

## A11. Settings hiện có liên quan tem

Rà soát section `TEM / MÁY IN / MODEL GẦN NHẤT`.

Các field như:
- Máy in Windows
- Tự in khi PASS
- BaudRate COM
- Số tem / PASS
- Timeout in
- Chiều rộng tem
- Chiều cao tem
- Định dạng EPL
- Thư mục lịch sử
- File THT lần cuối

Xác định field đang dùng/legacy/backend-specific/printer-specific.

Không xóa field chỉ vì có vẻ cũ nếu chưa trace usage.

Nếu `File THT lần cuối` không còn phù hợp cho cả `.tht` và `.model`, có thể đổi label UI thành `Mã hàng/model gần nhất` hoặc tương đương nhưng không phá persistence/backward compatibility.

## A12. Software test cases cho in tem

Kiểm tra:
1. PASS model A → đúng 1 print request A.
2. PASS model B → đúng label B.
3. FAIL → không print.
4. Probe/TestPin → không print.
5. Jig contact unstable → không print.
6. Duplicate PASS callback → chỉ 1 first-print.
7. Đổi model ngay sau PASS → tem cycle cũ vẫn đúng model cũ.
8. Printer offline → PASS giữ nguyên, PrintStatus lỗi.
9. Restart app → sequence không duplicate.
10. AutoPrint OFF → không tự in.
11. Copies = 1 → đúng 1 copy.
12. D2XX `.tht` → đúng profile.
13. UART `.model/.setup` → đúng profile.
14. History ↔ barcode ↔ PASS cycle khớp.

Không claim hardware verified khi chưa test printer/JIG thật.

---

# PHẦN B — FIX SETTINGS UI: THỜI GIAN QUÉT / XÁC NHẬN LỖI / BẢO TRÌ

## B1. Mục tiêu

Panel hiện tại bị mất/nhạt viền và các textbox khó nhìn ranh giới.

Chỉ chỉnh UI/XAML/style.

Không thay đổi:
- binding;
- ViewModel;
- command;
- setting name;
- validation;
- default value;
- timing logic;
- Probe maintenance logic.

## B2. Viền panel ngoài

Section `THỜI GIAN QUÉT / XÁC NHẬN LỖI / BẢO TRÌ` phải có:
- BorderThickness = 1;
- BorderBrush đủ rõ trên nền hiện tại;
- Padding đồng đều;
- CornerRadius nhẹ nếu style app đang dùng;
- header không sát viền.

## B3. TextBox

Tất cả textbox trong section phải:
- có viền đầy đủ 4 cạnh;
- BorderThickness = 1;
- chiều cao đồng nhất;
- padding đồng nhất;
- alignment đồng nhất;
- không có textbox nhìn như “mất viền”.

Giữ nguyên các field hiện tại và giá trị/default.

## B4. Hai cột

Căn lại:
- label thẳng hàng;
- textbox thẳng hàng;
- khoảng cách dòng đều;
- baseline rõ;
- không để field sát nhau;
- không thay đổi layout tổng thể lớn nếu không cần.

Ưu tiên giữ layout 2 cột hiện tại vì operator đã quen.

## B5. Độ trễ cuộn / trang

Field có 2 ô giá trị:
- giữ nguyên 2 ô;
- spacing đều;
- border rõ;
- cùng height với các textbox khác nếu phù hợp.

Không thay đổi semantics của 2 giá trị.

## B6. Checkbox

Hai checkbox:
- Hiển thị tiêu đề
- Hiển thị connector

phải:
- căn thẳng với label;
- spacing hợp lý;
- không lệch baseline.

Không thay đổi binding.

## B7. Style isolation

Nếu ResourceDictionary/global TextBox style hiện tại là nguyên nhân làm mất border:
- ưu tiên tạo style riêng cho panel Settings này;
- hoặc style scoped ở container/view.

Không sửa global style nếu có nguy cơ làm thay đổi UI toàn app.

## B8. Không redesign quá mức

Không chia lại toàn bộ UI thành custom control mới nếu chỉ cần sửa border/alignment.

Không tạo GroupBox mới nếu điều đó làm thay đổi layout lớn mà không cần.

Mục tiêu:
giữ giao diện hiện tại, nhưng nhìn rõ, sạch, chuyên nghiệp và dễ nhập liệu hơn.

---

# PHẦN C — PERFORMANCE / STABILITY

Cả hai thay đổi không được:
- block UI;
- thêm timer/polling không cần;
- tạo event leak;
- làm TestViewModel nặng thêm không cần thiết;
- query history nặng trong hot loop;
- in đồng bộ trên UI thread;
- tạo duplicate callback.

Nếu logic print hiện đang nằm quá nhiều trong TestViewModel:
- chỉ tách helper/service nhỏ nếu việc đó giảm rủi ro rõ ràng;
- không đại refactor TestViewModel trong task này.

---

# PHẦN D — BUILD / VERIFICATION

Sau khi sửa:
1. `dotnet clean`
2. `dotnet restore`
3. `dotnet build -c Release`
4. verify script theo `AGENTS.md`
5. kiểm tra XAML compile
6. kiểm tra WPF binding output
7. chạy relevant tests.

Nếu NuGet restore fail do sandbox/TLS:
- không sửa source để né;
- build ngoài sandbox nếu workflow cho phép;
- chỉ báo PASS khi compile thật sự PASS.

---

# PHẦN E — HARDWARE TEST CẦN LÀM Ở CÔNG TY

Không claim production-ready trước khi hardware verification.

Khi có printer/JIG thật, cần test:
- load model thật;
- PASS thật;
- đúng 1 tem;
- đúng model;
- barcode/QR scan được;
- barcode value đúng rule;
- text đúng bản vẽ;
- size/orientation đúng;
- 10 PASS liên tiếp → đúng 10 tem;
- không duplicate;
- đổi model → tem đổi đúng;
- printer disconnect/reconnect;
- hết tem;
- reprint;
- restart app;
- history khớp tem vật lý.

Settings UI:
- nhập/sửa giá trị;
- save;
- restart;
- reload đúng;
- không textbox nào mất viền;
- không binding error.

---

# PHẦN F — PRIORITY

## BLOCKER
- in sai model;
- duplicate label;
- FAIL vẫn in;
- barcode/sequence duplicate;
- stale cycle/model;
- print làm sai PASS/FAIL;
- UI Settings hỏng binding do sửa style.

## HIGH
- printer failure treo UI;
- sequence mất sau restart;
- reprint không distinguish;
- Copies sai;
- style global gây regression màn hình khác.

## MEDIUM
- wording/UI spacing;
- legacy field naming;
- maintainability nhỏ.

Không refactor LOW-priority nếu tăng regression risk.

---

# PHẦN G — GIT WORKFLOW

Tuân theo `AGENTS.md`.

Trước sửa:
- `git status`
- `git fetch origin`
- sync nếu an toàn.

Sau sửa:
- build/test;
- review diff;
- chỉ stage file thuộc task;
- fetch lại;
- commit;
- push nếu an toàn.

Không force push.

Có thể tách commit:
- `fix: harden pass label printing and traceability`
- `fix: restore timing settings panel borders`

---

# PHẦN H — BÁO CÁO CUỐI

Báo ngắn:

## Label Printing
- print trigger:
- D2XX config source:
- UART config source:
- snapshot/idempotency:
- barcode/sequence:
- printer failure handling:
- history/traceability:

## Settings UI
- XAML/style file:
- border fix:
- textbox style:
- global impact:

## Verification
- build:
- tests:
- hardware not yet verified:

## Git
- branch:
- commits:
- push:
- sync status:
