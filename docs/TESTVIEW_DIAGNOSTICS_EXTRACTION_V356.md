# TestViewModel extraction: diagnostics, V2026.09.356

First scoped extraction from V2026.09.355. `TestViewDiagnostics` owns diagnostic
formatting, rendered-row diagnostic comparison, scan-metrics throttling and the
product-evidence log signature. `TestViewModel` supplies read-only snapshots at
the existing call sites; the service owns no board, engine, Dispatcher, database,
worker or cancellation lifecycle.

The existing five-second metrics interval, evidence deduplication/reset points,
log field names/order and duplicate-row comparison are retained. Disabled system
logging now skips collecting/comparing rendered-row diagnostics. Actual fault-row
synchronization and the Master/Production handlers still execute at the same
call sites and on the same threads.

No changes to D2XX commands, frame queues, scan generations, debounce, PASS/FAIL,
relay timing, resistance recovery, Leak/printer ownership or production storage.
Further extraction of production orchestration is intentionally outside this step.
The separate log-writer failure-handling finding is also outside this step.

Regression coverage includes disabled diagnostic work, concurrent throttle entry,
metric output, detached row snapshots with duplicate keys, diagnostic row deltas,
and evidence deduplication/reset. Existing source-location assertions now inspect
the extracted service plus its ViewModel call site. Tests use temporary log files
and fake frames; no connected hardware or production database is used by the new test.

Verification: Release build and the full self-test harness. Real D2XX/COM hardware
validation remains necessary before claiming measured production equivalence.
