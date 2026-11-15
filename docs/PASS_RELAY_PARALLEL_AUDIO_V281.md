# V2026.09.281 — relay sequence does not await audio

The current requirement supersedes V275: STAGEOK followed by DINGDONG remains a
sequential background audio task, but the production PASS relay sequence no longer
awaits that task. Audio failure or cancellation is logged and cannot change a later
cycle, block relay completion, or cause a D2XX fault. Per-cycle playback deduplication
and immediate sound at the final passing resistance measurement remain intact.

Relay pulse widths, MARKING/JIG role mapping and the configured inter-relay delay
are unchanged. Durable history commit, resistance route recovery, STOP/RESET command
spacing, Probe interlock and runtime/model/cancellation guards remain prerequisites.
Thus removing the audio wait does not mean bypassing board readiness after measurement.

Latest available machine log: V276, 2026-10-20. Its final passing resistance measurement
at 15:09:16.689 precedes the first relay ON command at 15:09:19.222 by 2.533 s.
Recovery occupies 1.776 s of that interval and overlaps audio. Across its eight PASS
cycles, durable PASS UI to first relay ON command is 638–1335 ms. The removed sound
wait contributed to these intervals; they are not measurements of the new release.

Regression tests hold playback pending while the real TestEngine executes configured
relay sequences using a fake board, verify WAV order/deduplication, and observe audio
faults without touching cycle state. Existing measurement/recovery and relay/persistence
tests remain applicable. Physical relay/audio timing requires a new machine log.
