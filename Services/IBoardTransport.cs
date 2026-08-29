using JBZUniversalTester.Models;
using System.IO;
namespace JBZUniversalTester.Services;

public enum BoardConnectionState
{
    Disconnected,
    Connecting,
    Initializing,
    Ready,
    Scanning,
    PausedForHardwareOperation,
    Recovering,
    Faulted,
    ShuttingDown
}

public interface IBoardTransport : IAsyncDisposable
{
    BoardConnectionState ConnectionState => IsScanning
        ? BoardConnectionState.Scanning
        : IsConnected ? BoardConnectionState.Ready : BoardConnectionState.Disconnected;
    bool IsConnected { get; }
    bool IsScanning { get; }
    BoardScanMode CurrentScanMode { get; }
    BoardCapacity InstalledCapacity { get; }
    BoardCapacity Capacity { get; }
    BoardCapacity? AppliedScanCapacity { get; }
    BoardScanCapacity ScanCapacity { get; }
    DateTime LastFrameTimestampUtc { get; }
    long LastFrameSequence { get; }
    long LastCompleteFrameSequence { get; }
    long FramesReceived { get; }
    long CompleteFramesReceived { get; }
    int LastFrameSourceCount { get; }
    byte? LastFrameEndMarkerCode { get; }
    int LastFrameUnknownBytes { get; }
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
    void ConfigureActiveScanRange(int maxIo);

    Task StartScanAsync(BoardScanMode mode = BoardScanMode.Production, CancellationToken ct = default);
    Task StopScanAsync(CancellationToken ct = default);
    Task EnterIdleAsync(CancellationToken ct = default);
    Task SelectResistanceRouteAsync(ResistanceStep step, CancellationToken ct = default);
    Task ReleaseResistanceRouteAsync(CancellationToken ct = default);
    Task SetRelayAsync(int relay, CancellationToken ct = default);
    Task AllRelaysOffAsync(CancellationToken ct = default);
}
