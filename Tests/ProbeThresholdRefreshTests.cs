using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using JBZUniversalTester.Models;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestProbeThresholdRefreshAfterSettingsSave()
    {
        OnSettingsDispatcher(async () =>
        {
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0, ProbeReplacementThreshold = 200000 };
            var vm = CreateTestViewModel(settings, out FakeBoard board);
            var apply = typeof(TestViewModel).GetMethod("ApplyPartCounter", BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(ProbeCounterSnapshot)], null)!;
            apply.Invoke(vm, [new ProbeCounterSnapshot("PIN-TEST", 200000, 220)]);
            var text = new TextBlock { DataContext = vm };
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(TestViewModel.ProbeCycleText)) { Mode = BindingMode.OneWay });
            Assert(text.Text == "220/200.000", "TestView starts with the accumulated PIN count and current threshold");
            int commandsBefore = board.Commands.Count;
            bool scanningBefore = board.IsScanning;
            settings.ProbeReplacementThreshold = 150000;
            vm.RefreshProductionSettingsOnly();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Assert(text.Text == "220/150.000" && vm.ProbeReplacementThreshold == 150000 && vm.ProbeCycleCount == 220,
                "Saving a new PIN threshold immediately refreshes the bound TestView text without resetting count");
            apply.Invoke(vm, [new ProbeCounterSnapshot("PIN-TEST", 200000, 220)]);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Assert(text.Text == "220/150.000", "A delayed old counter snapshot cannot restore the previous threshold");
            settings.ProbeReplacementThreshold = 200;
            vm.RefreshProductionSettingsOnly();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Assert(vm.ProbeMaintenanceDue && text.Text == "220/200", $"Lowering the threshold immediately updates the maintenance warning: text={text.Text}, count={vm.ProbeCycleCount}, threshold={vm.ProbeReplacementThreshold}, due={vm.ProbeMaintenanceDue}");
            settings.ProbeReplacementThreshold = 500;
            vm.RefreshProductionSettingsOnly();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Assert(!vm.ProbeMaintenanceDue && text.Text == "220/500" && vm.ProbeCycleCount == 220 &&
                   board.Commands.Count == commandsBefore && board.IsScanning == scanningBefore,
                "Raising the threshold clears the warning without restarting scans, pulsing relays or resetting counts");
        });
    }
}
