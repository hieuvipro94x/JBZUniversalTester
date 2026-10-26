using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
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
