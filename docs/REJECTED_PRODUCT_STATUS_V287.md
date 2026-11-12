# V2026.09.287 — explicit product rejection and removal instruction

Confirmed wrong-wiring/short-circuit production failures awaiting removal, and
measured product FAIL/CHƯA ĐẠT results, now display two lines:
`SẢN PHẨM KHÔNG ĐẠT` / `THÁO SẢN PHẨM`, in red with white text.
Configured discard-bin confirmation retains priority.

A rejected good Master (wiring, resistance, or lost final continuity) uses the same
message. The Master remains locked until confirmed full removal, then returns to
`KIỂM TRA MẪU ĐẠT`. It does not approve production or pulse a failure relay.

Early Master Leak failures retain the per-channel retry behavior: the operator sees
`SẢN PHẨM KHÔNG ĐẠT` / `THÁO CONNECTOR LEAK`, then the reconnect prompt after that
connector is removed. This prompt takes priority over a generic pending-removal
label while the existing Master Leak retry owner is active. A successful Master
still keeps its green completion status. The final production Leak summary is
unchanged.

These changes project existing states into operator text and colors; they introduce
no delay, timer, counter change, history format change, or hardware command change.
Device, configuration and persistence faults are not classified as product FAIL.

Targeted checks cover confirmed wiring rejection and TESTPOINT, rejected Master
resistance/full removal, per-channel Master Leak retry, production Leak regression,
bound production status notifications, discard priority and D2XX fault separation.
Real hardware has not been exercised.

Release build: zero warnings/errors. All targeted checks pass. Full self-tests:
110/114 PASS, with the same four pre-existing failures as V286 (learned diagnostic
topology, D2XX resistance configuration, final status guards, production fault
debounce). Existing assertions for the former generic product-failure text were
updated to the explicitly requested removal message.
