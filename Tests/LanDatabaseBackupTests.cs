using System.IO;
using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLanDatabaseBackup()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZLanBackupTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string db = Path.Combine(root, "source.db");
            string remote = Path.Combine(root, "server", "station");
            string machineCode = StationIdentityService.GetOrCreateMachineCode(Path.Combine(root, "identity.txt"));
            var settings = new ProductionSettings
            {
                LanBackupEnabled = true, LanBackupServerIp = "192.168.10.150",
                LanBackupShareName = "JBZBackup"
            };
            string cfg = Path.Combine(root, "settings.cfg");
            ProductionConfigService.SaveLegacyCfg(settings, cfg);
            var loaded = (ProductionSettings)typeof(ProductionConfigService)
                .GetMethod("LoadEnglishCfg", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [cfg])!;
            Assert(loaded.LanBackupEnabled && loaded.LanBackupServerIp == settings.LanBackupServerIp &&
                   loaded.LanBackupShareName == settings.LanBackupShareName,
                "LAN backup settings survive CFG round-trip");
            Assert(LanDatabaseBackupService.BuildDestination(settings.LanBackupServerIp, settings.LanBackupShareName, machineCode)
                    == $@"\\192.168.10.150\JBZBackup\{machineCode}",
                "LAN backup isolates each station below the configured share");

            bool online = false;
            DateTime now = new(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
            int destinationCalls = 0;
            using var backup = new LanDatabaseBackupService(db,
                (_, _) => Task.FromResult(online), (_, _) => { destinationCalls++; return remote; }, () => now, () => machineCode);
            backup.Configure(settings);
            Assert(!backup.BackupOnceAsync().GetAwaiter().GetResult() && destinationCalls == 0 &&
                   !Directory.Exists(remote) && !File.Exists(db),
                "Offline backup does not touch SMB or create/read a database");

            using var source = new SqliteConnection($"Data Source={db};Pooling=False");
            source.Open();
            using (SqliteCommand seed = source.CreateCommand())
            {
                seed.CommandText = """
                    PRAGMA journal_mode=WAL;
                    PRAGMA wal_autocheckpoint=0;
                    CREATE TABLE Tests(Id INTEGER PRIMARY KEY, CycleId TEXT UNIQUE, PartId INTEGER, ModelId INTEGER);
                    CREATE TABLE TestFaults(Id INTEGER PRIMARY KEY, TestId INTEGER REFERENCES Tests(Id));
                    CREATE TABLE ResistanceMeasurements(Id INTEGER PRIMARY KEY, TestId INTEGER REFERENCES Tests(Id));
                    CREATE TABLE WaterProofMeasurements(Id INTEGER PRIMARY KEY, TestId INTEGER REFERENCES Tests(Id));
                    INSERT INTO Tests VALUES(1,'cycle-original',2,3);
                    INSERT INTO TestFaults VALUES(1,1);
                    INSERT INTO ResistanceMeasurements VALUES(1,1);
                    INSERT INTO WaterProofMeasurements VALUES(1,1);
                    CREATE TABLE BackupPadding(Data BLOB);
                    INSERT INTO BackupPadding VALUES(zeroblob(2097152));
                    """;
                seed.ExecuteNonQuery();
            }
            Assert(File.Exists(db + "-wal"), "Fixture retains committed records in WAL");
            online = true;
            Assert(backup.BackupOnceAsync().GetAwaiter().GetResult(), "Reconnected backup uploads a snapshot");
            string latest = Path.Combine(remote, "JBZUniversalTester.db");
            VerifyBackup(latest, 1);
            VerifyBackup(db, 1);

            int callsAfterUpload = destinationCalls;
            now = now.AddDays(29);
            Assert(!backup.BackupOnceAsync().GetAwaiter().GetResult() && destinationCalls == callsAfterUpload,
                "No second backup is sent before the monthly due date");
            using (var restarted = new LanDatabaseBackupService(db,
                (_, _) => throw new InvalidOperationException("Not due: network must not be touched"),
                (_, _) => remote, () => now, () => machineCode))
            {
                restarted.Configure(settings);
                Assert(!restarted.BackupOnceAsync().GetAwaiter().GetResult(),
                    "Restart preserves the successful monthly backup date without network access");
            }
            now = now.AddDays(2);
            online = false;
            Assert(!backup.BackupOnceAsync().GetAwaiter().GetResult() && destinationCalls == callsAfterUpload,
                "A monthly backup that is due remains pending while Wi-Fi is disconnected");
            online = true;

            using (SqliteTransaction pending = source.BeginTransaction())
            {
                using SqliteCommand write = source.CreateCommand();
                write.Transaction = pending;
                write.CommandText = "INSERT INTO Tests VALUES(2,'uncommitted',2,3);";
                write.ExecuteNonQuery();
                Assert(backup.BackupOnceAsync().GetAwaiter().GetResult(),
                    "WAL snapshot can run while a production write transaction is active");
                VerifyBackup(latest, 1);
                pending.Rollback();
            }

            now = now.AddMonths(1);
            Task<bool> concurrentBackup = backup.BackupOnceAsync();
            Assert(!concurrentBackup.IsCompleted, "Large snapshot yields between bounded page batches");
            using (SqliteCommand write = source.CreateCommand())
            {
                write.CommandText = "INSERT INTO Tests VALUES(2,'committed-during-backup',2,3);";
                write.ExecuteNonQuery();
            }
            Assert(concurrentBackup.GetAwaiter().GetResult(),
                "Production can commit while the incremental snapshot is in progress");
            VerifyBackup(latest, 2);

            // A failed snapshot/upload must leave the previous good backup untouched.
            byte[] previous = File.ReadAllBytes(latest);
            using var missing = new LanDatabaseBackupService(Path.Combine(root, "missing.db"),
                (_, _) => Task.FromResult(true), (_, _) => remote, getMachineCode: () => machineCode);
            missing.Configure(settings);
            Assert(!missing.BackupOnceAsync().GetAwaiter().GetResult() &&
                   previous.SequenceEqual(File.ReadAllBytes(latest)) &&
                   !Directory.EnumerateFiles(remote, "*.upload").Any(),
                "An unavailable source preserves the last backup and cleans its temporary upload");

            using var invalid = new LanDatabaseBackupService(Path.Combine(root, "corrupt.db"),
                (_, _) => Task.FromResult(true), (_, _) => remote, getMachineCode: () => machineCode);
            File.WriteAllText(Path.Combine(root, "corrupt.db"), "invalid SQLite");
            invalid.Configure(settings);
            bool failed = false;
            try { invalid.BackupOnceAsync().GetAwaiter().GetResult(); }
            catch (SqliteException) { failed = true; }
            Assert(failed && previous.SequenceEqual(File.ReadAllBytes(latest)),
                "A failed SQLite snapshot never replaces the server's valid backup");

            var pendingProbe = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            now = now.AddMonths(1);
            using var serialized = new LanDatabaseBackupService(db, (_, _) => pendingProbe.Task, (_, _) => remote, () => now, () => machineCode);
            serialized.Configure(settings);
            Task<bool> first = serialized.BackupOnceAsync();
            Assert(!serialized.BackupOnceAsync().GetAwaiter().GetResult(), "Only one backup attempt can own the worker");
            settings.LanBackupEnabled = false;
            serialized.Configure(settings);
            pendingProbe.SetResult(true);
            Assert(!first.GetAwaiter().GetResult() && previous.SequenceEqual(File.ReadAllBytes(latest)),
                "Disabling/changing settings prevents a stale backup from publishing");
            VerifyBackup(db, 2);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }

        static void VerifyBackup(string path, int tests)
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using SqliteCommand check = connection.CreateCommand();
            check.CommandText = "PRAGMA integrity_check;";
            Assert(check.ExecuteScalar()?.ToString() == "ok", "SQLite integrity is preserved in source and backup");
            check.CommandText = "SELECT COUNT(*) FROM Tests;";
            Assert(Convert.ToInt32(check.ExecuteScalar()) == tests, "Snapshot contains committed tests only");
            foreach (string table in new[] { "TestFaults", "ResistanceMeasurements", "WaterProofMeasurements" })
            {
                check.CommandText = $"SELECT COUNT(*) FROM {table} c JOIN Tests t ON t.Id=c.TestId WHERE t.CycleId='cycle-original' AND t.PartId=2 AND t.ModelId=3;";
                Assert(Convert.ToInt32(check.ExecuteScalar()) == 1, "Child records and original identities remain attached");
            }
        }
    }
}
