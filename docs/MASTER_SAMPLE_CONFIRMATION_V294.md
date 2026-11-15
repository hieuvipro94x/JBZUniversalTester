# V2026.09.294 — Master short groups and manual open-sample confirmation

## Master short / wrong-wire evidence

Confirmed unexpected wiring faults are classified for Master validation by their
actual connected component. Three or more distinct physically connected IOs
qualify as short evidence; a two-IO component qualifies as wrong-wire evidence.
Separate pairs do not combine. Transitive connections and unexpected IOs are
included. Correct common nets from the model never enter the unexpected fault
list and cannot qualify a short sample. Production classification is unchanged.

## Prefaulted open samples

The operator fits the prefaulted sample, inspects the live missing-connection
table, then presses `XÁC NHẬN ĐÃ LẮP XONG MẪU`. There is no automatic completion
based on a configured missing count. Settings show an explanation instead of
the old numeric field. Legacy CFG fields are retained for compatibility but
cannot determine the accepted missing count.

Confirmation rechecks the current complete valid scan after the existing Master
settle period: real product activity, expected-source coverage, at least one
missing expected connection (including CLIP), at least one remaining expected
connection, no wrong-wire/short candidates or confirmed wiring faults, and no
unstable contacts. Device/persistence faults, Probe ownership and stale runtime
contexts block confirmation. The operator is responsible for declaring that
physical installation is finished; the software cannot distinguish a prefaulted
wire from an intentionally uninstalled connection using continuity alone.

The actual missing count and fault details are committed through existing Master
history before JIG eject. Production counters and marking relay are unchanged.
The green completion prompt persists until confirmed physical sample removal.

## Verification

The regression group covers two-/three-IO faults, transitive groups, separate
pairs, unknown IOs, valid common nets, manual confirmation with 1/2/3 missing
connections despite a legacy configured count of 99, no automatic completion,
live wrong-wire rejection, rejection of a good sample, durable Master flow,
green completion, counters and JIG-only relay behavior. Tests use temporary
SQLite databases and fake hardware, not production data. Physical D2XX testing
remains necessary for hardware-specific verification.
