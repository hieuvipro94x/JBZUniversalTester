# UI responsiveness changes — V2026.09.256

This follows the baseline investigation in `AUDIT_UI_D2XX_RESPONSIVENESS_2026_10_18.md`.

## Changes

- D2XX control writes, purge and handshake reads execute native driver calls on workers. The existing I/O semaphore and control-waiter priority remain held until the native operation completes. Cancellation cannot close/release an in-flight native call. TX timestamps are still captured immediately after FT_Write, inside the native critical section. Connect failure cleanup and disconnect close also run outside Dispatcher, after stopping the reader. Commands, scan decoding, relay policy and configured delays are unchanged.
- Settings label preview, test print and batch preparation parse THT/template data on workers using snapshots of editor values. BCW lookup and serial Prepare/Outcome run on workers while preserving the printer gate and journal-before-send ordering. Batch cancellation is installed before parsing; closing the page cancels preparation. Async action buttons stay disabled until their action finishes, and released pages do not open preparation dialogs.
- Settings ComboBoxes explicitly recycle virtualized dropdown containers. Initial three-column settings layout no longer rebuilds an equivalent grid on the first viewport event.

## Verification

- Release build: 0 warnings, 0 errors.
- Full self-tests: **96/100 PASS**, up from baseline **94/98**. Both new regressions pass. The same four baseline source/UI contract failures remain: learned topology diagnostic headings; ten-slot resistance layout; Master status text; fault popup XAML representation. No tests were weakened or removed.
- New blocked-driver regression checks Dispatcher responsiveness, serialized execution, cancellation while waiting, ownership until an active call completes, exception propagation and balanced control priority counters. It never opens FTDI.
- New busy-SQLite regression uses a temporary BCW journal with a held write transaction. Dispatcher remains responsive, physical output cannot precede journal preparation, printing resumes after unlock and the outcome remains Sent. Temporary journal `PRAGMA integrity_check` returns `ok`.
- Offscreen WPF layout harness: 6 page creations and 1,200 card selections, without real hardware. With 10,000 model entries, only **9** dropdown containers were realized. First dropdown open measured **105 ms** in this harness.
- Settings cold constructor/first layout in the isolated harness still measured **340/557 ms**; subsequent constructor/first-layout totals were **131–220 ms**. These measurements include cold WPF/theme/JIT work and are not a production-station latency guarantee. The page's BAML/layout still runs on Dispatcher. This release does not establish that every action is free of stalls.

## Data and remaining verification

No production database was opened, migrated, deleted or replaced by this work. History schema, queries, timestamps, export and production counters are unchanged. Existing history regressions pass in the full suite. SQLite integrity above refers to test fixtures, not the station's real database.

Real D2XX/Leak/printer devices were not exercised. A production-station run is still needed to assess cold startup, first settings render and UI_STALL/scan queue metrics under real driver load. Do not drop complete scan frames or change firmware timing to mask latency.
