# V2026.09.296

## Installation prompt before background resistance recovery

After confirmed full PASS-product removal, clear the old results/removal gate,
reset CLIP and product presentation, and show `LẮP SẢN PHẨM` immediately. The
existing full route recovery then runs asynchronously. It no longer holds the
old `THÁO SẢN PHẨM` display or prevents returning to Main on an empty table.

D2XX is a single hardware resource: recovery still intentionally stops scanning
for its existing six writes/five gaps. Raw scan-supervisor verification remains
active; product/Probe evaluation resumes after recovery. New test, scan, manual
relay/measurement and configuration requests wait asynchronously for this owner.
The UI, printing and history are not made to await recovery. Cancelling a waiting
request cannot cancel the recovery owner. A model change defers applying its scan
range to its asynchronous reconciliation; recovery never resets a later cycle's
presentation. Navigation to Main preserves recovery and resumes background scan.

## Leak measurements and final product NG

- Removing a connector during a production Leak run does not cancel its COM
  measurement. Keep its final readings/result and save that attempt's history.
- Observe removal on each accepted production snapshot, even when UI updates
  coalesce. A connector removed/reinserted during the old run cannot reuse that
  run's PASS qualification. Retry starts only after the old result/COM owner ends.
- If the failed connector is already disconnected at completion, wait directly
  for reconnect; do not require another removal of an already absent connector.
- Before full continuity, connector-only retry remains available. Once continuity
  passes with final Leak NG, commit exactly one product FAIL with channel fault
  details and measurements. Retain the NG LEAK table, do not run MARKING, and
  require full product removal before another production cycle. This supersedes
  the older behavior that allowed connector retries after the final NG summary.

## Main navigation under equipment errors

Equipment errors do not themselves prohibit returning to Main. Two distinct,
valid, complete empty production frames establish that the table is clean even
if an equipment-error logical removal gate remains latched. On that path, close
TestView without issuing board commands or clearing the hardware fault. An
occupied table or one empty frame still blocks return. Lost D2XX communication
does not fabricate new physical-removal evidence. Window closing applies the
same physical-product interlock under a device fault.

## Verification

Regression coverage uses fake board/Keysight/Leak replies and temporary SQLite:
immediate installation text/results clearing during a deliberately blocked
recovery, responsive Dispatcher, queued/cancelled new-test requests, immediate
Main navigation and background scan resume, Leak removal while awaiting RESULT,
connector retry, no early product result, final NG counted once with stored
measurements, no MARKING, full-removal rearm, and clean/occupied equipment-error
return to Main. No real production history or schema is modified. Physical
D2XX/Leak hardware testing remains necessary.
