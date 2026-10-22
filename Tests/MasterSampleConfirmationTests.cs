using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestMasterShortGroupAndManualOpen()
    {
        using (TestEngine engine = CreateEngine(out _))
        {
            var model = Model(("A", new[] { 1, 18 }), ("B", new[] { 2, 19 }), ("C", new[] { 3, 20 }));
            long sequence = 100;
            void Fit(params (int Source, int[] Targets)[] pairs)
            {
                engine.SetModel(model);
                engine.ProcessFrame(FrameSeq(sequence++, pairs), false);
                Thread.Sleep(Math.Max(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs,
                    ProductionTimingPolicy.DefaultShortCircuitConfirmMs) + 20);
                engine.ProcessFrame(FrameSeq(sequence++, pairs), false);
            }
            Fit((1, [2]));
            Assert(engine.GetMasterWiringFaults().Count > 0 &&
                   engine.GetMasterWiringFaults().All(fault => fault.FaultType == ProductFaultType.WrongWiring),
                "Two incorrectly connected IOs are wrong-wire Master evidence, not short evidence");
            Fit((1, [2, 3]));
            Assert(engine.GetMasterWiringFaults().Count > 0 &&
                   engine.GetMasterWiringFaults().All(fault => fault.FaultType == ProductFaultType.ShortCircuit),
                "Three connected IOs qualify as short evidence even when original pairs are missing");
            Fit((1, [2]), (2, [3]));
            Assert(engine.GetMasterWiringFaults().Count > 0 &&
                   engine.GetMasterWiringFaults().All(fault => fault.FaultType == ProductFaultType.ShortCircuit),
                "Short groups use transitive physical connectivity, not the number of targets in one source row");
            Fit((1, [2]), (3, [19]));
            Assert(engine.GetMasterWiringFaults().Count > 0 &&
                   engine.GetMasterWiringFaults().All(fault => fault.FaultType == ProductFaultType.WrongWiring),
                "Separate two-IO faults never combine into a three-IO short group");
            Fit((1, [60, 61]));
            Assert(engine.GetMasterWiringFaults().Any(fault => fault.FaultType == ProductFaultType.ShortCircuit),
                "Unexpected IOs outside the item topology still count in a physical short group");
            model = Model(("COMMON", new[] { 1, 2, 3, 4 }));
            Fit((1, [2, 3, 4]));
            Assert(engine.GetMasterWiringFaults().Count == 0,
                "A valid common net in the item topology cannot confirm a short Master");
        }

        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-MasterConfirm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (int missingCount in new[] { 1, 2, 3 })
                {
                    var settings = new ProductionSettings { MasterFaultRequiredCount = 1,
                        MasterOpenFaultRequiredCount = 99, MasterSelectedFaultSamples = MasterSampleSelection.OpenCircuit,
                        IoConfirm1 = 1, IoConfirmN = 1, ProductSettleTimeMs = 0,
                        ShortCircuitConfirmMs = 0, WrongConnectionConfirmMs = 0, JigContactUnstableWindowMs = 0 };
                    var vm = CreateTestViewModel(settings, out FakeBoard board);
                    var model = Model(("A", new[] { 1, 18 }), ("B", new[] { 2, 19 }),
                        ("C", new[] { 3, 20 }), ("D", new[] { 4, 21 }));
                    model.PartNumber = "OPEN-CONFIRM-" + missingCount;
                    vm.SetModel(model);
                    await (Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!;
                    void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                    object? Get(string name) => typeof(TestViewModel).GetField(name, flags)!.GetValue(vm);
                    object? Call(string name, params object?[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
                    var store = new TestHistoryStore(Path.Combine(root, $"{missingCount}.db"));
                    await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
                    await persistence.Initialization;
                    Set("_historyStore", store);
                    Set("_productionPersistence", persistence);
                    Set("_masterGoodVerified", true);
                    Call("TransitionToBadMaster");
                    var engine = (TestEngine)Get("_engine")!;
                    engine.SetFrameProcessingEnabled(true);
                    long generation = (long)Get("_runtimeGeneration")!;
                    long sequence = 100;
                    void Feed(params (int Source, int[] Targets)[] pairs)
                    {
                        Set("_runtimeMode", 0);
                        var frame = FrameSeq(sequence++, pairs);
                        board.Publish(frame);
                        engine.ProcessFrame(frame, false);
                        Set("_runtimeMode", 1);
                        Call("ProcessMasterEngineChangedOnUi", generation);
                    }
                    int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
                    Feed((1, [18]), (2, []), (3, []), (4, []));
                    await Task.Delay(150);
                    Set("_masterBadCollectNotBeforeUtcTicks", 0L);
                    Feed((1, [18]), (2, []), (3, []), (4, []));
                    Assert(vm.MasterState == MasterSequenceState.TestingBadMaster && !vm.MasterApproved &&
                           vm.MasterDetectedFaultCount == 3 && vm.IsOpenMasterAwaitingConfirmation &&
                           !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                        "A partially fitted open sample remains unconfirmed regardless of its missing count until the operator clicks");
                    Feed((1, [18]), (2, missingCount < 3 ? [19] : []),
                        (3, missingCount < 2 ? [20] : []), (4, []));
                    Assert(vm.MasterDetectedFaultCount == missingCount && vm.CanConfirmOpenMaster,
                        "Confirmation accepts the live missing count without using the obsolete configured value 99");
                    Feed((1, [2]), (2, []), (3, []), (4, []));
                    Assert(!vm.CanConfirmOpenMaster, "Wrong wiring cannot enable open-Master confirmation");
                    await (Task)Call("ConfirmOpenMasterAsync")!;
                    Assert(vm.MasterState == MasterSequenceState.TestingBadMaster && !vm.MasterApproved,
                        "Direct invocation also rejects a changed live frame containing wrong wiring");
                    Feed((1, [18]), (2, [19]), (3, [20]), (4, [21]));
                    Assert(!vm.CanConfirmOpenMaster, "A fully connected good sample cannot qualify as an open sample");
                    Feed((1, [18]), (2, missingCount < 3 ? [19] : []),
                        (3, missingCount < 2 ? [20] : []), (4, []));
                    board.StartScanCallback = transport => transport.Publish(FrameSeq(sequence++, (1, [18])));
                    await (Task)Call("ConfirmOpenMasterAsync")!;
                    Assert(vm.MasterState == MasterSequenceState.EjectingBadMaster &&
                           vm.MasterRequiredFaultCount == missingCount &&
                           vm.ResultStatusText == "HOÀN THÀNH KIỂM TRA MẪU TUỘT TUÝT / ĐỨT DÂY" &&
                           vm.StateBackground == "#2AA84A" && !vm.IsOpenMasterAwaitingConfirmation &&
                           !vm.MasterApproved && !vm.IsPersistenceFault &&
                           board.Commands.Count(command => command == "SET:1") == 1 && !board.Commands.Contains("SET:2"),
                        $"An explicit confirmation commits the detected open count and ejects only JIG, retaining removal interlock: missing={missingCount}, state={vm.MasterState}, required={vm.MasterRequiredFaultCount}, status={vm.ResultStatusText}, approved={vm.MasterApproved}, persistence={vm.IsPersistenceFault}, device={vm.IsDeviceFault}, commands={string.Join(',', board.Commands)}");
                    await (Task)Get("_masterPersistenceTask")!;
                    Assert(vm.Total == total && vm.Pass == pass && vm.Fail == fail,
                        "Manual sample confirmation cannot count production");
                }
            }
            finally
            {
                AppSoundService.Current.StopAll(force: true);
                SqliteConnection.ClearAllPools();
                if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-MasterConfirm-"), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(root, true);
            }
        });
    }
}
