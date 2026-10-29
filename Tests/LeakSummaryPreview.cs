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
            void Counters(long quantity, string lot)
            {
                typeof(TestViewModel).GetProperty(nameof(TestViewModel.DailyTestCount))!.SetValue(vm.Test, quantity);
                typeof(TestViewModel).GetProperty(nameof(TestViewModel.Lot))!.SetValue(vm.Test, lot);
            }
            Counters(10, "2010");
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
            var lotCard = (System.Windows.Controls.Border)window.FindName("CurrentLotCard");
            var quantityCard = (System.Windows.Controls.Border)window.FindName("ProductionQuantityCard");
            var lotText = (System.Windows.Controls.TextBlock)window.FindName("CurrentLotText");
            var quantityText = (System.Windows.Controls.TextBlock)window.FindName("ProductionQuantityText");
            Assert(lotCard.Visibility == Visibility.Visible && lotText.Text == "2010" && quantityText.Text == "10" &&
                   quantityText.FontSize >= 40 && lotText.FontSize >= 40,
                "Main renders prominent saved quantity and current LOT for a product with label printing enabled");
            Counters(11, "2011");
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Assert(lotText.Text == "2011" && quantityText.Text == "11",
                "Main quantity and LOT update through live bindings without reopening the window");
            string productKey = ProductionConfigService.GetLotProductKey(model);
            settings.LabelTemplateTypesByProduct[productKey] = ProductionConfigService.NoProductLabel;
            vm.Home.Refresh();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Assert(lotCard.Visibility == Visibility.Collapsed && System.Windows.Controls.Grid.GetColumnSpan(quantityCard) == 2,
                "A product with printing disabled hides LOT and gives the quantity card the full width");
            settings.LabelTemplateTypesByProduct[productKey] = LabelSettings.SmallQrTemplate;
            vm.Model = null;
            vm.Home.Refresh();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Assert(lotCard.Visibility == Visibility.Collapsed, "No selected product cannot display a stale label LOT");
            vm.Model = model;
            vm.Home.Refresh();
            Counters(10, "2010");
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            root.UpdateLayout();
            Assert(lotCard.Visibility == Visibility.Visible && System.Windows.Controls.Grid.GetColumnSpan(quantityCard) == 1,
                "Re-enabling product printing restores the separate quantity and LOT cards");
            var summary = (FrameworkElement)window.FindName("ModelSummaryCard");
            foreach (var size in new[] { new Size(1366, 768), new Size(1920, 1080) })
            {
                root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                Point top = summary.TranslatePoint(new Point(), root);
                Assert(top.Y >= 0 && top.Y + summary.ActualHeight < size.Height - 50 &&
                       quantityCard.ActualWidth > 250 && lotCard.ActualWidth > 250,
                    "Main summary and prominent numbers fit the supported production screen sizes above the action bar");
            }
            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
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
            typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
            typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(vm, true);
            var entries = new List<(string Title, string Text, string Background, string Foreground)>();
            void Add(string title, string state, string? display = null)
            {
                vm.State = state;
                string text = display ?? vm.ResultStatusText;
                if (text == "ĐANG KIỂM TRA") text += "...";
                entries.Add((title, text, vm.StateBackground, vm.StateForeground));
            }
            Add("01 · CHỜ LẮP SẢN PHẨM", "LẮP SẢN PHẨM");
            typeof(TestViewModel).GetField("_presentationCycleStarted", flags)!.SetValue(vm, true);
            Add("02 · ĐANG KIỂM TRA", "ĐANG KIỂM TRA...");
            Add("ĐANG CHẠY LEAK", "ĐANG TEST LEAK");
            Add("ĐANG ĐO ĐIỆN TRỞ", "ĐANG ĐO ĐIỆN TRỞ");
            Add("03 · KẾT QUẢ ĐẠT", "PASS");
            Add("04 · KẾT QUẢ KHÔNG ĐẠT", "FAIL");
            typeof(TestViewModel).GetField("_productRemovalPending", flags)!.SetValue(vm, 1);
            Add("05 · CHỜ THÁO SẢN PHẨM", "THÁO SẢN PHẨM");
            typeof(TestViewModel).GetField("_productRemovalPending", flags)!.SetValue(vm, 0);
            Add("SAI DÂY", "LỖI SAI DÂY");
            Add("CHẬP MẠCH", "LỖI CHẬP MẠCH");
            typeof(TestViewModel).GetField("_pendingLeakFailureMeasurements", flags)!.SetValue(vm,
                new WaterProofChannelMeasurement[] { new(1, true, 80, 75, 5, false) });
            vm.SelectedOperationTabIndex = 2;
            Add("TEST LEAK KHÔNG ĐẠT", "FAIL LEAK");
            vm.SelectedOperationTabIndex = 0;
            typeof(TestViewModel).GetField("_productRemovalPending", flags)!.SetValue(vm, 1);
            Add("CHỜ THÁO · CHƯA CHỐT PASS", "THÁO SẢN PHẨM");
            typeof(TestViewModel).GetField("_productRemovalPending", flags)!.SetValue(vm, 0);
            var master = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 1 });
            typeof(TestViewModel).GetField("_model", flags)!.SetValue(master, model);
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
                entries.Add((title, master.ResultStatusText, master.StateBackground, master.StateForeground));
                typeof(TestViewModel).GetField("_masterGoodVerified", flags)!.SetValue(master, true);
                typeof(TestViewModel).GetField("_masterBadVerified", flags)!.SetValue(master, true);
                typeof(TestViewModel).GetField("_masterFaultCollectionLocked", flags)!.SetValue(master, true);
                typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(master,
                    state == MasterSequenceState.TestingGoodMaster ? MasterSequenceState.EjectingGoodMaster : MasterSequenceState.EjectingBadMaster);
                entries.Add((title + " · HOÀN THÀNH", master.ResultStatusText, master.StateBackground, master.StateForeground));
            }
            Add("11 · ĐANG KẾT NỐI BO", "ĐANG KẾT NỐI BO");
            Add("12 · LỖI THIẾT BỊ", "LỖI THIẾT BỊ", "LỖI THIẾT BỊ");
            Add("LỖI ĐỒNG HỒ ĐIỆN TRỞ", "LỖI KEYSIGHT - CHỜ THÁO SẢN PHẨM");
            Add("LỖI MÁY LEAK", "LỖI KIỂM TRA LEAK");
            Add("LỖI LƯU DỮ LIỆU", "LỖI LƯU DỮ LIỆU\nDỪNG KIỂM TRA");
            Add("LỖI ĐIỀU KHIỂN RELAY", "LỖI THỰC THI RELAY");
            Add("THAO TÁC TAY", "MANUAL");
            Brush BrushOf(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            var root = new System.Windows.Controls.DockPanel
            {
                Background = BrushOf("#F1F5F9"), Width = 1600, Height = 1740,
                LastChildFill = true
            };
            var heading = new System.Windows.Controls.TextBlock
            {
                Text = "MÀU TRẠNG THÁI · " + JBZUniversalTester.Versioning.AppVersion.DisplayVersion +
                    "\nĐANG KIỂM TRA: nền #DCEBFF · chữ #173B88 | Màu và nội dung lấy từ trạng thái thực của phần mềm",
                FontSize = 25, FontWeight = FontWeights.Bold, Margin = new Thickness(24, 20, 24, 18),
                Foreground = BrushOf("#0F172A"), FontFamily = new FontFamily("Malgun Gothic")
            };
            System.Windows.Controls.DockPanel.SetDock(heading, System.Windows.Controls.Dock.Top);
            root.Children.Add(heading);
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Rows = (int)Math.Ceiling(entries.Count / 4d), Margin = new Thickness(14, 0, 14, 14) };
            root.Children.Add(tiles);
            int number = 0;
            foreach (var entry in entries)
            {
                var layout = new System.Windows.Controls.DockPanel { Margin = new Thickness(10), Background = Brushes.White };
                string caption = System.Text.RegularExpressions.Regex.Replace(entry.Title, "^[0-9]+ · ", "");
                var title = new System.Windows.Controls.TextBlock { Text = $"{++number:00} · {caption}", FontSize = 15, FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    Padding = new Thickness(12, 10, 12, 8), Foreground = BrushOf("#334155") };
                System.Windows.Controls.DockPanel.SetDock(title, System.Windows.Controls.Dock.Top);
                layout.Children.Add(title);
                var codes = new System.Windows.Controls.TextBlock { Text = $"Nền {entry.Background}  ·  Chữ {entry.Foreground}",
                    FontSize = 16, Padding = new Thickness(12, 10, 12, 10), Foreground = BrushOf("#475569") };
                System.Windows.Controls.DockPanel.SetDock(codes, System.Windows.Controls.Dock.Bottom);
                layout.Children.Add(codes);
                layout.Children.Add(new System.Windows.Controls.Border { Background = BrushOf(entry.Background), Margin = new Thickness(10, 0, 10, 0),
                    Child = new System.Windows.Controls.Viewbox { Stretch = Stretch.Uniform, StretchDirection = System.Windows.Controls.StretchDirection.DownOnly,
                        Child = new System.Windows.Controls.TextBlock { Width = 310, Text = entry.Text, Foreground = BrushOf(entry.Foreground),
                        FontSize = 25, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8) } } });
                tiles.Children.Add(layout);
            }
            root.Measure(new Size(1600, 1740));
            root.Arrange(new Rect(0, 0, 1600, 1740));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1600, 1740, 96, 96, PixelFormats.Pbgra32);
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
