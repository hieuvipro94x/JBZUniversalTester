# Master tự chuyển bước sau khi tháo mẫu — V2026.09.156

## Nguyên nhân

V155 đếm hai frame tháo Master trong `ProcessMasterEngineChangedOnUi`. Sau reset engine/restart scan, các frame rỗng giống nhau chỉ có thể phát `Changed` một lần. Sự kiện đó còn có thể đến trong lúc `_masterEjectInProgress` đang khóa thao tác. Những frame rỗng tiếp theo không phát `Changed`, nên Master giữ trạng thái hoàn thành mẫu cho đến khi có sự kiện khác.

Mô phỏng qua `FakeBoard.Publish` và router thực tế của ViewModel đã tái hiện: **12 frame rỗng hợp lệ, chỉ 1 Changed, vẫn EjectingBadMaster** khi chỉ chọn mẫu đạt + sai dây. Không gọi thủ công state machine sau mỗi frame.

## Thay đổi

- Đếm tháo Master trên worker nhận frame, kể cả frame không làm thay đổi topology/UI.
- Chỉ xác nhận từ hai frame Production hoàn chỉnh, khác sequence, cùng scan generation, đã được engine chấp nhận và không còn IO/kết nối thật.
- Bỏ qua frame trong thời gian lưu/mở JIG/restart scan; sau khi nhả guard phải nhận đủ frame mới.
- Chốt xác nhận tháo một lần; callback UI kiểm tra runtime, model, bước Master và CycleId.
- Hủy hiệu lực snapshot UI của mẫu cũ khi chuyển bước hoặc mở sản xuất.
- Không thêm delay, không thay đổi lệnh bo/relay, không thay đổi điều kiện đạt mẫu, không thay đổi schema/history hoặc tăng LOT/sản lượng của Master.

Mẫu đạt/mẫu NG trung gian được reset và chuyển sang yêu cầu mẫu kế tiếp đã chọn. Sau **mẫu cuối cùng**, JIG về OFF và tháo hết mẫu thì tự hiện **LẮP SẢN PHẨM**, không cần lắp một sản phẩm mới để kích hoạt reset. Vẫn khóa sản xuất nếu còn mẫu hoặc chưa hoàn tất các mẫu đã chọn.

## Kiểm chứng

- Build Release: **0 lỗi, 0 cảnh báo**.
- Harness tạm tham chiếu assembly Release, dùng router/frame callback thực tế với bo giả:
  - tái hiện lỗi cũ và xác nhận sửa đúng;
  - cả 7 tổ hợp chọn NG, gồm sai dây/chập/tuột tuýt làm mẫu cuối;
  - mẫu đạt tự chuyển sang mẫu NG, không mở sản xuất sớm;
  - giữ khóa khi còn một kết nối, chập ngoài model hoặc một IO active;
  - không đếm frame trùng, thiếu, unknown bytes, không biết terminator hoặc TESTPIN;
  - không ghép frame của hai scan generation;
  - giữ khóa khi eject đang chạy hoặc persistence lỗi;
  - WPF Dispatcher bận/coalescing vẫn chuyển bước; callback runtime cũ bị chặn.
- Không chạy self-test suite và không sửa `Tests/Program.cs` theo yêu cầu trước của người dùng.
- Không mở COM/bo thật, kích relay hay ghi DB/CFG sản xuất trong các mô phỏng này.

## Kiểm tra trên máy sản xuất

1. Chạy bản V156, chọn mẫu đạt + sai dây; bật log hệ thống để đối chiếu.
2. Kiểm tra mẫu đạt, tháo hết sau khi JIG mở: bảng reset và yêu cầu mẫu sai dây.
3. Kiểm tra mẫu sai dây, tháo hết sau khi JIG mở: tự về LẮP SẢN PHẨM mà không lắp sản phẩm mới.
4. Thử giữ lại một kết nối: chưa được mở sản xuất. Tháo hết thì tự chuyển.
5. Thử chập hoặc tuột tuýt làm mẫu cuối với lựa chọn tương ứng.

Mốc log mới: `MASTER_REMOVAL_CONFIRMED ... source=FRAME_STREAM`, tiếp theo là chuyển mẫu hoặc `MASTER VALIDATION COMPLETED`.

Chưa kiểm tra lại bằng bộ mẫu thật/JIG trong lần sửa này; cần đối chiếu các bước trên và log chạy thực tế.
