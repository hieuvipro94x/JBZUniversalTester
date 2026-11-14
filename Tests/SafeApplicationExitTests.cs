using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private sealed class ExitAudit
    {
        public int Confirmations, ProductBlocks, CleanupCalls;
        public Func<bool> Confirm = () => false;
        public readonly TaskCompletionSource CleanupGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static void TestSafeApplicationExit()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var vm = new MainViewModel(new AppSettings(), new ProductionSettings { MasterFaultRequiredCount = 0 });
            var audit = new ExitAudit();
            var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), flags, null,
                [vm, (Func<bool>)(() => { audit.Confirmations++; return audit.Confirm(); }),
                 (Action)(() => audit.ProductBlocks++),
                 (Func<Task>)(() => { audit.CleanupCalls++; return audit.CleanupGate.Task; })], null)!;
            var closing = typeof(MainWindow).GetMethod("MainWindow_Closing", flags)!;
            var pending = typeof(TestViewModel).GetField("_productRemovalPending", flags)!;
            CancelEventArgs RequestClose()
            {
                var args = new CancelEventArgs();
                closing.Invoke(window, [window, args]); // Same Closing event as Alt+F4, WM_CLOSE and ExitCommand.
                return args;
            }
            bool Closed = false;
            window.Closed += (_, _) => Closed = true;
            try
            {
                Assert(RequestClose().Cancel && audit.Confirmations == 1 && audit.CleanupCalls == 0,
                    "Declining an Alt+F4/Close request leaves the application and hardware session running");
                pending.SetValue(vm.Test, 1);
                Assert(RequestClose().Cancel && audit.ProductBlocks == 1 && audit.Confirmations == 1,
                    "A product on the test table blocks exit before confirmation or cleanup");
                pending.SetValue(vm.Test, 0);
                audit.Confirm = () => { pending.SetValue(vm.Test, 1); return true; };
                Assert(RequestClose().Cancel && audit.ProductBlocks == 2 && audit.CleanupCalls == 0,
                    "A product appearing while confirmation pumps UI events is rechecked and blocks exit");
                pending.SetValue(vm.Test, 0);
                audit.Confirm = () => { Assert(RequestClose().Cancel, "Nested Close is canceled"); return false; };
                int prompts = audit.Confirmations;
                Assert(RequestClose().Cancel && audit.Confirmations == prompts + 1,
                    "Repeated Alt+F4 cannot stack confirmation dialogs");
                var test = new TestWindow(vm.Test, autoStartProduction: false);
                typeof(MainWindow).GetField("_testWindow", flags)!.SetValue(window, test);
                prompts = audit.Confirmations;
                Assert(RequestClose().Cancel && audit.Confirmations == prompts && audit.CleanupCalls == 0,
                    "Closing the main owner cannot bypass TestView's own removal and stop sequence");
                typeof(MainWindow).GetField("_testWindow", flags)!.SetValue(window, null);
                typeof(TestWindow).GetField("_allowClose", flags)!.SetValue(test, true);
                test.Close();
                audit.Confirm = () => true;
                Assert(RequestClose().Cancel, "The first confirmed close is canceled until cleanup finishes");
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(audit.CleanupCalls == 1 && !Closed &&
                       !((FrameworkElement)window.FindName("MainActionBar")).IsEnabled &&
                       !((FrameworkElement)window.FindName("ProductCatalog")).IsEnabled,
                    "Confirmed exit keeps the window alive and disables new work while cleanup is pending");
                Assert(RequestClose().Cancel && audit.CleanupCalls == 1,
                    "Repeated Alt+F4 during shutdown cannot start another cleanup");
                bool heartbeat = false;
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => heartbeat = true);
                Assert(heartbeat && !Closed, "Safe shutdown awaits pending work without blocking UI input dispatch");
                audit.CleanupGate.TrySetResult();
                for (int i = 0; i < 100 && !Closed; i++) await Task.Delay(5);
                Assert(Closed && audit.CleanupCalls == 1,
                    "The final close happens once and only after the cleanup task completes");
            }
            finally
            {
                pending.SetValue(vm.Test, 0);
                audit.CleanupGate.TrySetResult();
                typeof(MainWindow).GetField("_shutdownComplete", flags)!.SetValue(window, true);
                window.Close();
                await vm.ShutdownAsync();
            }
        });
    }
}
