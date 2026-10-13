using System.Text.Json;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestModelLeakLayout()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var directory = Path.Combine(Path.GetTempPath(), "JBZ_LEAK_LAYOUT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "layout.tht");
        var sidecar = path + ".leak.json";
        try
        {
            File.WriteAllBytes(path, BuildMinimalThtFile(
                "파트번호\t파트명\nLEAK\tLEAK TEST\n\n번 호\t커넥터\t핀 수\n1\tA\t2\n2\tB\t2\n\n" +
                "커넥터\t선이름\tI/O\t핀번호\nA\tW1\t1\t1\nA\tW2\t2\t2\nB\tW1\t3\t1\nB\tW2\t4\t2\n\n" +
                "선이름\t선연결\t굵기\t색깔\nW1\t\t0.5\tB\nW2\t\t0.5\tR"));
            var settings = new ProductionSettings();
            ProductionConfigService.SetWaterProofProfileForPath(settings, path,
                new WaterProofModelSettings { Enabled = true, Channel1Enabled = true, Channel2Enabled = false,
                    Channel3Enabled = true, PressMin = 42, LeakLimit = 7, PressTimeMs = 2300, WaitTimeMs = 600 });
            void Write(params ModelLeakLayoutService.Channel[] channels) => File.WriteAllText(sidecar,
                JsonSerializer.Serialize(new ModelLeakLayoutService.Layout(1, channels.ToList(),
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))));
            Write(new(3, "A"), new(1, "B"));
            var result = ProductionConfigService.GetWaterProofProfileForPath(settings, path);
            Assert(result.Enabled && result.Channel3Connector == "A" && result.Channel1Connector == "B" &&
                !result.Channel2Enabled && result.PressMin == 42 && result.LeakLimit == 7 &&
                result.PressTimeMs == 2300 && result.WaitTimeMs == 600,
                "ModelCreator assignments load automatically without changing pressure/timing");
            var fresh = ProductionConfigService.GetWaterProofProfileForPath(new ProductionSettings(), path);
            Assert(fresh.Enabled && fresh.Channel1Enabled && fresh.Channel3Enabled && !fresh.Channel2Enabled,
                "A new product still imports the ModelCreator Leak defaults");
            result.Enabled = false;
            result.Channel3Enabled = false;
            ProductionConfigService.SetWaterProofProfileForPath(settings, path, result);
            var disabled = ProductionConfigService.GetWaterProofProfileForPath(settings, path);
            Assert(!disabled.Enabled && !disabled.Channel3Enabled && disabled.Channel1Enabled &&
                disabled.Channel3Connector == "A" && disabled.Channel1Connector == "B",
                "Saved automatic/channel OFF settings survive a sidecar reload without losing connector assignments");
            disabled.Enabled = true;
            disabled.Channel3Enabled = true;
            ProductionConfigService.SetWaterProofProfileForPath(settings, path, disabled);
            var enabled = ProductionConfigService.GetWaterProofProfileForPath(settings, path);
            Assert(enabled.Enabled && enabled.Channel3Enabled, "The operator can re-enable automatic Leak and channels");
            enabled.Enabled = false;
            ProductionConfigService.SetWaterProofProfileForPath(settings, path, enabled);
            string cfgPath = Path.Combine(directory, "settings.cfg");
            ProductionConfigService.SaveLegacyCfg(settings, cfgPath);
            var load = typeof(ProductionConfigService).GetMethod("LoadEnglishCfg",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var reloaded = (ProductionSettings)load.Invoke(null, [cfgPath])!;
            Assert(!ProductionConfigService.GetWaterProofProfileForPath(reloaded, path).Enabled,
                "Saved OFF survives writing/reloading the cfg with an existing sidecar");
            File.Delete(cfgPath);
            Write();
            Assert(!ProductionConfigService.GetWaterProofProfileForPath(new ProductionSettings(), path).Enabled,
                "Empty explicit layout disables leak");
            File.WriteAllText(sidecar, JsonSerializer.Serialize(new ModelLeakLayoutService.Layout(1,
                [new(1, "A")], "WRONG-HASH")));
            bool mismatchRejected = false;
            try { ProductionConfigService.GetWaterProofProfileForPath(settings, path); }
            catch (InvalidDataException) { mismatchRejected = true; }
            Assert(mismatchRejected, "Mismatched model/config pair is rejected before applying channels");
            foreach (var channels in new[]
            {
                new[] { new ModelLeakLayoutService.Channel(1, "A"), new ModelLeakLayoutService.Channel(1, "B") },
                new[] { new ModelLeakLayoutService.Channel(4, "A") },
                new[] { new ModelLeakLayoutService.Channel(1, "MISSING") }
            })
            {
                Write(channels);
                bool rejected = false;
                try { ProductionConfigService.GetWaterProofProfileForPath(settings, path); }
                catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "Invalid channel/connector assignments are rejected");
            }
            File.Delete(sidecar);
            Assert(!ProductionConfigService.GetWaterProofProfileForPath(settings, path).Enabled,
                "Legacy models retain their saved profile when no sidecar exists");
        }
        finally
        {
            if (File.Exists(sidecar)) File.Delete(sidecar);
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static void TestModelCreatorLeakFile(string path)
    {
        var result = ProductionConfigService.GetWaterProofProfileForPath(new ProductionSettings(), path);
        Assert(result.Enabled && result.Channel1Connector == "B" && result.Channel3Connector == "A" &&
            result.Channel1Enabled && !result.Channel2Enabled && result.Channel3Enabled,
            "Real ModelCreator export loads CH1=B and CH3=A in tester");
        Console.WriteLine("PASS: ModelCreator THT + leak layout → tester profile");
    }
}
