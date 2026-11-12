using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestFirstProductPrintSetup()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-NoInitialSetup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string cfg = Path.Combine(root, "settings.cfg"), db = Path.Combine(root, "history.db");
            var settings = new ProductionSettings { LotNo = 123456, MasterFaultRequiredCount = 0 };
            var store = new TestHistoryStore(db);
            await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            await persistence.Initialization;
            var vm = CreateTestViewModel(settings, out FakeBoard board);
            var lots = new LotSequenceService(settings, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg));
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            Set("_historyStore", store); Set("_productionPersistence", persistence); Set("_lotSequence", lots);
            Set("_partCounterStore", new Lazy<PartCounterStore>(() => new PartCounterStore(Path.Combine(root, "PartCnt.txt"))));
            async Task Loaded() => await ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!).WaitAsync(TimeSpan.FromSeconds(5));
            var model = Model(("PAIR", new[] { 1, 2 })); model.PartNumber = "NEW-NO-SETUP";
            model.ModelName = model.PartNumber; model.SourcePath = Path.Combine(root, "NEW-NO-SETUP.tht");
            try
            {
                int windows = Application.Current?.Windows.Count ?? 0;
                Assert(await vm.LoadPreparedModelAsync(model) == model, "A fresh product loads without mandatory printer/Leak/LOT setup");
                await Loaded();
                Assert(vm.Lot == "0" && lots.StartLot == 0 &&
                       !settings.LabelTemplateTypesByProduct.ContainsKey(model.PartNumber) &&
                       !settings.LotSettingsByProduct[model.PartNumber].UseConfiguredStartLot &&
                       (Application.Current?.Windows.Count ?? 0) == windows,
                    "Selection opens no setup dialog, defaults to LOT0 and invents no printer choice or explicit base");
                var editor = new ProductionSettingsViewModel(vm, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg),
                    JsonSerializer.Deserialize<ProductionSettings>(JsonSerializer.Serialize(settings))!);
                editor.Settings.LotNo = 2000;
                await editor.SaveAsync();
                ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                vm.RefreshProductionSettingsOnly(); await Loaded();
                Assert(vm.Lot == "2000" && lots.StartLot == 2000 &&
                       settings.LotSettingsByProduct[model.PartNumber].UseConfiguredStartLot,
                    "Explicit settings save and CFG reload retain LOT2000 even with no PASS today");
                settings.LabelTemplateTypesByProduct[model.PartNumber] = ProductionConfigService.NoProductLabel;
                var now = DateTime.Now;
                for (int i = 1; i <= 20; i++)
                {
                    Set("_runtimeMode", 1); Set("_masterApproved", true); Set("_activeCycleId", "base-pass-" + i);
                    Set("_cycleStartedAt", now); Set("_resultRecordedThisCycle", 0);
                    long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
                    Assert(await ((Task<bool>)typeof(TestViewModel).GetMethod("RecordCompletedProductAsync", flags)!
                        .Invoke(vm, [true, "PASS", model, generation, CancellationToken.None, null, now])!).WaitAsync(TimeSpan.FromSeconds(5)),
                        "PASS commits exactly once with the configured base");
                }
                var criteria = new HistorySearchCriteria(null, null, null, model.PartNumber, "ALL");
                var rows = store.Search(criteria);
                Assert(vm.Lot == "2020" && vm.Pass == 20 && rows.Count == 20 &&
                       rows.Single(row => row.CycleId == "base-pass-20").ExportPassCountText == "2020" &&
                       rows.Select(row => row.LotNo).Order().SequenceEqual(Enumerable.Range(2001, 20).Select(i => (long)i)),
                    "Base2000 plus PASS20 displays and persists History PASS count2020; each previous PASS retains its own LOT");
                string csv = Path.Combine(root, "history.csv");
                Assert(HistoryExportService.ExportCsv(csv, store.EnumerateForExport(criteria)) == 20 && File.ReadAllText(csv).Contains("2020"),
                    "History export uses the same saved PASS count2020");
                vm.SetModel(model); await Loaded();
                Assert(vm.Lot == "2020" && vm.Pass == 20, "Reselect/restart never adds the base twice");
                var tomorrow = new LotSequenceService(ProductionConfigService.LoadSavedSettings(cfg), _ => { }, () => now.AddDays(1));
                tomorrow.SelectProduct(model.PartNumber, false);
                Assert(tomorrow.StartLot == 2000 && tomorrow.NextLot == 2000, "Explicit product base survives a new day");
                Assert(ScalarInt(db, "SELECT COUNT(*) FROM Tests;") == 20, "No setup operation creates or deletes history");
                using var connection = new SqliteConnection($"Data Source={db};Mode=ReadOnly"); connection.Open();
                using var integrity = connection.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
                Assert((string?)integrity.ExecuteScalar() == "ok", "Configured base preserves SQLite integrity");
            }
            finally { await persistence.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
        });
    }
}
