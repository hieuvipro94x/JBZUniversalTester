using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// One Windows host, two JBZ board families. AUTO prefers D2XX when a valid
/// FT245R board is present, otherwise falls back to the proven UART-TTL firmware.
/// </summary>
public sealed class UnifiedBoardTransport : IBoardTransport, IFirmwareProtocolBoard
{
    readonly ProductionSettings _production;
    readonly D2xxBoardTransport _d2xx;
    readonly UartTtlBoardTransport _uart;
    IBoardTransport? _active;
    IFirmwareProtocolBoard? _activeFirmware;
    int _disposed;

    public UnifiedBoardTransport(string ftdiSerial, ProductionSettings production)
    {
        _production = production;
        _d2xx = new D2xxBoardTransport(ftdiSerial, production);
        _uart = new UartTtlBoardTransport(production);
        Wire(_d2xx);
        Wire(_uart);
        _uart.ProtocolEventReceived += (_, e) => ProtocolEventReceived?.Invoke(this, e);
    }

    public bool IsConnected => _active?.IsConnected == true;
    public bool IsScanning => _active?.IsScanning == true;
    public BoardCapacity Capacity => _active?.Capacity ?? BoardCapacity.FromSettings(_production);
    public bool UsesFirmwareCycleResult => _activeFirmware?.UsesFirmwareCycleResult == true;
    public string ActivePort => _activeFirmware?.ActivePort ?? string.Empty;
    public BoardMode ActiveMode { get; private set; } = BoardMode.Auto;

    public event EventHandler<ScanFrame>? FrameReceived;
    public event EventHandler<string>? Log;
    public event EventHandler<BoardProtocolEvent>? ProtocolEventReceived;

    void Wire(IBoardTransport transport)
    {
        transport.FrameReceived += (_, e) =>
        {
            if (ReferenceEquals(_active, transport))
                FrameReceived?.Invoke(this, e);
        };
        transport.Log += (_, text) =>
        {
            if (ReferenceEquals(_active, transport) || _active is null)
                Log?.Invoke(this, text);
        };
    }

    public async Task<BoardConnectionInfo> ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        if (IsConnected)
            return new BoardConnectionInfo(BoardModeCatalog.DisplayName(ActiveMode), ActivePort);

        BoardMode requested = _production.BoardMode;
        List<(BoardMode Mode, IBoardTransport Transport)> order = requested switch
        {
            BoardMode.D2xx => [(BoardMode.D2xx, _d2xx)],
            BoardMode.UartTtl => [(BoardMode.UartTtl, _uart)],
            _ => [(BoardMode.D2xx, _d2xx), (BoardMode.UartTtl, _uart)]
        };

        var errors = new List<string>();
        foreach ((BoardMode mode, IBoardTransport transport) in order)
        {
            try
            {
                BoardConnectionInfo info = await transport.ConnectAsync(ct);
                _active = transport;
                _activeFirmware = transport as IFirmwareProtocolBoard;
                ActiveMode = mode;
                ConfigureScanRange(0);
                Log?.Invoke(this, $"BOARD SELECTED: {BoardModeCatalog.DisplayName(mode)}");
                return info with
                {
                    Description = $"{BoardModeCatalog.DisplayName(mode)} - {info.Description}"
                };
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                errors.Add($"{BoardModeCatalog.DisplayName(mode)}: {ex.Message}");
                try { await transport.DisconnectAsync(); } catch { }
            }
        }

        throw new InvalidOperationException(
            "Không kết nối được bo theo cấu hình hiện tại. " + string.Join(" | ", errors));
    }

    public async Task DisconnectAsync()
    {
        IBoardTransport? active = _active;
        _active = null;
        _activeFirmware = null;
        ActiveMode = BoardMode.Auto;
        if (active is not null)
            await active.DisconnectAsync();
    }

    public Task HandshakeAsync(CancellationToken ct = default) => Active().HandshakeAsync(ct);
    public Task ResetClearAsync(CancellationToken ct = default) => Active().ResetClearAsync(ct);

    public void ConfigureScanRange(int maxIo)
    {
        _d2xx.ConfigureScanRange(maxIo);
        _uart.ConfigureScanRange(maxIo);
    }

    public Task StartScanAsync(BoardScanMode mode = BoardScanMode.Production, CancellationToken ct = default) => Active().StartScanAsync(mode, ct);
    public Task StopScanAsync(CancellationToken ct = default) => Active().StopScanAsync(ct);
    public Task EnterIdleAsync(CancellationToken ct = default) => Active().EnterIdleAsync(ct);
    public Task SelectResistanceRouteAsync(ResistanceStep step, CancellationToken ct = default) => Active().SelectResistanceRouteAsync(step, ct);
    public Task ReleaseResistanceRouteAsync(CancellationToken ct = default) => Active().ReleaseResistanceRouteAsync(ct);
    public Task SetRelayAsync(int relay, CancellationToken ct = default) => Active().SetRelayAsync(relay, ct);
    public Task AllRelaysOffAsync(CancellationToken ct = default) => Active().AllRelaysOffAsync(ct);

    public Task<string> QueryModelNameAsync(CancellationToken ct = default) => Firmware().QueryModelNameAsync(ct);
    public Task UploadModelProfileAsync(UartModelProfile profile, CancellationToken ct = default) => Firmware().UploadModelProfileAsync(profile, ct);

    public Task StartFirmwareCycleAsync(int maxExt = 0, CancellationToken ct = default) =>
        Firmware().StartFirmwareCycleAsync(maxExt, ct);
    public Task SendPassPenAsync(int delayMs, int pinCount, CancellationToken ct = default) =>
        Firmware().SendPassPenAsync(delayMs, pinCount, ct);
    public Task RequestUnconnectAsync(int delayMs, int pinCount, CancellationToken ct = default) =>
        Firmware().RequestUnconnectAsync(delayMs, pinCount, ct);

    IBoardTransport Active() => _active ?? throw new InvalidOperationException("Chưa kết nối bo JBZ.");
    IFirmwareProtocolBoard Firmware() => _activeFirmware ?? throw new NotSupportedException("Bo đang dùng không phải JBZ UART TTL firmware protocol.");

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
        await _uart.DisposeAsync();
    }
}
