using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestMasterCommittedRelayBlockedRemoval()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string root = Path.Combine(Path.GetTempPath(), "JBZ-MasterBlocked-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ProductionPersistenceService? persistence = null;
        try
        {
            var settings = new ProductionSettings { MasterFaultRequiredCount = 1,
                MasterSelectedFaultSamples = MasterSampleSelection.WrongWiring };
            var vm = CreateTestViewModel(settings, out FakeBoard board);
            var model = Model(("A", new[] { 1, 2 }), ("B", new[] { 3, 4 }));
            model.PartNumber = "MASTER-BLOCKED-" + Guid.NewGuid().ToString("N");
            var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            object? Call(string name, params object?[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
            var store = new TestHistoryStore(Path.Combine(root, "history.db"));
            persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            persistence.Initialization.GetAwaiter().GetResult();
            Set("_historyStore", store);
            Set("_productionPersistence", persistence);
            Set("_model", model);
            engine.SetModel(model);
            Set("_runtimeMode", 1);
            Set("_masterApproved", false);
            Set("_masterSequenceState", MasterSequenceState.TestingGoodMaster);
            Set("_requiredMasterFaultSamples", new[] { MasterSampleType.WrongWiring });
            Set("_masterPostStarted", 1);
            using var cycle = new CancellationTokenSource();
            Set("_cycleCts", cycle);
            Call("BeginMasterHistoryCycle", HistoryInspectionType.MasterGood);
            long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
            ScanFrame Frame(long seq, bool all, long scanGeneration = 2) => FrameSeq(seq, all
                ? [(1, new[] { 2 }), (3, new[] { 4 })] : [(1, new[] { 2 })]) with
                { SourceCount = 640, ExpectedIoCount = 640, ScanUnitCount = 10, ScanGeneration = scanGeneration };
            engine.SetFrameProcessingEnabled(true);
            board.Publish(Frame(100, true));
            engine.ProcessFrame(Frame(101, true), false);
            Assert(engine.ContinuityPassed, "Master starts with validated continuity");
            board.StopScanCallback = _ =>
            {
                board.StopScanCallback = null;
                // Lose contact only after History has committed, during STOP.
                engine.ProcessFrame(Frame(102, false), false);
            };
            board.StartScanCallback = transport => transport.Publish(Frame(200, false, 3));
            ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!).GetAwaiter().GetResult();
            int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
            ((Task)Call("CompleteGoodMasterAsync", generation)!).GetAwaiter().GetResult();
            Assert(board.IsScanning && board.Commands.Last() == "START" && !vm.IsDeviceFault && !vm.IsPersistenceFault,
                "Blocked Master relay resumes real scan instead of leaving STOP with suspended watchdog");
            Assert(!vm.MasterApproved && vm.MasterState == MasterSequenceState.TestingGoodMaster &&
                   !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                "Restoring scan never ejects, marks or approves the blocked Master");
            using (var db = new SqliteConnection("Data Source=" + Path.Combine(root, "history.db")))
            {
                db.Open(); using var query = db.CreateCommand();
                query.CommandText = "SELECT COUNT(*) FROM Tests WHERE Passed=1";
                Assert(Convert.ToInt32(query.ExecuteScalar()) == 1, "Exactly one committed PASS survives the blocked relay");
            }
            board.Publish(Frame(201, false, 3));
            Assert(vm.MasterState == MasterSequenceState.TestingGoodMaster, "Partial removal cannot release the Master gate");
            board.Publish(FrameSeq(202) with { ScanGeneration = 3, Complete = false });
            Assert(vm.MasterState == MasterSequenceState.TestingGoodMaster, "Invalid removal cannot release the Master gate");
            for (long seq = 203; seq <= 207; seq++) board.Publish(FrameSeq(seq) with { ScanGeneration = 3 });
            Assert(vm.MasterState == MasterSequenceState.WaitingGoodMaster && !vm.MasterApproved &&
                   vm.Total == total && vm.Pass == pass && vm.Fail == fail && board.IsScanning,
                "Full removal re-arms good Master testing without counting production or restarting the app");
            ((Task)typeof(TestViewModel).GetField("_masterPersistenceTask", flags)!.GetValue(vm)!).GetAwaiter().GetResult();
            using (var db = new SqliteConnection("Data Source=" + Path.Combine(root, "history.db")))
            {
                db.Open(); using var query = db.CreateCommand();
                query.CommandText = "SELECT COUNT(*) FROM Tests";
                Assert(Convert.ToInt32(query.ExecuteScalar()) == 1, "Removal neither duplicates nor overwrites the stored Master PASS");
                query.CommandText = "SELECT COUNT(*) FROM Tests WHERE Passed=1 AND RemovedAt IS NOT NULL";
                Assert(Convert.ToInt32(query.ExecuteScalar()) == 1, "Removal updates the same PASS row without changing its result");
                query.CommandText = "PRAGMA integrity_check";
                Assert((string?)query.ExecuteScalar() == "ok", "Blocked-relay recovery preserves SQLite integrity");
            }
        }
        finally
        {
            if (persistence is not null) persistence.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SqliteConnection.ClearAllPools();
            if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-MasterBlocked-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, recursive: true);
        }
    }
}
