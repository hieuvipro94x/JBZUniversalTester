# Production hardening V2026.09.462

- Existing empty, malformed, unreadable or duplicate-key CFG files stop startup instead of silently enabling default hardware/relay settings. Reads share the atomic writer's gate so concurrent in-process LOT/settings saves do not cause false sharing violations. Fresh installations still initialize defaults; valid partial legacy CFG files remain supported. Configuration and history files are retained.
- The asynchronous log queue holds at most 4096 pending entries. Producers never wait for storage. A failed append retains its batch and retries once per second; overflow is counted and reported when writing recovers. `LastWriteError` and `DroppedLogCount` expose storage/overflow status. Shutdown retains its two-second drain limit.
- XLSX text uses SpreadsheetML `_xHHHH_` escapes for invalid XML code units. Literal escape-looking text is protected and valid Unicode is retained. Stored history and raw printer payloads are unchanged. CSV retains its existing encoding and format.
- LAN delivery uses `Scripts/Copy-VerifiedRelease.ps1`. Before replacing an existing version folder it preserves and verifies the entire folder in a timestamped sibling `.preserved` directory. It retains destination-only runtime files and checks SHA256 for every source file after copying. Linked paths and overlapping source/destination trees are rejected. A failed copy/verification still permits Git delivery but returns overall failure; an unavailable LAN remains an allowed skip.
- `SQLitePCLRaw.lib.e_sqlite3` is pinned to 2.1.14 to replace the vulnerable native engine, retaining `Microsoft.Data.Sqlite` 8.0.12 and its API. The resolved Windows x86 native engine reports SQLite 3.54.0. NuGet vulnerability audit reports no known vulnerable packages.
- History date/type paging selects rows with the existing timestamp semantics and stable Id cursor first, then calculates unfiltered per-product/batch ordinal prefixes through the existing index. Sparse text/fault/LOT searches retain single-pass ranking to avoid repeatedly counting large gaps. Master rows do not consume product ordinals. Export still streams the complete filtered dataset. No schema/index migration is added.

## Regression and performance verification

```powershell
dotnet build -c Release
dotnet run --project Tests -c Release
dotnet run --project Tests -c Release -- --test-production-hardening
dotnet run --project Tests -c Release -- --test-history-export
dotnet run --project Tests -c Release -- --benchmark-history-million
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Scripts/Test-CopyVerifiedRelease.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Scripts/Test-DailyBuildBranch.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Scripts/Test-SyncBuildSource.ps1
dotnet list JBZUniversalTester.csproj package --vulnerable --include-transitive
```

On the development machine, the temporary one-million-row fixture returned the first 200 rows in 31 ms versus 1143 ms for the former global-ranking query. Near the end of the dataset, keyset loading took 237 ms; the baseline global-ranking/offset query took 1481 ms. These timings are machine-specific and are not hardware acceptance thresholds. Both queries returned identical Id/ordinal pairs; fixture integrity remained `ok`.

All test databases are temporary. No production CFG/database is migrated, reset or rewritten by these verification commands. Real D2XX, Leak and printer acceptance remains a separate on-station check.

Release verification: Release build has zero warnings/errors; all 218 application self-tests pass. Release-copy/final-status tests pass 13 checks, daily-branch tests pass 10 checks, and source-sync tests pass 11 checks.
