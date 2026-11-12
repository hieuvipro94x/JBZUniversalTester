using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestDatabaseBackupExitCleanup()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-DbBackupCleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string db = Path.Combine(root, "JBZUniversalTester.db");
            var store = new TestHistoryStore(db);
            var model = Model(("PAIR", new[] { 1, 2 })); model.PartNumber = "CLEANUP-PRESERVE";
            var now = DateTime.Now;
            store.CommitResult(ProductionResultCommitRequest.Capture(new TestHistoryRecord
            { CycleId = "retained-result", PartNumber = model.PartNumber, ModelName = model.ModelName,
                Started = now, TestStartedAt = now, ResultAt = now, Finished = now, Passed = true,
                Result = "PASS", InspectionType = HistoryInspectionType.Product, LotNo = 2020 },
                model, new ProductionSettings(), [], [], null, ProgramIdentityService.VersionText), null);
            string before = HistoryDataSnapshot(db);
            string[] deleted = [db + ".pre-schema-v8.backup", db + ".pre-schema-v8.20261005_102149_0713655.backup",
                db + ".pre-schema-v9.backup", db + ".pre-schema-v9.20261109_101845_3705560.backup"];
            string[] retained = [Path.Combine(root, "other.db.pre-schema-v9.backup"), db + ".manual.backup",
                db + ".pre-schema-v9.backup.notes", Path.Combine(root, "JBZUniversalTester.cfg"),
                Path.Combine(root, "current.tht"), db + ".pre-schema-v9.locked.backup"];
            foreach (string path in deleted.Concat(retained)) File.WriteAllText(path, "preserve-or-remove");
            string nested = Path.Combine(root, "Nested"); Directory.CreateDirectory(nested);
            string nestedBackup = Path.Combine(nested, Path.GetFileName(deleted[0])); File.WriteAllText(nestedBackup, "nested");
            var cleanup = new DatabaseBackupCleanupService(); cleanup.TrackInitializedDatabase(db);
            Assert(deleted.All(File.Exists), "Migration recovery copies survive until orderly application exit");
            cleanup.CleanupAsync().GetAwaiter().GetResult();
            Assert(deleted.All(path => !File.Exists(path)) && retained.All(File.Exists) && File.Exists(nestedBackup),
                "Only exact pre-schema backup filenames of this database are deleted, without scanning subdirectories");
            Assert(HistoryDataSnapshot(db) == before && ScalarInt(db, "SELECT COUNT(*) FROM Tests WHERE CycleId='retained-result' AND Lot=2020;") == 1,
                "All production rows, child data and stored LOT remain unchanged after exit cleanup");
            using (var connection = new SqliteConnection($"Data Source={db};Mode=ReadOnly"))
            {
                connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
                Assert((string?)command.ExecuteScalar() == "ok", "Live database integrity remains valid after cleanup");
            }
            string broken = Path.Combine(root, "damaged.db"); File.WriteAllText(broken, "not a database");
            File.WriteAllText(broken + ".pre-schema-v9.backup", "last recovery copy");
            cleanup.TrackInitializedDatabase(broken); cleanup.CleanupAsync().GetAwaiter().GetResult();
            Assert(File.Exists(broken + ".pre-schema-v9.backup"), "A damaged active database retains its recovery backup");
            string missing = Path.Combine(root, "missing.db"); File.WriteAllText(missing + ".pre-schema-v9.backup", "last copy");
            cleanup.TrackInitializedDatabase(missing); cleanup.CleanupAsync().GetAwaiter().GetResult();
            Assert(File.Exists(missing + ".pre-schema-v9.backup") && !File.Exists(missing),
                "Missing active database cannot be silently recreated or lose its recovery backup");
            string locked = db + ".pre-schema-v9.backup"; File.WriteAllText(locked, "locked copy");
            using (var handle = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                cleanup.TrackInitializedDatabase(db); cleanup.CleanupAsync().GetAwaiter().GetResult();
                Assert(File.Exists(locked), "Locked backup is logged without aborting shutdown");
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
