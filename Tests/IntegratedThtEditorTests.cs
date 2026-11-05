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
    private static void TestNativeEditorFileCopy(string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-NativeEditorFile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original = File.ReadAllBytes(source);
        try
        {
            string copy = Path.Combine(root, Path.GetFileName(source));
            File.WriteAllBytes(copy, original);
            var doc = ThtFileService.Load(copy);
            var before = new ThtModelParser().Load(copy);
            IntegratedThtStore.Save(doc.Clone(), copy, IntegratedThtStore.Fingerprint(copy));
            Assert(File.ReadAllBytes(copy).SequenceEqual(original), "Unchanged native THT remains byte-exact");
            byte[] contents = ThtFileService.ReadContentsForTest(copy);
            long offset = contents.Length - System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(4));
            doc.Base.Name = "EDITOR VERIFIED";
            IntegratedThtStore.Save(doc, copy, IntegratedThtStore.Fingerprint(copy));
            var after = new ThtModelParser().Load(copy);
            contents = ThtFileService.ReadContentsForTest(copy);
            Assert(after.ProductName == "EDITOR VERIFIED" && before.Pins.SequenceEqual(after.Pins) &&
                   before.DiscardContactIo.SequenceEqual(after.DiscardContactIo) &&
                   contents.Length - System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(contents.AsSpan(4)) == offset,
                "Native file edit retains inner-header convention and product/discard topology");
            Assert(File.ReadAllBytes(source).SequenceEqual(original), "Diagnostic never edits the original THT");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestIntegratedThtEditor()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-IntegratedEditor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "EDIT-A.tht");
            File.Copy(Path.Combine(Environment.CurrentDirectory, "Features", "ModelEditor", "Assets", "harnprog_template_v1110.tht"), path);
            foreach (int offset in new[] { 16, 20, 32, 33, 102, 512 })
            {
                string variant = Path.Combine(root, $"HEADER-{offset}.tht");
                File.Copy(path, variant);
                byte[] variantContents = ThtFileService.ReadContentsForTest(variant);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(variantContents.AsSpan(4),
                    checked((uint)(variantContents.Length - offset)));
                typeof(ThtModelParser).GetMethod("WriteDiscardContents", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [variant, variantContents]);
                TestNativeEditorFileCopy(variant);
            }
            foreach (uint invalidSize in new[] { 0u, uint.MaxValue })
            {
                string variant = Path.Combine(root, $"INVALID-HEADER-{invalidSize}.tht");
                File.Copy(path, variant);
                byte[] contents = ThtFileService.ReadContentsForTest(variant);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(4), invalidSize);
                typeof(ThtModelParser).GetMethod("WriteDiscardContents", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [variant, contents]);
                byte[] originalVariant = File.ReadAllBytes(variant);
                bool refused = false;
                try { ThtFileService.Load(variant); }
                catch (JBZUniversalTester.ModelEditor.Models.ThtFormatException) { refused = true; }
                Assert(refused && File.ReadAllBytes(variant).SequenceEqual(originalVariant),
                    "Impossible inner lengths remain rejected without modifying the source");
            }
            var parser = new ThtModelParser();
            // Legacy files may have special CLIP pins without catalog wire rows.
            // Loading derives those rows for editing; an untouched Save must not
            // insert them into the native file merely because it was opened.
            string sparsePath = Path.Combine(root, "SPARSE-CLIP.tht");
            string sparseText = "검사\t파트번호\tＥＣＯ\tＡＬＣ\t파트명\n\tSPARSE\t\t\tPRODUCT\n\n" +
                "번 호\t커넥터\t핀 수\n1\tCN1\t1\n2\tCN2\t1\n\n" +
                "커넥터\t선이름\tＩ／Ｏ\t핀번호\t핀종류\t핀차수\t핀옵션\t비 고\n" +
                "CN1\tW01\t1\t1\t\t\t\t\nCN2\tW01\t2\t1\t\t\t\t\n\t\t3\t\tAO\t\t\t\n\t\t4\t\ta1\t\t\t\n\n" +
                "선이름\t선연결\t굵기\t색깔\nW01\t\t0.5\tR\n";
            byte[] sparse = BuildMinimalThtFile(sparseText);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(sparse.AsSpan(1024 + 76), 1);
            using (var body = new MemoryStream())
            {
                using (var writer = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(4096u - 8); writer.Write(4096u - 102);
                    writer.Write(0u); writer.Write(ThtFileService.ThtToken);
                    foreach (byte[] raw in new[] { System.Text.Encoding.ASCII.GetBytes(ThtFileService.ThtVersion),
                                 System.Text.Encoding.GetEncoding(949).GetBytes(sparseText) })
                    {
                        if (raw.Length < 255) writer.Write((byte)raw.Length);
                        else { writer.Write((byte)255); writer.Write(checked((ushort)raw.Length)); }
                        writer.Write(raw);
                    }
                }
                body.ToArray().CopyTo(sparse, 1536);
            }
            File.WriteAllBytes(sparsePath, sparse);
            TestNativeEditorFileCopy(sparsePath);
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
                Assert(new[] { "_connectors", "_pins", "_lines", "_colors" }.All(name =>
                        typeof(ThtEditorPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page) is null),
                    "Opening the base editor does not construct unused grid pages");
                var blankBase = (JBZUniversalTester.ModelEditor.Pages.BasePage)((ContentControl)page.FindName("ContentHost")).Content;
                var kindBox = (TextBox)blankBase.FindName("KindBox");
                var kindHint = (TextBlock)blankBase.FindName("KindHint");
                Assert(kindBox.Text == "" && kindHint.Text == "DL3 PE" && kindHint.Visibility == Visibility.Visible &&
                       blankBase.ReadData().Kind == "" && blankBase.ReadData().Model == "" && !page.HasUnsavedChanges,
                    "New THT examples are visual hints, never real input or unsaved data");
                kindBox.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice, 0, null, kindBox)
                    { RoutedEvent = System.Windows.Input.Keyboard.GotKeyboardFocusEvent });
                Assert(kindHint.Visibility == Visibility.Collapsed, "Entering an empty field hides its sample hint");
                kindBox.Text = "REAL VEHICLE";
                Assert(kindHint.Visibility == Visibility.Collapsed && blankBase.ReadData().Kind == "REAL VEHICLE",
                    "Typed values are read independently of the hint");
                kindBox.Clear();
                Assert(kindHint.Visibility == Visibility.Visible && blankBase.ReadData().Kind == "" && !page.HasUnsavedChanges,
                    "Deleting input restores the hint immediately without storing the sample");
                await page.OpenPathAsync(path);
                Assert(kindHint.Visibility == Visibility.Collapsed, "Loaded THT fields keep their actual values without new-file hints");
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
                    if (index == 1)
                    {
                        var connectorPage = (FrameworkElement)((ContentControl)page.FindName("ContentHost")).Content;
                        var grid = (DataGrid)connectorPage.FindName("Grid");
                        Assert(grid.Columns.Select(column => column.Header?.ToString()).SequenceEqual(
                            ["STT", "TÊN CONNECTOR", "SỐ PIN", "I/O BẮT ĐẦU"]),
                            "THT editor exposes topology only; Leak enable/channel controls belong exclusively to Settings");
                    }
                    Assert(!page.HasUnsavedChanges, $"Unedited page {index} cannot trigger a false Save prompt");
                }
                Assert(await page.ConfirmCloseAsync(), "Switching unchanged editor pages does not create false unsaved changes");
                var showPage = typeof(ThtEditorPage).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
                showPage.Invoke(page, [3]);
                var lines = (JBZUniversalTester.ModelEditor.Pages.LinePage)((ContentControl)page.FindName("ContentHost")).Content;
                var firstRow = lines.Rows[0];
                int collectionChanges = 0;
                lines.Rows.CollectionChanged += (_, _) => collectionChanges++;
                firstRow.Thickness = firstRow.Thickness == "0.5" ? "0.75" : "0.5";
                Assert(await page.SaveAsync() && !page.HasUnsavedChanges && collectionChanges == 0 &&
                       ReferenceEquals(firstRow, lines.Rows[0]),
                    "Saving a wire edit keeps the row instances and avoids rebuilding the visible grid");
                var savedPin = parser.Load(path).Pins.First(pin => pin.IoNumber == firstRow.Io);
                Assert(savedPin.Section == firstRow.Original.Thickness,
                    "Reused wire rows rebase their original pin data to the actual saved file");
                showPage.Invoke(page, [0]); showPage.Invoke(page, [3]);
                Assert(collectionChanges == 0 && ReferenceEquals(firstRow, lines.Rows[0]) && !page.HasUnsavedChanges,
                    "Returning to the unchanged wire tab reuses rows without a false dirty state");
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
            var page = new ThtEditorPage();
            if (pageIndex >= 0) await page.OpenPathAsync(path);
            typeof(ThtEditorPage).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [Math.Max(0, pageIndex)]);
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
