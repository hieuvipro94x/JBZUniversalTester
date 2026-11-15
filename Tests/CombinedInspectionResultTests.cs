using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestCombinedInspectionResults()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var (resistance, leak) in new[] { (true, true), (false, true), (true, false) })
        {
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0,
                ResistanceChannels = [new() { Name = "R1", Channel = 1, Enabled = resistance, MinOhm = 1, MaxOhm = 3 }] };
            var vm = CreateTestViewModel(settings, out FakeBoard board);
            var model = Model(("PAIR", new[] { 1, 2 }));
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            object? Call(string name, params object?[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
            Set("_model", model);
            Set("_masterApproved", true); // This fixture exercises production qualification.
            Set("_waterProofProfile", new WaterProofModelSettings { Enabled = leak,
                Channel1Enabled = true, Channel2Enabled = false, Channel3Enabled = false,
                Channel1Connector = "CN1", LeakLimit = 2 });
            Set("_lastWaterProofMeasurements", new WaterProofChannelMeasurement[] { new(1, true, 84.1, 83.5, 0.6, true) });
            Set("_preContinuityWaterProofPassed", leak ? 1 : 0);
            Set("_cycleContinuityCompletedAt", DateTime.Now);
            Call("PrepareResistanceRows", model);
            vm.SelectedOperationTabIndex = 1;
            Assert(vm.InspectionResultRows.Count == (resistance ? 1 : 0) + (leak ? 1 : 0) &&
                vm.HasResistanceResultRows == resistance && vm.HasLeakResultRows == leak,
                "Single table contains exactly the enabled measurement types, including Leak while resistance is running");
            if (resistance)
            {
                var display = vm.InspectionResultRows.First(row => row.IsResistance);
                bool notified = false;
                display.PropertyChanged += (_, _) => notified = true;
                Call("UpdateResistanceRows", (object)new ResistanceResult[] {
                    new() { Name = "R1", Channel = 1, ValueOhm = 2, IsStable = true, Passed = true, MeasurementStatus = "PASS" } });
                Assert(notified && display.Display.Contains('2') && display.ResultText == "PASS",
                    "Combined table receives live resistance value and PASS without another measurement or snapshot delay");
            }
            if (leak)
            {
                Call("ShowFinalWaterProofResults");
                var display = vm.InspectionResultRows.Last();
                Assert(display.Name == "TEST LEAK" && display.ChannelText == "CH1" &&
                    vm.FinalWaterProofRows[0].Connector == "CN1" && display.FirstPressureText == "84.1" &&
                    display.SecondPressureText == "83.5" && display.LeakText == "0.6" && display.ResultText == "PASS" &&
                    vm.InspectionResultRows.Count == (resistance ? 2 : 1),
                    "Final Leak display preserves all pressures and the preceding resistance rows in one table");
            }
            vm.SelectedOperationTabIndex = 0;
            vm.ResistanceDisplayRows.Clear();
            Assert(vm.InspectionResultRows.Count == 0 && !vm.HasLeakResultRows && !vm.HasResistanceResultRows &&
                !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                "Cycle cleanup clears the combined table without relay side effects");
            vm.FinalWaterProofRows.Add(new WaterProofChannelResult { Channel = 1, Enabled = true,
                IsMeasured = true, Passed = false, ResultTextOverride = "NG LEAK" });
            Assert(vm.InspectionResultRows.Single().ResultText == "NG LEAK",
                "Combined table preserves NG LEAK instead of presenting a false PASS");
        }
        OnSettingsDispatcher(async () =>
        {
            foreach (string language in new[] { "vi", "ko" })
            {
                var settings = new ProductionSettings { MasterFaultRequiredCount = 0, TestViewLanguage = language };
                var vm = CreateTestViewModel(settings, out FakeBoard board);
                typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, Model(("PAIR", new[] { 1, 2 })));
                typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(vm, true);
                vm.FinalWaterProofRows.Add(new WaterProofChannelResult { Channel = 1, Enabled = true, IsMeasured = true,
                    FirstResultPressure = 80, SecondResultPressure = 79.3, Leak = 0.7, LeakLimit = 0.2,
                    Passed = false, ResultTextOverride = "NG LEAK" });
                vm.SelectedOperationTabIndex = 2;
                var window = new JBZUniversalTester.Views.TestWindow(vm, autoStartProduction: false);
                var surface = (System.Windows.FrameworkElement)window.Content;
                window.Content = null;
                surface.DataContext = vm; surface.Resources.MergedDictionaries.Add(window.Resources);
                surface.Measure(new System.Windows.Size(1600, 900));
                surface.Arrange(new System.Windows.Rect(0, 0, 1600, 900));
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                surface.UpdateLayout();
                var grid = (System.Windows.Controls.DataGrid)window.FindName("ResistanceGrid");
                Assert(window.FindName("LeakLimitColumn") is null && !grid.Columns.OfType<System.Windows.Controls.DataGridBoundColumn>()
                    .Any(column => column.Binding is System.Windows.Data.Binding binding && binding.Path?.Path == "LeakLimitText") &&
                    grid.Columns.Count(column => column.Visibility == System.Windows.Visibility.Visible) == 6 &&
                    vm.InspectionResultRows.Single().LeakText == "0.7" && vm.InspectionResultRows.Single().ResultText == "NG LEAK" &&
                    vm.FinalWaterProofRows[0].LeakLimit == 0.2 && !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                    "Actual localized Leak table has six columns without a limit column; configured limits, measurements and NG are retained");
                typeof(JBZUniversalTester.Views.TestWindow).GetField("_allowClose", flags)!.SetValue(window, true);
                window.Close();
            }
        });
    }
}
