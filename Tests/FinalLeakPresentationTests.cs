using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestRecoverableKeysightEquipmentFault()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = CreateTestViewModel(new ProductionSettings(), out FakeBoard board);
        var model = Model(("PAIR", new[] { 1, 2 }));
        typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
        var engine = (JBZUniversalTester.Services.TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetModel(model);
        board.Publish(FrameSeq(1, (1, new[] { 2 })));
        var error = new JBZUniversalTester.Services.KeysightEquipmentException(
            "VISA connection failed", new IOException("Simulated USB failure"));
        var recover = typeof(TestViewModel).GetMethod("HandleKeysightEquipmentErrorAsync", flags)!;
        var task = (Task)recover.Invoke(vm, new object[] { error })!;
        for (int sequence = 1; !task.IsCompleted && sequence < 1000; sequence++)
        {
            board.Publish(FrameSeq(sequence + 1, (1, new[] { 2 })));
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
        Assert(!vm.IsDeviceFault && vm.IsBoardConnected && vm.IsProductRemovalPending,
            $"Keysight errors retain D2XX and require removal without fatal device transition: fault={vm.IsDeviceFault}, connected={vm.IsBoardConnected}, removal={vm.IsProductRemovalPending}, state={vm.State}");
        Assert((int)typeof(TestViewModel).GetField("_deviceFaultDialogCount", flags)!.GetValue(vm)! == 0,
            "Keysight recovery cannot open the dialog that shuts down the app");
        Assert(!board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Equipment error cannot trigger a PASS marking relay");
    }

    private static void TestLeakWaitingWithNonLeakConnector()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = CreateTestViewModel(new ProductionSettings(), out FakeBoard board);
        var model = TopologyModel(
            new Terminal(1, "LEAK", "1", "2", "RET1"),
            new Terminal(2, "LEAK", "2", "2", "RET1"),
            new Terminal(3, "OTHER", "1", "2", "WIRE"),
            new Terminal(4, "OTHER", "2", "2", "WIRE"));
        typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
        typeof(TestViewModel).GetField("_waterProofProfile", flags)!.SetValue(vm,
            new WaterProofModelSettings { Enabled = true, Channel1Enabled = true,
                Channel1Connector = "LEAK", Channel2Enabled = false, Channel3Enabled = false });
        var engine = (JBZUniversalTester.Services.TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetModel(model);
        var waiting = typeof(TestViewModel).GetMethod("WaterProofConnectorWaitingState", flags)!;
        string Status() => (string)waiting.Invoke(vm, null)!;
        engine.ProcessFrame(FrameSeq(1, (1, new[] { 2 })));
        Assert(Status() == "LẮP SẢN PHẨM", "Leak connector alone keeps the Leak retry installation prompt");
        engine.ProcessFrame(FrameSeq(2, (3, new[] { 4 })));
        Assert(Status() == "ĐANG KIỂM TRA...", "A non-Leak connector installed first keeps testing status");
        engine.ProcessFrame(FrameSeq(3));
        Assert(Status() == "LẮP SẢN PHẨM", "Removing the non-Leak connector restores installation while waiting for Leak retry");
        Assert(!board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Waiting status projection must not actuate marking relays");
    }

    private static void TestFinalLeakResultTable()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        TestViewModel vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
        void SetField(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
        SetField("_model", Model(("PAIR", new[] { 1, 2 })));
        SetField("_masterApproved", true);
        SetField("_waterProofProfile", new WaterProofModelSettings
        {
            Enabled = true, Channel1Enabled = true, Channel2Enabled = true,
            Channel3Enabled = false, LeakLimit = 2
        });
        SetField("_lastWaterProofMeasurements", new WaterProofChannelMeasurement[]
        {
            new(2, true, 81.2, 80.8, 0.4, true),
            new(1, true, 84.1, 83.5, 0.6, true),
            new(3, false, 0, 0, 0, true)
        });
        MethodInfo show = typeof(TestViewModel).GetMethod("ShowFinalWaterProofResults", flags)!;
        show.Invoke(vm, null);
        Assert(vm.FinalWaterProofRows.Count == 0 && vm.SelectedOperationTabIndex == 0,
            "Leak result table cannot appear before both continuity and Leak pass");
        SetField("_preContinuityWaterProofPassed", 1);
        show.Invoke(vm, null);
        Assert(vm.FinalWaterProofRows.Count == 0, "Leak PASS alone cannot display the final production table");
        SetField("_cycleContinuityCompletedAt", DateTime.Now);
        Type phase = typeof(TestViewModel).GetNestedType("ProductionPhase", BindingFlags.NonPublic)!;
        SetField("_productionPhase", Convert.ToInt32(Enum.Parse(phase, "Completed")));
        vm.State = "PASS";
        show.Invoke(vm, null);
        Assert(vm.SelectedOperationTabIndex == 2 && vm.FinalWaterProofRows.Count == 2 &&
               vm.FinalWaterProofRows[0].Channel == 1 && vm.FinalWaterProofRows[1].Channel == 2,
            "Final table displays each enabled Leak channel in order");
        Assert(vm.FinalWaterProofRows[0].FirstPressureText == "84.1" &&
               vm.FinalWaterProofRows[0].SecondPressureText == "83.5" &&
               vm.FinalWaterProofRows[0].LeakText == "0.6" &&
               vm.FinalWaterProofRows.All(row => row.ResultText == "PASS"),
            "Final table preserves measured suction, hold pressure and leak values with per-channel PASS");
        Assert(!vm.IsCenterResultVisible && vm.ResultStatusText == "PASS",
            "Large center PASS does not cover the final Leak table; header still shows PASS");
        vm.SelectedOperationTabIndex = 0;
        Assert(vm.FinalWaterProofRows.Count == 0 && vm.IsCenterResultVisible,
            "Leaving the completed cycle hides and clears its Leak table and restores normal center presentation");
        SetField("_lastWaterProofMeasurements", new WaterProofChannelMeasurement[]
        {
            new(1, true, 84.1, 83.5, 0.6, true), new(2, true, 80, 75, 5, false)
        });
        show.Invoke(vm, null);
        Assert(vm.SelectedOperationTabIndex == 0 && vm.FinalWaterProofRows.Count == 0,
            "A failed Leak channel must never appear in a final PASS table");
        Assert(!board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Result-table presentation cannot trigger relay commands");
    }
}
