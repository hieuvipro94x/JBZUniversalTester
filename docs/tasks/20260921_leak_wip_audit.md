# Leak WIP follow-up — 2026-09-21

Baseline: `wip/leak-window-20260920`, HEAD `6484e53`, clean working tree.
Compared all WIP source changes against `d99bad5`. All three LeakWindow files
were present. Initial Release build: zero warnings/errors. Initial SelfTests:
47/51 PASS. The WIP reset guard already prevented the previous StackOverflow.

## Root causes and corrections

- Learned topology test still required explanatory UI text deliberately replaced
  by commit `4b61e7f`. Updated the caption expectation, preserving diagnostic
  persistence, non-THT format, grid binding and virtualization checks.
- Presence was cleared by a scan generation change. Removal was defined as the
  negation of confirmed presence, so a reset or first connected frame could
  incorrectly release a Master/Leak removal gate. Removal now requires two
  complete no-connectivity frames, independently of presence. Scan restart
  preserves an already confirmed presence latch. The second removal frame emits
  Changed even if presence was not previously confirmed.
- WIP's extra “saw product” flags missed recovery entry points and could leave
  uncommitted-FAIL recovery locked. All paths now use the engine confirmation.
  Nested presentation callbacks are ignored during full-cycle reset. Existing
  incomplete normal/CLIP removal regression completes without recursion.
- Updated old single-empty-frame fixtures to assert the gate remains locked
  after frame one and opens after frame two. Stress coverage now requires exactly
  two startup notifications (snapshot and removal confirmation), then no more
  notifications across the remaining 498 identical frames.
- The WIP COM gate could release before a timed-out Open returned and scheduled
  Close. Late-open cleanup is registered before its worker exits. Handle detach
  and close registration are atomic with the gate-release check. Connect,
  disconnect and test all use deferred release. Close waits for the native
  reader/writer to exit; UI timeout/cancel does not wait for that lock. If Dispose
  fails, reopening remains blocked. Manual open registration rejects a canceled
  lifecycle; disposed services reject new operations.
- Coalescing could discard the last PRESS when WAIT replaced it. The coalescer
  retains the PRESS reference and latest WAIT with one pending UI update. Old-run
  callbacks are rejected. Official RESULT evaluation is unchanged; returned
  official measurements replace the temporary display estimate.
- Kept Leak cards removed from TestWindow. The owned window remains enabled-model
  only, after continuity/resistance, nonmodal and nonactivating. Added stale-open,
  duplicate-open, cancellation and late-final guards. Placement converts screen
  pixels to WPF units; flexible rows preserve room for stage text.
- Manual exit is idempotent. Settings progress stays in runtime properties;
  current Save/SettingsSaved is called only by the explicit settings save path.
- D2XX stopped reader now waits for scan-resume/cancel events, and rechecks scan
  state under the I/O lock before accessing the queue. No protocol commands or
  timing constants were changed.

## Verification

Release version metadata synchronized to 16.0.372. No merge, commit or publish.
SelfTests expanded to 54 cases, adding late-open/close gate, progress coalescing
and stale-run, and suspended-reader/resume regressions. Existing Leak case also
checks disabled/zero-channel models and exactly-once window requests. Manual
cleanup regression checks repeated exit emits no additional board commands.

No production database, schema, dependencies, topology or official Leak thresholds
were changed. History/migration/export/exactly-once/integrity coverage runs against
the existing self-test temporary databases.

## Required hardware acceptance

- Actual Leak PRESS → WAIT → RESULT PASS/FAIL; verify the last PRESS reference
  matches the machine's intended temporary estimate. Official verdict remains
  based only on RESULT pairs and existing thresholds.
- Missing RESULT, unplug during Open/Read/Write, slow Close, cancel/close window,
  and immediate retry: only one COM owner; CLOSING blocks reopen until safe.
- Manual Leak and Settings close: no production count/history or marking relay,
  one manual exit/resume, independent printer COM unaffected.
- Production continuity/resistance fail bypasses Leak; Leak FAIL marks no PASS
  relay; confirmed removal is required before the next cycle, including Master
  and return-to-Main paths.
- Real D2XX manual/Leak pause: measure CPU, queue/read rate, first-frame latency
  on resume, and manual resistance response routing.
- WPF responsiveness, stage visibility and positioning at 100/125/150% DPI,
  secondary monitors, owner movement/minimize/close and model change.

No physical Leak, D2XX, relay or printer test was performed in this session.
