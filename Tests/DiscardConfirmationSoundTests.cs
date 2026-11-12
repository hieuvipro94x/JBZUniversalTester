using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestDiscardConfirmationSound()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sound = AppSoundService.Current;
        object? SoundField(string name) => typeof(AppSoundService).GetField(name, flags)!.GetValue(sound);
        long Drips() => (long)SoundField("_discardContactPlaybackCount")!;
        try
        {
            foreach (bool confirmationDialog in new[] { false, true })
            {
                sound.StopAll(force: true);
                var clock = new ManualTimeProvider(DateTimeOffset.Now);
                var settings = new ProductionSettings { MasterFaultRequiredCount = 0,
                    WiringFaultConfirmationEnabled = confirmationDialog, JigContactUnstableWindowMs = 0 };
                var vm = CreateTestViewModel(settings, out FakeBoard board, timeProvider: clock);
                var model = Model(("PAIR", new[] { 1, 2 }), ("OTHER", new[] { 4, 5 }));
                model.DiscardContactIo = [97, 98];
                vm.SetModel(model);
                ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!).GetAwaiter().GetResult();
                void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                object? Call(string name, params object[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
                var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
                Set("_masterApproved", true);
                Set("_runtimeMode", 0); // No production-result commit or physical relay action.
                engine.SetFrameProcessingEnabled(true);
                engine.ProcessFrame(FrameSeq(100, (1, [3]), (4, [5])), false);
                clock.Advance(TimeSpan.FromSeconds(1));
                engine.ProcessFrame(FrameSeq(101, (1, [3]), (4, [5])), false);
                Set("_runtimeMode", 1);
                Set("_wiringFaultHandlingStarted", 1);
                Call("RefreshFaults");
                Assert(sound.IsElectricalFaultAlarmHeld && sound.IsWiringFaultAlarmActive, "Electrical faults hold TESTPOINT before removal");
                Call("ArmFaultProductRemoval", model);
                int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
                string lot = vm.Lot;
                long drips = Drips();
                board.Publish(FrameSeq(110, (4, [5])));
                Assert(sound.IsWiringFaultAlarmActive && vm.IsProductRemovalPending, "One remaining connection keeps the electrical alarm and discard gate");
                board.Publish(FrameSeq(111));
                board.Publish(FrameSeq(111)); // Duplicate cannot prove clean electrical removal.
                Assert(sound.IsWiringFaultAlarmActive, "One empty frame plus a duplicate cannot silence TESTPOINT");
                board.Publish(FrameSeq(112) with { Complete = false });
                board.Publish(FrameSeq(113) with { UnknownBytes = 1 });
                board.Publish(FrameSeq(114));
                Assert(sound.IsWiringFaultAlarmActive, "Invalid frames cannot contribute to the clean-removal sound gate");
                board.Publish(FrameSeq(115));
                Assert(!sound.IsElectricalFaultAlarmHeld && !sound.IsWiringFaultAlarmActive && vm.IsProductRemovalPending &&
                    vm.ResultStatusText == "CHỜ XÁC NHẬN THÙNG LỖI" && vm.CenterResultText == "ĐƯA HÀNG VÀO THÙNG HÀNG LỖI" && Drips() == drips,
                    $"Full product removal silences TESTPOINT while retaining the discard confirmation, dialog={confirmationDialog}");
                Call("RefreshFaults");
                board.Publish(FrameSeq(116));
                Assert(!sound.IsWiringFaultAlarmActive && vm.IsProductRemovalPending, "The latched FAIL and later UI/scan refreshes cannot restart TESTPOINT while waiting for NG");
                board.Publish(FrameSeq(117, (12, [97])));
                Assert(Drips() == drips + 1 && !sound.IsWiringFaultAlarmActive && vm.IsProductRemovalPending,
                    "The NG sensor entry plays DRIP once, without rearming before sensor release");
                board.Publish(FrameSeq(118, (12, [97])));
                Assert(Drips() == drips + 1 && vm.IsProductRemovalPending, "A held sensor cannot repeat DRIP or complete the cycle");
                board.Publish(FrameSeq(119));
                Assert(!vm.IsProductRemovalPending && !sound.IsWiringFaultAlarmActive && vm.ResultStatusText == "LẮP SẢN PHẨM" &&
                    Drips() == drips + 1 && vm.Total == total && vm.Pass == pass && vm.Fail == fail && vm.Lot == lot &&
                    !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                    "Only sensor ON-to-OFF unlocks the next cycle; sound does not change production, LOT or relays");
                ((Task)SoundField("_discardContactPlaybackTask")!).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            }
            sound.StopAll(force: true);
            // Hold the sound gate to prevent the duration-completion callback
            // racing this deterministic test of ordinary Probe/UI cleanup.
            lock (SoundField("_gate")!)
            {
                sound.PlayDiscardContact();
                int generation = (int)SoundField("_soundGeneration")!;
                long drips = Drips();
                sound.SetTestPointContactSound(false);
                sound.StopAll();
                sound.PlayClick();
                Assert((int)SoundField("_discardContactPlaybackActive")! == 1 &&
                    (int)SoundField("_soundGeneration")! == generation && Drips() == drips,
                    "Probe release, UI cleanup and clicks cannot cut the brief DRIP acknowledgement");
                sound.HoldElectricalFaultAlarm();
                Assert(sound.IsElectricalFaultAlarmHeld && sound.IsWiringFaultAlarmActive &&
                    (int)SoundField("_discardContactPlaybackActive")! == 0,
                    "A new electrical fault retains priority over DRIP and cancels its old playback generation");
            }
        }
        finally { sound.StopAll(force: true); }
    }
}
