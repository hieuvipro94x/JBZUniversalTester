using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public enum ScanHealthState
{
    Suspended = 0,
    Starting = 1,
    Monitoring = 2,
    Recovering = 3,
    Faulted = 4
}

public readonly record struct ScanHealthSnapshot(
    ScanHealthState State,
    string Reason,
    BoardScanMode ExpectedMode,
    long ScanGeneration,
    long LastCompleteFrameSequence,
    double StateAgeMilliseconds,
    double CompleteFrameAgeMilliseconds);

/// <summary>
/// Owns the scan/watchdog lifecycle. A stopped scan is healthy while Suspended;
/// old frame timestamps are discarded before START and Monitoring begins only
/// after a complete frame from the expected scan session.
/// </summary>
public sealed class ScanSupervisor
{
    private const int MinimumFirstFrameTimeoutMs = 1_500;
    private const int FirstFrameTimeoutPerExpansionCardMs = 1_500;
    private const int StallTimeoutMarginMs = 2_500;
    private readonly IBoardTransport _board;
    private readonly Action<string> _log;
    private readonly TimeProvider _timeProvider;
    private readonly object _healthGate = new();

    private ScanHealthState _healthState = ScanHealthState.Suspended;
    private string _healthReason = "initializing";
    private BoardScanMode _expectedMode = BoardScanMode.Production;
    private long _stateEnteredAt;
    private long _startCommandCompletedAt = -1;
    private long _lastCompleteFrameAt = -1;
    private long _lastCompleteFrameSequence;
    private long _lastObservedScanGeneration;
    private long _previousScanGeneration;
    private long _baselineCompleteFrames;
    private long _baselineCompleteFrameSequence;
    private bool _requireNewGeneration;
    private BoardCapacity _expectedCapacity = BoardCapacity.Create(1);
    private TaskCompletionSource<ScanFrame>? _firstFrameWaiter;

    public ScanSupervisor(
        IBoardTransport board,
        Action<string> log,
        TimeProvider? timeProvider = null)
    {
        _board = board;
        _log = log;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _stateEnteredAt = _timeProvider.GetTimestamp();
        _board.FrameReceived += OnFrameReceived;
    }

    public ScanHealthSnapshot HealthSnapshot
    {
        get
        {
            lock (_healthGate)
                return SnapshotUnsafe(_timeProvider.GetTimestamp());
        }
    }

    public void Suspend(string reason)
    {
        lock (_healthGate)
        {
            _healthState = ScanHealthState.Suspended;
            _healthReason = reason;
            _stateEnteredAt = _timeProvider.GetTimestamp();
            _startCommandCompletedAt = -1;
            _lastCompleteFrameAt = -1;
            _firstFrameWaiter?.TrySetCanceled();
            _firstFrameWaiter = null;
        }
        _log($"SCAN_HEALTH state=Suspended reason={reason}");
    }

    public async Task<bool> EnsureProductionScanAsync(int maxIo, CancellationToken ct)
    {
        if (!_board.IsConnected)
            return false;

        _board.ConfigureActiveScanRange(maxIo);
        BoardCapacity requestedCapacity = _board.Capacity;
        bool capacityChanged = _board.AppliedScanCapacity is not BoardCapacity appliedCapacity ||
                               !HasSameActiveRange(appliedCapacity, requestedCapacity);

        if (capacityChanged && _board.AppliedScanCapacity is BoardCapacity previousCapacity)
            _log($"SCAN_CAPACITY_IN_PLACE old={previousCapacity.ScanCardCount}/{previousCapacity.TotalIoCapacity} " +
                 $"new={requestedCapacity.ScanCardCount}/{requestedCapacity.TotalIoCapacity}");

        if (!capacityChanged &&
            _board.IsScanning &&
            _board.CurrentScanMode == BoardScanMode.Production)
        {
            if (HealthSnapshot.State is ScanHealthState.Suspended or ScanHealthState.Faulted)
            {
                _ = BeginFirstFrameWait(
                    BoardScanMode.Production,
                    requireNewGeneration: false,
                    recovering: false,
                    _board.CompleteFramesReceived,
                    requestedCapacity,
                    "reuse-running-stream");
                MarkStartCommandCompleted();
            }
            return false;
        }

        _ = BeginFirstFrameWait(
            BoardScanMode.Production,
            requireNewGeneration: true,
            recovering: false,
            _board.CompleteFramesReceived,
            requestedCapacity,
            "ensure-production");
        try
        {
            await _board.StartScanAsync(BoardScanMode.Production, ct);
            MarkStartCommandCompleted();
            return true;
        }
        catch
        {
            Suspend("ensure-production-failed");
            throw;
        }
    }

    public async Task StartProductionScanAndVerifyFrameAsync(
        int maxIo,
        CancellationToken ct,
        string reason)
    {
        ct.ThrowIfCancellationRequested();
        _board.ConfigureActiveScanRange(maxIo);
        BoardCapacity configuredCapacity = _board.Capacity;
        int firstFrameTimeoutMs = ResolveFirstFrameTimeoutMs(configuredCapacity);
        bool capacityChanged = _board.AppliedScanCapacity is not BoardCapacity appliedCapacity ||
                               !HasSameActiveRange(appliedCapacity, configuredCapacity);
        ScanHealthSnapshot health = HealthSnapshot;
        BoardConnectionState connectionState = _board.ConnectionState;
        bool hardwareOwner = connectionState == BoardConnectionState.PausedForHardwareOperation;
        bool recoveryPending = health.State is ScanHealthState.Recovering or ScanHealthState.Faulted;
        bool generationValid = health.ScanGeneration > 0;
        bool reuse = _board.IsConnected &&
                     !capacityChanged &&
                     _board.IsScanning &&
                     _board.CurrentScanMode == BoardScanMode.Production &&
                     connectionState == BoardConnectionState.Scanning &&
                     generationValid &&
                     !recoveryPending;
        string decisionReason = !_board.IsConnected
            ? "transport-disconnected"
            : capacityChanged
                ? "capacity-changed"
                : !_board.IsScanning
                    ? "stream-stopped"
                    : _board.CurrentScanMode != BoardScanMode.Production
                        ? "mode-changed"
                        : connectionState != BoardConnectionState.Scanning
                            ? $"connection-state-{connectionState}"
                            : !generationValid
                                ? "generation-invalid"
                                : recoveryPending
                                    ? $"health-{health.State}"
                                    : "stream-compatible";

        _log(
            $"SCAN_RESUME_DECISION reason={reason}:{decisionReason} reuse={reuse.ToString().ToLowerInvariant()} " +
            $"isScanning={_board.IsScanning.ToString().ToLowerInvariant()} mode={_board.CurrentScanMode} " +
            $"capacity={configuredCapacity.ScanCardCount}/{configuredCapacity.TotalIoCapacity} " +
            $"generation={health.ScanGeneration} hardwareOwner={hardwareOwner.ToString().ToLowerInvariant()}");

        if (capacityChanged && _board.AppliedScanCapacity is BoardCapacity previousCapacity)
            _log($"SCAN_CAPACITY_IN_PLACE old={previousCapacity.ScanCardCount}/{previousCapacity.TotalIoCapacity} " +
                 $"new={configuredCapacity.ScanCardCount}/{configuredCapacity.TotalIoCapacity} reason={reason}");

        long baselineFrameCount = _board.CompleteFramesReceived;
        long resumeStartedAt = _timeProvider.GetTimestamp();
        TaskCompletionSource<ScanFrame> firstFrame = BeginFirstFrameWait(
            BoardScanMode.Production,
            requireNewGeneration: !reuse,
            recovering: false,
            baselineFrameCount,
            configuredCapacity,
            reason);
        if (reuse)
        {
            _log(
                $"SCAN_KEEP_ALIVE reason={reason} start_seq={_board.LastCompleteFrameSequence} " +
                $"generation={health.ScanGeneration}");
        }
        else
        {
            _log($"SCAN_HARD_RESTART reason={reason}:{decisionReason}");
            await _board.StartScanAsync(BoardScanMode.Production, ct);
        }
        MarkStartCommandCompleted();

        ScanFrame frame = await WaitForFirstFrameAsync(
            firstFrame,
            baselineFrameCount,
            firstFrameTimeoutMs,
            ct,
            reason);
        if (reuse)
        {
            _log(
                $"SCAN_KEEP_ALIVE_READY elapsed_ms={ElapsedMilliseconds(resumeStartedAt, _timeProvider.GetTimestamp()):0.###} " +
                $"seq={frame.Sequence}");
        }
        else
        {
            _log($"START_SCAN OK sau {reason}: đã nhận frame production mới generation={frame.ScanGeneration}.");
        }
    }

    public bool TryBeginWatchdogRecovery(
        int startFrameTimeoutMs,
        int monitoringStallTimeoutMs,
        out ScanHealthSnapshot stalled)
    {
        lock (_healthGate)
        {
            long now = _timeProvider.GetTimestamp();
            stalled = SnapshotUnsafe(now);
            bool startTimedOut = _healthState == ScanHealthState.Starting &&
                                 _startCommandCompletedAt >= 0 &&
                                 ElapsedMilliseconds(_startCommandCompletedAt, now) > startFrameTimeoutMs;
            bool monitoringTimedOut = _healthState == ScanHealthState.Monitoring &&
                                      _lastCompleteFrameAt >= 0 &&
                                      ElapsedMilliseconds(_lastCompleteFrameAt, now) > monitoringStallTimeoutMs;
            bool stoppedUnexpectedly = _healthState == ScanHealthState.Monitoring && !_board.IsScanning;
            if (!startTimedOut && !monitoringTimedOut && !stoppedUnexpectedly)
                return false;

            _healthState = ScanHealthState.Recovering;
            _healthReason = startTimedOut
                ? "first-frame-timeout"
                : stoppedUnexpectedly ? "scan-stopped" : "monitoring-stall";
            _stateEnteredAt = now;
            _firstFrameWaiter?.TrySetCanceled();
            _firstFrameWaiter = null;
            stalled = SnapshotUnsafe(now);
            return true;
        }
    }

    public bool TryBeginDisconnectedRecovery(out ScanHealthSnapshot disconnected)
    {
        lock (_healthGate)
        {
            long now = _timeProvider.GetTimestamp();
            disconnected = SnapshotUnsafe(now);
            if (_healthState is ScanHealthState.Suspended or
                ScanHealthState.Recovering or
                ScanHealthState.Faulted)
                return false;

            _healthState = ScanHealthState.Recovering;
            _healthReason = "transport-disconnected";
            _stateEnteredAt = now;
            _firstFrameWaiter?.TrySetCanceled();
            _firstFrameWaiter = null;
            disconnected = SnapshotUnsafe(now);
            return true;
        }
    }

    public ScanHealthSnapshot BeginRecovery(string reason)
    {
        lock (_healthGate)
        {
            long now = _timeProvider.GetTimestamp();
            if (_healthState != ScanHealthState.Faulted)
            {
                _healthState = ScanHealthState.Recovering;
                _healthReason = reason;
                _stateEnteredAt = now;
                _firstFrameWaiter?.TrySetCanceled();
                _firstFrameWaiter = null;
            }
            return SnapshotUnsafe(now);
        }
    }

    public async Task<bool> RecoverSoftAsync(
        int maxIo,
        BoardScanMode mode,
        CancellationToken recoveryToken)
    {
        _log("SCAN_RECOVERY level=soft attempt=1");
        try
        {
            _board.ConfigureActiveScanRange(maxIo);
            await _board.StopScanAsync(recoveryToken);
            long baseline = _board.CompleteFramesReceived;
            TaskCompletionSource<ScanFrame> firstFrame = BeginFirstFrameWait(
                mode,
                requireNewGeneration: true,
                recovering: true,
                baseline,
                _board.Capacity,
                "soft-recovery");
            await _board.StartScanAsync(mode, recoveryToken);
            MarkStartCommandCompleted();
            ScanFrame frame = await WaitForFirstFrameAsync(
                firstFrame,
                baseline,
                ResolveFirstFrameTimeoutMs(_board.Capacity),
                recoveryToken,
                "SOFT_RECOVERY");
            _log($"SCAN_RECOVERY success level=soft seq={frame.Sequence} generation={frame.ScanGeneration}");
            return true;
        }
        catch (OperationCanceledException) when (recoveryToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log($"SCAN_RECOVERY level=soft failed reason={ex.Message}");
            return false;
        }
    }

    public async Task<bool> RecoverReopenAsync(
        int maxIo,
        BoardScanMode mode,
        CancellationToken recoveryToken)
    {
        _log("SCAN_RECOVERY level=reopen attempt=1");
        try
        {
            await _board.DisconnectAsync();
            recoveryToken.ThrowIfCancellationRequested();
            await _board.ConnectAsync(recoveryToken);
            _board.ConfigureActiveScanRange(maxIo);
            long baseline = _board.CompleteFramesReceived;
            TaskCompletionSource<ScanFrame> firstFrame = BeginFirstFrameWait(
                mode,
                requireNewGeneration: false,
                recovering: true,
                baseline,
                _board.Capacity,
                "reopen-recovery");
            await _board.StartScanAsync(mode, recoveryToken);
            MarkStartCommandCompleted();
            ScanFrame frame = await WaitForFirstFrameAsync(
                firstFrame,
                baseline,
                ResolveFirstFrameTimeoutMs(_board.Capacity),
                recoveryToken,
                "REOPEN_RECOVERY");
            _log($"SCAN_RECOVERY success level=reopen seq={frame.Sequence} generation={frame.ScanGeneration}");
            return true;
        }
        catch (OperationCanceledException) when (recoveryToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            MarkFaulted(ex.Message);
            _log($"SCAN_RECOVERY failed level=reopen reason={ex.Message}");
            return false;
        }
    }

    public void MarkFaulted(string reason)
    {
        lock (_healthGate)
        {
            _healthState = ScanHealthState.Faulted;
            _healthReason = reason;
            _stateEnteredAt = _timeProvider.GetTimestamp();
            _firstFrameWaiter?.TrySetCanceled();
            _firstFrameWaiter = null;
        }
        _log($"SCAN_HEALTH state=Faulted reason={reason}");
    }

    public static int ResolveFirstFrameTimeoutMs(BoardCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        return Math.Max(
            MinimumFirstFrameTimeoutMs,
            checked(capacity.ExpansionCardCount * FirstFrameTimeoutPerExpansionCardMs));
    }

    public static int ResolveProductionStallTimeoutMs(BoardCapacity capacity) =>
        checked(ResolveFirstFrameTimeoutMs(capacity) + StallTimeoutMarginMs);

    private TaskCompletionSource<ScanFrame> BeginFirstFrameWait(
        BoardScanMode mode,
        bool requireNewGeneration,
        bool recovering,
        long baselineCompleteFrames,
        BoardCapacity expectedCapacity,
        string reason)
    {
        TaskCompletionSource<ScanFrame> waiter = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_healthGate)
        {
            _firstFrameWaiter?.TrySetCanceled();
            _firstFrameWaiter = waiter;
            _expectedMode = mode;
            _previousScanGeneration = _lastObservedScanGeneration;
            _baselineCompleteFrames = Math.Max(
                baselineCompleteFrames,
                _board.CompleteFramesReceived);
            _baselineCompleteFrameSequence = _board.LastCompleteFrameSequence;
            _expectedCapacity = expectedCapacity;
            _requireNewGeneration = requireNewGeneration;
            _healthState = recovering ? ScanHealthState.Recovering : ScanHealthState.Starting;
            _healthReason = reason;
            _stateEnteredAt = _timeProvider.GetTimestamp();
            _startCommandCompletedAt = -1;
            _lastCompleteFrameAt = -1;
        }
        _log(
            $"SCAN_HEALTH state={(recovering ? "Recovering" : "Starting")} " +
            $"reason={reason} mode={mode} previous_generation={_previousScanGeneration}");
        return waiter;
    }

    private void MarkStartCommandCompleted()
    {
        lock (_healthGate)
        {
            _startCommandCompletedAt = _timeProvider.GetTimestamp();
            _stateEnteredAt = _startCommandCompletedAt;
        }
    }

    private async Task<ScanFrame> WaitForFirstFrameAsync(
        TaskCompletionSource<ScanFrame> firstFrame,
        long baselineFrameCount,
        int timeoutMs,
        CancellationToken ct,
        string reason)
    {
        using CancellationTokenRegistration registration = ct.Register(
            () => firstFrame.TrySetCanceled(ct));
        Task timeout = Task.Delay(timeoutMs, CancellationToken.None);
        Task completed = await Task.WhenAny(firstFrame.Task, timeout);
        ct.ThrowIfCancellationRequested();
        if (completed == firstFrame.Task)
            return await firstFrame.Task;

        throw new InvalidOperationException(BuildFrameTimeoutDiagnostic(reason, baselineFrameCount));
    }

    private void OnFrameReceived(object? sender, ScanFrame frame)
    {
        if (frame.Mode != _expectedMode ||
            !frame.Complete ||
            frame.UnknownBytes != 0 ||
            !frame.TerminatorKnown ||
            !HasExpectedCoverage(frame, _expectedCapacity))
        {
            return;
        }

        TaskCompletionSource<ScanFrame>? completedWaiter = null;
        double firstFrameLatencyMs = 0;
        lock (_healthGate)
        {
            if (_healthState is ScanHealthState.Starting or ScanHealthState.Recovering)
            {
                bool generationAccepted = !_requireNewGeneration ||
                                          frame.ScanGeneration == 0 ||
                                          _previousScanGeneration == 0 ||
                                          frame.ScanGeneration != _previousScanGeneration;
                // D2XX resets the frame sequence to 1 after a real STOP/START.
                // A new hardware generation is therefore the freshness proof for
                // hard restart; sequence monotonicity applies only to keep-alive.
                bool freshFrameAccepted = _requireNewGeneration &&
                                          frame.ScanGeneration != 0 &&
                                          _previousScanGeneration != 0
                    ? frame.ScanGeneration != _previousScanGeneration
                    : frame.Sequence > 0
                        ? frame.Sequence > _baselineCompleteFrameSequence
                        : _board.CompleteFramesReceived > _baselineCompleteFrames;
                if (!generationAccepted || !freshFrameAccepted)
                    return;

                long now = _timeProvider.GetTimestamp();
                long baseline = _startCommandCompletedAt >= 0
                    ? _startCommandCompletedAt
                    : _stateEnteredAt;
                firstFrameLatencyMs = ElapsedMilliseconds(baseline, now);
                _healthState = ScanHealthState.Monitoring;
                _healthReason = "first-complete-frame";
                _stateEnteredAt = now;
                _lastCompleteFrameAt = now;
                _lastCompleteFrameSequence = frame.Sequence;
                _lastObservedScanGeneration = frame.ScanGeneration;
                completedWaiter = _firstFrameWaiter;
                _firstFrameWaiter = null;
            }
            else if (_healthState == ScanHealthState.Monitoring)
            {
                if (_lastObservedScanGeneration != 0 &&
                    frame.ScanGeneration != 0 &&
                    frame.ScanGeneration != _lastObservedScanGeneration)
                {
                    return;
                }

                _lastCompleteFrameAt = _timeProvider.GetTimestamp();
                _lastCompleteFrameSequence = frame.Sequence;
            }
            else
            {
                return;
            }
        }

        if (completedWaiter is not null)
        {
            _log(
                $"SCAN_HEALTH first_frame seq={frame.Sequence} generation={frame.ScanGeneration} " +
                $"latency_ms={firstFrameLatencyMs:0.###}");
            _log("SCAN_HEALTH state=Monitoring");
            completedWaiter.TrySetResult(frame);
        }
    }

    private ScanHealthSnapshot SnapshotUnsafe(long now) => new(
        _healthState,
        _healthReason,
        _expectedMode,
        _lastObservedScanGeneration,
        _lastCompleteFrameSequence,
        ElapsedMilliseconds(_stateEnteredAt, now),
        _lastCompleteFrameAt < 0 ? 0 : ElapsedMilliseconds(_lastCompleteFrameAt, now));

    private static bool HasSameActiveRange(BoardCapacity left, BoardCapacity right) =>
        left.StartScanParameter == right.StartScanParameter &&
        left.TotalIoCapacity == right.TotalIoCapacity;

    private static bool HasExpectedCoverage(ScanFrame frame, BoardCapacity expected) =>
        // Zero metadata exists only on legacy/in-memory transport frames. Every
        // D2XX decoder frame carries both values and must match exactly.
        (frame.ExpectedIoCount == 0 && frame.ScanUnitCount == 0) ||
        (frame.ExpectedIoCount == expected.TotalIoCapacity &&
         frame.ScanUnitCount == expected.ScanCardCount);

    private string BuildFrameTimeoutDiagnostic(string reason, long baselineFrameCount)
    {
        BoardScanCapacity scan = _board.ScanCapacity;
        string endMarker = _board.LastFrameEndMarkerCode is byte code
            ? $"C0 {code:X2}"
            : "NONE";
        return
            $"BOARD_SCAN_STALLED after {reason}: ACTIVE_SCAN_UNITS={scan.ActiveScanUnits}; " +
            $"EXPECTED_IO={scan.ActiveIoCapacity}; LAST_SOURCE_COUNT={_board.LastFrameSourceCount}; " +
            $"LAST_END_MARKER={endMarker}; UNKNOWN_BYTES={_board.LastFrameUnknownBytes}; " +
            $"BASELINE_COMPLETE_FRAMES={baselineFrameCount}; FRAMES_RECEIVED={_board.FramesReceived}.";
    }

    private double ElapsedMilliseconds(long started, long now) =>
        started < 0 || now <= started
            ? 0
            : _timeProvider.GetElapsedTime(started, now).TotalMilliseconds;
}
