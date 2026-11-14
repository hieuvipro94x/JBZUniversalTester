using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JBZUniversalTester.Models;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestHistoryLongFileNames()
    {
        OnSettingsDispatcher(async () =>
        {
            var page = new HistoryPage();
            try
            {
                var grid = (DataGrid)page.FindName("HistoryGrid");
                var column = (DataGridTextColumn)grid.Columns.Single(item => Equals(item.Header, "File"));
                string name = "WH321331_CONNECTOR_10_2026_11_12_LONG_PRODUCT_FILE_NAME.tht";
                var row = new TestHistoryRecord { ModelFile = Path.Combine(Path.GetTempPath(), name) };
                double originalWidth = column.Width.Value;
                grid.ItemsSource = new[] { row };
                var text = new TextBlock { Style = column.ElementStyle, DataContext = row, FontSize = grid.FontSize };
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var formatted = new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, 1);
                Assert(column.Width.IsAbsolute && column.Width.Value == originalWidth && originalWidth == 260 &&
                       Equals(text.ToolTip, name) && row.ExportModelFileName == name &&
                       text.TextTrimming == TextTrimming.CharacterEllipsis,
                    "Long filenames retain the full tooltip and data while the fixed column uses ellipsis");
                grid.ItemsSource = new[] { new TestHistoryRecord { ModelFile = "SHORT.tht" } };
                Assert(column.Width.Value == originalWidth && grid.RowHeight == 32 && !grid.CanUserResizeColumns &&
                       grid.GridLinesVisibility == DataGridGridLinesVisibility.All,
                    "Changing pages never changes column width and the table has thin row/column lines");
            }
            finally { page.ReleasePageResources(); }

        });
    }
}
