using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestMissingConnectionConfirmationTimeout()
    {
        FaultDetail Pair(int source, int target) => new() { Type = ProductFaultType.OpenCircuit,
            ExpectedSourceIo = source, ExpectedTargetIo = target, RelatedIos = [source, target] };
        var a = Pair(1, 2); var b = Pair(3, 4);
        foreach (int seconds in new[] { 5, 10 })
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            var gate = new MissingConnectionConfirmationGate(clock);
            Assert(gate.Observe([a], true, seconds, true).Count == 0, "Missing pair does not warn immediately");
            clock.Advance(TimeSpan.FromSeconds(seconds - 1));
            Assert(gate.Observe([a, b], true, seconds, true).Count == 0, "Cannot confirm early");
            clock.Advance(TimeSpan.FromSeconds(1));
            var confirmed = gate.Observe([a, b], true, seconds, true);
            Assert(confirmed.Count == 1 && confirmed[0].ExpectedSourceIo == 1,
                "Each missing pair has its own timeout, including identical scan snapshots");
            gate.Observe([b], true, seconds, true);
            Assert(gate.Observe([a, b], true, seconds, true).All(item => item.ExpectedSourceIo != 1),
                "Reconnecting a pair resets its missing timer");
            gate.Observe([a, b], true, seconds, false);
            clock.Advance(TimeSpan.FromSeconds(seconds + 1));
            Assert(gate.Observe([a, b], true, seconds, true).Count == 0,
                "Removal, Master/Probe/Leak and stale frames reset eligibility and cannot carry over a timeout");
            gate.Observe([a], false, seconds, true);
            clock.Advance(TimeSpan.FromSeconds(seconds));
            Assert(gate.Observe([a], false, seconds, true).Count == 0,
                "Disabled feature retains old missing-wire behavior");
            Assert(gate.Observe([a], true, seconds, true).Count == 0, "Enabling starts a fresh timer");
        }
        using (var engine = CreateEngine(out var board, new ProductionSettings()))
        {
            engine.SetModel(Model(("CONNECTED", new[] { 1, 2 }), ("MISSING", new[] { 3, 4 })));
            engine.ProcessFrame(FrameSeq(1, (1, new[] { 2 }), (3, Array.Empty<int>())));
            var missing = engine.GetMissingConnectionFaults();
            Assert(missing.Count == 1 && missing[0].ExpectedSourceIo == 3 && missing[0].ExpectedTargetIo == 4 &&
                   missing[0].ActualSourceIo is null && missing[0].ActualTargetIo is null,
                "Warnings use the exact expected missing pair without inventing an actual electrical edge");
            Assert(!engine.HasWiringFault && engine.BuildConfirmedOpenFaults().Count == 0,
                "The optional UI timeout does not enable automatic legacy engine OPEN faults");
            engine.ProcessFrame(FrameSeq(2));
            Assert(engine.GetMissingConnectionFaults().Count == 0,
                "An empty table cannot produce an open-pair warning");
        }
        string root = Path.Combine(Path.GetTempPath(), "JBZ-OpenConfirmation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "settings.cfg");
            ProductionConfigService.SaveLegacyCfg(new ProductionSettings {
                MissingConnectionConfirmationEnabled = true, MissingConnectionConfirmationSeconds = 10 }, path);
            var load = typeof(ProductionConfigService).GetMethod("LoadEnglishCfg", BindingFlags.Static | BindingFlags.NonPublic)!;
            var settings = (ProductionSettings)load.Invoke(null, [path])!;
            Assert(settings.MissingConnectionConfirmationEnabled && settings.MissingConnectionConfirmationSeconds == 10,
                "The new enabled flag and seconds survive configuration roundtrip");
            File.WriteAllText(path, "[LotNo]2000");
            settings = (ProductionSettings)load.Invoke(null, [path])!;
            Assert(!settings.MissingConnectionConfirmationEnabled && settings.MissingConnectionConfirmationSeconds == 5,
                "Legacy configuration stays disabled by default with a five-second setting");
        }
        finally { Directory.Delete(root, true); }
    }
}
