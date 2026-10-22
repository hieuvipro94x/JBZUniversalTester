using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLeakRunCompletionAndProductFailure()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-LeakCompletion-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var replies = new List<TaskCompletionSource<WaterProofRunResult>>();
                var tokens = new List<CancellationToken>();
                var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
                var vm = CreateTestViewModel(settings, out FakeBoard board, waterProofRun: (_, ct) =>
                {
                    var reply = new TaskCompletionSource<WaterProofRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                    replies.Add(reply); tokens.Add(ct); return reply.Task.WaitAsync(ct);
                });
                var model = TopologyModel(new Terminal(40, "LEAK", "1", "2", "RET1"),
                    new Terminal(41, "ANCHOR", "1", "2", "RET1"),
                    new Terminal(50, "NORMAL", "1", "1", "WIRE"), new Terminal(51, "ANCHOR", "2", "2", "WIRE"));
                model.PartNumber = "LEAK-COMPLETE-" + Guid.NewGuid().ToString("N");
                vm.SetModel(model);
                await (Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!;
                void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                object? Get(string name) => typeof(TestViewModel).GetField(name, flags)!.GetValue(vm);
                var engine = (TestEngine)Get("_engine")!;
                var store = new TestHistoryStore(Path.Combine(root, "history.db"));
                await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
                await persistence.Initialization;
                Set("_historyStore", store); Set("_productionPersistence", persistence); Set("_runtimeMode", 1);
                Set("_cycleActive", true); Set("_masterApproved", true);
                Set("_productionPhase", (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, "Continuity"));
                Set("_waterProofProfile", new WaterProofModelSettings { Enabled = true, Channel1Enabled = true,
                    Channel1Connector = "LEAK", Channel2Enabled = false, Channel3Enabled = false, PressMin = 35, LeakLimit = 2 });
                engine.SetFrameProcessingEnabled(true);
                long seq = 100;
                void Feed(params (int Source, int[] Targets)[] pairs) => board.Publish(FrameSeq(seq++, pairs));
                async Task Until(Func<bool> condition)
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    while (!condition())
                    {
                        if (deadline.IsCancellationRequested)
                            throw new InvalidOperationException($"Leak fixture timeout: state={vm.State}, phase={Get("_productionPhase")}, running={Get("_waterProofRunning")}, retry={Get("_waterProofRetestConnectorState")}, continuity={engine.ContinuityPassed}, lastValid={engine.LastFrameValid}, fail={vm.Fail}, pending={vm.IsProductRemovalPending}, device={vm.IsDeviceFault}, persistence={vm.IsPersistenceFault}, rows={vm.FinalWaterProofRows.Count}");
                        await Task.Delay(5);
                    }
                }
                WaterProofRunResult Fail(double leak) => new([new(1, true, 80, 80 - leak, leak, false)], false, "FINAL_FAIL");
                Feed((40, [41])); Feed((40, [41]));
                await Until(() => replies.Count == 1);
                Feed(); Feed();
                Assert(!tokens[0].IsCancellationRequested && !replies[0].Task.IsCompleted && replies.Count == 1 && vm.Total == 0,
                    "Removing the running Leak connector and even all product connections cannot cancel the COM run or count a result early");
                replies[0].SetResult(Fail(12));
                await Until(() => (int)Get("_waterProofRetestConnectorState")! == 2 && (int)Get("_waterProofRunning")! == 0);
                Assert(vm.WaterProofChannels.Any(row => row.Channel == 1 && row.ResultText == "FAIL" && row.Leak == 12) &&
                       vm.Total == 0 && vm.Pass == 0 && vm.Fail == 0,
                    "The actual final channel failure remains displayed and may retry before continuity finishes");
                Feed((40, [41])); Feed((40, [41]));
                await Until(() => replies.Count == 2);
                Feed((40, [41]), (50, [51]));
                Assert(vm.Total == 0 && !tokens[1].IsCancellationRequested,
                    "Continuity PASS cannot finalize while a Leak result is still pending");
                replies[1].SetResult(Fail(15));
                await Until(() => (int)Get("_waterProofRunning")! == 0);
                Feed((40, [41]), (50, [51])); Feed((40, [41]), (50, [51]));
                await Until(() => vm.Fail == 1 && vm.IsProductRemovalPending);
                await (Task)Get("_leakFailureCommitTask")!;
                Assert(vm.Total == 1 && vm.Pass == 0 && vm.Fail == 1 && vm.SelectedOperationTabIndex == 2 &&
                       vm.FinalWaterProofRows.Single().ResultText == "NG LEAK" &&
                       vm.ResultStatusText == "KIỂM TRA KÍN NƯỚC KHÔNG ĐẠT" &&
                       !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                    "Final Leak FAIL with continuity PASS commits exactly one failed product, retains NG LEAK and never pulses MARKING");
                Feed((40, [41]), (50, [51])); Feed((50, [51])); Feed((40, [41]), (50, [51]));
                Assert(vm.Fail == 1 && vm.Total == 1 && replies.Count == 2 && vm.IsProductRemovalPending,
                    "A finalized Leak NG cannot retry its connector or count twice before full product removal");
                using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "history.db")}"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM WaterProofMeasurements";
                    Assert(Convert.ToInt32(command.ExecuteScalar()) >= 3,
                        "Both Leak attempts and the final product retain actual Leak measurements in durable history");
                }
                Feed(); Feed();
                Assert(!vm.IsProductRemovalPending && vm.ResultStatusText == "LẮP SẢN PHẨM" && vm.Fail == 1,
                    "Two full empty frames clear finalized NG and rearm the next product without changing the recorded failure");
                Feed((40, [41])); Feed((40, [41]));
                await Until(() => replies.Count == 3);
                Feed(); Feed((40, [41]));
                Assert(!tokens[2].IsCancellationRequested, "An unplug/replug while measuring still cannot abort the active Leak run");
                replies[2].SetResult(new WaterProofRunResult([new(1, true, 80, 79, 1, true)], true, "FINAL_PASS"));
                await Until(() =>
                {
                    Feed((40, [41]));
                    return replies.Count == 4;
                });
                Assert((int)Get("_waterProofPassedChannelsMask")! == 0 && vm.Total == 1 &&
                       !tokens[2].IsCancellationRequested,
                    "Reconnection during the initial run queues a fresh Leak run after RESULT without a second unplug or premature product PASS");
                Feed(); Feed((40, [41]));
                Assert(replies.Count == 4 && !tokens[3].IsCancellationRequested,
                    "Reconnection during a retry also waits for its own final result without overlapping COM runs");
                replies[3].SetResult(new WaterProofRunResult([new(1, true, 80, 79, 1, true)], true, "FINAL_PASS"));
                await Until(() =>
                {
                    Feed((40, [41]));
                    return replies.Count == 5;
                });
                Assert((int)Get("_waterProofPassedChannelsMask")! == 0 && vm.Total == 1,
                    "A retry interrupted by connector removal automatically restarts once after its result when already reconnected");
                replies[4].SetResult(new WaterProofRunResult([new(1, true, 80, 79, 1, true)], true, "FINAL_PASS"));
                await Until(() => (int)Get("_waterProofRunning")! == 0 && (int)Get("_preContinuityWaterProofPassed")! == 1);
                Feed((40, [41]));
                Assert(vm.Total == 1 && vm.Fail == 1 && vm.Pass == 0,
                    "Leak-only retries never count another product while ordinary wiring is still incomplete");
            }
            finally
            {
                AppSoundService.Current.StopAll(force: true);
                SqliteConnection.ClearAllPools();
                if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-LeakCompletion-"), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(root, true);
            }
        });
    }

    private static void TestEquipmentErrorReturnToMain()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (bool deviceFault in new[] { false, true })
        {
            var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
            vm.SetModel(Model(("PAIR", new[] { 1, 18 })));
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            Set("_runtimeMode", 1);
            Set("_waterProofEquipmentErrorAwaitingRemoval", true);
            Set("_productRemovalPending", 1);
            var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
            engine.SetFrameProcessingEnabled(true);
            board.Publish(FrameSeq(100, (1, [18])));
            Assert(!vm.StopViewAsync().GetAwaiter().GetResult(), "Equipment error cannot bypass a physically connected product");
            board.Publish(FrameSeq(101));
            Assert(!vm.StopViewAsync().GetAwaiter().GetResult(), "One empty frame cannot bypass physical removal confirmation");
            board.Publish(FrameSeq(102));
            if (deviceFault) Set("_deviceFault", 1);
            int commands = board.Commands.Count;
            Assert(!vm.HasProductOnTestTable && vm.StopViewAsync().GetAwaiter().GetResult() &&
                   board.Commands.Count == commands && vm.IsDeviceFault == deviceFault,
                "A confirmed empty table permits Main during equipment errors without sending board commands or clearing the fault");
        }
    }
}
