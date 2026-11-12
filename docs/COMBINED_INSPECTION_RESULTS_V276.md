# V2026.09.276 — combined measurement results

TestView uses a single result DataGrid for resistance and final Leak results.
Resistance rows appear first, followed by one row per enabled Leak channel.
Resistance-only products hide all pressure/leak columns; Leak-only products hide
the resistance MIN/value/MAX columns. Both types retain their original units and
per-row PASS/FAIL/NG LEAK styles. The center PASS overlay cannot cover this table.

The display adapter observes the authoritative result objects, so the final numeric
resistance reading, its PASS and the large status retain their existing timing.
Preparing resistance retains already completed Leak results, and final Leak
presentation retains measured resistance rows. Returning to continuity/removal
clears the Leak display through the existing cycle lifecycle.

Measurement, retry, persistence, audio and relay sequencing are unchanged. No CFG,
hardware protocol or history schema/data is modified. Regression tests cover all
three enabled-type combinations, live result updates, NG Leak, cleanup and absence
of relay commands. The preview command renders the actual TestWindow without
starting its hardware lifecycle:

`dotnet run --project Tests -c Release -- --preview-combined-results <png-path>`
