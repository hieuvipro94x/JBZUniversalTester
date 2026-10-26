using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestFirstProductPrintSetup()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-FirstPrint-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var settings = new ProductionSettings();
            var store = new TestHistoryStore(Path.Combine(root, "history.db"));
            await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            await persistence.Initialization;
            var vm = CreateTestViewModel(settings, out FakeBoard board);
            typeof(TestViewModel).GetField("_historyStore", flags)!.SetValue(vm, store);
            typeof(TestViewModel).GetField("_productionPersistence", flags)!.SetValue(vm, persistence);
            try
            {
                var fresh = Model(("PAIR", new[] { 1, 2 })); fresh.PartNumber = "NEW-FIRST-ITEM";
                var tested = Model(("PAIR", new[] { 1, 2 })); tested.PartNumber = "TESTED-BEFORE";
                DateTime now = DateTime.Now;
                store.CommitResult(ProductionResultCommitRequest.Capture(new TestHistoryRecord
                {
                    CycleId = "existing-fail", PartNumber = tested.PartNumber, ModelName = tested.ModelName,
                    Started = now, Finished = now, TestStartedAt = now, ResultAt = now,
                    Passed = false, Result = "FAIL", InspectionType = HistoryInspectionType.Product
                }, tested, settings, [], [], null, ProgramIdentityService.VersionText), null);
                int prompts = 0, saves = 0;
                Assert(!await vm.EnsureInitialProductPrintSetupAsync(fresh, (_, _) =>
                {
                    prompts++; return Task.FromResult(false);
                }, _ => saves++), "Cancel keeps an unconfigured new product unselected");
                Assert(prompts == 1 && saves == 0 && !settings.LabelTemplateTypesByProduct.ContainsKey(fresh.PartNumber),
                    "Cancel cannot persist default LOT or template silently");
                string cfg = Path.Combine(root, "settings.cfg");
                Assert(await vm.EnsureInitialProductPrintSetupAsync(fresh, async (_, save) =>
                {
                    prompts++; await save(new(2500, LabelSettings.SmallQrTemplate)); return true;
                }, snapshot => { saves++; ProductionConfigService.SaveLegacyCfg(snapshot, cfg); }),
                    "First explicit selection requires and saves the operator's LOT and template");
                Assert(settings.LotSettingsByProduct[fresh.PartNumber].StartLotNo == 2500 &&
                       settings.LabelTemplateTypesByProduct[fresh.PartNumber] == LabelSettings.SmallQrTemplate,
                    "Successful first setup publishes the exact chosen values");
                Assert(await vm.EnsureInitialProductPrintSetupAsync(fresh, (_, _) => throw new InvalidOperationException("Must not prompt twice")),
                    "Configured but not yet tested products are never prompted again");
                Assert(await vm.EnsureInitialProductPrintSetupAsync(tested, (_, _) => throw new InvalidOperationException("Existing history must skip prompt")),
                    "Any previous production test, including FAIL, skips first-time setup");
                ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                Assert(await vm.EnsureInitialProductPrintSetupAsync(fresh, (_, _) => throw new InvalidOperationException("Restart must retain setup")),
                    "CFG reload keeps first-time setup complete without requiring a test");
                var failed = Model(("PAIR", new[] { 1, 2 })); failed.PartNumber = "SAVE-FAILED";
                bool failedSave = false;
                try
                {
                    await vm.EnsureInitialProductPrintSetupAsync(failed, async (_, save) =>
                    {
                        await save(new(7000, LabelSettings.LargeSqdzTemplate)); return true;
                    }, _ => throw new IOException("Simulated disk failure"));
                }
                catch (IOException) { failedSave = true; }
                Assert(failedSave && !settings.LabelTemplateTypesByProduct.ContainsKey(failed.PartNumber),
                    "Failed CFG writes do not mark setup complete or mutate runtime settings");
                Assert(prompts == 2 && saves == 1 && board.Commands.Count == 0,
                    "First setup opens no hardware sessions, measures nothing and fires no relays");
                var noPrint = Model(("PAIR", new[] { 1, 2 })); noPrint.PartNumber = "NO-LABEL";
                Assert(await vm.EnsureInitialProductPrintSetupAsync(noPrint, async (_, save) =>
                {
                    await save(new(0, string.Empty, PrintLabel: false)); return true;
                }, snapshot => ProductionConfigService.SaveLegacyCfg(snapshot, cfg)),
                    "A new nonprinting product does not require a template");
                ProductionConfigService.CopyInto(settings, ProductionConfigService.LoadSavedSettings(cfg));
                Assert(!ProductionConfigService.IsProductLabelPrintingEnabled(settings, noPrint.PartNumber) &&
                       await vm.EnsureInitialProductPrintSetupAsync(noPrint, (_, _) => throw new InvalidOperationException("No-print choice must persist")),
                    "No-print selection is durable and suppresses future first-time prompts");

                bool rejectSave = true;
                int dialogSaves = 0;
                var dialog = new ProductPrintSetupWindow(fresh, _ =>
                {
                    dialogSaves++;
                    return rejectSave ? Task.FromException(new IOException("Disk unavailable")) : Task.CompletedTask;
                }) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
                Exception? dialogError = null;
                dialog.Loaded += async (_, _) =>
                {
                  try
                  {
                    var lot = (TextBox)dialog.FindName("StartLotTextBox");
                    var template = (ComboBox)dialog.FindName("TemplateComboBox");
                    var validation = (TextBlock)dialog.FindName("ValidationText");
                    async Task Confirm() => await (Task)typeof(ProductPrintSetupWindow).GetMethod("ConfirmAsync", flags)!.Invoke(dialog, null)!;
                    await Confirm();
                    Assert(dialog.IsVisible && dialogSaves == 0 && validation.Visibility == Visibility.Visible,
                        "Blank LOT cannot confirm first setup");
                    lot.Text = "2000";
                    ((RadioButton)dialog.FindName("PrintYesRadioButton")).IsChecked = true;
                    await Confirm();
                    Assert(dialog.IsVisible && template.SelectedIndex == -1 && dialogSaves == 0,
                        "Template must be explicitly selected by the operator");
                    template.SelectedItem = LabelSettings.LargeSqdzTemplate;
                    await Confirm();
                    Assert(dialog.IsVisible && dialogSaves == 1 && validation.Visibility == Visibility.Visible,
                        "Failed persistence keeps the actual setup dialog open for retry");
                    validation.Visibility = Visibility.Collapsed;
                    dialog.UpdateLayout();
                    var surface = (FrameworkElement)dialog.Content;
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(surface);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var output = File.Create(Path.Combine(Path.GetTempPath(), "jbz-v319-first-product-setup.png"))) encoder.Save(output);
                    rejectSave = false;
                    await Confirm();
                    Assert(!dialog.IsVisible && dialogSaves == 2, "Only successful storage closes first-time setup");
                  }
                  catch (Exception ex) { dialogError = ex; }
                  finally { dialog.Close(); }
                };
                dialog.ShowDialog();
                if (dialogError is not null) throw dialogError;
            }
            finally
            {
                await persistence.DisposeAsync();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(root, recursive: true);
            }
        });
    }
}
