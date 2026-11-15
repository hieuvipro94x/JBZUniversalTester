using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestDiscardConfigurationStatus()
    {
        string configDirectory = Path.Combine(Path.GetTempPath(), "JBZ-DiscardNotice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDirectory);
        try
        {
            string cfg = Path.Combine(configDirectory, "settings.cfg");
            var settings = new ProductionSettings();
            Assert(settings.ShowDiscardConfigurationNotice, "Existing stations keep the notice enabled by default");
            foreach (bool enabled in new[] { false, true })
            {
                settings.ShowDiscardConfigurationNotice = enabled;
                ProductionConfigService.SaveLegacyCfg(settings, cfg);
                Assert(ProductionConfigService.LoadSavedSettings(cfg).ShowDiscardConfigurationNotice == enabled,
                    "The notice preference remains saved after reopening Settings");
            }
        }
        finally
        {
            if (Path.GetFullPath(configDirectory).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-DiscardNotice-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(configDirectory, true);
        }
        Assert(DiscardConfigurationPresentation.For(null).Summary.Contains("CHƯA CHỌN MÃ HÀNG"),
            "An empty selection cannot retain another product's discard status");
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var main = new MainViewModel(new AppSettings(), new ProductionSettings());
            var window = new MainWindow(main);
            var root = (FrameworkElement)window.Content;
            window.Content = null; // No app startup or device connections.
            root.DataContext = main;
            root.Resources.MergedDictionaries.Add(window.Resources);
            var label = (TextBlock)window.FindName("MainDiscardStatusText");
            foreach (var example in new[]
            {
                (new List<int> { 41, 86 }, "ĐÃ CÓ XÁC NHẬN THÙNG LỖI", "IO 41 / 86", "#FF166534"),
                (new List<int>(), "CHƯA CÓ XÁC NHẬN THÙNG LỖI", "CHƯA CẤU HÌNH IO", "#FFB45309"),
                (new List<int> { 41 }, "CẤU HÌNH THÙNG LỖI CHƯA HỢP LỆ", "IO 41", "#FFB91C1C"),
                (new List<int> { 41, 41 }, "CẤU HÌNH THÙNG LỖI CHƯA HỢP LỆ", "IO 41 / 41", "#FFB91C1C")
            })
            {
                var model = new ProductModel { SourcePath = "selected.tht", DiscardContactIo = example.Item1 };
                var display = DiscardConfigurationPresentation.For(model);
                Assert(display.Title == example.Item2 && display.IoText == example.Item3,
                    "Valid, absent, incomplete and duplicated discard IO have distinct operator states");
                typeof(TestViewModel).GetField("_model", flags)!.SetValue(main.Test, model);
                typeof(TestViewModel).GetMethod("RaiseHomeEquipmentPresentation", flags)!.Invoke(main.Test, null);
                root.Measure(new Size(1366, 768)); root.Arrange(new Rect(0, 0, 1366, 768));
                await Dispatcher.Yield(DispatcherPriority.Background);
                root.UpdateLayout();
                Assert(label.Text == display.Summary && ((SolidColorBrush)label.Foreground).Color.ToString() == example.Item4,
                    "Actual MainWindow updates discard status and color after switching models");
                var notice = new DiscardConfigurationNoticeWindow(model);
                Assert(((TextBlock)notice.FindName("ConfiguredHeaderText")).Text == example.Item2 &&
                    ((TextBlock)notice.FindName("ConfiguredIoText")).Text == example.Item3 &&
                    ((TextBlock)notice.FindName("ConfiguredDescriptionText")).Text == display.Description,
                    "The actual selection/startup notice supports both configured and unconfigured models");
                notice.Close();
            }
            typeof(TestViewModel).GetField("_model", flags)!.SetValue(main.Test, null);
            typeof(TestViewModel).GetMethod("RaiseHomeEquipmentPresentation", flags)!.Invoke(main.Test, null);
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert(label.Text.Contains("CHƯA CHỌN MÃ HÀNG"), "Clearing the selected model clears the persistent MainWindow status");
            Assert(main.Test.Total == 0 && main.Test.Pass == 0 && main.Test.Fail == 0,
                "Configuration status rendering never counts as a test or sensor confirmation");
        });
    }
}
