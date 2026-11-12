using System.Reflection;
using System.Windows.Threading;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestD2xxNativeIoResponsiveness()
    {
        OnSettingsDispatcher(async () =>
        {
            // Exercise the production ownership boundary without opening any FTDI device.
            var board = new D2xxBoardTransport("fixture-only");
            MethodInfo method = typeof(D2xxBoardTransport).GetMethod("RunControlIoAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(int));
            Task<int> Run(Func<int> operation, CancellationToken token = default) =>
                (Task<int>)method.Invoke(board, [operation, token])!;
            using var release = new ManualResetEventSlim();
            using var cancel = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int uiThread = Environment.CurrentManagedThreadId, driverThread = uiThread;
            Task<int> native = Run(() =>
            {
                driverThread = Environment.CurrentManagedThreadId;
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture release");
                return 42;
            }, cancel.Token);
            bool secondEntered = false;
            Task<int>? second = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancel.Cancel();
                second = Run(() => { secondEntered = true; return 43; });
                using var waitingCancel = new CancellationTokenSource();
                Task<int> cancelledWaiter = Run(() => throw new Exception("Must not enter cancelled native call"), waitingCancel.Token);
                waitingCancel.Cancel();
                bool cancelled = false;
                try { await cancelledWaiter; }
                catch (OperationCanceledException) { cancelled = true; }
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert(driverThread != uiThread && !native.IsCompleted && !secondEntered && cancelled,
                    "Blocked driver keeps UI responsive; cancellation cannot release an active native handle");
            }
            finally { release.Set(); }
            Assert(await native == 42 && await second! == 43, "Exclusive driver calls finish in ownership order");
            bool reported = false;
            try { await Run(() => throw new IOException("Fixture native error")); }
            catch (IOException) { reported = true; }
            Assert(reported && await Run(() => 44) == 44, "Driver exceptions propagate and release ownership");
            int waiters = (int)typeof(D2xxBoardTransport).GetField("_controlWaiters",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(board)!;
            Assert(waiters == 0, "Native priority counter balances success, cancellation and faults");
        });
    }

    private static void TestBcwJournalResponsiveness()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-JournalUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            OnSettingsDispatcher(async () =>
            {
                string path = Path.Combine(root, "fixture.db"), output = Path.Combine(root, "printer.epl");
                var store = new BcwSerialStore(path);
                await Task.Run(() => store.Peek(new DateTime(2026, 10, 14), "09", "LH"));
                using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
                connection.Open();
                using var transaction = connection.BeginTransaction(deferred: false);
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await using var printer = new LabelPrintService(store, () =>
                {
                    entered.TrySetResult();
                    return new DateTime(2026, 10, 14);
                });
                Task<JBZUniversalTester.Models.LabelPrintTransportResult> printing =
                    printer.PrintPassLabelAsync(BcwRequest("ui-locked-journal", output));
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Assert(!printing.IsCompleted && !File.Exists(output),
                        "Busy SQLite journal leaves UI responsive and cannot send an unjournaled label");
                }
                finally { transaction.Rollback(); }
                var result = await printing;
                Assert(result.Printed && File.Exists(output), "Printing resumes after journal ownership becomes available");
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT State FROM BcwPrints WHERE CycleId='ui-locked-journal';";
                Assert((string?)command.ExecuteScalar() == "Sent", "Physical send result stays durably recorded");
                command.CommandText = "PRAGMA integrity_check;";
                Assert((string?)command.ExecuteScalar() == "ok", "Temporary label journal integrity remains ok");
            });
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, recursive: true); }
    }
}
