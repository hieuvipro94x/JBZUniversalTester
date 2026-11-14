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
        public int ProductBlocks, CleanupCalls;
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
                [vm, (Action)(() => audit.ProductBlocks++),
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
                pending.SetValue(vm.Test, 1);
                Assert(RequestClose().Cancel && audit.ProductBlocks == 1 && audit.CleanupCalls == 0,
                    "A product on the test table still blocks unsafe exit");
                pending.SetValue(vm.Test, 0);
                var test = new TestWindow(vm.Test, autoStartProduction: false);
                typeof(MainWindow).GetField("_testWindow", flags)!.SetValue(window, test);
                Assert(RequestClose().Cancel && audit.CleanupCalls == 0,
                    "Closing the main owner cannot bypass TestView's own removal and stop sequence");
                typeof(MainWindow).GetField("_testWindow", flags)!.SetValue(window, null);
                typeof(TestWindow).GetField("_allowClose", flags)!.SetValue(test, true);
                test.Close();
                Assert(RequestClose().Cancel, "Exit starts without confirmation and keeps the window alive until cleanup finishes");
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(audit.CleanupCalls == 1 && !Closed &&
                       !((FrameworkElement)window.FindName("MainActionBar")).IsEnabled &&
                       !((FrameworkElement)window.FindName("ProductCatalog")).IsEnabled,
                    "Exit keeps the window alive and disables new work while cleanup is pending");
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
    private static void TestBoardFaultSafeApplicationExit()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var vm = new MainViewModel(new AppSettings(), new ProductionSettings { MasterFaultRequiredCount = 0 });
            var audit = new ExitAudit();
            var window = (MainWindow)Activator.CreateInstance(typeof(MainWindow), flags, null,
                [vm, (Action)(() => audit.ProductBlocks++),
                 (Func<Task>)(() => { audit.CleanupCalls++; return audit.CleanupGate.Task; })], null)!;
            var request = typeof(MainWindow).GetMethod("ViewModel_BoardFaultExitRequested", flags)!;
            bool closed = false;
            window.Closed += (_, _) => closed = true;
            var test = new TestWindow(vm.Test, autoStartProduction:false);
            typeof(MainWindow).GetField("_testWindow",flags)!.SetValue(window,test);
            typeof(TestViewModel).GetField("_productRemovalPending",flags)!.SetValue(vm.Test,1);
            typeof(TestViewModel).GetField("_deviceFault",flags)!.SetValue(vm.Test,1);
            try
            {
                request.Invoke(window,[vm.Test,EventArgs.Empty]);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(audit.CleanupCalls==0,"A board-fault exit cannot run before its history is secured");
                typeof(TestViewModel).GetField("_boardFaultDataSecured",flags)!.SetValue(vm.Test,1);
                request.Invoke(window,[vm.Test,EventArgs.Empty]);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(audit.CleanupCalls==1 && audit.ProductBlocks==0 && !closed,
                    "The secured device-fault path automatically exits despite stale TestView/product state, while still awaiting cleanup");
                audit.CleanupGate.TrySetResult();
                for(int i=0;i<100 && !closed;i++) await Task.Delay(5);
                Assert(closed,"Device fault closes Main only after cleanup and never waits for a second operator confirmation");
            }
            finally
            {
                audit.CleanupGate.TrySetResult();
                typeof(MainWindow).GetField("_shutdownComplete",flags)!.SetValue(window,true);
                window.Close();
                typeof(TestWindow).GetField("_allowClose",flags)!.SetValue(test,true);
                test.Close();
                await vm.ShutdownAsync();
            }
        });
    }

}
