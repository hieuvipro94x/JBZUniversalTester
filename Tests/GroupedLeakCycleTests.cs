using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestGroupedLeakCycle()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-GroupedLeak-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (int mask in new[] { 3, 5, 7 })
                {
                    int[] channels = Enumerable.Range(1, 3).Where(channel => (mask & (1 << (channel - 1))) != 0).ToArray();
                    int count = channels.Length;
                    var requests = new List<(WaterProofModelSettings Profile, TaskCompletionSource<WaterProofRunResult> Reply)>();
                    var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
                    var vm = CreateTestViewModel(settings, out FakeBoard board, waterProofRun: (profile, ct) =>
                    {
                        var reply = new TaskCompletionSource<WaterProofRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                        requests.Add((profile.Clone(), reply)); return reply.Task.WaitAsync(ct);
                    });
                    var model = TopologyModel(new Terminal(40, "L1", "1", "2", "RET1"), new Terminal(41, "JIG", "1", "3", "RET1"),
                        new Terminal(42, "L2", "1", "2", "RET2"), new Terminal(43, "JIG", "2", "3", "RET2"),
                        new Terminal(44, "L3", "1", "2", "RET3"), new Terminal(45, "JIG", "3", "3", "RET3"),
                        new Terminal(50, "LAST", "1", "1", "WIRE"), new Terminal(51, "JIG", "4", "3", "WIRE"));
                    vm.SetModel(model);
                    void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                    int Get(string name) => (int)typeof(TestViewModel).GetField(name, flags)!.GetValue(vm)!;
                    object? Call(string name, params object?[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
                    var store = new TestHistoryStore(Path.Combine(root, $"{mask}.db"));
                    await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
                    await persistence.Initialization; Set("_historyStore", store); Set("_productionPersistence", persistence);
                    Set("_suppressEngineChanged", 1); Set("_cycleActive", true); Set("_masterApproved", true);
                    Set("_productionPhase", (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, "Continuity"));
                    var profile = new WaterProofModelSettings { Enabled = true, Channel1Enabled = true, Channel2Enabled = (mask & 2) != 0,
                        Channel3Enabled = (mask & 4) != 0, Channel1Connector = "L1", Channel2Connector = "L2", Channel3Connector = "L3" };
                    Set("_waterProofProfile", profile);
                    var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
                    engine.SetFrameProcessingEnabled(true);
                    long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
                    long seq = 100;
                    void Feed(params (int Source, int[] Targets)[] pairs)
                    {
                        Set("_runtimeMode", 0); var frame = FrameSeq(seq++, pairs); board.Publish(frame); engine.ProcessFrame(frame, false); Set("_runtimeMode", 1);
                    }
                    var all = channels.Select(channel => (38 + 2 * channel, new[] { 39 + 2 * channel })).ToArray();
                    async Task Until(Func<bool> condition)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                        while (!condition()) await Task.Delay(5, timeout.Token);
                    }
                    void Reply(int index, bool passed)
                    {
                        requests[index].Reply.SetResult(new WaterProofRunResult(channels
                            .Select(channel => new WaterProofChannelMeasurement(channel, true, 80,
                                passed || channel != 1 ? 79 : 60, passed || channel != 1 ? 1 : 20, passed || channel != 1)).ToArray(), passed, "GROUP"));
                    }
                    Feed((40, [41]));
                    Assert(!(bool)Call("TryBeginWaterProofOnConnector", generation)! && requests.Count == 0,
                        $"{count} channels: first connector alone cannot start Leak");
                    if (count == 3)
                    {
                        Feed((40, [41]), (42, [43]));
                        Assert(!(bool)Call("TryBeginWaterProofOnConnector", generation)! && requests.Count == 0,
                            "Three enabled channels wait for the third connector");
                    }
                    Feed(all); Assert((bool)Call("TryBeginWaterProofOnConnector", generation)!, "All enabled connectors start one group");
                    await Until(() => requests.Count == 1);
                    Assert(requests[0].Profile.EnabledChannelCount == count &&
                        WaterProofSerialService.BuildTestCommand(requests[0].Profile).StartsWith(mask == 3 ? ":TEST,1,1,0,0," : mask == 5 ? ":TEST,1,0,1,0," : ":TEST,1,1,1,0,"),
                        "One serial TEST command enables the entire configured group");
                    Reply(0, false); await Until(() => Get("_waterProofRunning") == 0 && Get("_waterProofRetestConnectorState") == 1);
                    Assert(Get("_waterProofPassedChannelsMask") == 0 && Get("_preContinuityWaterProofPassed") == 0,
                        "CH1 FAIL invalidates group qualification even when CH2/CH3 measured PASS");
                    Feed(all.Skip(1).ToArray()); Call("ObserveWaterProofRetestConnectorCycle", generation);
                    Assert(Get("_waterProofRetestConnectorState") == 2, "Only the failed CH1 connector needs removal");
                    Feed((40, [41])); Call("ObserveWaterProofRetestConnectorCycle", generation);
                    Assert(requests.Count == 1 && Get("_waterProofRetestConnectorState") == 2,
                        "Refitted CH1 cannot retry while another required connector is absent");
                    Feed(all); Call("ObserveWaterProofRetestConnectorCycle", generation);
                    await Until(() => requests.Count == 2);
                    Assert(requests[1].Profile.EnabledChannelCount == count &&
                        WaterProofSerialService.BuildTestCommand(requests[1].Profile) == WaterProofSerialService.BuildTestCommand(requests[0].Profile),
                        "Retry repeats the same full-group serial command, including previously passed channels");
                    Feed(all.Take(all.Length - 1).ToArray()); Call("ObserveWaterProofRemovalDuringRun");
                    Feed(all); Call("ObserveWaterProofRemovalDuringRun");
                    Reply(1, true); await Until(() => requests.Count == 3);
                    Assert(Get("_preContinuityWaterProofPassed") == 0 && requests[2].Profile.EnabledChannelCount == count,
                        "Removing a previously passed channel during grouped retry invalidates that run and repeats the whole group");
                    Reply(2, true); await Until(() => Get("_preContinuityWaterProofPassed") == 1 && Get("_waterProofRunning") == 0);
                    Assert(Get("_waterProofPassedChannelsMask") == mask && requests.Count == 3 && vm.Total == 0 &&
                        !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)) &&
                        store.Search(new HistorySearchCriteria(null, null, null, "", "ALL")).Count == 0,
                        "Grouped retry does not count production, pulse result relays or add separate Tests history");
                }
            }
            finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
        });
    }
}
