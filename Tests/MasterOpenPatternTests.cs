using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestAllMasterWireRowsAndOpenPattern()
    {
        ProductModel model = Model(("A", new[] { 1, 2 }), ("B", new[] { 3, 4 }),
            ("C", new[] { 5, 6 }), ("D", new[] { 7, 8 }));
        using TestEngine engine = CreateEngine(out _);
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(1, (1, new[] { 2 }), (3, Array.Empty<int>()),
            (5, Array.Empty<int>()), (7, Array.Empty<int>())), false);
        Assert(engine.GetOpenMasterFaults().Count == 3 && !engine.CanConfirmOpenMasterSample(1) && !engine.CanConfirmOpenMasterSample(2),
            "A partially installed sample with three missing connections cannot satisfy one/two open faults");
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(2, (1, new[] { 2 }), (3, new[] { 4 }),
            (5, Array.Empty<int>()), (7, Array.Empty<int>())), false);
        Assert(engine.CanConfirmOpenMasterSample(2) && !engine.CanConfirmOpenMasterSample(1),
            "Exactly two missing connections and all other expected connections fitted accepts only a two-open sample");
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(3, (1, new[] { 2 }), (3, new[] { 4 }),
            (5, new[] { 6 }), (7, Array.Empty<int>())), false);
        Assert(engine.CanConfirmOpenMasterSample(1) && !engine.CanConfirmOpenMasterSample(2),
            "Exactly one missing connection accepts only a one-open sample");
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(4, (1, new[] { 2 }), (3, new[] { 4 }), (5, new[] { 6 })), false);
        Assert(engine.GetOpenMasterFaults().Count == 1 && !engine.CanConfirmOpenMasterSample(1),
            "An apparently correct missing count cannot confirm without the last THT source being scanned");
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(5, (1, new[] { 3 }), (3, new[] { 4 }),
            (5, new[] { 6 }), (7, Array.Empty<int>())), false);
        Assert(!engine.CanConfirmOpenMasterSample(1), "Wrong wiring cannot qualify as a confirmed open sample");
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(6, (1, Array.Empty<int>()), (3, Array.Empty<int>()),
            (5, Array.Empty<int>()), (7, Array.Empty<int>())), false);
        Assert(!engine.CanConfirmOpenMasterSample(4) && !engine.CanConfirmOpenMasterSample(0),
            "An absent product or zero required faults cannot qualify as an open sample");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (MasterSampleType sample in new[] { MasterSampleType.WrongWiring, MasterSampleType.ShortCircuit, MasterSampleType.OpenCircuit })
        {
            TestViewModel vm = CreateTestViewModel(new ProductionSettings
            {
                MasterFaultRequiredCount = 99, MasterSelectedFaultSamples = MasterSampleSelection.All
            });
            LoadReadyModel(vm, model);
            vm.StartProductionTestAsync().GetAwaiter().GetResult();
            typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(vm, MasterSequenceState.TestingBadMaster);
            typeof(TestViewModel).GetField("_masterSampleType", flags)!.SetValue(vm, sample);
            typeof(TestViewModel).GetMethod("TryHandleContinuityPreviewFrame", flags)!
                .Invoke(vm, [ContinuityPreviewFrame(100, 1, new[] { 2 })]);
            var details = (Dictionary<MasterFaultKey, FaultDetail>)typeof(TestViewModel)
                .GetField("_masterDetectedFaultDetails", flags)!.GetValue(vm)!;
            var fault = new FaultDetail
            {
                Type = MasterSampleCatalog.FaultType(sample), ExpectedSourceIo = 3, ExpectedTargetIo = 4,
                ActualSourceIo = 3, ActualTargetIo = 5, RelatedIos = [3, 4, 5], WireName = "B"
            };
            details[MasterFaultKey.From(fault)] = fault;
            typeof(TestViewModel).GetField("_masterWaterProofSequenceActive", flags)!.SetValue(vm, 1);
            typeof(TestViewModel).GetMethod("RefreshFaults", flags)!.Invoke(vm, null);
            Assert(vm.Faults.Any(row => row.WireName == "D") && !vm.MasterApproved,
                $"{sample} retains the full live installation table, including when Master Leak owns validation");
        }
    }
}
