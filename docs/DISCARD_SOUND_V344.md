# Discard confirmation audio (V2026.09.344)

For a THT with two `_DISCARD` contacts, an electrical FAIL keeps TESTPOINT until
full product removal is confirmed. Two fresh, complete, valid clean frames in
the current runtime must still satisfy the existing removal evidence. Partial
removal, duplicate or corrupt frames cannot release the sound.

After confirmed full removal, TESTPOINT stops while the NG confirmation remains
locked. The operator sees the existing discard prompt. A fresh sensor OFF-to-ON
edge plays the embedded DRIP acknowledgement once; the existing sensor release
condition remains required before the next cycle. Latched FAIL rows and UI
refreshes must not restart TESTPOINT during this wait.

Ordinary Probe-release/audio cleanup and button clicks cannot cut the DRIP WAV
before its actual duration has elapsed. A new electrical fault or forced shutdown
can still preempt it. No firmware commands, scan timing, relay behavior, database
schema, history rows, LOT or production counts change.

Regression coverage uses simulated board frames and sound ownership/counters.
Physical sensor wiring, speaker output and the audible level of DRIP require a
station hardware check; the WAV file itself is unchanged.
