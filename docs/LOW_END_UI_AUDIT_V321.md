# V2026.09.321 — Recent workflow responsiveness audit

Scope: product selection, initial LOT/template setup, LOT offset recovery after
saving Settings, final PASS persistence, independent peripheral reconnect and
shutdown. History/SQLite schema and hardware protocols are unchanged.

## Findings and changes

The V320 LOT service performed synchronous CFG persistence under the same lock
used by UI counter/date reads. Its production callers could run on Dispatcher.
Slow disk flush could therefore block UI input during statistics reconciliation
or PASS. Simply wrapping the call in Task.Run would still block UI readers on
the counter lock.

Production now uses asynchronous counter operations with detached settings and
a separate worker-owned counter lock. Live counters publish only after durable
write acknowledgement; failures retain the previous LOT and retry reservation.
The existing synchronous API remains for compatibility. The current-product
revision is checked before applying an old completion, and only the affected
product counter/date is copied back, never worker normalization of other fields.
The expensive deep serialization runs on the worker. UI capture freezes the
small mutable counter entries and copies the settings header.

Counter reads also use direct per-product lookup, rather than normalizing every
profile in the entire station configuration on each clock/read operation.

Runtime CFG writes share the existing model-persistence semaphore. Explicit
model selection and opening Settings asynchronously drain pending writes before
capturing configuration. Selection generation advances immediately, retaining
the rule that newer selections supersede old parsing. Shutdown tracks/drains
the accepted product-result transaction before disposing services; duplicate
rejected calls cannot replace that outstanding transaction. Initial-product
setup also clones configuration off Dispatcher and rechecks selection/removal
afterwards.

The peripheral retry workers already perform their native connect/discovery
work in the background, one owner per device. The existing delayed USB and
driver ownership regressions remain in the complete suite. The picker already
coalesces partial-name filtering and virtualizes the 1000-file list; no hardware
or timing behavior was changed by this audit.

## Measurements and limits

Test fixture: 1000 product configurations, CFG writer held for 300ms, Dispatcher
input timer reading LOT/date during the hold, disk failure/retry, product switch,
20,000 counter reads and asynchronous shutdown draining. The frozen write
cannot block those live counter reads or overwrite unrelated UI settings.

Focused runs: maximum input interval about 30–35ms, 20,000 reads about 5–6ms.
Separate process restricted to one allowed logical CPU, BelowNormal priority:
maximum input interval about 29ms and 20,000 reads about 3ms. These are observed
measurements on the available host, not a guarantee for every older CPU, GPU,
driver or disk. A blocked disk still delays durable acknowledgement before the
production sequence proceeds; the change keeps that wait off Dispatcher and
preserves existing PASS/relay ordering rather than weakening persistence safety.

Final verification ran the entire suite in a separate process restricted to one
logical CPU at BelowNormal priority: **141/141 PASS**, build zero warnings/errors.
That complete run measured 31.961ms maximum LOT-test input interval, 15.01ms for
20,000 counter reads, and staged Settings preparation totals 159–249ms at
1366/1920 widths. Individual Settings batches yielded to Dispatcher between
panels (largest observed batch about 81ms). A 500-IO/250-network engine fixture
processed 2,000 identical frames in 72ms. These timings vary with scheduling/JIT
and are synthetic measurements, not physical board scan latency.

Tests use fake hardware and temporary data. Actual low-end production hardware
and physical USB/printer/relay timing were not measured in this audit.
