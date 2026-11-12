# V289 — persistent electrical TESTPOINT warning (included in V290)

Confirmed wrong/short wiring, confirmed missing-connection failures handled by the
fault workflow, real live-topology connections in an empty model, and runtime D2XX
device faults acquire a persistent TESTPOINT hold. A confirmed Master electrical
fault uses the same audio hold without becoming a production FAIL or incrementing
counters. The existing Probe classifier and its isolation from product evaluation
remain unchanged. A Probe operation cannot silence an already held alarm.

Ordinary SetWiringFaultAlarm(false), StopAll, clicks, PASS, Leak NG, discard and
Probe-release sounds cannot release the hold. Only fresh complete empty production
scan evidence (two distinct frames in the current runtime/scan context), combined
with a ready product/sample-installation state and cleared removal/discard/error
gates, may stop it. Invalid, partial, duplicate, stale or still-connected frames
cannot prove removal. MainWindow continues observing removal after TestView closes.
The navigation reset preserves previously confirmed presence, so a product removed
during the transition does not require an extra connected frame to unlock Main.

Application shutdown/disposal explicitly stops all native audio resources. D2XX
faults retain the existing restart policy. The hold does not add scan commands,
production results, relay pulses, counters, popup confirmations or fault
classification rules.

PASS playback starts each WAV asynchronously under the audio gate and protects the
actual WAV duration before starting the next one. This lets TESTPOINT preempt it
without a delayed old PlaySync replacing the warning. Playback generations reject
stale completion; normal cleanup resets the pending PASS flag/task so future PASS
audio can start. Relay sequencing remains independent of sound completion.

Tests cover navigation, partial removal, invalid/duplicate scans, empty-model multi-IO
connections, Master/Probe operations, competing sounds, PASS preemption and shutdown.
Each simulated application resets audio at test boundaries; production UI cleanup
cannot use that forced test/shutdown path. Real hardware/audio-device behavior still
requires verification on the station.
