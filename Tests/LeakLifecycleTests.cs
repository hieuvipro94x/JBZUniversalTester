using System.IO.Ports;
using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static class LeakLifecycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, Private)!.Invoke(target, args);
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void LateOpenAndClose()
    {
        var service = new WaterProofSerialService();
        var gate = (SemaphoreSlim)typeof(WaterProofSerialService).GetField("_gate", Private)!.GetValue(service)!;
        var opened = new TaskCompletionSource<SerialPort>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var port = new SerialPort(); // Never opens a physical device.
        using var readerEntered = new ManualResetEventSlim();
        using var readerRelease = new ManualResetEventSlim();
        Task reader = Task.Run(() =>
        {
            lock (port)
            {
                readerEntered.Set();
                readerRelease.Wait();
            }
        });
        try
        {
            Check(readerEntered.Wait(TimeSpan.FromSeconds(5)), "Simulated native reader started");
            gate.Wait();
            Call(service, "ObserveLateOpen", opened.Task);
            Call(service, "ReleasePublicGate", 1, null);
            Check(service.SessionState == WaterProofSessionState.Closing && !gate.Wait(0),
                "Timeout cannot release gate while Open is still pending");
            opened.SetResult(port);
            Check(!gate.Wait(TimeSpan.FromMilliseconds(100)),
                "Late Open must not release gate while Close waits for the native reader");
            service.AbortActiveRun();
            service.AbortActiveRun();
            Check(!gate.Wait(0), "Repeated abort cannot bypass pending cleanup");
            readerRelease.Set();
            reader.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Check(gate.Wait(TimeSpan.FromSeconds(5)), "Gate becomes available after real cleanup completion");
            Check(service.SessionState == WaterProofSessionState.Idle && gate.CurrentCount == 0,
                "Cleanup releases exactly one permit and transitions to IDLE");
            gate.Release();
        }
        finally
        {
            readerRelease.Set();
            opened.TrySetResult(port);
            reader.GetAwaiter().GetResult();
            service.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public static void CoalescedProgress()
    {
        var service = new WaterProofSerialService();
        var context = new QueuedContext();
        var profile = new WaterProofModelSettings { Enabled = true, Channel1Enabled = true };
        var vm = new WaterProofTestViewModel("TEST", profile);
        Action<WaterProofProgress> callback = vm.ApplyProgress;
        Call(service, "ActivateRun", 1);
        void Report(WaterProofStage stage, double value) => Call(service, "SafeProgress",
            callback, new WaterProofProgress(stage, [value, 0, 0], ""), 1, context);

        Report(WaterProofStage.Pressurizing, 80);
        Report(WaterProofStage.Pressurizing, 84);
        Report(WaterProofStage.Waiting, 83.7);
        Check(context.Pending == 1, "PRESS/WAIT burst queues only one UI callback");
        context.Drain();
        Check(vm.Channel1Text == "0.3" && vm.StageText == "WAIT",
            "Coalescing retains the last PRESS reference when WAIT supersedes it");
        foreach (double delta in new[] { 0.5, 2.0, 5.6 })
        {
            Report(WaterProofStage.Waiting, 84 - delta);
            context.Drain();
            Check(vm.Channel1Text == delta.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) && vm.IsRunning,
                "Live leak changes without producing an official verdict");
        }
        Report(WaterProofStage.Waiting, 70);
        Call(service, "DeactivateRun", 1);
        Call(service, "ActivateRun", 2);
        context.Drain();
        Check(vm.Channel1Text == "5.6", "Queued old-run progress cannot mutate the old or new UI");
        vm.Cancel();
        vm.ApplyFinal(new WaterProofRunResult([new(1, true, 84, 83, 1, true)], true, ""));
        Check(vm.Channel1Text == "5.6", "Closed window ignores a late final result");
        service.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public static void SuspendedReader()
    {
        var board = new D2xxBoardTransport(string.Empty);
        using var cancellation = new CancellationTokenSource();
        Task reader = (Task)Call(board, "ScanLoopAsync", cancellation.Token)!;
        try
        {
            Check(!reader.Wait(TimeSpan.FromMilliseconds(100)),
                "Stopped scan waits for resume without polling a native handle");
            long polls = (long)typeof(D2xxBoardTransport).GetField("_pollCount", Private)!.GetValue(board)!;
            Check(polls == 0, "Suspended reader performs zero queue polls");
            typeof(D2xxBoardTransport).GetField("_firmwareScanning", Private)!.SetValue(board, 1);
            ((ManualResetEvent)typeof(D2xxBoardTransport).GetField("_scanActiveEvent", Private)!.GetValue(board)!).Set();
            // A null handle ends the loop immediately after it wakes; no native I/O.
            reader.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            polls = (long)typeof(D2xxBoardTransport).GetField("_pollCount", Private)!.GetValue(board)!;
            Check(polls == 1, "Resume event wakes the reader immediately");
        }
        finally
        {
            cancellation.Cancel();
            reader.GetAwaiter().GetResult();
            board.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public int Pending => _queue.Count;
        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
        public void Drain()
        {
            while (_queue.TryDequeue(out var item)) item.Callback(item.State);
        }
    }
}
