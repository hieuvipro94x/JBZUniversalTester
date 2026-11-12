using System.Windows;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Shapes;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.Views;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestDiagnosticUiAudit()
    {
        OnSettingsDispatcher(async () =>
        {
            FaultDetail[] faults = [
                new() { Type = ProductFaultType.WrongWiring, ExpectedSourceIo = 1, ExpectedTargetIo = 2,
                    ActualSourceIo = 1, ActualTargetIo = 3 },
                new() { Type = ProductFaultType.ShortCircuit, ActualSourceIo = 40, ActualTargetIo = 41,
                    RelatedIos = [40, 41] }
            ];
            var popup = new FaultConfirmationWindow(faults, "THÁO SẢN PHẨM");
            var root = (FrameworkElement)popup.Content;
            popup.Content = null; // Render without opening a modal dialog or any device.
            root.Resources.MergedDictionaries.Add(popup.Resources);
            using var source = new HwndSource(new HwndSourceParameters("FaultDetailsAudit")
                { Width = 560, Height = 680, WindowStyle = unchecked((int)0x80000000) });
            source.RootVisual = root;
            root.Measure(new Size(560, 680));
            root.Arrange(new Rect(0, 0, 560, 680));
            root.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Background);
            root.UpdateLayout();
            var items = (ItemsControl)popup.FindName("FaultItemsControl");
            string[] visibleDetails = Descendants(root).OfType<TextBlock>()
                .Select(block => new TextRange(block.ContentStart, block.ContentEnd).Text.Trim()).ToArray();
            Assert(items.Items.Count == faults.Length && faults.All(fault => visibleDetails.Contains(
                    FaultDisplayFormatter.FormatCompactOperator(fault).Summary.Trim())),
                "The real popup renders every physical IO pair even when wire/housing metadata is missing");
            Assert(visibleDetails.Contains("LỖI SAI DÂY") && visibleDetails.Contains("LỖI CHẬP MẠCH"),
                "Mixed fault types retain individual titles instead of a generic unsuccessful result");

            var settingsVm = new ProductionSettingsViewModel();
            settingsVm.Settings.Label.PrinterCom = "COM9876";
            var settingsPage = new ProductionSettingsPage(null, settingsVm);
            try
            {
                await (Task)typeof(ProductionSettingsPage).GetMethod("RefreshPortsAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settingsPage, null)!;
                var printer = (ComboBox)settingsPage.FindName("PrinterComComboBox");
                Assert((string?)printer.SelectedValue == "COM9876" &&
                       settingsVm.Settings.Label.PrinterCom == "COM9876",
                    "Refreshing detected COM ports keeps the unplugged saved printer COM selected and visible without connecting or saving");
            }
            finally { settingsPage.ReleasePageResources(); }

            var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
            vm.SetModel(Model(("LED-PAIR", new[] { 1, 2 })));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var testWindow = new TestWindow(vm, autoStartProduction: false);
            vm.State = "PASS";
            typeof(TestWindow).GetField("_statusLedHandlersAttached", flags)!.SetValue(testWindow, 1);
            long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
            typeof(TestViewModel).GetField("_passStatusLedActiveRequest", flags)!.SetValue(vm, 1L);
            typeof(TestViewModel).GetField("_passStatusLedRuntimeGeneration", flags)!.SetValue(vm, generation);
            var apply = typeof(TestWindow).GetMethod("ApplyStatusLedState", flags)!;
            apply.Invoke(testWindow, [vm]);
            var green = (Ellipse)testWindow.FindName("GreenStatusLed");
            Assert(((SolidColorBrush)green.Fill).Color == Color.FromRgb(0x22, 0xC5, 0x5E),
                "The real green status lamp is held during the PASS sequence");
            vm.State = "PASS ĐÃ LƯU - RELAY BỊ CHẶN";
            apply.Invoke(testWindow, [vm]);
            Assert(((SolidColorBrush)green.Fill).Color == Color.FromRgb(0x31, 0x54, 0x3B),
                "A blocked relay cannot keep the PASS lamp lit while its explicit error is displayed");
            vm.State = "PASS";
            typeof(TestViewModel).GetField("_persistenceFault", flags)!.SetValue(vm, 1);
            apply.Invoke(testWindow, [vm]);
            Assert(((SolidColorBrush)green.Fill).Color == Color.FromRgb(0x31, 0x54, 0x3B) && !vm.IsPassStatusLedHeld,
                "A persistence fault extinguishes the held PASS lamp instead of leaving a contradictory green result");
        });
    }
}
