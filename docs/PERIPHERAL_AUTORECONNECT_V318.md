# V2026.09.318 — USB peripheral automatic reconnect

Previously Leak reopened its COM on the next run and Keysight connected on the
next resistance attempt, but no background owner retried while waiting for a
removed USB device to return.

After an automatic/manual instrument connection or read error:

- Keysight closes the failed VISA session and requests automatic VISA/USB
  discovery/identity validation using the existing ConnectAutomatic policy.
- Leak requests reopening the configured COM with a snapshot of the settings
  used for the failed run. No other COM is guessed; a printer-owned COM is
  rejected. The connection worker does not send a Leak test command.
- A separate worker per peripheral attempts immediately and retries every two
  seconds while unavailable. The cadence matches the existing hardware monitor;
  it does not change protocol commands, baud rates or driver timeouts.
- Retry runs on background tasks. Repeated requests coalesce; new requests
  during an attempt cannot be cleared by an older completion. Logging records
  the initial failure and successful reconnect without repeated operator popups.
- VISA automatic discovery, open, identity validation and rejected-session
  cleanup now share the same session lock as instrument I/O and disposal.
  Leak retains its existing public operation gate and pending-close ownership.
- Shutdown cancels and drains retry workers before disposing instrument services.

Reconnect opens the connection only. It never declares a result, restarts an
interrupted sample, changes production counts, drives D2XX/relays or bypasses
physical removal. After full removal, the next installation uses the recovered
connection. Returning to Main remains available after the existing clear-table
confirmation; retry does not depend on the TestView window staying open.

Headless simulations do not open native VISA/COM hardware. The reconnect worker
is tested with controlled fake USB failures: repeated requests retain one owner,
USB return completes reconnect, an unavailable Keysight does not delay Leak,
Dispatcher remains responsive, success stops retry, and cancellation drains a
pending operation. Existing peripheral-error/Back tests remain included.

No physical USB cable was removed during automated verification. The configured
Leak COM must still refer to that device after Windows enumerates it; automatic
selection of an unrelated COM is deliberately excluded.
