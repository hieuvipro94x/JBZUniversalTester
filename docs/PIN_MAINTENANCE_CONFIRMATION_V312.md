# PIN replacement confirmation — V2026.09.312

- TestView opens the replacement/password dialog when the accumulated count reaches or exceeds the configured threshold (for example 10/10). It also checks when an already-overdue product is opened. Duplicate property notifications do not stack dialogs for the same model/count/threshold.
- Critical equipment/data faults and an active product-fault confirmation take precedence; the existing UI clock retries a deferred maintenance prompt without creating a polling worker or touching hardware.
- Manual replacement uses the same dialog. Wrong password, cancellation, an active product/removal interlock or an unapproved Master sequence never resets the counter. Wrong-password feedback remains in the dialog for retry.
- Authentication uses the current Settings administrator credential through the existing settings-access service, on a worker thread. The old independent fixed maintenance password is removed. Changing the Settings password therefore changes maintenance authentication immediately.
- Physical replacement must be confirmed after the product has been completely removed. Model/generation and removal gates are rechecked after asynchronous authentication. The persistence gate serializes reset against counter updates, and duplicate reset attempts are rejected.
- Successful resets use the existing atomic SQLite counter reset and maintenance audit. No production result, daily LOT, PASS/FAIL count or test-history row is reset. The PartCnt mirror remains a compatibility output.
- Vietnamese/Korean captions and inline validation follow TestView language; passwords remain masked and are cleared on close.

Regression tests use temporary credentials, a temporary SQLite database and a fake board. They cover 9/10 vs 10/10 automatic dialog behavior, rejection/cancellation without reset, shared changed passwords, asynchronous stale-model/duplicate confirmation rejection, durable audit, SQLite integrity and no extra hardware commands. Physical board/Leak hardware is not exercised by these tests.
