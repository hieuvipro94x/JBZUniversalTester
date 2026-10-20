using System.Reflection;
using JBZUniversalTester.Models;
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
        void Call(string method, params object?[] args) => typeof(TestViewModel).GetMethod(method, flags)!.Invoke(vm, args);
        Call("PrepareResistanceRows", model);
        vm.SelectedOperationTabIndex = 1;
        vm.State = "KIỂM TRA ĐIỆN TRỞ";
        var r1 = new ResistanceResult { Name = "R1", Channel = 1, ValueOhm = 2, Passed = true, IsStable = true, MeasurementStatus = "PASS" };
        var r2 = new ResistanceResult { Name = "R2", Channel = 2, ValueOhm = 2.5, Passed = true, IsStable = true, MeasurementStatus = "PASS" };
        Call("UpdateProductionResistanceRows", (object)new ResistanceResult[] { r1 });
        Assert(vm.ResistanceDisplayRows[0].ResultText == "PASS" && vm.ResultStatusText == "ĐANG KIỂM TRA",
            "An earlier channel remains live while another resistance channel still needs measurement");
        Call("UpdateProductionResistanceRows", (object)new ResistanceResult[] { r2 });
        Assert(vm.Resistance.All(row => row.Passed) && vm.Resistance[1].ValueOhm == 2.5 &&
               vm.ResistanceDisplayRows[1].ResultText == "ĐANG ĐO" && vm.ResultStatusText == "ĐANG KIỂM TRA",
            "Final measured values stay accurate but the last visual PASS waits for the committed production result");
        Call("UpdateProductionResistanceRows", (object)new ResistanceResult[] { r1, r2 });
        Assert(vm.ResistanceDisplayRows[0].ResultText == "PASS" && vm.ResistanceDisplayRows[1].ResultText == "ĐANG ĐO",
            "Final measurement consolidation cannot make an earlier PASS flash back to measuring");
        bool sawSynchronizedPass = false;
        vm.ResistanceDisplayRows[1].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ResistanceResult.ResultText) && vm.ResistanceDisplayRows[1].ResultText == "PASS")
            {
                Assert(vm.ResultStatusText == "PASS", "Final resistance PASS must never precede the large PASS status");
                sawSynchronizedPass = true;
            }
        };
        Call("PublishProductionPassPresentation");
        Assert(sawSynchronizedPass && vm.ResistanceDisplayRows.All(row => row.ResultText == "PASS") && vm.ResultStatusText == "PASS",
            "Publishing the committed result updates final resistance PASS and large PASS in one UI action");
        Assert(!System.Text.Json.JsonSerializer.Serialize(vm.Resistance[1]).Contains("IsPassPresentationPending", StringComparison.Ordinal),
            "The presentation latch never enters serialized measurement data");
        Call("PrepareResistanceRows", model);
        vm.State = "KIỂM TRA ĐIỆN TRỞ";
        Call("UpdateProductionResistanceRows", (object)new ResistanceResult[] { r1,
            new() { Name = "R2", Channel = 2, ValueOhm = 4, Passed = false, MeasurementStatus = "FAIL" } });
        Assert(vm.ResistanceDisplayRows[1].ResultText == "FAIL" && vm.ResultStatusText != "PASS",
            "An actual failed channel remains immediately visible and cannot publish product PASS");
        Call("PrepareResistanceRows", model);
        Call("UpdateResistanceRows", (object)new ResistanceResult[] { r1, r2 });
        Assert(vm.ResistanceDisplayRows.All(row => row.ResultText == "PASS" && !row.IsPassPresentationPending) &&
               !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Master/manual measurement presentation is unchanged and display synchronization cannot pulse relays");
    }
}
