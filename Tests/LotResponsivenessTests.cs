using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLotPersistenceResponsiveness()
    {
        OnSettingsDispatcher(async () =>
        {
            int uiThread = Environment.CurrentManagedThreadId;
            var settings = new ProductionSettings { DeviceName = "ORIGINAL", LotNo = 0 };
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            for (int i = 0; i < 1000; i++)
            {
                settings.LotSettingsByProduct["PART-" + i] = new() { StartLotNo = 0, LotNo = 0, LotNoDate = today };
                settings.LabelTemplateTypesByProduct["PART-" + i] = LabelSettings.SmallQrTemplate;
            }
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int writerThread = 0, writes = 0, ticks = 0;
            bool failWrite = false;
            var lots = new LotSequenceService(settings, snapshot =>
            {
                writerThread = Environment.CurrentManagedThreadId;
                Assert(!ReferenceEquals(snapshot, settings), "Disk worker must own a frozen CFG snapshot");
                snapshot.DeviceName = "WORKER NORMALIZATION";
                Interlocked.Increment(ref writes);
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult(); // Simulated slow disk Flush(true).
                if (failWrite) throw new IOException("Simulated disk error");
            });
            lots.SelectProduct("PART-1", false);
            Assert(lots.ReserveForCycle("pass-1") == 1, "First reserve is deterministic");
            double maxGap = 0;
            var clock = Stopwatch.StartNew();
            double last = clock.Elapsed.TotalMilliseconds;
            var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) =>
            {
                double at = clock.Elapsed.TotalMilliseconds;
                maxGap = Math.Max(maxGap, at - last); last = at; ticks++;
                _ = lots.NextLot; _ = lots.HistoryBatchKey;
            };
            timer.Start();
            Task<(bool Success, string Error)> saving = lots.CommitSuccessfulPassAsync("pass-1", 1);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                {
                    Assert(lots.NextLot == 0 && !saving.IsCompleted,
                        "Clock/input can read LOT while slow storage is blocked; publication waits for durable acknowledgement");
                    settings.DeviceName = "OPERATOR CHANGE";
                    lots.SelectProduct("PART-2", false);
                }, DispatcherPriority.Input).Task.WaitAsync(TimeSpan.FromMilliseconds(500));
                await Task.Delay(300);
                Assert(ticks >= 5 && writerThread != uiThread && !saving.IsCompleted,
                    "A 300ms blocked CFG writer leaves input/paint and LOT reads responsive");
            }
            finally { release.TrySetResult(); timer.Stop(); }
            var committed = await saving;
            Assert(committed.Success && lots.NextLot == 0 && settings.DeviceName == "OPERATOR CHANGE" &&
                   settings.LotSettingsByProduct["PART-1"].LotNo == 1,
                "Old product completion changes only its own counter and does not overwrite the active product or UI settings");
            lots.SelectProduct("PART-1", false);
            Assert(lots.NextLot == 1, "Switching back retains the durable old-product LOT");
            failWrite = true;
            Assert(lots.ReserveForCycle("pass-2") == 2, "Next reserve follows the acknowledged LOT");
            var failed = await lots.CommitSuccessfulPassAsync("pass-2", 2);
            Assert(!failed.Success && lots.NextLot == 1 && lots.IsCommitCandidate("pass-2", 2),
                "Failed asynchronous storage neither publishes a false LOT nor loses the retry reservation");
            failWrite = false;
            Assert((await lots.CommitSuccessfulPassAsync("pass-2", 2)).Success && lots.NextLot == 2,
                "Retry commits exactly the reserved LOT");
            Assert((await lots.ReconcileCommittedLotAsync(7100, lots.HistoryBatchKey, true)).Success && lots.NextLot == 7100,
                "Statistics recovery uses the same asynchronous acknowledgement path");
            var hot = Stopwatch.StartNew();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) { _ = lots.NextLot; _ = lots.StartLot; }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            hot.Stop();
            Assert(hot.ElapsedMilliseconds < 1000 && allocated < 10_000_000,
                "LOT reads must not repeatedly normalize/allocate the full 1000-product configuration");
            Console.WriteLine($"LOT_UI_PERF products=1000 blocked_disk_ms=300 input_ticks={ticks} max_gap_ms={maxGap:0.###} reads_20000_ms={hot.Elapsed.TotalMilliseconds:0.###} allocated_bytes={allocated} writes={writes}");

            var vm = CreateTestViewModel(new ProductionSettings(), out _);
            var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            typeof(TestViewModel).GetField("_productionResultCommitTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(vm, accepted.Task);
            Task closing = vm.ShutdownAsync();
            bool shutdownHeartbeat = false;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => shutdownHeartbeat = true, DispatcherPriority.Input);
            Assert(shutdownHeartbeat && !closing.IsCompleted,
                "Shutdown drains the accepted result/CFG transaction asynchronously before disposing services");
            accepted.SetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
        });
    }
}
