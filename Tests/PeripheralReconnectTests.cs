using System.Windows.Threading;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestPeripheralAutomaticReconnect()
    {
        OnSettingsDispatcher(async () =>
        {
            using var lifetime = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var usbInserted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0, active = 0, maximumActive = 0;
            bool connected = false;
            var reconnect = new PeripheralReconnectWorker("SELFTEST-Keysight", async ct =>
            {
                int currentActive = Interlocked.Increment(ref active);
                maximumActive = Math.Max(maximumActive, currentActive);
                try
                {
                    int attempt = Interlocked.Increment(ref attempts);
                    if (attempt < 3) throw new IOException("USB removed");
                    entered.TrySetResult();
                    await usbInserted.Task.WaitAsync(ct);
                    connected = true;
                }
                finally { Interlocked.Decrement(ref active); }
            }, lifetime.Token, TimeSpan.FromMilliseconds(10));

            reconnect.Request();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            for (int i = 0; i < 20; i++) reconnect.Request();
            Task owner = reconnect.Completion;
            Assert(!owner.IsCompleted && attempts == 3 && !connected && maximumActive == 1,
                "Unplugged device retries in the background with one connection owner despite repeated errors");
            int uiTicks = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) => uiTicks++;
            timer.Start();
            try
            {
                // A blocked VISA reconnect must neither block UI nor the other peripheral.
                int leakConnections = 0;
                var leak = new PeripheralReconnectWorker("SELFTEST-Leak", _ =>
                {
                    Interlocked.Increment(ref leakConnections);
                    return Task.CompletedTask;
                }, lifetime.Token, TimeSpan.FromMilliseconds(10));
                leak.Request();
                await leak.Completion.WaitAsync(TimeSpan.FromSeconds(3));
                await Task.Delay(50);
                Assert(leakConnections == 1 && uiTicks > 0 && !connected,
                    "Leak reconnect remains independent while VISA is waiting; Dispatcher continues processing input");
                usbInserted.SetResult();
                await owner.WaitAsync(TimeSpan.FromSeconds(3));
                Assert(connected && maximumActive == 1 && attempts == 4,
                    "USB reinsertion reconnects and drains coalesced requests without restarting the app or overlapping owners");
            }
            finally { timer.Stop(); }

            int completedAttempts = attempts;
            await Task.Delay(35);
            Assert(attempts == completedAttempts, "Successful reconnect stops polling and performs no measurements/relay actions");
            lifetime.Cancel();
            reconnect.Request();
            Assert(attempts == completedAttempts, "Shutdown prevents further reconnect requests");

            using var unpluggedLifetime = new CancellationTokenSource();
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var unplugged = new PeripheralReconnectWorker("SELFTEST-Unplugged", async ct =>
            {
                waiting.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }, unpluggedLifetime.Token, TimeSpan.FromMilliseconds(10));
            unplugged.Request();
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
            unpluggedLifetime.Cancel();
            await unplugged.Completion.WaitAsync(TimeSpan.FromSeconds(3));
            Assert(unplugged.Completion.IsCompletedSuccessfully,
                "Shutdown drains an in-flight reconnect before device session disposal");
        });
    }
}
