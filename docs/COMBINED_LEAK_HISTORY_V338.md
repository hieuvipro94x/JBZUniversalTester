# Combined continuity and Leak history (V2026.09.338)

New production cycles persist one PRODUCT record at their final PASS/FAIL verdict.
Early Leak runs and connector retries no longer create LEAK_RETEST records. Their
timestamps, channel readings and verdicts are appended to that product's Korean
inspection trace. The final channel readings remain attached to the same Test in
WaterProofMeasurements. Production totals and LOT remain based on the final
product verdict, never on individual Leak attempts.

History labels combined products `회로·기밀 검사` and legacy LEAK_RETEST records
`기밀 검사`. Existing database rows, identifiers and child measurements are not
rewritten or removed. Legacy rows remain separate, queryable and exportable.
The legacy type filter therefore applies to those saved standalone rows; new
combined cycles remain production records.

Attempts are retained in memory until the product is finalized. An abandoned or
interrupted cycle does not create a new standalone Leak history record. Diagnostic
application logs remain independent. No schema migration is required.

Excel/CSV retain the existing 18-column format. The 검사기록 cell contains both
continuity and Leak results, including failed attempts before a successful retry,
on the same product row.

Verification uses isolated temporary databases and simulated board/Leak results.
No production database or physical device is modified by the preview.
