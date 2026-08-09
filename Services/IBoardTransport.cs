using JBZUniversalTester.Models;
using System.IO;
namespace JBZUniversalTester.Services;

public interface IBoardTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    bool IsScanning { get; }
    BoardCapacity Capacity { get; }
    event EventHandler<ScanFrame>? FrameReceived;
    event EventHandler<string>? Log;
    Task<BoardConnectionInfo> ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task HandshakeAsync(CancellationToken ct = default);
    Task ResetClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Cấu hình dải I/O cần quét theo Settings + model hiện tại. Toàn bộ
    /// Transport/Decoder/TestView lấy cùng BoardCapacity, không tự tính card.
    /// </summary>
    void ConfigureScanRange(int maxIo);

    Task StartScanAsync(BoardScanMode mode = BoardScanMode.Production, CancellationToken ct = default);
    Task StopScanAsync(CancellationToken ct = default);
    Task EnterIdleAsync(CancellationToken ct = default);
    Task SelectResistanceRouteAsync(ResistanceStep step, CancellationToken ct = default);
    Task ReleaseResistanceRouteAsync(CancellationToken ct = default);
    Task SetRelayAsync(int relay, CancellationToken ct = default);
    Task AllRelaysOffAsync(CancellationToken ct = default);
}
