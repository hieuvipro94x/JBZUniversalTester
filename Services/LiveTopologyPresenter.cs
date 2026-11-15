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

        // Htdrv gốc hiển thị mỗi quan hệ điện thành MỘT CẶP 2 dòng:
        // dòng đầu là IO thứ nhất, dòng sau là IO thứ hai. Mỗi cặp mới có
        // IsNetworkStart=true ở dòng đầu để DataGrid vẽ đường phân cách cyan.
        // Đây chỉ là presentation: FaultKind.Info + ProductFaultType.None đảm bảo
        // LiveTopology không bị tính là OPEN/SAI DÂY/CHẬP MẠCH hay PASS/FAIL.
        // One block represents one connected component, with one IO per row.
        // For example, IO3-IO4 and IO3-IO5 render as IO3, IO4, IO5 without
        // repeating IO3 in separate pair blocks. This is presentation-only.
        FaultRow[] rows = components
            .SelectMany((component, componentIndex) =>
                BuildComponentRows(component.ToArray(), componentIndex))
            .ToArray();

        string signature = string.Join('|', orderedPairs.Select(pair => $"{pair.FirstIo}-{pair.SecondIo}"));

        return new LiveTopologySnapshot(
            frame.Sequence,
            frame.ScanGeneration,
            orderedPairs,
            components,
            rows,
            signature);
    }

    private static IEnumerable<FaultRow> BuildComponentRows(
        int[] related,
        int componentIndex)
    {
        for (int rowIndex = 0; rowIndex < related.Length; rowIndex++)
        {
            int io = related[rowIndex];
            int zeroBased = io - 1;
            int connector = (zeroBased / BoardCapacity.IoPerPort) + 1;
            int pin = (zeroBased % BoardCapacity.IoPerPort) + 1;

            yield return new FaultRow
            {
                Kind = FaultKind.Info,
                ProductFaultType = ProductFaultType.None,
                FaultType = "CHẬP MẠCH",
                Io = io,
                IoTextOverride = $"IO ({io})",
                ActualSourceIo = related[0],
                ActualTargetIo = related[1],
                RelatedIos = related,
                DisplayOrder = (componentIndex * 1000) + rowIndex,
                IsNetworkStart = rowIndex == 0,
                IoCnPnOverride = $"{io}-{connector}-{pin}",
                Status = related.Length > 2
                    ? $"NỐI CHUNG {related.Length} IO"
                    : $"NỐI VỚI IO({related[1 - rowIndex]})"
            };
        }
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
