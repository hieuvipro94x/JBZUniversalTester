# V2026.09.275 — PASS audio and relay order

For production models with resistance enabled, the final stable passing measurement
publishes the numeric value and PASS status and starts STAGEOK immediately. Board
route recovery continues concurrently. The sound worker plays STAGEOK synchronously,
then DINGDONG synchronously; blocking WAV playback stays off the UI thread.

The durable PASS path joins that same playback task rather than replaying it.
Models without resistance keep DINGDONG only. Relay execution waits for complete
audio playback, successful board recovery and successful history commit. Existing
relay configuration, Probe interlock, cancellation and runtime/model generation
checks remain in force. Interrupted/failed audio blocks relays with a visible
status; it does not initiate a D2XX restart or rewrite the stored PASS as FAIL.

This supersedes the sound timing description in the V274 presentation note.
Self-tests use controlled audio tasks and fake boards; physical audio/relay timing
still requires a production-machine check.
