using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;
using JBZUniversalTester.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestRecentTestedProducts()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-Recent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string database = Path.Combine(root, "history.db");
        var a = new ProductFileEntry("WH322444", "MX5", Path.Combine(root, "MX5", "WH322444.tht"));
        var b = new ProductFileEntry("WH322444", "HALMP", Path.Combine(root, "HALMP", "WH322444.tht"));
        try
        {
            Assert(RecentTestedProductService.Read(database, [a,b]).Count == 0 && !File.Exists(database),
                "Picker does not create a missing history database");
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE Models (Id INTEGER PRIMARY KEY, FilePath TEXT);
                    CREATE TABLE Tests (Id INTEGER PRIMARY KEY, ModelId INTEGER, InspectionType TEXT, ResultAt TEXT);
                    CREATE INDEX IX_Tests_ModelId_ResultAt_Id ON Tests(ModelId,ResultAt DESC,Id DESC);
                    INSERT INTO Models VALUES (1,$A),(2,$B),(3,$A),(4,'missing.tht');
                    INSERT INTO Tests VALUES
                    (1,1,'PRODUCT','2026-10-13T09:00:00'),
                    (2,2,'PRODUCT','2026-10-14T10:00:00'),
                    (3,1,'PRODUCT','2026-10-14T11:00:00'),
                    (4,3,'PRODUCT','2026-10-14T12:00:00'),
                    (5,2,'MASTER_GOOD','2026-10-14T13:00:00'),
                    (6,4,'PRODUCT','2026-10-14T14:00:00');
                    """;
                command.Parameters.AddWithValue("$A", a.FullPath.ToUpperInvariant());
                command.Parameters.AddWithValue("$B", b.FullPath);
                command.ExecuteNonQuery();
            }
            byte[] before = File.ReadAllBytes(database);
            var recent = RecentTestedProductService.Read(database, [a,b]);
            Assert(recent.Count == 2 && recent[0].File == a && recent[1].File == b,
                "Recent picker uses actual product tests, deduplicates paths and preserves same names in separate folders");
            Assert(recent[0].TestedAt.Hour == 12 && recent[1].TestedAt.Hour == 10,
                "Recent picker retains latest production test time and ignores master-only activity");
            Assert(RecentTestedProductService.Read(database, [a,b], limit:1).Single().File == a,
                "Recent list respects its display limit");
            Assert(before.SequenceEqual(File.ReadAllBytes(database)),
                "Reading recent products does not migrate or alter history");
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                var window = new ProductPickerWindow(root);
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        var grid = (DataGrid)window.FindName("ProductGrid");
                        var list = (ListBox)window.FindName("RecentProductsList");
                        var button = (Button)window.FindName("SelectButton");
                        grid.ItemsSource = new[] { a, b };
                        grid.SelectedIndex = 0;
                        list.ItemsSource = recent;
                        ((TextBlock)window.FindName("RecentEmptyText")).Visibility = Visibility.Collapsed;
                        list.SelectedIndex = 1;
                        Assert(grid.SelectedItem is null && button.IsEnabled,
                            "Selecting a recent product clears the main list selection and enables confirmation");
                        window.UpdateLayout();
                        var content = (FrameworkElement)window.Content;
                        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight,
                            96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(content);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var output = File.Create(Path.Combine(Path.GetTempPath(), "jbz-v234-picker.png")))
                            encoder.Save(output);
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(window.SelectedFilePath == b.FullPath,
                            "Recent selection returns the exact file path even when two folders have identical product names");
                    }
                    catch (Exception ex) { failure = ex; window.Close(); }
                }));
                window.ShowDialog();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert(thread.Join(TimeSpan.FromSeconds(15)), "Recent product UI selection finishes");
            if (failure is not null) throw new InvalidOperationException("Recent picker UI failed", failure);
        }
        finally { Directory.Delete(root, recursive:true); }
    }
}
