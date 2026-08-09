Đọc AGENTS.md.



Hãy review riêng `TestViewModel.cs` về maintainability và production risk.



File hiện có hơn 4000 dòng.



MỤC TIÊU:

Không refactor ngay.

Không thay đổi behavior.

Không sửa code nếu chưa phát hiện BLOCKER/HIGH bug thực tế.



Hãy phân tích:



1\. TestViewModel hiện đang đảm nhiệm những responsibility nào.

2\. Xác định các nhóm logic:

&#x20;  - UI state

&#x20;  - Test lifecycle

&#x20;  - PASS/FAIL

&#x20;  - Fault handling

&#x20;  - D2XX

&#x20;  - UART

&#x20;  - ProductRemoved

&#x20;  - JIG/Relay

&#x20;  - counters

&#x20;  - history

&#x20;  - timers

&#x20;  - reconnect

&#x20;  - cancellation/dispose

&#x20;  - model loading

3\. Tìm:

&#x20;  - method quá dài;

&#x20;  - duplicated logic;

&#x20;  - event subscription risk;

&#x20;  - stale callback risk;

&#x20;  - race condition;

&#x20;  - UI-thread blocking;

&#x20;  - multiple reader ownership;

&#x20;  - recursive property/event;

&#x20;  - state flags phụ thuộc chéo;

&#x20;  - code khó test độc lập.



4\. Phân loại findings:

&#x20;  BLOCKER / HIGH / MEDIUM / LOW.



5\. Chỉ sửa nếu phát hiện BLOCKER/HIGH ảnh hưởng production correctness/stability.



6\. Với các vấn đề maintainability, hãy đề xuất decomposition plan nhưng KHÔNG thực hiện refactor lớn trong task này.



7\. Đề xuất TestViewModel về lâu dài nên giữ những responsibility nào và những phần nào nên tách thành service/controller riêng.



8\. Không thay đổi D2XX/UART protocol.

9\. Không thay đổi PASS/FAIL semantics.

10\. Không thay đổi hardware lifecycle chỉ để giảm số dòng.



Cuối cùng báo:

\- tổng số responsibility;

\- 5 khu vực rủi ro nhất;

\- có cần refactor trước production hay có thể để sau hardware validation;

\- decomposition plan theo từng bước nhỏ, có thể rollback.

