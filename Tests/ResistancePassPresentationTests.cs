using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestProductionResistancePassPresentation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var settings = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ResistanceChannels = [
                new() { Name = "R1", Channel = 1, Enabled = true, MinOhm = 1, MaxOhm = 3 },
                new() { Name = "R2", Channel = 2, Enabled = true, MinOhm = 1, MaxOhm = 3 }]
        };
        var vm = CreateTestViewModel(settings, out FakeBoard board);
        var model = Model(("PAIR", new[] { 1, 2 }));
        typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
        typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(vm, true);
        typeof(TestViewModel).GetField("_presentationCycleStarted", flags)!.SetValue(vm, true);
        typeof(TestViewModel).GetField("_runtimeMode", flags)!.SetValue(vm, 1);
        typeof(TestViewModel).GetField("_cycleActive", flags)!.SetValue(vm, true);
        typeof(TestViewModel).GetField("_cycleContinuityCompletedAt", flags)!.SetValue(vm, DateTime.Now);
        typeof(TestViewModel).GetField("_productionPhase", flags)!.SetValue(vm,
            (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, "Resistance"));
        void Call(string method, params object?[] args) => typeof(TestViewModel).GetMethod(method, flags)!.Invoke(vm, args);
        Call("PrepareResistanceRows", model);
        vm.SelectedOperationTabIndex = 1;
        vm.State = "KIỂM TRA ĐIỆN TRỞ";
        var r1 = new ResistanceResult { Name = "R1", Channel = 1, ValueOhm = 2, Passed = true, IsStable = true, MeasurementStatus = "PASS" };
        var r2 = new ResistanceResult { Name = "R2", Channel = 2, ValueOhm = 2.5, Passed = true, IsStable = true, MeasurementStatus = "PASS" };
        Call("UpdateProductionResistanceRows", (object)new ResistanceResult[] { r1 });
        Assert(vm.ResistanceDisplayRows[0].ResultText == "PASS" && vm.ResultStatusText == "ĐANG KIỂM TRA",
            "An earlier channel remains live while another resistance channel still needs measurement");
        bool sawSynchronizedPass = false;
        vm.ResistanceDisplayRows[1].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ResistanceResult.ValueOhm) ||
                (e.PropertyName == nameof(ResistanceResult.ResultText) && vm.ResistanceDisplayRows[1].ResultText == "PASS"))
            {
                Assert(vm.ResultStatusText == "PASS", "Final resistance PASS must never precede the large PASS status");
                sawSynchronizedPass = true;
            }
        };
        Call("UpdateProductionResistanceRows", (object)new ResistanceResult[] { r2 });
        Assert(sawSynchronizedPass && vm.Resistance[1].ValueOhm == 2.5 && vm.ResultStatusText == "PASS" &&
               vm.ResistanceDisplayRows.All(row => row.ResultText == "PASS") && !vm.IsCenterResultVisible,
            "Final numeric value, final resistance PASS and large PASS appear together before recovery without covering the table");
        Call("UpdateProductionResistanceRows", (object)new ResistanceResult[] { r1, r2 });
        Assert(vm.ResistanceDisplayRows.All(row => row.ResultText == "PASS"),
            "Consolidating measured results never flashes the PASS table back to measuring");
        Call("PublishProductionPassPresentation");
        Assert(sawSynchronizedPass && vm.ResistanceDisplayRows.All(row => row.ResultText == "PASS") && vm.ResultStatusText == "PASS",
            "Durable commit keeps the already measured PASS presentation");
        Call("PrepareResistanceRows", model);
        vm.State = "KIỂM TRA ĐIỆN TRỞ";
        Call("UpdateProductionResistanceRows", (object)new ResistanceResult[] { r1,
            new() { Name = "R2", Channel = 2, ValueOhm = 4, Passed = false, MeasurementStatus = "FAIL" } });
        Assert(vm.ResistanceDisplayRows[1].ResultText == "FAIL" && vm.ResultStatusText != "PASS",
            "An actual failed channel remains immediately visible and cannot publish product PASS");
        Call("PrepareResistanceRows", model);
        Call("UpdateResistanceRows", (object)new ResistanceResult[] { r1, r2 });
        Assert(vm.ResistanceDisplayRows.All(row => row.ResultText == "PASS") &&
               !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Master/manual measurement presentation is unchanged and display synchronization cannot pulse relays");

        // Use the real measurement engine to verify the timing boundary: its
        // callback must publish PASS before the board recovery is even entered.
        foreach (bool passed in new[] { true, false })
        {
            var app = new AppSettings();
            app.Test.ResistanceMinimumSettleMs = 0;
            app.Test.ResistanceSampleIntervalMs = 0;
            app.Test.ResistanceStableSampleCount = 2;
            var transport = new FakeBoard();
            var visa = new FakeKeysightVisaService(true, passed ? 2 : 4);
            using var engine = new TestEngine(transport, visa, app, settings);
            var live = new TestViewModel(new MainViewModel(), engine, transport, visa, new WaterProofSerialService(),
                app, settings, new LegacyPhtHistoryService(enabled: false), false,
                passSoundPlayback: _ => Task.CompletedTask);
            void Set(string field, object value) => typeof(TestViewModel).GetField(field, flags)!.SetValue(live, value);
            Set("_model", model);
            engine.SetModel(model);
            Set("_masterApproved", true);
            Set("_runtimeMode", 1);
            Set("_cycleActive", true);
            Set("_cycleContinuityCompletedAt", DateTime.Now);
            Set("_productionPhase", (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, "Resistance"));
            typeof(TestViewModel).GetMethod("PrepareResistanceRows", flags)!.Invoke(live, [model]);
            live.SelectedOperationTabIndex = 1;
            live.State = "KIỂM TRA ĐIỆN TRỞ";
            bool observedRecovery = false;
            transport.BeforeReleaseResistance = () =>
            {
                observedRecovery = true;
                Assert((live.ResultStatusText == "PASS") == passed && live.ResistanceDisplayRows.All(row => row.Passed == passed) &&
                       !transport.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                    "Measured result is visible before recovery, while relay execution remains blocked");
            };
            engine.MeasureResistanceAsync(result => typeof(TestViewModel).GetMethod("UpdateProductionResistanceRows", flags)!
                .Invoke(live, [new ResistanceResult[] { result }])).GetAwaiter().GetResult();
            Assert(observedRecovery && transport.ReleaseResistanceRouteCount == 1,
                "Early measured PASS never skips board recovery");
        }
    }
}
