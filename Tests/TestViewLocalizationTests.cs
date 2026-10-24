using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Threading;
using System.Xml.Linq;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestTestViewLanguagePersistence()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-Language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert(new ProductionSettings().TestViewLanguage == "vi", "Existing installations default to Vietnamese");
            string path = Path.Combine(root, "settings.cfg");
            var settings = new ProductionSettings { TestViewLanguage = "ko", ProbeReplacementThreshold = 150000 };
            settings.Label.TemplateType = "TEM_BE_QRCODE";
            ProductionConfigService.SaveLegacyCfg(settings, path);
            var loaded = ProductionConfigService.LoadSavedSettings(path);
            Assert(loaded.TestViewLanguage == "ko" && loaded.Label.TemplateType == "TEM_BE_QRCODE" && loaded.ProbeReplacementThreshold == 150000,
                "Korean selection persists without changing label configuration or maintenance thresholds");
            File.WriteAllText(path, File.ReadAllText(path).Replace("[TestViewLanguage]ko", "[TestViewLanguage]unknown", StringComparison.Ordinal));
            Assert(ProductionConfigService.LoadSavedSettings(path).TestViewLanguage == "vi", "Unknown language falls back safely to Vietnamese");
            Assert(TestViewText.Translate("Mã hàng: SAI DÂY - WH322444", "ko") == "품번: SAI DÂY - WH322444" &&
                   TestViewText.Translate("Model: PASS THÁO SẢN PHẨM", "ko") == "모델: PASS THÁO SẢN PHẨM",
                "Localized metadata captions preserve opaque product identifiers even when they resemble UI text");
            string complete = TestViewText.Translate("HOÀN THÀNH KIỂM TRA MẪU TUỘT TUÝT / ĐỨT DÂY", "ko");
            Assert(complete == "핀 빠짐 / 단선 마스터 검사 완료" &&
                TestViewText.Translate("KIỂM TRA MẪU ĐẠT\nĐANG TEST LEAK", "ko") == "정상 마스터 검사\n기밀 검사 중",
                "Master completion and combined Master/Leak instructions are fully Korean");
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++) TestViewText.Translate("CHƯA KẾT NỐI", "ko");
            Assert(watch.ElapsedMilliseconds < 500, "Frequent row status translation uses the in-memory catalog without IO");
        }
        finally { Directory.Delete(root, true); }
    }

    private static bool HasVietnamese(string text) => Regex.IsMatch(text, "[ĂăÂâĐđÊêÔôƠơƯưáéíóúýàèìòùỳãẽĩõũỹ\u1EA0-\u1EF9]");

    private static void TestTestViewLanguageCoverage()
    {
        foreach (string filename in new[] { "TestWindow.xaml", "FaultConfirmationWindow.xaml", "ProbeMaintenanceResetWindow.xaml", "WaterProofTestWindow.xaml" })
        {
            var document = XDocument.Load(Path.Combine(Environment.CurrentDirectory, "Views", filename));
            foreach (var attr in document.Descendants().Attributes().Where(attr => attr.Name.LocalName is "Text" or "Header" or "Content" or "Title" or "ToolTip"))
            {
                if (attr.Value.StartsWith('{') || !HasVietnamese(attr.Value)) continue;
                Assert(!HasVietnamese(TestViewText.Translate(attr.Value, "ko")), $"Korean catalog covers {filename}: {attr.Value}");
            }
        }
        string source = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "ViewModels", "TestViewModel.cs"));
        var missing = Regex.Matches(source, "(?:State|MasterStatus)\\s*=\\s*\"([^\"]+)\"")
            .Select(match => Regex.Unescape(match.Groups[1].Value))
            .Where(text => HasVietnamese(TestViewText.Translate(text, "ko"))).Distinct().ToArray();
        Assert(missing.Length == 0, "Korean catalog covers authored fixed TestView states: " + string.Join("; ", missing));
    }

    private static void TestTestViewKoreanPresentation()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0, TestViewLanguage = "ko" };
            var vm = CreateTestViewModel(settings, out FakeBoard board);
            var model = Model(("SAI DÂY", new[] { 1, 2 }));
            model.ModelName = "DL3_LAM SAI DÂY WH322444";
            model.PartNumber = "PASS-WH322444";
            model.ProductName = "THÁO SẢN PHẨM";
            typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
            typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(vm, true);
            vm.State = "LẮP SẢN PHẨM";
            int commandsBefore = board.Commands.Count;
            var window = new TestWindow(vm, autoStartProduction: false);
            vm.State = "LẮP SẢN PHẨM";
            await Dispatcher.Yield(DispatcherPriority.Background);
            var grid = (DataGrid)window.FindName("FaultGrid");
            Assert((string)grid.Columns[0].Header == "종류" && (string)grid.Columns[2].Header == "커넥터",
                "Korean TestView translates actual table headers");
            var statusBinding = (Binding)((DataGridBoundColumn)grid.Columns[7]).Binding;
            Assert((string)statusBinding.Converter.Convert("SAI DÂY", typeof(string), statusBinding.ConverterParameter, System.Globalization.CultureInfo.InvariantCulture) == "오배선",
                "Dynamically created/virtualized fault cells use Korean display converters");
            Assert(((Binding)((DataGridBoundColumn)grid.Columns[4]).Binding).Converter is null &&
                   ((TextBlock)window.FindName("ModelTitleText")).Text == model.ModelName &&
                   vm.PartNumber == model.PartNumber && vm.ProductName == model.ProductName,
                "Product names, full model titles and wire data bindings are never translated");
            var statusHost = (Border)window.FindName("ResultStatusHost");
            var statusBlock = (TextBlock)window.FindName("ResultStatusTextBlock");
            Assert(statusBlock.Text == "제품을\n장착하세요" && vm.ResultStatusText == "LẮP SẢN PHẨM",
                $"Only rendered status changes language; canonical state logic remains Vietnamese: rendered={statusBlock.Text}; raw={vm.ResultStatusText}");
            // Render the actual WPF content without showing/starting a production window.
            var surface = (FrameworkElement)window.Content;
            surface.Measure(new Size(1920, 1080));
            surface.Arrange(new Rect(0, 0, 1920, 1080));
            surface.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1920, 1080, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(Path.GetTempPath(), "jbz-v309-testview-ko.png"))) encoder.Save(output);
            vm.State = "LỖI KEYSIGHT - CHỜ THÁO SẢN PHẨM";
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert(statusBlock.Text == "KEYSIGHT 오류 - 제품 탈거 대기" && !HasVietnamese(statusBlock.Text) && board.Commands.Count == commandsBefore,
                "Live device faults remain specific in Korean and localization sends no hardware commands");
            var resultBinding = BindingOperations.GetBinding(statusBlock, TextBlock.TextProperty)!;
            foreach (string statusPath in new[] { "ResultStatusText", "CenterResultText" })
                Assert((string)resultBinding.Converter.Convert("PASS", typeof(string), statusPath, System.Globalization.CultureInfo.InvariantCulture) == "PASS",
                    "Large and center PASS labels remain PASS in Korean mode");
            Assert((string)resultBinding.Converter.Convert("PASS", typeof(string), "ResultText", System.Globalization.CultureInfo.InvariantCulture) == "합격",
                "Keeping large PASS labels does not change the Korean measurement table");
            typeof(TestWindow).GetField("_allowClose", flags)!.SetValue(window, true);
            window.Close();
            var settingsVm = new ProductionSettingsViewModel(saveSettings: _ => { }, initialSettings: new ProductionSettings { TestViewLanguage = "ko" });
            var page = new ProductionSettingsPage(null, settingsVm, deferInitialLayout: true);
            page.SetAccessSession(new(false, SettingsPermission.Leak));
            await page.PrepareForDisplayAsync();
            Assert(!((ComboBox)page.FindName("TestLanguageComboBox")).IsEnabled, "Language editor respects the IO settings permission");
            page.SetAccessSession(new(false, SettingsPermission.Io));
            Assert(((ComboBox)page.FindName("TestLanguageComboBox")).IsEnabled &&
                (string)((ComboBox)page.FindName("TestLanguageComboBox")).SelectedValue == "ko", "Settings shows and enables the saved Korean selection for authorized operators");
            page.ReleasePageResources();
        });
    }

    private static void TestExclusiveTestingAndPassLabels()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (string language in new[] { "vi", "ko" })
            {
                var settings = new ProductionSettings { MasterFaultRequiredCount = 0, TestViewLanguage = language };
                var vm = CreateTestViewModel(settings, out FakeBoard board);
                typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, Model(("PAIR", new[] { 1, 2 })));
                typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(vm, true);
                typeof(TestViewModel).GetField("_presentationCycleStarted", flags)!.SetValue(vm, true);
                var phase = typeof(TestViewModel).GetField("_productionPhase", flags)!;
                var phaseType = typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!;
                var window = new TestWindow(vm, autoStartProduction: false);
                var text = (TextBlock)window.FindName("ResultStatusTextBlock");
                var dots = (TextBlock)window.FindName("TestingStatusDots");
                var center = (Border)window.FindName("CenterResultHost");
                var panel = (FrameworkElement)text.Parent;
                var surface = (FrameworkElement)window.Content;
                int commandsBefore = board.Commands.Count;
                void Layout()
                {
                    surface.Measure(new Size(1920, 1080));
                    surface.Arrange(new Rect(0, 0, 1920, 1080));
                    surface.UpdateLayout();
                }
                void SaveImage(string name)
                {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1920, 1080, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(surface);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(Path.GetTempPath(), name));
                    encoder.Save(output);
                }
                try
                {
                    for (int i = 0; i < 20; i++)
                    {
                        phase.SetValue(vm, (int)Enum.Parse(phaseType, "Continuity"));
                        vm.State = "ĐANG KIỂM TRA";
                        typeof(TestWindow).GetMethod("UpdateTestingDots", flags)!.Invoke(window, [vm]);
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        Layout();
                        Point before = text.TranslatePoint(new Point(), panel);
                        for (int dot = 0; dot < 4; dot++)
                        {
                            typeof(TestWindow).GetMethod("TestingDotsTimer_Tick", flags)!.Invoke(window, [window, EventArgs.Empty]);
                            Layout();
                            Assert(text.Text == (language == "ko" ? "검사 중" : "ĐANG KIỂM TRA") &&
                                   text.TranslatePoint(new Point(), panel) == before && center.Visibility == Visibility.Collapsed,
                                "Testing text remains stationary and center PASS is hidden throughout the animated dots");
                            Point dotStart = dots.TranslatePoint(new Point(), panel);
                            Assert(before.X + text.ActualWidth <= dotStart.X + 0.1 && dots.Visibility == Visibility.Visible,
                                "Testing text and animated dots occupy separate non-overlapping layout columns");
                        }
                        if (language == "ko" && i == 19) SaveImage("jbz-v314-testing-ko.png");
                        phase.SetValue(vm, (int)Enum.Parse(phaseType, "Completed"));
                        vm.State = "PASS";
                        typeof(TestWindow).GetMethod("UpdateTestingDots", flags)!.Invoke(window, [vm]);
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        Layout();
                        Assert(text.Text == "PASS" && dots.Text.Length == 0 && dots.Visibility == Visibility.Collapsed &&
                               center.Visibility == Visibility.Visible && ((TextBlock)center.Child).Text == "PASS",
                            "PASS replaces testing text in both views without retained Korean/Vietnamese text or dots");
                    }
                    Assert(board.Commands.Count == commandsBefore, "Status layout transitions never send relay or scan commands");
                    if (language == "ko") SaveImage("jbz-v314-pass-ko.png");
                }
                finally
                {
                    typeof(TestWindow).GetField("_allowClose", flags)!.SetValue(window, true);
                    window.Close();
                }
            }
        });
    }
}
