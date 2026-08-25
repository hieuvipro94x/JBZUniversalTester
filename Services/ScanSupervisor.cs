using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed class ScanSupervisor
{
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

        _board.ConfigureScanRange(maxIo);
        long baseline = _board.LastFrameSequence;
        await _board.StartScanAsync(BoardScanMode.Production, ct);

        if (await WaitForNextProductionFrameAsync(baseline, 1_200, ct))
        {
            _log($"START_SCAN OK sau {reason}: đã nhận frame production mới.");
            return;
        }

        _log($"START_SCAN sau {reason} chưa có frame đầu - tự recovery STOP/START một lần.");
        await _board.StopScanAsync(CancellationToken.None);

        baseline = _board.LastFrameSequence;
        _board.ConfigureScanRange(maxIo);
        await _board.StartScanAsync(BoardScanMode.Production, ct);

        if (await WaitForNextProductionFrameAsync(baseline, 1_500, ct))
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

            long baseline = _board.LastFrameSequence;
            await _board.StopScanAsync(CancellationToken.None);
            _board.ConfigureScanRange(maxIo);
            await _board.StartScanAsync(BoardScanMode.Production, ct);

            if (await WaitForNextProductionFrameAsync(baseline, 1_500, ct))
            {
                _log($"[SCAN-WATCHDOG] recovery success first-frame seq={_board.LastFrameSequence}.");
                return true;
            }

            _log("[SCAN-WATCHDOG] STOP/START không có frame mới; reconnect D2XX.");
            await _board.DisconnectAsync();
            await reconnectAsync();

            baseline = _board.LastFrameSequence;
            await EnsureProductionScanAsync(maxIo, ct);
            if (await WaitForNextProductionFrameAsync(baseline, 2_000, ct))
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

    private async Task<bool> WaitForNextProductionFrameAsync(
        long baselineSequence,
        int timeoutMs,
        CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();

            if (!_board.IsConnected)
                return false;

            long current = _board.LastFrameSequence;
            if (current > 0 && current != baselineSequence)
                return true;

            await Task.Delay(25, ct);
        }

        return _board.LastFrameSequence != baselineSequence;
    }
}
