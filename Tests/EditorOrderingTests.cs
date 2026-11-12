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
    private static void TestEditorConnectorPinOrdering()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-EditorOrder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc = new ModelDocument
            {
                Base = new() { Model = "UNORDERED", Number = "UNORDERED", Name = "ORDER TEST" },
                Connectors = [new() { Name = "10", StartIo = 1, PinCount = 2 },
                    new() { Name = "2", StartIo = 200, PinCount = 2 }, new() { Name = "1", StartIo = 100, PinCount = 2 }],
                Pins = [new() { Connector = "10", PinNo = 5, Io = 1, Line = "WIRE" },
                    new() { Connector = "10", PinNo = 3, Io = 2 }, new() { Connector = "2", PinNo = 7, Io = 200 },
                    new() { Connector = "2", PinNo = 4, Io = 201 }, new() { Connector = "1", PinNo = 10, Io = 100, Line = "WIRE" },
                    new() { Connector = "1", PinNo = 2, Io = 101 }]
            };
            ModelLogic.RebuildConnections(doc.Pins);
            Assert(EditorDisplayOrder.Pins(doc.Pins).Select(pin => (pin.Connector, pin.PinNo, pin.Io)).SequenceEqual(
                [("1", 2, 101), ("1", 10, 100), ("2", 4, 201), ("2", 7, 200), ("10", 3, 2), ("10", 5, 1)]),
                "Editor uses connector/physical-PIN numeric order independently of IO and native row sequence");
            var naturalNames = new[] { "_Interlock", "CN10", "10", "CLIP", "CN2", "2", "1" }
                .Select(name => new Connector { Name = name, PinCount = 1, StartIo = 1 });
            Assert(EditorDisplayOrder.Connectors(naturalNames).Select(connector => connector.Name)
                .SequenceEqual(["1", "2", "10", "CN2", "CN10", "CLIP", "_Interlock"]),
                "Connector natural sorting handles numeric gaps, alphanumeric names and special groups");
            string path = Path.Combine(root, "UNORDERED.tht"); IntegratedThtStore.Save(doc, path, null);
            TestEditorOrderedNativeCopy(path);
            var tracker = new DocumentChangeTracker();
            var aliases = new ModelDocument { Pins = [new() { Io = 20, Connector = "10", PinNo = 5 },
                new() { Io = 20, Connector = "2", PinNo = 7 }] };
            tracker.MarkSaved(aliases); aliases.Pins.Reverse();
            Assert(!tracker.HasChanges(aliases), "Sorting IO aliases for display does not create an unsaved-change prompt");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestEditorOrderedNativeCopy(string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-NativeEditorOrder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original = File.ReadAllBytes(source);
        try
        {
            string path = Path.Combine(root, Path.GetFileName(source)); File.WriteAllBytes(path, original);
            var originalRuntime = new ThtModelParser().Load(path);
            var loaded = ThtFileService.Load(path);
            Assert(loaded.Connectors.Select(connector => connector.Name).SequenceEqual(
                EditorDisplayOrder.Connectors(loaded.Connectors).Select(connector => connector.Name)),
                "Loaded connector layout is naturally ordered without relabeling connector names");
            OnSettingsDispatcher(async () =>
            {
                var editor = new ThtEditorPage(); await editor.OpenPathAsync(path);
                var host = (ContentControl)editor.FindName("ContentHost");
                var show = typeof(ThtEditorPage).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var commit = typeof(ThtEditorPage).GetMethod("Commit", BindingFlags.Instance | BindingFlags.NonPublic)!;
                foreach (int page in new[] { 1, 2, 3, 0 })
                {
                    commit.Invoke(editor, null); show.Invoke(editor, [page]);
                    if (page == 1)
                        Assert(((ConnectorPage)host.Content).Rows.Select(row => row.Name).SequenceEqual(
                            EditorDisplayOrder.Connectors(loaded.Connectors).Select(connector => connector.Name)),
                            "Connector tab displays natural connector order");
                    if (page == 2)
                        Assert(((PinPage)host.Content).Rows.Select(row => (row.Connector, row.PinNo, row.Io)).SequenceEqual(
                            EditorDisplayOrder.Pins(loaded.Pins).Select(pin => (pin.Connector, pin.PinNo, pin.Io))),
                            "PIN tab sorts physical PINs within each connector without changing IO");
                    if (page == 3)
                        Assert(((LinePage)host.Content).Rows.Select(row => (row.Connector, row.PinNo, row.Io)).SequenceEqual(
                            EditorDisplayOrder.Pins(loaded.Pins).Select(pin => (pin.Connector, pin.PinNo, pin.Io))),
                            "Wire tab follows the same connector/PIN order");
                    Assert(!editor.HasUnsavedChanges, "Visiting/reordering a native tab does not count as a data edit");
                }
                Assert(await editor.SaveAsync() && File.ReadAllBytes(path).SequenceEqual(original),
                    "Opening unordered native THT and visiting tabs preserves every original byte on no-op Save");
                ((TextBox)((BasePage)host.Content).FindName("NameBox")).Text = "ORDERED EDITOR VERIFIED";
                Assert(await editor.SaveAsync(), "An unordered/sparse native file remains editable and saves in place");
                var saved = new ThtModelParser().Load(path);
                Assert(saved.Pins.SequenceEqual(originalRuntime.Pins) && saved.MaxIo == originalRuntime.MaxIo &&
                    JsonSerializer.Serialize(saved.Clip) == JsonSerializer.Serialize(originalRuntime.Clip),
                    "Sorted editor metadata saves preserve physical product topology and CLIP mapping");
            });
            Assert(File.ReadAllBytes(source).SequenceEqual(original), "Original source file remains unchanged by ordering tests");
        }
        finally { Directory.Delete(root, true); }
    }
}
