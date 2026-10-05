using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLeakWithoutRetTrigger()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ProductModel model = TopologyModel(
            new Terminal(40, "JIG-1", "1", "4", "RET1"),
            new Terminal(41, "JIG-3", "1", "4", "RET1"),
            new Terminal(42, "JIG-1", "2", "4", "WIRE1"),
            new Terminal(43, "JIG-2", "1", "4", "WIRE1"),
            new Terminal(44, "JIG-3", "2", "4", "WIRE1"),
            new Terminal(45, "JIG-1", "3", "4", "WIRE2"),
            new Terminal(46, "JIG-2", "2", "4", "WIRE2"),
            new Terminal(47, "ISOLATED", "1", "2", "LOCAL"),
            new Terminal(48, "ISOLATED", "2", "2", "LOCAL"),
            new Terminal(49, "JIG-2", "3", "4", "WIRE3"),
            new Terminal(50, "NO-RET-PEER", "1", "1", "WIRE3"));
        using TestEngine engine = CreateEngine(out _);
        engine.SetModel(model);
        Assert(engine.TryGetLeakConnectorTriggerState("JIG-2", out bool connected, out bool removed) &&
               !connected && removed, "Fallback topology exists even before fitting");
        engine.ProcessFrame(FrameSeq(1, (42, new[] { 43 })), false);
        Assert(engine.TryGetLeakConnectorTriggerState("JIG-2", out connected, out removed) &&
               connected && !removed && !engine.ContinuityPassed,
            "One expected edge starts the no-RET jig without needing a complete three-endpoint net");
        Assert(engine.TryGetLeakConnectorTriggerState("JIG-1", out connected, out _) && !connected,
            "A connector with RET retains its RET gate despite connected normal wires");
        Assert(!engine.TryGetLeakConnectorTriggerState("ISOLATED", out _, out _) &&
               !engine.TryGetLeakConnectorTriggerState("UNKNOWN", out _, out _),
            "Same-connector topology and unknown connectors cannot trigger");
        engine.SuppressProbeRelatedWiringFaults([42, 43]);
        Assert(engine.TryGetLeakConnectorTriggerState("JIG-2", out connected, out removed) && !connected && !removed,
            "Probe suppression cannot trigger Leak or falsely confirm removal");
        engine.ClearProbeEvidenceExclusions();
        engine.ProcessFrame(FrameSeq(2, (43, new[] { 44 })), false);
        Assert(engine.TryGetLeakConnectorTriggerState("JIG-2", out connected, out removed) &&
               connected && !removed, "A connector outside the enabled Leak channels can be a peer");
        engine.ProcessFrame(FrameSeq(3, (43, new[] { 45 })), false);
        Assert(engine.TryGetLeakConnectorTriggerState("JIG-2", out connected, out _) && !connected,
            "Wrong-wire activity across the two jigs is rejected");
        engine.ProcessFrame(FrameSeq(4, (42, new[] { 43 }), (45, new[] { 46 })), false);
        engine.ProcessFrame(FrameSeq(5, (45, new[] { 46 })), false);
        Assert(engine.TryGetLeakConnectorTriggerState("JIG-2", out connected, out removed) &&
               connected && !removed, "Losing only one fallback edge does not confirm removal");
        engine.ProcessFrame(FrameSeq(6), false);
        Assert(engine.TryGetLeakConnectorTriggerState("JIG-2", out connected, out removed) &&
               !connected && removed, "All fallback edges absent permits connector retest removal");
        engine.ProcessFrame(FrameSeq(7, (49, new[] { 50 })), false);
        Assert(engine.TryGetLeakConnectorTriggerState("JIG-2", out connected, out removed) &&
               connected && !removed, "A peer with no RET and no enabled Leak channel qualifies");

        TestViewModel vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        vm.SetModel(model);
        typeof(TestViewModel).GetField("_waterProofProfile", flags)!.SetValue(vm, new WaterProofModelSettings
        {
            Enabled = true, Channel1Enabled = true, Channel1Connector = "JIG-1",
            Channel2Enabled = true, Channel2Connector = "JIG-2"
        });
        var vmEngine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        vmEngine.SetFrameProcessingEnabled(true);
        vmEngine.ProcessFrame(FrameSeq(8, (49, new[] { 50 })), false);
        Assert((int)typeof(TestViewModel).GetMethod("SelectReadyWaterProofChannel", flags)!.Invoke(vm, null)! == 2,
            "Runtime selects CH2 through a no-RET peer while CH1 waits for RET");
        MethodInfo gate = typeof(TestViewModel).GetMethod("TryValidateWaterProofConnectorGate", flags)!;
        object?[] args = [model, null, new WaterProofModelSettings
        {
            Enabled = true, Channel1Enabled = false, Channel2Enabled = true, Channel2Connector = "JIG-2"
        }];
        Assert((bool)gate.Invoke(vm, args)!, "Single-channel gate accepts an expected edge to a non-Leak connector");
        typeof(TestViewModel).GetField("_waterProofPassedChannelsMask", flags)!.SetValue(vm, 2);
        Assert((int)typeof(TestViewModel).GetMethod("SelectReadyWaterProofChannel", flags)!.Invoke(vm, null)! == 0,
            "Passed CH2 is not repeated just because its edge remains connected");
    }
}
