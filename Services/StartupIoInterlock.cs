using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public readonly record struct StartupIoContactPair(int FirstIo, int SecondIo);

/// <summary>
/// Detects electrical contacts that already exist before a production/master
/// cycle is armed. This safety interlock never creates a product FAIL.
/// </summary>
public static class StartupIoInterlock
{
    public static IReadOnlyList<StartupIoContactPair> FindConnectedPairs(
        ScanFrame frame,
        ProductModel? model = null,
        BoardCapacity? capacity = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Mode != BoardScanMode.Production || !frame.Complete || frame.UnknownBytes > 0)
            return Array.Empty<StartupIoContactPair>();

        HashSet<int> probeIo = ProbeContactClassifier
            .DetectMany(frame, model: null, maxContacts: 4, boardCapacity: capacity)
            .Select(item => item.Io)
            .ToHashSet();

        return frame.Connections
            .SelectMany(source => source.Value
                .Where(target => !probeIo.Contains(source.Key) &&
                                 !probeIo.Contains(target) &&
                                 !IsSubstitutedSourceArtifact(frame, model, source.Key, target))
                .Select(target => Normalize(source.Key, target)))
            .Where(pair => pair.FirstIo > 0 && pair.SecondIo > 0 && pair.FirstIo != pair.SecondIo)
            .Distinct()
            .OrderBy(pair => pair.FirstIo)
            .ThenBy(pair => pair.SecondIo)
            .ToArray();
    }

    private static bool IsSubstitutedSourceArtifact(
        ScanFrame frame,
        ProductModel? model,
        int sourceIo,
        int targetIo)
    {
        // On complete sweeps firmware may emit TARGET in place of that IO's
        // SOURCE word. The decoder must retain it for probe detection, but the
        // preceding SOURCE->TARGET relation is not proof of a physical pair.
        bool targetReplacedItsSource =
            frame.ExpectedIoCount > 0 &&
            frame.SourceCount < frame.ExpectedIoCount &&
            frame.ActiveIo.Count == 1 &&
            frame.ActiveIo.Contains(targetIo) &&
            !frame.Connections.ContainsKey(targetIo);
        return targetReplacedItsSource &&
               !IsExpectedModelPair(model, sourceIo, targetIo);
    }

    private static bool IsExpectedModelPair(ProductModel? model, int firstIo, int secondIo)
    {
        if (model is null)
            return false;

        if (model.Nets.Any(net =>
                net.IoNumbers.Contains(firstIo) && net.IoNumbers.Contains(secondIo)))
        {
            return true;
        }

        return model.Clip is not null &&
               ((model.Clip.CommonIo == firstIo &&
                 model.Clip.Branches.Any(branch => branch.TargetIo == secondIo)) ||
                (model.Clip.CommonIo == secondIo &&
                 model.Clip.Branches.Any(branch => branch.TargetIo == firstIo)));
    }

    private static StartupIoContactPair Normalize(int sourceIo, int targetIo) =>
        sourceIo <= targetIo
            ? new StartupIoContactPair(sourceIo, targetIo)
            : new StartupIoContactPair(targetIo, sourceIo);
}
