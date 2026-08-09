# V11.6 Probe isolation and immediate production fault

## TestPin

TestPin is a GND probe diagnostic mode only.

- Board transport switches to `BoardScanMode.Probe`.
- `TestEngine` frame processing is disabled.
- `TestViewModel.RuntimeMode` is `Probe`.
- Only `PinProbeWindow` receives `ScanFrameReceived`.
- A0/A1 in Probe means the physical I/O touched by the GND probe.
- No WrongWiring/Short, production popup, relay, PASS/FAIL, or Keysight logic may execute.

Runtime generation invalidates any Production handler that was scheduled before Probe started.
This closes the race that caused old production fault tasks to show a popup while Probe was open.

## Production wrong wiring

Production frame evaluation remains source -> target based.
A relation whose source and target do not belong to the same expected THT electrical component is a wiring fault.

V11.6 reports it immediately on the first complete production frame:

1. Build/update fault rows.
2. Start TESTPOINT alarm.
3. Latch production fault handler.
4. Set ERROR state and stop production scan.
5. Show the production wiring-fault popup.

The old 1000 ms ShortConfirm delay is removed.
The fault check runs before changing the UI to `ĐANG KIỂM TRA...`, so a wrong pair does not need to pass through that state first.
