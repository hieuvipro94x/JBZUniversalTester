using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLeakFailureSummary()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
        var model = TopologyModel(
            new Terminal(40, "LEAK-1", "1", "1", "RET1"),
            new Terminal(41, "ANCHOR", "1", "3", "RET1"),
            new Terminal(42, "LEAK-2", "1", "1", "RET2"),
            new Terminal(43, "ANCHOR", "2", "3", "RET2"),
            new Terminal(44, "NORMAL", "1", "1", "WIRE"),
            new Terminal(45, "ANCHOR", "3", "3", "WIRE"));
        vm.SetModel(model);
        void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
        object? Call(string name, params object?[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
        int Get(string name) => (int)typeof(TestViewModel).GetField(name, flags)!.GetValue(vm)!;
        int Phase(string value) => (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, value);
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetFrameProcessingEnabled(true);
        var profile = new WaterProofModelSettings
        {
            Enabled = true, Channel1Enabled = true, Channel1Connector = "LEAK-1",
            Channel2Enabled = true, Channel2Connector = "LEAK-2", LeakLimit = 2
        };
        Set("_waterProofProfile", profile);
        Set("_waterProofCurrentRunProfile", profile.Clone());
        Set("_cycleActive", true);
        Set("_productionPhase", Phase("WaterProof"));
        var run = new WaterProofRunResult([
            new WaterProofChannelMeasurement(1, true, 80, 79, 1, true),
            new WaterProofChannelMeasurement(2, true, 80, 70, 10, false)], false, "FAIL");
        ((Task)Call("ApplyWaterProofFinalResultAsync", run, true)!).GetAwaiter().GetResult();
        Call("RecordPassedWaterProofChannels", run);
        // Match the real failed-channel profile used by the production retry path.
        var failedProfile = (WaterProofModelSettings)Call("WaterProofProfileForChannels",
            new Func<int, bool>(channel => run.Channels.Any(item => item.Channel == channel && !item.Passed)))!;
        Set("_waterProofCurrentRunProfile", failedProfile);
        Set("_runtimeMode", 1);
        Call("ArmWaterProofRetestConnectorCycle");
        engine.ProcessFrame(FrameSeq(100, (40, new[] { 41 }), (42, new[] { 43 })), false);
        Call("UpdatePendingWaterProofFailurePresentation");
        Assert(vm.SelectedOperationTabIndex == 0, "Leak FAIL waits for full continuity before showing the end-of-test summary");
        engine.ProcessFrame(FrameSeq(101, (40, new[] { 41 }), (42, new[] { 43 }), (44, new[] { 45 })), false);
        long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
        Call("ProcessEngineChangedOnUi", generation, null);
        Assert(engine.ContinuityPassed && Get("_preContinuityWaterProofPassed") == 0 &&
               vm.SelectedOperationTabIndex == 2 && vm.FinalWaterProofRows.Count == 2 &&
               vm.FinalWaterProofRows[0].ResultText == "PASS" && vm.FinalWaterProofRows[1].ResultText == "NG LEAK" &&
               vm.FinalWaterProofRows[1].SecondResultPressure == 70 &&
               vm.ResultStatusText == "KIỂM TRA KÍN NƯỚC KHÔNG ĐẠT" &&
               vm.StateBackground == "#C62828",
            "Continuity PASS with a failed Leak channel shows measured NG LEAK and the exact water-tightness failure status");
        var firstRow = vm.FinalWaterProofRows[0];
        Call("ProcessEngineChangedOnUi", generation, null);
        Assert(ReferenceEquals(firstRow, vm.FinalWaterProofRows[0]) && !vm.IsCenterResultVisible,
            "Repeated scan frames retain the same result rows without flicker or a center overlay");
        engine.ProcessFrame(FrameSeq(102, (40, new[] { 41 }), (44, new[] { 45 })), false);
        Call("ProcessEngineChangedOnUi", generation, null);
        Assert(vm.SelectedOperationTabIndex == 0 && Get("_waterProofRetestConnectorState") == 2 &&
               failedProfile.IsChannelEnabled(2) && !failedProfile.IsChannelEnabled(1) &&
               Get("_waterProofPassedChannelsMask") == 1,
            "Removing only failed CH2 hides the summary and arms CH2 reconnect while CH1 stays passed");
        Set("_runtimeMode", 0);
        engine.ProcessFrame(FrameSeq(103, (40, new[] { 41 }), (42, new[] { 43 }), (44, new[] { 45 })), false);
        Assert(engine.TryGetLeakConnectorTriggerState("LEAK-2", out bool reconnected, out _) && reconnected,
            "Refitting only CH2 supplies the retry trigger without removing the rest of the product");
        var retryPass = new WaterProofRunResult([new WaterProofChannelMeasurement(2, true, 81, 80, 1, true)], true, "PASS");
        ((Task)Call("ApplyWaterProofFinalResultAsync", retryPass, true)!).GetAwaiter().GetResult();
        Call("RecordPassedWaterProofChannels", retryPass);
        Set("_productionPhase", Phase("Continuity"));
        Set("_runtimeMode", 1);
        Call("UpdatePendingWaterProofFailurePresentation");
        Assert(Get("_preContinuityWaterProofPassed") == 1 && Get("_waterProofPassedChannelsMask") == 3 &&
               vm.SelectedOperationTabIndex == 0 && !board.Commands.Any(c => c.StartsWith("SET:", StringComparison.Ordinal)),
            "Successful retry clears NG display and opens the all-channel Leak gate without counting or relays");
    }
}
