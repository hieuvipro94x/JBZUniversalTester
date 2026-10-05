using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestCommittedPassRejectsInstallationRows()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        TestViewModel vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        LoadReadyModel(vm, Model(("PAIR-1", new[] { 1, 2 }), ("PAIR-2", new[] { 3, 4 })));
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetFrameProcessingEnabled(true);
        engine.ProcessFrame(FrameSeq(100, (1, new[] { 2 })), false);
        FaultRow[] staleRows = engine.BuildRows().ToArray();
        Assert(staleRows.Length > 0, "Fixture has a genuine incomplete connection snapshot");
        int completedPhase = Convert.ToInt32(Enum.Parse(
            typeof(TestViewModel).GetNestedType("ProductionPhase", BindingFlags.NonPublic)!, "Completed"));
        typeof(TestViewModel).GetField("_productionPhase", flags)!.SetValue(vm, completedPhase);
        typeof(TestViewModel).GetField("_presentationCycleStarted", flags)!.SetValue(vm, true);
        vm.State = "PASS";
        MethodInfo synchronize = typeof(TestViewModel).GetMethod("SynchronizeFaultRows", flags)!;
        synchronize.Invoke(vm, [staleRows]);
        Assert(vm.Faults.Count == 0, "Late pre-PASS snapshot cannot restore installation rows before relay/removal arming");
        var snapshot = (TestEnginePresentationSnapshot)typeof(TestViewModel)
            .GetMethod("BuildEngineFaultRowsSnapshot", flags)!.Invoke(vm, null)!;
        Assert(snapshot.Rows.Count == 0, "PASS snapshot stays empty even when live engine connections are incomplete");
        typeof(TestViewModel).GetMethod("RefreshFaults", flags)!.Invoke(vm, null);
        Assert(vm.Faults.Count == 0, "Normal refresh cannot reopen installation rows while final PASS is displayed");
        typeof(TestViewModel).GetMethod("ArmPassProductRemovalWait", flags)!.Invoke(vm, null);
        synchronize.Invoke(vm, [staleRows]);
        Assert(vm.ResultStatusText == "PASS" && vm.Faults.Count == 0,
            "PASS removal baseline also rejects stale installation rows");
        typeof(TestViewModel).GetField("_passRemovalStarted", flags)!.SetValue(vm, 1);
        vm.State = "THÁO SẢN PHẨM";
        engine.ProcessFrame(FrameSeq(101, (1, new[] { 2 })), false);
        FaultRow[] removal = engine.BuildRemovalRows().ToArray();
        synchronize.Invoke(vm, [removal]);
        Assert(removal.Length > 0 && vm.Faults.Count == removal.Length,
            "Real product-removal presentation retains its connection rows");
    }
}
