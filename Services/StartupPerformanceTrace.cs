using System.Diagnostics;

namespace JBZUniversalTester.Services;

public static class StartupPerformanceTrace
{
    private static readonly long Started = Stopwatch.GetTimestamp();
    private static readonly double ProcessOffsetMs = GetProcessAgeMs();

    public static void Begin() { _ = Started; }

    private static double GetProcessAgeMs()
    {
        using Process process = Process.GetCurrentProcess();
        return Math.Max(0, (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds);
    }

    public static void Mark(string marker)
    {
        double elapsedMs = Stopwatch.GetElapsedTime(Started).TotalMilliseconds;
        AsyncFileLogService.Current.Application(FormattableString.Invariant(
            $"STARTUP_PERF {marker} elapsed_ms={elapsedMs:0.###} process_elapsed_ms={ProcessOffsetMs + elapsedMs:0.###}"));
    }

    public static void Stage(string stage, long started) => Mark(FormattableString.Invariant(
        $"phase={stage} duration_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.###}"));

    public static IDisposable Measure(string marker) => new Scope(marker);

    private sealed class Scope : IDisposable
    {
        private readonly string _marker;
        private readonly long _started = Stopwatch.GetTimestamp();

        public Scope(string marker)
        {
            _marker = marker;
            Mark($"{marker}_BEGIN");
        }

        public void Dispose()
        {
            double elapsedMs = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            AsyncFileLogService.Current.Application($"STARTUP_PERF {_marker}_END duration_ms={elapsedMs:0.###}");
        }
    }
}
