using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSealLeakTrigger()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (string seal in new[] { "SEAL1", "seal1", "SeAl01", "SEAL2", "SEAL02'" })
        {
            var model = TopologyModel(new Terminal(10, "LEAK-1", "1", "2", seal),
                new Terminal(11, "ANCHOR", "1", "2", seal), new Terminal(12, "LEAK-2", "1", "1", seal),
                new Terminal(13, "LEAK-1", "2", "2", "WIRE1"), new Terminal(14, "ANCHOR", "2", "2", "WIRE1"));
            var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
            typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
            var profile = new WaterProofModelSettings { Enabled = true, Channel1Enabled = true,
                Channel1Connector = "LEAK-1", Channel2Enabled = true, Channel2Connector = "LEAK-2" };
            typeof(TestViewModel).GetField("_waterProofProfile", flags)!.SetValue(vm, profile);
            var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
            engine.SetModel(model); engine.SetFrameProcessingEnabled(true);
            int Ready() => (int)typeof(TestViewModel).GetMethod("SelectReadyWaterProofChannel", flags)!.Invoke(vm, null)!;
            engine.ProcessFrame(FrameSeq(100, (13, new[] { 14 })), false);
            Assert(Ready() == 0, $"{seal}: normal wires cannot bypass a declared SEAL trigger");
            engine.ProcessFrame(FrameSeq(101, (10, new[] { 11 })), false);
            Assert(Ready() == 1 && !engine.ContinuityPassed && !engine.HasConnectedRetWire("LEAK-1"),
                $"{seal}: a local declared SEAL edge starts the fitted channel before the entire shared net/harness");
            Assert(engine.TryGetLeakConnectorTriggerState("LEAK-2", out bool connected, out _) && !connected,
                "SEAL activity elsewhere cannot trigger an unfitted connector");
            object?[] gate = [model, null, new WaterProofModelSettings { Enabled = true,
                Channel1Enabled = true, Channel1Connector = "LEAK-1", Channel2Enabled = false, Channel3Enabled = false }];
            Assert((bool)typeof(TestViewModel).GetMethod("TryValidateWaterProofConnectorGate", flags)!.Invoke(vm, gate)!,
                "Pre-run validation accepts the same early SEAL gate as channel selection");
            engine.SuppressProbeRelatedWiringFaults([10, 11]);
            Assert(engine.TryGetLeakConnectorTriggerState("LEAK-1", out connected, out bool removed) && !connected && !removed,
                "Probe cannot trigger SEAL Leak or fabricate a removal event");
            engine.ClearProbeEvidenceExclusions();
            engine.ProcessFrame(FrameSeq(102, (10, new[] { 14 })), false);
            Assert(Ready() == 0, "Wrong SEAL wiring cannot activate Leak");
            engine.ProcessFrame(FrameSeq(103, (10, new[] { 11, 12 })), false);
            Assert(engine.HasConnectedRetWire("LEAK-1") && !engine.IsRetWireDisconnected("LEAK-1"),
                "Complete SEAL topology is recognized by the shared trigger helpers");
            typeof(TestViewModel).GetField("_waterProofPassedChannelsMask", flags)!.SetValue(vm, 1);
            Assert(Ready() == 2, "A passed SEAL channel is skipped while another fitted channel remains eligible");
            engine.ProcessFrame(FrameSeq(104, (10, new[] { 11 })), false);
            Assert(engine.TryGetLeakConnectorTriggerState("LEAK-1", out connected, out removed) && connected && !removed,
                "Removing another shared SEAL endpoint cannot re-arm the still fitted connector");
            engine.ProcessFrame(FrameSeq(105, (13, new[] { 14 })), false);
            Assert(engine.TryGetLeakConnectorTriggerState("LEAK-1", out connected, out removed) && !connected && removed,
                "Only removal of local SEAL edges re-arms that connector, even while normal wires remain connected");
            Assert(!board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                "SEAL trigger checks cannot pulse relays or complete the production cycle");
        }
        var mixed = TopologyModel(new Terminal(20, "LEAK", "1", "2", "SEAL1"),
            new Terminal(21, "ANCHOR", "1", "2", "SEAL1"), new Terminal(30, "LEAK", "2", "2", "RET1"),
            new Terminal(31, "ANCHOR", "2", "2", "RET1"));
        using TestEngine mixedEngine = CreateEngine(out _);
        mixedEngine.SetModel(mixed);
        foreach (var pair in new[] { (20, new[] { 21 }), (30, new[] { 31 }) })
        {
            mixedEngine.ProcessFrame(FrameSeq(pair.Item1 + 200, pair), false);
            Assert(mixedEngine.TryGetLeakConnectorTriggerState("LEAK", out bool fitted, out _) && fitted,
                "A connector with both RET and SEAL accepts either declared trigger, without requiring both");
        }
    }
}
