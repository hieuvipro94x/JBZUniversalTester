# V2026.09.286 — early per-connector Leak for the good Master

Good Master inspection now starts an enabled Leak channel as soon as its configured
connector has the same valid local RET/RT edge (or expected inter-connector IO edge)
used by production. The other connectors and full continuity are not prerequisites
for that Leak run. A local edge can also wake WaitingGoodMaster when the rest of a
shared RET network is still incomplete. Probe exclusions, valid-frame checks and
the existing single Leak COM owner remain in force.

Channels execute serially. Each channel has a per-Master qualification bit, separate
from production's Leak cache. Already passed channels are skipped; removing a passed
connector before Master approval invalidates that channel only. A failed channel
uses the existing unplug/reconnect retry loop, scoped to that connector. Removing
the entire sample ends the attempt through the normal Master removal path.

Master confirmation still requires full continuity, every enabled Leak channel PASS
and configured resistance PASS. Resistance routing is followed by scan reacquisition
before cached Leak qualification is trusted. History commit still precedes relay;
early Leak neither records a production/retest result nor increments LOT/production
counters. NG Master samples remain electrical-only. New sample history resets the
Master Leak mask; stale run results are rejected using the Master cycle identity.

Tests inject measurement replies without opening serial ports and use a temporary
history database. Coverage includes ordinary connector isolation, partial shared RET,
Probe exclusions, NG sample isolation, channel serialization, failed-channel retry,
cached PASS invalidation/reconnect, incomplete-continuity gating, stale sample replies
and no early relay/counter changes. Existing production Leak, Master resistance and
Master history regressions are also run. Real Leak hardware has not been exercised.

The end-to-end confirmation test also verifies one JIG pulse, no MARKING pulse,
unchanged production counters and the completed good-Master status while waiting
for removal. That verified status keeps its existing green PASS presentation even
when the product-removal interlock is active; device and persistence faults retain
priority.

Verification: Release build succeeds with zero warnings/errors. Targeted early
Master Leak, production Leak, Master resistance and Master history tests pass.
Full self-test suite: 110/114 PASS. The four pre-existing failures remain in learned
diagnostic topology, D2XX resistance configuration, final status guards and production
fault debounce; no additional failing group was introduced.
