using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;
using JBZUniversalTester.ModelEditor.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestConnectorAutomaticStartIo()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-ConnectorAutoIo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            OnSettingsDispatcher(async () =>
            {
                var page = new ConnectorPage();
                page.LoadData(new ModelDocument());
                using var host = new HwndSource(new HwndSourceParameters("Connector auto IO self-test")
                {
                    Width = 1100, Height = 650, WindowStyle = unchecked((int)0x80000000)
                });
                host.RootVisual = page;
                page.Measure(new Size(1100, 650));
                page.Arrange(new Rect(0, 0, 1100, 650));
                page.UpdateLayout();
                var grid = (DataGrid)page.FindName("Grid");
                var insert = typeof(ConnectorPage).GetMethod("Insert_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var delete = typeof(ConnectorPage).GetMethod("Delete_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
                insert.Invoke(page, [page, new RoutedEventArgs()]);
                insert.Invoke(page, [page, new RoutedEventArgs()]);
                insert.Invoke(page, [page, new RoutedEventArgs()]);

                // Exercise the SỐ PIN binding while the cell is still being edited.
                grid.CurrentCell = new DataGridCellInfo(page.Rows[0], grid.Columns[2]);
                grid.ScrollIntoView(page.Rows[0], grid.Columns[2]);
                grid.UpdateLayout();
                Assert(grid.BeginEdit(), "SỐ PIN enters its normal DataGrid editor");
                var input = (TextBox)grid.Columns[2].GetCellContent(page.Rows[0]);
                input.Text = "4";
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                Assert(page.Rows.Select(row => row.StartIo).SequenceEqual([1, 5, 6]),
                    "Typing a pin count immediately fills following IO starts before leaving the cell");
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                page.Rows[1].PinCount = 3;
                page.Rows[2].PinCount = 2;
                Assert(page.Rows.Select(row => row.StartIo).SequenceEqual([1, 5, 8]),
                    "Automatic IO ranges follow the final PIN counts");

                var connectors = page.ReadData();
                var document = new ModelDocument
                {
                    SourceFormat = "tht",
                    Base = new() { Model = "AUTO-IO", Number = "AUTO-IO", Name = "AUTO IO TEST" },
                    Connectors = connectors,
                    Pins = ModelLogic.RegeneratePins(connectors, [])
                };
                string path = Path.Combine(root, "AUTO-IO.tht");
                IntegratedThtStore.Save(document, path, null);
                var loaded = ThtFileService.Load(path);
                Assert(loaded.Connectors.Select(connector => connector.StartIo).SequenceEqual([1, 5, 8]) &&
                       loaded.Connectors.Select(connector => connector.PinCount).SequenceEqual([4, 3, 2]) &&
                       loaded.Pins.Select(pin => pin.Io).SequenceEqual(Enumerable.Range(1, 9)),
                    "Saved THT reopens with the automatically filled addresses and all nine PINs");

                page.Rows[2].StartIo = 30;
                grid.CurrentCell = new DataGridCellInfo(page.Rows[2], grid.Columns[1]);
                insert.Invoke(page, [page, new RoutedEventArgs()]);
                page.Rows[0].PinCount = 6;
                Assert(page.Rows.Select(row => row.StartIo).SequenceEqual([1, 7, 30, 32]),
                    "An explicit IO anchor is retained and new rows continue after it");
                page.Rows[1].StartIo = 20;
                page.Rows[0].PinCount = 10;
                Assert(page.Rows[1].StartIo == 20 && page.Rows[2].StartIo == 30,
                    "Manual ranges remain anchored during later PIN edits");

                var oldRow = page.Rows[0];
                int changes = 0;
                page.Changed += (_, _) => changes++;
                page.LoadData(new ModelDocument { Connectors =
                [
                    new() { Name = "1", PinCount = 2, StartIo = 1 },
                    new() { Name = "CLIP", PinCount = 2, StartIo = 201 },
                    new() { Name = "2", PinCount = 1, StartIo = 203 }
                ] });
                Assert(changes == 0 && page.ReadData().Select(connector => connector.StartIo).SequenceEqual([1, 201, 203]),
                    "Opening an existing noncontiguous layout retains its actual IO addresses without edits");
                oldRow.PinCount++;
                Assert(changes == 0, "Rows from the previous document cannot change the active editor");
                page.Rows[0].PinCount = 4;
                Assert(changes == 1 && page.Rows.Select(row => row.StartIo).SequenceEqual([1, 201, 203]),
                    "Reserved CLIP ranges remain stable with one change notification");

                page.LoadData(new ModelDocument { Connectors =
                [
                    new() { Name = "A", PinCount = 4, StartIo = 1 },
                    new() { Name = "B", PinCount = 2, StartIo = 5 }
                ] });
                grid.CurrentCell = new DataGridCellInfo(page.Rows[0], grid.Columns[1]);
                insert.Invoke(page, [page, new RoutedEventArgs()]);
                page.Rows[1].Name = "INSERTED";
                page.Rows[1].PinCount = 3;
                Assert(page.Rows.Select(row => row.StartIo).SequenceEqual([1, 5, 8]),
                    "Inserting between connectors updates the following automatic range");
                grid.CurrentCell = new DataGridCellInfo(page.Rows[1], grid.Columns[1]);
                delete.Invoke(page, [page, new RoutedEventArgs()]);
                Assert(page.Rows.Select(row => row.StartIo).SequenceEqual([1, 5]) &&
                       page.Rows.Select(row => row.Name).SequenceEqual(["A", "INSERTED"]),
                    "Deleting from the end preserves the inserted connector and its automatic IO range");

                page.LoadData(new ModelDocument { Connectors =
                [
                    new() { Name = "LAST", PinCount = 1, StartIo = int.MaxValue },
                    new() { Name = "OVERFLOW", PinCount = 1, StartIo = 0 }
                ] });
                page.Rows[0].PinCount = 2;
                bool rejected = false;
                try { ModelLogic.ValidateConnectors(page.ReadData()); }
                catch (ValidationException) { rejected = true; }
                Assert(rejected && page.Rows[1].StartIo == 0,
                    "An overflowing automatic address is rejected instead of wrapping into a valid IO");
            });
        }
        finally { Directory.Delete(root, true); }
    }
}
