using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using JBZUniversalTester.ModelEditor;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;
using JBZUniversalTester.ModelEditor.Services;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSparseConnectorPins()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-SparseConnector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc = new ModelDocument
            {
                Base = new() { Model = "SPARSE-PIN", Number = "SAME-PART", Name = "SPARSE NATIVE" },
                Connectors = [new() { Name = "1", PinCount = 3, StartIo = 1 }, new() { Name = "10", PinCount = 2, StartIo = 10 }],
                Pins = [new() { Connector = "1", PinNo = 1, Io = 1, Line = "W1" },
                    new() { Connector = "1", PinNo = 3, Io = 3, Line = "W2" }, new() { Connector = "1", PinNo = 5, Io = 5, Line = "W2" },
                    new() { Connector = "10", PinNo = 3, Io = 10, Line = "W1" }, new() { Connector = "10", PinNo = 4, Io = 11 }]
            };
            ModelLogic.RebuildConnections(doc.Pins);
            string path = Path.Combine(root, "SPARSE.tht");
            IntegratedThtStore.Save(doc, path, null);
            TestSparseConnectorNativeCopy(path);
            var loaded = ThtFileService.Load(path);
            Assert(loaded.ThtState!.ConnectorRows.Single(row => row[1] == "1")[2] == "5" &&
                   loaded.ThtState.ConnectorRows.Single(row => row[1] == "10")[2] == "4",
                "Sparse export declares the physical PIN span independently of populated row count");
            var numberEdit = loaded.Clone(); numberEdit.Pins.Single(pin => pin.Io == 10).PinNo = 8;
            string editedPath = Path.Combine(root, "NUMBER-EDIT.tht");
            var edited = IntegratedThtStore.Save(numberEdit, editedPath, null);
            Assert(edited.ThtState!.ConnectorRows.Single(row => row[1] == "10")[2] == "8" &&
                   edited.Pins.Single(pin => pin.Io == 10).PinNo == 8,
                "Changing a physical PIN label extends metadata without changing its IO address");
            var grownLayout = loaded.Connectors.Select(connector => connector.Clone()).ToList();
            grownLayout.Add(new() { Name = "NEW", PinCount = 1, StartIo = 30 });
            var grown = ModelLogic.RegeneratePins(grownLayout, loaded.Pins);
            Assert(loaded.Pins.All(pin => grown.Any(p => p.Io == pin.Io && p.Connector == pin.Connector &&
                p.PinNo == pin.PinNo && p.Line == pin.Line && p.SourceThtRowIndex == pin.SourceThtRowIndex)),
                "Adding another connector preserves every existing sparse PIN, IO, wire and source row");
            ModelLogic.ValidatePinsForSave(grownLayout, grown);
            grownLayout[0].PinCount++;
            grown = ModelLogic.RegeneratePins(grownLayout, grown);
            Assert(grown.Any(pin => pin.Connector == "1" && pin.PinNo == 2) &&
                   grown.Any(pin => pin.Connector == "1" && pin.PinNo == 5 && pin.Io == 5),
                "Explicit growth fills a vacant physical PIN without dropping the highest existing PIN");
            ModelLogic.ValidatePinsForSave(grownLayout, grown);
            var swapped = loaded.Connectors.Select(connector => connector.Clone()).ToList();
            swapped[0].Name = "10"; swapped[1].Name = "1";
            var renamed = ModelLogic.RegeneratePins(swapped, loaded.Pins,
                new Dictionary<string, string> { ["10"] = "1", ["1"] = "10" });
            Assert(renamed.Single(pin => pin.Io == 5).Connector == "10" && renamed.Single(pin => pin.Io == 5).PinNo == 5 &&
                   renamed.Single(pin => pin.Io == 10).Connector == "1" && renamed.Single(pin => pin.Io == 10).PinNo == 3,
                "Connector renames/swaps retain the original physical PIN identities");
            ModelLogic.ValidatePinsForSave(swapped, renamed);
            var structural = loaded.Pins.Select(pin => pin.Clone()).ToList();
            ModelLogic.InsertPinAfter(structural, 0);
            Assert(loaded.Pins.All(pin => structural.Any(p => p.Io == pin.Io && p.PinNo == pin.PinNo && p.SourceThtRowIndex == pin.SourceThtRowIndex)),
                "Inserting a native wire row cannot renumber other physical PINs or IO");
            ModelLogic.DeletePinAt(structural, 1);
            Assert(JsonSerializer.Serialize(structural) == JsonSerializer.Serialize(loaded.Pins),
                "Deleting the added native row restores the exact sparse layout");
            structural[0].PinNo = 0;
            bool rejected = false;
            try { ModelLogic.ValidatePinsForEditing(loaded.Connectors, structural); } catch (ValidationException) { rejected = true; }
            Assert(rejected, "Sparse support still rejects nonpositive physical PIN numbers");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestSparseConnectorNativeCopy(string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-SparseConnectorCopy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original = File.ReadAllBytes(source);
        try
        {
            string copy = Path.Combine(root, Path.GetFileName(source)); File.WriteAllBytes(copy, original);
            var parser = new ThtModelParser(); var before = parser.Load(copy);
            var originalTables = ThtFileService.Load(copy).ThtState!;
            OnSettingsDispatcher(async () =>
            {
                var editor = new ThtEditorPage(); await editor.OpenPathAsync(copy);
                Assert(!editor.HasUnsavedChanges && await editor.SaveAsync() && File.ReadAllBytes(copy).SequenceEqual(original),
                    "Actual editor opens sparse native THT and no-op Save remains byte-exact");
                var host = (ContentControl)editor.FindName("ContentHost");
                ((TextBox)((BasePage)host.Content).FindName("NameBox")).Text = "SPARSE UI VERIFIED";
                Assert(await editor.SaveAsync(), "Actual editor saves metadata on sparse native THT");
                var after = parser.Load(copy);
                var savedTables = ThtFileService.Load(copy).ThtState!;
                Assert(JsonSerializer.Serialize(originalTables.PinRows) == JsonSerializer.Serialize(savedTables.PinRows) &&
                       JsonSerializer.Serialize(originalTables.ConnectorRows) == JsonSerializer.Serialize(savedTables.ConnectorRows) &&
                       JsonSerializer.Serialize(originalTables.WireRows) == JsonSerializer.Serialize(savedTables.WireRows.Take(originalTables.WireRows.Count)),
                    "Metadata-only edits preserve all original PIN/connector rows and wire-catalog entries");
                Assert(after.ProductName == "SPARSE UI VERIFIED" && before.Pins.SequenceEqual(after.Pins) &&
                       JsonSerializer.Serialize(before.Clip) == JsonSerializer.Serialize(after.Clip) &&
                       before.MaxIo == after.MaxIo,
                    "Metadata edits preserve runtime physical PIN/IO, CLIP and topology");
                var show = typeof(ThtEditorPage).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
                show.Invoke(editor, [1]); var connectors = (ConnectorPage)host.Content;
                Assert(!editor.HasUnsavedChanges, "Visiting sparse connector settings never triggers regeneration or a dirty prompt");
                connectors.Rows[0].Name = "RENAMED-SPARSE";
                Assert(await editor.SaveAsync(), "Actual editor renames a sparse connector and saves in place");
                var renamed = parser.Load(copy);
                Assert(renamed.Pins.Select(pin => (pin.IoNumber, pin.PinNumber, pin.WireName)).SequenceEqual(
                       before.Pins.Select(pin => (pin.IoNumber, pin.PinNumber, pin.WireName))),
                    "Connector-tab regeneration never inserts ghost PINs or renumbers sparse product IO");
                Assert(File.ReadAllBytes(source).SequenceEqual(original), "Original native source remains untouched");
            });
        }
        finally { Directory.Delete(root, true); }
    }
}
