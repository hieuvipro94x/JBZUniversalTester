using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using JBZUniversalTester.ModelEditor;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;
using JBZUniversalTester.Views;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestEditorResponsiveReload()
    {
        OnSettingsDispatcher(async () =>
        {
            var doc = new ModelDocument
            {
                Connectors = [new() { Name = "2", PinCount = 2, StartIo = 100 }],
                Pins = [new() { Io = 100, Connector = "2", PinNo = 3, Line = "A", Color = "R" },
                    new() { Io = 120, Connector = "2", PinNo = 9, Line = "A", Color = "R" }]
            };
            var connectors = new ConnectorPage(); connectors.LoadData(doc);
            var connector = connectors.Rows[0]; int connectorChanges = 0;
            connectors.Rows.CollectionChanged += (_, _) => connectorChanges++;
            connectors.LoadData(doc);
            Assert(ReferenceEquals(connector, connectors.Rows[0]) && connectorChanges == 0,
                "Unchanged connector reload retains row identity without collection reset");
            var pins = new PinPage(); pins.LoadData(doc.Pins);
            var pin = pins.Rows[0]; int pinChanges = 0;
            pins.Rows.CollectionChanged += (_, _) => pinChanges++;
            doc.Pins[0].Color = "L"; doc.Pins[0].Line = "NEW";
            doc.Pins[0].MainIo = 120;
            pins.LoadData(doc.Pins);
            Assert(ReferenceEquals(pin, pins.Rows[0]) && pinChanges == 0 && pin.MainIo == 120 &&
                pins.ReadData()[0].Line == "NEW" && pins.ReadData()[0].Color == "L",
                "PIN reload retains row identity and refreshes hidden wire fields and connections");
            doc.Pins[0].PinNo = 4; pins.LoadData(doc.Pins);
            Assert(!ReferenceEquals(pin, pins.Rows[0]) && pins.Rows[0].PinNo == 4 && pins.Rows[1].Io == 120,
                "Structural PIN edits still refresh rows without filling sparse PIN/IO gaps");
            var lines = new LinePage(); lines.SetColors(JBZUniversalTester.ModelEditor.Services.ColorStore.Defaults); lines.LoadData(doc.Pins);
            using var source = new HwndSource(new HwndSourceParameters("Editor responsiveness regression")
                { Width = 1224, Height = 800, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = lines;
            lines.Measure(new Size(1224, 800)); lines.Arrange(new Rect(0, 0, 1224, 800)); lines.UpdateLayout();
            int CountCombos(DependencyObject root)
            {
                int count = root is ComboBox ? 1 : 0;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) count += CountCombos(VisualTreeHelper.GetChild(root, i));
                return count;
            }
            Assert(CountCombos(lines) == 0, "Inactive wire color cells render text without allocating ComboBox editors");
            var grid = (DataGrid)lines.FindName("Grid");
            grid.CurrentCell = new DataGridCellInfo(lines.Rows[0], grid.Columns[6]);
            grid.Focus(); Assert(grid.BeginEdit(), "Color cell still supports DataGrid editing");
            lines.UpdateLayout();
            Assert(CountCombos(lines) == 1, "Only the actively edited color cell creates a ComboBox");
            ComboBox FindCombo(DependencyObject root)
            {
                if (root is ComboBox combo) return combo;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                {
                    var found = FindCombo(VisualTreeHelper.GetChild(root, i));
                    if (found is not null) return found;
                }
                return null!;
            }
            var colorEditor = FindCombo(lines); colorEditor.Text = "r/w";
            colorEditor.GetBindingExpression(ComboBox.TextProperty)!.UpdateSource();
            Assert(grid.CommitEdit(DataGridEditingUnit.Cell, true) && lines.Rows[0].Color == "R/W",
                "Transient color editor normalizes and commits typed color on cell navigation");
            grid.CommitEdit(DataGridEditingUnit.Row, true); source.RootVisual = null;
            var settings = new ProductionSettingsPage(null, new ProductionSettingsViewModel(saveSettings: _ => { }), deferInitialLayout: true);
            var editor = new ThtEditorPage();
            typeof(ProductionSettingsPage).GetField("_modelEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(settings, editor);
            var host = (ContentControl)settings.FindName("ModelEditorHost"); host.Content = editor;
            source.RootVisual = settings;
            settings.Measure(new Size(1224, 800)); settings.Arrange(new Rect(0, 0, 1224, 800)); settings.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert(((FrameworkElement)settings.FindName("IoSettingsPanel")).Visibility == Visibility.Collapsed,
                "Settings Loaded does not initialize hidden panels while the THT editor is active");
            settings.ReleasePageResources();
            Assert(host.Content is null && ReferenceEquals(editor.DataContext, editor),
                "Settings teardown detaches the editor without rebinding its self-owned visual tree");
            await Task.CompletedTask;
        });
    }
}
