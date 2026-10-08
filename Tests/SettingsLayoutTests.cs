using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSettingsLongLeakErrorLayout()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new ProductionSettingsViewModel();
                var page = new ProductionSettingsPage(null, vm);
                void Layout()
                {
                    for (int pass = 0; pass < 4; pass++)
                    {
                        page.Measure(new Size(1800, 1050));
                        page.Arrange(new Rect(0, 0, 1800, 1050));
                        page.UpdateLayout();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    }
                }
                Layout();
                var scroll = (ScrollViewer)page.FindName("SettingsScrollViewer");
                var relay = (Border)page.FindName("RelayLeakMainPanel");
                double before = relay.ActualWidth;
                typeof(ProductionSettingsViewModel).GetProperty("ManualWaterProofStatus")!.SetValue(vm,
                    "TEST LEAK LỖI • " + string.Concat(Enumerable.Repeat(
                        "Không nhận được phản hồi hợp lệ từ máy Leak trên COM7. Kiểm tra cáp/kết nối máy Leak. ", 8)));
                Layout();
                Assert(relay.ActualWidth <= before + 1,
                    $"A long Leak error must not widen the settings column: before={before}, after={relay.ActualWidth}, viewport={scroll.ViewportWidth}, extent={scroll.ExtentWidth}");
                Assert(scroll.ExtentWidth <= scroll.ViewportWidth + 1,
                    "Leak errors wrap without introducing horizontal overflow on a wide settings page");
                page.ReleasePageResources();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Settings layout regression", failure);
    }
}
