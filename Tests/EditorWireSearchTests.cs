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
                Line = io is 511 or 512 ? "SHARED-WIRE" : "", Add = io == 600 ? "BRANCH-TARGET" : ""
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
            await System.Windows.Threading.Dispatcher.Yield();
        });
    }
}
