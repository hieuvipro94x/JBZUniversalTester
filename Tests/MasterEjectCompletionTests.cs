using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestMasterRemovalDuringEjectCompletion()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        TestViewModel vm = CreateTestViewModel(new ProductionSettings
        {
            MasterFaultRequiredCount = 1, MasterSelectedFaultSamples = MasterSampleSelection.WrongWiring
        });
        LoadReadyModel(vm, Model(("PAIR", new[] { 1, 2 })));
        void SetField(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
        SetField("_runtimeMode", 1);
        SetField("_masterSequenceState", MasterSequenceState.EjectingBadMaster);
        SetField("_masterSampleType", MasterSampleType.WrongWiring);
        SetField("_masterGoodVerified", true);
        SetField("_masterBadVerified", true);
        SetField("_masterRequiredFaultCount", 1);
        SetField("_masterValidationProductionDay", MasterSampleCatalog.ProductionDay(DateTime.Now));
        SetField("_masterRecordedHistoryStore", new TestHistoryStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db")));
        var keys = (HashSet<MasterFaultKey>)typeof(TestViewModel).GetField("_masterDetectedFaultKeys", flags)!.GetValue(vm)!;
        keys.Add(new MasterFaultKey(ProductFaultType.WrongWiring, 1, 2, 1, 3));
        SetField("_masterRemovalConfirmed", 1);
        SetField("_masterEjectInProgress", 1);
        long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
        typeof(TestViewModel).GetMethod("ProcessMasterEngineChangedOnUi", flags)!.Invoke(vm, [generation]);
        Assert(!vm.MasterApproved && vm.MasterState == MasterSequenceState.EjectingBadMaster,
            "Removal confirmation cannot complete Master while JIG still owns the sequence");
        typeof(TestViewModel).GetMethod("FinishMasterEjectSequence", flags)!.Invoke(vm, [generation]);
        Assert(vm.MasterApproved && vm.MasterState == MasterSequenceState.Completed &&
               vm.ResultStatusText == "LẮP SẢN PHẨM" && vm.CenterResultText == "LẮP SẢN PHẨM" &&
               vm.CurrentProductionRuntimeState == ProductionRuntimeState.WaitingForProduct && vm.Faults.Count == 0,
            "Releasing JIG processes the latched removal without another frame and returns to product installation");
        typeof(TestViewModel).GetMethod("FinishMasterEjectSequence", flags)!.Invoke(vm, [generation]);
        Assert(vm.ResultStatusText == "LẮP SẢN PHẨM", "A duplicate completion callback cannot restore testing state");

        TestViewModel unremoved = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 1 });
        LoadReadyModel(unremoved, Model(("PAIR", new[] { 1, 2 })));
        typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(unremoved, MasterSequenceState.EjectingBadMaster);
        typeof(TestViewModel).GetField("_masterEjectInProgress", flags)!.SetValue(unremoved, 1);
        long unremovedGeneration = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(unremoved)!;
        typeof(TestViewModel).GetMethod("FinishMasterEjectSequence", flags)!.Invoke(unremoved, [unremovedGeneration]);
        Assert(!unremoved.MasterApproved && unremoved.MasterState == MasterSequenceState.EjectingBadMaster,
            "Finishing relay work alone cannot replace a real sample removal confirmation");
    }
}
