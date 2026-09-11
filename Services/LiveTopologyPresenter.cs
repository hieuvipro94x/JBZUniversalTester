using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public readonly record struct LiveTopologyPair(int FirstIo, int SecondIo);

public sealed record LiveTopologySnapshot(
    long FrameSequence,
    long ScanGeneration,
    IReadOnlyList<LiveTopologyPair> Pairs,
    IReadOnlyList<IReadOnlyList<int>> Components,
    IReadOnlyList<FaultRow> Rows,
    string Signature)
{
    public static LiveTopologySnapshot Empty(long frameSequence = 0, long scanGeneration = 0) =>
        new(frameSequence, scanGeneration, [], [], [], string.Empty);
}

/// <summary>
/// Builds display-only topology from the actual SOURCE/TARGET relations decoded
/// from one complete board frame. It never infers pairs from an active-I/O list.
/// </summary>
public static class LiveTopologyPresenter
{
    public static LiveTopologySnapshot Build(
        ScanFrame frame,
        BoardCapacity capacity,
        IReadOnlyCollection<int>? excludedIo = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(capacity);

        if (frame.Mode != BoardScanMode.Production ||
            !frame.Complete ||
            frame.UnknownBytes != 0 ||
            !frame.TerminatorKnown)
        {
            return LiveTopologySnapshot.Empty(frame.Sequence, frame.ScanGeneration);
        }

        HashSet<int> excluded = excludedIo?.ToHashSet() ?? [];
        var pairs = new HashSet<LiveTopologyPair>();
        foreach ((int source, IReadOnlySet<int> targets) in frame.Connections)
        {
            foreach (int target in targets)
            {
                if (source == target ||
                    !capacity.ContainsGlobalIo(source) ||
                    !capacity.ContainsGlobalIo(target) ||
                    excluded.Contains(source) ||
                    excluded.Contains(target))
                {
                    continue;
                }

                pairs.Add(source < target
                    ? new LiveTopologyPair(source, target)
                    : new LiveTopologyPair(target, source));
            }
        }

        LiveTopologyPair[] orderedPairs = pairs
            .OrderBy(pair => pair.FirstIo)
            .ThenBy(pair => pair.SecondIo)
            .ToArray();
        IReadOnlyList<IReadOnlyList<int>> components = BuildComponents(orderedPairs);
        FaultRow[] rows = orderedPairs.Select(pair => new FaultRow
        {
            Kind = FaultKind.Info,
            FaultType = "THÔNG MẠCH",
            Io = pair.FirstIo,
            ActualSourceIo = pair.FirstIo,
            ActualTargetIo = pair.SecondIo,
            RelatedIos = [pair.FirstIo, pair.SecondIo],
            WireName = $"IO({pair.FirstIo}) ↔ IO({pair.SecondIo})",
            DisplayOrder = pair.FirstIo * 10_000 + pair.SecondIo,
            Status = $"ĐANG KẾT NỐI: IO({pair.FirstIo}) ↔ IO({pair.SecondIo})"
        }).ToArray();
        string signature = string.Join('|', orderedPairs.Select(pair => $"{pair.FirstIo}-{pair.SecondIo}"));

        return new LiveTopologySnapshot(
            frame.Sequence,
            frame.ScanGeneration,
            orderedPairs,
            components,
            rows,
            signature);
    }

    private static IReadOnlyList<IReadOnlyList<int>> BuildComponents(
        IReadOnlyList<LiveTopologyPair> pairs)
    {
        var adjacency = new Dictionary<int, HashSet<int>>();
        foreach (LiveTopologyPair pair in pairs)
        {
            if (!adjacency.TryGetValue(pair.FirstIo, out HashSet<int>? first))
                adjacency[pair.FirstIo] = first = [];
            if (!adjacency.TryGetValue(pair.SecondIo, out HashSet<int>? second))
                adjacency[pair.SecondIo] = second = [];
            first.Add(pair.SecondIo);
            second.Add(pair.FirstIo);
        }

        var visited = new HashSet<int>();
        var components = new List<IReadOnlyList<int>>();
        foreach (int start in adjacency.Keys.OrderBy(io => io))
        {
            if (!visited.Add(start))
                continue;

            var pending = new Stack<int>();
            var component = new List<int>();
            pending.Push(start);
            while (pending.Count > 0)
            {
                int current = pending.Pop();
                component.Add(current);
                foreach (int next in adjacency[current].OrderByDescending(io => io))
                {
                    if (visited.Add(next))
                        pending.Push(next);
                }
            }

            component.Sort();
            components.Add(component);
        }

        return components;
    }
}
