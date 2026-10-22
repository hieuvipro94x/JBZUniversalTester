using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestEarlyGoodMasterLeak()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-MasterEarlyLeak-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (bool initialFailure in new[] { false, true })
                {
                    var requests = new List<(WaterProofModelSettings Profile, TaskCompletionSource<WaterProofRunResult> Reply)>();
                    var settings = new ProductionSettings { MasterFaultRequiredCount = 1 };
                    var vm = CreateTestViewModel(settings, out FakeBoard board, waterProofRun: (profile, ct) =>
                    {
                        var reply = new TaskCompletionSource<WaterProofRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                        requests.Add((profile, reply));
                        return reply.Task.WaitAsync(ct);
                    });
                    var model = TopologyModel(
                        new Terminal(40, "LEAK-1", "1", "2", "RET1"),
                        new Terminal(41, "ANCHOR", "1", "2", "RET1"),
                        new Terminal(42, "LEAK-2", "1", "1", "RET1"),
                        new Terminal(43, "NORMAL", "1", "1", "WIRE1"),
                        new Terminal(44, "ANCHOR", "2", "2", "WIRE1"),
                        new Terminal(50, "LAST", "1", "1", "WIRE2"),
                        new Terminal(51, "ANCHOR", "3", "2", "WIRE2"));
                    vm.SetModel(model);
                    var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
                    void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                    int Get(string name) => (int)typeof(TestViewModel).GetField(name, flags)!.GetValue(vm)!;
                    object? Call(string name, params object?[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
                    var store = new TestHistoryStore(Path.Combine(root, initialFailure ? "retry.db" : "early.db"));
                    await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
                    await persistence.Initialization;
                    Set("_historyStore", store);
                    Set("_productionPersistence", persistence);
                    Set("_masterApproved", false);
                    Set("_masterSequenceState", MasterSequenceState.WaitingGoodMaster);
                    Set("_waterProofProfile", new WaterProofModelSettings { Enabled = true,
                        Channel1Enabled = true, Channel1Connector = "LEAK-1",
                        Channel2Enabled = true, Channel2Connector = "LEAK-2", Channel3Enabled = false,
                        PressMin = 35, LeakLimit = 2 });
                    engine.SetFrameProcessingEnabled(true);
                    long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
                    int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
                    long seq = 100;
                    void Feed(params (int Source, int[] Targets)[] connections)
                    {
                        Set("_runtimeMode", 0);
                        ScanFrame frame = FrameSeq(seq++, connections);
                        board.Publish(frame); // Also release any real scan-supervisor first-frame wait.
                        engine.ProcessFrame(frame, false);
                        Set("_runtimeMode", 1);
                        Call("ProcessMasterEngineChangedOnUi", generation);
                    }
                    async Task Until(Func<bool> condition)
                    {
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        while (!condition()) await Task.Delay(5, deadline.Token);
                    }
                    void Reply(int index, bool passed)
                    {
                        var request = requests[index];
                        request.Reply.SetResult(new WaterProofRunResult(Enumerable.Range(1, 3)
                            .Select(channel => new WaterProofChannelMeasurement(channel,
                                request.Profile.IsChannelEnabled(channel), 80, passed ? 79 : 60,
                                passed ? 1 : 20, passed)).ToArray(), passed, "SELFTEST"));
                    }

                    Feed((43, [44]));
                    Assert(requests.Count == 0, "A connector without Leak configured cannot trigger Master Leak");
                    Set("_masterSequenceState", MasterSequenceState.TestingBadMaster);
                    Assert(!(bool)Call("TryBeginMasterWaterProofOnConnector", generation)!, "NG samples never start Leak");
                    Set("_runtimeMode", 0);
                    engine.ProcessFrame(FrameSeq(seq++, (40, [41])), false);
                    engine.SuppressProbeRelatedWiringFaults([40, 41]);
                    Assert((int)Call("SelectReadyMasterWaterProofChannel")! == 0,
                        "Probe-excluded contacts cannot trigger an early Master Leak channel");
                    engine.ClearProbeEvidenceExclusions();
                    Set("_masterSequenceState", initialFailure ? MasterSequenceState.TestingGoodMaster : MasterSequenceState.WaitingGoodMaster);
                    if (initialFailure) Feed((40, [41]), (43, [44]));
                    else Feed((40, [41])); // The Leak connector itself must also wake the waiting Master.
                    Assert(requests.Count == 1 && requests[0].Profile.Channel1Enabled && !requests[0].Profile.Channel2Enabled &&
                        !engine.ContinuityPassed && Get("_masterPostStarted") == 0 && !vm.MasterApproved,
                        "Good Master CH1 starts on its local shared-RET edge before the other Leak connector and final wire are fitted");
                    Feed((40, [41, 42]), (43, [44]));
                    Assert(requests.Count == 1, "Master Leak channels serialize on the existing single COM owner");
                    Reply(0, !initialFailure);
                    if (initialFailure)
                    {
                        await Until(() => vm.MasterStatus.Contains("THÁO CONNECTOR LEAK"));
                        Assert(vm.ResultStatusText == "SẢN PHẨM KHÔNG ĐẠT\nTHÁO CONNECTOR LEAK" &&
                            vm.StateBackground == "#C62828" && vm.StateForeground == "#FFFFFF",
                            "A failed Master Leak channel reports product rejection and the specific connector-removal action in red");
                        Feed((42, [41]), (43, [44]));
                        await Until(() => vm.MasterStatus.Contains("LẮP LẠI CONNECTOR LEAK"));
                        Feed((40, [41, 42]), (43, [44]));
                        await Until(() => requests.Count == 2);
                        Assert(requests[1].Profile.Channel1Enabled && !requests[1].Profile.Channel2Enabled,
                            "A failed CH1 retries by unplugging/reconnecting CH1 alone, without requiring full sample removal");
                        Reply(1, true);
                    }
                    int secondChannel = initialFailure ? 2 : 1;
                    await Until(() => requests.Count == secondChannel + 1);
                    Assert(Get("_masterWaterProofPassedChannelsMask") == 1 && requests[secondChannel].Profile.Channel2Enabled &&
                        !requests[secondChannel].Profile.Channel1Enabled && !engine.ContinuityPassed,
                        "After CH1 passes, an already fitted CH2 runs alone while incomplete continuity keeps Master locked");
                    Reply(secondChannel, true);
                    await Until(() => Get("_masterWaterProofPassedChannelsMask") == 3 && Get("_masterWaterProofSequenceActive") == 0);
                    Feed((40, [41, 42]), (43, [44]));
                    Assert(requests.Count == secondChannel + 1 && !vm.MasterApproved && Get("_masterPostStarted") == 0,
                        "Cached Leak PASS cannot confirm a Master with missing continuity or trigger duplicate runs");
                    if (initialFailure)
                    {
                        board.StartScanCallback = transport => transport.Publish(
                            FrameSeq(seq++, (40, [41, 42]), (43, [44]), (50, [51])));
                        Feed((40, [41, 42]), (43, [44]), (50, [51]));
                        await Task.Delay(250); // Existing continuity confirmation policy.
                        Feed((40, [41, 42]), (43, [44]), (50, [51]));
                        await Until(() => vm.MasterState == MasterSequenceState.EjectingGoodMaster && Get("_masterEjectInProgress") == 0);
                        Assert(vm.SelectedOperationTabIndex == 2 && vm.HasLeakResultRows &&
                            !vm.HasResistanceResultRows && vm.InspectionResultRows.Count == 2 &&
                            vm.InspectionResultRows.All(row => row.ResultText == "PASS"),
                            "Leak-only good Master retains both channel results in the common table until full sample removal");
                        Assert(requests.Count == secondChannel + 1 && !vm.IsPersistenceFault && !vm.IsDeviceFault &&
                            vm.ResultStatusText == "HOÀN THÀNH KIỂM TRA MẪU ĐẠT" &&
                            board.Commands.Count(command => command == "SET:1") == 1 && !board.Commands.Contains("SET:2") &&
                            vm.Total == total && vm.Pass == pass && vm.Fail == fail,
                            $"Full continuity confirms and persists the good Master using its early Leak cache, ejecting JIG once without re-running Leak or counting production: requests={requests.Count}, persistence={vm.IsPersistenceFault}, device={vm.IsDeviceFault}, status={vm.ResultStatusText}, commands={string.Join(',', board.Commands)}, counters={total}/{pass}/{fail}->{vm.Total}/{vm.Pass}/{vm.Fail}");
                        continue;
                    }
                    Feed((42, [41]), (43, [44]));
                    Assert(Get("_masterWaterProofPassedChannelsMask") == 2, "Removing CH1 invalidates only CH1 before Master approval");
                    Feed((40, [41, 42]), (43, [44]));
                    await Until(() => requests.Count == secondChannel + 2);
                    int reconnect = secondChannel + 1;
                    Assert(requests[reconnect].Profile.Channel1Enabled && !requests[reconnect].Profile.Channel2Enabled,
                        "Reconnecting a passed channel requires another Leak run, preserving the other channel PASS");
                    Set("_masterHistoryCycleId", "NEW-MASTER-SAMPLE");
                    Set("_lastWaterProofMeasurements", Array.Empty<WaterProofChannelMeasurement>());
                    Set("_masterWaterProofPassedChannelsMask", 0);
                    Set("_masterWaterProofSequenceActive", 1);
                    Reply(reconnect, true);
                    await Until(() => Get("_waterProofRunning") == 0);
                    await Task.Delay(20);
                    Assert(Get("_masterWaterProofPassedChannelsMask") == 0 && Get("_masterWaterProofSequenceActive") == 1 &&
                        ((IReadOnlyList<WaterProofChannelMeasurement>)typeof(TestViewModel).GetField("_lastWaterProofMeasurements", flags)!.GetValue(vm)!).Count == 0,
                        "An old sample result cannot update the next sample or release its active Leak lock");
                    Assert(vm.Total == total && vm.Pass == pass && vm.Fail == fail &&
                        !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                        "Early Master Leak never counts production or pulses MARKING/JIG before the full Master passes");
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-MasterEarlyLeak-"), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(root, recursive: true);
            }
        });
    }
}
