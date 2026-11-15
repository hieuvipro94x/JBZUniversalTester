using System.IO;
using System.Reflection;
using System.Windows.Threading;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestApplicationAsyncLogShutdown()
    {
        OnSettingsDispatcher(async () =>
        {
            var writer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var logger = new AsyncFileLogService();
            typeof(AsyncFileLogService).GetField("_writerTask", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(logger, writer.Task);
            Task shutdown = logger.DisposeAsync().AsTask();
            try
            {
                bool heartbeat = false;
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => heartbeat = true, DispatcherPriority.Background);
                Assert(heartbeat && !shutdown.IsCompleted,
                    "Log draining must keep Dispatcher responsive while waiting for the final writer");
            }
            finally
            {
                writer.TrySetResult();
                await shutdown;
                await logger.DisposeAsync();
            }

            string root = Path.Combine(Path.GetTempPath(), "JBZ-async-log-" + Guid.NewGuid().ToString("N"));
            try
            {
                var actual = new AsyncFileLogService();
                actual.Configure(true, root);
                for (int index = 0; index < 1000; index++) actual.Application("DRAIN-" + index);
                await actual.DisposeAsync();
                string[] lines = await File.ReadAllLinesAsync(Path.Combine(root, "JBZUniversalTester.log"));
                Assert(lines.Count(line => line.Contains("DRAIN-", StringComparison.Ordinal)) == 1000 &&
                       lines[^1].Contains("DRAIN-999", StringComparison.Ordinal),
                    "Asynchronous shutdown must drain every queued entry in order before returning");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        });
    }
}
