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
        OnSettingsDispatcher(() =>
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
            foreach (double width in new[] { 401d, 402d, 503d })
            {
                var grid = new DataGrid
                {
                    Resources = resources,
                    Style = (Style)resources["PiFaultGridStyle"],
                    UseLayoutRounding = true,
                    SnapsToDevicePixels = true,
                    ItemsSource = new[] { new { WireColorText = "L", WireColorBrush = Brushes.Blue,
                        RowBackgroundBrush = Brushes.White, RowForegroundBrush = Brushes.Black } }
                };
                grid.Columns.Add(new DataGridTextColumn { Header = "Wire", Width = new DataGridLength(width / 2) });
                grid.Columns.Add(new DataGridTemplateColumn { Header = "Color", Width = new DataGridLength(width / 2 - 20),
                    CellStyle = (Style)resources[colorStyle], CellTemplate = (DataTemplate)resources["WireColorCellTemplate"] });
                using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("WireColorLayoutTest")
                { Width = (int)width, Height = 160, WindowStyle = unchecked((int)0x80000000) });
                source.RootVisual = grid;
                grid.Measure(new Size(width, 160));
                grid.Arrange(new Rect(0, 0, width, 160));
                grid.UpdateLayout();
                Border color = Descendants(grid).OfType<Border>().First(border => border.Background is SolidColorBrush brush && brush.Color == Colors.Blue);
                DependencyObject ancestor = color;
                while (ancestor is not DataGridCell) ancestor = VisualTreeHelper.GetParent(ancestor);
                var cell = (DataGridCell)ancestor;
                Point offset = color.TranslatePoint(new Point(), cell);
                Assert(color.ActualWidth > 150 && Math.Abs(offset.X) < 0.01 && Math.Abs(offset.Y) < 0.01 &&
                       Math.Abs(color.ActualWidth - cell.ActualWidth) <= 1.01,
                    $"Wire color must reach cell edges: width={width}, offset={offset}, color={color.ActualWidth}, cell={cell.ActualWidth}");
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, 160, 96, 96, PixelFormats.Pbgra32);
                image.Render(grid);
                Point origin = cell.TranslatePoint(new Point(0, 3), grid);
                byte[] pixel = new byte[4];
                image.CopyPixels(new Int32Rect((int)Math.Ceiling(origin.X), (int)Math.Ceiling(origin.Y), 1, 1), pixel, 4, 0);
                Assert(pixel[0] == 255 && pixel[1] == 0 && pixel[2] == 0 && pixel[3] == 255,
                    $"First pixel inside color cell must be blue, not an empty white inset (width={width})");
            }
            return Task.CompletedTask;
        });
    }

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
