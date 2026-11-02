using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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
            grid.CurrentCell = new DataGridCellInfo(page.Rows[0], grid.Columns[3]);
            Assert(Press(grid, pinHost, Key.Down), "PIN page handles Down while using an editable column");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[1]), "Down moves to the next PIN row");
            var editor = grid.Columns[3].GetCellContent(page.Rows[1]) as TextBox;
            Assert(editor is not null, "PIN arrow navigation opens the next cell for editing");
            editor!.Text = "KEEP-TYPE";
            editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Press(grid, pinHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[2]) && page.ReadData()[1].TestNo == "KEEP-TYPE",
                "Down commits entered data before leaving the edited PIN cell");
            Press(grid, pinHost, Key.Up);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[1]), "Up returns to the previous PIN row");
            Press(grid, pinHost, Key.Down); Press(grid, pinHost, Key.Down); Press(grid, pinHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[4]),
                "Rapid arrows cannot let a stale deferred editor focus move the current cell backward");
            grid.CommitEdit();
            grid.CurrentCell = new DataGridCellInfo(page.Rows[^1], grid.Columns[3]);
            Press(grid, pinHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(ReferenceEquals(grid.CurrentCell.Item, page.Rows[^1]), "Down at the last row cannot wrap or add a PIN");

            var lines = new LinePage(); lines.LoadData(pins);
            using var lineHost = Host(lines);
            var lineGrid = (DataGrid)lines.FindName("Grid");
            var search = (TextBox)lines.FindName("WireSearchBox");
            search.Text = "12";
            Assert(Press(search, lineHost, Key.Down), "Down leaves the search box and navigates the matching grid");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 13 && search.Text == "12",
                "Down flushes a pending IO search then moves one row without deleting the query");
            await Task.Delay(250);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 13,
                "A pending automatic search cannot jump back after explicit arrow navigation");
            Press(lineGrid, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 14 && lines.Rows.Count == pins.Count && !lines.HasStructuralChanges,
                "Further Down presses navigate Wire rows without structural edits");
            // Click the visible text content, as a real mouse click would route
            // through the cell into the shared grid helper.
            lineGrid.CommitEdit();
            var wireText = lineGrid.Columns[3].GetCellContent(lines.Rows[13]);
            Assert(wireText is not null, "Wire cell is realized for single-click input");
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent };
            wireText!.RaiseEvent(click);
            var wireEditor = lineGrid.Columns[3].GetCellContent(lines.Rows[13]) as TextBox;
            Assert(click.Handled && wireEditor is not null,
                $"One click must open its editor without a second click or F2; handled={click.Handled}, editor={wireEditor is not null}");
            wireEditor!.Text = "WIRE-EDITED";
            wireEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Press(lineGrid, lineHost, Key.Right);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(lineGrid.CurrentCell.Column == lineGrid.Columns[4] && lines.Rows[13].Line == "WIRE-EDITED",
                "Right arrow commits the wire name and opens the Add cell");
            var addEditor = lineGrid.Columns[4].GetCellContent(lines.Rows[13]) as TextBox;
            Assert(addEditor is not null, "The destination Add cell is ready for immediate typing");
            addEditor!.Text = "ADD-EDITED";
            addEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Press(lineGrid, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 15 && lines.Rows[13].Add == "ADD-EDITED",
                "Down retains entered Add data and moves vertically in the same column");
            Press(lineGrid, lineHost, Key.Left);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(lineGrid.CurrentCell.Column == lineGrid.Columns[3], "Left arrow returns to the wire-name column");
            for (int index = 0; index < 40; index++) Press(lineGrid, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 55 && lines.ReadData()[13].Line == "WIRE-EDITED" &&
                   lines.ReadData()[13].Add == "ADD-EDITED" && lines.Rows.Count == pins.Count,
                "Repeated Down navigation reaches virtualized rows without losing prior edits or adding rows");
            lineGrid.CommitEdit();
            var thicknessText = lineGrid.Columns[5].GetCellContent(lines.Rows[54]);
            thicknessText!.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            var thicknessEditor = lineGrid.Columns[5].GetCellContent(lines.Rows[54]) as TextBox;
            Assert(thicknessEditor is not null, "Thickness also supports immediate single-click typing");
            thicknessEditor!.Text = "0.5";
            thicknessEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Press(lineGrid, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(lines.ReadData()[54].Thickness == "0.5", "Thickness input survives arrow navigation");
            lineGrid.CommitEdit();
            var ioText = lineGrid.Columns[0].GetCellContent(lines.Rows[55]);
            ioText!.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            var ioEditor = lineGrid.Columns[0].GetCellContent(lines.Rows[55]) as TextBox;
            Assert(ioEditor is not null, "The IO cell supports single-click numeric entry");
            ioEditor!.Text = "156";
            ioEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Press(lineGrid, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(lines.ReadData()[55].Io == 156 && ((LineRow)lineGrid.CurrentCell.Item).Io == 57,
                "IO entry is committed before moving to the next actual row");
            lineGrid.UpdateLayout();
            lineGrid.Columns[0].GetCellContent(lines.Rows[56]).RaiseEvent(
                new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            var invalidIoEditor = (TextBox)lineGrid.Columns[0].GetCellContent(lines.Rows[56]);
            invalidIoEditor.Text = "INVALID-IO";
            invalidIoEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Press(lineGrid, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 57 && invalidIoEditor.Text == "INVALID-IO",
                "An invalid numeric IO must stay editable instead of losing input during navigation");
            invalidIoEditor.Text = "57";
            invalidIoEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Press(lineGrid, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 58, "Navigation resumes after correcting invalid numeric input");
            lineGrid.CommitEdit();
            lineGrid.CurrentCell = new DataGridCellInfo(lines.Rows[57], lineGrid.Columns[5]);
            Press(lineGrid, lineHost, Key.Right);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var colorContent = lineGrid.Columns[6].GetCellContent(lines.Rows[57]);
            var color = FindCombo(colorContent);
            Assert(color is { IsEditable: true }, "Right into Color materializes an editable ComboBox");
            color!.Text = "R";
            color.GetBindingExpression(ComboBox.TextProperty)!.UpdateSource();
            Press(lineGrid, lineHost, Key.Down);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert(((LineRow)lineGrid.CurrentCell.Item).Io == 59 && lines.Rows[57].Color == "R",
                "Color entry survives Down navigation in the same column");
        });

        static HwndSource Host(FrameworkElement root)
        {
            var host = new HwndSource(new HwndSourceParameters("Editor keyboard self-test")
                { Width = 1100, Height = 650, WindowStyle = unchecked((int)0x80000000) });
            host.RootVisual = root;
            root.Measure(new Size(1100, 650)); root.Arrange(new Rect(0, 0, 1100, 650)); root.UpdateLayout();
            return host;
        }
        static bool Press(UIElement element, HwndSource host, Key key)
        {
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, host, Environment.TickCount, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            element.RaiseEvent(args);
            return args.Handled;
        }
        static ComboBox? FindCombo(DependencyObject root)
        {
            if (root is ComboBox combo) return combo;
            for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            {
                var found = FindCombo(System.Windows.Media.VisualTreeHelper.GetChild(root, index));
                if (found is not null) return found;
            }
            return null;
        }
    }
}
