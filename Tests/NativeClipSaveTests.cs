using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using JBZUniversalTester.ModelEditor;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;
using JBZUniversalTester.ModelEditor.Services;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSparseNativeClipSave()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-SparseClipFixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var document = new ModelDocument
            {
                SourceFormat = "tht", Base = new() { Model = "SPARSE", Number = "SPARSE", Name = "NATIVE PRODUCT" },
                Connectors = [new() { Name = "CN1", PinCount = 2, StartIo = 49 },
                    new() { Name = "CLIP", PinCount = 14, StartIo = 76 }],
                Pins = [new() { Io = 49, Connector = "CN1", PinNo = 1, Line = "WIRE-A" },
                    new() { Io = 49, Connector = "CN1", PinNo = 2, Line = "WIRE-B" }]
            };
            document.Pins.AddRange(Enumerable.Range(0, 14).Select(index => new Pin
            {
                Io = index < 12 ? 76 + index : 78 + index,
                Connector = "CLIP", PinNo = index + 1, Line = index == 0 ? "AO" : $"a{index}"
            }));
            string source = Path.Combine(root, "SPARSE.tht");
            IntegratedThtStore.Save(document, source, null);
            VerifyNativeClipSaveCopy(source);
            TestWirePinEditing(source);
        }
        finally { Directory.Delete(root, true); }
    }

    private static void VerifyNativeClipSaveCopy(string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-NativeClipSave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original = File.ReadAllBytes(source);
        try
        {
            string path = Path.Combine(root, Path.GetFileName(source));
            File.WriteAllBytes(path, original);
            var document = ThtFileService.Load(path);
            var originalClip = document.Pins.Where(pin => pin.Connector == "CLIP").Select(pin => (pin.Io, pin.Line)).ToArray();
            Assert(originalClip.Length > 1 && originalClip.Any(pin => pin.Io == 90),
                "Native sparse AO/aN rows are included in the editor");
            Assert(!document.Pins.Any(pin => pin.Connector == "CLIP" && pin.Io is 88 or 89),
                "Loading sparse native rows does not create nonexistent IO88 or IO89");
            string ordinary = JsonSerializer.Serialize(document.Pins.Where(pin => pin.Connector != "CLIP"));
            var runtime = new ThtModelParser().Load(path);
            string runtimeClip = JsonSerializer.Serialize(runtime.Clip);
            var noEdit = IntegratedThtStore.Save(document.Clone(), path, IntegratedThtStore.Fingerprint(path));
            Assert(File.ReadAllBytes(path).SequenceEqual(original), "Opening and saving native data without edits is byte-exact");
            noEdit.Base.Name = "EDITED NATIVE PRODUCT";
            document = IntegratedThtStore.Save(noEdit, path, IntegratedThtStore.Fingerprint(path));
            Assert(ordinary == JsonSerializer.Serialize(document.Pins.Where(pin => pin.Connector != "CLIP")) &&
                   runtimeClip == JsonSerializer.Serialize(new ThtModelParser().Load(path).Clip),
                "Metadata saving preserves native physical IO aliases and runtime CLIP topology");

            var clip = document.Connectors.Single(connector => connector.Name == "CLIP");
            clip.PinCount += 3;
            document.Pins = ModelLogic.RegeneratePins(document.Connectors, document.Pins);
            document = IntegratedThtStore.Save(document, path, IntegratedThtStore.Fingerprint(path));
            Assert(document.Pins.Count(pin => pin.Connector == "CLIP") == originalClip.Length + 3 &&
                   originalClip.All(old => document.Pins.Any(pin => pin.Connector == "CLIP" && pin.Io == old.Io && pin.Line == old.Line)),
                "Increasing CLIP count preserves every native branch and appends new ones");
            Assert(ordinary == JsonSerializer.Serialize(document.Pins.Where(pin => pin.Connector != "CLIP")),
                "Allocating CLIP pins never renumbers existing physical product IO");
            AssertUniqueClipRows(document);
            document.Base.Name = "REPEATED SAVE";
            document = IntegratedThtStore.Save(document, path, IntegratedThtStore.Fingerprint(path));
            AssertUniqueClipRows(document);
            document.Connectors.Single(connector => connector.Name == "CLIP").PinCount = originalClip.Length;
            document.Pins = ModelLogic.RegeneratePins(document.Connectors, document.Pins);
            document = IntegratedThtStore.Save(document, path, IntegratedThtStore.Fingerprint(path));
            Assert(document.Pins.Where(pin => pin.Connector == "CLIP").Select(pin => (pin.Io, pin.Line)).SequenceEqual(originalClip),
                "Shrinking CLIP removes only added rows and restores the native sparse layout");
            document.Pins.Single(pin => pin.Connector == "CLIP" && pin.Io == 91).Io = 89;
            document = IntegratedThtStore.Save(document, path, IntegratedThtStore.Fingerprint(path));
            document.Base.Name = "SAVE AFTER IO MOVE";
            document = IntegratedThtStore.Save(document, path, IntegratedThtStore.Fingerprint(path));
            Assert(document.Pins.Any(pin => pin.Connector == "CLIP" && pin.Io == 89 && pin.Line == "a13"),
                "Moving a branch retains its source row through repeated saves");
            AssertUniqueClipRows(document);
            Assert(Directory.GetFiles(root, "*.tmp.tht").Length == 0 &&
                   Directory.GetFiles(Path.Combine(root, "ThtBackups"), "*.tht").Any(file => File.ReadAllBytes(file).SequenceEqual(original)),
                "Native saves preserve an exact backup and clean staging files");
            Assert(File.ReadAllBytes(source).SequenceEqual(original), "The actual source THT remains untouched");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void AssertUniqueClipRows(ModelDocument document)
    {
        var clipIos = document.Pins.Where(pin => pin.Connector == "CLIP").Select(pin => pin.Io).ToHashSet();
        var rows = document.ThtState!.PinRows.Where(row => row[0] == "" && int.TryParse(row[2], out var io) && clipIos.Contains(io)).ToArray();
        Assert(rows.Length == clipIos.Count && rows.Select(row => row[2]).Distinct().Count() == rows.Length,
            "Each AO/aN address is serialized exactly once");
    }

    private static void TestWirePinEditing(string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-WirePinEdit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "WIRE-PIN.tht");
            File.Copy(source, path);
            OnSettingsDispatcher(async () =>
            {
                var editor = new ThtEditorPage();
                await editor.OpenPathAsync(path);
                ((TextBox)((BasePage)((ContentControl)editor.FindName("ContentHost")).Content).FindName("NameBox")).Text = "UI NATIVE SAVE";
                Assert(await editor.SaveAsync(), "Normal editor Save accepts native files containing IO aliases");
                typeof(ThtEditorPage).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [3]);
                var wires = (LinePage)((ContentControl)editor.FindName("ContentHost")).Content;
                using var host = new HwndSource(new HwndSourceParameters("Wire PIN edit self-test")
                    { Width = 1100, Height = 650, WindowStyle = unchecked((int)0x80000000) });
                host.RootVisual = editor;
                editor.Measure(new Size(1100, 650)); editor.Arrange(new Rect(0, 0, 1100, 650)); editor.UpdateLayout();
                var grid = (DataGrid)wires.FindName("Grid");
                var first = wires.Rows[0];
                int io = first.Io;
                grid.CurrentCell = new DataGridCellInfo(first, grid.Columns[2]);
                grid.ScrollIntoView(first, grid.Columns[2]); grid.UpdateLayout();
                Assert(!grid.Columns[2].IsReadOnly && grid.BeginEdit(), "PIN in the Wire tab opens its numeric editor");
                var input = (TextBox)grid.Columns[2].GetCellContent(first);
                input.Text = "2";
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
                wires.Rows[1].PinNo = 1;
                Assert(first.PinNo == 2 && first.Original.PinNo == 1 && editor.HasUnsavedChanges,
                    "Wire PIN edits track unsaved changes independently from the saved original");
                Assert(await editor.SaveAsync() && ReferenceEquals(first, wires.Rows[0]) && !editor.HasUnsavedChanges,
                    "Saving PIN edits retains the rendered row and clears the dirty state");
                var saved = ThtFileService.Load(path);
                Assert(saved.Pins[0].Io == io && saved.Pins[0].PinNo == 2 && saved.Pins[1].PinNo == 1,
                    "Wire PIN changes persist in the native THT and preserve IO addresses");
                first.PinNo = 0;
                bool refused = false;
                try { ModelLogic.ValidatePinsForSave(saved.Connectors, wires.ReadData()); }
                catch (ValidationException) { refused = true; }
                Assert(refused, "A nonpositive PIN is rejected by normal THT save validation");
                first.PinNo = 2;
                var clipRow = wires.Rows.First(row => row.Connector == "CLIP");
                grid.CurrentCell = new DataGridCellInfo(clipRow, grid.Columns[2]);
                grid.ScrollIntoView(clipRow, grid.Columns[2]); grid.UpdateLayout();
                Assert(!grid.BeginEdit(), "Virtual AO/aN PIN numbers stay protected by their native format");
            });
        }
        finally { Directory.Delete(root, true); }
    }
}
