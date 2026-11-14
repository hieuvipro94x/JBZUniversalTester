using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void CreateBcwMergeSource(string path, string cycle, long serial, string state,
        bool legacy = false, int capture = 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS BcwPrints(CycleId TEXT PRIMARY KEY,Scope TEXT NOT NULL,Serial INTEGER NOT NULL,
                State TEXT NOT NULL,RequestJson TEXT NOT NULL,Message TEXT NOT NULL DEFAULT '',UpdatedAt TEXT NOT NULL
            """ + (legacy ? "" : ",ProductionCapture INTEGER NOT NULL DEFAULT 0") + ",UNIQUE(Scope,Serial));";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO BcwPrints VALUES($Cycle,'20261014|09|LH',$Serial,$State,$Json,'saved diagnostic','2026-10-14T10:20:30'" +
            (legacy ? "" : ",$Capture") + ");";
        command.Parameters.AddWithValue("$Cycle", cycle);
        command.Parameters.AddWithValue("$Serial", serial);
        command.Parameters.AddWithValue("$State", state);
        command.Parameters.AddWithValue("$Json", JsonSerializer.Serialize(
            BcwLabelService.Bind(BcwRequest(cycle), new DateTime(2026, 10, 14), serial)));
        if (!legacy) command.Parameters.AddWithValue("$Capture", capture);
        command.ExecuteNonQuery();
    }

    private static void TestBcwSharedDatabaseMigration()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-BcwShared-" + Guid.NewGuid().ToString("N"));
        try
        {
            string db = Path.Combine(root, "Shared", "JBZUniversalTester.db");
            var store = new TestHistoryStore(db);
            var model = Model(("PAIR", new[] { 1, 2 })); model.PartNumber = "BCW-KEEP-HISTORY";
            var started = new DateTime(2026, 10, 14, 9, 0, 0);
            store.CommitResult(ProductionResultCommitRequest.Capture(new TestHistoryRecord
                { CycleId = "keep-history", PartNumber = model.PartNumber, Started = started, TestStartedAt = started,
                    Finished = started.AddMinutes(1), ResultAt = started.AddMinutes(1), Passed = false, Result = "FAIL", LotNo = 2345 },
                model, new ProductionSettings(), [new FaultDetail { Type = ProductFaultType.OpenCircuit, Message = "KEEP" }],
                [], null, ProgramIdentityService.VersionText) with
                { Resistance = [new ResistancePersistenceSnapshot(1, "R1", 100, 90, 110, true, 1, 0)],
                  WaterProof = [new WaterProofPersistenceSnapshot(1, true, 80, 79, 1, true)] }, null);
            string before = HistoryDataSnapshot(db);
            string old = Path.Combine(root, "Old", "Data", "BCW-labels.db");
            string pending = Path.Combine(root, "Other", "Data", "BCW-labels.db");
            CreateBcwMergeSource(old, "sent", 14, "Sent", legacy: true);
            CreateBcwMergeSource(pending, "pending", 15, "Failed", capture: 1);
            string duplicate = Path.Combine(root, "duplicate.db"); File.Copy(old, duplicate);
            byte[] oldHash = SHA256.HashData(File.ReadAllBytes(old)), pendingHash = SHA256.HashData(File.ReadAllBytes(pending));
            BcwDatabaseMigrationService.Ensure(db, () => [old, pending, duplicate]);
            Assert(HistoryDataSnapshot(db) == before && ScalarInt(db, "SELECT COUNT(*) FROM BcwPrints;") == 2,
                "Merge retains History identities, faults, resistance/leak children and deduplicates identical BCW rows");
            Assert(ScalarInt(db, "SELECT ProductionCapture FROM BcwPrints WHERE CycleId='sent';") == 0 &&
                ScalarInt(db, "SELECT ProductionCapture FROM BcwPrints WHERE CycleId='pending';") == 1 &&
                ScalarText(db, "SELECT State||'|'||Message||'|'||UpdatedAt FROM BcwPrints WHERE CycleId='pending';") ==
                    "Failed|saved diagnostic|2026-10-14T10:20:30", "Legacy capture defaults to zero; pending state and diagnostic fields remain exact");
            string backup = Directory.GetFiles(Path.GetDirectoryName(db)!, "*.pre-bcw-merge-*.backup").Single();
            Assert(HistoryDataSnapshot(backup) == before && ScalarText(backup, "PRAGMA integrity_check;") == "ok" &&
                ScalarInt(backup, "SELECT COUNT(*) FROM sqlite_master WHERE name='BcwPrints';") == 0,
                "Timestamped SQLite backup preserves the complete database before BCW schema mutation");
            BcwDatabaseMigrationService.Ensure(db, () => throw new Exception("Completed migration must not probe sources"));
            var journal = new BcwSerialStore(db);
            Assert(journal.Find("sent")!.Data.BcwSerial == 14 && journal.Find("pending")!.Data.BcwSerial == 15 &&
                journal.Peek(new DateTime(2026, 10, 14), "09", "LH") == 16,
                "Restart and reprint retain immutable serials and next serial follows migrated journal");
            var next = journal.Prepare(BcwRequest("next"), new DateTime(2026, 10, 14), false, productionCapture: true);
            Assert(next.Request.Data.BcwSerial == 16 && HistoryDataSnapshot(db) == before &&
                SHA256.HashData(File.ReadAllBytes(old)).SequenceEqual(oldHash) &&
                SHA256.HashData(File.ReadAllBytes(pending)).SequenceEqual(pendingHash),
                "New BCW reservation touches neither production History nor original source databases");
            Assert(ScalarText(db, "PRAGMA integrity_check;") == "ok", "Merged SQLite integrity remains ok");
            using var connection = new SqliteConnection($"Data Source={db};Pooling=False"); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_key_check;";
            using var rows = command.ExecuteReader(); Assert(!rows.Read(), "All original child rows remain attached to their Tests");
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void TestBcwSharedDatabaseConflicts()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-BcwConflicts-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string kind in new[] { "cycle", "serial", "pending", "corrupt", "existing" })
            {
                string directory = Path.Combine(root, kind), db = Path.Combine(directory, "JBZUniversalTester.db");
                var store = new TestHistoryStore(db);
                store.Add(new TestHistoryRecord { CycleId = "keep", Started = DateTime.Now, Finished = DateTime.Now,
                    PartNumber = "KEEP", Result = "PASS", Passed = true });
                if (kind == "existing") CreateBcwMergeSource(db, "existing-cycle", 1, "Sent");
                string before = HistoryDataSnapshot(db);
                string a = Path.Combine(directory, "a.db"), b = Path.Combine(directory, "b.db");
                CreateBcwMergeSource(a, "one", 1, kind == "pending" ? "Failed" : "Sent");
                if (kind == "corrupt") File.WriteAllText(b, "not a database");
                else CreateBcwMergeSource(b, kind == "cycle" ? "one" : "two", kind == "serial" ? 1 : 2, "Failed");
                bool rejected = false;
                try { BcwDatabaseMigrationService.Ensure(db, () => [a, b]); }
                catch (Exception ex) when (ex is InvalidDataException or SqliteException) { rejected = true; }
                Assert(rejected && HistoryDataSnapshot(db) == before &&
                    (kind == "existing"
                        ? ScalarInt(db, "SELECT COUNT(*) FROM BcwPrints WHERE CycleId='existing-cycle' AND Serial=1 AND State='Sent';") == 1
                        : ScalarInt(db, "SELECT COUNT(*) FROM sqlite_master WHERE name='BcwPrints';") == 0) &&
                    ScalarInt(db, "SELECT COUNT(*) FROM RuntimeMigrationState WHERE MigrationKey='BCW_SHARED_DATABASE_V1';") == 0 &&
                    ScalarText(db, "PRAGMA integrity_check;") == "ok",
                    "Conflicting/corrupt " + kind + " source rolls back the entire merge and preserves History");
                BcwDatabaseMigrationService.Ensure(db, () => kind == "existing" ? [] : [a]);
                Assert(ScalarInt(db, "SELECT COUNT(*) FROM BcwPrints;") == 1,
                    "Resolving source selection safely retries the rolled-back migration");
            }
            string missing = Path.Combine(root, "Missing", "JBZUniversalTester.db");
            var missingStore = new TestHistoryStore(missing);
            missingStore.Add(new TestHistoryRecord { CycleId = "bcw-existing-history", Started = DateTime.Now,
                Finished = DateTime.Now, PartNumber = "KEEP-BCW", LabelTemplateType = "BCW", Passed = true, Result = "PASS" });
            string missingBefore = HistoryDataSnapshot(missing);
            bool missingRejected = false;
            try { BcwDatabaseMigrationService.Ensure(missing, () => []); }
            catch (InvalidDataException) { missingRejected = true; }
            Assert(missingRejected && HistoryDataSnapshot(missing) == missingBefore &&
                ScalarInt(missing, "SELECT COUNT(*) FROM RuntimeMigrationState WHERE MigrationKey='BCW_SHARED_DATABASE_V1';") == 0,
                "Existing BCW History without a serial journal cannot silently restart with an empty counter");
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void TestBcwSharedDatabaseDefaultStore()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-BcwDefault-" + Guid.NewGuid().ToString("N"));
        object? previous = AppContext.GetData("JBZUniversalTester.SelfTest.DatabaseFile");
        try
        {
            string db = Path.Combine(root, "Shared", "Data", "JBZUniversalTester.db");
            AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile", db);
            Directory.CreateDirectory(Path.GetDirectoryName(db)!);
            bool blocked = false;
            try { new BcwSerialStore().Peek(new DateTime(2026, 10, 14), "09", "LH"); }
            catch (SqliteException) { blocked = true; }
            Assert(blocked && !File.Exists(db), "Label access cannot create a fresh DB before History relocation/bootstrap");
            _ = new TestHistoryStore(db);
            var journal = new BcwSerialStore();
            journal.Prepare(BcwRequest("shared-default"), new DateTime(2026, 10, 14), false);
            journal.Outcome("shared-default", "Sent", "saved");
            Assert(new BcwSerialStore().Find("shared-default")!.Data.BcwSerial == 1 &&
                ScalarInt(db, "SELECT COUNT(*) FROM BcwPrints;") == 1 &&
                Directory.GetFiles(root, "BCW-labels.db", SearchOption.AllDirectories).Length == 0,
                "Default BCW reserve/send/restart writes the single shared DB without creating a label sidecar");
            string old = Path.Combine(root, "Old"), deployment = Path.Combine(root, "New");
            CreateBcwMergeSource(Path.Combine(old, "Data", "BCW-labels.db"), "legacy", 9, "Sent");
            ProductionDataUpgradeService.MigrateMissingProductionData(deployment, [old]);
            Assert(!File.Exists(Path.Combine(deployment, "Data", "BCW-labels.db")),
                "Upgrade bootstrap never copies the old separate label DB into new deployment folders");
        }
        finally
        {
            AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile", previous);
            SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
