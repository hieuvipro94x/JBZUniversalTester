# V2026.09.357 — native discard IO save

The Settings save failure was reproduced on a temporary copy of the station's
`C:\ITEM\2.tht`, without modifying the production file. Its Contents stream has
7571 bytes, outer length 7563 (length minus 8) and inner length 7539 (length minus
32). The writer previously accepted only zero or length minus 20 for the inner
field, so it rejected the file before any replacement.

The discard writer now accepts the observed minus-32 convention as well as the
existing minus-20 and zero conventions. A nonzero inner field changes by exactly
the size delta of Contents, preserving its original convention. Other unknown
length conventions remain rejected. Native parsing, topology verification,
atomic replacement, backups and Leak sidecar rollback remain in place.

Regression coverage exercises both nonzero conventions, unknown-header refusal,
exact original backup, updating/clearing the pair, and the real Settings save
pipeline with a minus-32 fixture. Saving IO 97/98 on the temporary copy of the
station file succeeded after the fix. No hardware operation or production history
mutation was performed by this reproduction.

## V2026.09.358 — integrated editor

The integrated editor had its own strict inner-size check, separate from the
discard writer. Opening a temporary copy of the same `2.tht` reproduced
`Contents inner size không đúng định dạng HarnProg.` before the fix.

Its parser now accepts the observed minus-32 convention alongside minus-20.
Its writer retains the opened file's convention using the original Contents
header; new files continue using minus-20. Unknown offsets still fail validation.
The actual station file passed open, unchanged byte-exact save, and metadata edit
with product/discard topology and the minus-32 header preserved, all on a temporary
copy. The editor regression suite also covers both supported conventions and
rejection of an unknown offset.

## V2026.09.359 — connector name

New discard-contact writes use `_Interlock` in both Connector and Pin tables.
The production parser recognizes `_Interlock` and legacy `_DISCARD`, ignoring
case, and excludes either from product continuity topology and inherited connector
context. Operator descriptions and sensor rows use the new name.

Existing files are not bulk rewritten when selected. An explicit discard write
canonicalizes both tables to `_Interlock`, retaining the configured IO, topology,
backup and Leak sidecar protections. Unchanged Settings saves keep their existing
no-write behavior. Tests cover legacy loading, explicit same-pair migration,
backup preservation and canonical writes.

## V2026.09.372 — preserve unnamed legacy CLIP branches

Existing THTs can contain an empty PIN TYPE within their AO/aN range, including
IO197 of W321798.tht. MergePins already retains that empty value, but the export
validator previously required a generated aN label and rejected the save.
Validation now accepts a retained blank only for an originally blank CLIP branch
at the same IO with the original AO common. Named branches and newly created
CLIP rows retain their existing validation. The runtime parser is unchanged;
an unnamed branch does not become an active aN branch after an unrelated edit.

Regression coverage saves a six-pin fixture with two unnamed branches, verifies
raw PIN rows and runtime CLIP semantics through metadata edits, checks exact
backups, and rejects accidental blanking of an originally named branch. The
dedicated CLI can check supplied files using temporary copies only. This fix
does not change wire-catalog normalization or promise byte-exact whole-file saves.
