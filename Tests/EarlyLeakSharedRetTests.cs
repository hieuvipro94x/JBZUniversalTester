using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestEarlyLeakSharedRet()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var model = TopologyModel(
            new Terminal(40, "LEAK-1", "1", "2", "RET1"),
            new Terminal(41, "ANCHOR", "1", "2", "RET1"),
            new Terminal(42, "LEAK-2", "1", "1", "RET1"),
            new Terminal(43, "LEAK-1", "2", "2", "WIRE1"),
            new Terminal(44, "ANCHOR", "2", "2", "WIRE1"));
        var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
        vm.SetModel(model);
        var profile = new WaterProofModelSettings
        {
            Enabled = true, Channel1Enabled = true, Channel1Connector = "LEAK-1",
            Channel2Enabled = true, Channel2Connector = "LEAK-2"
        };
        typeof(TestViewModel).GetField("_waterProofProfile", flags)!.SetValue(vm, profile);
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetFrameProcessingEnabled(true);
        int Ready() => (int)typeof(TestViewModel).GetMethod("SelectReadyWaterProofChannel", flags)!.Invoke(vm, null)!;
        engine.ProcessFrame(FrameSeq(100, (43, new[] { 44 })), false);
        Assert(Ready() == 0, "Normal wiring cannot replace the configured RET trigger");
        engine.ProcessFrame(FrameSeq(101, (40, new[] { 41 })), false);
        Assert(Ready() == 1 && !engine.ContinuityPassed && !engine.HasConnectedRetWire("LEAK-1"),
            "CH1 is ready on its own expected RET edge while shared RET and other wires remain incomplete");
        Assert(engine.TryGetLeakConnectorTriggerState("LEAK-2", out bool connected, out _) && !connected,
            "A RET edge between other connectors cannot trigger the unfitted connector");
        object?[] gateArgs = [model, null, new WaterProofModelSettings
        {
            Enabled = true, Channel1Enabled = true, Channel1Connector = "LEAK-1", Channel2Enabled = false
        }];
        Assert((bool)typeof(TestViewModel).GetMethod("TryValidateWaterProofConnectorGate", flags)!.Invoke(vm, gateArgs)!,
            "The actual pre-run validation accepts the early channel before full continuity");
        engine.SuppressProbeRelatedWiringFaults([40, 41]);
        Assert(engine.TryGetLeakConnectorTriggerState("LEAK-1", out connected, out bool removed) && !connected && !removed,
            "Probe cannot start Leak or falsely confirm RET removal");
        engine.ClearProbeEvidenceExclusions();
        engine.ProcessFrame(FrameSeq(102, (40, new[] { 41, 42 })), false);
        typeof(TestViewModel).GetField("_waterProofPassedChannelsMask", flags)!.SetValue(vm, 1);
        Assert(Ready() == 2 && !engine.ContinuityPassed,
            "The next fitted channel starts independently and a passed channel is not repeated");
        engine.ProcessFrame(FrameSeq(103, (40, new[] { 41 })), false);
        Assert(engine.TryGetLeakConnectorTriggerState("LEAK-1", out connected, out removed) && connected && !removed,
            "Unplugging another RET endpoint cannot re-arm the still fitted connector");
        engine.ProcessFrame(FrameSeq(104), false);
        Assert(engine.TryGetLeakConnectorTriggerState("LEAK-1", out connected, out removed) && !connected && removed,
            "Removing all local RET edges permits the connector retry cycle");
        Assert(!board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Early channel eligibility never pulses relays or bypasses final PASS");
    }
}
