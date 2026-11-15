using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestDiscardContactLearning()
    {
        var capacity = BoardCapacity.Create(10);
        foreach (var pair in new[] { (17, 46), (129, 186), (639, 640) })
        {
            var learner = new DiscardContactLearner();
            ScanFrame On(long seq) => FrameSeq(seq, (pair.Item1, [pair.Item2]), (pair.Item2, [pair.Item1]));
            learner.Observe(On(1), capacity);
            Assert(learner.Observe(On(2), capacity).FirstIo == 0, "A sensor already closed at startup is not learned without a clear baseline");
            learner.Observe(FrameSeq(3), capacity); learner.Observe(FrameSeq(4), capacity);
            learner.Observe(On(5) with { Complete = false }, capacity);
            learner.Observe(On(6), capacity); learner.Observe(On(6), capacity);
            learner.Observe(FrameSeq(7), capacity);
            Assert(!learner.Current.Completed, "A partial/duplicate activation is not accepted");
            learner.Observe(On(8), capacity); learner.Observe(On(9), capacity);
            Assert(learner.Current.FirstIo == pair.Item1 && !learner.Current.Completed,
                "The actual arbitrary pair is recognized but not accepted until sensor release");
            learner.Observe(On(10), capacity);
            learner.Observe(FrameSeq(11), capacity); learner.Observe(FrameSeq(11), capacity);
            Assert(!learner.Current.Completed, "A held sensor or duplicated release frame cannot complete learning");
            learner.Observe(FrameSeq(12) with { UnknownBytes = 1 }, capacity);
            learner.Observe(FrameSeq(13), capacity);
            var completed = learner.Observe(FrameSeq(14), capacity);
            Assert(completed.Completed && completed.FirstIo == pair.Item1 && completed.SecondIo == pair.Item2,
                "Two fresh release frames confirm the learned pair without fixed IO numbers");
        }
        var ambiguous = new DiscardContactLearner();
        ambiguous.Observe(FrameSeq(1), capacity); ambiguous.Observe(FrameSeq(2), capacity);
        ambiguous.Observe(FrameSeq(3, (1, [2]), (3, [4])), capacity);
        Assert(!ambiguous.Current.Completed && ambiguous.Current.FirstIo == 0,
            "Several active pairs are rejected instead of guessing the sensor");
        ambiguous.Observe(FrameSeq(4), capacity); ambiguous.Observe(FrameSeq(5), capacity);
        ambiguous.Observe(ProbeFrameSeq(6, 20), capacity);
        Assert(ambiguous.Current.FirstIo == 0, "Production Probe fan-in cannot become a learned discard pair");
        var stale = new DiscardContactLearner();
        stale.Observe(FrameSeq(1) with { ScanGeneration = 2 }, capacity);
        stale.Observe(FrameSeq(2) with { ScanGeneration = 2 }, capacity);
        stale.Observe(FrameSeq(3, (17, [46])) with { ScanGeneration = 1 }, capacity);
        stale.Observe(FrameSeq(4, (17, [46])) with { ScanGeneration = 1 }, capacity);
        Assert(stale.Current.FirstIo == 0, "Old scan callbacks cannot teach a pair in a newer learning session");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
        var vm = CreateTestViewModel(settings, out FakeBoard board);
        var model = Model(("WIRE", new[] { 1, 2 }));
        model.DiscardContactIo = [41, 86];
        typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetModel(model);
        var live = new DiscardContactLearner();
        void Observe(object? _, ScanFrame frame) => live.Observe(frame, vm.BoardCapacity);
        long frames = engine.FramesProcessed;
        vm.StartTopologyLearningAsync().GetAwaiter().GetResult();
        vm.BoardFrameActivity += Observe;
        try
        {
            board.Publish(FrameSeq(10)); board.Publish(FrameSeq(11));
            board.Publish(FrameSeq(12, (41, [86]))); board.Publish(FrameSeq(13, (41, [86])));
            Assert(!vm.IsProductRemovalPending && !AppSoundService.Current.IsWiringFaultAlarmActive,
                "Learning the already configured sensor does not enter discard/FAIL/Probe interlocks");
            board.Publish(FrameSeq(14)); board.Publish(FrameSeq(15));
            Assert(live.Current.Completed && live.Current.FirstIo == 41 && live.Current.SecondIo == 86 &&
                engine.FramesProcessed == frames && vm.Total == 0 && vm.Pass == 0 && vm.Fail == 0 &&
                !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                "One shared D2XX scan learns the pair without product tests, counters or relay actions");
        }
        finally
        {
            vm.BoardFrameActivity -= Observe;
            vm.StopTopologyLearningAsync().GetAwaiter().GetResult();
            AppSoundService.Current.StopAll(force: true);
        }
        OnSettingsDispatcher(() =>
        {
            var configured = new ProductModel { SourcePath = "existing.tht", DiscardContactIo = [41, 86] };
            var notice = new JBZUniversalTester.Views.DiscardConfigurationNoticeWindow(configured);
            Assert(((System.Windows.Controls.TextBlock)notice.FindName("ConfiguredIoText")).Text == "IO 41 / 86" &&
                ((System.Windows.Controls.TextBlock)notice.FindName("ConfiguredFileText")).Text == "existing.tht",
                "Existing-DISCARD notice displays the actual file and its own pair, without assuming 97/98");
            notice.Close();
            return Task.CompletedTask;
        });
    }
}
