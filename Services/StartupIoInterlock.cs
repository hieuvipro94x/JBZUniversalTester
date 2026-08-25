using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public readonly record struct StartupIoContactPair(int FirstIo, int SecondIo);

/// <summary>
/// Detects electrical contacts that already exist before a production/master
/// cycle is armed. This safety interlock never creates a product FAIL.
/// </summary>
public static class StartupIoInterlock
{
    public static IReadOnlyList<StartupIoContactPair> FindConnectedPairs(ScanFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Mode != BoardScanMode.Production || !frame.Complete || frame.UnknownBytes > 0)
            return Array.Empty<StartupIoContactPair>();

        return frame.Connections
            .SelectMany(source => source.Value.Select(target => Normalize(source.Key, target)))
            .Where(pair => pair.FirstIo > 0 && pair.SecondIo > 0 && pair.FirstIo != pair.SecondIo)
            .Distinct()
            .OrderBy(pair => pair.FirstIo)
            .ThenBy(pair => pair.SecondIo)
            .ToArray();
    }

    private static StartupIoContactPair Normalize(int sourceIo, int targetIo) =>
        sourceIo <= targetIo
            ? new StartupIoContactPair(sourceIo, targetIo)
            : new StartupIoContactPair(targetIo, sourceIo);
}
