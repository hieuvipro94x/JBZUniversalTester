using System.Globalization;
using System.IO;
using System.Text;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed record PartCounterEntry(
    string PartNumber,
    long ReplacementThreshold,
    long Counter);

/// <summary>
/// Compatible store for the original PHT20 PartCnt.txt format:
/// PartNumber ReplacementThreshold Counter
/// </summary>
public sealed class PartCounterStore
{
    public const long DefaultReplacementThreshold = 200_000;

    private readonly object _gate = new();
    private readonly string _path;

    public PartCounterStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppContext.BaseDirectory, "PartCnt.txt")
            : Path.GetFullPath(path);
    }

    public string StoragePath => _path;
    public string LastWarning { get; private set; } = string.Empty;

    public PartCounterEntry GetOrCreate(
        ProductModel model,
        long initialCounter = 0,
        long initialThreshold = DefaultReplacementThreshold)
    {
        ArgumentNullException.ThrowIfNull(model);
        lock (_gate)
        {
            PartCounterFile file = Load();
            string partNumber = ResolvePartNumber(model);
            if (file.Items.TryGetValue(partNumber, out PartCounterEntry? existing))
                return existing;

            var created = new PartCounterEntry(
                partNumber,
                NormalizeThreshold(initialThreshold),
                Math.Max(0, initialCounter));
            file.Items[partNumber] = created;
            Save(file);
            return created;
        }
    }

    public PartCounterEntry Increment(
        ProductModel model,
        long initialCounter = 0,
        long initialThreshold = DefaultReplacementThreshold)
    {
        ArgumentNullException.ThrowIfNull(model);
        lock (_gate)
        {
            PartCounterFile file = Load();
            string partNumber = ResolvePartNumber(model);
            if (!file.Items.TryGetValue(partNumber, out PartCounterEntry? current))
            {
                current = new PartCounterEntry(
                    partNumber,
                    NormalizeThreshold(initialThreshold),
                    Math.Max(0, initialCounter));
            }

            PartCounterEntry updated = current with { Counter = checked(current.Counter + 1) };
            file.Items[partNumber] = updated;
            Save(file);
            return updated;
        }
    }

    public PartCounterEntry Reset(ProductModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        lock (_gate)
        {
            PartCounterFile file = Load();
            string partNumber = ResolvePartNumber(model);
            if (!file.Items.TryGetValue(partNumber, out PartCounterEntry? current))
            {
                current = new PartCounterEntry(
                    partNumber,
                    DefaultReplacementThreshold,
                    0);
            }

            PartCounterEntry reset = current with { Counter = 0 };
            file.Items[partNumber] = reset;
            Save(file);
            return reset;
        }
    }

    private PartCounterFile Load()
    {
        var result = new PartCounterFile();
        LastWarning = string.Empty;
        if (!File.Exists(_path))
            return result;

        int lineNumber = 0;
        foreach (string rawLine in File.ReadAllLines(_path, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            string[] fields = rawLine.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length == 3 &&
                long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long threshold) &&
                long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long counter) &&
                threshold > 0 && counter >= 0)
            {
                string partNumber = NormalizePartNumber(fields[0]);
                result.Items[partNumber] = new PartCounterEntry(partNumber, threshold, counter);
                continue;
            }

            result.UnparsedLines.Add(rawLine);
            LastWarning = $"PartCnt.txt có dòng không hợp lệ tại dòng {lineNumber}; dòng này được giữ nguyên.";
        }

        return result;
    }

    private void Save(PartCounterFile file)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var lines = file.Items.Values
            .OrderBy(item => item.PartNumber, StringComparer.OrdinalIgnoreCase)
            .Select(item => string.Create(
                CultureInfo.InvariantCulture,
                $"{item.PartNumber} {item.ReplacementThreshold} {item.Counter}"))
            .Concat(file.UnparsedLines)
            .ToArray();

        string text = lines.Length == 0
            ? string.Empty
            : string.Join("\r\n", lines) + "\r\n";
        string temp = _path + ".tmp";
        string backup = _path + ".bak";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        if (File.Exists(_path))
            File.Replace(temp, _path, backup, ignoreMetadataErrors: true);
        else
            File.Move(temp, _path);
    }

    private static string ResolvePartNumber(ProductModel model)
    {
        string value = !string.IsNullOrWhiteSpace(model.PartNumber)
            ? model.PartNumber
            : !string.IsNullOrWhiteSpace(model.ModelName)
                ? model.ModelName
                : Path.GetFileNameWithoutExtension(model.SourcePath);
        return NormalizePartNumber(string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value);
    }

    private static string NormalizePartNumber(string value)
    {
        string trimmed = value.Trim();
        var builder = new StringBuilder(trimmed.Length);
        foreach (char character in trimmed)
            builder.Append(char.IsWhiteSpace(character) ? '_' : character);
        return builder.ToString();
    }

    private static long NormalizeThreshold(long threshold) =>
        threshold > 0 ? threshold : DefaultReplacementThreshold;

    private sealed class PartCounterFile
    {
        public Dictionary<string, PartCounterEntry> Items { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> UnparsedLines { get; } = [];
    }
}
