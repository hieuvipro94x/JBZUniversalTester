# V2026.09.326 — Automatic configured open Master

The requested open/terminal-disconnection Master workflow now automatically
accepts the sample when its current missing-connection count equals the shared
`MasterOpenFaultRequiredCount` setting. The former manual installation-confirmation
button and runtime command are removed. Settings exposes the existing persisted
count again; no configuration key or SQLite schema changes are required.

Acceptance still requires a valid current production snapshot, coverage of every
expected source, real product activity, no unstable-contact indication, and no
candidate or confirmed wrong-wire/short fault. Missing counts above or below the
configured target cannot pass. The existing Master installation settle gate,
Probe exclusion, generation checks and confirmed-removal interlock remain in use.
An absent product, zero missing contacts or a target covering all expected
connections cannot pass. Existing count validation retains at least one correct
connection. Physical installation is inferred from electrical topology; this
change adds no new presence sensor or firmware assumptions.

The open-Master badge displays **total expected/current connected**, for example
`20/18`, and appends `OK` after automatic acceptance. Other Master sample types and
production fault classification retain their existing meanings. The accepted
snapshot remains visible during JIG eject/removal; it is not replaced by cleared
engine state. Newly written Master audit traces use the same total/connected order.
Existing history rows are not rewritten.

Accepted NG samples continue through the existing Master persistence/JIG-only
eject path. They do not increment production PASS/FAIL/LOT or trigger marking.
Production remains locked until physical removal and the remaining required
Master samples are complete.

Regression fixtures cover 20-connection models configured for 1, 2 and 3 missing
contacts, partial installation, absent products, incomplete source coverage,
unexpected connections, zero/insufficient missing counts, automatic acceptance,
JIG-only output, held removal gating, exactly-once Master history and child-row
ownership. Databases are temporary fixtures; no production database migration is
performed. Physical board/relay behavior still requires station verification.
