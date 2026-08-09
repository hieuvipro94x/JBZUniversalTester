using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// High-level ASCII protocol emitted by the Raspberry-Pi firmware family.
/// D2XX boards do not implement this interface; their continuity remains driven
/// by ScanFrame/TestEngine.
/// </summary>
public interface IFirmwareProtocolBoard
{
    bool UsesFirmwareCycleResult { get; }
    string ActivePort { get; }
    event EventHandler<BoardProtocolEvent>? ProtocolEventReceived;

    Task<string> QueryModelNameAsync(CancellationToken ct = default);
    Task UploadModelProfileAsync(UartModelProfile profile, CancellationToken ct = default);

    Task StartFirmwareCycleAsync(int maxExt = 0, CancellationToken ct = default);
    Task SendPassPenAsync(int delayMs, int pinCount, CancellationToken ct = default);
    Task RequestUnconnectAsync(int delayMs, int pinCount, CancellationToken ct = default);
}
