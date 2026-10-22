using System.Diagnostics;
using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestDeferredResistanceRecovery()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-DeferredR-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var sound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var settings = new ProductionSettings { MasterFaultRequiredCount = 0,
                    Relay1JigPulseMs = 10, Relay2MarkingPulseMs = 10, PassMarkingToJigDelayMs = 0,
                    ResistanceChannels = [new() { Name = "R1", Channel = 1, Enabled = true, MinOhm = 1, MaxOhm = 3 }] };
                var app = new AppSettings();
                app.Keysight.SettleDelayMs = 0;
                app.Test.ResistanceMinimumSettleMs = 0;
                app.Test.ResistanceSampleIntervalMs = 0;
                var board = new FakeBoard();
                var visa = new FakeKeysightVisaService(true, 2);
                using var engine = new TestEngine(board, visa, app, settings);
                int soundRequests = 0;
                var vm = new TestViewModel(new MainViewModel(), engine, board, visa, new WaterProofSerialService(),
                    app, settings, new LegacyPhtHistoryService(enabled: false), false,
                    passSoundPlayback: resistance => { Assert(resistance, "Resistance keeps STAGEOK then DINGDONG selection"); soundRequests++; return sound.Task; });
                var model = Model(("A", new[] { 1, 18 }), ("B", new[] { 2, 19 }));
                model.PartNumber = "DEFERRED-RECOVERY-" + Guid.NewGuid().ToString("N");
                vm.SetModel(model);
                await (Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!;
                void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                object? Get(string name) => typeof(TestViewModel).GetField(name, flags)!.GetValue(vm);
                object? Call(string name, params object?[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
                var store = new TestHistoryStore(Path.Combine(root, "history.db"));
                await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
                await persistence.Initialization;
                Set("_historyStore", store);
                Set("_productionPersistence", persistence);
                Set("_runtimeMode", 1);
                Set("_masterApproved", true);
                Set("_cycleActive", true);
                Set("_postContinuityStarted", 1);
                Set("_productionPhase", (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, "Continuity"));
                long generation = (long)Get("_runtimeGeneration")!;
                long sequence = 100;
                engine.SetFrameProcessingEnabled(true);
                for (int i = 0; i < 4; i++)
                {
                    var frame = FrameSeq(sequence++, (1, [18]), (2, [19]));
                    engine.ProcessFrame(frame, false);
                    Call("ObserveProductPresenceForCompletion", frame, generation, Stopwatch.GetTimestamp());
                }
                board.StartScanCallback = transport => transport.Publish(FrameSeq(sequence++, (1, [18]), (2, [19])));
                await (Task)Call("RunAutomaticPostContinuityAsync")!;
                Assert(engine.HasDeferredResistanceRecovery && board.ReleaseResistanceRouteCount == 0 &&
                       board.Commands.Contains("SET:2") && board.Commands.Contains("SET:1") &&
                       !sound.Task.IsCompleted && soundRequests == 1 && vm.Pass == 1 && !vm.IsPersistenceFault && !vm.IsDeviceFault,
                    $"Durable PASS and both relays complete before route recovery and without waiting for audio: pending={engine.HasDeferredResistanceRecovery}, recovery={board.ReleaseResistanceRouteCount}, pass={vm.Pass}, sound={soundRequests}, device={vm.IsDeviceFault}, persistence={vm.IsPersistenceFault}, status={vm.State}, commands={string.Join(',',board.Commands)}");
                Assert(vm.HasProductOnTestTable, "Pending recovery keeps navigation/new-product interlock held");
                board.Publish(FrameSeq(sequence++, (1, [18])));
                board.Publish(FrameSeq(sequence++, (1, [18])));
                Assert(board.ReleaseResistanceRouteCount == 0, "Partial product removal cannot run deferred recovery");
                board.Publish(FrameSeq(sequence++));
                Assert(board.ReleaseResistanceRouteCount == 0, "One empty frame cannot run deferred recovery");
                board.BeforeReleaseResistance = () =>
                    Assert((int)Get("_passProductRemoved")! == 1 && vm.HasProductOnTestTable,
                        "Recovery starts only after confirmed full removal and holds the next cycle closed");
                var recoveryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                board.ResistanceRecoveryWork = () => recoveryRelease.Task;
                board.StartScanCallback = transport => transport.Publish(FrameSeq(sequence++));
                board.Publish(FrameSeq(sequence++));
                Assert(engine.HasDeferredResistanceRecovery && vm.HasProductOnTestTable && vm.IsProductRemovalPending,
                    "A slow hardware recovery retains the removal/navigation gate until it really completes");
                Call("TryCompletePassProductRemoval");
                Assert(board.ReleaseResistanceRouteCount == 1, "Repeated UI callbacks cannot start a parallel recovery");
                recoveryRelease.SetResult();
                await (Task)Get("_deferredResistanceRecoveryTask")!;
                Assert(board.ReleaseResistanceRouteCount == 1 && !engine.HasDeferredResistanceRecovery &&
                       (int)Get("_deferredResistanceRecoveryActive")! == 0 && !vm.IsProductRemovalPending &&
                       vm.Pass == 1 && vm.Fail == 0 && !vm.IsDeviceFault,
                    "Full removal restores once, verifies a new scan and rearms without duplicating counters");
                Call("TryCompletePassProductRemoval");
                await engine.CompleteDeferredResistanceRecoveryAsync();
                Assert(board.ReleaseResistanceRouteCount == 1, "Duplicate callbacks cannot repeat the route recovery");

                await engine.MeasureResistanceAsync(null, CancellationToken.None, deferSuccessfulRecovery: true);
                Call("ArmPassProductRemovalWait");
                Set("_passProductRemoved", 1);
                board.BeforeReleaseResistance = () => throw new IOException("Recovery hardware write failed");
                Call("TryCompletePassProductRemoval");
                await (Task)Get("_deferredResistanceRecoveryTask")!;
                Assert(vm.IsDeviceFault && vm.IsProductRemovalPending && engine.HasDeferredResistanceRecovery &&
                       vm.Pass == 1 && vm.Fail == 0,
                    "Failed recovery blocks rearm and preserves the already committed PASS and counters");
                int commandsAfterFault = board.Commands.Count;
                Call("TryCompletePassProductRemoval");
                Assert(board.Commands.Count == commandsAfterFault, "A D2XX recovery fault cannot silently retry/rearm from another callback");

                // Failed or cancelled measurements retain immediate safe cleanup.
                foreach (bool failure in new[] { true, false })
                {
                    var cleanupBoard = new FakeBoard();
                    var cleanupVisa = new FakeKeysightVisaService(true, failure ? 4 : 2);
                    using var cleanupEngine = new TestEngine(cleanupBoard, cleanupVisa, app, settings);
                    cleanupEngine.SetModel(model);
                    using var cancel = new CancellationTokenSource();
                    if (!failure) cleanupVisa.BeforeMeasure = cancel.Cancel;
                    try { await cleanupEngine.MeasureResistanceAsync(null, cancel.Token, deferSuccessfulRecovery: true); }
                    catch (OperationCanceledException) when (!failure) { }
                    Assert(cleanupBoard.ReleaseResistanceRouteCount == 1 && !cleanupEngine.HasDeferredResistanceRecovery,
                        "FAIL/cancel cannot defer cleanup or enable a relay");
                }
            }
            finally
            {
                sound.TrySetResult();
                AppSoundService.Current.StopAll(force: true);
                SqliteConnection.ClearAllPools();
                if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-DeferredR-"), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(root, true);
            }
        });
    }
}
