# V2026.09.298: combined Leak/resistance results for good Master samples

The shared result table previously required production Leak qualification and a
production continuity timestamp. Good Master samples use a separate qualified
channel mask, so their Leak measurements were missing beside resistance results.

PopulateFinalWaterProofRows now accepts the current good Master qualification and
current continuity PASS while the Master sequence is active. Production retains
its existing qualification/timestamp guards. Bad Master states are excluded.
The enabled-channel count and actual PASS measurements are still validated.

Preparing the resistance rows therefore includes qualified Leak rows immediately.
Good Master completion also opens the shared table for Leak-only samples and keeps
it visible through the eject/removal stage. Existing full-removal cleanup clears
both measurement types before the next sample.

Regression checks cover live resistance with qualified Master Leak rows (without
production qualification flags), two-channel Leak-only completion, partial removal
retaining results, and full removal clearing both types before the bad sample.
No protocol, relay timing, production count, or database schema is changed.
Hardware behavior is covered by fakes; no physical Leak/Keysight hardware is used.
