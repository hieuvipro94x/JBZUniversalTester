using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestEditorArrowNavigation()
    {
        OnSettingsDispatcher(async () =>
        {
            var pins = Enumerable.Range(1, 80).Select(io => new Pin
                { Io = io, Connector = "1", PinNo = io, Line = "WIRE" + io }).ToList();
            var page = new PinPage(); page.LoadData(pins);
            using var pinHost = Host(page);
            var grid = (DataGrid)page.FindName("MainGrid");
            Click(grid, page.Rows[0], 3);
            AssertGray(grid, page.Rows[0], 3);
            Assert(grid.Columns[3].GetCellContent(page.Rows[0]) is TextBlock,
                "Excel-style single click selects PIN cell instead of trapping arrows inside its text editor");
            Press(grid, pinHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[1]), "Down after a mouse click moves to the next PIN row");
            Type(grid, "KEEP-TYPE");
            Assert(grid.Columns[3].GetCellContent(page.Rows[1]) is TextBox, "Typing starts the selected PIN editor immediately");
            Press(grid, pinHost, Key.Down); // Event originates in the actual TextBox, not in the grid.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[2]) && page.ReadData()[1].TestNo == "KEEP-TYPE",
                "Down routed from a clicked/typed editor commits its value and leaves that cell");
            Press(grid, pinHost, Key.Up);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[1]), "Up returns to the previous PIN row");
            Press(grid, pinHost, Key.F2);
            var directEditor = Input(grid)!;
            Assert(directEditor is not null, "F2 explicitly enables character editing");
            Press(grid, pinHost, Key.Left);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[1]), "F2 keeps arrows inside the text instead of moving grid cells");
            directEditor!.Text = "CANCEL-ME";
            directEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Press(grid, pinHost, Key.Escape);
            Assert(page.ReadData()[1].TestNo == "KEEP-TYPE", "Escape restores the value captured before F2 editing");
            for (int index = 0; index < 3; index++) Press(grid, pinHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[4]), "Rapid arrows do not focus stale cells");
            Click(grid, page.Rows[0], 0);
            Press(grid, pinHost, Key.Right);
            Assert(grid.CurrentCell.Column == grid.Columns[1], "Arrow selection includes read-only cells like Excel");
            string connector = page.Rows[0].Connector;
            Type(grid, "MUST-NOT-EDIT");
            Assert(page.Rows[0].Connector == connector, "Selecting read-only Connector never permits data entry");
            Click(grid, page.Rows[^1], 3);
            Press(grid, pinHost, Key.Down);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[^1]), "Down at the last row cannot add or wrap a PIN");

            var lines = new LinePage(); lines.LoadData(pins);
            using var lineHost = Host(lines);
            var lineGrid = (DataGrid)lines.FindName("Grid");
            var search = (TextBox)lines.FindName("WireSearchBox");
            search.Text = "12";
            PressElement(search, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 13 && search.Text == "12", "Down leaves pending search without losing query text");
            await Task.Delay(250);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 13, "Search cannot pull explicit navigation back to the result");
            Click(lineGrid, lines.Rows[13], 3);
            AssertGray(lineGrid, lines.Rows[13], 3);
            Type(lineGrid, "WIRE-EDITED");
            Press(lineGrid, lineHost, Key.Right);
            Assert(lineGrid.CurrentCell.Column == lineGrid.Columns[4] && lines.Rows[13].Line == "WIRE-EDITED",
                "Right from the actual text editor commits the wire name and selects Add");
            Type(lineGrid, "ADD-EDITED");
            Press(lineGrid, lineHost, Key.Enter);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 15 && lines.Rows[13].Add == "ADD-EDITED",
                "Enter commits Add and selects the next row");
            Press(lineGrid, lineHost, Key.Left);
            for (int index = 0; index < 40; index++) Press(lineGrid, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 55 && lines.ReadData()[13].Line == "WIRE-EDITED" &&
                lines.ReadData()[13].Add == "ADD-EDITED" && lines.Rows.Count == pins.Count,
                "Rapid navigation through virtualized rows preserves prior edits and row count");
            Click(lineGrid, lines.Rows[54], 5); Type(lineGrid, "0.5");
            Press(lineGrid, lineHost, Key.Down);
            Assert(lines.Rows[54].Thickness == "0.5", "Thickness typing survives Down");
            Click(lineGrid, lines.Rows[55], 0); Type(lineGrid, "156");
            Press(lineGrid, lineHost, Key.Down);
            Assert(lines.Rows[55].Io == 156 && ((LineRow)lineGrid.CurrentCell.Item).Io == 57, "Numeric IO commits before Down");
            Type(lineGrid, "INVALID-IO"); Press(lineGrid, lineHost, Key.Down);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 57 && Input(lineGrid)?.Text == "INVALID-IO",
                "Invalid numeric input remains in its editor instead of being discarded");
            Press(lineGrid, lineHost, Key.Escape);
            Assert(lines.Rows[56].Io == 57, "Escape restores a valid numeric IO");
            Click(lineGrid, lines.Rows[57], 6); Type(lineGrid, "R");
            Press(lineGrid, lineHost, Key.Down);
            Assert(lines.Rows[57].Color == "R" && ((LineRow)lineGrid.CurrentCell.Item).Io == 59,
                "Color typing and vertical navigation retain the chosen code");
            Click(lineGrid, lines.Rows[0], 6); Press(lineGrid, lineHost, Key.Tab);
            Assert(ReferenceEquals(lineGrid.CurrentCell.Item, lines.Rows[1]) && lineGrid.CurrentCell.Column == lineGrid.Columns[0],
                "Tab skips read-only preview columns and wraps to the next editable row");

            var connectors = new ConnectorPage();
            connectors.LoadData(new ModelDocument { Connectors = [new() { Name = "A", PinCount = 1, StartIo = 1 }, new() { Name = "B", PinCount = 1, StartIo = 2 }] });
            using var connectorHost = Host(connectors);
            var connectorGrid = (DataGrid)connectors.FindName("Grid");
            Click(connectorGrid, connectors.Rows[0], 1); Type(connectorGrid, "CN-EDIT");
            Press(connectorGrid, connectorHost, Key.Down);
            Assert(ReferenceEquals(connectorGrid.CurrentCell.Item, connectors.Rows[1]) && connectors.Rows[0].Name == "CN-EDIT",
                "Connector supports the same click/type/arrow workflow");
            AssertGray(connectorGrid, connectors.Rows[1], 1);

            var colors = new ColorPage(); colors.Colors.Clear();
            colors.Colors.Add(new WireColor { Code = "R", Name = "Red", Value = "#FF0000" });
            colors.Colors.Add(new WireColor { Code = "G", Name = "Green", Value = "#00FF00" });
            using var colorHost = Host(colors);
            var colorGrid = (DataGrid)colors.FindName("Grid");
            Click(colorGrid, colors.Colors[0], 0); Press(colorGrid, colorHost, Key.Down);
            Assert(ReferenceEquals(colorGrid.CurrentCell.Item, colors.Colors[1]) && ReferenceEquals(colorGrid.SelectedItem, colors.Colors[1]),
                "Read-only color table navigation also updates the selected palette row");
            AssertGray(colorGrid, colors.Colors[1], 0);
            Type(colorGrid, "MUST-NOT-EDIT");
            Assert(colors.Colors[1].Code == "G", "Color-table navigation cannot bypass its read-only editing policy");
        });

        static HwndSource Host(FrameworkElement root)
        {
            var host = new HwndSource(new HwndSourceParameters("Editor keyboard self-test")
                { Width = 1100, Height = 650, WindowStyle = unchecked((int)0x80000000) });
            host.RootVisual = root;
            root.Measure(new Size(1100, 650)); root.Arrange(new Rect(0, 0, 1100, 650)); root.UpdateLayout();
            return host;
        }
        static DataGridCell Cell(DataGrid grid, object item, int column)
        {
            grid.ScrollIntoView(item, grid.Columns[column]); grid.UpdateLayout();
            DependencyObject? child = grid.Columns[column].GetCellContent(item);
            while (child is not null && child is not DataGridCell) child = VisualTreeHelper.GetParent(child);
            return (DataGridCell)(child ?? throw new InvalidOperationException("Cell was not realized"));
        }
        static void Click(DataGrid grid, object item, int column)
        {
            Cell(grid, item, column).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
        }
        static void AssertGray(DataGrid grid, object item, int column)
        {
            var cell = Cell(grid, item, column);
            Assert(cell.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(0xE5, 0xE7, 0xEB) &&
                   cell.Foreground is SolidColorBrush foreground && foreground.Color == Colors.Black,
                "Selected cells use light gray and black text consistently across editor pages");
        }
        static TextBox? Input(DataGrid grid) => FindText(grid.CurrentCell.Column.GetCellContent(grid.CurrentCell.Item));
        static TextBox? FindText(DependencyObject? root)
        {
            if (root is null) return null;
            if (root is TextBox box) return box;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var found = FindText(VisualTreeHelper.GetChild(root, index)); if (found is not null) return found;
            }
            return null;
        }
        static void Type(DataGrid grid, string text)
        {
            UIElement source = (UIElement?)Input(grid) ?? Cell(grid, grid.CurrentCell.Item, grid.CurrentCell.Column.DisplayIndex);
            var composition = new TextComposition(InputManager.Current, source, text);
            var preview = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
                { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            source.RaiseEvent(preview);
            if (!preview.Handled) source.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
                { RoutedEvent = TextCompositionManager.TextInputEvent });
        }
        static void Press(DataGrid grid, HwndSource host, Key key)
        {
            UIElement source = (UIElement?)Input(grid) ?? Cell(grid, grid.CurrentCell.Item, grid.CurrentCell.Column.DisplayIndex);
            PressElement(source, host, key);
        }
        static void PressElement(UIElement source, HwndSource host, Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, host, Environment.TickCount, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            source.RaiseEvent(args);
            if (!args.Handled) source.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, host, Environment.TickCount, key)
                { RoutedEvent = Keyboard.KeyDownEvent });
        }
    }
}
