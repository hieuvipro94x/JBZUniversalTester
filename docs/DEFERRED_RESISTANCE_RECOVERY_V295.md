# V2026.09.295 — PASS relay before full resistance recovery

## Requested production order

1. Last configured resistance channel passes; the measured table/header update
   together and retain STAGEOK followed by DINGDONG, once per product.
2. Commit the complete product PASS/history/counters through existing durable
   persistence. Run the configured MARKING/JIG relay sequence immediately after
   that commit, independently of audio completion.
3. Resume production scanning to observe physical removal. D2XX still performs
   its existing normal scan initialization when the measurement route invalidated
   the prepared scan state. This is required to observe all IOs; it is not the
   full three-pair resistance recovery.
4. Only two fresh valid complete empty frames confirming full product removal
   release the deferred full recovery. Partial removal or one empty frame cannot
   do so.
5. Stop the removal-monitoring stream, run the existing six recovery writes and
   five 350 ms gaps, verify a fresh scan, then rearm the next product.

The approximately 1.8 seconds of full route recovery no longer precedes PASS
relays. This moves that work to after physical removal; it does not eliminate
hardware scan initialization, relay command settle times, configured pulse
widths or the configured MARKING-to-JIG gap.

## Boundaries

- Only successful automatic production resistance opts into deferral. Master,
  manual measurements, measurement FAIL and cancellation retain immediate
  existing cleanup.
- An uncommitted or rejected product PASS cannot drive relays and cleans up its
  deferred route instead. Failed hardware recovery retains its pending flag,
  enters the D2XX device-fault path and cannot rearm/erase the committed PASS.
- The next cycle/navigation remains interlocked until deferred recovery and its
  fresh scan verification complete. Concurrent/duplicate removal callbacks are
  serialized; shutdown awaits an active removal recovery before disconnecting.
- No firmware commands, thresholds, instrument sampling rules, SQLite schema or
  production history data are changed.

## Verification

`TestDeferredResistanceRecovery` runs the real production post-continuity flow
against fake D2XX/Keysight, a temporary SQLite history and a deliberately pending
audio task. It verifies PASS and both relay pulses before recovery/audio finish,
partial-removal and one-frame rejection, a deliberately delayed recovery keeping
the next-cycle gate closed, single recovery despite repeated callbacks, rearm
after fresh scanning, failure retaining committed counters, and immediate
measurement FAIL/cancel cleanup. Existing trace timing, audio, Master and
production regressions remain applicable. Physical hardware verification is
still required, particularly the scan initialization used for removal monitoring
before the deferred full recovery.
