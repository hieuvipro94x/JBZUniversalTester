# Station configuration storage

From V2026.09.447, production settings are read and written as
`%ProgramData%\JBZUniversalTester\Data\JBZUniversalTester.cfg`, beside the station
`JBZUniversalTester.db`. The CFG format is unchanged. Self-tests use their isolated
database directory for the CFG as well.

On first launch, an existing shared CFG is kept. Otherwise, the CFG beside the EXE
is copied without deleting or modifying the source. If that file is missing,
identical CFG files in previous release directories may be inherited. Different
CFG files stop startup; place the actual operating station's CFG beside the EXE
and retry. No CFG is silently selected by LOT or release date.

Legacy JSON/UniversalTester.cfg loading remains supported when no canonical CFG
is available. Database relocation rules and history contents are unchanged.
