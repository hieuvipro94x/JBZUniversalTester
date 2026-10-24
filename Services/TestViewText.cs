using System.Text.RegularExpressions;

namespace JBZUniversalTester.Services;

/// <summary>Presentation-only catalog. Never call this on product, wire, printer or history data.</summary>
public static class TestViewText
{
    public static string NormalizeLanguage(string? language) =>
        string.Equals(language, "ko", StringComparison.OrdinalIgnoreCase) ? "ko" : "vi";

    private static readonly Dictionary<string, string> Korean = new(StringComparer.Ordinal)
    {
        ["TRANG CHÍNH"] = "메인 화면", ["Số LOT"] = "LOT 번호",
        ["Số lỗi hở mạch"] = "단선 수", ["Số lỗi đấu sai"] = "오배선 수", ["Số lỗi"] = "불량 수",
        ["THỬ LẠI IN TEM"] = "라벨 인쇄 재시도", ["IN THÊM BẢN SAO"] = "라벨 추가 인쇄",
        ["XÁC NHẬN ĐÃ LẮP XONG MẪU"] = "샘플 장착 완료 확인",
        ["Mã sản phẩm"] = "품번", ["Tên sản phẩm"] = "품명", ["Loại xe"] = "차종", ["Mã khách hàng"] = "고객 코드",
        ["Tổng số"] = "총 검사 수", ["Số đạt"] = "합격 수", ["Tỷ lệ"] = "합격률",
        ["CHU KỲ THAY PIN:"] = "검사 핀 교체 주기:", ["Loại"] = "종류", ["LOẠI"] = "종류",
        ["Connector"] = "커넥터", ["Chân Pin"] = "핀 번호", ["Mã Dây"] = "와이어 코드",
        ["Màu"] = "색상", ["Tiết Diện"] = "단면적", ["Trạng thái"] = "상태",
        ["KÊNH"] = "채널", ["MIN Ω"] = "최소 Ω", ["MAX Ω"] = "최대 Ω", ["ĐIỆN TRỞ"] = "저항",
        ["ÁP SUẤT HÚT"] = "흡입압력", ["ÁP SUẤT GIỮ"] = "유지압력", ["ĐỘ RÒ"] = "누설량",
        ["GIỚI HẠN RÒ"] = "누설 한계", ["KẾT QUẢ"] = "결과", ["TEST LEAK"] = "기밀 검사",
        ["PASS"] = "합격", ["FAIL"] = "불합격", ["NG LEAK"] = "기밀 불량", ["OPEN"] = "단선",
        ["LẮP SẢN PHẨM"] = "제품을 장착하세요", ["CHỜ LẮP SẢN PHẨM"] = "제품 장착 대기",
        ["ĐANG KIỂM TRA"] = "검사 중", ["ĐANG KIỂM TRA..."] = "검사 중...",
        ["THÁO SẢN PHẨM"] = "제품을 탈거하세요", ["THÁO SẢN PHẨM LỖI"] = "불량 제품을 탈거하세요",
        ["SẢN PHẨM KHÔNG ĐẠT"] = "제품 불합격", ["CHƯA ĐẠT"] = "검사 불합격",
        ["LẮP MẪU MASTER"] = "마스터 샘플을 장착하세요",
        ["ĐƯA HÀNG VÀO THÙNG HÀNG LỖI"] = "불량 제품을 불량함에 넣으세요",
        ["CHỜ XÁC NHẬN THÙNG LỖI"] = "불량함 확인 대기", ["CHỜ THÁO SẢN PHẨM"] = "제품 탈거 대기",
        ["THÙNG LỖI ĐÃ KHÓA - CHỜ NHẢ CẢM BIẾN"] = "불량함 잠김 - 센서 해제 대기",
        ["ĐẠT"] = "정상", ["SAI DÂY"] = "오배선", ["CHẬP MẠCH"] = "합선", ["HỞ MẠCH"] = "단선",
        ["TUỘT TUÝT / ĐỨT DÂY"] = "핀 빠짐 / 단선", ["TUỘT TUÝT/ĐỨT DÂY"] = "핀 빠짐 / 단선",
        ["Đơn"] = "단독", ["Nối chung"] = "공통 연결", ["ĐÃ KẾT NỐI"] = "연결 완료",
        ["CHƯA KẾT NỐI"] = "미연결", ["CHƯA ĐỦ KẾT NỐI"] = "연결 부족", ["KẾT NỐI ĐÚNG"] = "정상 연결",
        ["SAI KẾT NỐI"] = "오접속", ["SAI VỊ TRÍ"] = "위치 오류", ["SAI MÀU DÂY"] = "색상 오류",
        ["TERMINAL SAI VỊ TRÍ"] = "단자 위치 오류", ["ĐẢO VỊ TRÍ TERMINAL"] = "단자 위치 뒤바뀜",
        ["ĐIỆN TRỞ KHÔNG ĐẠT"] = "저항 불량", ["KÍN NƯỚC KHÔNG ĐẠT"] = "기밀 불량",
        ["ĐIỆN ÁP KHÔNG ĐẠT"] = "전압 불량", ["DÒNG ĐIỆN KHÔNG ĐẠT"] = "전류 불량",
        ["LỖI THIẾT BỊ / HỆ THỐNG"] = "장비 / 시스템 오류", ["LỖI CHƯA PHÂN LOẠI"] = "미분류 오류",
        ["KIỂM TRA KÍN NƯỚC KHÔNG ĐẠT"] = "기밀 검사 불합격", ["THÁO CONNECTOR LEAK"] = "기밀 검사 커넥터를 탈거하세요",
        ["LẮP LẠI CONNECTOR LEAK"] = "기밀 검사 커넥터를 다시 장착하세요", ["ĐANG TEST LEAK"] = "기밀 검사 중",
        ["KIỂM TRA ĐIỆN TRỞ"] = "저항 검사 중", ["ĐANG CHUẨN BỊ ĐO ĐIỆN TRỞ"] = "저항 측정 준비 중",
        ["LỖI BO D2XX"] = "D2XX 보드 오류", ["KHỞI ĐỘNG LẠI PHẦN MỀM"] = "프로그램을 다시 시작하세요",
        ["LỖI LƯU DỮ LIỆU"] = "데이터 저장 오류", ["DỪNG KIỂM TRA"] = "검사를 중지하세요",
        ["LỖI THIẾT BỊ"] = "장비 오류", ["LỖI KẾT NỐI BO"] = "보드 연결 오류",
        ["LỖI THIẾT BỊ KÍN NƯỚC"] = "기밀 검사기 오류", ["LỖI THIẾT BỊ LEAK"] = "기밀 검사기 오류",
        ["LỖI KEYSIGHT"] = "KEYSIGHT 오류", ["KHÔNG KẾT NỐI KEYSIGHT 34461A"] = "KEYSIGHT 34461A 연결 안 됨",
        ["LỖI LƯU LỊCH SỬ LEAK"] = "기밀 검사 이력 저장 오류", ["KHÔNG MARKING/JIG"] = "마킹 / JIG 동작 금지",
        ["KHÔNG BẮT ĐẦU TEST"] = "검사 시작 금지", ["KHÔNG MỞ PRODUCTION"] = "생산 검사 시작 금지",
        ["PASS ĐÃ LƯU"] = "합격 결과 저장 완료", ["RELAY BỊ CHẶN"] = "릴레이 동작 차단",
        ["LỖI THỰC THI RELAY"] = "릴레이 동작 오류", ["KHÔNG THỂ MỞ JIG"] = "JIG 개방 불가",
        ["LỖI KIỂM TRA LEAK"] = "기밀 검사 오류", ["LỖI KIỂM TRA MẪU NG"] = "불량 마스터 검사 오류",
        ["CẤU HÌNH CARD KHÔNG ĐỦ"] = "보드 설정 용량 부족",
        ["CẤU HÌNH TEST LEAK KHÔNG HỢP LỆ"] = "기밀 검사 설정 오류",
        ["CẤU HÌNH TEST LEAK MASTER KHÔNG HỢP LỆ"] = "마스터 기밀 검사 설정 오류",
        ["CHƯA KẾT NỐI BO - KHÔNG THỂ TEST"] = "보드 미연결 - 검사 불가", ["CHỜ CHỌN MÃ HÀNG"] = "품번 선택 대기",
        ["CHỜ ĐỒNG BỘ DỮ LIỆU BO"] = "보드 데이터 동기화 대기", ["ĐANG KẾT NỐI BO"] = "보드 연결 중",
        ["BO ĐANG CHUẨN BỊ"] = "보드 준비 중", ["ĐANG ĐỒNG BỘ DỮ LIỆU BO..."] = "보드 데이터 동기화 중...",
        ["CHƯA ĐỒNG BỘ CARD/MÃ HÀNG - HÃY THỬ LẠI"] = "보드 / 품번 동기화 안 됨 - 다시 시도하세요",
        ["CHƯA ĐỒNG BỘ CẤU HÌNH CARD - HÃY THỬ LẠI"] = "보드 설정 동기화 안 됨 - 다시 시도하세요",
        ["CHƯA NHẬN ĐƯỢC FRAME CỦA MÃ HÀNG - HÃY THỬ LẠI"] = "품번 검사 프레임 미수신 - 다시 시도하세요",
        ["TIẾP XÚC JIG/PROBE KHÔNG ỔN ĐỊNH — KIỂM TRA PROBE PIN/JIG"] = "JIG / 프로브 접촉 불안정 — 검사 핀 / JIG를 확인하세요",
        ["CHỌN MẪU NG CẦN KIỂM TRA TRONG CÀI ĐẶT"] = "설정에서 검사할 불량 마스터를 선택하세요",
        ["KIỂM TRA MASTER PASS"] = "정상 마스터 검사", ["LẮP MẪU MASTER ĐẠT ĐẦU CA"] = "작업 시작 전 정상 마스터를 장착하세요",
        ["HOÀN THÀNH MẪU MASTER LỖI"] = "불량 마스터 검사 완료", ["MASTER HOÀN TẤT • PRODUCTION ENABLED"] = "마스터 검사 완료 • 생산 검사 가능",
        ["MẤT KẾT NỐI MÁY TEST - VUI LÒNG KHỞI ĐỘNG LẠI"] = "검사기 연결 끊김 - 프로그램을 다시 시작하세요",
        ["ĐANG MỞ JIG HÀNG LỖI"] = "불량 제품 JIG 개방 중", ["MANUAL"] = "수동 모드",
        ["ĐO ĐIỆN TRỞ BẰNG TAY"] = "수동 저항 측정", ["CHẠY THỬ MÁY LEAK"] = "기밀 검사기 시험 운전",
        ["ĐẾN CHU KỲ THAY PROBE PIN"] = "검사 핀 교체 시기", ["PROBE PIN ĐANG TRONG CHU KỲ SỬ DỤNG"] = "검사 핀 사용 주기 이내",
        ["VUI LÒNG THÁO SẢN PHẨM!!"] = "제품을 탈거해 주세요!!", ["CHƯA THÁO SẢN PHẨM"] = "제품이 탈거되지 않았습니다",
        ["Bạn có muốn dừng kiểm tra và quay về màn hình chọn mã?"] = "검사를 중지하고 품번 선택 화면으로 돌아가시겠습니까?",
        ["Xác nhận"] = "확인", ["XÁC NHẬN"] = "확인", ["HỦY"] = "취소", ["Sản phẩm không đạt"] = "제품 불합격",
        ["NHẬP MẬT KHẨU XỬ LÝ HÀNG LỖI"] = "불량 처리 비밀번호 입력",
        ["Chưa cài Mật khẩu thùng lỗi trong Cài đặt Production."] = "생산 설정에 불량함 비밀번호가 설정되지 않았습니다.",
        ["Mật khẩu xử lý hàng lỗi không đúng."] = "불량 처리 비밀번호가 올바르지 않습니다.",
        ["Bấm XÁC NHẬN để mở đầu gá và tháo sản phẩm."] = "확인을 눌러 JIG를 열고 제품을 탈거하세요.",
        ["Bấm XÁC NHẬN để mở JIG. Sau đó đưa hàng lỗi qua cảm biến thùng NG 1 lần."] = "확인을 눌러 JIG를 연 뒤 불량 제품을 불량함 센서에 한 번 통과시키세요.",
        ["Vui lòng kiểm tra lại sản phẩm xem có đứt dây / tuột tuýt không. Bấm XÁC NHẬN để xử lý hàng lỗi, mở JIG và tháo sản phẩm."] = "제품의 단선 / 핀 빠짐을 확인하세요. 확인을 눌러 불량 처리 후 JIG를 열고 제품을 탈거하세요.",
        [" Đưa hàng lỗi qua cảm biến thùng NG 1 lần."] = " 불량 제품을 불량함 센서에 한 번 통과시키세요.",
        ["Xác nhận thay Probe Pin"] = "검사 핀 교체 확인", ["BẢO TRÌ PROBE PIN"] = "검사 핀 유지보수",
        ["Xác nhận đã thay pin"] = "검사 핀 교체 완료 확인", ["THIẾT BỊ ĐANG BẢO TRÌ"] = "유지보수 대상",
        ["Kiểm tra đúng mã hàng và số lượt trước khi đặt lại bộ đếm."] = "카운터 초기화 전 품번과 검사 횟수를 확인하세요.",
        ["SỐ LƯỢT HIỆN TẠI"] = "현재 검사 횟수", ["NGƯỠNG THAY PIN"] = "검사 핀 교체 기준",
        ["Chỉ xác nhận sau khi đã thay Probe Pin thực tế. Bộ đếm sẽ được đặt về 0 và ghi nhận vào lịch sử bảo trì."] = "검사 핀을 실제로 교체한 후 확인하세요. 카운터가 0으로 초기화되고 유지보수 이력에 기록됩니다.",
        ["MẬT KHẨU QUẢN TRỊ"] = "관리자 비밀번호", ["Đã reset counter Pin"] = "검사 핀 카운터 초기화 완료",
        ["Không thể reset counter Pin"] = "검사 핀 카운터 초기화 불가", ["Chưa chọn mã hàng."] = "품번을 선택하지 않았습니다.",
        ["Không thể reset trong khi sản phẩm/JIG đang ở trong chu kỳ test."] = "제품 / JIG 검사 주기 중에는 초기화할 수 없습니다.",
        ["Không thể reset counter trong chu trình xác nhận MASTER."] = "마스터 확인 검사 중에는 카운터를 초기화할 수 없습니다.",
        ["Mật khẩu quản trị thay Probe Pin không đúng."] = "검사 핀 교체 관리자 비밀번호가 올바르지 않습니다.",
        ["Chưa lưu được thay đổi Probe Pin. Vui lòng thử lại."] = "검사 핀 교체 내용을 저장하지 못했습니다. 다시 시도하세요.",
        ["Cảnh báo bảo trì Probe Pin"] = "검사 핀 유지보수 알림", ["CHƯA BẮT ĐẦU KIỂM TRA"] = "검사 시작 안 됨",
        ["CHƯA KẾT NỐI BO D2XX"] = "D2XX 보드 미연결", ["LỖI ĐỒNG HỒ KEYSIGHT"] = "KEYSIGHT 계측기 오류",
        ["LỖI MÁY LEAK"] = "기밀 검사기 오류", ["KIỂM TRA MÁY LEAK"] = "기밀 검사기 확인",
        ["CHƯA LƯU ĐƯỢC KẾT QUẢ"] = "결과 저장 안 됨", ["Lỗi in tem"] = "라벨 인쇄 오류",
        ["Mất kết nối máy Leak. Hãy rút/cắm lại cáp, chọn lại cổng COM rồi thử lại."] = "기밀 검사기 연결이 끊겼습니다. 케이블을 다시 연결하고 COM 포트를 선택한 후 재시도하세요.",
        ["Kênh Leak 1"] = "기밀 검사 채널 1", ["Kênh Leak 2"] = "기밀 검사 채널 2", ["Kênh Leak 3"] = "기밀 검사 채널 3",
        [" cắm nhầm với "] = " 오접속 상대: ", [" cắm nhầm vị trí chưa xác định"] = " 오접속 위치 미확인",
        ["Mã dây chưa xác định"] = "와이어 코드 미확인", [" màu "] = " 색상 ",
        ["KIỂM TRA LỖI HỞ MẠCH"] = "단선 불량 확인", ["KIỂM TRA LỖI SAI DÂY"] = "오배선 불량 확인",
        ["KIỂM TRA LỖI CHẬP MẠCH"] = "합선 불량 확인", ["KIỂM TRA LỖI ĐIỆN TRỞ"] = "저항 불량 확인",
        ["KIỂM TRA LỖI KÍN NƯỚC"] = "기밀 불량 확인", ["LỖI HỆ THỐNG KIỂM TRA"] = "검사 시스템 오류",
        ["KIỂM TRA SẢN PHẨM"] = "제품 검사", ["KHÔNG CÓ KẾT NỐI"] = "연결 없음",
        ["THÔNG MẠCH"] = "도통", ["CHỜ THÁO"] = "탈거 대기", ["ĐANG ĐO"] = "측정 중",
        ["UNSTABLE"] = "불안정", ["ĐỨT DÂY / TUỘT TUÝT"] = "단선 / 핀 빠짐",
        ["SẢN PHẨM VẪN ĐANG LẮP — THÁO SẢN PHẨM"] = "제품이 장착되어 있습니다 — 제품을 탈거하세요",
        ["MASTER PASS: ĐIỆN TRỞ KHÔNG ĐẠT"] = "정상 마스터: 저항 불합격",
        ["MẪU MASTER PASS ĐANG CÓ LỖI DÂY"] = "정상 마스터 배선 불량",
        ["KIỂM TRA / THÁO MẪU"] = "샘플을 확인 / 탈거하세요", ["MASTER DISABLED"] = "마스터 검사 비활성",
        ["CHỜ KIỂM TRA MASTER ĐẦU CA"] = "작업 시작 전 마스터 검사 대기",
        ["CẦN THAY PROBE PIN"] = "검사 핀 교체 필요",
        ["Chưa thể bắt đầu kiểm tra. Vui lòng quay về trang chính và thử lại."] = "검사를 시작할 수 없습니다. 메인 화면으로 돌아가 다시 시도하세요.",
        ["Bấm XÁC NHẬN để đóng chương trình."] = "확인을 눌러 프로그램을 종료하세요.",
        ["ĐANG NẠP MÃ HÀNG..."] = "품번 불러오는 중...", ["ĐÃ HỦY CHỌN PART"] = "품번 선택 취소됨",
        ["ĐANG TẢI MODEL GẦN NHẤT"] = "최근 품번 불러오는 중", ["IO MAPPING MODE • KHÔNG PASS/FAIL"] = "IO 매핑 모드 • 합격 / 불합격 판정 없음",
        ["MASTER PASS MẤT ĐIỀU KIỆN PASS - KIỂM TRA LẠI"] = "정상 마스터 합격 조건 해제 - 다시 검사하세요",
        ["MASTER PASS KHÔNG CÒN ĐỦ ĐIỀU KIỆN - KIỂM TRA LẠI"] = "정상 마스터 합격 조건 부족 - 다시 검사하세요",
        ["HOÀN THÀNH MẪU MASTER ĐẠT"] = "정상 마스터 검사 완료", ["LỖI THỰC THI RELAY MASTER"] = "마스터 릴레이 동작 오류",
        ["MASTER ĐÃ LƯU PASS NHƯNG KHÔNG THỂ MỞ JIG"] = "마스터 합격 저장 완료 / JIG 개방 불가",
        ["Mất kết nối với máy test. Vui lòng khởi động lại."] = "검사기 연결이 끊겼습니다. 프로그램을 다시 시작하세요.",
        ["Chưa kết nối bo mạch D2XX. Hãy kiểm tra nguồn và cáp USB của bo trước khi thực hiện chức năng này."] = "D2XX 보드가 연결되지 않았습니다. 이 기능을 실행하기 전에 보드 전원과 USB 케이블을 확인하세요.",
        ["Không thể lưu giao dịch in vào lịch sử; first-print đã bị chặn để tránh mất traceability."] = "인쇄 작업 이력 저장 실패. 추적 정보 손실을 방지하기 위해 최초 인쇄를 차단했습니다.",
        ["Giao dịch in bị hủy; trạng thái tem vật lý chưa thể xác định. Không tự retry."] = "인쇄 작업 취소됨. 실제 라벨 상태를 확인할 수 없어 자동 재인쇄하지 않습니다.",
        ["LỖI HỞ MẠCH"] = "단선 불량", ["LỖI SAI DÂY"] = "오배선 불량", ["LỖI CHẬP MẠCH"] = "합선 불량",
        ["LỖI ĐIỆN TRỞ"] = "저항 불량", ["LỖI KÍN NƯỚC"] = "기밀 불량",
        ["Chưa cấu hình connector Leak cho MASTER."] = "마스터 기밀 검사 커넥터가 설정되지 않았습니다.",
        ["Connector TEST LEAK cần dây RET/RT, hoặc một cặp IO trong THT nối sang một connector khác."] = "기밀 검사 커넥터에는 RET/RT 배선 또는 THT에 정의된 다른 커넥터와의 IO 연결 쌍이 필요합니다.",
        ["Hãy tháo sản phẩm, vào CÀI ĐẶT và tắt TEST LEAK hoặc cấu hình lại connector/THT. Ứng dụng vẫn tiếp tục hoạt động, không cần đóng/mở lại."] = "제품을 탈거한 후 설정에서 기밀 검사를 끄거나 커넥터 / THT 설정을 수정하세요. 프로그램을 다시 시작할 필요는 없습니다.",
        ["HÃY BẬT TEST LEAK TRƯỚC."] = "먼저 기밀 검사를 활성화하세요.", ["CHỌN KÊNH KIỂM TRA"] = "검사 채널을 선택하세요",
        ["Máy Leak đang chạy test; không thể mở COM thủ công."] = "기밀 검사 진행 중에는 COM 포트를 수동으로 열 수 없습니다.",
        ["Baudrate may leak khong hop le."] = "기밀 검사기 통신 속도 설정 오류.",
        ["MASTER PASS"] = "정상 마스터", ["MASTER NG"] = "불량 마스터",
    };

    // Captured values are copied verbatim, even if a model/wire name resembles a UI label.
    private static readonly (Regex Pattern, string Format)[] Templates =
    [
        Template("HOÀN THÀNH KIỂM TRA MẪU {0}", "{0} 마스터 검사 완료"),
        Template("KIỂM TRA MẪU {0}", "{0} 마스터 검사"),
        Template("KIỂM TRA MASTER {0}", "{0} 마스터 검사"),
        Template("CHƯA ĐÚNG MẪU MASTER {0}", "{0} 마스터 샘플이 올바르지 않습니다"),
        Template("CHƯA PHÁT HIỆN LỖI MASTER {0}", "{0} 마스터 불량이 검출되지 않았습니다"),
        Template("MẪU TUỘT TUÝT / ĐỨT DÂY: THIẾU {0} KẾT NỐI; LẮP XONG MẪU RỒI BẤM XÁC NHẬN", "핀 빠짐 / 단선 샘플: 연결 {0}개 부족; 샘플 장착 후 확인을 누르세요"),
        Template("UniversalTester {0} - Màn hình kiểm tra", "UniversalTester {0} - 검사 화면"),
        Template("ĐANG ĐỒNG BỘ {0} CARD...", "보드 {0}개 동기화 중..."),
        Template("ĐANG TEST LEAK LẠI LẦN {0}", "기밀 재검사 {0}회 진행 중"),
        Template("ĐÃ ĐỒNG BỘ MÃ HÀNG: {0} • {1} CARD", "품번 동기화 완료: {0} • 보드 {1}개"),
        Template("THÁO CONNECTOR LEAK {0}", "기밀 검사 커넥터 탈거: {0}"),
        Template("LẮP LẠI CONNECTOR LEAK {0}", "기밀 검사 커넥터 재장착: {0}"),
        Template("Mã hàng: {0}", "품번: {0}"), Template("Model: {0}", "모델: {0}"),
        Template("Chu kỳ hiện tại: {0}", "현재 횟수: {0}"), Template("Chu kỳ thay thế: {0}", "교체 주기: {0}"),
        Template("Trạng thái: {0}", "상태: {0}"),
        Template("Đã lưu thay Probe Pin vào SQLite. Counter {0} → 0.", "검사 핀 교체를 저장했습니다. 카운터 {0} → 0."),
        Template("Đã có kết quả Leak retest {0} nhưng chưa lưu được lịch sử.", "기밀 재검사 결과 {0}의 이력을 저장하지 못했습니다."),
        Template(" cắm nhầm lỗ chân {0}", " 오접속 핀 번호: {0}"),
        Template(" vị trí Housing: {0}", " 하우징 위치: {0}"),
        Template("Lỗi máy Leak. Kiểm tra nguồn và cổng COM, tháo/lắp lại connector để thử lại.\n{0}", "기밀 검사기 오류. 전원과 COM 포트를 확인하고 커넥터를 다시 장착하세요.\n{0}"),
        Template("Không kết nối hoặc đọc được đồng hồ Keysight. Kiểm tra nguồn, USB và VISA. Tháo sản phẩm rồi lắp lại để thử lại.\n{0}", "KEYSIGHT 연결 / 읽기 오류. 전원, USB, VISA를 확인하고 제품을 다시 장착하세요.\n{0}"),
        Template("CH{0} chưa chọn connector THT.", "CH{0}: THT 커넥터를 선택하지 않았습니다."),
        Template("CH{0}: connector '{1}' không tồn tại trong THT {2}.", "CH{0}: 커넥터 '{1}'이 THT {2}에 없습니다."),
        Template("CH{0}: connector '{1}' không có RET/RT hoặc cặp IO nối sang connector khác trong THT.", "CH{0}: 커넥터 '{1}'에 RET/RT 또는 THT의 다른 커넥터로 연결되는 IO 쌍이 없습니다."),
        Template("CONNECTOR {0}: LẮP ĐÚNG DÂY RET/RT; NẾU KHÔNG CÓ RET, CẦN MỘT CẶP IO ĐÚNG THT NỐI SANG CONNECTOR KHÁC.", "커넥터 {0}: RET/RT 배선을 올바르게 연결하세요. RET가 없으면 THT에 정의된 다른 커넥터와의 IO 연결 쌍이 필요합니다."),
        Template("Máy Leak/driver COM không phản hồi trong {0} giây.", "기밀 검사기 / COM 드라이버가 {0}초 동안 응답하지 않았습니다."),
        Template("Qua thoi gian {0} giay cho :RESULT tu may leak.", "기밀 검사기 :RESULT 수신 대기 시간 {0}초 초과."),
        Template("Không mở được {0} trong {1} giây. Driver COM có thể đang giữ handle cũ.", "{0} 포트를 {1}초 이내에 열지 못했습니다. COM 드라이버가 이전 연결을 해제하지 않았을 수 있습니다."),
        Template("Cổng COM máy Leak chưa nhả handle cũ trong {0} giây; không mở chồng phiên mới.", "기밀 검사기 COM의 이전 연결이 {0}초 이내에 해제되지 않아 새 연결을 열지 않습니다."),
        Template("RESULT may leak can 6 gia tri, nhan {0}: {1}", "기밀 검사 RESULT에는 6개 값이 필요합니다. 수신 {0}개: {1}"),
        Template("RESULT may leak co gia tri khong hop le '{0}'.", "기밀 검사 RESULT의 값 '{0}'이 올바르지 않습니다.")
    ];

    private static (Regex, string) Template(string source, string format)
    {
        string pattern = Regex.Escape(source);
        string valuePattern = source.Contains('\n') ? ".*?" : "[^\\r\\n]*?";
        for (int i = 0; i < 4; i++) pattern = pattern.Replace(Regex.Escape("{" + i + "}"), "(?<v" + i + ">" + valuePattern + ")", StringComparison.Ordinal);
        return (new Regex("\\A" + pattern + "\\z", RegexOptions.CultureInvariant | RegexOptions.Singleline), format);
    }

    public static string Translate(string? text, string? language)
    {
        if (text is null) return string.Empty;
        if (NormalizeLanguage(language) != "ko" || text.Length == 0) return text;
        if (Korean.TryGetValue(text, out string? exact)) return exact;
        foreach ((Regex pattern, string format) in Templates)
        {
            Match match = pattern.Match(text);
            if (!match.Success) continue;
            bool semanticSample = text.StartsWith("KIỂM TRA MẪU ", StringComparison.Ordinal) ||
                text.StartsWith("HOÀN THÀNH KIỂM TRA MẪU ", StringComparison.Ordinal) ||
                text.StartsWith("KIỂM TRA MASTER ", StringComparison.Ordinal) ||
                text.StartsWith("CHƯA ĐÚNG MẪU MASTER ", StringComparison.Ordinal) ||
                text.StartsWith("CHƯA PHÁT HIỆN LỖI MASTER ", StringComparison.Ordinal);
            if (semanticSample && !Korean.ContainsKey(match.Groups["v0"].Value)) continue;
            string translated = format;
            for (int i = 0; i < 4; i++)
            {
                string captured = match.Groups["v" + i].Value;
                if (text.StartsWith("Lỗi máy Leak.", StringComparison.Ordinal) || text.StartsWith("Không kết nối hoặc đọc được đồng hồ Keysight.", StringComparison.Ordinal))
                    captured = Translate(captured, language);
                // Only known sample/status slots are semantic; all identifiers remain opaque.
                if ((text.StartsWith("KIỂM TRA MẪU ", StringComparison.Ordinal) ||
                     text.StartsWith("HOÀN THÀNH KIỂM TRA MẪU ", StringComparison.Ordinal) ||
                     text.StartsWith("KIỂM TRA MASTER ", StringComparison.Ordinal) ||
                     text.StartsWith("CHƯA ĐÚNG MẪU MASTER ", StringComparison.Ordinal) ||
                     text.StartsWith("CHƯA PHÁT HIỆN LỖI MASTER ", StringComparison.Ordinal) ||
                     text.StartsWith("Trạng thái: ", StringComparison.Ordinal)) && Korean.TryGetValue(captured, out string? sample)) captured = sample;
                translated = translated.Replace("{" + i + "}", captured, StringComparison.Ordinal);
            }
            return translated;
        }
        if (text.Contains('\n')) return string.Join('\n', text.Split('\n').Select(line => Translate(line, language)));
        if (text.Contains(" - ", StringComparison.Ordinal)) return string.Join(" - ", text.Split(" - ").Select(part => Translate(part, language)));
        return text; // Preserve unknown diagnostics rather than hiding them or translating raw identifiers.
    }
}
