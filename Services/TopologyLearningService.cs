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

    public static LearnedTopologySnapshot BuildSnapshot(ScanFrame frame, BoardCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(capacity);

        if (frame.Mode != BoardScanMode.Production || !frame.Complete || frame.UnknownBytes != 0)
            return new LearnedTopologySnapshot(string.Empty, [], []);

        var parent = new Dictionary<int, int>();

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
            if (!capacity.ContainsGlobalIo(source))
                continue;

            foreach (int target in targets)
            {
                if (source != target && capacity.ContainsGlobalIo(target))
                    Union(source, target);
            }
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
