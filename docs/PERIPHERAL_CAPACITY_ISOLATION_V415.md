# V2026.09.415: peripheral cleanup must not fault a connected board

V414 log, 2026-11-11 16:41:48-51:
- Manual Leak COM7 open failed with FileNotFoundException.
- Manual cleanup called EnsureContinuousProductionScanAsync.
- Model WH322244 has MaxIO224 (4 required cards); installed configuration is
  2 cards/128 IO. Production scan rejected this configuration.
- The generic recovery path attempted soft restart and FTDI reopen, then
  incorrectly escalated a model capacity mismatch to DeviceFault.
- The log also contains the same capacity fault at 16:41:21, independently of
  the later COM7 failure. COM2 printer failure is reported independently.
  No Keysight connection failure is established by this log.

Before scan recovery, configure the current model range and check the existing
BoardScanCapacity result. A mismatch blocks engine processing and suspends the
scan watchdog, reports the actionable capacity error, and returns without
reopening/disconnecting FTDI or raising DeviceFault. It does not bypass model
capacity validation or change the selected card configuration. Correct settings
can resume scanning on the current handle. Real D2XX transport failures retain
their safety policy. Leak/Keysight connection/read errors remain peripheral errors.
A suspended scan for invalid model capacity is intentional, not a dropped FTDI
connection; testing an incomplete model is not permitted.

Regression first failed against V414. It covers Leak COM failure, Keysight read
failure, manual exit without a peripheral error, model capacity gating, no fault
reports/reconnects/result relays/counts, and successful in-place resume after
correcting the simulated installed card count. Existing tests preserve independent
USB reconnection and real board-fault handling. No production files were modified.
