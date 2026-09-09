using System.IO;
using System.Text.Json;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public static class TopologyLearningService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static IReadOnlyList<int> FindProbeContactIo(
        ScanFrame frame,
        BoardCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(capacity);

        if (frame.Mode != BoardScanMode.Production || !frame.Complete || frame.UnknownBytes != 0)
            return [];

        int[] activeIo = frame.ActiveIo
            .Where(capacity.ContainsGlobalIo)
            .Distinct()
            .ToArray();

        // Một đầu dò chỉ xác định một IO. Từ hai IO active trở lên là một
        // quan hệ continuity cần đưa sang bảng kết nối, không phải hai đầu dò.
        if (activeIo.Length != 1)
            return [];

        return ProbeContactClassifier
            .DetectMany(frame, model: null, maxContacts: 1, boardCapacity: capacity)
            .Select(detection => detection.Io)
            .Where(io => io == activeIo[0])
            .Distinct()
            .OrderBy(io => io)
            .ToArray();
    }

    /// <summary>
    /// Kết quả quan sát vật lý dùng chung với cửa sổ QUÉT/HỌC MÃ:
    /// một active IO là tác động trực tiếp; từ hai active IO trở lên lấy đúng
    /// các thành phần continuity mà BuildSnapshot đang hiển thị.
    /// Chỉ mở nhánh continuity khi frame có chữ ký fan-in Probe mạnh, nên một
    /// cạnh sản phẩm/wrong wiring thông thường không bị lấy khỏi TestEngine.
    /// </summary>
    public static IReadOnlyList<int> FindProbeObservationIo(
        ScanFrame frame,
        BoardCapacity capacity)
    {
        IReadOnlyList<int> direct = FindProbeContactIo(frame, capacity);
        if (direct.Count > 0)
            return direct;

        if (frame.ActiveIo.Count < 2 ||
            ProbeContactClassifier.DetectMany(
                frame,
                model: null,
                maxContacts: Math.Max(2, frame.ActiveIo.Count),
                boardCapacity: capacity).Count == 0)
        {
            return [];
        }

        return BuildSnapshot(frame, capacity).Networks
            .SelectMany(network => network.Ios)
            .Where(capacity.ContainsGlobalIo)
            .Distinct()
            .OrderBy(io => io)
            .ToArray();
    }

    public static LearnedTopologySnapshot BuildSnapshot(ScanFrame frame, BoardCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(capacity);

        if (frame.Mode != BoardScanMode.Production || !frame.Complete || frame.UnknownBytes != 0)
            return new LearnedTopologySnapshot(string.Empty, [], []);

        var parent = new Dictionary<int, int>();
        HashSet<int> activeIo = frame.ActiveIo
            .Where(capacity.ContainsGlobalIo)
            .Distinct()
            .ToHashSet();
        HashSet<int> probeContactIo = FindProbeContactIo(frame, capacity).ToHashSet();
        activeIo.ExceptWith(probeContactIo);

        int Find(int value)
        {
            if (!parent.TryGetValue(value, out int root))
            {
                parent[value] = value;
                return value;
            }

            while (root != parent[root])
                root = parent[root];

            int current = value;
            while (parent[current] != root)
            {
                int next = parent[current];
                parent[current] = root;
                current = next;
            }

            return root;
        }

        void Union(int first, int second)
        {
            int firstRoot = Find(first);
            int secondRoot = Find(second);
            if (firstRoot != secondRoot)
                parent[Math.Max(firstRoot, secondRoot)] = Math.Min(firstRoot, secondRoot);
        }

        foreach ((int source, IReadOnlySet<int> targets) in frame.Connections)
        {
            // Chỉ các IO thực tế active mới được phép tạo topology. Những
            // source quét trung gian không được kéo cả dải card vào một mạng.
            if (!activeIo.Contains(source))
                continue;

            foreach (int target in targets)
            {
                if (source != target &&
                    activeIo.Contains(target))
                    Union(source, target);
            }
        }

        // Trace thực tế có frame hai đầu mút active nhưng firmware không luôn
        // phát cạnh trực tiếp giữa chúng. Với đúng hai IO, chính hai đầu mút là
        // cặp continuity duy nhất có thể kết luận mà không suy diễn thêm.
        if (parent.Count == 0 && activeIo.Count == 2)
        {
            int[] pair = activeIo.OrderBy(io => io).ToArray();
            Union(pair[0], pair[1]);
        }

        int[][] components = parent.Keys
            .GroupBy(Find)
            .Select(group => group.Distinct().OrderBy(io => io).ToArray())
            .Where(group => group.Length >= 2)
            .OrderBy(group => group[0])
            .ThenBy(group => group.Length)
            .ToArray();

        LearnedTopologyNetwork[] networks = components
            .Select((ios, index) => new LearnedTopologyNetwork
            {
                Name = $"AUTO-{index + 1:000}",
                Ios = ios.ToList()
            })
            .ToArray();
        LearnedTopologyRow[] rows = components
            .Select((ios, index) => new LearnedTopologyRow(
                index + 1,
                string.Join(" ↔ ", ios.Select(io => $"IO({io})"))))
            .ToArray();
        string signature = string.Join('|', components.Select(group => string.Join('-', group)));
        return new LearnedTopologySnapshot(signature, networks, rows);
    }

    public static async Task SaveAsync(
        string path,
        LearnedTopologyProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(profile);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Không xác định được thư mục lưu cấu hình học.");

        Directory.CreateDirectory(directory);
        string temporaryPath = fullPath + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(profile, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
