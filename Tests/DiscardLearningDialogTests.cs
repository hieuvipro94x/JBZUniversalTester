using System.Reflection;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestDiscardLearningDialog()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (bool accept in new[] { true, false })
            {
                var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
                var dialog = new DiscardContactLearningWindow(vm);
                bool timeout = false;
                var errors = new List<Exception>();
                var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                void ForceClose()
                {
                    typeof(DiscardContactLearningWindow).GetField("_allowClose", flags)!.SetValue(dialog, true);
                    dialog.DialogResult = false;
                }
                deadline.Tick += (_, _) => { timeout = true; deadline.Stop(); ForceClose(); };
                DispatcherUnhandledExceptionEventHandler handler = (_, e) =>
                {
                    errors.Add(e.Exception); e.Handled = true;
                    dialog.Dispatcher.BeginInvoke(ForceClose);
                };
                dialog.Dispatcher.UnhandledException += handler;
                Task? feeding = null;
                dialog.Loaded += (_, _) => feeding = FeedAsync();
                async Task FeedAsync()
                {
                    while (!(bool)typeof(DiscardContactLearningWindow).GetField("_active", flags)!.GetValue(dialog)!)
                        await Task.Delay(5);
                    if (!accept) { dialog.Close(); return; }
                    board.Publish(FrameSeq(100)); board.Publish(FrameSeq(101));
                    board.Publish(FrameSeq(102, (41, [86]), (86, [41])));
                    board.Publish(FrameSeq(103, (41, [86]), (86, [41])));
                    board.Publish(FrameSeq(104)); board.Publish(FrameSeq(105));
                }
                deadline.Start();
                bool? result;
                try { result = dialog.ShowDialog(); }
                finally { deadline.Stop(); dialog.Dispatcher.UnhandledException -= handler; }
                if (feeding is not null) await feeding;
                Assert(!timeout && errors.Count == 0 && result == accept && !dialog.IsVisible,
                    "The real modal learning dialog closes once and returns its result even when scan cleanup finishes synchronously");
                Assert(accept ? dialog.FirstIo == 41 && dialog.SecondIo == 86 : dialog.FirstIo == 0 && dialog.SecondIo == 0,
                    "Accepted learning returns the two IOs to Settings; cancellation returns no learned IOs");
                Assert((int)typeof(JBZUniversalTester.ViewModels.TestViewModel).GetField("_topologyLearningActive", flags)!.GetValue(vm)! == 0 &&
                       vm.Total == 0 && !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                    "Dialog completion restores background scanning without counts or relay output");
            }
        });
    }
}
