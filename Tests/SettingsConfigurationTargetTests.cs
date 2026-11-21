using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;
namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestCatalogDeleteConfirmation()
    {
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-CatalogDelete-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string active = Path.Combine(root, "ACTIVE.tht"), other = Path.Combine(root, "OTHER.tht");
            File.WriteAllText(active, "ACTIVE"); File.WriteAllText(other, "KEEP-UNTIL-CONFIRMED");
            int writes = 0, opens = 0; bool fail = false;
            var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var panel = new MainProductCatalog(ProductFileCatalog.Read, path =>
            {
                if (fail) throw new IOException("Fixture denied");
                writes++; worker.TrySetResult(); release.Task.GetAwaiter().GetResult();
                File.Move(path, path + ".recycled");
            });
            try
            {
                panel.ProductRequested += _ => opens++;
                await panel.LoadAsync(root, active, Path.Combine(root, "none.db"));
                var list = (ListBox)panel.FindName("ProductList");
                Assert(!await panel.DeleteSelectedAsync(_ => throw new Exception("Cannot confirm active model")) && writes == 0,
                    "Deletion of the active runtime THT is blocked before confirmation");
                list.SelectedItem = list.Items.Cast<ProductFileEntry>().Single(x => x.FullPath == other);
                Assert(!await panel.DeleteSelectedAsync(_ => false) && File.Exists(other) && writes == 0,
                    "Cancel confirmation leaves the selected THT intact");
                fail = true;
                bool rejected = false;
                try { await panel.DeleteSelectedAsync(_ => true); } catch (IOException) { rejected = true; }
                Assert(rejected && File.Exists(other) && list.Items.Count == 2, "Failed deletion preserves the file and visible row");
                fail = false;
                Task<bool> deleting = panel.DeleteSelectedAsync(file => file.FullPath == other);
                await worker.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Assert(!deleting.IsCompleted && !await panel.DeleteSelectedAsync(_ => true), "Slow recycle runs off UI and rejects duplicate deletion");
                await System.Windows.Threading.Dispatcher.Yield(); release.TrySetResult();
                Assert(await deleting && !File.Exists(other) && File.ReadAllText(other + ".recycled") == "KEEP-UNTIL-CONFIRMED" &&
                       File.Exists(active) && list.Items.Count == 1 && opens == 0,
                    "Confirmed exact-path deletion refreshes the list while preserving active THT and avoiding runtime selection");
            }
            finally { release.TrySetResult(); Directory.Delete(root, recursive: true); }
        });
    }

    private static void TestOfflineSettingsDiscardCapacity()
    {
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-OfflineDiscard-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            ProductionSettingsPage? page = null;
            try
            {
                string a = CreateDiscardSettingsTht(root, "ACTIVE"), b = CreateDiscardSettingsTht(root, "WH320853-V_BMS&U ART(BC3개제외)_190610");
                var parser = new ThtModelParser(); parser.SaveDiscardContacts(b, 609, 610);
                byte[] originalB = File.ReadAllBytes(b), originalA = File.ReadAllBytes(a);
                string cfg = Path.Combine(root, "settings.cfg"); int writes = 0;
                var initial = new ProductionSettingsViewModel(initialSettings: new ProductionSettings { LastThtPath = a, MasterFaultRequiredCount = 0, ExpansionCardCount = 2 },
                    saveSettings: snapshot => { ProductionConfigService.SaveLegacyCfg(snapshot, cfg); writes++; });
                var vm = initial.CreateForConfigurationModel(b);
                page = new ProductionSettingsPage(null, vm, deferInitialLayout: true);
                page.SetAccessSession(new SettingsAccessSession(false, SettingsPermission.All));
                vm.PrintLabelForCurrentProduct = true; vm.Settings.Label.TemplateType = LabelSettings.SmallQrTemplate;
                async Task<bool> Save() => await (Task<bool>)typeof(ProductionSettingsPage).GetMethod("PersistSettingsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
                Assert(await Save() && writes == 1,
                    "Operator can save the selected offline THT label while its unchanged 609/610 pair exceeds the active station's 128 IO");
                Assert(File.ReadAllBytes(b).SequenceEqual(originalB) && File.ReadAllBytes(a).SequenceEqual(originalA),
                    "Saving a label keeps both native THT files byte-for-byte intact");
                var saved = ProductionConfigService.LoadSavedSettings(cfg);
                Assert(saved.LastThtPath == a && vm.CreateForConfigurationModel(b).Settings.Label.TemplateType == LabelSettings.SmallQrTemplate &&
                       saved.ExpansionCardCount == 2 && parser.Load(b).DiscardContactIo.SequenceEqual([609,610]),
                    "Saving B preserves A startup and board capacity and restores B's exact label and discard pair");
                vm.DiscardContactIo1Text = "611"; vm.DiscardContactIo2Text = "612";
                Assert(await Save() && writes == 2 && parser.Load(b).DiscardContactIo.SequenceEqual([611,612]),
                    "Explicit offline IO edits use supported THT capacity rather than the unrelated active card count");
                byte[] beforeInvalid = File.ReadAllBytes(b);
                vm.DiscardContactIo1Text = "641";
                Assert(!await Save() && writes == 2 && File.ReadAllBytes(b).SequenceEqual(beforeInvalid),
                    "Offline IO outside hardware maximum still blocks all writes");
                var status = (System.Windows.Controls.TextBlock)page.FindName("SettingsSavedStatusText");
                Assert(status.Text.Contains("640") && Equals(status.ToolTip, status.Text),
                    "A rejected save shows the actual IO limit and exposes the complete reason as a tooltip");
                var active = new ProductionSettingsViewModel(initialSettings: new ProductionSettings { LastThtPath = b, ExpansionCardCount = 2 }, saveSettings: _ => throw new Exception("No active invalid save"));
                bool blocked = false; try { await active.SaveAsync(); } catch (InvalidOperationException ex) { blocked = ex.Message.Contains("128"); }
                Assert(blocked, "The normal runtime editor still enforces the active station's 128-IO capacity");
            }
            finally { page?.ReleasePageResources(); Directory.Delete(root, recursive: true); }
        });
    }

    private static void TestSettingsConfigurationTarget()
    {
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-ConfigTarget-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string a = Path.Combine(root, "A.tht"), b = Path.Combine(root, "B.tht"), c = Path.Combine(root, "C.tht");
                foreach (string path in new[] { a, b, c }) File.Copy(Path.Combine(Environment.CurrentDirectory, "Tests", "Fixtures", "MultiPartOptions.tht"), path);
                ProductionSettings? saved = null;
                var settings = new ProductionSettings { LastThtPath = a, LotNo = 321 };
                settings.Label.TemplateType = LabelSettings.LargeTemplate;
                var initial = new ProductionSettingsViewModel(null, snapshot => saved = snapshot, settings);
                var target = await Task.Run(() => initial.CreateForConfigurationModel(b));
                Assert(target.DiscardModelPath == b && target.Settings.LastThtPath == a && !target.IsEditingRuntimeModel,
                    "Selecting B configures B without replacing the active/startup model A");
                Assert(target.WaterProofConnectorOptions.SequenceEqual(new ThtModelParser().Load(b).Connectors.Select(x => x.ConnectorId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)),
                    "Leak connectors come from the selected THT rather than an unrelated runtime model");
                target.Settings.Label.TemplateType = LabelSettings.SmallQrTemplate;
                target.PrintLabelForCurrentProduct = true;
                target.ResistanceChannels[0].Enabled = true;
                target.ResistanceChannels[0].MinOhm = 100;
                target.ResistanceChannels[0].MaxOhm = 200;
                target.WaterProof.Enabled = true;
                target.WaterProof.PressTimeMs = 4567;
                target.Settings.LotNo = 7000;
                target.Settings.RelayWiringMode = 1;
                await target.SaveAsync();
                Assert(saved is not null && saved.LastThtPath == a && saved.LotNo == 321 && saved.Label.TemplateType == LabelSettings.LargeTemplate && !saved.ResistanceChannels[0].Enabled,
                    $"Saving B preserves station startup, legacy LOT, label and resistance fallbacks: {saved?.LastThtPath}, {saved?.LotNo}, {saved?.Label.TemplateType}, {saved?.ResistanceChannels[0].Enabled}");
                Assert(ProductionConfigService.GetResistanceProfileForPath(saved!, b)[0].MinOhm == 100 &&
                       ProductionConfigService.GetWaterProofProfileForPath(saved!, b).PressTimeMs == 4567 &&
                       ProductionConfigService.GetRelayWiringModeForPath(saved!, b) == 1 &&
                       ProductionConfigService.GetRelayWiringModeForPath(saved!, a) == 0,
                    "B resistance, Leak and relay overrides persist separately from A");
                var reopened = target.CreateForConfigurationModel(b);
                Assert(reopened.Settings.Label.TemplateType == LabelSettings.SmallQrTemplate && reopened.Settings.LotNo == 7000 && reopened.ResistanceChannels[0].Enabled,
                    "Reopening B restores its saved label, LOT and resistance editors");
                var next = target.CreateForConfigurationModel(c);
                Assert(next.Settings.LotNo != 7000 && !next.ResistanceChannels[0].Enabled,
                    "A new target cannot inherit B's LOT or resistance edits");
                var page = new ProductionSettingsPage(null, target, deferInitialLayout: true);
                try
                {
                    int switches = 0;
                    page.ConfigurationModelRequested += (_, path) => { Assert(path == c, "Switch event carries the target path"); switches++; return Task.CompletedTask; };
                    target.Settings.DeviceName = "UNSAVED";
                    Assert(!await page.SelectConfigurationModelAsync(c, () => null) && switches == 0,
                        "Cancel keeps the current settings page and pending edits");
                    Assert(await page.SelectConfigurationModelAsync(c, () => false) && switches == 1,
                        "Discard switches to another target without saving pending edits");
                    Assert(target.CreateForConfigurationModel(c).Settings.DeviceName != "UNSAVED",
                        "Discarded shared edits do not leak into the next configuration target");
                    Assert((string)typeof(ProductionSettingsPage).GetField("_labelPrintModelPath", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)! == b,
                        "Manual label preview/printing uses the selected configuration THT");
                }
                finally { page.ReleasePageResources(); }
                using var catalog = new MainProductCatalog();
                await catalog.LoadAsync(root, a, Path.Combine(root, "none.db"));
                catalog.Measure(new Size(420, 650)); catalog.Arrange(new Rect(0, 0, 420, 650)); catalog.UpdateLayout();
                var list = (ListBox)catalog.FindName("ProductList");
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert(row.Height == 58 && row.BorderThickness == new Thickness(0,0,0,1),
                    "Catalog reserves fixed two-line rows with a single separator");
            }
            finally { Directory.Delete(root, recursive: true); }
        });
    }
}
