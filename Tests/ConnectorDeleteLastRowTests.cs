using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;
using JBZUniversalTester.ModelEditor.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestConnectorDeleteLastRow()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-ConnectorDelete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            OnSettingsDispatcher(async () =>
            {
                var page = new ConnectorPage();
                var document = new ModelDocument { SourceFormat = "tht", Base = new() { Model = "DELETE-CN", Number = "DELETE-CN" },
                    Connectors = [new() { Name = "A", PinCount = 2, StartIo = 1 },
                        new() { Name = "B", PinCount = 2, StartIo = 3 }, new() { Name = "C", PinCount = 1, StartIo = 5 }] };
                document.Pins = ModelLogic.RegeneratePins(document.Connectors, []);
                page.LoadData(document);
                using var host = new HwndSource(new HwndSourceParameters("Connector deletion self-test")
                    { Width = 1100, Height = 650, WindowStyle = unchecked((int)0x80000000) });
                host.RootVisual = page;
                page.Measure(new Size(1100, 650)); page.Arrange(new Rect(0, 0, 1100, 650)); page.UpdateLayout();
                var grid = (DataGrid)page.FindName("Grid");
                var button = (Button)page.FindName("DeleteConnectorButton");
                grid.CurrentCell = new DataGridCellInfo(page.Rows[0], grid.Columns[1]);
                grid.SelectedCells.Clear(); grid.SelectedCells.Add(grid.CurrentCell);
                grid.CurrentCell = new DataGridCellInfo();
                button.Focus();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(page.Rows.Select(row => row.Name).SequenceEqual(["A", "B"]) &&
                       page.Rows.Select(row => row.Index).SequenceEqual([1, 2]),
                    "Delete always removes the final connector even if the first row was selected");
                Assert(grid.SelectedCells.Count == 1 && ReferenceEquals(grid.SelectedCells[0].Item, page.Rows[1]),
                    "The new last connector is visibly selected after deletion");
                document.Connectors = page.ReadData();
                document.Pins = ModelLogic.RegeneratePins(document.Connectors, document.Pins);
                string path = Path.Combine(root, "DELETE-CN.tht");
                var saved = IntegratedThtStore.Save(document, path, null);
                Assert(saved.Connectors.Select(connector => connector.Name).SequenceEqual(["A", "B"]) &&
                       saved.Pins.All(pin => pin.Connector != "C"),
                    "Saving and reopening THT removes the final connector and its PINs");

                grid.CurrentCell = new DataGridCellInfo(page.Rows[0], grid.Columns[1]);
                grid.ScrollIntoView(page.Rows[0], grid.Columns[1]); grid.UpdateLayout();
                Assert(grid.BeginEdit(), "A retained row can be edited before deleting the last row");
                ((TextBox)grid.Columns[1].GetCellContent(page.Rows[0])).Text = "RENAMED-A";
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(page.Rows.Count == 1 && page.Rows[0].Name == "RENAMED-A",
                    "Deleting the final connector commits edits to a retained row");
                grid.CurrentCell = new DataGridCellInfo(page.Rows[0], grid.Columns[2]);
                grid.ScrollIntoView(page.Rows[0], grid.Columns[2]); grid.UpdateLayout();
                Assert(grid.BeginEdit(), "The final row enters the numeric editor");
                ((TextBox)grid.Columns[2].GetCellContent(page.Rows[0])).Text = "INVALID-PIN-COUNT";
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.DataBind);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(page.Rows.Count == 0 && !button.IsEnabled,
                    "The last connector can be removed despite invalid input in that row");

                page.LoadData(new ModelDocument { Connectors = [new() { Name = "FRESH", PinCount = 1, StartIo = 1 }] });
                Assert(button.IsEnabled, "An existing final row can be deleted without first selecting a cell");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(page.Rows.Count == 0 && !button.IsEnabled, "Repeated deletion stops at an empty table");
            });
        }
        finally { Directory.Delete(root, true); }
    }
}
