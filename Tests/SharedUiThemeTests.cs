using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using JBZUniversalTester.ModelEditor.Dialogs;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSharedUiTheme() => VerifySharedUiTheme(null);

    private static void VerifySharedUiTheme(string? previewDirectory)
    {
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        string themePath = Path.GetFullPath("Themes/Theme.xaml");
        bool ReachesTheme(string path, HashSet<string> visited)
        {
            path = Path.GetFullPath(path);
            if (string.Equals(path, themePath, StringComparison.OrdinalIgnoreCase)) return true;
            if (!visited.Add(path)) return false;
            XDocument doc = XDocument.Load(path);
            return doc.Descendants(ns + "ResourceDictionary").Attributes("Source").Any(source =>
            {
                if (source.Value == ThemePalette.Source) return true;
                string linked = Path.Combine(Path.GetDirectoryName(path)!, source.Value.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(linked) && ReachesTheme(linked, visited);
            });
        }
        int surfaces = 0;
        var brushProperties = new HashSet<string>
        { "Background", "Foreground", "BorderBrush", "Color", "Fill", "Stroke", "RowBackground", "AlternatingRowBackground",
          "HorizontalGridLinesBrush", "VerticalGridLinesBrush", "SelectionBrush", "CaretBrush" };
        foreach (string path in Directory.EnumerateFiles("Views", "*.xaml", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles("Features", "*.xaml", SearchOption.AllDirectories)).Append("App.xaml"))
        {
            XDocument doc = XDocument.Load(path);
            Assert(ReachesTheme(path, new(StringComparer.OrdinalIgnoreCase)), $"Every WPF surface reaches the one shared theme: {path}");
            foreach (XElement element in doc.Descendants())
            foreach (XAttribute attribute in element.Attributes())
            {
                bool isBrush = brushProperties.Contains(attribute.Name.LocalName) ||
                    (element.Name.LocalName == "Setter" && attribute.Name.LocalName == "Value" &&
                     brushProperties.Contains(element.Attribute("Property")?.Value ?? ""));
                if (isBrush)
                    Assert(attribute.Value.StartsWith('{') || attribute.Value == "Transparent",
                        $"UI chrome colors must come from shared resources, never a local palette: {path}, {attribute}");
            }
            surfaces++;
        }

        OnSettingsDispatcher(async () =>
        {
            Assert(((SolidColorBrush)ThemePalette.Brush("WireOutline")).Color == Color.FromRgb(0x11, 0x11, 0x11) &&
                   ((SolidColorBrush)ThemePalette.Brush("PanelBackground")).Color == Colors.White &&
                   ((SolidColorBrush)ThemePalette.Brush("GridLine")).Color == Color.FromRgb(0xD6, 0xD6, 0xD6),
                "Shared resources preserve the exact white wire text, dark outline and pixel-aligned divider colors");
            var model = new ProductModel { ModelName = "THEME MODEL", PartNumber = "THEME PART", SourcePath = "THEME.tht" };
            var dialogs = new List<Window>
            {
                new PartSelectionWindow(new[] { model }, ""),
                new ProductPickerWindow(Path.GetTempPath()),
                new ProductPrintSetupWindow(model, _ => Task.CompletedTask),
                new ProbeMaintenanceResetWindow("THEME PART", "THEME MODEL", 1, 100, _ => Task.FromResult((true, "OK"))),
                new DiscardConfigurationNoticeWindow(model),
                new FaultConfirmationWindow([], "Thông báo kiểm tra"),
                new InputDialog("Nhập giá trị", "Tên model", "THEME MODEL")
            };
            var owner = new Window { ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000, Width = 160, Height = 80 };
            ThemePalette.Apply(owner);
            owner.Show();
            var security = ((Window Window, StackPanel Panel, TextBlock Error))typeof(MainWindow).Assembly
                .GetType("JBZUniversalTester.Views.SettingsSecurityDialogs", throwOnError: true)!
                .GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [owner, "QUYỀN TRUY CẬP"])!;
            dialogs.Add(security.Window);
            try
            {
                foreach (Window dialog in dialogs)
                {
                    Assert(dialog.FindResource("AccentBlue") is SolidColorBrush &&
                           dialog.FindResource("TextPrimary") is SolidColorBrush &&
                           dialog.FindResource("FailRed") is SolidColorBrush,
                        $"Dialog resolves shared normal/action/error resources: {dialog.GetType().Name}");
                }
                Assert(((SolidColorBrush)security.Error.Foreground).Color == ((SolidColorBrush)ThemePalette.Brush("FailText")).Color,
                    "Code-created security dialogs use the same error palette as XAML dialogs");
                var leakVm = new WaterProofTestViewModel("THEME LEAK", new WaterProofModelSettings
                { Enabled = true, Channel1Enabled = true, Channel2Enabled = true, Channel3Enabled = true });
                leakVm.ApplyFinal(new WaterProofRunResult(
                    [new(1, true, 80, 79, 0.5, true), new(2, true, 80, 78, 2.0, false), new(3, true, 80, 79, 0.4, true)], false, ":RESULT"));
                var leakHud = new WaterProofTestWindow(leakVm) { Left = -10000, Top = -10000 };
                try
                {
                    leakHud.Show();
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    leakHud.UpdateLayout();
                    var channels = ((Panel)leakHud.Content).Children.Cast<Border>().ToArray();
                    for (int index = 0; index < channels.Length; index++)
                    {
                        var text = (TextBlock)channels[index].Child;
                        Color expected = index == 1 ? Color.FromRgb(0xFF, 0x40, 0x40) : Color.FromRgb(0x32, 0xCD, 0x32);
                        Assert(((SolidColorBrush)text.Foreground).Color == Colors.Black &&
                               ((SolidColorBrush)channels[index].Background).Color == expected,
                            "Leak result cells retain BLACK text with the original GREEN PASS / RED FAIL background");
                    }
                    if (previewDirectory is not null)
                    {
                        Directory.CreateDirectory(previewDirectory);
                        var bitmap = new RenderTargetBitmap(220, 50, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render((Visual)leakHud.Content);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(previewDirectory, "WaterProofResults.png"));
                        encoder.Save(stream);
                    }
                }
                finally { leakHud.CloseOnce(); }
                if (previewDirectory is not null)
                {
                    Directory.CreateDirectory(previewDirectory);
                    foreach (Window dialog in dialogs.Where(dialog => dialog is PartSelectionWindow or ProductPrintSetupWindow or InputDialog or FaultConfirmationWindow))
                    {
                        // Render detached content: Window.Loaded/device callbacks never run.
                        var content = dialog.Content as FrameworkElement ?? throw new InvalidOperationException("Dialog has no renderable content.");
                        dialog.Content = null;
                        content.Resources.MergedDictionaries.Add(dialog.Resources);
                        content.SetValue(TextElement.ForegroundProperty, ThemePalette.Brush("TextPrimary"));
                        double width = Math.Min(720, double.IsNaN(dialog.Width) ? 540 : dialog.Width);
                        content.Measure(new Size(width, 900));
                        double height = Math.Clamp(content.DesiredSize.Height, 180, 900);
                        content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                        var drawing = new DrawingVisual();
                        using (DrawingContext dc = drawing.RenderOpen())
                        {
                            dc.DrawRectangle(ThemePalette.Brush("PageBackground"), null, new Rect(0, 0, width, height));
                            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
                        }
                        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(drawing);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(previewDirectory, dialog.GetType().Name + ".png"));
                        encoder.Save(stream);
                    }
                }
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            finally { foreach (Window dialog in dialogs) dialog.Close(); owner.Close(); }
        });
        Console.WriteLine($"SHARED_THEME PASS: {surfaces} XAML surfaces, 8 dialogs and original black/green/red Leak results");
    }
}
