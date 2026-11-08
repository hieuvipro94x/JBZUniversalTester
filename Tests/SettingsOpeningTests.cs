using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSettingsOpeningResponsiveness() => TestSettingsOpeningResponsiveness(null);

    private static void TestSettingsOpeningResponsiveness(string? previewDirectory)
    {
        OnSettingsDispatcher(async () =>
        {
            foreach (int width in new[] { 1366, 1920 })
            {
                var vm = new ProductionSettingsViewModel(saveSettings: _ => throw new InvalidOperationException("Opening cannot save settings"));
                var page = new ProductionSettingsPage(null, vm, deferInitialLayout: true);
                Border[] panels = new[] { "IoSettingsPanel", "RelayLeakMainPanel", "LabelSettingsPanel", "ResistanceSettingsPanel" }
                    .Select(name => (Border)page.FindName(name)).ToArray();
                var save = (Button)page.FindName("SaveSettingsButton");
                Assert(panels.All(panel => panel.Visibility == Visibility.Collapsed) && !save.IsEnabled,
                    "First settings frame does not synchronously lay out every editor or allow a premature save");
                var events = new List<string>();
                var batches = new List<double>();
                var descriptor = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Border));
                var handlers = new List<(Border Panel, EventHandler Handler)>();
                foreach (Border panel in panels)
                {
                    EventHandler handler = (_, _) =>
                    {
                        events.Add(panel.Name);
                        long batchStarted = Stopwatch.GetTimestamp();
                        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input,
                            new Action(() => { events.Add("INPUT"); batches.Add(Stopwatch.GetElapsedTime(batchStarted).TotalMilliseconds); }));
                    };
                    descriptor.AddValueChanged(panel, handler);
                    handlers.Add((panel, handler));
                }
                page.Measure(new Size(width, 1000));
                page.Arrange(new Rect(0, 0, width, 1000));
                page.UpdateLayout();
                var clock = Stopwatch.StartNew();
                Task opening = page.PrepareForDisplayAsync();
                Assert(!opening.IsCompleted && ReferenceEquals(opening, page.PrepareForDisplayAsync()),
                    "Opening yields to the dispatcher and duplicate requests share the same initialization");
                await opening;
                foreach (var (panel, handler) in handlers) descriptor.RemoveValueChanged(panel, handler);
                Assert(events.Count == 8 && Enumerable.Range(0, 4).All(index =>
                    events[index * 2] == panels[index].Name && events[index * 2 + 1] == "INPUT"),
                    "Dispatcher services input between every panel layout instead of blocking across the entire settings page");
                Assert(panels.All(panel => panel.Visibility == Visibility.Visible) && save.IsEnabled &&
                    page.FindName("SettingsOpeningOverlay") is null,
                    "Prepared editors are ready for direct navigation without a loading overlay");
                Assert(vm.ResistanceChannels.Count == 10 && ReferenceEquals(page.DataContext, vm),
                    "Deferred layout retains every resistance editor and its original configuration bindings");
                foreach (Size size in new[] { new Size(800, 600), new Size(1024, 768), new Size(1366, 768), new Size(1920, 1080), new Size(800, 600) })
                {
                    for (int pass = 0; pass < 4; pass++)
                    {
                        page.Measure(size); page.Arrange(new Rect(size)); page.UpdateLayout();
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    }
                    var scroll = (ScrollViewer)page.FindName("SettingsScrollViewer");
                    Assert(scroll.ScrollableWidth < 1,
                        "Settings columns fit the viewport without horizontal scrolling at supported widths");
                    var grid = (Grid)page.FindName("UnifiedSettingsGrid");
                    Assert(grid.ColumnDefinitions.Count == (size.Width >= 1800 ? 3 : size.Width >= 1100 ? 2 : 1),
                        $"Settings reflows into readable columns at {size}");
                    var language = (ComboBox)page.FindName("TestLanguageComboBox");
                    foreach (FrameworkElement control in new FrameworkElement[] { panels[0], language, save })
                    {
                        var transform = control.TransformToAncestor(page);
                        Point origin = transform.Transform(new Point());
                        Point unit = transform.Transform(new Point(1, 1));
                        Assert(Math.Abs(unit.X - origin.X - 1) < 0.001 && Math.Abs(unit.Y - origin.Y - 1) < 0.001,
                            $"Settings content and footer retain their original text scale at {size}");
                    }
                    Assert(language.FontSize == 16 && save.FontSize == 16,
                        "Settings editor and save button keep the configured font size");
                    if (size.Height <= 768)
                        Assert(scroll.ScrollableHeight > 0, "Small screens provide scrolling instead of shrinking settings");
                    var resultText = (TextBlock)page.FindName("DiscardLearningResultText");
                    Rect PanelBounds(Border panel) => new(panel.TranslatePoint(new Point(), page),
                        panel.TranslatePoint(new Point(panel.ActualWidth, panel.ActualHeight), page));
                    Rect[] originalBounds = panels.Select(PanelBounds).ToArray();
                    string originalMessage = resultText.Text;
                    foreach (string message in new[]
                    {
                        "ĐÃ HỌC IO 97 / 98 cho 2.tht. Bấm Lưu để ghi vào THT của mã này.",
                        "Bấm Lưu để xóa cặp IO thùng lỗi khỏi 2.tht.",
                        "ĐÃ HỌC IO 639 / 640 cho " + new string('W', 160) + ".tht. Bấm Lưu để ghi vào THT của mã này.",
                        originalMessage
                    })
                    {
                        resultText.Text = message;
                        page.UpdateLayout();
                        Assert(panels.Select(PanelBounds).SequenceEqual(originalBounds),
                            "Learning completion/clear messages cannot resize or rescale any Settings panel after the dialog closes");
                    }
                    foreach (Border panel in panels)
                    {
                        Point top = panel.TranslatePoint(new Point(), page);
                        Point bottom = panel.TranslatePoint(new Point(panel.ActualWidth, panel.ActualHeight), page);
                        Assert(top.X >= 0 && bottom.X <= size.Width + 1,
                            $"{panel.Name} fits the viewport horizontally at {size}");
                        scroll.ScrollToVerticalOffset(scroll.ScrollableHeight);
                        page.UpdateLayout();
                        bottom = panel.TranslatePoint(new Point(panel.ActualWidth, panel.ActualHeight), page);
                        Assert(bottom.Y <= scroll.TranslatePoint(new Point(0, scroll.ActualHeight), page).Y + 1,
                            $"The bottom of {panel.Name} is reachable by scrolling at {size}");
                        scroll.ScrollToTop();
                        page.UpdateLayout();
                    }
                    if (previewDirectory is not null && width == 1920)
                    {
                        System.IO.Directory.CreateDirectory(previewDirectory);
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width, (int)size.Height,
                            96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(page);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var file = System.IO.File.Create(System.IO.Path.Combine(previewDirectory, $"settings-fit-{size.Width}.png"));
                        encoder.Save(file);
                    }
                }
                Console.WriteLine($"SETTINGS_OPEN_TEST width={width} staged_total_ms={clock.Elapsed.TotalMilliseconds:0.###} batches_ms={string.Join("/", batches.Select(value => value.ToString("0.###")))}");
                page.ReleasePageResources();
            }
            var canceledPage = new ProductionSettingsPage(null, new ProductionSettingsViewModel(), deferInitialLayout: true);
            Task canceled = canceledPage.PrepareForDisplayAsync();
            canceledPage.ReleasePageResources();
            await canceled;
            Assert(canceledPage.DataContext is null && !((Button)canceledPage.FindName("SaveSettingsButton")).IsEnabled &&
                ((Border)canceledPage.FindName("ResistanceSettingsPanel")).Visibility == Visibility.Collapsed,
                "Closing during deferred opening cannot revive bindings, enable saving or initialize the remaining panels");
        });
    }
}
