# Settings responsiveness and hardware ownership (V2026.09.249)

The V248 trace recorded UI heartbeat delays of 1,075 ms and 1,255 ms near
settings closure. Config persistence performed synchronous durable file flushes
on Dispatcher, config reload parsed files on Dispatcher, and printer replacement
could execute native SerialPort.Close/Dispose there. The trace correlates the
delays with settings activity; it does not provide a sampled call stack proving
which operation caused each individual stall.

## Changes

- Freeze the settings editor DTO, persist on a worker, and apply normalized
  values on the original UI context. File failures preserve pending edits.
- Read saved settings off Dispatcher. Missing/incomplete saved config is rejected
  instead of applying default card/confirmation settings to the running tester.
- Serialize atomic config temporary-file writes through replacement.
- Serialize all settings manual commands. Save/close cancel pending Leak/VISA
  operations and await their cleanup before reset or applying configuration.
- Recheck cancellation after acquiring the resistance gate, preserving its exact
  ownership and restoring background scan without reporting a device fault.
- Cancel superseded/page-owned printer connection attempts. Native printer close
  and disposal run on workers while the print gate stays owned. Failed disposal
  retains the handle for cleanup before any replacement connection.
- Reject known printer/Leak COM conflicts before opening another owner. An
  external application holding COM12 still requires releasing that external port.
- Disable MainWindow actions while Settings is open; guard deferred TestView
  opens. Save and return retain the existing safe relay reset sequence.
- Log SETTINGS_PERF phases SAVE_FILE, RELOAD_FILE, MANUAL_CLEANUP, OPEN_VIEW and
  RELEASE_BINDINGS for diagnosis on the station.

## Verification

`dotnet run --project Tests -c Release -- --test-settings-lifecycle` simulates a
blocked durable write, failed write, parallel atomic saves, a blocked native COM
close/disposal failure, pending replacement cancellation, overlapping manual
commands and cancellation while waiting for the VISA/relay gate. Dispatcher
continues processing during blocked I/O. Serial tests never open a physical COM.

Existing layout, scan/probe/fault, Master, Leak lifecycle, label and persistence
regressions are run through the complete self-test suite. No protocol, hardware
timing, production history schema or real production database is changed.

Release build completed with zero warnings/errors. The complete suite returned
92/96 PASS, including all three new lifecycle groups. The four failures match
the V248 baseline: learned topology persistence, resistance selector source
assertions, final Master status guards, and production fault presentation.

Station verification is still required to measure actual native-driver delays
and verify manual Leak/Keysight/relay behavior on connected hardware.
