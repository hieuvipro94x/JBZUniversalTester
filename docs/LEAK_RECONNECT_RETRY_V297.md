# V2026.09.297: remember connector reinsertion during Leak measurement

When a configured Leak connector disconnects during a production measurement,
the independent COM measurement still completes and its actual result is retained
in Leak attempt history. That run cannot qualify the reinserted connector.

Previously, reinsertion before RESULT was forgotten by the retry gate: it required
another unplug/replug. The retry gate now accepts removal already observed during
the completed run. If reconnected, the next authoritative scan starts the retry;
if still disconnected, it waits for reconnection. Each required retry connector
must independently satisfy removal and reconnection. Unremoved failed channels
still require their normal unplug/replug. Durable product results remain immutable.

PASS measurement invalidated by connector removal is logged explicitly instead of
the misleading empty `FAIL kênh` message. Waiting status uses the connector waiting
projection rather than blindly using the machine's PASS flag.

This does not implement an immediate hardware reset while the machine is measuring.
The verified protocol in WaterProofSerialService only sends `:TEST`; no documented
STOP/RESET command or evidence that a new TEST interrupts an old measurement is
available. Closing the PC COM handle does not prove a machine measurement stopped.
Immediate restart requires that device protocol information and stale-result handling.

Regression coverage uses fake hardware and a temporary SQLite database: initial
run and retry both finish without cancellation, each remembered reinsertion starts
exactly one subsequent run, no intermediate production count/relay is emitted,
actual Leak failures are retained, and normal full-removal gating is preserved.
No real production history is modified and no physical Leak machine is tested.
