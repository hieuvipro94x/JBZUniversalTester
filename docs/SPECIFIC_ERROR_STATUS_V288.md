# V2026.09.288 — preserve the actual error in TestView

The presentation service previously collapsed device, connection, configuration,
storage and miscellaneous errors into the same standalone `KHÔNG ĐẠT`. Removal
labels could also hide the underlying system error.

Latched D2XX faults now show `LỖI BO D2XX` / `KHỞI ĐỘNG LẠI PHẦN MỀM`.
Latched persistence faults show `LỖI LƯU DỮ LIỆU` / `DỪNG KIỂM TRA`.
Both outrank retained PASS and pending-removal text. Specific Leak, Keysight,
configuration, history-save and relay errors retain their existing source message
and red error background. Other error messages retain their original reason rather
than being reduced to standalone `KHÔNG ĐẠT`.

Master error presentation similarly retains MasterStatus (or State if empty),
including the saved-PASS/JIG-open failure reason. The confirmed product-rejection
prompt and per-channel Master Leak removal/reconnect instructions remain in place.
Successful NG sample detection is still a successful Master validation, not a system
error.

Only presentation policy and its tests changed. Hardware lifecycle, restart policy,
relay commands, persistence transactions, history format, counters and removal gates
are unchanged. Tests cover system-error priority over stale PASS/removal, individual
Leak/Keysight/configuration/storage reasons and Master relay/configuration errors,
alongside early Master Leak, product removal and production Leak regressions.
Real hardware has not been exercised.

Verification: Release build succeeds with zero warnings/errors. Targeted status,
early Master Leak, rejected Master removal and production Leak checks pass.
Full suite: 110/114 PASS; the same four existing failures remain (learned diagnostic
topology, D2XX resistance configuration, final status guards and production fault
debounce). No new failing test group was introduced.
