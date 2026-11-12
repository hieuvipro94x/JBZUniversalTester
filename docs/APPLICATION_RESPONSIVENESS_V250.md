# Application responsiveness V2026.09.250

This release builds on the settings lifecycle fixes in V249. Review covered
startup, product selection/loading, settings, history, scan presentation and
shutdown. It does not change D2XX commands, scan cadence, fault classification,
relay timing, LOT counters or the history schema.

## Removed Dispatcher work

- Startup configuration migration/read and WAV resource loading run on a worker.
  MainViewModel receives the loaded settings instead of reading both files again.
  WPF objects and global button sound registration remain on Dispatcher.
- Product bundle reading and selected model SHA256/file identity capture run on
  workers with model preparation. Existing model-load generations still reject
  superseded results; shutdown now invalidates pending loads as well.
- Keysight native session disposal runs on a worker during equipment recovery and
  shutdown. Existing serial ownership and cleanup order remain unchanged.
- Normal shutdown awaits log draining off Dispatcher, retaining the existing
  drain timeout. A status message renders before cleanup; actions and deferred
  TestView navigation stay blocked during shutdown.
- Process/thread diagnostic enumeration runs on a worker and is skipped when
  file logging is disabled.

History ignores late callbacks after page release and obsolete search errors.
Next-page read failures are reported locally instead of escaping an async event
to the application exception handler. Existing SQL filters, 200-row keyset
pagination and complete exports are preserved; no production DB is modified by
this review.

The product picker already scans files/recent products on workers, uses a 150ms
search debounce and virtualized rows. IO processing already has a dedicated
frame worker and a coalesced presentation worker with stale-snapshot rejection.
Those mechanisms are retained rather than changing hardware scan timing.

Both MainWindow exit buttons now read THOÁT ỨNG DỤNG, with additional width.

## Validation and limits

Release build: zero warnings/errors. The async shutdown regression checks that
Dispatcher processes a heartbeat while the logger's final writer is pending,
then verifies that all 1,000 real queued log entries reach a temporary file in
order. It does not open physical hardware or modify production history.

The final complete run returned 93/97 PASS, with the same four V248/V249 baseline
failures: learned topology persistence, resistance selector source assertions,
final Master status guards and production fault presentation. No new failing
test group was introduced.

No live station benchmark was performed. Driver latency, large/network model
files, first WPF/JIT rendering and actual D2XX/Leak/Keysight shutdown still need
measurement on the station; this release cannot guarantee zero frame stalls on
every machine. Existing UI_STALL/UI_FRAME/PRODUCT_PICKER_FILTER/MODEL_LOAD_PERF
and SETTINGS_PERF logs provide evidence for subsequent station verification.
