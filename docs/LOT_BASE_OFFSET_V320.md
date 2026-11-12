# V2026.09.320 — Changing the starting LOT retains current production progress

V319 treated a changed starting LOT as a new range beginning at the entered
base, ignoring PASS products already produced that day. The new rule is:

`current LOT = configured starting LOT + this product's daily production PASS count`

When a base is edited for the selected production date, statistics reload uses
that calculation instead of the latest LOT from the newly created batch. Thus
100 PASS followed by saving base 7000 displays and persists LOT 7100; the next
PASS uses 7101. Repeated saves, increases, decreases, model reselection and CFG
reload retain progress exactly once. FAIL, Master and Leak retest records do not
contribute to this count. Existing other-date recovery remains unchanged.

The existing Start gate already awaits statistics reconciliation before a cycle
may reserve its LOT, so no new cycle can print the temporary base-only value.
Daily totals, past label snapshots, production history, SQL schema and old LOTs
are unchanged. No migration or rewrite of production data is performed.

The targeted regression test seeds a temporary DB with 100 PASS and three FAIL,
saves 7000 through the Settings VM, checks display/CFG 7100, sends that number
through the actual label rendering/file-output pipeline, commits and sends the
next production PASS as 7101, checks restart/reselect, lowers the base to 2000
and verifies 2101. Original rows, LOT sums, exactly-once persistence and SQLite
integrity remain intact. Fake board hardware and temporary print files are used;
no physical printer or production database is modified.
