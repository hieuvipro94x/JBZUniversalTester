using System.Reflection;
using System.Text.Json;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLateLotBaseActivation()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-LateLotBase-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string cfg = Path.Combine(root, "settings.cfg"), db = Path.Combine(root, "history.db");
            var now = DateTime.Now;
            var model = Model(("PAIR", new[] { 1, 2 }));
            model.PartNumber = "LATE-BASE"; model.SourcePath = Path.Combine(root, "LATE-BASE.tht");
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
            // A legacy/default start is still stored, but was never activated
            // for this product's daily zero-start production.
            settings.LotSettingsByProduct[model.PartNumber] = new ProductLotSettings
            { StartLotNo = 7000, LotNo = 0, LotNoDate = now.ToString("yyyy-MM-dd"),
                DailyLotStartsAtZero = true, UseConfiguredStartLot = false, HistoryBatchId = "legacy-base" };
            var store = new TestHistoryStore(db);
            for (int i = 1; i <= 20; i++)
                store.CommitResult(ProductionResultCommitRequest.Capture(new TestHistoryRecord
                { CycleId = "before-base-" + i, Started = now, TestStartedAt = now, ResultAt = now, Finished = now,
                    Passed = true, Result = "PASS", InspectionType = HistoryInspectionType.Product,
                    LotNo = i, ProductionBatchKey = "unconfigured" }, model, settings, [], [], null,
                    ProgramIdentityService.VersionText), null);
            await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            await persistence.Initialization;
            try
            {
                var runtime = CreateTestViewModel(settings, out FakeBoard board);
                void Set(string field, object value) => typeof(TestViewModel).GetField(field, flags)!.SetValue(runtime, value);
                var lots = new LotSequenceService(settings, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg));
                Set("_lotSequence", lots); Set("_historyStore", store); Set("_productionPersistence", persistence);
                Set("_partCounterStore", new Lazy<PartCounterStore>(() => new PartCounterStore(Path.Combine(root, "PartCnt.txt"))));
                async Task Loaded() => await ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(runtime)!)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                runtime.SetModel(model); await Loaded();
                Assert(runtime.Pass == 20 && runtime.Lot == "20", "Twenty zero-base PASS products recover before the operator sets a start LOT");
                var unchanged = new ProductionSettingsViewModel(runtime, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg),
                    JsonSerializer.Deserialize<ProductionSettings>(JsonSerializer.Serialize(settings))!);
                Assert(unchanged.Settings.LotNo == 0, "Settings displays the active zero base rather than a dormant legacy 7000");
                unchanged.Settings.DeviceName = "UNRELATED-EDIT";
                await unchanged.SaveAsync();
                Assert(unchanged.Settings.LotNo == 0, "The same open settings page continues showing the base rather than the current LOT 20");
                unchanged.Settings.DeviceName = "SECOND-UNRELATED-EDIT";
                await unchanged.SaveAsync();
                ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                runtime.RefreshProductionSettingsOnly(); await Loaded();
                Assert(runtime.Lot == "20" && settings.LotSettingsByProduct[model.PartNumber].StartLotNo == 7000 &&
                       !settings.LotSettingsByProduct[model.PartNumber].UseConfiguredStartLot,
                    "Saving unrelated settings preserves both zero-base progress and the dormant saved number");
                var editor = new ProductionSettingsViewModel(runtime, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg),
                    JsonSerializer.Deserialize<ProductionSettings>(JsonSerializer.Serialize(settings))!);
                editor.Settings.LotNo = 7000;
                editor.Settings.Label.TemplateType = LabelSettings.LargeSqdzTemplate;
                await editor.SaveAsync();
                Assert(editor.Settings.LotNo == 7000, "The saved start stays 7000 in the editor");
                string activeBatch = editor.Settings.LotSettingsByProduct[model.PartNumber].HistoryBatchId;
                editor.Settings.DeviceName = "SAVE-WITHOUT-LOT-CHANGE";
                await editor.SaveAsync();
                Assert(editor.Settings.LotNo == 7000 && editor.Settings.LotSettingsByProduct[model.PartNumber].HistoryBatchId == activeBatch,
                    "Repeated saves in one settings page keep the base and batch stable");
                ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                runtime.RefreshProductionSettingsOnly(); await Loaded();
                Assert(runtime.Lot == "7020" && runtime.Pass == 20 && lots.StartLot == 7000,
                    $"Explicitly setting the dormant 7000 base retains twenty PASS: expected 7020, actual {runtime.Lot}");
                editor.Settings.DeviceName = "SAME-PAGE-AFTER-RECONCILIATION";
                await editor.SaveAsync();
                ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                runtime.RefreshProductionSettingsOnly(); await Loaded();
                Assert(runtime.Lot == "7020" && runtime.Pass == 20 && editor.Settings.LotNo == 7000,
                    "A later save from the same page cannot restore its older counter snapshot or turn progress into a base");
                Assert(settings.LotSettingsByProduct[model.PartNumber].UseConfiguredStartLot &&
                       !settings.LotSettingsByProduct[model.PartNumber].DailyLotStartsAtZero,
                    "The explicit operator edit activates the base even when it equals a dormant saved start");
                settings.Label.PrinterCom = string.Empty;
                Set("_runtimeMode", 1); Set("_masterApproved", true); Set("_activeCycleId", "after-base-21");
                Set("_cycleStartedAt", now); Set("_resultRecordedThisCycle", 0);
                long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(runtime)!;
                Assert(await ((Task<bool>)typeof(TestViewModel).GetMethod("RecordCompletedProductAsync", flags)!
                    .Invoke(runtime, [true, "PASS", model, generation, CancellationToken.None, null, now])!).WaitAsync(TimeSpan.FromSeconds(5)),
                    "The next PASS commits normally after the base edit");
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                object? context;
                while ((context = typeof(TestViewModel).GetField("_failedLabelPrint", flags)!.GetValue(runtime)) is null)
                    await Task.Delay(10, deadline.Token);
                var request = (LabelPrintRequest)context.GetType().GetProperty("Request")!.GetValue(context)!;
                Assert(request.Data.LotNo == 7021 && request.Payload.Contains("7021", StringComparison.Ordinal) &&
                       runtime.Lot == "7021" && runtime.Pass == 21 &&
                       ScalarInt(db, "SELECT COUNT(*) FROM Tests WHERE CycleId='after-base-21' AND Lot=7021 AND LabelPayload LIKE '%7021%';") == 1,
                    "The twenty-first PASS stores and prepares the exact printer LOT 7021");
                Assert(ScalarInt(db, "SELECT COUNT(*) FROM Tests WHERE CycleId LIKE 'before-base-%' AND Lot BETWEEN 1 AND 20;") == 20 &&
                       ScalarInt(db, "SELECT COUNT(*) FROM Tests;") == 21,
                    "Activating a late base never rewrites or duplicates the original twenty history rows");
                var other = Model(("PAIR", new[] { 1, 2 }));
                other.PartNumber = "OTHER-LOT"; other.SourcePath = Path.Combine(root, "OTHER-LOT.tht");
                runtime.SetModel(other); await Loaded();
                var otherEditor = new ProductionSettingsViewModel(runtime, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg),
                    JsonSerializer.Deserialize<ProductionSettings>(JsonSerializer.Serialize(settings))!);
                otherEditor.Settings.LotNo = 3000;
                await otherEditor.SaveAsync();
                var reloaded = ProductionConfigService.LoadSavedSettings(cfg);
                Assert(reloaded.LotSettingsByProduct[model.PartNumber].StartLotNo == 7000 &&
                       reloaded.LotSettingsByProduct[other.PartNumber].StartLotNo == 3000 &&
                       reloaded.LotSettingsByProduct[model.PartNumber].UseConfiguredStartLot &&
                       reloaded.LotSettingsByProduct[other.PartNumber].UseConfiguredStartLot,
                    "The CFG durably stores independent explicit start LOTs for both products");
                ProductionConfigService.CopyInto(settings, reloaded);
                runtime.SetModel(model); await Loaded();
                Assert(runtime.Lot == "7021" && runtime.Pass == 21 && lots.ReserveForCycle("next-after-reload") == 7022,
                    "A -> B -> A and CFG reload preserve the 7000 base plus 21 PASS without applying the base twice");
                using var check = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Pooling=False"); check.Open();
                using var integrity = check.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
                Assert((string?)integrity.ExecuteScalar() == "ok", "Late LOT activation retains SQLite integrity");
            }
            finally { await persistence.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
        });
    }
}
