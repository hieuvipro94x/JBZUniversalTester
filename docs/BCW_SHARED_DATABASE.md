# BCW storage from V2026.09.448

BCW reservations, serials, saved print requests and send/reprint state now use
`BcwPrints` in `%ProgramData%\JBZUniversalTester\Data\JBZUniversalTester.db`.
Production History/LOT and label sequence policies remain independent.
New deployments do not create or copy `Data\BCW-labels.db`.

After History database initialization, startup imports the old BCW journal once.
It first looks for `Data\BCW-labels.db` beside the EXE or `BCW-labels.db` beside
the shared database. If neither exists, it reads previous version directories.
Identical rows are deduplicated; conflicting CycleIds, serials or outstanding
manual reservations stop startup and roll back the entire import. No source
is selected by file size. To resolve conflicting previous releases, place the
actual station's BCW journal at `Data\BCW-labels.db` beside the EXE and retry.
Existing BCW History with no recoverable journal also stops migration rather
than silently resetting the serial counter.

Migration validates SQLite integrity, retains a timestamped
`JBZUniversalTester.db.pre-bcw-merge-*.backup`, imports transactionally, checks
History row counts and foreign keys, and records completion in
`RuntimeMigrationState`. Later launches do not reimport retained old journals.
All original files remain untouched for recovery. Keep the pre-merge backup
and source journals until the station's serials/reprints have been verified.

Only the shared database is active after migration. SQLite may maintain its
normal `-wal`/`-shm` companions while the application runs; these are not
independent label databases. Use SQLite backups rather than copying a live DB
without its WAL.
