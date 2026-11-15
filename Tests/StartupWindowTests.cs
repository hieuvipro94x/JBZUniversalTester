using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestStartupWindow(string? imagePath = null)
    {
        OnSettingsDispatcher(async () =>
        {
            var splash = new StartupWindow
            {
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000
            };
            bool closed = false;
            splash.Closed += (_, _) => closed = true;
            try
            {
                splash.Show();
                await Dispatcher.Yield(DispatcherPriority.Background);
                var bar = (ProgressBar)splash.FindName("StartupProgressBar");
                var stage = (TextBlock)splash.FindName("StageText");
                var percent = (TextBlock)splash.FindName("ProgressText");
                Assert(((Image)splash.FindName("LogoImage")).Source is BitmapSource { PixelWidth: > 0 },
                    "Startup resolves the embedded JBZ logo from the application assembly");
                Assert(splash.IsVisible && bar.Value == 0 && !bar.IsIndeterminate,
                    "Startup renders before initialization with a determinate stage progress bar");
                splash.Report(80, "Đang nạp mã hàng gần nhất và sản lượng…");
                splash.Report(30, "Đang hoàn tất chuẩn bị dữ liệu…");
                Assert(bar.Value == 80 && percent.Text == "80%",
                    "Startup progress never moves backwards between concurrent preparation steps");
                // V399 moved progress into the right column. Check rendered
                // bounds and wrapping instead of the old full-window width.
                var progressPanel = (FrameworkElement)bar.Parent;
                foreach (string message in new[]
                {
                    "Đang mở màn hình chính…",
                    "Đang nạp mã hàng gần nhất và sản lượng…",
                    "Đang kiểm tra cấu trúc database và chuẩn bị dữ liệu…"
                })
                {
                    splash.Report(98, message);
                    splash.UpdateLayout();
                    Point stageOrigin = stage.TranslatePoint(new Point(), progressPanel);
                    Point percentOrigin = percent.TranslatePoint(new Point(), progressPanel);
                    Point barOrigin = bar.TranslatePoint(new Point(), progressPanel);
                    Assert(stage.ActualWidth > 0 && stage.ActualHeight > 0 &&
                           stage.ActualWidth >= stage.DesiredSize.Width - 1 &&
                           stage.ActualHeight >= stage.DesiredSize.Height - 1 &&
                           stageOrigin.X >= 0 && stageOrigin.Y >= 0 &&
                           stageOrigin.X + stage.ActualWidth <= percentOrigin.X + 1 &&
                           stageOrigin.Y + stage.ActualHeight <= barOrigin.Y + 1 &&
                           percentOrigin.X + percent.ActualWidth <= progressPanel.ActualWidth + 1 &&
                           bar.ActualWidth > 0 && bar.ActualHeight == 6 &&
                           Math.Abs(bar.ActualWidth - progressPanel.ActualWidth) <= 1 &&
                           barOrigin.Y + bar.ActualHeight <= progressPanel.ActualHeight + 1,
                        "Startup status, percent and progress fit the right column without clipping: " + message);
                }
                var viewport = (Viewbox)splash.Content;
                var design = (Border)splash.FindName("RootBorder");
                var title = (FrameworkElement)splash.FindName("TitlePanel");
                var subtitle = (TextBlock)splash.FindName("SubtitleText");
                var version = (TextBlock)splash.FindName("VersionText");
                foreach (Size size in new[] { new Size(720, 400), new Size(630, 350), new Size(540, 300), new Size(450, 250) })
                {
                    splash.Width = size.Width; splash.Height = size.Height;
                    splash.UpdateLayout();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    splash.UpdateLayout();
                    foreach (FrameworkElement element in new FrameworkElement[] { design, title, subtitle, stage, percent, bar, version })
                    {
                        Rect bounds = element.TransformToAncestor(viewport).TransformBounds(
                            new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                        Assert(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -1 && bounds.Top >= -1 &&
                               bounds.Right <= viewport.ActualWidth + 1 && bounds.Bottom <= viewport.ActualHeight + 1,
                            $"Splash content remains inside the window at {size}: {element.Name}, {bounds}");
                    }
                    Point titleBottom = title.TranslatePoint(new Point(0, title.ActualHeight), design);
                    Point stageTop = stage.TranslatePoint(new Point(), design);
                    Assert(titleBottom.Y <= stageTop.Y && subtitle.ActualWidth >= subtitle.DesiredSize.Width - 1,
                        $"Splash title, subtitle and progress stay separate on small/high-DPI work areas: {size}");
                }
                splash.Width = 720; splash.Height = 400;
                splash.UpdateLayout();
                var cornerImage = new RenderTargetBitmap(720, 400, 96, 96, PixelFormats.Pbgra32);
                cornerImage.Render(viewport);
                foreach (Point corner in new[] { new Point(0, 0), new Point(719, 0), new Point(0, 399), new Point(719, 399) })
                {
                    byte[] pixel = new byte[4];
                    cornerImage.CopyPixels(new Int32Rect((int)corner.X, (int)corner.Y, 1, 1), pixel, 4, 0);
                    Assert(pixel[3] == 0, "Rounded splash corners remain transparent without a square window background");
                }
                Assert(splash.AllowsTransparency && design.CornerRadius.TopLeft == 10 && design.Child.Clip is RectangleGeometry,
                    "Splash applies a subtle rounded outer border and clips child backgrounds to the same shape");
                splash.Close();
                Assert(!closed && splash.IsVisible,
                    "Closing the splash cannot orphan initialization before the app completes handoff");
                if (imagePath is not null)
                {
                    var content = (FrameworkElement)splash.Content;
                    var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight,
                        96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(imagePath); encoder.Save(stream);
                }
                splash.Report(110, "Hệ thống đã sẵn sàng");
                Assert(bar.Value == 100 && percent.Text == "100%", "Startup completion clamps to 100 percent");
                splash.Complete();
                Assert(closed, "Startup completion releases the splash window");
            }
            finally { if (!closed) splash.Complete(); }
        });
    }
}
