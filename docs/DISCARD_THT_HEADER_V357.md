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
