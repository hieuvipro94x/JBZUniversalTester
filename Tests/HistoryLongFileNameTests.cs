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
                var expand = typeof(HistoryPage).GetMethod("ExpandFileColumnForRows", BindingFlags.NonPublic | BindingFlags.Instance)!;
                expand.Invoke(page, [new[] { row }]);
                var text = new TextBlock { Style = column.ElementStyle, DataContext = row, FontSize = grid.FontSize };
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var formatted = new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, 1);
                Assert(column.Width.IsAbsolute && column.Width.Value > formatted.WidthIncludingTrailingWhitespace + 12 &&
                       Equals(text.ToolTip, name) && row.ExportModelFileName == name,
                    $"A long filename fits its fixed column and the actual text element exposes the complete filename tooltip: width={column.Width.Value}, text={formatted.WidthIncludingTrailingWhitespace}, tip={text.ToolTip}, name={row.ExportModelFileName}");
                double width = column.Width.Value;
                expand.Invoke(page, [new[] { new TestHistoryRecord { ModelFile = "SHORT.tht" } }]);
                Assert(column.Width.Value == width && grid.RowHeight == 32 && grid.CanUserResizeColumns == false,
                    "Short later pages never shrink the column or disturb fixed row height and scrolling");
            }
            finally { page.ReleasePageResources(); }

        });
    }
}
