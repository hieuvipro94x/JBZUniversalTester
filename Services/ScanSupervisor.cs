using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed class ScanSupervisor
{
    private const int MinimumFirstFrameTimeoutMs = 1_500;
    private const int FirstFrameTimeoutPerExpansionCardMs = 1_500;
    private const int StallTimeoutMarginMs = 2_500;
    private readonly IBoardTransport _board;
    private readonly Action<string> _log;
    private int _recoveryActive;

    public ScanSupervisor(IBoardTransport board, Action<string> log)
    {
        _board = board;
        _log = log;
    }

    public async Task<bool> EnsureProductionScanAsync(int maxIo, CancellationToken ct)
    {
        if (!_board.IsConnected)
            return false;

        _board.ConfigureActiveScanRange(maxIo);
        BoardCapacity requestedCapacity = _board.Capacity;
        bool capacityChanged = _board.AppliedScanCapacity is not BoardCapacity appliedCapacity ||
                               !HasSameActiveRange(appliedCapacity, requestedCapacity);

        if (!capacityChanged &&
            _board.IsScanning &&
            _board.CurrentScanMode == BoardScanMode.Production)
            return false;

        await _board.StartScanAsync(BoardScanMode.Production, ct);
        return true;
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

        // App đã chạy Production scan nền ngay sau khi kết nối. Khi người dùng
        // mở TestView với cùng capacity, không STOP/START stream khỏe chỉ để
        // "xác nhận" lại. Việc STOP/START ở đây trước đây tạo thêm chuyển trạng
        // thái D2XX và làm startup/TestView có cảm giác đứng.
        if (!capacityChanged &&
            _board.IsScanning &&
            _board.CurrentScanMode == BoardScanMode.Production)
        {
            long healthyBaseline = _board.CompleteFramesReceived;
            if (await WaitForNextProductionFrameAsync(
                    healthyBaseline,
                    firstFrameTimeoutMs,
                    ct))
            {
                _log($"SCAN KEEP-ALIVE sau {reason}: giữ stream production hiện tại, đã nhận frame mới.");
                return;
            }

            _log($"SCAN KEEP-ALIVE sau {reason} không có frame mới - chuyển sang recovery STOP/START.");
        }

        long baselineFrameCount = _board.CompleteFramesReceived;
        await _board.StartScanAsync(BoardScanMode.Production, ct);

        if (await WaitForNextProductionFrameAsync(baselineFrameCount, firstFrameTimeoutMs, ct))
        {
            _log($"START_SCAN OK sau {reason}: đã nhận frame production mới.");
            return;
        }

        _log($"START_SCAN sau {reason} chưa có frame đầu - tự recovery STOP/START một lần.");
        await _board.StopScanAsync(CancellationToken.None);

        baselineFrameCount = _board.CompleteFramesReceived;
        _board.ConfigureActiveScanRange(maxIo);
        await _board.StartScanAsync(BoardScanMode.Production, ct);

        if (await WaitForNextProductionFrameAsync(baselineFrameCount, firstFrameTimeoutMs, ct))
        {
            _log($"Recovery scan OK sau {reason}: stream production đã trở lại.");
            return;
        }

        throw new InvalidOperationException(BuildFrameTimeoutDiagnostic(reason));
    }

    public async Task<bool> RecoverProductionScanStallAsync(
        double ageMs,
        long lastSequence,
        long framesReceived,
        int maxIo,
        Func<Task> reconnectAsync,
        CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _recoveryActive, 1, 0) != 0)
            return true;

        try
        {
            _log(
                $"[SCAN-WATCHDOG] STALL age={ageMs:0}ms seq={lastSequence} frames={framesReceived}; recovery STOP/START.");

            long baselineFrameCount = _board.CompleteFramesReceived;
            await _board.StopScanAsync(CancellationToken.None);
            _board.ConfigureActiveScanRange(maxIo);
            await _board.StartScanAsync(BoardScanMode.Production, ct);

            int firstFrameTimeoutMs = ResolveFirstFrameTimeoutMs(_board.Capacity);
            if (await WaitForNextProductionFrameAsync(baselineFrameCount, firstFrameTimeoutMs, ct))
            {
                _log($"[SCAN-WATCHDOG] recovery success first-frame seq={_board.LastFrameSequence}.");
                return true;
            }

            _log("[SCAN-WATCHDOG] STOP/START không có frame mới; reconnect D2XX.");
            await _board.DisconnectAsync();
            await reconnectAsync();

            baselineFrameCount = _board.CompleteFramesReceived;
            await EnsureProductionScanAsync(maxIo, ct);
            firstFrameTimeoutMs = ResolveFirstFrameTimeoutMs(_board.Capacity);
            if (await WaitForNextProductionFrameAsync(baselineFrameCount, firstFrameTimeoutMs, ct))
            {
                _log($"[SCAN-WATCHDOG] reconnect recovery success first-frame seq={_board.LastFrameSequence}.");
                return true;
            }

            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _recoveryActive, 0);
        }
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

    private static bool HasSameActiveRange(BoardCapacity left, BoardCapacity right) =>
        left.StartScanParameter == right.StartScanParameter &&
        left.TotalIoCapacity == right.TotalIoCapacity;

    private string BuildFrameTimeoutDiagnostic(string reason)
    {
        BoardScanCapacity scan = _board.ScanCapacity;
        string endMarker = _board.LastFrameEndMarkerCode is byte code
            ? $"C0 {code:X2}"
            : "NONE";
        return
            $"BOARD_SCAN_STALLED after {reason}: ACTIVE_SCAN_UNITS={scan.ActiveScanUnits}; " +
            $"EXPECTED_IO={scan.ActiveIoCapacity}; LAST_SOURCE_COUNT={_board.LastFrameSourceCount}; " +
            $"LAST_END_MARKER={endMarker}; UNKNOWN_BYTES={_board.LastFrameUnknownBytes}; " +
            $"FRAMES_RECEIVED={_board.FramesReceived}.";
    }

    private async Task<bool> WaitForNextProductionFrameAsync(
        long baselineFrameCount,
        int timeoutMs,
        CancellationToken ct)
    {
        if (_board.CompleteFramesReceived > baselineFrameCount)
            return true;

        var frameArrived = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrameReceived(object? sender, ScanFrame frame)
        {
            if (frame.Mode == BoardScanMode.Production &&
                frame.Complete &&
                frame.UnknownBytes == 0 &&
                frame.TerminatorKnown &&
                _board.CompleteFramesReceived > baselineFrameCount)
            {
                frameArrived.TrySetResult(true);
            }
        }

        _board.FrameReceived += OnFrameReceived;
        try
        {
            // Recheck after subscribing so a frame arriving across the
            // subscription boundary cannot be lost.
            if (_board.CompleteFramesReceived > baselineFrameCount)
                return true;

            using CancellationTokenRegistration registration = ct.Register(
                () => frameArrived.TrySetCanceled(ct));
            Task timeout = Task.Delay(timeoutMs, CancellationToken.None);
            Task completed = await Task.WhenAny(frameArrived.Task, timeout);
            ct.ThrowIfCancellationRequested();
            return completed == frameArrived.Task && await frameArrived.Task;
        }
        finally
        {
            _board.FrameReceived -= OnFrameReceived;
        }
    }
}
