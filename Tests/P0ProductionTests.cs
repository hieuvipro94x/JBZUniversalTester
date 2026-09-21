using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void AwaitModelReconcile(TestViewModel vm)
    {
        var task = (Task?)typeof(TestViewModel).GetField("_modelScanReconcileTask",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm);
        task?.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    private static void LoadReadyModel(TestViewModel vm, ProductModel model)
    {
        vm.LoadPreparedModelAsync(model).GetAwaiter().GetResult();
        AwaitModelReconcile(vm);
        var board = (FakeBoard)typeof(TestViewModel).GetField("_board",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        board.Publish(FrameSeq(board.LastCompleteFrameSequence + 1));
    }

    private static void TestP0ModelTransitionAndPresence()
    {
        using TestEngine engine = CreateEngine(out _);
        ProductModel model = Model(("PAIR", new[] { 1, 2 }));
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(1));
        engine.ProcessFrame(FrameSeq(2));
        Assert(!engine.IsConfirmedProductRemoved, "Empty startup is not a removed product");
        engine.ProcessFrame(FrameSeq(3, (1, [2])));
        Assert(engine.GetProductEvidenceSnapshot().ValidProductEvidence && engine.ContinuityPassed,
            "One complete correct frame confirms presence and continuity immediately");
        engine.ResetForProductRemoval();
        engine.ProcessFrame(FrameSeq(4));
        Assert(!engine.IsConfirmedProductRemoved, "Removal still requires two clean frames after reset");
        engine.ProcessFrame(FrameSeq(5));
        Assert(engine.IsConfirmedProductRemoved, "Removal reset retains prior confirmed presence");
        engine.ResetProductCycle();
        engine.ProcessFrame(FrameSeq(6));
        engine.ProcessFrame(FrameSeq(7));
        Assert(!engine.IsConfirmedProductRemoved, "A new cycle cannot inherit the previous presence latch");
        engine.ProcessFrame(FrameSeq(8, (1, [2, 3])));
        Assert(!engine.GetProductEvidenceSnapshot().ValidProductEvidence &&
               engine.GetPassGateDiagnostics().ShortCandidateCount + engine.GetPassGateDiagnostics().WrongCandidateCount > 0,
            "Full topology plus extra short creates a candidate immediately but is not strong presence or PASS");

        engine.ResetProductCycle();
        engine.ProcessFrame(FrameSeq(10, (1, [2])) with { ScanGeneration = 2 });
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(1, (1, [2])) with { ScanGeneration = 3 });
        Assert(engine.ContinuityPassed, "A restarted scan accepts sequence reset");
        engine.ProcessFrame(FrameSeq(10) with { ScanGeneration = 2 });
        engine.ProcessFrame(FrameSeq(11) with { ScanGeneration = 2 });
        Assert(engine.ContinuityPassed, "Late old-model frames stay rejected after the first fresh frame");

        TestViewModel vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo transition = typeof(TestViewModel).GetField("_modelTransitionActive", flags)!;
        MethodInfo complete = typeof(TestViewModel).GetMethod("CompleteModelTransition", flags)!;
        vm.LoadPreparedModelAsync(model).GetAwaiter().GetResult();
        AwaitModelReconcile(vm);
        Assert((int)transition.GetValue(vm)! == 1, "Same-capacity reconciliation alone cannot release the barrier");
        int generation = (int)typeof(TestViewModel).GetField("_modelLoadGeneration", flags)!.GetValue(vm)!;
        complete.Invoke(vm, [generation, "MonitoringTimeout"]);
        board.Publish(FrameSeq(0, (1, [2])));
        Assert((int)transition.GetValue(vm)! == 1 && vm.CurrentProductionRuntimeState != ProductionRuntimeState.TestingRealtime,
            "Timeout and the previous model's frame cannot activate production");
        board.Publish(FrameSeq(1));
        Assert((int)transition.GetValue(vm)! == 0, "A fresh authoritative same-capacity frame releases the barrier");
        vm.LoadPreparedModelAsync(Model(("NEXT", new[] { 3, 4 }))).GetAwaiter().GetResult();
        AwaitModelReconcile(vm);
        board.Publish(FrameSeq(1, (1, [2])));
        Assert((int)transition.GetValue(vm)! == 1, "A second model switch establishes a new frame watermark");
        board.Publish(FrameSeq(2) with { Complete = false });
        Assert((int)transition.GetValue(vm)! == 1, "Partial frames cannot release model transition");
        board.Publish(FrameSeq(3));
        Assert((int)transition.GetValue(vm)! == 0, "New model becomes ready on its first authoritative frame");

        board.SetRequestedScanCapacityForTest(2);
        board.SetAppliedScanCapacityForTest(2);
        foreach ((int cards, long scanGeneration) in new[] { (4, 2L), (2, 3L) })
        {
            int oldCards = board.Capacity.ScanCardCount;
            int connectionsBefore = board.ConnectAttempts;
            board.SetRequestedScanCapacityForTest(cards);
            ScanFrame fresh = FrameSeq(1) with
            {
                ScanGeneration = scanGeneration,
                ExpectedIoCount = cards * 64,
                ScanUnitCount = cards
            };
            board.StartScanCallback = transport => transport.Publish(fresh);
            vm.LoadPreparedModelAsync(Model(("CAPACITY", new[] { 1, cards * 64 })))
                .GetAwaiter().GetResult();
            AwaitModelReconcile(vm);
            Assert(board.ConnectAttempts == connectionsBefore + 1,
                "Capacity changes preserve the controlled reopen");
            board.Publish(fresh with { Sequence = 2, ExpectedIoCount = oldCards * 64, ScanUnitCount = oldCards });
            Assert((int)transition.GetValue(vm)! == 1, "Old capacity coverage cannot release the barrier");
            board.Publish(fresh with { Sequence = 3 });
            Assert((int)transition.GetValue(vm)! == 0 &&
                   vm.CurrentProductionRuntimeState != ProductionRuntimeState.TestingRealtime,
                "Matching new scan capacity and generation unlock without flashing TestingRealtime");
        }
        vm.ShutdownAsync().GetAwaiter().GetResult();
    }
}
