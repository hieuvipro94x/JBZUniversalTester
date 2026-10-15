using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestOrphanedProbeCandidateRestoresCenter()
    {
        foreach (bool useDiscard in new[] { true, false })
        {
            var production = new ProductionSettings { MasterFaultRequiredCount=0, ExpansionCardCount=2,
                ProductSettleTimeMs=10_000, IoConfirm1=1, IoConfirmN=1 };
            var vm = CreateTestViewModel(production, out FakeBoard board);
            var model = Model(("PAIR",new[] {1,2}));
            if(useDiscard) model.DiscardContactIo=[97,98];
            vm.SetModel(model);
            vm.StartProductionTestAsync().GetAwaiter().GetResult();
            int beforeTotal=vm.Total, beforeFail=vm.Fail, beforePass=vm.Pass, beforeCommands=board.Commands.Count;
            board.PublishProbePreview(new ProductionProbePreview(DateTime.Now,[97],12,12,Sequence:10,ScanGeneration:1));
            Assert(vm.CurrentProbePresentationState==ProbePresentationState.Candidate && !vm.IsCenterResultVisible,
                "Early preview hides waiting center until its complete frame is reconciled");
            if(useDiscard)
            {
                board.Publish(FrameSeq(10,(97,new[] {98})) with {ScanGeneration=1});
                Assert(!vm.IsCenterResultVisible,"Discard contact interlock does not prematurely show installation");
            }
            board.Publish(FrameSeq(11) with {ScanGeneration=1});
            board.Publish(FrameSeq(12) with {ScanGeneration=1});
            Assert(vm.CurrentProbePresentationState is ProbePresentationState.Released or ProbePresentationState.Inactive &&
                   vm.IsCenterResultVisible && vm.CenterResultText=="LẮP SẢN PHẨM",
                $"Candidate-only preview releases center after {(useDiscard ? "discard close/release" : "a newer complete empty frame")}; " +
                $"probe={vm.CurrentProbePresentationState}, center={vm.IsCenterResultVisible}, text={vm.CenterResultText}");
            Assert(vm.Total==beforeTotal && vm.Fail==beforeFail && vm.Pass==beforePass && board.Commands.Count==beforeCommands,
                "Preview cleanup never creates production results or hardware relay commands");
        }
    }

    private static void TestRemovalStatusNotifications()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        var model = Model(("PAIR", new[] { 1, 2 }));
        void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
        Set("_model", model);
        Set("_masterApproved", true);
        Set("_cycleActive", true);
        var phaseType = typeof(TestViewModel).GetNestedType("ProductionPhase", BindingFlags.NonPublic)!;
        Set("_productionPhase", Convert.ToInt32(Enum.Parse(phaseType, "Continuity")));
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(1));
        Set("_presentationCycleStarted", true);
        string rendered = "";
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TestViewModel.ResultStatusText)) rendered = vm.ResultStatusText;
        };
        vm.State = "ĐANG KIỂM TRA...";
        vm.State = "LẮP SẢN PHẨM";
        Assert(rendered == "ĐANG KIỂM TRA", "Presentation can still show testing while the old activity latch is present");
        Set("_presentationCycleStarted", false);
        Assert(vm.ResultStatusText == "LẮP SẢN PHẨM" && rendered == "ĐANG KIỂM TRA",
            "Changing a dependent activity flag alone leaves the previously published text stale");
        typeof(TestViewModel).GetMethod("ApplyAuthoritativeProductionState", flags)!
            .Invoke(vm, [engine.GetProductionElectricalSnapshot()]);
        Assert(vm.State == "LẮP SẢN PHẨM" && rendered == "LẮP SẢN PHẨM" && vm.StateBackground == "#F5E731",
            "An authoritative waiting snapshot refreshes the bound label/colors even when raw State does not change");
    }
}
