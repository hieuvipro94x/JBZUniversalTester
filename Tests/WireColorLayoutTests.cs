using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestWireColorCellFillsBounds()
    {
        OnSettingsDispatcher(async () =>
        {
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XDocument document = XDocument.Load(System.IO.Path.Combine(Environment.CurrentDirectory, "Views", "TestWindow.xaml"));
            var dictionary = new XElement(ns + "ResourceDictionary",
                new XAttribute("xmlns", ns.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
                new XAttribute(XNamespace.Xmlns + "sys", "clr-namespace:System;assembly=System.Runtime"),
                new XAttribute(XNamespace.Xmlns + "views", "clr-namespace:JBZUniversalTester.Views;assembly=JBZUniversalTester"),
                document.Root!.Element(ns + "Window.Resources")!.Elements());
            foreach (XElement element in dictionary.Descendants().Where(element => element.Name.NamespaceName == "clr-namespace:JBZUniversalTester.Views"))
                element.Name = XName.Get(element.Name.LocalName, "clr-namespace:JBZUniversalTester.Views;assembly=JBZUniversalTester");
            ResourceDictionary resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            string colorStyle = document.Descendants(ns + "DataGridTemplateColumn")
                .First(column => (string?)column.Attribute("Header") == "Màu")
                .Attribute("CellStyle")!.Value.Replace("{StaticResource ", "").TrimEnd('}');
            XElement faultGrid = document.Descendants(ns + "DataGrid").First(element =>
                (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "FaultGrid");
            foreach (double scale in new[] { 1d, 1.25, 1.5, 1.75 })
            foreach (double width in new[] { 1904d, 1536d, 1281d })
            foreach (SolidColorBrush tone in new[] { Brushes.Blue, Brushes.Red, Brushes.Yellow })
            {
                var grid = new DataGrid
                {
                    Resources = resources,
                    Style = (Style)resources["FaultOperationGridStyle"],
                    Visibility = Visibility.Visible,
                    UseLayoutRounding = true,
                    SnapsToDevicePixels = true,
                    ItemsSource = new[] { new { WireColorText = "R", WireColorBrush = tone,
                        RowBackgroundBrush = Brushes.White, RowForegroundBrush = Brushes.Black } }
                };
                foreach (XElement column in faultGrid.Element(ns + "DataGrid.Columns")!.Elements())
                {
                    string styleKey = column.Attribute("CellStyle")!.Value.Replace("{StaticResource ", "").TrimEnd('}');
                    DataGridColumn actual = column.Name.LocalName == "DataGridTemplateColumn"
                        ? new DataGridTemplateColumn { CellStyle = (Style)resources[colorStyle], CellTemplate = (DataTemplate)resources["WireColorCellTemplate"] }
                        : new DataGridTextColumn { CellStyle = (Style)resources[styleKey] };
                    actual.Width = (DataGridLength)new DataGridLengthConverter().ConvertFromInvariantString(column.Attribute("Width")!.Value)!;
                    actual.MinWidth = double.Parse(column.Attribute("MinWidth")!.Value, System.Globalization.CultureInfo.InvariantCulture);
                    grid.Columns.Add(actual);
                }
                using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("WireColorLayoutTest")
                { Width = (int)width, Height = 160, WindowStyle = unchecked((int)0x80000000) });
                source.RootVisual = grid;
                VisualTreeHelper.SetRootDpi(grid, new DpiScale(scale, scale));
                grid.Measure(new Size(width, 160));
                grid.Arrange(new Rect(0, 0, width, 160));
                grid.UpdateLayout();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                grid.UpdateLayout();
                Border color = Descendants(grid).OfType<Border>().First(border => border.Background is SolidColorBrush brush && brush.Color == tone.Color);
                DependencyObject ancestor = color;
                while (ancestor is not DataGridCell) ancestor = VisualTreeHelper.GetParent(ancestor);
                var cell = (DataGridCell)ancestor;
                Point offset = color.TranslatePoint(new Point(), cell);
                Assert(grid.GridLinesVisibility == DataGridGridLinesVisibility.Horizontal &&
                       cell.BorderThickness.Left == 1 && HasNoWireNameRightDivider(grid) &&
                       color.ActualWidth > 80 && Math.Abs(offset.X) < 0.01 && Math.Abs(offset.Y) < 0.01 &&
                       Math.Abs(color.ActualWidth - cell.ActualWidth) <= 2.01,
                    $"Wire color must reach cell edges: width={width}, offset={offset}, color={color.ActualWidth}, cell={cell.ActualWidth}");
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(160 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                image.Render(grid);
                Point origin = cell.TranslatePoint(new Point(0, 3), grid);
                var previous = Descendants(grid).OfType<DataGridCell>().First(item => item.Column.DisplayIndex == 4);
                Point previousRight = previous.TranslatePoint(new Point(previous.ActualWidth, 3), grid);
                int first = (int)Math.Floor(previousRight.X * scale);
                int last = (int)Math.Ceiling(origin.X * scale) + 1;
                for (int x = first; x <= last; x++)
                {
                    byte[] pixel = new byte[4];
                    image.CopyPixels(new Int32Rect(x, (int)Math.Ceiling(origin.Y * scale), 1, 1), pixel, 4, 0);
                    Assert(!(pixel[0] == 255 && pixel[1] == 255 && pixel[2] == 255),
                        $"No white pixel may separate wire-name edge and color: width={width}, dpi={scale}, x={x}, previous={previousRight.X}, color={origin.X}");
                }
            }
        });
    }

    private static bool HasNoWireNameRightDivider(DataGrid grid) =>
        Descendants(grid).OfType<DataGridCell>().First(item => item.Column.DisplayIndex == 4).BorderThickness.Right == 0;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }
}
