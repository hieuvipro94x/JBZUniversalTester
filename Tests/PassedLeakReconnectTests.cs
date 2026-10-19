using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestPassedLeakReconnect()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
        var model = TopologyModel(
            new Terminal(40, "LEAK-1", "1", "2", "RET1"),
            new Terminal(41, "ANCHOR", "1", "3", "RET1"),
            new Terminal(42, "PEER", "1", "1", "RET1"),
            new Terminal(43, "LEAK-2", "1", "1", "RET2"),
            new Terminal(44, "ANCHOR", "2", "3", "RET2"),
            new Terminal(45, "NORMAL", "1", "1", "WIRE"),
            new Terminal(46, "ANCHOR", "3", "3", "WIRE"));
        vm.SetModel(model);
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
        int Get(string name) => (int)typeof(TestViewModel).GetField(name, flags)!.GetValue(vm)!;
        void Observe() => typeof(TestViewModel).GetMethod("ObservePassedWaterProofConnectorRemoval", flags)!.Invoke(vm, null);
        int Ready() => (int)typeof(TestViewModel).GetMethod("SelectReadyWaterProofChannel", flags)!.Invoke(vm, null)!;
        int Phase(string name) => (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, name);
        void Feed(ScanFrame frame)
        {
            Set("_runtimeMode", 0); // Feed only the engine; never open a physical Leak COM in self-tests.
            engine.ProcessFrame(frame, false);
            Set("_runtimeMode", 1);
            Observe();
        }
        Set("_waterProofProfile", new WaterProofModelSettings
        {
            Enabled = true, Channel1Enabled = true, Channel1Connector = "LEAK-1",
            Channel2Enabled = true, Channel2Connector = "LEAK-2"
        });
        Set("_cycleActive", true);
        Set("_productionPhase", Phase("Continuity"));
        engine.SetFrameProcessingEnabled(true);
        Set("_waterProofPassedChannelsMask", 3);
        Set("_preContinuityWaterProofPassed", 1);
        Feed(FrameSeq(100, (40, new[] { 41 }), (43, new[] { 44 }), (45, new[] { 46 })));
        Assert(Get("_waterProofPassedChannelsMask") == 3 && Ready() == 0,
            "A shared RET losing an unrelated endpoint does not invalidate its fitted Leak connector");
        Feed(FrameSeq(101, (43, new[] { 44 }), (45, new[] { 46 })) with { Complete = false });
        Assert(Get("_waterProofPassedChannelsMask") == 3, "An incomplete frame cannot invalidate Leak PASS");
        Set("_probeSessionActive", 1);
        Feed(FrameSeq(102, (43, new[] { 44 }), (45, new[] { 46 })));
        Assert(Get("_waterProofPassedChannelsMask") == 3, "Probe cannot invalidate production Leak PASS");
        Set("_probeSessionActive", 0);
        Observe();
        Assert(Get("_waterProofPassedChannelsMask") == 2 && Get("_preContinuityWaterProofPassed") == 0 && Ready() == 0,
            "Removing CH1 invalidates only CH1 and blocks final product PASS without starting an unplugged channel");
        Feed(FrameSeq(103, (40, new[] { 41 }), (43, new[] { 44 }), (45, new[] { 46 })));
        Assert(Ready() == 1 && Get("_waterProofPassedChannelsMask") == 2,
            "Reconnecting CH1 selects it for another Leak run and preserves CH2 PASS");
        typeof(TestViewModel).GetMethod("RecordPassedWaterProofChannels", flags)!.Invoke(vm,
            [new WaterProofRunResult([new WaterProofChannelMeasurement(1, true, 80, 78, 2, true)], true, "PASS")]);
        Assert(Get("_waterProofPassedChannelsMask") == 3 && Get("_preContinuityWaterProofPassed") == 1 && Ready() == 0,
            "A new CH1 PASS restores the full Leak gate without repeating other passed channels");
        Set("_productionPhase", Phase("WaterProof"));
        Set("_waterProofRunning", 1);
        Feed(FrameSeq(104, (40, new[] { 41 }), (45, new[] { 46 })));
        Assert(Get("_waterProofPassedChannelsMask") == 1 && Get("_preContinuityWaterProofPassed") == 0,
            "A previously passed channel is invalidated even while another Leak run owns COM");
        Set("_waterProofRunning", 0);
        Feed(FrameSeq(105, (40, new[] { 41 }), (43, new[] { 44 }), (45, new[] { 46 })));
        Assert(Ready() == 2, "CH2 reconnect selects CH2 alone for the next serialized run");
        Set("_waterProofPassedChannelsMask", 3);
        Set("_preContinuityWaterProofPassed", 1);
        Set("_waitForProductRelease", true);
        Set("_productionPhase", Phase("WaitingProductRemoval"));
        Feed(FrameSeq(106, (45, new[] { 46 })));
        Assert(Get("_waterProofPassedChannelsMask") == 3 && Get("_preContinuityWaterProofPassed") == 1 &&
               !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Removal after durable product PASS does not alter the committed result or fire relays");
    }
}
