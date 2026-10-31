using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JBZUniversalTester.ModelEditor;
using JBZUniversalTester.ModelEditor.Services;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestIntegratedThtEditor()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-IntegratedEditor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "EDIT-A.tht");
            File.Copy(Path.Combine(Environment.CurrentDirectory, "Features", "ModelEditor", "Assets", "harnprog_template_v1110.tht"), path);
            var parser = new ThtModelParser();
            parser.SaveDiscardContacts(path, 639, 640);
            Assert(parser.Load(path).DiscardContactIo.SequenceEqual([639, 640]),
                "Explicit per-product Save fills a native THT's unused DISCARD connector without replacing product pins");
            byte[] original = File.ReadAllBytes(path);
            var document = ThtFileService.Load(path);
            var unchanged = IntegratedThtStore.Save(document.Clone(), path, IntegratedThtStore.Fingerprint(path));
            Assert(File.ReadAllBytes(path).SequenceEqual(original), "Native editor no-edit round trip preserves the complete THT byte for byte");
            var product = parser.Load(path);
            unchanged.Base.Name = "EDITED PRODUCT";
            var saved = IntegratedThtStore.Save(unchanged, path, IntegratedThtStore.Fingerprint(path));
            var edited = parser.Load(path);
            Assert(edited.ProductName == "EDITED PRODUCT" && edited.PartNumber == product.PartNumber &&
                   edited.DiscardContactIo.SequenceEqual(product.DiscardContactIo) && edited.Pins.SequenceEqual(product.Pins) &&
                   edited.LabelTemplate == product.LabelTemplate && edited.ResistanceSteps.SequenceEqual(product.ResistanceSteps) &&
                   System.Text.Json.JsonSerializer.Serialize(edited.Clip) == System.Text.Json.JsonSerializer.Serialize(product.Clip),
                "Editing metadata retains product topology, the per-product DISCARD pair and identity");
            byte[] beforeInvalid = File.ReadAllBytes(path);
            bool rejected = false;
            try { IntegratedThtStore.Save(saved.Clone(), path, "STALE"); } catch (IOException) { rejected = true; }
            Assert(rejected && File.ReadAllBytes(path).SequenceEqual(beforeInvalid), "A stale editor cannot overwrite a newer THT");
            var invalid = saved.Clone(); invalid.Base.Name = "INVALID \U0001F600";
            rejected = false;
            try { IntegratedThtStore.Save(invalid, path, IntegratedThtStore.Fingerprint(path)); }
            catch (Exception ex) when (ex is JBZUniversalTester.ModelEditor.Models.ThtExportException or System.Text.EncoderFallbackException) { rejected = true; }
            Assert(rejected && File.ReadAllBytes(path).SequenceEqual(beforeInvalid), "Unsupported CP949 text cannot corrupt the original file");
            Assert(Directory.GetFiles(Path.Combine(root, "ThtBackups"), "*.tht").Length >= 2 &&
                   !Directory.GetFiles(root, "*.tmp.tht").Any(), "Every overwrite retains a backup and leaves no staged THT behind");
            var leakDoc = ThtFileService.Load(path);
            var leakConnector = leakDoc.Connectors.First(connector => !connector.Name.StartsWith('_') && connector.Name != "CLIP");
            leakConnector.Waterproof = true; leakConnector.LeakChannel = 1;
            var withLeak = IntegratedThtStore.Save(leakDoc, path, IntegratedThtStore.Fingerprint(path));
            Assert(ModelLeakLayoutService.Apply(path, new()).Channel1Connector == leakConnector.Name,
                "Editor saves Leak connector assignment with a valid matching THT hash");
            byte[] thtBeforeLock = File.ReadAllBytes(path), leakBeforeLock = File.ReadAllBytes(path + ".leak.json");
            using (var held = new FileStream(path + ".leak.json", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                withLeak.Base.Name = "MUST ROLL BACK";
                rejected = false;
                try { IntegratedThtStore.Save(withLeak, path, IntegratedThtStore.Fingerprint(path)); } catch (IOException) { rejected = true; }
                Assert(rejected && File.ReadAllBytes(path).SequenceEqual(thtBeforeLock) &&
                       File.ReadAllBytes(path + ".leak.json").SequenceEqual(leakBeforeLock),
                    "Failed sidecar replacement rolls back the THT and retains both exact original files");
            }
            var created = new JBZUniversalTester.ModelEditor.Models.ModelDocument
            {
                SourceFormat = "tht", Base = new() { Model = "NEW-THT", Number = "NEW-THT", Name = "NEW PRODUCT" },
                Connectors = [new() { Name = "1", PinCount = 1, StartIo = 1 }, new() { Name = "2", PinCount = 1, StartIo = 2 }],
                Pins = [new() { Io = 1, Connector = "1", PinNo = 1, Line = "WIRE1" }, new() { Io = 2, Connector = "2", PinNo = 1, Line = "WIRE1" }]
            };
            ModelLogic.RebuildConnections(created.Pins);
            string newPath = Path.Combine(root, "NEW-THT.tht");
            IntegratedThtStore.Save(created, newPath, null);
            Assert(parser.Load(newPath).Pins.Count == 2 && parser.Load(newPath).PartNumber == "NEW-THT",
                "New native THT uses the bundled HarnProg template without an external program or another topology format");
            byte[] newBytes = File.ReadAllBytes(newPath);
            string archived = IntegratedThtStore.Archive(newPath, IntegratedThtStore.Fingerprint(newPath));
            Assert(!File.Exists(newPath) && File.ReadAllBytes(archived).SequenceEqual(newBytes),
                "The restored Delete action archives the exact THT instead of losing the original");

            OnSettingsDispatcher(async () =>
            {
                var page = new ThtEditorPage();
                await page.OpenPathAsync(path);
                var canvas = (FrameworkElement)page.FindName("EditorCanvas");
                Assert(canvas.Width == 1224 && canvas.Height == 900 &&
                       Grid.GetColumn((FrameworkElement)((FrameworkElement)page.FindName("BaseNav")).Parent) == 0 &&
                       Grid.GetColumn((FrameworkElement)((ContentControl)page.FindName("ContentHost")).Parent) == 2,
                    "Editor retains ModelCreator's compact canvas, left navigation and right-hand editing panel");
                Assert(!page.HasUnsavedChanges, "Opening the original file does not require a Save confirmation");
                Assert(await page.ConfirmCloseAsync(), "Return immediately after Open does not show a Save dialog");
                var basePage = (FrameworkElement)((ContentControl)page.FindName("ContentHost")).Content;
                ((TextBox)basePage.FindName("NameBox")).Text = "SAVED FROM EDITOR UI";
                ((TextBox)basePage.FindName("ModelBox")).Text = "RENAMED-METADATA";
                Assert(page.HasUnsavedChanges, "Actual unsaved input requires confirmation");
                Assert(await page.SaveAsync() && parser.Load(path).ProductName == "SAVED FROM EDITOR UI" &&
                       page.SavedPaths.Contains(path) && !File.Exists(Path.Combine(root, "RENAMED-METADATA.tht")) &&
                       !page.HasUnsavedChanges, "Save overwrites the original opened path even when model metadata is renamed and clears the dirty state");
                int backupsBeforeNoOp = Directory.GetFiles(Path.Combine(root, "ThtBackups"), "*.tht").Length;
                byte[] savedBytes = File.ReadAllBytes(path);
                Assert(await page.SaveAsync() && !page.HasUnsavedChanges &&
                       Directory.GetFiles(Path.Combine(root, "ThtBackups"), "*.tht").Length == backupsBeforeNoOp &&
                       File.ReadAllBytes(path).SequenceEqual(savedBytes), "Repeated Save with no edits neither rewrites nor creates redundant backups");
                var savedBase = (FrameworkElement)((ContentControl)page.FindName("ContentHost")).Content;
                var nameEditor = (TextBox)savedBase.FindName("NameBox");
                string savedName = nameEditor.Text;
                nameEditor.Text = "TEMPORARY UNSAVED CHANGE";
                Assert(page.HasUnsavedChanges, "A new edit after Save requires confirmation again");
                nameEditor.Text = savedName;
                Assert(!page.HasUnsavedChanges && await page.ConfirmCloseAsync(),
                    "Reverting to saved values removes the Save confirmation requirement");
                foreach (int index in Enumerable.Range(0, 5))
                {
                    var nav = (System.Windows.Controls.Primitives.ToggleButton)page.FindName(new[] { "BaseNav", "ConnectorNav", "PinNav", "LineNav", "ColorNav" }[index]);
                    typeof(ThtEditorPage).GetMethod("Nav_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [nav, new RoutedEventArgs()]);
                    Assert(nav.IsChecked == true, "The original left navigation highlights the active page");
                    page.Measure(new Size(1366, 768)); page.Arrange(new Rect(0, 0, 1366, 768)); page.UpdateLayout();
                    Assert(((ContentControl)page.FindName("ContentHost")).Content is FrameworkElement { ActualWidth: > 0 },
                        "All five editor pages load their own resources inside the existing window");
                    Assert(!page.HasUnsavedChanges, $"Unedited page {index} cannot trigger a false Save prompt");
                }
                Assert(await page.ConfirmCloseAsync(), "Switching unchanged editor pages does not create false unsaved changes");
            });
        }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-IntegratedEditor-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, true);
        }
    }

    private static void RenderIntegratedEditorPreview(string path, string imagePath, int pageIndex = 3)
    {
        OnSettingsDispatcher(async () =>
        {
            var page = new ThtEditorPage(); await page.OpenPathAsync(path);
            typeof(ThtEditorPage).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [pageIndex]);
            using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("THT editor preview")
                { Width = 1600, Height = 900, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = page;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            page.Measure(new Size(1600, 900)); page.Arrange(new Rect(0, 0, 1600, 900)); page.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(page);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(imagePath))!);
            using var stream = File.Create(imagePath); encoder.Save(stream);
        });
    }
}
