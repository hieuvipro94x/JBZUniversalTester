using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed class ScanSupervisor
{
    private const int MinimumFirstFrameTimeoutMs = 1_500;
    private const int FirstFrameTimeoutPerExpansionModuleMs = 1_500;
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

        if (_board.IsScanning && _board.CurrentScanMode == BoardScanMode.Production)
            return false;

        _board.ConfigureScanRange(maxIo);
        await _board.StartScanAsync(BoardScanMode.Production, ct);
        return true;
    }

    public async Task StartProductionScanAndVerifyFrameAsync(
        int maxIo,
        CancellationToken ct,
        string reason)
    {
        ct.ThrowIfCancellationRequested();

        BoardCapacity previousCapacity = _board.Capacity;
        _board.ConfigureScanRange(maxIo);
        BoardCapacity configuredCapacity = _board.Capacity;
        int firstFrameTimeoutMs = ResolveFirstFrameTimeoutMs(configuredCapacity);

        bool capacityChanged =
            previousCapacity.ExpansionModuleCount != configuredCapacity.ExpansionModuleCount ||
            previousCapacity.StartCardNumber != configuredCapacity.StartCardNumber ||
            previousCapacity.TotalIoCapacity != configuredCapacity.TotalIoCapacity;

        // App đã chạy Production scan nền ngay sau khi kết nối. Khi người dùng
        // mở TestView với cùng capacity, không STOP/START stream khỏe chỉ để
        // "xác nhận" lại. Việc STOP/START ở đây trước đây tạo thêm chuyển trạng
        // thái D2XX và làm startup/TestView có cảm giác đứng.
        if (!capacityChanged &&
            _board.IsScanning &&
            _board.CurrentScanMode == BoardScanMode.Production)
        {
            long healthyBaseline = _board.FramesReceived;
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

        long baselineFrameCount = _board.FramesReceived;
        await _board.StartScanAsync(BoardScanMode.Production, ct);

        if (await WaitForNextProductionFrameAsync(baselineFrameCount, firstFrameTimeoutMs, ct))
        {
            _log($"START_SCAN OK sau {reason}: đã nhận frame production mới.");
            return;
        }

        _log($"START_SCAN sau {reason} chưa có frame đầu - tự recovery STOP/START một lần.");
        await _board.StopScanAsync(CancellationToken.None);

        baselineFrameCount = _board.FramesReceived;
        _board.ConfigureScanRange(maxIo);
        await _board.StartScanAsync(BoardScanMode.Production, ct);

        if (await WaitForNextProductionFrameAsync(baselineFrameCount, firstFrameTimeoutMs, ct))
        {
            _log($"Recovery scan OK sau {reason}: stream production đã trở lại.");
            return;
        }

        throw new InvalidOperationException(
            $"START_SCAN sau {reason} không trả frame production trong thời gian watchdog.");
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

            long baselineFrameCount = _board.FramesReceived;
            await _board.StopScanAsync(CancellationToken.None);
            _board.ConfigureScanRange(maxIo);
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

            baselineFrameCount = _board.FramesReceived;
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
            checked(capacity.ExpansionModuleCount * FirstFrameTimeoutPerExpansionModuleMs));
    }

    public static int ResolveProductionStallTimeoutMs(BoardCapacity capacity) =>
        checked(ResolveFirstFrameTimeoutMs(capacity) + StallTimeoutMarginMs);

    private async Task<bool> WaitForNextProductionFrameAsync(
        long baselineFrameCount,
        int timeoutMs,
        CancellationToken ct)
    {
        if (_board.FramesReceived > baselineFrameCount)
            return true;

        var frameArrived = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnFrameReceived(object? sender, ScanFrame frame)
        {
            if (frame.Mode == BoardScanMode.Production &&
                _board.FramesReceived > baselineFrameCount)
            {
                frameArrived.TrySetResult(true);
            }
        }

        _board.FrameReceived += OnFrameReceived;
        try
        {
            // Recheck after subscribing so a frame arriving across the
            // subscription boundary cannot be lost.
            if (_board.FramesReceived > baselineFrameCount)
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
