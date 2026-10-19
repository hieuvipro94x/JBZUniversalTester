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
    private static void RenderLeakSummaryPreview(string path)
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
            typeof(TestViewModel).GetMethod("UpdatePendingWaterProofFailurePresentation", flags)!.Invoke(vm, null);
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
