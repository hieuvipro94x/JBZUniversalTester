using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestPersistentElectricalAlarm()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sound = AppSoundService.Current;
        try
        {
            var clock = new ManualTimeProvider(DateTimeOffset.Now);
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0,
                ProductSettleTimeMs = 0, WrongConnectionConfirmMs = 0, ShortCircuitConfirmMs = 0,
                JigContactUnstableWindowMs = 0 };
            var vm = CreateTestViewModel(settings, out FakeBoard board, timeProvider: clock);
            var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            vm.SetModel(Model(("PAIR", new[] { 1, 2 })));
            ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!).GetAwaiter().GetResult();
            Set("_masterApproved", true);
            Set("_runtimeMode", 0); // Exercise presentation without committing a production FAIL.
            engine.SetFrameProcessingEnabled(true);
            engine.ProcessFrame(FrameSeq(100, (1, [3])), false);
            clock.Advance(TimeSpan.FromSeconds(1));
            engine.ProcessFrame(FrameSeq(101, (1, [3])), false);
            clock.Advance(TimeSpan.FromSeconds(1));
            engine.ProcessFrame(FrameSeq(102, (1, [3])), false);
            Set("_runtimeMode", 1);
            typeof(TestViewModel).GetMethod("RefreshFaults", flags)!.Invoke(vm, null);
            Assert(engine.HasWiringFault && sound.IsElectricalFaultAlarmHeld && sound.IsWiringFaultAlarmActive,
                "Confirmed electrical faults start the persistent TESTPOINT owner");
            int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
            sound.SetWiringFaultAlarm(false);
            sound.StopAll();
            sound.PlayClick();
            sound.PlayProductStart();
            sound.PlayTestPoint();
            sound.PlayDiscardContact();
            sound.PlayLeakFail();
            sound.SetTestPointContactSound(true);
            sound.SetTestPointContactSound(false);
            sound.PlayTestOkAsync(true).GetAwaiter().GetResult();
            Assert(sound.IsElectricalFaultAlarmHeld && sound.IsWiringFaultAlarmActive,
                "UI cleanup, Probe release, clicks, discard, NG and PASS sounds cannot silence TESTPOINT");
            ((Task)typeof(TestViewModel).GetMethod("StopTestAsync", flags)!.Invoke(vm, null)!).GetAwaiter().GetResult();
            Assert(sound.IsWiringFaultAlarmActive && vm.IsProductRemovalPending,
                "Returning to Main keeps TESTPOINT and the physical product-removal interlock");
            board.Publish(FrameSeq(109, (1, [3]), (4, [5])));
            clock.Advance(TimeSpan.FromSeconds(1));
            board.Publish(FrameSeq(110, (1, [3]), (4, [5])));
            board.Publish(FrameSeq(111, (4, [5])));
            Assert(sound.IsWiringFaultAlarmActive, "Releasing one faulty pair cannot stop the remaining-pair alarm");
            board.Publish(FrameSeq(112));
            board.Publish(FrameSeq(112));
            Assert(sound.IsWiringFaultAlarmActive, "One empty frame and a duplicate cannot release TESTPOINT");
            board.Publish(FrameSeq(113) with { Complete = false });
            board.Publish(FrameSeq(114) with { UnknownBytes = 1 });
            board.Publish(FrameSeq(115));
            Assert(sound.IsWiringFaultAlarmActive, "Incomplete/corrupt frames cannot prove clean removal");
            board.Publish(FrameSeq(116));
            Assert(!sound.IsElectricalFaultAlarmHeld && !sound.IsWiringFaultAlarmActive &&
                   !vm.IsProductRemovalPending && vm.ResultStatusText == "LẮP SẢN PHẨM",
                $"Two fresh complete empty frames and ready presentation release TESTPOINT on Main: held={sound.IsElectricalFaultAlarmHeld}, active={sound.IsWiringFaultAlarmActive}, pending={vm.IsProductRemovalPending}, status={vm.ResultStatusText}, state={vm.State}, empty={typeof(TestViewModel).GetField("_faultAlarmEmptyFrames", flags)!.GetValue(vm)}, removed={engine.IsConfirmedProductRemoved}, activity={engine.HasProductActivity}");
            Assert(vm.Total == total && vm.Pass == pass && vm.Fail == fail &&
                   !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                $"The alarm and its release never commit production or pulse a relay: counters={total}/{pass}/{fail}->{vm.Total}/{vm.Pass}/{vm.Fail}, commands={string.Join(',', board.Commands)}");

            Set("_runtimeMode", 0);
            engine.SetFrameProcessingEnabled(true);
            engine.ProcessFrame(FrameSeq(120, (1, [3])), false);
            clock.Advance(TimeSpan.FromSeconds(1));
            engine.ProcessFrame(FrameSeq(121, (1, [3])), false);
            Set("_runtimeMode", 1);
            typeof(TestViewModel).GetMethod("RefreshFaults", flags)!.Invoke(vm, null);
            Assert(sound.IsElectricalFaultAlarmHeld, "A later installation starts its own electrical alarm");
            ((Task)typeof(TestViewModel).GetMethod("StopTestAsync", flags)!.Invoke(vm, null)!).GetAwaiter().GetResult();
            board.Publish(FrameSeq(122));
            board.Publish(FrameSeq(123));
            Assert(!sound.IsWiringFaultAlarmActive && !vm.IsProductRemovalPending && vm.ResultStatusText == "LẮP SẢN PHẨM",
                "Removal during navigation to Main needs no extra connected frame to release the alarm and interlock");

            var blank = CreateTestViewModel(settings, out FakeBoard blankBoard);
            blank.SetModel(new ProductModel { ModelName = "ALARM-EMPTY", PartNumber = "ALARM-EMPTY" });
            blank.StartProductionTestAsync().GetAwaiter().GetResult();
            blankBoard.Publish(FrameSeq(200, (1, [3, 5]), (3, [1, 5]), (5, [1, 3])));
            Assert(sound.IsElectricalFaultAlarmHeld && sound.IsWiringFaultAlarmActive && blank.WrongCount >= 2,
                "Multiple real connections in an empty model also keep the TESTPOINT alarm without a product FAIL");
            sound.StopAll();
            blankBoard.Publish(FrameSeq(201, (1, [3])));
            Assert(sound.IsWiringFaultAlarmActive, "One remaining empty-model connection keeps the alarm");
            blankBoard.Publish(FrameSeq(202));
            Assert(sound.IsWiringFaultAlarmActive, "A single empty-model release frame cannot stop the held alarm");
            blankBoard.Publish(FrameSeq(203));
            Assert(!sound.IsWiringFaultAlarmActive && blank.ResultStatusText == "LẮP SẢN PHẨM" &&
                   blank.Total == 0 && blank.Pass == 0 && blank.Fail == 0 &&
                   !blankBoard.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                "Empty-model clean release stops TESTPOINT without production/relay effects");

            sound.HoldElectricalFaultAlarm();
            typeof(TestViewModel).GetField("_deviceFault", flags)!.SetValue(blank, 1);
            long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(blank)!;
            foreach (long seq in new[] { 204L, 205L })
                typeof(TestViewModel).GetMethod("ObserveElectricalFaultAlarmFrame", flags)!.Invoke(blank, [FrameSeq(seq), generation]);
            blank.State = "LẮP SẢN PHẨM";
            Assert(sound.IsWiringFaultAlarmActive, "A latched D2XX error cannot be silenced by a stale ready status");
            sound.StopAll(force: true);
            Assert(!sound.IsWiringFaultAlarmActive, "Application shutdown releases native audio resources");

            Task oldPass = sound.PlayTestOkAsync(resistancePassed: true);
            sound.StopAll();
            Task nextPass = sound.PlayTestOkAsync(resistancePassed: false);
            Assert(!ReferenceEquals(oldPass, nextPass),
                "Normal audio cleanup cannot leave the cancelled PASS task blocking all later PASS sounds");
            sound.HoldElectricalFaultAlarm();
            foreach (Task interrupted in new[] { oldPass, nextPass })
            {
                bool cancelled = false;
                try { interrupted.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancelled = true; }
                Assert(cancelled && sound.IsElectricalFaultAlarmHeld && sound.IsWiringFaultAlarmActive,
                    "A new electrical alarm preempts PASS playback; its stale completion cannot silence or replace TESTPOINT");
            }
        }
        finally { sound.StopAll(force: true); }
    }
}
