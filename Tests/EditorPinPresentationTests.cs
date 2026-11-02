using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestEditorPinPresentation()
    {
        OnSettingsDispatcher(async () =>
        {
            List<Pin> pins =
            [
                new() { Io = 4, Connector = "1", PinNo = 4, Line = "WIRE", Coupling = [175, 14] },
                new() { Io = 175, Connector = "5", PinNo = 3, Line = "WIRE", MainIo = 4 },
                new() { Io = 14, Connector = "CN2", PinNo = 6, Line = "WIRE", MainIo = 4 },
                new() { Io = 200, Connector = "6", PinNo = 1 },
                new() { Io = 196, Connector = "CLIP", PinNo = 1, Line = "AO", TestNo = "AO", Coupling = [197] },
                new() { Io = 197, Connector = "CLIP", PinNo = 2, Line = "a1", TestNo = "a1", MainIo = 196 }
            ];
            List<List<string>> rawRows = [["", "", "197", "", "", "", "", ""]];
            string original = JsonSerializer.Serialize(pins.OrderBy(pin => pin.Io));
            var page = new PinPage();
            page.LoadData(pins, rawRows);
            var source = page.Rows.Single(row => row.Io == 4);
            var target = page.Rows.Single(row => row.Io == 175);
            var isolated = page.Rows.Single(row => row.Io == 200);
            var unnamed = page.Rows.Single(row => row.Io == 197);
            Assert(source.MainContactDisplay == "—" && source.Role == "CHÂN CHÍNH" &&
                   source.CouplingDisplay == "IO175 · CN5 · PN3" + Environment.NewLine + "IO14 · CN2 · PN6" &&
                   target.MainContactDisplay == "IO4 · CN1 · PN4" && target.Role == "CHÂN NỐI VỀ" &&
                   isolated.Role == "CHƯA CÓ LIÊN KẾT",
                "Editor resolves source/target contacts to IO, Connector and Pin without exposing sentinel -1");
            Assert(unnamed.TestNo == "" && unnamed.MainContactDisplay == "IO196 · CLIP · AO" &&
                   page.Rows.Single(row => row.Io == 196).CouplingDisplay == "IO197 · CLIP · PIN2" &&
                   JsonSerializer.Serialize(page.ReadData()) == original &&
                   JsonSerializer.Serialize(pins.OrderBy(pin => pin.Io)) == original,
                "Native blank PIN TYPE is displayed blank without changing the original editable model or CLIP metadata");
            var grid = (DataGrid)page.FindName("MainGrid");
            Assert(grid.Columns.Select(column => column.Header.ToString()).SequenceEqual(
                ["I/O", "CONNECTOR", "PIN", "LOẠI CHÂN (PIN TYPE)", "NỐI VỀ CHÂN CHÍNH", "VAI TRÒ", "CÁC CHÂN CẦN NỐI"]) &&
                grid.Columns[4] is DataGridTemplateColumn && grid.Columns[6] is DataGridTemplateColumn,
                "Contact labels replace existing columns while retaining their edit/synchronization positions");

            var editor = (TextBox)((DataGridTemplateColumn)grid.Columns[6]).CellEditingTemplate.LoadContent();
            editor.DataContext = source;
            await System.Windows.Threading.Dispatcher.Yield();
            Assert(editor.Text == "175/14", "Editing displays original numeric IO syntax rather than formatted contact labels");
            editor.Text = "14";
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            typeof(PinPage).GetMethod("SynchronizeConnections", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, [source, 6]);
            Assert(source.CouplingText == "14" && page.Rows.Single(row => row.Io == 14).MainIo == 4 &&
                   target.MainIo == -1 && target.Role == "CHƯA CÓ LIÊN KẾT",
                "Numeric connection editing retains reciprocal synchronization and updates displayed roles");
            source.Io = 7;
            typeof(PinPage).GetMethod("SynchronizeIoChange", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, [source]);
            Assert(page.Rows.Single(row => row.Io == 14).MainContactDisplay == "IO7 · CN1 · PN4",
                "Renumbering IO refreshes formatted references instead of retaining stale labels");
            source.CouplingText = "999";
            Assert(source.CouplingDisplay == "IO999 (chưa xác định chân)", "Unknown IO must remain visible without guessing a connector");
            page.LoadData(pins, rawRows);
            Assert(JsonSerializer.Serialize(page.ReadData()) == original, "Reloading the page cannot retain presentation edits or stale contact mappings");
        });
    }
}
