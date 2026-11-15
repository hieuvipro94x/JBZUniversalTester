# V2026.09.416: run enabled Leak channels together

User requirement replaces the earlier per-connector/channel scheduling. For a
model with CH1 and CH2 enabled, fitting CH1 alone must not run Leak. Once both
configured connector triggers are present, one TEST command enables CH1+CH2.
If CH1 fails while CH2 passes, refit CH1; the next attempt measures CH1+CH2,
not CH1 alone. CH2 must remain fitted. The same rule covers CH1+CH3 and three
channels and applies to good Master validation. Single-channel behavior stays.

The trigger topology remains unchanged: declared RET/RT/SEAL or the existing
THT cross-connector fallback. Group readiness does not require unrelated harness
continuity. D2XX continues observing continuity while the independent Leak COM
owns one grouped run. No firmware/protocol/timing changes were made.

A failed-channel profile is retained only to identify the connectors that must
be refitted. It is never used as the next measurement profile. Every retry uses
the full enabled model profile. Group qualification is atomic: separate partial
PASS results cannot accumulate; any qualified connector removal invalidates the
whole group before final product PASS. Durable committed product PASS is intact.

Regression covers real orchestration with fake COM results for two and three
channels, no early start, the exact existing TEST bit mask, mixed FAIL/PASS,
failed-connector-only refit, waiting for the other connector before retry,
full-group remeasurement, atomic qualification, good Master, stale results,
RET/SEAL/fallback topology, no extra product counts or separate Tests rows.
Original per-channel scheduling assertions were updated to this explicit new
requirement; topology, removal, stale-context, relay and history checks remain.

Production hardware was not exercised. Validate simultaneous channel behavior
on the actual Leak machine using its existing supported TEST command.
