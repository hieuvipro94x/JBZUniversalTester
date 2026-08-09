using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// Lưu sản lượng riêng cho từng mã hàng. Dữ liệu tồn tại qua lần đổi model,
/// đóng/mở phần mềm và không phụ thuộc vào trạng thái DataGrid hiện tại.
/// </summary>
public sealed class ProductionStatisticsStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, ModelProductionStatistics> _items;

    public ProductionStatisticsStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppContext.BaseDirectory, "production.statistics.json")
            : path;

        _items = LoadFile();
    }

    public string StoragePath => _path;

    public ModelProductionStatistics Get(ProductModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        lock (_gate)
        {
            string key = BuildModelKey(model);
            if (_items.TryGetValue(key, out ModelProductionStatistics? saved))
                return saved.Clone();

            return new ModelProductionStatistics
            {
                ModelKey = key,
                ModelName = model.ModelName ?? string.Empty,
                PartNumber = model.PartNumber ?? string.Empty,
                SourceFile = model.SourcePath ?? string.Empty
            };
        }
    }

    public ModelProductionStatistics Record(
        ProductModel model,
        bool passed,
        long lotNo,
        string resultText)
    {
        ArgumentNullException.ThrowIfNull(model);

        lock (_gate)
        {
            string key = BuildModelKey(model);
            if (!_items.TryGetValue(key, out ModelProductionStatistics? item))
            {
                item = new ModelProductionStatistics
                {
                    ModelKey = key
                };
                _items[key] = item;
            }

            item.ModelName = model.ModelName ?? string.Empty;
            item.PartNumber = model.PartNumber ?? string.Empty;
            item.SourceFile = model.SourcePath ?? string.Empty;
            item.Total++;
            if (passed)
                item.Pass++;
            else
                item.Fail++;

            item.LastLotNo = lotNo;
            item.LastResult = resultText ?? (passed ? "PASS" : "FAIL");
            item.LastTestedAt = DateTime.Now;

            SaveFile();
            return item.Clone();
        }
    }

    public static string BuildModelKey(ProductModel model)
    {
        string part = Normalize(model.PartNumber);
        string name = Normalize(model.ModelName);

        if (!string.IsNullOrWhiteSpace(part))
            return $"PN:{part}|MODEL:{name}";

        if (!string.IsNullOrWhiteSpace(name))
            return $"MODEL:{name}";

        string source = model.SourcePath ?? string.Empty;
        string file = string.IsNullOrWhiteSpace(source)
            ? "UNKNOWN"
            : Normalize(Path.GetFileNameWithoutExtension(source));

        return $"FILE:{file}";
    }

    private Dictionary<string, ModelProductionStatistics> LoadFile()
    {
        try
        {
            if (!File.Exists(_path))
                return new Dictionary<string, ModelProductionStatistics>(StringComparer.OrdinalIgnoreCase);

            StatisticsFile? file = JsonSerializer.Deserialize<StatisticsFile>(
                File.ReadAllText(_path, Encoding.UTF8));

            return (file?.Models ?? new List<ModelProductionStatistics>())
                .Where(x => !string.IsNullOrWhiteSpace(x.ModelKey))
                .GroupBy(x => x.ModelKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Last(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // File thống kê hỏng không được phép làm app production crash.
            return new Dictionary<string, ModelProductionStatistics>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveFile()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new StatisticsFile
        {
            Version = 1,
            Models = _items.Values
                .OrderBy(x => x.PartNumber, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ModelName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Clone())
                .ToList()
        };

        string json = JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions { WriteIndented = true });

        string temp = _path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, _path, overwrite: true);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();

    private sealed class StatisticsFile
    {
        public int Version { get; set; } = 1;
        public List<ModelProductionStatistics> Models { get; set; } = new();
    }
}

public sealed class ModelProductionStatistics
{
    public string ModelKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Pass { get; set; }
    public int Fail { get; set; }
    public long LastLotNo { get; set; }
    public string LastResult { get; set; } = string.Empty;
    public DateTime? LastTestedAt { get; set; }

    public double Rate => Total <= 0 ? 0 : 100.0 * Pass / Total;

    public ModelProductionStatistics Clone() => new()
    {
        ModelKey = ModelKey,
        ModelName = ModelName,
        PartNumber = PartNumber,
        SourceFile = SourceFile,
        Total = Total,
        Pass = Pass,
        Fail = Fail,
        LastLotNo = LastLotNo,
        LastResult = LastResult,
        LastTestedAt = LastTestedAt
    };
}
