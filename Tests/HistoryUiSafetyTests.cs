using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestHistoryExportFailureSafety()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-export-safe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        IEnumerable<TestHistoryRecord> FailingRows()
        {
            yield return new TestHistoryRecord { Started = DateTime.Today, ModelFile = "A.tht" };
            throw new IOException("Simulated database failure after the first export row");
        }
        try
        {
            foreach (bool excel in new[] { false, true })
            {
                string path = Path.Combine(root, excel ? "history.xlsx" : "history.csv");
                byte[] original = [1, 9, 4, 8, 0, 255];
                File.WriteAllBytes(path, original);
                int Export(IEnumerable<TestHistoryRecord> rows) => excel
                    ? HistoryExportService.ExportXlsx(path, rows) : HistoryExportService.ExportCsv(path, rows);
                bool failed = false;
                try { Export(FailingRows()); } catch (IOException) { failed = true; }
                Assert(failed && File.ReadAllBytes(path).SequenceEqual(original),
                    "A mid-stream CSV/XLSX failure preserves the previous destination byte-for-byte");
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    failed = false;
                    try { Export([new TestHistoryRecord { Started = DateTime.Today }]); } catch (IOException) { failed = true; }
                    Assert(failed, "A locked destination is reported as failure without truncating the original");
                }
                Assert(File.ReadAllBytes(path).SequenceEqual(original) && Directory.GetFiles(root, "*.tmp").Length == 0,
                    "Failed exports clean up temporary files and preserve a locked destination");
                Assert(Export([new TestHistoryRecord { Started = DateTime.Today }]) == 1 &&
                       !File.ReadAllBytes(path).SequenceEqual(original),
                    "A completed export replaces the destination and returns the complete row count");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestHistoryFiltersAndExportLifecycle()
    {
        OnSettingsDispatcher(async () =>
        {
            var page = new HistoryPage();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var create = typeof(HistoryPage).GetMethod("CreateSearchCriteria", flags)!;
            var capture = typeof(HistoryPage).GetMethod("CaptureExportCriteria", flags)!;
            var setButtons = typeof(HistoryPage).GetMethod("SetExportButtons", flags)!;
            var lot = (TextBox)page.FindName("LotTextBox");
            var from = (DatePicker)page.FindName("FromDatePicker");
            var to = (DatePicker)page.FindName("ToDatePicker");
            var csv = (Button)page.FindName("CsvExportButton");
            bool Rejects(MethodInfo method)
            {
                try { method.Invoke(page, null); return false; }
                catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { return true; }
            }
            try
            {
                foreach (string invalid in new[] { "abc", "-1", "9223372036854775808", "1.5" })
                {
                    lot.Text = invalid;
                    Assert(Rejects(create), "Invalid PASS filters never silently broaden a history search/export");
                }
                lot.Text = "2001";
                from.SelectedDate = new DateTime(2026, 11, 14);
                to.SelectedDate = from.SelectedDate.Value.AddDays(-1);
                Assert(Rejects(create), "Reversed date filters are rejected before querying");
                to.SelectedDate = from.SelectedDate;
                var criteria = (HistorySearchCriteria)create.Invoke(page, null)!;
                typeof(HistoryPage).GetField("_activeCriteria", flags)!.SetValue(page, criteria);
                setButtons.Invoke(page, null);
                Assert(csv.IsEnabled && Equals(capture.Invoke(page, null), criteria),
                    "Export uses the exact successfully applied filter");
                lot.Text = "2002";
                Assert(Rejects(capture), "Changing a pending filter requires Search before export");
                typeof(HistoryPage).GetField("_exporting", flags)!.SetValue(page, true);
                setButtons.Invoke(page, null);
                Assert(!csv.IsEnabled && !((Button)page.FindName("ExcelExportButton")).IsEnabled,
                    "Both export actions are disabled for an active export");
                var records = (ObservableCollection<TestHistoryRecord>)typeof(HistoryPage).GetField("_records", flags)!.GetValue(page)!;
                for (int i = 0; i < 8; i++) records.Add(new TestHistoryRecord
                {
                    Started = DateTime.Today.AddSeconds(i), ModelFile = "WH322444.tht", PartNumber = "1200020430",
                    Passed = i != 3, Result = i != 3 ? "PASS" : "FAIL", PartName = "BMS EXT", LotNo = 2001 + i
                });
                page.Measure(new Size(1366, 768));
                page.Arrange(new Rect(0, 0, 1366, 768));
                page.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1366, 768, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(page);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(Path.GetTempPath(), "jbz-v441-history-grid.png"))) encoder.Save(file);
                page.ReleasePageResources();
                await (Task)typeof(HistoryPage).GetMethod("ExportAsync", flags)!.Invoke(page, [true])!;
                typeof(HistoryPage).GetMethod("ShowMessage", flags)!.Invoke(page,
                    ["Must never appear after release", "Audit", MessageBoxImage.Information]);
            }
            finally { page.ReleasePageResources(); }
            await Task.CompletedTask;
        });
    }

    private static void TestAuxiliaryWindowViewport()
    {
        OnSettingsDispatcher(async () =>
        {
            var fit = typeof(MainWindow).Assembly.GetType("JBZUniversalTester.Views.DialogViewport")!
                .GetMethod("ApplyBounds", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (double dpiScale in new[] { 1.0, 1.25, 1.5 })
            {
                foreach (Size desired in new[] { new Size(850,770), new Size(900,680), new Size(1120,720) })
                {
                    var dialog = new Window { Width = desired.Width, Height = desired.Height, MinWidth = 760, MinHeight = 500,
                        Content = new Grid() };
                    var work = new Rect(0, 0, 1366 / dpiScale, 728 / dpiScale);
                    fit.Invoke(null, [dialog, work]);
                    Assert(dialog.Width <= work.Width - 16 && dialog.Height <= work.Height - 16 &&
                           dialog.Left >= work.Left && dialog.Top >= work.Top &&
                           dialog.Left + dialog.Width <= work.Right && dialog.Top + dialog.Height <= work.Bottom,
                        "Auxiliary windows fit the monitor including taskbar at 100/125/150% DPI before first display");
                    dialog.Close();
                }
            }
            await Task.CompletedTask;
        });
    }
    private static void TestCenterResultViewport()
    {
        OnSettingsDispatcher(async () =>
        {
            var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
            var window = new TestWindow(vm, autoStartProduction: false);
            var root = (FrameworkElement)window.Content;
            window.Content = null; // Never invoke production/hardware Loaded callbacks.
            root.Resources.MergedDictionaries.Add(window.Resources);
            var host = (Border)window.FindName("CenterResultHost");
            host.Visibility = Visibility.Visible;
            var box = host.Child as Viewbox;
            Assert(box is not null && box.StretchDirection == StretchDirection.DownOnly,
                "Center messages fit down into their own viewport without enlarging short statuses");
            var text = (TextBlock)box!.Child;
            var header = (FrameworkElement)window.FindName("TestHeaderSurface");
            foreach (int width in new[] { 683, 910, 1024, 1366 })
            {
                header.Width = Math.Max(1344, width - 16);
                foreach (string message in new[] { "ĐƯA HÀNG VÀO THÙNG HÀNG LỖI", "불량 제품을 불량품 보관함에 넣으세요", "PASS" })
                {
                    text.Text = message;
                    text.FontSize = message == "PASS" ? 156 : 64;
                    root.Measure(new Size(width, 600));
                    root.Arrange(new Rect(0, 0, width, 600));
                    root.UpdateLayout();
                    Rect rendered = text.TransformToAncestor(host).TransformBounds(new Rect(text.RenderSize));
                    Assert(rendered.Left >= -0.5 && rendered.Right <= host.ActualWidth + 0.5 &&
                           rendered.Top >= -0.5 && rendered.Bottom <= host.ActualHeight + 0.5 && text.Text == message,
                        "Vietnamese/Korean messages and PASS fit the center without clipping at narrow logical widths");
                    if (width == 1024 && message.StartsWith("ĐƯA"))
                    {
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 600, 96, 96,
                            System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(root);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(Path.GetTempPath(), "jbz-v441-testview-fit.png"));
                        encoder.Save(file);
                    }
                }
            }
            await Task.CompletedTask;
        });
    }

}
