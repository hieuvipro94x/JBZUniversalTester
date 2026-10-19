using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestGoodMasterWiringSound()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var clock = new ManualTimeProvider(DateTimeOffset.Now);
        var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 1 },
            out FakeBoard board, timeProvider: clock);
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        var model = Model(("PAIR", new[] { 1, 2 }));
        void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
        void Refresh() => typeof(TestViewModel).GetMethod("RefreshFaults", flags)!.Invoke(vm, null);
        Set("_model", model);
        engine.SetModel(model);
        Set("_runtimeMode", 1);
        Set("_masterSequenceState", MasterSequenceState.TestingGoodMaster);
        // Isolate presentation from history/eject sequencing in this test.
        Set("_requiredMasterFaultSamples", Array.Empty<MasterSampleType>());
        Set("_masterPostStarted", 1);
        try
        {
            engine.ProcessFrame(FrameSeq(100, (1, new[] { 3 })), false);
            clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultProductSettleTimeMs + 1));
            engine.ProcessFrame(FrameSeq(101, (1, new[] { 3 })), false);
            clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs + 1));
            engine.ProcessFrame(FrameSeq(102, (1, new[] { 3 })), false);
            Refresh();
            Assert(engine.HasWiringFault && AppSoundService.Current.IsWiringFaultAlarmActive,
                "A confirmed wrong connection in Good Master starts TESTPOINT");
            Set("_requiredMasterFaultSamples", new[] { MasterSampleType.WrongWiring });
            long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
            typeof(TestViewModel).GetMethod("ProcessMasterEngineChangedOnUi", flags)!.Invoke(vm, [generation]);
            Assert(AppSoundService.Current.IsWiringFaultAlarmActive && !vm.MasterApproved &&
                   vm.MasterState == MasterSequenceState.TestingGoodMaster,
                "Rejected Good Master retains the warning while production stays locked");
            Set("_requiredMasterFaultSamples", Array.Empty<MasterSampleType>());
            Set("_masterSequenceState", MasterSequenceState.TestingBadMaster);
            Refresh();
            Assert(!AppSoundService.Current.IsWiringFaultAlarmActive,
                "A fault sample does not inherit the Good Master wiring alarm");
            Set("_masterSequenceState", MasterSequenceState.TestingGoodMaster);
            Refresh();
            Assert(AppSoundService.Current.IsWiringFaultAlarmActive, "Returning to Good Master restores its live warning");
            Set("_probeSessionActive", 1);
            Refresh();
            Assert(!AppSoundService.Current.IsWiringFaultAlarmActive, "Probe keeps its separate sound ownership");
            Set("_probeSessionActive", 0);
            engine.SetModel(model);
            engine.ProcessFrame(FrameSeq(103), false);
            Refresh();
            Assert(!AppSoundService.Current.IsWiringFaultAlarmActive &&
                   !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                "Clearing wiring faults stops TESTPOINT without pulsing a relay");
        }
        finally
        {
            AppSoundService.Current.SetWiringFaultAlarm(false);
        }
    }
}
