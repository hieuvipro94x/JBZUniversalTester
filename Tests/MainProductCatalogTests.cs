using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JBZUniversalTester.Services;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestCatalogAsyncSearch()
    {
        OnSettingsDispatcher(async () =>
        {
            var files = Enumerable.Range(0, 30000).Select(i => new ProductFileEntry(
                $"MODEL{i:00000}", i % 2 == 0 ? "EVEN" : "ODD", Path.Combine(Path.GetTempPath(), $"MODEL{i:00000}.tht"))).ToArray();
            using var panel = new MainProductCatalog((_, _) => new ProductFileCatalogResult(files, []));
            await panel.LoadAsync(Path.GetTempPath(), files[0].FullPath, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".absent.db"));
            var list = (ListBox)panel.FindName("ProductList");
            var search = (TextBox)panel.FindName("SearchBox");
            var open = (Button)panel.FindName("OpenButton");
            string? requested = null;
            panel.ProductRequested += path => requested = path;
            var apply = typeof(MainProductCatalog).GetMethod("ApplySearchAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Task Filter() => (Task)apply.Invoke(panel, [null])!;

            search.Text = "EVEN";
            Task oldSearch = Filter();
            search.Text = "MODEL29999";
            Assert(!open.IsEnabled, "A newer query immediately blocks opening the previous model");
            // Direct routed events/Enter must obey the same pending-search guard.
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(requested is null, "Pending search cannot open a stale selection");
            Task latestSearch = Filter();
            await Task.WhenAll(oldSearch, latestSearch);
            Assert(list.Items.Count == 1 && ReferenceEquals(list.Items[0], files[^1]),
                "Only the newest query is displayed with the original file identity");
            list.SelectedIndex = 0;
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(requested == files[^1].FullPath, "Completed asynchronous search opens the exact selected model");

            search.Text = "EVEN";
            await Filter();
            Assert(list.Items.Count == 15000 && ReferenceEquals(list.Items[0], files[0]) &&
                ReferenceEquals(list.Items[^1], files[^2]), "Background filtering preserves folder matching and catalog order");
            search.Text = "NO-MATCH";
            Task closingSearch = Filter();
            panel.Dispose();
            await closingSearch;
            Assert(list.Items.Count == 15000, "A search finishing after disposal cannot replace the visible result");
        });
    }

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
                var browse = (Button)panel.FindName("BrowseButton");
                var refresh = (Button)panel.FindName("RefreshButton");
                int browseRequests = 0;
                panel.BrowseRequested += () => browseRequests++;
                window.UpdateLayout();
                browse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert(browseRequests == 1 && requested is null &&
                       refresh.TranslatePoint(new Point(), panel).X >=
                       browse.TranslatePoint(new Point(), panel).X + browse.ActualWidth,
                    "Browse sits beside Refresh and requests a file dialog without selecting a catalog item");
                panel.IsEnabled = false;
                Assert(!browse.IsEnabled && !open.IsEnabled,
                    "The catalog safety gate also disables the relocated browse action");
                panel.IsEnabled = true;
                Assert(list.Items.Count == 1001 && ((ProductFileEntry)list.SelectedItem).FullPath == duplicate && requested is null,
                    "Startup exposes the complete catalog and marks the active path without loading a model");
                var selectionFolder = (TextBlock)panel.FindName("SelectionFolderText");
                var selectionState = (TextBlock)panel.FindName("SelectionStateText");
                Assert(selectionFolder.Text.Contains("SECOND") && selectionState.Text.Contains("đang được mở"),
                    "Selected duplicate filename exposes its folder and active model state");
                list.SelectedItem = list.Items.Cast<ProductFileEntry>().First(file => file.FullPath != duplicate);
                Assert(selectionState.Text.Contains("Chưa áp dụng") && requested is null,
                    "Highlighting a different model explicitly remains unapplied until Open");
                list.SelectedItem = list.Items.Cast<ProductFileEntry>().Single(file => file.FullPath == duplicate);
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

    private static void TestCatalogStableGeometry()
    {
        OnSettingsDispatcher(async () =>
        {
            var names = Enumerable.Range(0, 100).Select(i => i < 70 ? $"A{i:000}" : $"ZZ_MA_HANG_RAT_DAI_K32000_22401R_260224_PHAN_DAY_{i:000}").ToArray();
            using var panel = new MainProductCatalog((_, _) => new ProductFileCatalogResult(
                names.Select(name => new ProductFileEntry(name, "", Path.Combine(Path.GetTempPath(), name + ".tht"))).ToArray(), []));
            var declaration = System.Xml.Linq.XDocument.Load(Path.Combine(Environment.CurrentDirectory, "Views", "MainWindow.xaml"))
                .Descendants().Single(x => x.Name.LocalName == "MainProductCatalog");
            panel.HorizontalAlignment = Enum.Parse<HorizontalAlignment>(declaration.Attribute("HorizontalAlignment")!.Value);
            panel.MaxWidth = 420; panel.Margin = new Thickness(16,0,0,0);
            var host = new Grid(); host.Children.Add(panel);
            var window = new Window { Content = host, Width = 470, Height = 720, Left = -20000, Top = -20000, ShowActivated = false };
            try
            {
                window.Show();
                await panel.LoadAsync(Path.GetTempPath(), "", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".absent.db"));
                window.UpdateLayout();
                var list = (ListBox)panel.FindName("ProductList");
                var scroll = FindCatalogScroll(list)!;
                var search = (TextBox)panel.FindName("SearchBox");
                var open = (Button)panel.FindName("OpenButton");
                var status = (TextBlock)panel.FindName("StatusText");
                double width = panel.ActualWidth, height = panel.ActualHeight, listWidth = list.ActualWidth, viewport = scroll.ViewportWidth;
                Point openPosition = open.TranslatePoint(new Point(), panel);
                foreach (bool bottom in new[] { true, false, true, false })
                {
                    if (bottom) scroll.ScrollToEnd(); else scroll.ScrollToHome();
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    window.UpdateLayout();
                    list.SelectedIndex = bottom ? 99 : 0;
                    ((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex))?.Focus();
                    window.UpdateLayout();
                    Assert(Math.Abs(panel.ActualWidth-width) < .1 && Math.Abs(panel.ActualHeight-height) < .1 &&
                           Math.Abs(list.ActualWidth-listWidth) < .1 && Math.Abs(scroll.ViewportWidth-viewport) < .1 &&
                           open.TranslatePoint(new Point(), panel) == openPosition,
                        $"Scrolling/recycling and focus preserve catalog geometry: {width} -> {panel.ActualWidth}, viewport {viewport} -> {scroll.ViewportWidth}");
                }
                search.Text = "NO-MATCH"; await Task.Delay(260); window.UpdateLayout();
                Assert(Math.Abs(scroll.ViewportWidth-viewport) < .1 && open.TranslatePoint(new Point(), panel) == openPosition,
                    "An empty filtered list reserves the scrollbar and status slots without moving controls");
                search.Text = "A0"; await Task.Delay(260); window.UpdateLayout();
                Assert(Math.Abs(panel.ActualWidth-width) < .1 && open.TranslatePoint(new Point(), panel) == openPosition,
                    "Short results cannot shrink the catalog after long filenames have been recycled");
            }
            finally { window.Close(); }
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
