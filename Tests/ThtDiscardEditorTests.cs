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
            Assert(ThtModelParser.IsDiscardConnector("_DISCARD") &&
                   ThtModelParser.IsDiscardConnector("_Interlock") && ThtModelParser.IsDiscardConnector("_INTERLOCK") &&
                   !ThtModelParser.IsDiscardConnector("Interlock"),
                "Only reserved legacy and new interlock connector names are recognized case-insensitively");
            string legacyPath = Path.Combine(root, "legacy.tht");
            string legacyText = text.Replace("CN1\t2\n", "CN1\t2\n\t_DISCARD\t2\n")
                .Replace("\n\n선이름", "\n_DISCARD\t\t97\t1\t\t\t\t\n_DISCARD\t\t98\t2\t\t\t\t\n\n선이름");
            byte[] legacyBytes = BuildMinimalThtFile(legacyText);
            BinaryPrimitives.WriteUInt32LittleEndian(legacyBytes.AsSpan(1024 + 76), 1);
            File.WriteAllBytes(legacyPath, legacyBytes);
            var legacyModel = parser.Load(legacyPath);
            Assert(legacyModel.DiscardContactIo.SequenceEqual([97, 98]), "Legacy DISCARD files still load without modification");
            string legacyBackup = parser.SaveDiscardContacts(legacyPath, 97, 98)!;
            string migratedText = Encoding.ASCII.GetString(JBZUniversalTester.ModelEditor.Services.ThtFileService.ReadContentsForTest(legacyPath));
            var migratedModel = parser.Load(legacyPath);
            Assert(migratedText.Contains("_Interlock") && !migratedText.Contains("_DISCARD") &&
                   migratedModel.DiscardContactIo.SequenceEqual([97, 98]) && migratedModel.Pins.SequenceEqual(legacyModel.Pins) &&
                   File.ReadAllBytes(legacyBackup).SequenceEqual(legacyBytes),
                "Explicit saving migrates both native tables to Interlock while preserving IO, product topology and backup");
            File.Delete(legacyPath);
            File.Delete(legacyBackup);
            foreach (int offset in new[] { 16, 20, 24, 32, 33, 64, 102, 128, 256, 1024, 4095 })
            {
                string variantRoot = Path.Combine(root, "ThtBackups", $"header-{offset}");
                Directory.CreateDirectory(variantRoot);
                string variant = CreateDiscardSettingsTht(variantRoot, "product", offset);
                byte[] variantOriginal = File.ReadAllBytes(variant);
                string variantBackup = parser.SaveDiscardContacts(variant, 97, 98)!;
                var readerType = typeof(ThtModelParser).GetNestedType("CompoundFileReader", BindingFlags.NonPublic)!;
                var reader = Activator.CreateInstance(readerType, new object[] { File.ReadAllBytes(variant) })!;
                var contents = (byte[])readerType.GetMethod("ReadStream")!.Invoke(reader, ["Contents"])!;
                Assert(parser.Load(variant).DiscardContactIo.SequenceEqual([97, 98]) &&
                       File.ReadAllBytes(variantBackup).SequenceEqual(variantOriginal) &&
                       BinaryPrimitives.ReadUInt32LittleEndian(contents) == contents.Length - 8 &&
                       BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(4)) == contents.Length - offset,
                    "Variable native inner header conventions survive a verified discard write with exact backup");
                parser.SaveDiscardContacts(variant, 41, 86);
                parser.SaveDiscardContacts(variant, 0, 0);
                Assert(parser.Load(variant).DiscardContactIo.Count == 0,
                    "Native header variants support subsequent updates and clearing the pair");
            }
            foreach (int outerOffset in new[] { 0, 4, 16, 102 })
            {
                string variantRoot = Path.Combine(root, "ThtBackups", $"outer-{outerOffset}");
                Directory.CreateDirectory(variantRoot);
                string variant = CreateDiscardSettingsTht(variantRoot, "product", 102);
                byte[] bytes = File.ReadAllBytes(variant);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1536), (uint)(4096 - outerOffset));
                File.WriteAllBytes(variant, bytes);
                parser.SaveDiscardContacts(variant, 97, 98);
                byte[] contents = JBZUniversalTester.ModelEditor.Services.ThtFileService.ReadContentsForTest(variant);
                Assert(BinaryPrimitives.ReadUInt32LittleEndian(contents) == contents.Length - outerOffset &&
                       BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(4)) == contents.Length - 102 &&
                       parser.Load(variant).DiscardContactIo.SequenceEqual([97, 98]),
                    "Variable outer/inner length offsets are preserved together instead of normalized");
            }
            foreach (bool invalidOuter in new[] { false, true })
            {
                string variantRoot = Path.Combine(root, "ThtBackups", $"invalid-{invalidOuter}");
                Directory.CreateDirectory(variantRoot);
                string variant = CreateDiscardSettingsTht(variantRoot, "product", 102);
                byte[] bytes = File.ReadAllBytes(variant);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(invalidOuter ? 1536 : 1540), uint.MaxValue);
                File.WriteAllBytes(variant, bytes);
                bool rejected = false;
                try { parser.SaveDiscardContacts(variant, 97, 98); }
                catch (InvalidDataException) { rejected = true; }
                Assert(rejected && File.ReadAllBytes(variant).SequenceEqual(bytes),
                    "Out-of-bounds headers are still rejected without modifying the source THT");
            }
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
    private static void TestDiscardNativeHeaderCopies(string directory)
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-native-discard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var parser = new ThtModelParser();
            foreach (string source in Directory.EnumerateFiles(directory, "*.tht", SearchOption.TopDirectoryOnly))
            {
                byte[] original = File.ReadAllBytes(source);
                string copyRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(copyRoot);
                string copy = Path.Combine(copyRoot, Path.GetFileName(source));
                File.WriteAllBytes(copy, original);
                var before = parser.LoadAll(copy);
                var used = before.SelectMany(model => model.Pins).Select(pin => pin.IoNumber).ToHashSet();
                int[] free = Enumerable.Range(1, BoardCapacity.MaxGlobalIo).Reverse().Where(io => !used.Contains(io)).Take(2).ToArray();
                Assert(free.Length == 2, "Native test copy has two unused IOs");
                byte[] contents = JBZUniversalTester.ModelEditor.Services.ThtFileService.ReadContentsForTest(copy);
                uint outer = BinaryPrimitives.ReadUInt32LittleEndian(contents), inner = BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(4));
                string? backup = parser.SaveDiscardContacts(copy, free[0], free[1]);
                Assert(parser.LoadAll(copy).All(model => model.DiscardContactIo.Order().SequenceEqual(free.Order())),
                    "Actual native file copy saves and reloads its interlock IOs");
                Assert(backup is null || File.ReadAllBytes(backup).SequenceEqual(original), "Native file backup is byte-identical");
                byte[] after = JBZUniversalTester.ModelEditor.Services.ThtFileService.ReadContentsForTest(copy);
                long delta = after.Length - (long)contents.Length;
                Assert(BinaryPrimitives.ReadUInt32LittleEndian(after) == (outer == 0 ? 0 : outer + delta) &&
                       BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan(4)) == (inner == 0 ? 0 : inner + delta),
                    "Actual native file retains its own length conventions");
                parser.SaveDiscardContacts(copy, free[1], free[0]);
                parser.SaveDiscardContacts(copy, 0, 0);
                Assert(parser.LoadAll(copy).All(model => model.DiscardContactIo.Count == 0) &&
                       File.ReadAllBytes(source).SequenceEqual(original), "Clearing native copy preserves source bytes");
                Console.WriteLine($"PASS NATIVE DISCARD: {Path.GetFileName(source)} Contents={contents.Length} Outer={outer} Inner={inner}");
            }
        }
        finally { Directory.Delete(root, true); }
    }

}


