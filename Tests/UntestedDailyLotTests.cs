using System.Globalization;
using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestUntestedDailyLotStartsAtZero()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-UntestedLot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var now = DateTime.Now; string date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string cfg = Path.Combine(root, "test.cfg"), db = Path.Combine(root, "history.db");
            var settings = new ProductionSettings { LotNo = 123456, LotNoDate = date, MasterFaultRequiredCount = 0 };
            settings.LotSettingsByProduct["WH321334"] = new ProductLotSettings
            { StartLotNo = 123456, LotNo = 123456, LotNoDate = date, BulkPrintLotNo = 900, HistoryBatchId = "A-BASE" };
            settings.LotSettingsByProduct["OTHER"] = new ProductLotSettings
            { StartLotNo = 7000, LotNo = 7001, LotNoDate = date, HistoryBatchId = "B-BASE" };
            var model = Model(("PAIR", new[] { 1, 2 })); model.PartNumber = "WH321334"; model.SourcePath = Path.Combine(root, "A.tht");
            var other = Model(("PAIR", new[] { 1, 2 })); other.PartNumber = "OTHER"; other.SourcePath = Path.Combine(root, "B.tht");
            settings.LotSettingsByProduct["WH321331"] = new ProductLotSettings
            { StartLotNo = 123456, LotNo = 123456, LotNoDate = date, HistoryBatchId = "FAIL-BASE" };
            var failOnly = Model(("PAIR", new[] { 1, 2 })); failOnly.PartNumber = "WH321331";
            failOnly.SourcePath = Path.Combine(root, "WH321331.tht");
            var store = new TestHistoryStore(db);
            await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            await persistence.Initialization;
            void Commit(string cycle, ProductModel product, DateTime at, long lot, bool passed = true) =>
                store.CommitResult(ProductionResultCommitRequest.Capture(new TestHistoryRecord
                { CycleId = cycle, PartNumber = product.PartNumber, ModelName = product.ModelName,
                    Started = at, TestStartedAt = at, Finished = at, ResultAt = at, Passed = passed,
                    Result = passed ? "PASS" : "FAIL", InspectionType = HistoryInspectionType.Product, LotNo = lot },
                    product, settings, [], [], null, ProgramIdentityService.VersionText), null);
            Commit("old-A", model, now.AddDays(-1), 123456); Commit("today-B", other, now, 7001);
            Commit("today-fail-only", failOnly, now, 123456, passed: false);
            var vm = CreateTestViewModel(settings, out FakeBoard board);
            var lots = new LotSequenceService(settings, saved => ProductionConfigService.SaveLegacyCfg(saved, cfg));
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            Set("_historyStore", store); Set("_productionPersistence", persistence); Set("_lotSequence", lots);
            async Task Load(ProductModel product)
            {
                vm.SetModel(product);
                Assert(vm.Lot == "0", "Selecting a product never flashes a saved/global LOT during history load");
                await ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!).WaitAsync(TimeSpan.FromSeconds(5));
            }
            try
            {
                await Load(failOnly);
                Assert(vm.Lot == "0" && vm.Total == 1 && vm.Pass == 0 && vm.Fail == 1 && lots.StartLot == 0,
                    "V423 LAN regression: WH321331 with FAIL1/PASS0 cannot restore configured LOT123456");
                Assert(settings.LotSettingsByProduct["WH321331"].StartLotNo == 123456 &&
                       settings.LotSettingsByProduct["WH321331"].DailyLotStartsAtZero,
                    "FAIL-only reset preserves configured base and persists zero-day metadata");
                long afterFail = lots.ReserveForCycle("after-fail");
                Assert(afterFail == 1, "First PASS after a FAIL-only day reserves LOT1");
                Commit("after-fail", failOnly, now, afterFail);
                Assert((await lots.CommitSuccessfulPassAsync("after-fail", afterFail)).Success,
                    "PASS after FAIL-only recovery commits LOT1");
                await Load(other); await Load(failOnly);
                Assert(vm.Lot == "1" && vm.Total == 2 && vm.Pass == 1 && vm.Fail == 1,
                    "Reload preserves FAIL count and committed LOT1 without adding the saved base");
                await Load(model);
                Assert(vm.Lot == "0" && vm.Total == 0 && vm.Pass == 0 && vm.Fail == 0 && lots.StartLot == 0,
                    "A product with yesterday's LOT123456 and no tests today starts at LOT0");
                Assert(settings.LotSettingsByProduct["WH321334"].StartLotNo == 123456 &&
                       settings.LotSettingsByProduct["WH321334"].BulkPrintLotNo == 900,
                    "Daily zero does not rewrite the configured base or bulk printing LOT");
                var restored = ProductionConfigService.LoadSavedSettings(cfg);
                var restarted = new LotSequenceService(restored, _ => { }); restarted.SelectProduct("WH321334", false);
                Assert(restored.LotSettingsByProduct["WH321334"].DailyLotStartsAtZero && restarted.StartLot == 0 &&
                    restarted.NextLot == 0 && restarted.ReserveForCycle("first") == 1,
                    "CFG round-trip and restart preserve zero daily base and first PASS LOT1");
                await Load(other);
                Assert(vm.Lot == "7001" && vm.Pass == 1, "Selecting another tested product restores its own daily LOT");
                await Load(model);
                Assert(vm.Lot == "0" && vm.Total == 0, "Switching back never borrows the other product's LOT");
                long first = lots.ReserveForCycle("first-A");
                Assert(first == 1, "First real reservation after daily reset is LOT1");
                Commit("first-A", model, now, first);
                Assert((await lots.CommitSuccessfulPassAsync("first-A", first)).Success, "First durable PASS commits LOT1");
                await Load(other); await Load(model);
                Assert(vm.Lot == "1" && vm.Pass == 1 && vm.Total == 1,
                    "Reload after PASS does not add the stored123456 base back onto LOT1");
                var fresh = Model(("PAIR", new[] { 1, 2 })); fresh.PartNumber = "NEW"; fresh.SourcePath = Path.Combine(root, "NEW.tht");
                await Load(fresh);
                Assert(vm.Lot == "0" && settings.LotSettingsByProduct["NEW"].StartLotNo == 0,
                    "A new product cannot inherit the machine-wide LOT");
                var failedSettings = new ProductionSettings { LotNo = 123456, LotNoDate = date };
                var failedLots = new LotSequenceService(failedSettings, _ => throw new IOException("CFG unavailable"));
                bool resetSaved = failedLots.TryReconcileCommittedLot(0, out string saveError,
                    failedLots.HistoryBatchKey, restoreSelectedDate: true, resetUntestedDay: true);
                Assert(!resetSaved && saveError.Contains("CFG unavailable") && failedLots.NextLot == 123456 &&
                       !failedSettings.LotSettingsByProduct["DEFAULT"].DailyLotStartsAtZero,
                    "A failed CFG write rolls back both LOT and its zero-day metadata");
                var activeLots = new LotSequenceService(new ProductionSettings { LotNo = 40, LotNoDate = date }, _ => { });
                long reserved = activeLots.ReserveForCycle("active-cycle");
                Assert(!activeLots.TryReconcileCommittedLot(0, out _, activeLots.HistoryBatchKey,
                           restoreSelectedDate: true, resetUntestedDay: true) && activeLots.IsCommitCandidate("active-cycle", reserved),
                    "An uncommitted product reservation cannot be discarded by an empty-day reset");
                Assert(ScalarInt(db, "SELECT COUNT(*) FROM Tests;") == 5 &&
                       ScalarInt(db, "SELECT COUNT(*) FROM Tests WHERE CycleId='today-fail-only' AND Lot=123456 AND Passed=0;") == 1 &&
                       ScalarInt(db, "SELECT COUNT(*) FROM Tests WHERE CycleId='old-A' AND Lot=123456;") == 1 &&
                       ScalarInt(db, "SELECT COUNT(*) FROM Tests WHERE CycleId='today-B' AND Lot=7001;") == 1,
                    "Old and other-product history rows, LOT values and counts remain untouched");
                using var connection = new SqliteConnection($"Data Source={db};Mode=ReadOnly"); connection.Open();
                using var integrity = connection.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
                Assert((string?)integrity.ExecuteScalar() == "ok" && !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                    "Daily LOT reset keeps SQLite integrity and never fires relays");
            }
            finally { await persistence.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
        });
    }
}
