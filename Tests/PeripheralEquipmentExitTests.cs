using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestManualPeripheralCapacityIsolation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (string operation in new[] { "Leak", "Keysight", "Manual" })
        {
            int faultReports = 0;
            var visa = new FakeKeysightVisaService(connected: true, measurement: 10) { ThrowOnMeasure = true };
            var settings = new ProductionSettings { ExpansionCardCount = 2, MasterFaultRequiredCount = 0 };
            var vm = CreateTestViewModel(settings, out FakeBoard board, visa: visa,
                waterProofRun: (_, _) => Task.FromException<WaterProofRunResult>(new FileNotFoundException("Missing Leak COM7")),
                hardwareFaultReport: (_, _, _) => faultReports++);
            board.CapacityValidationSettings = settings;
            // Exact V414 log case: 219 populated rows, highest IO224, but only 128 installed IO.
            typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, Model(("PAIR", new[] { 1, 224 })));
            board.ConfigureActiveScanRange(224); board.SetAppliedScanCapacityForTest(2);
            Exception? error = null;
            try
            {
                Task work = operation == "Leak"
                    ? vm.TestManualWaterProofAsync(new WaterProofMachineSettings { PortName = "COM7" },
                        new WaterProofModelSettings { Channel1Enabled = true, Channel2Enabled = false, Channel3Enabled = false })
                    : operation == "Keysight"
                        ? vm.MeasureManualResistanceAsync([new ResistanceStep("R1", 1, 0, 100)])
                        : RunManual();
                work.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                async Task RunManual() { await vm.EnterManualModeAsync(); await vm.ExitManualModeAsync(); }
            }
            catch (Exception ex) { error = ex; }
            Assert(!vm.IsDeviceFault && vm.DeviceFaultDialogCount == 0 && faultReports == 0 &&
                   board.IsConnected && board.ConnectAttempts == 0 && !vm.IsManualModeActive,
                $"{operation}: invalid model capacity must not reopen/disconnect the connected board or request application exit");
            Assert(!board.ScanCapacity.IsModelWithinInstalledCapacity && !board.Commands.Contains("START") &&
                   !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                $"{operation}: undersized hardware cannot start model testing or fire a result relay");
            Assert(operation == "Manual" ? error is null : operation == "Leak" ? error is FileNotFoundException :
                   error?.GetType().Name == "ManualResistanceKeysightException",
                $"{operation}: cleanup retains the original peripheral error and allows the operator to correct settings");
            settings.ExpansionCardCount = 4;
            board.StartScanCallback = current => current.Publish(FrameSeq(300));
            ((Task)typeof(TestViewModel).GetMethod("EnsureContinuousProductionScanAsync", flags)!.Invoke(vm, null)!)
                .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert(board.IsConnected && board.IsScanning && board.ScanCapacity.IsModelWithinInstalledCapacity &&
                   board.ConnectAttempts == 0 && !vm.IsDeviceFault && faultReports == 0 && vm.Pass == 0 && vm.Fail == 0,
                $"{operation}: correcting card configuration resumes scanning in place without app restart or production counts");
        }
    }

    private static void TestPeripheralEquipmentRecoveryCancellation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (bool leak in new[] { false, true })
        {
            var reports = new List<string>();
            var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board,
                waterProofRun: (_, _) => Task.FromException<WaterProofRunResult>(new IOException("Leak COM unplugged")),
                hardwareFaultReport: (_, source, _) => reports.Add(source));
            var model = Model(("PAIR", new[] { 1, 2 }));
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
            Set("_model", model);
            engine.SetModel(model);
            Set("_runtimeMode", 1);
            Set("_masterApproved", true);
            using var cycle = new CancellationTokenSource();
            Set("_cycleCts", cycle);
            board.StartScanCallback = _ => cycle.Cancel();
            Task recovery;
            if (leak)
            {
                Set("_waterProofProfile", new WaterProofModelSettings
                {
                    Enabled = true, Channel1Enabled = true, Channel2Enabled = false, Channel3Enabled = false
                });
                long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
                recovery = (Task)typeof(TestViewModel).GetMethod("RunAutomaticWaterProofAsync", flags)!
                    .Invoke(vm, [model, generation, cycle.Token, false, null, null])!;
            }
            else
            {
                recovery = (Task)typeof(TestViewModel).GetMethod("HandleKeysightEquipmentErrorAsync", flags)!
                    .Invoke(vm, [new KeysightEquipmentException("Keysight USB unplugged", new IOException())])!;
            }
            try { recovery.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (cycle.IsCancellationRequested) { }
            Assert(cycle.IsCancellationRequested && !vm.IsDeviceFault && vm.DeviceFaultDialogCount == 0 && reports.Count == 0,
                $"{(leak ? "Leak" : "Keysight")} canceled recovery cannot create a board crash or exit dialog");
            board.StartScanCallback = null;
            board.Publish(FrameSeq(100, (1, [2])));
            Assert(!vm.StopViewAsync().GetAwaiter().GetResult(), "Peripheral failure still blocks Main while a product is connected");
            board.Publish(FrameSeq(101)); board.Publish(FrameSeq(102));
            int commands = board.Commands.Count;
            Assert(vm.StopViewAsync().GetAwaiter().GetResult() && !vm.IsDeviceFault && board.Commands.Count == commands,
                "After two confirmed empty frames Main remains available without hardware commands");
        }

        var visa = new FakeKeysightVisaService(connected: true, measurement: 10) { ThrowOnMeasure = true };
        var manual = CreateTestViewModel(new ProductionSettings(), out FakeBoard manualBoard, visa: visa);
        manualBoard.StartScanCallback = current => current.Publish(FrameSeq(200));
        Exception? error = null;
        try { manual.MeasureManualResistanceAsync([new ResistanceStep("R1", 1, 0, 100)])
            .WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch (Exception ex) { error = ex; }
        Assert(error?.GetType().Name == "ManualResistanceKeysightException" &&
               !manual.IsDeviceFault && manual.DeviceFaultDialogCount == 0 && !manual.IsManualModeActive &&
               !(bool)typeof(TestViewModel).GetField("_waterProofEquipmentErrorAwaitingRemoval", flags)!.GetValue(manual)!,
            "Manual Keysight read errors stay in Settings cleanup without starting a production removal recovery");

        int boardReports = 0;
        var disconnected = CreateTestViewModel(new ProductionSettings(), out FakeBoard disconnectedBoard,
            hardwareFaultReport: (_, _, _) => boardReports++);
        disconnectedBoard.SetConnectionStateForTest(false);
        typeof(TestViewModel).GetMethod("ReportDeviceFaultForTest", flags)!
            .Invoke(disconnected, [new KeysightEquipmentException("USB measurement failed", new IOException()), -1]);
        Assert(disconnected.IsDeviceFault && disconnected.DeviceFaultTransitionCount == 1 && boardReports == 1,
            "An independently disconnected D2XX board still follows its fatal safety/reporting path");
    }
}
