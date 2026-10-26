using System.Globalization;
using System.Reflection;
using System.Text.Json;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestProductLabelAndImmediateLotSettings()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-ProductLabelLot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string cfg = Path.Combine(root, "settings.cfg");
            string db = Path.Combine(root, "history.db");
            DateTime now = DateTime.Now;
            var settings = new ProductionSettings { LotNo = 2000, LotNoDate = now.ToString("yyyy-MM-dd"), MasterFaultRequiredCount = 0 };
            var store = new TestHistoryStore(db);
            await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            await persistence.Initialization;
            try
            {
                var vm = CreateTestViewModel(settings, out FakeBoard board);
                void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                var lots = new LotSequenceService(settings, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg));
                Set("_lotSequence", lots);
                Set("_historyStore", store);
                Set("_productionPersistence", persistence);
                Set("_partCounterStore", new Lazy<PartCounterStore>(() => new PartCounterStore(Path.Combine(root, "PartCnt.txt"))));
                var a = Model(("PAIR", new[] { 1, 2 }));
                a.PartNumber = "LABEL-A"; a.ModelName = "A"; a.SourcePath = Path.Combine(root, "A.tht");
                a.LabelTemplate = new LabelTemplateDefinition(RawTemplate: "N\nOLD_THT_FORMAT\nP1\n");
                var b = Model(("PAIR", new[] { 1, 2 }));
                b.PartNumber = "LABEL-B"; b.ModelName = "B"; b.SourcePath = Path.Combine(root, "B.tht");
                b.LabelTemplate = a.LabelTemplate;
                async Task Loaded() => await ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                async Task Save(string template, long start)
                {
                    var editor = new ProductionSettingsViewModel(vm,
                        snapshot => ProductionConfigService.SaveLegacyCfg(snapshot, cfg),
                        JsonSerializer.Deserialize<ProductionSettings>(JsonSerializer.Serialize(settings))!);
                    editor.Settings.Label.TemplateType = template;
                    editor.Settings.LotNo = start;
                    await editor.SaveAsync();
                    ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                    vm.RefreshProductionSettingsOnly();
                    await Loaded();
                }
                void Commit(string cycle, long lot, string batch)
                {
                    var row = new TestHistoryRecord
                    {
                        CycleId = cycle, PartNumber = a.PartNumber, ModelName = a.ModelName,
                        ModelFile = a.SourcePath, Started = now, TestStartedAt = now, Finished = now, ResultAt = now,
                        Passed = true, Result = "PASS", InspectionType = HistoryInspectionType.Product,
                        LotNo = lot, ProductionBatchKey = batch
                    };
                    store.CommitResult(ProductionResultCommitRequest.Capture(row, a, settings, [], [], null,
                        ProgramIdentityService.VersionText), null);
                }
                vm.SetModel(a); await Loaded();
                await Save(LabelSettings.LargeSqdzTemplate, 2000);
                Commit("old-a", 2003, lots.HistoryBatchKey);
                vm.SetModel(b); await Loaded();
                await Save(LabelSettings.SmallTemplate, 5000);
                vm.SetModel(a); await Loaded();
                Assert(settings.Label.TemplateType == LabelSettings.LargeSqdzTemplate && vm.Lot == "2003",
                    "A -> B -> A restores A's label and committed LOT independently");
                await Save(LabelSettings.LargeSqdzTemplate, 7000);
                Assert(vm.Lot == "7000" && settings.LotSettingsByProduct[a.PartNumber].StartLotNo == 7000 && vm.Pass == 1,
                    "Saving 2000 -> 7000 applies immediately; old daily PASS cannot overwrite the new base or disappear");
                long first = lots.ReserveForCycle("new-a");
                var print = LabelPrintRequest.Capture(new TestHistoryRecord { CycleId = "new-a", LotNo = first, Finished = now }, a, settings.Label);
                Assert(first == 7001 && print.Data.LotNo == 7001 && print.Profile.Id == LabelSettings.LargeSqdzTemplate &&
                       !print.Payload.Contains("OLD_THT_FORMAT", StringComparison.Ordinal),
                    "Next PASS uses the new base plus one and A's explicitly selected format rather than embedded old THT commands");
                Commit("new-a", first, lots.HistoryBatchKey);
                Assert(lots.TryCommitSuccessfulPass("new-a", first, out _), "New base commits normally");
                vm.SetModel(b); await Loaded();
                var small = LabelPrintRequest.Capture(new TestHistoryRecord { CycleId = "b-preview", LotNo = 5001, Finished = now }, b, settings.Label);
                Assert(settings.Label.TemplateType == LabelSettings.SmallTemplate && small.Profile.Id == LabelSettings.SmallTemplate &&
                       !small.Payload.Contains("OLD_THT_FORMAT", StringComparison.Ordinal), "B retains its own small SQDZ format");
                vm.SetModel(a); await Loaded();
                Assert(vm.Lot == "7001" && vm.Pass == 2, "Reload recovers the latest committed LOT from the new batch and retains all daily history");
                await Save(LabelSettings.LargeSqdzTemplate, 1000);
                Assert(vm.Lot == "1000" && vm.Pass == 2, "Lowering the base is also immediate and cannot restore an older larger LOT");
                await Save(LabelSettings.LargeSqdzTemplate, 1000);
                Assert(vm.Lot == "1000", "Unrelated/repeated saves do not advance or reset the base again");
                var reloaded = ProductionConfigService.LoadSavedSettings(cfg);
                Assert(reloaded.LabelTemplateTypesByProduct[a.PartNumber] == LabelSettings.LargeSqdzTemplate &&
                       reloaded.LabelTemplateTypesByProduct[b.PartNumber] == LabelSettings.SmallTemplate &&
                       reloaded.LotSettingsByProduct[a.PartNumber].LastStartChangedDate == now.ToString("yyyy-MM-dd"),
                    "Both product choices and reset revision survive application restart via CFG");
                Assert(store.GetLatestCommittedLotForBatch(PartIdentitySnapshot.Capture(a), now, lots.HistoryBatchKey) is null &&
                       store.GetStatistics(PartIdentitySnapshot.Capture(a), now).DailyPass == 2 &&
                       ScalarInt(db, "SELECT COUNT(*) FROM Tests;") == 2,
                    "The new batch has no PASS yet; all old tests remain queryable and unchanged");
                using var connection = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
                connection.Open();
                using var integrity = connection.CreateCommand();
                integrity.CommandText = "PRAGMA integrity_check;";
                Assert(Convert.ToString(integrity.ExecuteScalar(), CultureInfo.InvariantCulture) == "ok", "Existing history remains intact");
                Assert(!board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)), "Settings changes do not fire production relays");
                var noPrintEditor = new ProductionSettingsViewModel(vm, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg),
                    JsonSerializer.Deserialize<ProductionSettings>(JsonSerializer.Serialize(settings))!);
                noPrintEditor.PrintLabelForCurrentProduct = false;
                await noPrintEditor.SaveAsync();
                ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                vm.RefreshProductionSettingsOnly(); await Loaded();
                Assert(!ProductionConfigService.IsProductLabelPrintingEnabled(settings, a.PartNumber) &&
                       ProductionConfigService.IsProductLabelPrintingEnabled(settings, b.PartNumber),
                    "Settings can disable A printing without changing B's saved choice");
                Set("_historyStore", store); Set("_runtimeMode", 1); Set("_masterApproved", true);
                Set("_activeCycleId", "disabled-a"); Set("_cycleStartedAt", now);
                Set("_resultRecordedThisCycle", 0);
                settings.Label.RawDestination = Path.Combine(root, "must-not-print.epl");
                long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
                var committed = (Task<bool>)typeof(TestViewModel).GetMethod("RecordCompletedProductAsync", flags)!
                    .Invoke(vm, [true, "PASS", a, generation, CancellationToken.None, null, now])!;
                Assert(await committed.WaitAsync(TimeSpan.FromSeconds(5)) && vm.Lot == "1001" &&
                       ScalarInt(db, "SELECT COUNT(*) FROM Tests WHERE CycleId='disabled-a' AND Passed=1 AND PrintStatus='NotRequested' AND LabelPayload='';") == 1 &&
                       !File.Exists(settings.Label.RawDestination),
                    "A nonprinting PASS still commits LOT/history once and never captures or sends printer commands");
            }
            finally
            {
                await persistence.DisposeAsync();
                SqliteConnection.ClearAllPools();
                Directory.Delete(root, recursive: true);
            }
        });
    }
}
