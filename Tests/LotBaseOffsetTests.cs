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
    private static void TestLotBaseRetainsDailyPassProgress()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-LotBaseOffset-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string cfg = Path.Combine(root, "settings.cfg"), db = Path.Combine(root, "history.db");
            DateTime now = DateTime.Now;
            var settings = new ProductionSettings { LotNo = 0, MasterFaultRequiredCount = 0 };
            var store = new TestHistoryStore(db);
            await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            await persistence.Initialization;
            try
            {
                var model = Model(("PAIR", new[] { 1, 2 }));
                model.PartNumber = "OFFSET-100"; model.SourcePath = Path.Combine(root, "OFFSET.tht");
                for (int i = 1; i <= 103; i++)
                {
                    bool pass = i <= 100;
                    DateTime at = now.Date.AddHours(8).AddSeconds(i);
                    store.CommitResult(ProductionResultCommitRequest.Capture(new TestHistoryRecord
                    {
                        CycleId = "old-" + i, PartNumber = model.PartNumber, ModelName = model.ModelName,
                        Started = at, TestStartedAt = at, Finished = at, ResultAt = at,
                        Passed = pass, Result = pass ? "PASS" : "FAIL", InspectionType = HistoryInspectionType.Product,
                        LotNo = pass ? i : 100, ProductionBatchKey = "old-unconfigured"
                    }, model, settings, [], [], null, ProgramIdentityService.VersionText), null);
                }
                var vm = CreateTestViewModel(settings, out FakeBoard board);
                void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                var lots = new LotSequenceService(settings, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg));
                Set("_lotSequence", lots); Set("_historyStore", store); Set("_productionPersistence", persistence);
                Set("_partCounterStore", new Lazy<PartCounterStore>(() => new PartCounterStore(Path.Combine(root, "PartCnt.txt"))));
                async Task Loaded() => await ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                async Task SaveBase(long start)
                {
                    var editor = new ProductionSettingsViewModel(vm, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg),
                        JsonSerializer.Deserialize<ProductionSettings>(JsonSerializer.Serialize(settings))!);
                    editor.Settings.LotNo = start;
                    editor.Settings.Label.TemplateType = LabelSettings.LargeSqdzTemplate;
                    await editor.SaveAsync();
                    ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                    vm.RefreshProductionSettingsOnly();
                    await Loaded();
                }
                vm.SetModel(model); await Loaded();
                Assert(vm.Pass == 100 && vm.Fail == 3 && vm.Lot == "100", "An unconfigured product recovers its existing 100 PASS and three FAIL");
                await SaveBase(7000);
                Assert(vm.Lot == "7100" && vm.Pass == 100 && vm.Total == 103 &&
                       ProductionConfigService.LoadSavedSettings(cfg).LotSettingsByProduct[model.PartNumber].LotNo == 7100,
                    "Save 7000 displays and durably retains 7000 + 100 PASS; FAIL never advances LOT");
                string batch = lots.HistoryBatchKey;
                await SaveBase(7000);
                Assert(vm.Lot == "7100" && lots.HistoryBatchKey == batch, "Saving the same base does not add the 100 PASS twice");

                settings.Label.RawDestination = Path.Combine(root, "current-7100.epl");
                LabelPrintRequest current = LabelPrintRequest.Capture(new TestHistoryRecord
                { CycleId = "preview-current", LotNo = lots.NextLot, Finished = now }, model, settings.Label);
                var transport = await vm.PrintSettingsLabelAsync(current);
                Assert(transport.Printed && current.Data.LotNo == 7100 &&
                       File.ReadAllText(settings.Label.RawDestination).Contains("7100", StringComparison.Ordinal),
                    "The displayed 7100 reaches the real label rendering/file-output pipeline unchanged");

                Set("_historyStore", store); Set("_runtimeMode", 1); Set("_masterApproved", true);
                Set("_activeCycleId", "next-101"); Set("_cycleStartedAt", now); Set("_resultRecordedThisCycle", 0);
                settings.Label.RawDestination = Path.Combine(root, "next-7101.epl");
                long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
                bool committed = await ((Task<bool>)typeof(TestViewModel).GetMethod("RecordCompletedProductAsync", flags)!
                    .Invoke(vm, [true, "PASS", model, generation, CancellationToken.None, null, now])!).WaitAsync(TimeSpan.FromSeconds(5));
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (typeof(TestViewModel).GetField("_failedLabelPrint", flags)!.GetValue(vm) is null)
                    await Task.Delay(10, deadline.Token);
                // Production remains COM-only. Inspect its durable offline
                // snapshot, then exercise that exact payload via the test-file transport.
                object context = typeof(TestViewModel).GetField("_failedLabelPrint", flags)!.GetValue(vm)!;
                var captured = (LabelPrintRequest)context.GetType().GetProperty("Request")!.GetValue(context)!;
                Assert(!File.Exists(settings.Label.RawDestination), "Production cannot silently switch from COM to file output");
                var nextTransport = await vm.PrintSettingsLabelAsync(captured);
                Assert(committed && vm.Lot == "7101" && vm.Pass == 101 &&
                       nextTransport.Printed && captured.Data.LotNo == 7101 &&
                       File.ReadAllText(settings.Label.RawDestination).Contains("7101", StringComparison.Ordinal) &&
                       ScalarInt(db, "SELECT COUNT(*) FROM Tests WHERE CycleId='next-101' AND Lot=7101 AND Passed=1 AND LabelPayload LIKE '%7101%';") == 1,
                    "The 101st PASS atomically records and prepares LOT 7101; its captured printer payload retains that LOT");

                ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                vm.SetModel(model); await Loaded();
                Assert(vm.Lot == "7101" && vm.Pass == 101, "Restart/reselect restores the edited base plus progress without double counting");
                await SaveBase(2000);
                Assert(vm.Lot == "2101" && vm.Pass == 101, "Lowering the base retains all 101 PASS");
                Assert(ScalarInt(db, "SELECT COUNT(*) FROM Tests WHERE CycleId LIKE 'old-%';") == 103 &&
                       ScalarInt(db, "SELECT SUM(Lot) FROM Tests WHERE CycleId LIKE 'old-%';") == 5350 &&
                       ScalarInt(db, "SELECT COUNT(*) FROM Tests;") == 104,
                    "Base edits never rewrite, delete or duplicate historical LOTs, FAIL rows or counts");
                using var connection = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
                connection.Open();
                using var integrity = connection.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check;";
                Assert(Convert.ToString(integrity.ExecuteScalar(), CultureInfo.InvariantCulture) == "ok", "LOT offset preserves SQLite integrity");
                Assert(!board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)), "Changing the LOT base never fires relays");
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
