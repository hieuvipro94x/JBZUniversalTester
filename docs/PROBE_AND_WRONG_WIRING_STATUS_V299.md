# V2026.09.299: dedicated Probe responsiveness and delayed removal instruction

## Dedicated Probe protocol

The existing verified protocol defines TARGET as that IO's ON event and SOURCE
as its OFF event. Previously the decoder emitted a snapshot for every word and
cleared the active set on C0. On large scans, unrelated normal IOs at the start of
the next scan could therefore emit a false empty snapshot; a two-observation VM
tracker also held up isolated authoritative ON events.

The decoder now retains IO states across C0, emits per-word snapshots only when
the addressed IO actually changes, and clears all retained states on transport,
capacity or mode reset. C0 still emits the complete snapshot. Explicit Probe-mode
events apply immediately in the VM; noisy/inferred Production Probe signatures
retain their existing classification and confirmation policy. No firmware commands,
scan delays, Production fault thresholds or board ownership were changed.

The physical delay before the board transmits the relevant IO word remains; no
claim of measured equivalence to original hardware software is made without trace.

## Wrong-wiring production status

On the existing confirmed wrong-wiring handler, record a monotonic timestamp and
cycle identity. For five seconds the large status names the wrong-wire fault;
then it displays `THÁO SẢN PHẨM LỖI`, in red. This is a presentation-only timer:
confirmation-enabled popup execution, durable FAIL commit, TESTPOINT, scan stop,
relay interlocks and the confirmation-disabled removal flow have no new wait.
System faults and the dedicated NG-discard confirmation presentation retain priority.
Runtime generation, cycle identity and removal phase checks reject stale updates.

Regression coverage: 640-IO prefix after C0 retains IO600, duplicate ON is silent,
explicit ON/OFF updates VM before C0, no production/relay effects, reset clears
contacts; wrong-wire status before/after five seconds under both confirmation
settings, stale cycle/reset exclusion. Tests use fake hardware. Production history
and schema are unchanged; no physical board was exercised.
