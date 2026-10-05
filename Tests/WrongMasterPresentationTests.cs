using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestWrongMasterLiveWireTable()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        TestViewModel vm = CreateTestViewModel(new ProductionSettings
        {
            MasterFaultRequiredCount = 99, MasterSelectedFaultSamples = MasterSampleSelection.WrongWiring
        });
        LoadReadyModel(vm, Model(("FIRST", new[] { 1, 2 }), ("SECOND", new[] { 3, 4 }), ("THIRD", new[] { 5, 6 })));
        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(vm, MasterSequenceState.TestingBadMaster);
        typeof(TestViewModel).GetField("_masterSampleType", flags)!.SetValue(vm, MasterSampleType.WrongWiring);
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        typeof(TestViewModel).GetMethod("TryHandleContinuityPreviewFrame", flags)!
            .Invoke(vm, [ContinuityPreviewFrame(100, 1, new[] { 3 })]);
        Assert(vm.Faults.Any(row => row.WireName == "THIRD") && !vm.MasterApproved,
            "The first wrong SOURCE contact presents pending wires without approving the Master");
        var details = (Dictionary<MasterFaultKey, FaultDetail>)typeof(TestViewModel)
            .GetField("_masterDetectedFaultDetails", flags)!.GetValue(vm)!;
        var detail = new FaultDetail
        {
            Type = ProductFaultType.WrongWiring, ExpectedSourceIo = 1, ExpectedTargetIo = 2,
            ActualSourceIo = 1, ActualTargetIo = 3, RelatedIos = [1, 2, 3], WireName = "FIRST"
        };
        details[MasterFaultKey.From(detail)] = detail;
        typeof(TestViewModel).GetMethod("RefreshFaults", flags)!.Invoke(vm, null);
        Assert(vm.Faults.Any(row => row.WireName == "THIRD") && details.Count == 1 && !vm.MasterApproved,
            "Detecting a Wrong Master fault does not replace the whole table with only collected fault details");
        engine.ClearContinuityPreview();
        engine.ProcessFrame(FrameSeq(101, (1, new[] { 3 }), (5, new[] { 6 })), false);
        typeof(TestViewModel).GetMethod("RefreshFaults", flags)!.Invoke(vm, null);
        Assert(!vm.Faults.Any(row => row.WireName == "THIRD" && row.Kind == FaultKind.MissingConnection),
            "Master wire rows continue updating when another connector is fitted");
    }
}
