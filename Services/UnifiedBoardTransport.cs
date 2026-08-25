using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// D2XX-only board transport. Auto is kept as a config value, but it resolves to
/// the FTDI D2XX board so this application stays separated from other board families.
/// </summary>
public sealed class UnifiedBoardTransport : IBoardTransport
{
    readonly ProductionSettings _production;
    readonly D2xxBoardTransport _d2xx;
    int _disposed;

    public UnifiedBoardTransport(string ftdiSerial, ProductionSettings production)
    {
        _production = production;
        _d2xx = new D2xxBoardTransport(ftdiSerial, production);
        _d2xx.FrameReceived += (_, e) => FrameReceived?.Invoke(this, e);
        _d2xx.Log += (_, text) => Log?.Invoke(this, text);
    }

    public bool IsConnected => _d2xx.IsConnected;
    public bool IsScanning => _d2xx.IsScanning;
    public BoardScanMode CurrentScanMode => _d2xx.CurrentScanMode;
    public BoardCapacity Capacity => _d2xx.Capacity;
    public DateTime LastFrameTimestampUtc => _d2xx.LastFrameTimestampUtc;
    public long LastFrameSequence => _d2xx.LastFrameSequence;
    public long FramesReceived => _d2xx.FramesReceived;
    public BoardMode ActiveMode { get; private set; } = BoardMode.Auto;

    public event EventHandler<ScanFrame>? FrameReceived;
    public event EventHandler<string>? Log;

    public async Task<BoardConnectionInfo> ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (IsConnected)
            return new BoardConnectionInfo(BoardModeCatalog.DisplayName(ActiveMode), string.Empty);

        try
        {
            BoardConnectionInfo info = await _d2xx.ConnectAsync(ct);
            ActiveMode = BoardMode.D2xx;
            ConfigureScanRange(0);
            Log?.Invoke(this, "BOARD SELECTED: JBZ D2XX");
            return info with { Description = $"JBZ D2XX - {info.Description}" };
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            try { await _d2xx.DisconnectAsync(); } catch { }
            throw new InvalidOperationException(
                "Không kết nối được bo JBZ D2XX. Kiểm tra cáp USB/driver FTDI. " + ex.Message,
                ex);
        }
    }

    public async Task DisconnectAsync()
    {
        ActiveMode = BoardMode.Auto;
        await _d2xx.DisconnectAsync();
    }

    public Task HandshakeAsync(CancellationToken ct = default) => _d2xx.HandshakeAsync(ct);
    public Task ResetClearAsync(CancellationToken ct = default) => _d2xx.ResetClearAsync(ct);
    public void ConfigureScanRange(int maxIo) => _d2xx.ConfigureScanRange(maxIo);
    public Task StartScanAsync(BoardScanMode mode = BoardScanMode.Production, CancellationToken ct = default) => _d2xx.StartScanAsync(mode, ct);
    public Task StopScanAsync(CancellationToken ct = default) => _d2xx.StopScanAsync(ct);
    public Task EnterIdleAsync(CancellationToken ct = default) => _d2xx.EnterIdleAsync(ct);
    public Task SelectResistanceRouteAsync(ResistanceStep step, CancellationToken ct = default) => _d2xx.SelectResistanceRouteAsync(step, ct);
    public Task ReleaseResistanceRouteAsync(CancellationToken ct = default) => _d2xx.ReleaseResistanceRouteAsync(ct);
    public Task SetRelayAsync(int relay, CancellationToken ct = default) => _d2xx.SetRelayAsync(relay, ct);
    public Task AllRelaysOffAsync(CancellationToken ct = default) => _d2xx.AllRelaysOffAsync(ct);

    void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(UnifiedBoardTransport));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try { await DisconnectAsync(); } catch { }
        await _d2xx.DisposeAsync();
    }
}
