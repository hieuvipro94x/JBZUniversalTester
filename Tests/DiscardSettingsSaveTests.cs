using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
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
                var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
                var vm = new ProductionSettingsViewModel(initialSettings: settings, saveSettings: value =>
                {
                    ProductionConfigService.SaveLegacyCfg(value, cfg);
                    Interlocked.Increment(ref writes);
                });
                var page = new ProductionSettingsPage(null, vm);
                pages.Add(page);
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
                Assert(saved.DiscardContactIo1 == 41 && saved.DiscardContactIo2 == 86,
                    "Both learned IOs reach the durable CFG");
                var reopened = new ProductionSettingsViewModel(initialSettings: saved, saveSettings: _ => { });
                Assert(reopened.DiscardContactIo1Text == "41" && reopened.DiscardContactIo2Text == "86",
                    "Reopening settings restores the learned pair");
                Assert(await Save() && !page.LastSaveChanged && writes == 1,
                    "A repeated Save does not write an unchanged learned pair again");

                vm.DiscardContactIo1Text = "42";
                Assert(await Save() && writes == 2 && ProductionConfigService.LoadSavedSettings(cfg).DiscardContactIo1 == 42,
                    "Changing just one IO is also detected and persisted");
                vm.DiscardContactIo1Text = vm.DiscardContactIo2Text = "";
                Assert(await Save() && writes == 3,
                    "Clearing only the discard pair must not bypass Save");
                saved = ProductionConfigService.LoadSavedSettings(cfg);
                Assert(saved.DiscardContactIo1 == 0 && saved.DiscardContactIo2 == 0,
                    "Cleared CFG values remain cleared after reload");
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
