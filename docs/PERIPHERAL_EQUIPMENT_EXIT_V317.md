# V2026.09.317 — Peripheral errors preserve application navigation

Audit scope: automatic Leak, automatic/Master Keysight resistance, manual
resistance in Settings, and TestView return-to-Main.

The existing peripheral error paths preserve the application, but their scan
recovery catch blocks could classify cycle cancellation as a fatal D2XX error.
Keysight recovery was also fire-and-forget and could continue after a screen or
model change. Manual resistance read errors entered this production recovery
while the manual operation was already running its own cleanup.

Changes:

- Keysight recovery captures the cycle token/model/runtime generation, rejects
  canceled/stale work before further hardware commands or dialogs, and does not
  convert expected cancellation into a fatal board error.
- Keysight session-disposal failures are logged separately from D2XX failures.
- Manual Keysight read errors return to Settings' error handling and manual
  cleanup instead of starting a concurrent production recovery.
- Leak recovery propagates cycle cancellation to its normal cancellation owner;
  stale recovery errors do not enter a new model's fatal board state.
- The Leak operator popup is only shown in an actual WPF application, consistent
  with the Keysight path; this permits headless fault-injection verification.

The physical-removal gate remains unchanged. A connected product blocks return
to Main; two complete valid empty frames permit it during an equipment error,
without sending extra hardware commands. Genuine D2XX disconnection/failure
retains the existing safety shutdown/reporting path.

Regression tests inject a Leak COM exception and a Keysight USB exception,
cancel recovery during scan start, verify no fatal dialog/report, verify the
physical-removal gate, exercise manual Keysight read failure, and verify actual
D2XX disconnection still enters its fatal safety path. All use fake hardware;
no production history/database is modified.
