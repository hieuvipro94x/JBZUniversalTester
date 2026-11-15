using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static ScanFrame SessionFrame(long sequence, long generation, int cards = 2, bool complete = true) => new(
        DateTime.Now, cards, new HashSet<int>(), [], complete, 0, sequence,
        new Dictionary<int, IReadOnlySet<int>>(), new Dictionary<int, int>(), BoardScanMode.Production,
        ExpectedIoCount: cards * 64, SourceCount: complete ? cards * 64 : 11,
        ScanUnitCount: cards, TerminatorKnown: true, ScanGeneration: generation);

    private static void TestScanRestartAfterUnmonitoredSession()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var board = new FakeBoard();
        var supervisor = new ScanSupervisor(board, _ => { }, clock);
        // V444 log: settings/model expected 10 cards while the board scanned 2.
        supervisor.EnsureProductionScanAsync(640, CancellationToken.None).GetAwaiter().GetResult();
        board.Publish(SessionFrame(2000, 1));
        Assert(supervisor.HealthSnapshot.State == ScanHealthState.Starting,
            "A complete frame with the wrong capacity cannot arm testing");
        supervisor.Suspend("ManualMode");
        board.StopScanAsync().GetAwaiter().GetResult();
        board.SetRequestedScanCapacityForTest(2);
        supervisor.EnsureProductionScanAsync(128, CancellationToken.None).GetAwaiter().GetResult();
        board.Publish(SessionFrame(2001, 1));
        Assert(supervisor.HealthSnapshot.State == ScanHealthState.Starting,
            "A delayed previous-session frame cannot satisfy the restart gate even before any monitored generation exists");
        board.Publish(SessionFrame(1, 3, complete: false));
        board.Publish(SessionFrame(2, 3, cards: 10));
        Assert(supervisor.HealthSnapshot.State == ScanHealthState.Starting,
            "Incomplete or wrong-capacity frames still cannot arm the new session");
        board.Publish(SessionFrame(3, 3));
        Assert(supervisor.HealthSnapshot.State == ScanHealthState.Monitoring && supervisor.HealthSnapshot.ScanGeneration == 3,
            "The first valid new-session frame is accepted despite resetting sequence 2000 to 3");
        for (int restart = 0; restart < 100; restart++)
        {
            long previous = 3 + restart * 2;
            supervisor.Suspend("ManualMode");
            board.StopScanAsync().GetAwaiter().GetResult();
            supervisor.EnsureProductionScanAsync(128, CancellationToken.None).GetAwaiter().GetResult();
            board.Publish(SessionFrame(9000, previous - 1));
            Assert(supervisor.HealthSnapshot.State == ScanHealthState.Starting, "Older callbacks cannot arm a later restart");
            board.Publish(SessionFrame(1, previous + 2));
            Assert(supervisor.HealthSnapshot.State == ScanHealthState.Monitoring, "Repeated manual STOP/START accepts sequence 1");
            clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert(!supervisor.TryBeginWatchdogRecovery(3000, 5500, out _), "A valid restarted stream never falsely times out");
        }
        Assert(board.ConnectAttempts == 0 && board.Commands.All(c => c is "START" or "STOP"),
            "Session tracking neither reconnects USB nor operates relays, labels or production counters");
    }

    private static void TestScanSessionFreshnessAndWatchdogDiagnostics()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var board = new FakeBoard();
        // The transport can have counters before the supervisor has seen a generation.
        board.Publish(SessionFrame(5000, 5));
        var supervisor = new ScanSupervisor(board, _ => { }, clock);
        board.SetRequestedScanCapacityForTest(2);
        supervisor.EnsureProductionScanAsync(128, CancellationToken.None).GetAwaiter().GetResult();
        board.Publish(SessionFrame(1, 7));
        Assert(supervisor.HealthSnapshot.State == ScanHealthState.Monitoring,
            "A known new transport generation does not depend on an unavailable previous monitored generation");
        supervisor.Suspend("keep-alive");
        supervisor.EnsureProductionScanAsync(128, CancellationToken.None).GetAwaiter().GetResult();
        board.Publish(SessionFrame(9000, 5));
        Assert(supervisor.HealthSnapshot.State == ScanHealthState.Starting,
            "Reusing a running stream also rejects a stale generation with a larger sequence");
        board.Publish(SessionFrame(2, 7));
        Assert(supervisor.HealthSnapshot.State == ScanHealthState.Monitoring,
            "Keep-alive accepts the next frame in the same current session without STOP/START");
        clock.Advance(TimeSpan.FromMilliseconds(1001));
        board.Publish(SessionFrame(2, 7));
        board.Publish(SessionFrame(5001, 5));
        Assert(supervisor.TryBeginWatchdogRecovery(1500, 1000, out var stall) &&
            stall.Reason == "monitoring-stall" && stall.CompleteFrameAgeMilliseconds >= 1001 && stall.StateAgeMilliseconds >= 1001,
            "Duplicate/old frames do not hide a real stall and diagnostics retain the actual elapsed time");
        Assert(!supervisor.TryBeginWatchdogRecovery(1500, 1000, out _), "One stall creates only one fault transition");
        supervisor.Suspend("CapacityChange");
        board.StopScanAsync().GetAwaiter().GetResult();
        supervisor.EnsureProductionScanAsync(128, CancellationToken.None).GetAwaiter().GetResult();
        clock.Advance(TimeSpan.FromMilliseconds(1501));
        Assert(supervisor.TryBeginWatchdogRecovery(1500, 1000, out var missing) &&
            missing.Reason == "first-frame-timeout" && missing.StateAgeMilliseconds >= 1501,
            "A genuinely silent restarted board still times out with a truthful duration");

        var legacy = new FakeBoard();
        var legacySupervisor = new ScanSupervisor(legacy, _ => { });
        legacySupervisor.EnsureProductionScanAsync(640, CancellationToken.None).GetAwaiter().GetResult();
        legacy.Publish(SessionFrame(10, 0, cards: 10));
        legacySupervisor.Suspend("legacy-keep-alive");
        legacySupervisor.EnsureProductionScanAsync(640, CancellationToken.None).GetAwaiter().GetResult();
        legacy.Publish(SessionFrame(10, 0, cards: 10));
        Assert(legacySupervisor.HealthSnapshot.State == ScanHealthState.Starting,
            "Generation-less legacy frames still require a fresh sequence");
        legacy.Publish(SessionFrame(11, 0, cards: 10));
        Assert(legacySupervisor.HealthSnapshot.State == ScanHealthState.Monitoring,
            "Fresh generation-less legacy frames remain compatible");
    }
}
