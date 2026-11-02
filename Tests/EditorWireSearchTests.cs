using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestEditorWireSearch()
    {
        OnSettingsDispatcher(async () =>
        {
            var pins = Enumerable.Range(1, 640).Select(io => new Pin
            {
                Io = io, Connector = ((io - 1) / 32 + 1).ToString(), PinNo = (io - 1) % 32 + 1,
                Line = io is 12 or 620 ? "M8C0" : io is 511 or 512 ? "SHARED-WIRE" : "", Add = io == 600 ? "BRANCH-TARGET" : ""
            }).ToList();
            var page = new LinePage();
            page.LoadData(pins);
            int changes = 0;
            page.Changed += (_, _) => changes++;
            page.ConnectionsChanged += (_, _) => changes++;
            var box = (TextBox)page.FindName("WireSearchBox");
            var grid = (DataGrid)page.FindName("Grid");
            var status = (TextBlock)page.FindName("WireSearchStatus");
            string before = JsonSerializer.Serialize(page.ReadData());
            var originalRows = page.Rows.ToArray();
            void Find(string query)
            {
                box.Text = query;
                typeof(LinePage).GetMethod("FindWire_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(page, [page, new RoutedEventArgs()]);
            }
            int SelectedIo() => ((LineRow)grid.CurrentCell.Item).Io;
            Find("512");
            Assert(SelectedIo() == 512 && status.Text == "1/1 kết quả", "Numeric search jumps to the exact IO in a 640-pin model");
            Find("io 511");
            Assert(SelectedIo() == 511, "Explicit IO prefix is case insensitive");
            Find("shared-wire");
            Assert(SelectedIo() == 511 && status.Text == "1/2 kết quả", "Wire-name search starts at its first match");
            Find("shared-wire");
            Assert(SelectedIo() == 512 && status.Text == "2/2 kết quả", "Find next advances through matching rows");
            Find("shared-wire");
            Assert(SelectedIo() == 511, "Find next wraps without filtering or losing other pins");
            Find("CN16");
            Assert(SelectedIo() == 481 && status.Text == "1/32 kết quả", "Connector search resolves the original connector name");
            Find("PN32");
            Assert(SelectedIo() == 32 && status.Text == "1/20 kết quả", "Pin search finds that pin number across connectors");
            Find("branch-target");
            Assert(SelectedIo() == 600, "Wire coupling/add text is searchable");
            Find("NO-SUCH-WIRE");
            Assert(status.Text == "Không tìm thấy" && SelectedIo() == 600, "A missing match does not change the selected data row");
            typeof(LinePage).GetMethod("ClearWireSearch_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, [page, new RoutedEventArgs()]);
            Assert(box.Text == "" && status.Text == "" && page.Rows.SequenceEqual(originalRows) &&
                   JsonSerializer.Serialize(page.ReadData()) == before && changes == 0 && !page.HasStructuralChanges,
                "Search/clear must preserve all rows, order, values and dirty/structural state");
            box.Text = "M";
            await Task.Delay(60);
            box.Text = "M8";
            await Task.Delay(60);
            box.Text = "M8C0";
            box.CaretIndex = box.Text.Length;
            await Task.Delay(300);
            Assert(SelectedIo() == 12 && box.Text == "M8C0" && box.CaretIndex == 4 && status.Text == "1/2 kết quả",
                "Typing a wire name automatically selects its first match without clearing text or moving the caret");
            Find("M8C0");
            Assert(SelectedIo() == 620 && status.Text == "2/2 kết quả", "Explicit next still advances after automatic search");
            box.Text = "1";
            await Task.Delay(60);
            box.Text = "12";
            await Task.Delay(300);
            Assert(SelectedIo() == 12 && box.Text == "12" && status.Text == "1/1 kết quả",
                "Typing a bare IO number automatically selects the exact IO without a button press");
            page.LoadData(pins);
            await Task.Delay(300);
            Assert(box.Text == "12" && SelectedIo() == 12 && changes == 0 &&
                   JsonSerializer.Serialize(page.ReadData()) == before,
                "Reloading a page preserves the entered query and model data while restarting automatic search");
            page.Measure(new Size(1000, 650)); page.Arrange(new Rect(0, 0, 1000, 650)); page.UpdateLayout();
            Assert(box.Height == 36 && box.Padding.Top == 0 && box.Padding.Bottom == 0 &&
                   box.VerticalContentAlignment == VerticalAlignment.Center && box.ActualHeight >= 36,
                "The search box reserves enough centered height so typed characters are not clipped");
            box.Text = "M8C0";
            typeof(LinePage).GetMethod("ClearWireSearch_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, [page, new RoutedEventArgs()]);
            await Task.Delay(300);
            Assert(box.Text == "" && status.Text == "", "Clear cancels a pending automatic search without restoring stale text/results");
            await System.Windows.Threading.Dispatcher.Yield();
        });
    }
}
