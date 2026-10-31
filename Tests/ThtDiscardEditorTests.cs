using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestThtDiscardEditor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string root = Path.Combine(Path.GetTempPath(), "JBZ-DiscardEditor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string text = "검사\t파트번호\tＥＣＯ\tＡＬＣ\t파트명\n\tPART-01\tECO-1\tALC-1\tPRODUCT\n\n" +
                "번 호\t커넥터\t핀 수\n\tCN1\t2\n\tCN2\t2\n\n" +
                "커넥터\t선이름\tＩ／Ｏ\t핀번호\t핀종류\t핀차수\t핀옵션\t비 고\nCN1\tW01\t1\t1\t\t\t\t\nCN2\tW01\t2\t1\t\t\t\t\n\n" +
                "선이름\t선연결\t굵기\t색깔\nW01\t\t0.5\tR\n";
            string path = Path.Combine(root, "product.tht");
            byte[] original = BuildMinimalThtFile(text);
            // The legacy parser fixture scans directory entries directly. Set
            // the standard root-child link so Windows IStorage can edit it too.
            BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(1024 + 76), 1);
            File.WriteAllBytes(path, original);
            var parser = new ThtModelParser();
            ProductModel before = parser.Load(path);
            string backup = parser.EnsureDiscardContacts(path, 97, 98)!;
            Assert(File.ReadAllBytes(backup).SequenceEqual(original), "THT update keeps an exact original backup before atomic replacement");
            ProductModel after = parser.Load(path);
            Assert(after.DiscardContactIo.SequenceEqual([97, 98]) &&
                before.Pins.SequenceEqual(after.Pins) && before.Nets.Count == after.Nets.Count &&
                after.PartNumber == before.PartNumber && after.Eco == before.Eco && after.Alc == before.Alc,
                "Native THT update installs exactly two discard pins and preserves product topology and Part metadata");
            byte[] once = File.ReadAllBytes(path);
            Assert(parser.EnsureDiscardContacts(path, 97, 98) is null && File.ReadAllBytes(path).SequenceEqual(once) &&
                Directory.GetFiles(Path.Combine(root, "ThtBackups")).Length == 1,
                "Repeated selection is byte-idempotent and creates no duplicate contacts or redundant backups");
            string pendingEdit = Path.Combine(root, ".product.tht.preview.tmp.tht");
            File.WriteAllBytes(pendingEdit, original);
            Assert(ProductFileCatalog.Read(root).Files.Select(entry => entry.FullPath).SequenceEqual([path]),
                "Product selection excludes editor backups and in-progress temporary THT files");
            File.Delete(pendingEdit);
            parser.EnsureDiscardContacts(path, 99, 100);
            Assert(parser.Load(path).DiscardContactIo.SequenceEqual([97, 98]) && File.ReadAllBytes(path).SequenceEqual(once),
                "An existing DISCARD pair is preserved even when the newly learned station pair differs");
            byte[] changed = File.ReadAllBytes(path);
            Assert(parser.EnsureDiscardContacts(path, 0, 0) is null && File.ReadAllBytes(path).SequenceEqual(changed),
                "Clearing both setting fields preserves the THT's existing DISCARD configuration");
            foreach (var pair in new[] { (1, 98), (97, 97), (0, 98), (-1, 98), (97, 641) })
            {
                File.WriteAllBytes(path, original);
                bool rejected = false;
                try { parser.EnsureDiscardContacts(path, pair.Item1, pair.Item2); }
                catch (InvalidOperationException) { rejected = true; }
                Assert(rejected && File.ReadAllBytes(path).SequenceEqual(original), "Occupied, duplicate, partial or out-of-range IO never modifies the original THT");
            }
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool rejected = false;
                try { parser.EnsureDiscardContacts(path, 101, 102); }
                catch (IOException) { rejected = true; }
                Assert(rejected && File.ReadAllBytes(path).SequenceEqual(original), "A THT held by another program is retained when atomic replacement is denied");
            }
            Assert(!Directory.GetFiles(root, "*.tmp.tht").Any(), "Failed or completed edits leave no temporary THT files");
            var settings = new ProductionSettings { DiscardContactIo1 = 97, DiscardContactIo2 = 98 };
            string cfg = Path.Combine(root, "station.cfg");
            ProductionConfigService.SaveLegacyCfg(settings, cfg);
            var loaded = ProductionConfigService.LoadSavedSettings(cfg);
            Assert(loaded.DiscardContactIo1 == 0 && loaded.DiscardContactIo2 == 0 &&
                   !File.ReadAllText(cfg).Contains("[DiscardContactIo", StringComparison.Ordinal),
                "Global discard IO keys are no longer written to CFG");
            parser.SaveDiscardContacts(path, 97, 98);
            settings.LastThtPath = path;
            var vm = new ProductionSettingsViewModel(initialSettings: settings, saveSettings: _ => { });
            Assert(vm.DiscardContactIo1Text == "97" && vm.DiscardContactIo2Text == "98", "Settings editor reads the pair from this product's THT");
            vm.DiscardContactIo1Text = "97";
            vm.DiscardContactIo2Text = string.Empty;
            bool partialRejected = false;
            try { vm.Save(); } catch (InvalidOperationException) { partialRejected = true; }
            Assert(partialRejected, "Settings cannot save only one discard IO");

            string sidecar = path + ".leak.json";
            var layout = System.Text.Json.JsonSerializer.SerializeToNode(new ModelLeakLayoutService.Layout(1,
                [new(1, "CN1")], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))))!;
            layout["CustomMachineSetting"] = "KEEP";
            File.WriteAllText(sidecar, layout.ToJsonString());
            parser.SaveDiscardContacts(path, 41, 86);
            var rebound = ModelLeakLayoutService.Apply(path, new WaterProofModelSettings { PressMin = 42, LeakLimit = 7 });
            Assert(parser.Load(path).DiscardContactIo.SequenceEqual([41, 86]) && rebound.Channel1Connector == "CN1" &&
                   rebound.PressMin == 42 && rebound.LeakLimit == 7 && File.ReadAllText(sidecar).Contains("KEEP"),
                "Changing this product's pair rebinds its validated Leak sidecar while retaining channel and machine settings");
            byte[] thtBeforeFailure = File.ReadAllBytes(path), sidecarBeforeFailure = File.ReadAllBytes(sidecar);
            using (var held = new FileStream(sidecar, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool blocked = false;
                try { parser.SaveDiscardContacts(path, 43, 88); } catch (IOException) { blocked = true; }
                Assert(blocked && File.ReadAllBytes(path).SequenceEqual(thtBeforeFailure) &&
                       File.ReadAllBytes(sidecar).SequenceEqual(sidecarBeforeFailure),
                    "A locked Leak sidecar rolls back the THT and preserves the exact original matching pair");
            }
            Assert(ModelLeakLayoutService.Apply(path, new()).Channel1Connector == "CN1",
                "Rolled-back THT and sidecar remain valid together");
            parser.SaveDiscardContacts(path, 0, 0);
            Assert(parser.Load(path).DiscardContactIo.Count == 0 && ModelLeakLayoutService.Apply(path, new()).Channel1Connector == "CN1",
                "Clearing only this product's DISCARD leaves its product and Leak topology usable");
        }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-DiscardEditor-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, recursive: true);
        }
    }
}


