using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JBZUniversalTester.Services;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestMainProductCatalog()
    {
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-MainCatalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "SECOND"));
            Directory.CreateDirectory(Path.Combine(root, "MX5"));
            using var panel = new MainProductCatalog();
            var window = new Window { Content = panel, Width = 550, Height = 650,
                Left = -20000, Top = -20000, ShowActivated = false };
            try
            {
                for (int i = 0; i < 1000; i++) File.WriteAllText(Path.Combine(root, "MX5", $"WH{i:000000}.tht"), string.Empty);
                string duplicate = Path.Combine(root, "SECOND", "WH000999.tht");
                File.WriteAllText(duplicate, string.Empty);
                string? requested = null;
                panel.ProductRequested += path => requested = path;
                window.Show();
                await panel.LoadAsync(root, duplicate, Path.Combine(root, "absent.db"));
                var list = (ListBox)panel.FindName("ProductList");
                var search = (TextBox)panel.FindName("SearchBox");
                var open = (Button)panel.FindName("OpenButton");
                window.UpdateLayout();
                Assert(list.Items.Count == 1001 && ((ProductFileEntry)list.SelectedItem).FullPath == duplicate && requested is null,
                    "Startup exposes the complete catalog and marks the active path without loading a model");
                ScrollViewer? scroll = FindCatalogScroll(list);
                Assert(scroll is not null && scroll.ScrollableHeight > 0,
                    "Main catalog has an independently scrollable viewport for every row");
                search.Text = "SECOND";
                Assert(!open.IsEnabled, "Pending search prevents opening a stale selection");
                await Task.Delay(300);
                Assert(list.Items.Count == 1 && ((ProductFileEntry)list.Items[0]).FullPath == duplicate,
                    "Folder search disambiguates identical product names");
                list.SelectedIndex = 0;
                open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(requested == duplicate, "Explicit open sends the exact selected THT path");
                search.Text = "NO-MATCH";
                await Task.Delay(300);
                Assert(list.Items.Count == 0 && !open.IsEnabled, "No-match query clears and disables selection");
                search.Text = string.Empty;
                await Task.Delay(300);
                list.SelectedIndex = 0;
                window.UpdateLayout();
                string? preview = Environment.GetEnvironmentVariable("JBZ_MAIN_CATALOG_PREVIEW");
                if (!string.IsNullOrWhiteSpace(preview))
                {
                    var bitmap = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(panel);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(preview); png.Save(stream);
                }
                Assert(list.Items.Count == 1001 && !File.Exists(Path.Combine(root, "absent.db")),
                    "Clearing search reaches all rows without creating or migrating a history DB");
            }
            finally
            {
                window.Close();
                panel.Dispose();
                Directory.Delete(root, true); // Only this generated fixture.
            }
        });
    }

    private static ScrollViewer? FindCatalogScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindCatalogScroll(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
}
