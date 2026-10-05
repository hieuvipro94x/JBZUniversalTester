using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;
using System.Reflection;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestHistoryLotBatchOrdinals()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-ordinal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string db = Path.Combine(root, "history.db");
            var store = new TestHistoryStore(db);
            DateTime at = new(2026, 10, 5, 9, 0, 0);
            int position = 0;
            void Add(string part, string batch, bool passed, long lot, string inspection = HistoryInspectionType.Product)
            {
                DateTime started = at.AddSeconds(position++);
                store.Add(new TestHistoryRecord
                {
                    Started = started, TestStartedAt = started, Finished = started.AddMilliseconds(100),
                    PartNumber = part, ModelFile = part + ".tht", ModelName = part,
                    ProductionBatchKey = batch, Passed = passed, LotNo = lot,
                    InspectionType = inspection, Result = passed ? "PASS" : "FAIL",
                    CycleId = "ordinal-" + position
                });
            }
            Add("A", "", true, 0, HistoryInspectionType.MasterGood);
            Add("A", "", true, 0, HistoryInspectionType.MasterBad);
            Add("A", "batch-1", true, 2001);
            Add("A", "batch-1", false, 0);
            Add("B", "batch-1", false, 0);
            Add("A", "batch-1", true, 2002);
            Add("A", "batch-2", false, 0);
            Add("A", "batch-2", true, 3001);
            var criteria = new HistorySearchCriteria(null, null, null, "", "ALL", MaxRows: 3);
            IReadOnlyList<TestHistoryRecord> first = store.SearchSummary(criteria);
            TestHistoryRecord cursor = first[^1];
            IReadOnlyList<TestHistoryRecord> second = store.SearchSummary(criteria with
                { BeforeHistoryAt = cursor.EffectiveTestStartedAt, BeforeId = cursor.Id, MaxRows = 10 });
            TestHistoryRecord[] all = first.Concat(second).ToArray();
            Assert(all.Select(r => r.HistoryOrdinal).SequenceEqual(new long?[] { null, null, 1, 2, 1, 3, 1, 2 }),
                "PASS and FAIL count per product/LOT batch, while Master does not consume production ordinals");
            Assert(all.Take(2).All(r => r.IsMasterRecord) && all.Skip(2).All(r => r.IsProductionRecord),
                "Both Master samples are stored before the production tests");
            HistorySummary summary = store.GetHistorySummary(criteria);
            Assert(summary.MasterTotal == 2 && summary.ProductTotal == 6 &&
                   summary.ProductPass == 3 && summary.ProductFail == 3,
                "Validated NG Master is separate from production PASS/FAIL totals");
            IReadOnlyList<TestHistoryRecord> failures = store.SearchSummary(criteria with { Result = "FAIL", MaxRows = 10 });
            Assert(failures.Select(r => r.HistoryOrdinal).SequenceEqual(new long?[] { 2, 1, 1 }) &&
                   failures.All(r => r.ExportLotText.Length == 0),
                "Filtering FAIL preserves batch ordinals and keeps LOT blank");
            TestHistoryRecord[] exported = store.EnumerateForExport(criteria).ToArray();
            Assert(HistoryExportService.ExportCsv(Path.Combine(root, "history.csv"), exported) == 8 &&
                   HistoryExportService.ExportXlsx(Path.Combine(root, "history.xlsx"), exported) == 8 &&
                   exported.Select(r => r.HistoryOrdinal).SequenceEqual(all.Select(r => r.HistoryOrdinal)),
                "CSV/XLSX export complete history with the same per-batch ordinals as paginated UI");
            using (var connection = new SqliteConnection("Data Source=" + db))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check;";
                Assert((string)command.ExecuteScalar()! == "ok", "History ordinal fixture integrity is ok");
                command.CommandText = "UPDATE SchemaInfo SET SchemaVersion=7;";
                command.ExecuteNonQuery();
                command.CommandText = "DROP INDEX IX_Tests_ProductBatch_HistoryAt_Id; ALTER TABLE Tests DROP COLUMN ProductionBatchKey;";
                command.ExecuteNonQuery();
            }
            var upgraded = new TestHistoryStore(db);
            Assert(upgraded.SearchForExport(criteria).Count == 8,
                "Schema v7 upgrade retains every production and Master record");
            using (var connection = new SqliteConnection("Data Source=" + db))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check;";
                Assert((string)command.ExecuteScalar()! == "ok", "Upgraded fixture integrity is ok");
                command.CommandText = "PRAGMA foreign_key_check;";
                Assert(command.ExecuteScalar() is null, "Upgrade creates no orphan child records");
            }
            var settings = new ProductionSettings { LotNo = 2000 };
            DateTime clock = at;
            var sequence = new LotSequenceService(settings, _ => { }, () => clock);
            sequence.SelectProduct("A", true);
            string batch1 = sequence.HistoryBatchKey;
            long reserved = sequence.ReserveForCycle("pass");
            Assert(sequence.TryCommitSuccessfulPass("pass", reserved, out _), "Fixture PASS commits LOT");
            Assert(sequence.HistoryBatchKey == batch1, "PASS does not create a new batch");
            ProductionConfigService.SetProductLot(settings, "A", 3000, clock.ToString("yyyy-MM-dd"));
            sequence.RefreshActiveProduct();
            string batch2 = sequence.HistoryBatchKey;
            Assert(batch2 != batch1, "Changing LOT start creates a new batch");
            string cfg = Path.Combine(root, "settings.cfg");
            ProductionConfigService.SaveLegacyCfg(settings, cfg);
            var restored = (ProductionSettings)typeof(ProductionConfigService).GetMethod("LoadEnglishCfg",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [cfg])!;
            var restoredSequence = new LotSequenceService(restored, _ => { }, () => clock);
            restoredSequence.SelectProduct("A", false);
            Assert(restoredSequence.HistoryBatchKey == batch2, "Config reload keeps the same LOT batch");
            clock = clock.AddDays(1);
            Assert(sequence.HistoryBatchKey != batch2, "Daily LOT reset creates a new batch");

            var masterStore = new TestHistoryStore(Path.Combine(root, "actual-master.db"));
            var masterSettings = new ProductionSettings { MasterFaultRequiredCount = 1 };
            TestViewModel vm = CreateTestViewModel(masterSettings);
            LoadReadyModel(vm, Model(("PAIR", new[] { 1, 2 })));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!)
                .GetAwaiter().GetResult();
            typeof(TestViewModel).GetField("_historyStore", flags)!.SetValue(vm, masterStore);
            var persistence = new ProductionPersistenceService(masterStore, masterSettings, "SELFTEST");
            try
            {
                persistence.Initialization.GetAwaiter().GetResult();
                typeof(TestViewModel).GetField("_productionPersistence", flags)!.SetValue(vm, persistence);
                int beforePass = vm.Pass;
                int beforeFail = vm.Fail;
                long beforeProbe = vm.ProbeCycleCount;
                foreach (string inspection in new[] { HistoryInspectionType.MasterGood, HistoryInspectionType.MasterBad })
                {
                    typeof(TestViewModel).GetMethod("BeginMasterHistoryCycle", flags)!.Invoke(vm, [inspection]);
                    typeof(TestViewModel).GetMethod("RecordMasterHistory", flags)!.Invoke(vm,
                        [inspection, true, Array.Empty<FaultDetail>(), null]);
                    ((Task)typeof(TestViewModel).GetField("_masterPersistenceTask", flags)!.GetValue(vm)!)
                        .GetAwaiter().GetResult();
                }
                HistorySummary masterSummary = masterStore.GetHistorySummary(criteria);
                Assert(masterSummary.MasterTotal == 2 && masterSummary.ProductTotal == 0 &&
                       vm.Pass == beforePass && vm.Fail == beforeFail && vm.ProbeCycleCount == beforeProbe,
                    $"Actual Master recording saves both samples without production or Probe counters: " +
                    $"master={masterSummary.MasterTotal}, product={masterSummary.ProductTotal}, " +
                    $"pass={beforePass}->{vm.Pass}, fail={beforeFail}->{vm.Fail}, probe={beforeProbe}->{vm.ProbeCycleCount}");
            }
            finally
            {
                persistence.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
