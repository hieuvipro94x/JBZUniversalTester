using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static string CreateDiscardSettingsTht(string root, string name)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        string text = "파트번호\t파트명\n" + name + "\tPRODUCT\n\n" +
            "번 호\t커넥터\t핀 수\n1\tCN1\t1\n2\tCN2\t1\n\n" +
            "커넥터\t선이름\tＩ／Ｏ\t핀번호\t핀종류\t핀차수\t핀옵션\t비 고\nCN1\tW01\t1\t1\t\t\t\t\nCN2\tW01\t2\t1\t\t\t\t\n\n" +
            "선이름\t선연결\t굵기\t색깔\nW01\t\t0.5\tR\n";
        byte[] bytes = BuildMinimalThtFile(text);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1024 + 76), 1);
        string path = Path.Combine(root, name + ".tht");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void TestDiscardSettingsSave()
    {
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-DiscardSave-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var pages = new List<ProductionSettingsPage>();
            try
            {
                string cfg = Path.Combine(root, "settings.cfg");
                int writes = 0;
                string product = CreateDiscardSettingsTht(root, "A");
                string other = CreateDiscardSettingsTht(root, "B");
                byte[] untouched = File.ReadAllBytes(other);
                var parser = new ThtModelParser();
                var settings = new ProductionSettings { MasterFaultRequiredCount = 0, LastThtPath = product,
                    DiscardContactIo1 = 97, DiscardContactIo2 = 98 };
                var vm = new ProductionSettingsViewModel(initialSettings: settings, saveSettings: value =>
                {
                    ProductionConfigService.SaveLegacyCfg(value, cfg);
                    Interlocked.Increment(ref writes);
                });
                var page = new ProductionSettingsPage(null, vm);
                pages.Add(page);
                Assert(vm.DiscardContactIo1Text == "" && vm.DiscardContactIo2Text == "",
                    "Legacy station-wide IO cannot leak into a product without DISCARD");
                async Task<bool> Save() => await (Task<bool>)typeof(ProductionSettingsPage)
                    .GetMethod("PersistSettingsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                Assert(await Save() && !page.LastSaveChanged && writes == 0,
                    "Unchanged settings retain the no-write fast path");

                // Same assignments used by LearnDiscardIo_Click after sensor learning.
                vm.DiscardContactIo1Text = "41";
                vm.DiscardContactIo2Text = "86";
                Assert(await Save() && page.LastSaveChanged && writes == 1,
                    "Learning only the discard IO pair must invoke the real page's Save pipeline");
                var saved = ProductionConfigService.LoadSavedSettings(cfg);
                Assert(parser.Load(product).DiscardContactIo.SequenceEqual([41, 86]) &&
                       !File.ReadAllText(cfg).Contains("[DiscardContactIo", StringComparison.Ordinal) &&
                       File.ReadAllBytes(other).SequenceEqual(untouched),
                    "Only product A's native THT is changed, with no global CFG IO and no change to B");
                var reopened = new ProductionSettingsViewModel(initialSettings: saved, saveSettings: _ => { });
                Assert(reopened.DiscardContactIo1Text == "41" && reopened.DiscardContactIo2Text == "86",
                    "Reopening settings restores the learned pair");
                Assert(await Save() && !page.LastSaveChanged && writes == 1,
                    "A repeated Save does not write an unchanged learned pair again");

                vm.DiscardContactIo1Text = "42";
                Assert(await Save() && writes == 2 && parser.Load(product).DiscardContactIo.SequenceEqual([42, 86]),
                    "Changing just one IO is also detected and persisted");
                var productB = new ProductionSettingsViewModel(initialSettings: new ProductionSettings { LastThtPath = other }, saveSettings: _ => { });
                productB.DiscardContactIo1Text = "51";
                productB.DiscardContactIo2Text = "96";
                await productB.SaveAsync();
                Assert(parser.Load(other).DiscardContactIo.SequenceEqual([51, 96]) &&
                       parser.Load(product).DiscardContactIo.SequenceEqual([42, 86]), "A and B retain independent learned IO pairs");
                vm.DiscardContactIo1Text = vm.DiscardContactIo2Text = "";
                Assert(await Save() && writes == 3,
                    "Clearing only the discard pair must not bypass Save");
                saved = ProductionConfigService.LoadSavedSettings(cfg);
                Assert(parser.Load(product).DiscardContactIo.Count == 0 && parser.Load(other).DiscardContactIo.SequenceEqual([51, 96]),
                    "Clearing A removes only A's DISCARD pair and preserves B");
            }
            finally
            {
                foreach (var page in pages) page.ReleasePageResources();
                if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-DiscardSave-"), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(root, true);
            }
        });
    }
}
