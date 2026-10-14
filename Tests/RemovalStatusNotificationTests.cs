using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
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
