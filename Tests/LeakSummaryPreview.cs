using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void RenderMainLabelPreview(string path)
    {
        OnSettingsDispatcher(async () =>
        {
            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var appDocument = System.Xml.Linq.XDocument.Load(Path.Combine(Environment.CurrentDirectory, "App.xaml"));
            var resourceXml = new System.Xml.Linq.XElement(ns + "ResourceDictionary",
                new System.Xml.Linq.XAttribute("xmlns", ns.NamespaceName),
                new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
                appDocument.Root!.Element(ns + "Application.Resources")!.Elements());
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resourceXml.ToString());
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
            settings.Label.TemplateType = LabelSettings.SmallQrTemplate;
            var vm = new MainViewModel(new AppSettings(), settings);
            var model = Model(("PAIR", new[] { 1, 2 }));
            model.ModelName = "DL3_LAM WH322444";
            model.PartNumber = "WH322444";
            vm.Model = model;
            vm.Home.Refresh();
            vm.Status = "ĐÃ NẠP MÃ HÀNG: DL3_LAM WH322444";
            Assert(vm.Home.LabelTemplateType == LabelSettings.SmallQrTemplate,
                "MainWindow label information follows the loaded model and current print selection");
            var window = new MainWindow(vm);
            var root = (FrameworkElement)window.Content;
            window.Content = null; // Never show the window or start its hardware lifecycle.
            root.DataContext = vm;
            root.Resources.MergedDictionaries.Add(window.Resources);
            root.SetValue(System.Windows.Controls.TextBlock.FontFamilyProperty, new FontFamily("Segoe UI"));
            // Preview enabled button colors without connecting hardware or invoking commands.
            foreach (var name in new[] { "StartTestButton", "SelectModelButton", "LearnTopologyButton" })
                ((System.Windows.Controls.Button)window.FindName(name)).IsEnabled = true;
            Descendants(root).OfType<System.Windows.Controls.Image>().First().Source = new BitmapImage(
                new Uri("pack://application:,,,/JBZUniversalTester;component/Assets/mainlogo.gif", UriKind.Absolute));
            const int width = 1440, height = 900;
            using var source = new HwndSource(new HwndSourceParameters("MainLabelPreview")
                { Width = width, Height = height, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = root;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            root.UpdateLayout();
            Assert(Descendants(root).OfType<System.Windows.Controls.TextBlock>().Any(text =>
                text.Text == "Loại tem: TEM_BE_QRCODE"), "The actual MainWindow view renders the selected label type");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var file = File.Create(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(file);
        });
    }

    private static void RenderStatusPalettePreview(string path)
    {
        OnSettingsDispatcher(() =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
            var model = Model(("PAIR", new[] { 1, 2 }));
            vm.SetModel(model);
            typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(vm, true);
            var entries = new List<(string Title, string Text, string Background, string Foreground)>();
            void Add(string title, string state, string? display = null)
            {
                vm.State = state;
                entries.Add((title, display ?? vm.ResultStatusText, vm.StateBackground, vm.StateForeground));
            }
            Add("01 · CHỜ LẮP SẢN PHẨM", "LẮP SẢN PHẨM");
            typeof(TestViewModel).GetField("_presentationCycleStarted", flags)!.SetValue(vm, true);
            Add("02 · ĐANG KIỂM TRA", "ĐANG KIỂM TRA...");
            Add("03 · KẾT QUẢ ĐẠT", "PASS");
            Add("04 · KẾT QUẢ KHÔNG ĐẠT", "FAIL");
            typeof(TestViewModel).GetField("_productRemovalPending", flags)!.SetValue(vm, 1);
            Add("05 · CHỜ THÁO SẢN PHẨM", "THÁO SẢN PHẨM");
            typeof(TestViewModel).GetField("_productRemovalPending", flags)!.SetValue(vm, 0);
            Add("06 · TEST LEAK KHÔNG ĐẠT", "FAIL LEAK", "KIỂM TRA KÍN NƯỚC\nKHÔNG ĐẠT");
            var master = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 1 });
            master.SetModel(model);
            typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(master, false);
            foreach (var (title, text, type, state) in new[]
            {
                ("07 · MẪU ĐẠT", "KIỂM TRA MẪU ĐẠT", MasterSampleType.WrongWiring, MasterSequenceState.TestingGoodMaster),
                ("08 · MẪU SAI DÂY", "KIỂM TRA MẪU\nSAI DÂY", MasterSampleType.WrongWiring, MasterSequenceState.TestingBadMaster),
                ("09 · MẪU CHẬP MẠCH", "KIỂM TRA MẪU\nCHẬP MẠCH", MasterSampleType.ShortCircuit, MasterSequenceState.TestingBadMaster),
                ("10 · MẪU TUỘT TUÝT / ĐỨT DÂY", "KIỂM TRA MẪU\nTUỘT TUÝT / ĐỨT DÂY", MasterSampleType.OpenCircuit, MasterSequenceState.TestingBadMaster)
            })
            {
                typeof(TestViewModel).GetField("_masterSampleType", flags)!.SetValue(master, type);
                typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(master, state);
                master.State = text;
                entries.Add((title, text, master.StateBackground, master.StateForeground));
            }
            Add("11 · ĐANG KẾT NỐI BO", "ĐANG KẾT NỐI BO");
            Add("12 · LỖI THIẾT BỊ", "LỖI THIẾT BỊ", "LỖI THIẾT BỊ");
            Brush BrushOf(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            var root = new System.Windows.Controls.DockPanel
            {
                Background = BrushOf("#F1F5F9"), Width = 1440, Height = 1120,
                LastChildFill = true
            };
            var heading = new System.Windows.Controls.TextBlock
            {
                Text = "MÀU TRẠNG THÁI · V2026.09.268\nMàu lấy trực tiếp từ phần mềm · Mẫu chờ lắp và đang kiểm tra dùng chung nền vàng",
                FontSize = 25, FontWeight = FontWeights.Bold, Margin = new Thickness(24, 20, 24, 18),
                Foreground = BrushOf("#0F172A"), FontFamily = new FontFamily("Malgun Gothic")
            };
            System.Windows.Controls.DockPanel.SetDock(heading, System.Windows.Controls.Dock.Top);
            root.Children.Add(heading);
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Rows = 4, Margin = new Thickness(14, 0, 14, 14) };
            root.Children.Add(tiles);
            foreach (var entry in entries)
            {
                var layout = new System.Windows.Controls.DockPanel { Margin = new Thickness(10), Background = Brushes.White };
                var title = new System.Windows.Controls.TextBlock { Text = entry.Title, FontSize = 17, FontWeight = FontWeights.Bold,
                    Padding = new Thickness(12, 10, 12, 8), Foreground = BrushOf("#334155") };
                System.Windows.Controls.DockPanel.SetDock(title, System.Windows.Controls.Dock.Top);
                layout.Children.Add(title);
                var codes = new System.Windows.Controls.TextBlock { Text = $"Nền {entry.Background}  ·  Chữ {entry.Foreground}",
                    FontSize = 16, Padding = new Thickness(12, 10, 12, 10), Foreground = BrushOf("#475569") };
                System.Windows.Controls.DockPanel.SetDock(codes, System.Windows.Controls.Dock.Bottom);
                layout.Children.Add(codes);
                layout.Children.Add(new System.Windows.Controls.Border { Background = BrushOf(entry.Background), Margin = new Thickness(10, 0, 10, 0),
                    Child = new System.Windows.Controls.TextBlock { Text = entry.Text, Foreground = BrushOf(entry.Foreground),
                        FontSize = 25, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) } });
                tiles.Children.Add(layout);
            }
            root.Measure(new Size(1440, 1120));
            root.Arrange(new Rect(0, 0, 1440, 1120));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1440, 1120, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var file = File.Create(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(file);
            return Task.CompletedTask;
        });
    }

    private static void RenderLeakSummaryPreview(string path, bool combined = false)
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
            var model = TopologyModel(
                new Terminal(40, "CN1", "1", "1", "RET1"), new Terminal(41, "JIG", "1", "3", "RET1"),
                new Terminal(42, "CN2", "1", "1", "RET2"), new Terminal(43, "JIG", "2", "3", "RET2"),
                new Terminal(44, "CN3", "1", "1", "RET3"), new Terminal(45, "JIG", "3", "3", "RET3"));
            model.ModelName = "MINH HỌA KẾT QUẢ LEAK";
            model.PartNumber = "WH322444";
            vm.SetModel(model);
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            Set("_waterProofProfile", new WaterProofModelSettings
            {
                Enabled = true, Channel1Enabled = true, Channel1Connector = "CN1",
                Channel2Enabled = true, Channel2Connector = "CN2",
                Channel3Enabled = true, Channel3Connector = "CN3", LeakLimit = 2
            });
            Set("_lastWaterProofMeasurements", new WaterProofChannelMeasurement[]
            {
                new(1, true, 80.5, 79.8, 0.7, true),
                new(2, true, 81.0, 75.2, 5.8, false),
                new(3, true, 80.8, 79.9, 0.9, true)
            });
            var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
            engine.SetFrameProcessingEnabled(true);
            engine.ProcessFrame(FrameSeq(100, (40, new[] { 41 }), (42, new[] { 43 }), (44, new[] { 45 })), false);
            Set("_runtimeMode", 1);
            Set("_cycleActive", true);
            Set("_productionPhase", (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, "WaterProof"));
            var window = new TestWindow(vm, autoStartProduction: false);
            if (combined)
            {
                vm.ResistanceDisplayRows.Add(new ResistanceResult { Name = "R1", Channel = 1,
                    MinOhm = 7500, MaxOhm = 11000, ValueOhm = 9065, IsStable = true, Passed = true });
                vm.ResistanceDisplayRows.Add(new ResistanceResult { Name = "R2", Channel = 2,
                    MinOhm = 7500, MaxOhm = 11000, ValueOhm = 9070, IsStable = true, Passed = true });
                for (int channel = 1; channel <= 3; channel++)
                    vm.FinalWaterProofRows.Add(new WaterProofChannelResult { Channel = channel,
                        Connector = $"CN{channel}", Enabled = true, FirstResultPressure = 80.5,
                        SecondResultPressure = 79.8, Leak = 0.7, LeakLimit = 2, IsMeasured = true, Passed = true });
                Set("_productionPhase", (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, "Completed"));
                vm.SelectedOperationTabIndex = 2;
                vm.State = "PASS";
            }
            else typeof(TestViewModel).GetMethod("UpdatePendingWaterProofFailurePresentation", flags)!.Invoke(vm, null);
            var root = (FrameworkElement)window.Content;
            window.Content = null; // Render the real view without opening a test window or triggering its Loaded lifecycle.
            root.DataContext = vm;
            root.Resources.MergedDictionaries.Add(window.Resources);
            root.SetValue(System.Windows.Controls.TextBlock.FontFamilyProperty, new FontFamily("Malgun Gothic"));
            const int width = 1600, height = 720;
            using var source = new HwndSource(new HwndSourceParameters("LeakSummaryPreview")
                { Width = width, Height = height, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = root;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var file = File.Create(path);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(file);
        });
    }
}
