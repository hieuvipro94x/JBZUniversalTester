using JBZUniversalTester.Services;
using JBZUniversalTester.Views;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestProductPickerDebouncedSearch()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-PickerTyping-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int i = 0; i < 1000; i++)
                File.WriteAllText(Path.Combine(root, $"WH{i:000000}.tht"), string.Empty);
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                ProductPickerWindow? window = null;
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                int ticks = 0, stage = 0, resets = 0;
                try
                {
                    window = new ProductPickerWindow(root)
                    { Left = -20000, Top = -20000, ShowActivated = false };
                    var grid = (DataGrid)window.FindName("ProductGrid");
                    var search = (TextBox)window.FindName("SearchBox");
                    var select = (Button)window.FindName("SelectButton");
                    var refresh = (Button)window.FindName("RefreshButton");
                    timer.Tick += (_, _) =>
                    {
                        try
                        {
                            Assert(++ticks < 300, "Picker search fixture finishes without hanging");
                            if (stage == 0 && grid.Items.Count == 1000 && refresh.IsEnabled)
                            {
                                ((INotifyCollectionChanged)grid.Items).CollectionChanged += (_, e) =>
                                { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
                                search.Text = "2";
                                search.Text = "28";
                                search.Text = "285";
                                Assert(resets == 0 && grid.Items.Count == 1000 && !select.IsEnabled,
                                    "Rapid typing does not rebuild the table per key or permit a stale selection");
                                stage = 1;
                            }
                            else if (stage == 1 && grid.Items.Count == 1 && select.IsEnabled)
                            {
                                Assert(resets == 1 && ((ProductFileEntry)grid.Items[0]).Name == "WH000285",
                                    "Only the final typed query is applied once and finds the correct suffix");
                                search.Text = string.Empty;
                                search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
                                    PresentationSource.FromVisual(window), 0, Key.Down)
                                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                                Assert(grid.Items.Count == 1000 && resets == 2 && select.IsEnabled,
                                    "Keyboard navigation flushes a pending search and retains all 1000 files");
                                search.Text = "DOES-NOT-EXIST";
                                stage = 2;
                            }
                            else if (stage == 2 && grid.Items.Count == 0)
                            {
                                Assert(resets == 3 && !select.IsEnabled,
                                    "No-match query clears selection after a single update");
                                timer.Stop();
                                frame.Continue = false;
                            }
                        }
                        catch (Exception ex)
                        { failure = ex; timer.Stop(); frame.Continue = false; }
                    };
                    window.Show();
                    timer.Start();
                    Dispatcher.PushFrame(frame);
                }
                catch (Exception ex) { failure = ex; }
                finally { timer.Stop(); window?.Close(); }
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert(thread.Join(TimeSpan.FromSeconds(15)), "Picker UI regression thread completes");
            if (failure is not null)
                throw new InvalidOperationException("Debounced picker UI regression failed.", failure);
        }
        finally
        {
            Directory.Delete(root, recursive: true); // This generated fixture only.
        }
    }

    private static void TestRecursiveProductCatalogSearch()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-ITEM-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "MX5", "HALMP"));
        Directory.CreateDirectory(Path.Combine(root, "OTHER"));
        try
        {
            File.WriteAllText(Path.Combine(root, "WH322285.tht"), "");
            File.WriteAllText(Path.Combine(root, "MX5", "285ABC.THT"), "");
            File.WriteAllText(Path.Combine(root, "MX5", "HALMP", "XX285YY.tht"), "");
            File.WriteAllText(Path.Combine(root, "OTHER", "WH322285.tht"), "");
            File.WriteAllText(Path.Combine(root, "OTHER", "UNRELATED.tht"), "");
            File.WriteAllText(Path.Combine(root, "ignore.json"), "");
            ProductFileCatalogResult catalog = ProductFileCatalog.Read(root);
            Assert(catalog.Files.Count == 5 && catalog.Errors.Count == 0,
                "Catalog includes root and every nested folder, including uppercase THT, and excludes other formats");
            Assert(catalog.Files.Select(file => file.Name).SequenceEqual(
                new[] { "285ABC", "UNRELATED", "WH322285", "WH322285", "XX285YY" }),
                "Catalog has a stable alphabetical product-name order across folders");
            ProductFileEntry[] matches = catalog.Files.Where(file => ProductFileCatalog.Matches(file, " 285 ")).ToArray();
            Assert(matches.Length == 4,
                "Typing a partial suffix, prefix or middle finds all matching product files");
            Assert(catalog.Files.Count(file => ProductFileCatalog.Matches(file, "wh322")) == 2 &&
                   catalog.Files.Count(file => ProductFileCatalog.Matches(file, "285YY")) == 1 &&
                   catalog.Files.Count(file => ProductFileCatalog.Matches(file, "")) == 5 &&
                   !catalog.Files.Any(file => ProductFileCatalog.Matches(file, "NO-MATCH")),
                "Search is case-insensitive, trims whitespace, supports arbitrary substrings and has no-match/empty behavior");
            Assert(matches.Where(file => file.Name == "WH322285").Select(file => file.FullPath).Distinct().Count() == 2 &&
                   matches.Any(file => file.Folder == Path.Combine("MX5", "HALMP")),
                "Same-name products remain separate with their exact paths and nested folders");
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            AssertThrows<OperationCanceledException>(() => ProductFileCatalog.Read(root, cancel.Token),
                "Closing the picker can cancel catalog enumeration");
            Assert(ProductFileCatalog.Read(Path.Combine(root, "missing")).Errors.Count == 1,
                "An inaccessible or missing root is reported instead of silently presenting a complete catalog");
        }
        finally
        {
            Directory.Delete(root, recursive: true); // Only this generated temporary fixture.
        }
    }
}
