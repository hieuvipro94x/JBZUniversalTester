using System.Diagnostics;
using System.Reflection;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestResistanceTraceTiming()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        MethodInfo recovery = typeof(D2xxBoardTransport).GetMethod("RunResistanceRecoveryAsync", flags)!;
        MethodInfo gap = typeof(D2xxBoardTransport).GetMethod("WaitForResistanceCommandGapAsync", flags)!;
        Task Gap(long timestamp, CancellationToken token = default) => (Task)gap.Invoke(null, [timestamp, token])!;
        long expiredTx = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
        Assert(Gap(expiredTx).IsCompletedSuccessfully,
            "Already elapsed native TX spacing is not followed by another full 350 ms wait");
        long freshTx = Stopwatch.GetTimestamp();
        Gap(freshTx).GetAwaiter().GetResult();
        Assert(Stopwatch.GetElapsedTime(freshTx).TotalMilliseconds >= 350,
            "Native TX deadline never shortens the original hardware's 350 ms interval");
        using var cancel = new CancellationTokenSource();
        Task waiting = Gap(Stopwatch.GetTimestamp(), cancel.Token);
        cancel.Cancel();
        bool cancelled = false;
        try { waiting.GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "Resistance command waits remain cancellable");

        OnSettingsDispatcher(() =>
        {
            var commands = new List<string>();
            var writes = new List<long>();
            var waits = new List<long>();
            Func<byte[], CancellationToken, Task<long>> write = (command, token) => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                commands.Add(Convert.ToHexString(command));
                long nativeTx = Stopwatch.GetTimestamp();
                writes.Add(nativeTx);
                return nativeTx;
            }, token);
            Func<long, CancellationToken, Task> wait = async (timestamp, token) =>
            {
                waits.Add(timestamp);
                await Task.Delay(1, token).ConfigureAwait(false);
            };
            Task running = (Task)recovery.Invoke(null, [write, wait, CancellationToken.None])!;
            // Deliberately do not pump the UI while the control sequence runs.
            // No native device is opened: the writer records fixture bytes only.
            Task.Run(async () => await running.WaitAsync(TimeSpan.FromSeconds(3))).GetAwaiter().GetResult();
            Assert(commands.SequenceEqual(new[] { "91000000", "90000030", "91000000", "90000030", "91000000", "90000030" }) &&
                   waits.SequenceEqual(writes.Take(5)),
                "Original recovery's six writes and five native TX deadlines continue even when the UI dispatcher is busy");
            return Task.CompletedTask;
        });

        int attempted = 0;
        Func<byte[], CancellationToken, Task<long>> failingWrite = (_, _) =>
            ++attempted == 3 ? Task.FromException<long>(new IOException("Fixture write failed")) : Task.FromResult(Stopwatch.GetTimestamp());
        Func<long, CancellationToken, Task> noWait = (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        bool failed = false;
        try { ((Task)recovery.Invoke(null, [failingWrite, noWait, CancellationToken.None])!).GetAwaiter().GetResult(); }
        catch (IOException) { failed = true; }
        Assert(failed && attempted == 3, "A failed recovery write propagates and cannot mark the remaining commands as complete");
        using var alreadyCancelled = new CancellationTokenSource();
        alreadyCancelled.Cancel();
        attempted = 0;
        cancelled = false;
        try { ((Task)recovery.Invoke(null, [failingWrite, noWait, alreadyCancelled.Token])!).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled && attempted == 0, "A cancelled recovery cannot send a new command");
    }
}
