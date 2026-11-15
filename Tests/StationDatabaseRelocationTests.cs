using System.Security.Cryptography;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestStationDatabaseRelocation()
    {
        static byte[] HashLiveFile(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return SHA256.HashData(stream);
        }
        string root = Path.Combine(Path.GetTempPath(), "JBZ-Relocation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string app = Path.Combine(root, "Operating"), source = Path.Combine(app, "Data", "JBZUniversalTester.db");
            var store = new TestHistoryStore(source);
            var model = Model(("PAIR", new[] { 1, 2 })); model.PartNumber = "RELOCATE";
            var now = DateTime.Now;
            store.CommitResult(ProductionResultCommitRequest.Capture(new TestHistoryRecord
            { CycleId = "preserved", PartNumber = model.PartNumber, ModelName = model.ModelName,
                Started = now, TestStartedAt = now, Finished = now, ResultAt = now, Passed = true,
                Result = "PASS", LotNo = 2020, InspectionType = HistoryInspectionType.Product },
                model, new ProductionSettings(), [], [], null, ProgramIdentityService.VersionText) with
                { Resistance = [new ResistancePersistenceSnapshot(1, "R1", 100, 90, 110, true, 1, 0)],
                  WaterProof = [new WaterProofPersistenceSnapshot(1, true, 80, 79, 1, true)] }, null);
            using var writer = new SqliteConnection($"Data Source={source};Pooling=False"); writer.Open();
            using var sql = writer.CreateCommand();
            sql.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE ExtraData(Id INTEGER PRIMARY KEY, Value BLOB); INSERT INTO ExtraData VALUES(1,X'000102FF');";
            sql.ExecuteNonQuery();
            Assert(File.Exists(source + "-wal"), "Relocation fixture retains uncheckpointed WAL content");
            string before = HistoryDataSnapshot(source);
            string destination = Path.Combine(root, "ProgramData", "Data", "JBZUniversalTester.db");
            IEnumerable<string> NoProbe() => throw new InvalidOperationException("Must not probe previous versions");
            StationDatabaseMigrationService.Ensure(destination, app, NoProbe);
            Assert(File.Exists(source) && File.Exists(destination) && HistoryDataSnapshot(destination) == before &&
                   HistoryDataSnapshot(source) == before && ScalarInt(destination, "SELECT COUNT(*) FROM ExtraData WHERE Value=X'000102FF';") == 1 &&
                   ScalarInt(destination, "SELECT COUNT(*) FROM ResistanceMeasurements;") == 1 &&
                   ScalarInt(destination, "SELECT COUNT(*) FROM WaterProofMeasurements;") == 1,
                "SQLite backup copies WAL, unknown tables, History and child measurements without mutating the source");
            byte[] targetHash = SHA256.HashData(File.ReadAllBytes(destination));
            string portableApp = Path.Combine(root, "Portable"); Directory.CreateDirectory(portableApp);
            string portableSource = Path.Combine(portableApp, "JBZUniversalTester.db");
            using (var portableWriter = new SqliteConnection($"Data Source={portableSource};Pooling=False"))
            {
                portableWriter.Open(); writer.BackupDatabase(portableWriter);
                using var portableSql = portableWriter.CreateCommand();
                portableSql.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO ExtraData VALUES(2,X'ABCD');";
                portableSql.ExecuteNonQuery();
                byte[] portableHash = HashLiveFile(portableSource);
                byte[] walHash = HashLiveFile(portableSource + "-wal");
                string portableTarget = Path.Combine(root, "PortableTarget", "JBZUniversalTester.db");
                StationDatabaseMigrationService.Ensure(portableTarget, portableApp, NoProbe);
                Assert(HistoryDataSnapshot(portableTarget) == before &&
                       ScalarInt(portableTarget, "SELECT COUNT(*) FROM ExtraData;") == 2 &&
                       HashLiveFile(portableSource).SequenceEqual(portableHash) &&
                       HashLiveFile(portableSource + "-wal").SequenceEqual(walHash),
                    "A DB beside the EXE takes priority over sibling discovery, includes WAL and preserves its source bytes");
                string previousPortableTarget = Path.Combine(root, "PreviousPortable", "JBZUniversalTester.db");
                StationDatabaseMigrationService.Ensure(previousPortableTarget, Path.Combine(root, "NewPortable"), () => [portableApp]);
                Assert(ScalarInt(previousPortableTarget, "SELECT COUNT(*) FROM ExtraData;") == 2,
                    "A previous-version DB beside its EXE is discovered without losing its WAL data");
                Directory.CreateDirectory(Path.Combine(portableApp, "Data"));
                File.Copy(destination, Path.Combine(portableApp, "Data", "JBZUniversalTester.db"));
                string canonicalTarget = Path.Combine(root, "CanonicalPriority", "JBZUniversalTester.db");
                StationDatabaseMigrationService.Ensure(canonicalTarget, portableApp, NoProbe);
                Assert(ScalarInt(canonicalTarget, "SELECT COUNT(*) FROM ExtraData;") == 1,
                    "The canonical adjacent Data database keeps its existing priority over a portable root database");
                bool portableAmbiguous = false;
                string mixedTarget = Path.Combine(root, "MixedPortable", "JBZUniversalTester.db");
                try { StationDatabaseMigrationService.Ensure(mixedTarget, Path.Combine(root, "NewPortable"), () => [portableApp]); }
                catch (IOException) { portableAmbiguous = true; }
                Assert(portableAmbiguous && !File.Exists(mixedTarget),
                    "Conflicting previous portable/Data databases block startup rather than dropping portable history");
            }
            StationDatabaseMigrationService.Ensure(destination, Path.Combine(root, "new-version"), NoProbe);
            Assert(SHA256.HashData(File.ReadAllBytes(destination)).SequenceEqual(targetHash), "Existing shared DB is never overwritten or probed against old versions");
            string sibling = Path.Combine(root, "V2026.09.427"); Directory.CreateDirectory(Path.Combine(sibling, "Data"));
            string siblingDb = Path.Combine(sibling, "Data", "JBZUniversalTester.db"); File.Copy(destination, siblingDb);
            string fallback = Path.Combine(root, "Fallback", "JBZUniversalTester.db");
            StationDatabaseMigrationService.Ensure(fallback, Path.Combine(root, "New"), () => [sibling]);
            Assert(HistoryDataSnapshot(fallback) == before, "A single previous-version DB is inherited without using file size/date");
            string other = Path.Combine(root, "V2026.09.426"); string otherDb = Path.Combine(other, "Data", "JBZUniversalTester.db");
            _ = new TestHistoryStore(otherDb);
            string ambiguous = Path.Combine(root, "Ambiguous", "JBZUniversalTester.db"); bool refused = false;
            try { StationDatabaseMigrationService.Ensure(ambiguous, Path.Combine(root, "New"), () => [sibling, other]); }
            catch (IOException) { refused = true; }
            Assert(refused && !File.Exists(ambiguous) && File.Exists(siblingDb) && File.Exists(otherDb),
                "Divergent previous histories block migration instead of silently discarding one");
            string brokenApp = Path.Combine(root, "Broken"); Directory.CreateDirectory(Path.Combine(brokenApp, "Data"));
            File.WriteAllText(Path.Combine(brokenApp, "Data", "JBZUniversalTester.db"), "damaged");
            string brokenTarget = Path.Combine(root, "BrokenTarget", "JBZUniversalTester.db"); refused = false;
            try { StationDatabaseMigrationService.Ensure(brokenTarget, brokenApp, NoProbe); }
            catch (Exception ex) when (ex is SqliteException or InvalidDataException) { refused = true; }
            Assert(refused && !File.Exists(brokenTarget), "Damaged source cannot create or replace a shared database");
            string absent = Path.Combine(root, "Absent", "JBZUniversalTester.db"); refused = false;
            try { StationDatabaseMigrationService.Ensure(absent, Path.Combine(root, "NoSource"), () => []); }
            catch (IOException) { refused = true; }
            Assert(refused && !File.Exists(absent), "An existing station with no reachable source never silently starts empty");
            string duplicate = Path.Combine(root, "Duplicate", "Data"); Directory.CreateDirectory(duplicate);
            File.Copy(siblingDb, Path.Combine(duplicate, "JBZUniversalTester.db"));
            string duplicateTarget = Path.Combine(root, "DuplicateTarget", "JBZUniversalTester.db");
            StationDatabaseMigrationService.Ensure(duplicateTarget, Path.Combine(root, "New"), () => [sibling, Path.GetDirectoryName(duplicate)!]);
            Assert(HistoryDataSnapshot(duplicateTarget) == before, "Identical previous databases are accepted after logical content comparison");
            string emptyTarget = Path.Combine(root, "empty.db"); File.WriteAllBytes(emptyTarget, []); refused = false;
            try { StationDatabaseMigrationService.Ensure(emptyTarget, app, NoProbe); }
            catch (InvalidDataException) { refused = true; }
            Assert(refused && new FileInfo(emptyTarget).Length == 0 && HistoryDataSnapshot(source) == before,
                "An abnormal empty shared DB stops startup instead of silently hiding production history");
            string fresh = Path.Combine(root, "Fresh", "JBZUniversalTester.db");
            StationDatabaseMigrationService.Ensure(fresh, Path.Combine(root, "FreshApp"), () => [], allowNewDatabase: true);
            Assert(!File.Exists(fresh), "Fresh installations defer empty schema creation to the repository");
            using var check = new SqliteConnection($"Data Source={destination};Mode=ReadOnly"); check.Open();
            using var integrity = check.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
            Assert((string?)integrity.ExecuteScalar() == "ok", "Relocated database integrity is valid");
            Assert(RuntimePaths.SharedDatabaseFile == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "JBZUniversalTester", "Data", "JBZUniversalTester.db"), "Production path is version-independent machine-wide ProgramData");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
