using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// Chuyển frame Production thành bảng quan sát I/O cho THT trống.
/// Không đánh giá topology, không tạo ProductFault và không điều khiển relay.
/// </summary>
public static class IoMappingFramePresenter
{
    public static IReadOnlyList<FaultRow> BuildRows(
        ScanFrame frame,
        BoardCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(capacity);

        if (frame.Mode != BoardScanMode.Production ||
            !frame.Complete ||
            frame.UnknownBytes != 0)
        {
            return [];
        }

        int[] probeIos = ProbeContactClassifier
            .DetectMany(frame, model: null, maxContacts: 2, boardCapacity: capacity)
            .Select(item => item.Io)
            .Where(capacity.ContainsGlobalIo)
            .Distinct()
            .OrderBy(io => io)
            .ToArray();

        var pairs = new HashSet<(int First, int Second)>();
        foreach (KeyValuePair<int, IReadOnlySet<int>> source in frame.Connections)
        {
            foreach (int target in source.Value)
            {
                if (source.Key == target ||
                    !capacity.ContainsGlobalIo(source.Key) ||
                    !capacity.ContainsGlobalIo(target) ||
                    probeIos.Contains(source.Key) ||
                    probeIos.Contains(target))
                {
                    continue;
                }

                pairs.Add(source.Key < target
                    ? (source.Key, target)
                    : (target, source.Key));
            }
        }

        var rows = new List<FaultRow>(probeIos.Length + pairs.Count);
        rows.AddRange(probeIos.Select(io => new FaultRow
        {
            Kind = FaultKind.Probe,
            FaultType = "ĐẦU DÒ",
            Io = io,
            Connector = "CHƯA CÀI CHÂN",
            WireName = $"IO({io})",
            RelatedIos = [io],
            DisplayOrder = io,
            Status = $"ĐẦU DÒ ĐANG CHẠM IO({io})"
        }));

        rows.AddRange(pairs
            .OrderBy(pair => pair.First)
            .ThenBy(pair => pair.Second)
            .Select(pair => new FaultRow
            {
                Kind = FaultKind.Info,
                FaultType = "THÔNG MẠCH",
                Io = pair.First,
                ActualSourceIo = pair.First,
                ActualTargetIo = pair.Second,
                RelatedIos = [pair.First, pair.Second],
                WireName = $"IO({pair.First}) ↔ IO({pair.Second})",
                DisplayOrder = pair.First * 10_000 + pair.Second,
                Status = $"ĐANG KẾT NỐI: IO({pair.First}) ↔ IO({pair.Second})"
            }));

        return rows;
    }
}
