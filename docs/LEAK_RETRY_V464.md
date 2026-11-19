# V2026.09.464 Leak retry and Master lifecycle

- Initial Leak triggering is unchanged: all enabled Leak connectors must have valid THT trigger connectivity; the full enabled group runs before full product continuity is required. D2XX scanning remains independent of Leak COM.
- After a measured Leak failure, unplugging and refitting any connector assigned to an enabled Leak channel requests a repeat of every enabled channel, including channels that previously passed. An unrelated connector does not request a retry.
- Refitting must restore the trigger connectivity of all configured Leak connectors. An active COM run finishes before a queued retry can start; unplugging during measurement never creates a second reader.
- Without a retry request, full continuity with a failed Leak result finalizes one production FAIL and its combined History record. No MARKING occurs. Full product removal is required before the next product.
- The same fully fitted failure rejects a good Master and saves Master History while keeping production locked, without changing production counters or pulsing result relays. Full sample removal rearms Master validation.
- Master continuations recheck cancellation, model, runtime generation, sample CycleId and confirmed product removal after awaiting Leak and scan readiness. Old samples cannot arm retry or change the next sample's status.
- No database schema, firmware command, THT topology, measurement formula or timing is changed. Regression tests use isolated temporary databases; real station hardware acceptance is still required.
