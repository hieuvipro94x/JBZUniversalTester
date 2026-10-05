using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLeakIndependentContinuityPresentation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ProductModel model = TopologyModel(
            new Terminal(40, "1", "1", "2", "RET1"),
            new Terminal(41, "3", "1", "3", "RET1"),
            new Terminal(42, "2", "1", "1", "RET2"),
            new Terminal(43, "3", "2", "3", "RET2"),
            new Terminal(44, "4", "1", "1", "NORMAL"),
            new Terminal(45, "3", "3", "3", "NORMAL"));
        TestViewModel vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
        LoadReadyModel(vm, model);
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetFrameProcessingEnabled(true);
        typeof(TestViewModel).GetField("_runtimeMode", flags)!.SetValue(vm, 1);
        typeof(TestViewModel).GetField("_cycleActive", flags)!.SetValue(vm, true);
        typeof(TestViewModel).GetField("_presentationCycleStarted", flags)!.SetValue(vm, true);
        engine.ProcessFrame(FrameSeq(100, (40, new[] { 41 })), false);
        MethodInfo refresh = typeof(TestViewModel).GetMethod("RefreshFaults", flags)!;
        refresh.Invoke(vm, null);
        FaultRow[] initialRows = vm.Faults.ToArray();
        Assert(initialRows.Length > 0, "A fitted connector produces missing-wire rows before Leak starts");
        int leakPhase = Convert.ToInt32(Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, "WaterProof"));
        typeof(TestViewModel).GetField("_productionPhase", flags)!.SetValue(vm, leakPhase);
        typeof(TestViewModel).GetField("_waterProofRunning", flags)!.SetValue(vm, 1);
        ((Task)typeof(TestViewModel).GetMethod("EnsureProductionScanForWaterProofAsync", flags)!
            .Invoke(vm, [CancellationToken.None])!).GetAwaiter().GetResult();
        int resets = 0;
        vm.Faults.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        };
        typeof(TestViewModel).GetMethod("ShowWaterProofOperationPanel", flags)!.Invoke(vm, null);
        refresh.Invoke(vm, null);
        Assert(vm.Faults.Count == initialRows.Length && vm.Faults.Zip(initialRows).All(pair => ReferenceEquals(pair.First, pair.Second)) && resets == 0,
            "Initial Leak entry preserves the same continuity rows without clearing and re-adding them");
        engine.ProcessFrame(FrameSeq(101, (40, new[] { 41 }), (42, new[] { 43 })), false);
        refresh.Invoke(vm, null);
        Assert(vm.Faults.Count > 0 && vm.Faults.Count < initialRows.Length && board.IsScanning && !board.Commands.Contains("STOP"),
            "Another connector updates continuity during Leak while D2XX remains running");

        typeof(TestViewModel).GetField("_waterProofRunning", flags)!.SetValue(vm, 0);
        var fullProfile = new WaterProofModelSettings
        {
            Enabled = true, Channel1Enabled = true, Channel1Connector = "1",
            Channel2Enabled = true, Channel2Connector = "2"
        };
        var failedProfile = new WaterProofModelSettings
        {
            Enabled = true, Channel1Enabled = true, Channel1Connector = "1", Channel2Enabled = false
        };
        typeof(TestViewModel).GetField("_waterProofProfile", flags)!.SetValue(vm, fullProfile);
        typeof(TestViewModel).GetField("_waterProofCurrentRunProfile", flags)!.SetValue(vm, failedProfile);
        typeof(TestViewModel).GetField("_lastWaterProofMeasurements", flags)!.SetValue(vm,
            new WaterProofChannelMeasurement[] { new(1, true, 84, 70, 14, false) });
        typeof(TestViewModel).GetMethod("ArmWaterProofRetestConnectorCycle", flags)!.Invoke(vm, null);
        FieldInfo retryState = typeof(TestViewModel).GetField("_waterProofRetestConnectorState", flags)!;
        Assert((int)retryState.GetValue(vm)! == 1, "CH1 failure arms connector removal");
        engine.ProcessFrame(FrameSeq(102, (42, new[] { 43 }), (44, new[] { 45 })), false);
        long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
        typeof(TestViewModel).GetMethod("ObserveWaterProofRetestConnectorCycle", flags)!.Invoke(vm, [generation]);
        Assert((int)retryState.GetValue(vm)! == 2 && engine.HasConnectedRetWire("2") && engine.HasProductActivity && !vm.IsProductRemovalPending,
            "Removing only connector 1 accepts the retry edge while the rest of the product remains fitted");
        engine.ProcessFrame(FrameSeq(103, (40, new[] { 41 }), (42, new[] { 43 }), (44, new[] { 45 })), false);
        Assert(engine.TryGetLeakConnectorTriggerState("1", out bool reconnected, out _) && reconnected &&
               failedProfile.IsChannelEnabled(1) && !failedProfile.IsChannelEnabled(2),
            "Refitting connector 1 reopens the retry gate for CH1 alone without repeating CH2 or removing the product");
    }
}
