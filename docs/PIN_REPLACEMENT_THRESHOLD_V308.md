# Configurable PIN replacement cycle — V2026.09.308

- Settings / Relay and Maintenance replaces the read-only `PartCnt.txt` field with **Chu kỳ thay PIN**. Its default remains 200,000 cycles.
- The editor accepts positive whole numbers, either plain (`200000`) or grouped with periods (`200.000`). Invalid, empty, negative, zero, fractional or overflowing values block Save.
- The value uses the existing CFG `ProbeReplacementThreshold` setting; no new file or setting key is needed. Changing it requires Relay/Maintenance permission.
- Loading a product applies the configured threshold to that part's SQLite maintenance record while preserving its accumulated counter. Increment/reset and the existing TestView `ProbeCycleText` then use the same threshold.
- TestView keeps its existing `current/threshold` format, e.g. `42/150.000`. Changing the threshold does not reset production, LOT, maintenance counts or test history.
- The PartCnt compatibility mirror remains supported internally; only its filename is removed from this settings field.
- Regression checks use temporary settings and SQLite files for input validation, permission checks, changing an imported threshold without resetting its count, increment behavior and `PRAGMA integrity_check`.
