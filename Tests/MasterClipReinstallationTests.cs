using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestMasterClipReinstallation()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-MasterClip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (bool badSample in new[] { false, true })
                {
                    var settings = new ProductionSettings { MasterFaultRequiredCount = 1,
                        MasterSelectedFaultSamples = MasterSampleSelection.WrongWiring,
                        IoConfirm1 = 1, IoConfirmN = 1, ProductSettleTimeMs = 0,
                        ShortCircuitConfirmMs = 0, WrongConnectionConfirmMs = 0, JigContactUnstableWindowMs = 0 };
                    var leakReplies = new List<TaskCompletionSource<WaterProofRunResult>>();
                    var vm = CreateTestViewModel(settings, out FakeBoard board, waterProofRun: (profile, ct) =>
                    {
                        var reply = new TaskCompletionSource<WaterProofRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                        leakReplies.Add(reply);
                        return reply.Task.WaitAsync(ct);
                    });
                    var model = TopologyModel(
                        new Terminal(1, "NORMAL", "1", "1", "WIRE1"),
                        new Terminal(18, "ANCHOR", "1", "2", "WIRE1"),
                        new Terminal(2, "UNFITTED", "1", "1", "WIRE2"),
                        new Terminal(19, "ANCHOR", "2", "2", "WIRE2"),
                        new Terminal(40, "LEAK", "1", "2", "RET1"),
                        new Terminal(41, "ANCHOR", "3", "2", "RET1"));
                    model.PartNumber = "MASTER-CLIP-" + (badSample ? "NG" : "GOOD");
                    var common = new PinRecord("CLIP", "AO", 201, "AO", PinType: "AO");
                    ClipBranch[] branches = Enumerable.Range(1, 3).Select(index => new ClipBranch(
                        $"a{index}", index, 201 + index,
                        new PinRecord("CLIP", $"a{index}", 201 + index, $"a{index}", PinType: $"a{index}"), null)).ToArray();
                    model.Pins.AddRange([common, .. branches.Select(branch => branch.ClipPin)]);
                    model.Clip = new ClipTopology(common, branches);
                    vm.SetModel(model);
                    await (Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!;
                    var store = new TestHistoryStore(Path.Combine(root, badSample ? "bad.db" : "good.db"));
                    await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
                    await persistence.Initialization;
                    void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                    object? Get(string name) => typeof(TestViewModel).GetField(name, flags)!.GetValue(vm);
                    var engine = (TestEngine)Get("_engine")!;
                    Set("_historyStore", store);
                    Set("_productionPersistence", persistence);
                    Set("_runtimeMode", 1);
                    engine.SetFrameProcessingEnabled(true);
                    if (badSample)
                    {
                        Set("_masterGoodVerified", true);
                        typeof(TestViewModel).GetMethod("TransitionToBadMaster", flags)!.Invoke(vm, null);
                    }
                    int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
                    long sequence = 100;
                    void Feed(params (int Source, int[] Targets)[] pairs) => board.Publish(FrameSeq(sequence++, pairs));
                    void AssertClipMissing(string message)
                    {
                        IReadOnlyList<FaultRow> rows = engine.BuildRows();
                        Assert(new[] { 201, 202, 203, 204 }.All(io => rows.Any(row => row.Io == io &&
                            row.Kind == FaultKind.MissingConnection)) && engine.PassedNetCount == 1 && !engine.ContinuityPassed,
                            message);
                    }
                    MasterSequenceState testing = badSample ? MasterSequenceState.TestingBadMaster : MasterSequenceState.TestingGoodMaster;
                    MasterSequenceState waiting = badSample ? MasterSequenceState.WaitingBadMaster : MasterSequenceState.WaitingGoodMaster;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        Feed((1, [18]), (201, [202, 203]));
                        Feed((1, [18]), (201, [202, 203]));
                        Assert(vm.MasterState == testing && engine.PassedNetCount == 3 && !engine.ContinuityPassed,
                            "An incomplete Master can qualify two CLIP branches while the third and an ordinary pair are still missing");
                        Set("_masterWaterProofPassedChannelsMask", 7);
                        Set("_lastWaterProofMeasurements", new WaterProofChannelMeasurement[] { new(1, true, 80, 79, 1, true) });
                        Feed((1, [18]));
                        Assert(vm.MasterState == testing, "Removing only CLIP is not full physical sample removal");
                        Feed();
                        Assert(vm.MasterState == testing, "One empty frame cannot end the incomplete Master attempt");
                        Feed();
                        Assert(vm.MasterState == waiting && engine.PassedNetCount == 0 &&
                               (int)Get("_masterWaterProofPassedChannelsMask")! == 0 &&
                               ((IReadOnlyList<WaterProofChannelMeasurement>)Get("_lastWaterProofMeasurements")!).Count == 0,
                            "Confirmed full Master removal resets ordinary/CLIP qualification and previous sample measurement cache immediately");
                        Feed((1, [18]));
                        Feed((1, [18]));
                        AssertClipMissing("Reinstalling only an ordinary pair must show AO and every aN as missing, including branches fitted on previous attempts");
                        Assert(new[] { 201, 202, 203, 204 }.All(io => vm.Faults.Any(row => row.Io == io)),
                            "The actual Master wire table shows every CLIP connection that must be fitted again");
                        Assert(!vm.MasterApproved && !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                            "Remembered CLIP branches cannot approve the Master or pulse a relay on reinstallation");
                    }

                    if (!badSample)
                    {
                        Set("_waterProofProfile", new WaterProofModelSettings { Enabled = true,
                            Channel1Enabled = true, Channel1Connector = "LEAK",
                            Channel2Enabled = false, Channel3Enabled = false, PressMin = 35, LeakLimit = 2 });
                        foreach (bool lateError in new[] { false, true })
                        {
                            int request = leakReplies.Count;
                            Feed((1, [18]), (201, [202, 203]), (40, [41]));
                            Feed((1, [18]), (201, [202, 203]), (40, [41]));
                            Assert(leakReplies.Count == request + 1 && (int)Get("_waterProofRunning")! == 1,
                                "Incomplete sample starts an early Leak while CLIP branches are qualified");
                            Feed();
                            Feed();
                            Assert(vm.MasterState == waiting && engine.PassedNetCount == 0 &&
                                   (int)Get("_waterProofRunning")! == 1,
                                "Full removal resets the sample immediately while the old COM owner finishes safely");
                            Feed((1, [18]));
                            Feed((1, [18]));
                            AssertClipMissing("A new installation cannot reuse CLIP while the old Leak reply is pending");
                            if (lateError) leakReplies[request].SetException(new TimeoutException("Old removed sample"));
                            else leakReplies[request].SetResult(new WaterProofRunResult(
                                [new WaterProofChannelMeasurement(1, true, 80, 79, 1, true)], true, "SELFTEST"));
                            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                            while ((int)Get("_waterProofRunning")! != 0) await Task.Delay(5, deadline.Token);
                            await Task.Delay(20);
                            Assert((int)Get("_masterWaterProofPassedChannelsMask")! == 0 &&
                                   ((IReadOnlyList<WaterProofChannelMeasurement>)Get("_lastWaterProofMeasurements")!).Count == 0 &&
                                   !vm.IsDeviceFault && !vm.IsPersistenceFault,
                                "Late PASS/error from the removed sample cannot alter the new sample or publish old measurements");
                            AssertClipMissing("Fresh CLIP remains required after the old Leak run finishes");
                        }
                        Set("_waterProofProfile", new WaterProofModelSettings { Enabled = false });
                    }

                    // The next Master type, and production after the last sample,
                    // must not inherit qualification from a validated sample either.
                    Feed((1, [18]), (201, [202, 203, 204]));
                    Feed((1, [18]), (201, [202, 203, 204]));
                    Assert(engine.PassedNetCount == 4, "The fixture has real CLIP qualification before the validated-sample removal path");
                    Set("_masterGoodVerified", true);
                    Set("_masterValidationProductionDay", MasterSampleCatalog.ProductionDay(DateTime.Now));
                    if (badSample)
                    {
                        Set("_masterBadVerified", true);
                        store.Add(new TestHistoryRecord { Started = DateTime.Now, Finished = DateTime.Now,
                            PartNumber = model.PartNumber, InspectionType = HistoryInspectionType.MasterBad,
                            CycleId = (string)Get("_masterHistoryCycleId")!, Passed = true, Result = "CONFIRMED" });
                        Set("_masterRecordedHistoryStore", store); // Simulate the committed validation before eject/removal.
                        ((HashSet<MasterFaultKey>)Get("_masterDetectedFaultKeys")!).Add(new MasterFaultKey(
                            ProductFaultType.WrongWiring, 1, 18, 1, 3));
                    }
                    typeof(TestViewModel).GetProperty(nameof(TestViewModel.MasterState))!.SetValue(vm,
                        badSample ? MasterSequenceState.EjectingBadMaster : MasterSequenceState.EjectingGoodMaster);
                    Feed();
                    Feed();
                    Assert(engine.PassedNetCount == 0 && (badSample
                            ? vm.MasterApproved && vm.MasterState == MasterSequenceState.Completed
                            : !vm.MasterApproved && vm.MasterState == MasterSequenceState.WaitingBadMaster),
                        $"Changing to the next NG sample or production after full removal clears all previous CLIP qualification: bad={badSample}, passed={engine.PassedNetCount}, state={vm.MasterState}, approved={vm.MasterApproved}, clear={Get("_masterRemovalClearFrames")}, confirmed={Get("_masterRemovalConfirmed")}, keys={vm.MasterDetectedFaultCount}, good={Get("_masterGoodVerified")}, badVerified={Get("_masterBadVerified")}, status={vm.ResultStatusText}");
                    Feed((1, [18]));
                    Feed((1, [18]));
                    AssertClipMissing("The next sample/production requires fresh physical AO/a1/a2/a3 fitting");
                    Assert(vm.Total == total && vm.Pass == pass && vm.Fail == fail &&
                           !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                        "Sample reset/presentation never changes production counters or directly actuates relays");
                    await (Task)Get("_masterPersistenceTask")!;
                }
            }
            finally
            {
                AppSoundService.Current.StopAll(force: true);
                SqliteConnection.ClearAllPools();
                if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-MasterClip-"), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(root, recursive: true);
            }
        });
    }
}
