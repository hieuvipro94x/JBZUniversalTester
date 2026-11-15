using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void RenderMasterCounterPreview(string outputPath)
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 2,
                MasterOpenFaultRequiredCount = 2 });
            var model = Model(("MC1", new[] { 1, 2 }), ("MC2", new[] { 3, 4 }));
            typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
            var window = new TestWindow(vm, autoStartProduction: false);
            var root = (FrameworkElement)window.Content;
            window.Content = null; // Render only; never load/start hardware or record history.
            root.DataContext = vm;
            root.Resources.MergedDictionaries.Add(window.Resources);
            var counter = (TextBlock)window.FindName("FaultCountValueText");
            var counterGrid = (Grid)((Border)window.FindName("FaultCountValueBorder")).Parent;
            var status = (Border)window.FindName("ResultStatusHost");
            var keys = (HashSet<MasterFaultKey>)typeof(TestViewModel).GetField("_masterDetectedFaultKeys", flags)!.GetValue(vm)!;
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            void Refresh() => typeof(TestViewModel).GetMethod("RaiseMasterState", flags)!.Invoke(vm, null);
            var sheet = new DrawingVisual();
            using (DrawingContext drawing = sheet.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, 1560, 960));
                int card = 0;
                foreach (MasterSampleType? sample in new MasterSampleType?[]
                         { null, MasterSampleType.WrongWiring, MasterSampleType.ShortCircuit, MasterSampleType.OpenCircuit })
                for (int phase = 0; phase < 3; phase++)
                {
                    bool good = sample is null;
                    int required = sample == MasterSampleType.ShortCircuit ? 1 : 2;
                    int detected = Math.Min(phase, required);
                    bool accepted = phase == 2;
                    Set("_masterApproved", false);
                    Set("_masterGoodVerified", good ? accepted : true);
                    Set("_masterBadVerified", !good && accepted);
                    Set("_masterFaultCollectionLocked", !good && accepted);
                    Set("_masterRequiredFaultCount", required);
                    Set("_masterSampleType", sample ?? MasterSampleType.WrongWiring);
                    Set("_masterSequenceState", good
                        ? phase == 0 ? MasterSequenceState.WaitingGoodMaster : accepted ? MasterSequenceState.EjectingGoodMaster : MasterSequenceState.TestingGoodMaster
                        : phase == 0 ? MasterSequenceState.WaitingBadMaster : accepted ? MasterSequenceState.EjectingBadMaster : MasterSequenceState.TestingBadMaster);
                    keys.Clear();
                    if (sample is { } faultSample)
                        for (int i = 0; i < detected; i++)
                            keys.Add(new MasterFaultKey(MasterSampleCatalog.FaultType(faultSample), i + 1, i + 20, 0, 0));
                    vm.State = "KIỂM TRA MẪU ĐẠT";
                    Refresh();
                    root.Measure(new Size(1920, 1080));
                    root.Arrange(new Rect(0, 0, 1920, 1080));
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    root.UpdateLayout();
                    Assert(counter.Text == (good ? "0" : $"{detected}/{required}"),
                        "Preview uses the actual bound Master counter");
                    double x = card % 3 * 520, y = card / 3 * 240;
                    drawing.DrawRectangle(Brushes.White, new Pen(Brushes.LightGray, 1), new Rect(x + 4, y + 4, 512, 230));
                    string title = (good ? "MẪU ĐẠT" : "MẪU " + MasterSampleCatalog.Name(sample!.Value)) +
                        " · " + (phase == 0 ? "CHỜ LẮP" : accepted ? "HOÀN THÀNH" : "ĐANG KIỂM TRA");
                    var heading = new FormattedText(title, CultureInfo.GetCultureInfo("vi-VN"), FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 15, Brushes.DarkSlateGray, 1) { MaxTextWidth = 494 };
                    drawing.DrawText(heading, new Point(x + 12, y + 10));
                    // Snapshot before the next case mutates the same bindings.
                    var cardVisual = new DrawingVisual();
                    using (var cardDrawing = cardVisual.RenderOpen())
                    {
                        cardDrawing.DrawRectangle(new VisualBrush(counterGrid) { Stretch = Stretch.Uniform }, null, new Rect(0, 0, 240, 165));
                        cardDrawing.DrawRectangle(new VisualBrush(status) { Stretch = Stretch.Uniform }, null, new Rect(256, 0, 240, 165));
                    }
                    var cardBitmap = new RenderTargetBitmap(496, 165, 96, 96, PixelFormats.Pbgra32);
                    cardBitmap.Render(cardVisual);
                    drawing.DrawImage(cardBitmap, new Rect(x + 12, y + 54, 496, 165));
                    card++;
                }
            }
            var bitmap = new RenderTargetBitmap(1560, 960, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(sheet);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            using (var output = File.Create(outputPath)) encoder.Save(output);
            typeof(TestWindow).GetField("_allowClose", flags)!.SetValue(window, true);
            window.Close();
        });
    }
}
