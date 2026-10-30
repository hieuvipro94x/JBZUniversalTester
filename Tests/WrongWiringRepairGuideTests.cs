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
    private static void TestWrongWiringRepairGuide()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out var board);
            var model = TopologyModel(
                new Terminal(1, "CN1", "1", "2", "W01", "R"),
                new Terminal(2, "CN2", "1", "2", "W01", "R"),
                new Terminal(3, "CN1", "2", "2", "W02", "B"),
                new Terminal(4, "CN2", "2", "2", "W02", "B"));
            vm.SetModel(model);
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            Set("_masterApproved", true);
            Set("_masterSequenceState", MasterSequenceState.Completed);
            var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
            var window = new TestWindow(vm, autoStartProduction: false);
            var surface = (FrameworkElement)window.Content;
            window.Content = null; // No Loaded or physical-device startup.
            surface.DataContext = vm;
            surface.Resources.MergedDictionaries.Add(window.Resources);
            int commands = board.Commands.Count;
            long sequence = 100;
            async Task Feed(params (int Source, int[] Targets)[] edges)
            {
                Set("_runtimeMode", 0);
                engine.SetModel(model);
                engine.SetFrameProcessingEnabled(true);
                engine.ProcessFrame(FrameSeq(sequence++, edges), false);
                await Task.Delay(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs + 60);
                engine.ProcessFrame(FrameSeq(sequence++, edges), false);
                Set("_runtimeMode", 1);
                typeof(TestViewModel).GetMethod("RefreshFaults", flags)!.Invoke(vm, null);
                surface.Measure(new Size(1600, 900));
                surface.Arrange(new Rect(0, 0, 1600, 900));
                await Dispatcher.Yield(DispatcherPriority.Background);
                surface.UpdateLayout();
            }
            try
            {
                await Feed((1, [4]), (4, [1]));
                Assert(vm.Faults.Select(row => row.OperatorStatus).SequenceEqual(
                    ["ĐẤU SAI DÂY", "ĐANG CẮM NHẦM", "DÂY ĐÚNG"]) &&
                    vm.Faults.Select(row => (row.Connector, row.Pin, row.WireName)).SequenceEqual(
                        [("CN1", "1", "W01"), ("CN2", "2", "W02"), ("CN2", "1", "W01")]),
                    "One wrong wire identifies its source, mistaken connector/pin/wire and correct destination");
                var grid = (DataGrid)window.FindName("FaultGrid");
                Assert(!vm.ShowFaultIoColumns && grid.Columns[1].Visibility == Visibility.Collapsed &&
                    grid.Columns[8].Visibility == Visibility.Collapsed &&
                    ((System.Windows.Data.Binding)((DataGridBoundColumn)grid.Columns[7]).Binding).Path.Path == "OperatorStatus",
                    "Actual TestView prioritizes connector/pin/wire and hides technical IO only for fully mapped repair guides");
                Assert(vm.Faults.Select(row => row.Status).SequenceEqual(["SAI DÂY", "CHẬP MẠCH", "HỞ MẠCH"]),
                    "Operator guidance preserves canonical fault kinds, statuses and diagnostic data");
                await Feed((40, [2]), (2, [40]));
                Assert(vm.Faults[0].Io == 2 && vm.Faults[1].Io == 40 && vm.Faults[2].Io == 1 && vm.ShowFaultIoColumns,
                    "Target-side wrong wiring starts at the connected endpoint and retains IO for an unknown connector");
                await Feed((1, [2]), (2, [1]), (3, [4]), (4, [3]), (7, [8]), (8, [7]));
                Assert(vm.Faults.Count == 2 && vm.Faults.All(row => !row.IsWrongWiringGuide &&
                    row.OperatorStatus == row.Status && row.OperatorFaultType == row.FaultType && row.Status == "CHẬP MẠCH") &&
                    vm.ShowFaultIoColumns && grid.Columns[1].Visibility == Visibility.Visible && grid.Columns[8].Visibility == Visibility.Visible,
                    "A real short retains the original two rows, labels and IO columns after switching from a wrong-wire guide");
                Assert(board.Commands.Count == commands && vm.Total == 0 && vm.Pass == 0 && vm.Fail == 0,
                    "Repair-guide rendering does not send hardware commands or increment production");
                foreach (string label in new[] { "ĐẤU SAI DÂY", "ĐANG CẮM NHẦM", "DÂY ĐÚNG" })
                    Assert(TestViewText.Translate(label, "ko") != label, "Repair roles support Korean without translating product data");
            }
            finally
            {
                AppSoundService.Current.StopAll(force: true);
                typeof(TestWindow).GetField("_allowClose", flags)!.SetValue(window, true);
                window.Close();
            }
        });
    }
}
