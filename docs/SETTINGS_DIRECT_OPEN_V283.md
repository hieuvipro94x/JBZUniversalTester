# V2026.09.283 — direct settings display

Settings is prepared while disconnected from the visible MainWindow page host.
Fresh config/THT preparation remains on its worker. The editor view is measured
at the current client size, then its four panel layout batches yield between turns.
Only after all panels and bindings are ready is it attached to the shared host.
Main remains visible and interactive during preparation; the temporary loading
overlay and opacity cover are removed. This supersedes the V277 opening cover.

This keeps the single-window behavior and avoids displaying partial layout or
blocking one long UI turn to realize every editor at once. Each opening still
loads a fresh settings model; stale navigation releases the unattached page instead
of displaying it. COM/catalog discovery remains deferred until the page is loaded.
No hardware lifecycle, CFG/history format, save semantics or relay sequence changes.

`OPEN_PREPARED` records editor construction/preparation elapsed time. `OPEN_VIEW`
now records the final host attachment only; it must not be interpreted as total
click-to-ready latency. Existing `VIEW_BAML`, `OPEN_PANEL` and `OPEN_READY` logs
retain preparation diagnostics.

Tests verify input work between batches, ready editors without an opening overlay,
normal single-window navigation, and abandoned opening that never attaches. Timing
varies with cold WPF initialization and machine load; confirm end-to-end opening
and UI stalls on the production PC rather than assuming zero latency.
