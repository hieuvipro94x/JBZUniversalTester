using System.IO;
using System.Security.Cryptography;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestStationConfigurationRelocation()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-CfgRelocation-" + Guid.NewGuid().ToString("N"));
        string app = Path.Combine(root, "App"), oldA = Path.Combine(root, "OldA"), oldB = Path.Combine(root, "OldB");
        string db = Path.Combine(root, "Shared", "Data", "JBZUniversalTester.db");
        object? previousDatabase = AppContext.GetData("JBZUniversalTester.SelfTest.DatabaseFile");
        foreach (string folder in new[] { app, oldA, oldB }) Directory.CreateDirectory(folder);
        try
        {
            AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile", db);
            var store = new TestHistoryStore(db);
            store.Add(new TestHistoryRecord { CycleId = "cfg-history-keep", Started = DateTime.Now,
                Finished = DateTime.Now, PartNumber = "KEEP", Passed = true, Result = "PASS" });
            SqliteConnection.ClearAllPools();
            byte[] originalDatabase = SHA256.HashData(File.ReadAllBytes(db));
            string cfg = RuntimePaths.ConfigFile;
            string adjacent = Path.Combine(app, "JBZUniversalTester.cfg");
            string a = Path.Combine(oldA, "JBZUniversalTester.cfg"), b = Path.Combine(oldB, "JBZUniversalTester.cfg");
            File.WriteAllText(a, "[LotNo]1234\r\n");
            File.WriteAllText(b, "[LotNo]5678\r\n");
            bool rejected = false;
            try { ProductionDataUpgradeService.MigrateStationConfiguration(cfg, app, () => [oldA, oldB]); }
            catch (IOException) { rejected = true; }
            Assert(rejected && !File.Exists(cfg), "Conflicting old CFG files cannot create a default or pick a station silently");
            File.WriteAllText(adjacent, "[LotNo]9000\r\n[DeviceName]KEEP-STATION\r\n");
            Assert(ProductionDataUpgradeService.MigrateStationConfiguration(cfg, app,
                () => throw new Exception("Adjacent CFG must bypass sibling probing")).Count == 1 &&
                File.ReadAllBytes(cfg).SequenceEqual(File.ReadAllBytes(adjacent)),
                "Adjacent station CFG is copied intact beside the database");
            var settings = ProductionConfigService.Load();
            Assert(settings.DeviceName == "KEEP-STATION" && settings.LotNo == 9000,
                "Configuration reads station identity and LOT from the shared CFG");
            settings.DeviceName = "SAVED-SHARED";
            ProductionConfigService.Save(settings);
            Assert(ProductionConfigService.LoadSavedSettings().DeviceName == "SAVED-SHARED" &&
                ProductionConfigService.ConfigDirectory == Path.GetDirectoryName(db),
                "Configuration save and reload use the database directory");
            byte[] saved = File.ReadAllBytes(cfg);
            Assert(ProductionDataUpgradeService.MigrateStationConfiguration(cfg, app,
                () => throw new Exception("Warm startup must bypass probing")).Count == 0 &&
                File.ReadAllBytes(cfg).SequenceEqual(saved) && File.ReadAllText(adjacent).Contains("KEEP-STATION") &&
                File.ReadAllText(a).Contains("1234") && File.ReadAllText(b).Contains("5678"),
                "Subsequent upgrades preserve shared CFG and all legacy sources");
            string identicalTarget = Path.Combine(root, "Identical", "JBZUniversalTester.cfg");
            File.Copy(a, b, true);
            Assert(ProductionDataUpgradeService.MigrateStationConfiguration(identicalTarget,
                Path.Combine(root, "Absent"), () => [oldA, oldB]).Count == 1 &&
                File.ReadAllBytes(identicalTarget).SequenceEqual(File.ReadAllBytes(a)), "Identical old CFG copies are safe to inherit");
            string fresh = Path.Combine(root, "Fresh", "JBZUniversalTester.cfg");
            Assert(ProductionDataUpgradeService.MigrateStationConfiguration(fresh, app + "-new", () => []).Count == 0 &&
                !File.Exists(fresh), "Fresh installation leaves default creation to configuration bootstrap");
            Assert(SHA256.HashData(File.ReadAllBytes(db)).SequenceEqual(originalDatabase),
                "CFG relocation and saving never modify the existing history database");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            Assert((string?)command.ExecuteScalar() == "ok", "CFG relocation preserves SQLite integrity");
        }
        finally
        {
            AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile", previousDatabase);
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
