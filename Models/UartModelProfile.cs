using System.IO;
using System.Text.Json;

namespace JBZUniversalTester.Models;

public sealed record UartModelCommand(string Tx, string ExpectMode, string ExpectValue, int TimeoutMs = 2000);

/// <summary>
/// V14 model profile for the Raspberry-Pi firmware family. This is deliberately
/// separate from ProductModel/THT because the firmware owns ARRAY/CON/CONNECTOR
/// semantics and ACK ordering.
/// </summary>
public sealed class UartModelProfile
{
    public string ModelName { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public IReadOnlyList<UartModelCommand> Commands { get; init; } = [];

    public static UartModelProfile Load(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Không tìm thấy UART model/profile.", full);
        string ext = Path.GetExtension(full).ToLowerInvariant();
        return ext == ".json" ? LoadJson(full) : LoadTranscript(full);
    }

    static UartModelProfile LoadJson(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("commands", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("UART profile JSON thiếu commands[].");
        var commands = new List<UartModelCommand>();
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.String)
            {
                string tx = Normalize(row.GetString() ?? "");
                commands.Add(CreateDefault(tx));
                continue;
            }
            string tx2 = Normalize(row.GetProperty("tx").GetString() ?? "");
            if (row.TryGetProperty("expect", out JsonElement expect))
            {
                if (expect.ValueKind == JsonValueKind.String)
                    commands.Add(new(tx2, "exact", expect.GetString() ?? "", 2000));
                else
                    commands.Add(new(tx2,
                        expect.TryGetProperty("mode", out var mode) ? mode.GetString() ?? "exact" : "exact",
                        expect.GetProperty("value").GetString() ?? "",
                        expect.TryGetProperty("timeout", out var timeout) ? (int)Math.Round(timeout.GetDouble() * 1000) : 2000));
            }
            else commands.Add(CreateDefault(tx2));
        }
        ValidateCommands(commands);
        string modelName = root.TryGetProperty("model_name", out var mn) ? mn.GetString() ?? "" : ExtractModelName(commands);
        return new() { ModelName = modelName, SourcePath = path, Commands = commands };
    }

    static UartModelProfile LoadTranscript(string path)
    {
        var commands = new List<UartModelCommand>();
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith("TX ", StringComparison.OrdinalIgnoreCase)) line = line[3..].Trim();
            if (!line.StartsWith(':')) continue;
            string family = Family(line);
            if (KnownFamilies.Contains(family)) commands.Add(CreateDefault(Normalize(line)));
        }
        if (commands.Count == 0) throw new InvalidDataException("Không tìm thấy command model UART trong profile/transcript.");
        ValidateCommands(commands);
        return new() { ModelName = ExtractModelName(commands), SourcePath = path, Commands = commands };
    }

    static readonly HashSet<string> KnownFamilies = new(StringComparer.OrdinalIgnoreCase)
    { "MODEL", "PINCOUNT", "PINDATA", "ARRAYCOUNT", "ARRAY", "CONCOUNT", "CON", "CONNECTORCOUNT", "CONNECTOR", "FINISH" };

    public static UartModelCommand CreateDefault(string tx)
    {
        string family = Family(tx);
        string[] parts = tx.Split(',');
        return family switch
        {
            "MODEL" => new(tx, "exact", ":OK,MODEL", 3000),
            "PINCOUNT" => new(tx, "exact", ":OK,PINCOUNT"),
            "PINDATA" or "ARRAY" or "CON" or "CONNECTOR" => new(tx, "exact", $":OK,{family},{parts.ElementAtOrDefault(1)}"),
            "ARRAYCOUNT" => new(tx, "exact", ":OK,ARRAYCOUNT"),
            "CONCOUNT" => new(tx, "exact", ":OK,CONCOUNT"),
            "CONNECTORCOUNT" => new(tx, "exact", ":OK,CONNECTORCOUNT"),
            "FINISH" => new(tx, "prefix", ":OK,FINISH,", 4000),
            _ => throw new InvalidDataException($"Command UART model chưa được xác nhận ACK: {tx}")
        };
    }

    public static void ValidateCommands(IReadOnlyList<UartModelCommand> commands)
    {
        if (commands.Count == 0 || Family(commands[0].Tx) != "MODEL" || Family(commands[^1].Tx) != "FINISH")
            throw new InvalidDataException("UART profile phải bắt đầu :MODEL và kết thúc :FINISH.");
        string[] required = ["MODEL", "PINCOUNT", "ARRAYCOUNT", "CONCOUNT", "CONNECTORCOUNT", "FINISH"];
        foreach (string family in required)
            if (!commands.Any(c => Family(c.Tx) == family)) throw new InvalidDataException($"UART profile thiếu {family}.");

        ValidateCount(commands, "PINCOUNT", "PINDATA");
        ValidateCount(commands, "ARRAYCOUNT", "ARRAY");
        ValidateCount(commands, "CONCOUNT", "CON");
        ValidateCount(commands, "CONNECTORCOUNT", "CONNECTOR");
        foreach (string family in new[] { "PINDATA", "ARRAY", "CON", "CONNECTOR" })
        {
            int[] indexes = commands.Where(c => Family(c.Tx) == family)
                .Select(c => int.TryParse(c.Tx.Split(',').ElementAtOrDefault(1), out int n) ? n : -1).ToArray();
            if (!indexes.SequenceEqual(Enumerable.Range(0, indexes.Length)))
                throw new InvalidDataException($"Index {family} phải liên tục 0..{Math.Max(0, indexes.Length - 1)}.");
        }
    }

    static void ValidateCount(IReadOnlyList<UartModelCommand> commands, string countFamily, string itemFamily)
    {
        UartModelCommand? count = commands.FirstOrDefault(c => Family(c.Tx) == countFamily);
        if (count is null || !int.TryParse(count.Tx.Split(',').ElementAtOrDefault(1), out int declared))
            throw new InvalidDataException($"Giá trị {countFamily} không hợp lệ.");
        int actual = commands.Count(c => Family(c.Tx) == itemFamily);
        if (declared != actual) throw new InvalidDataException($"{countFamily}={declared} nhưng có {actual} {itemFamily}.");
    }

    static string ExtractModelName(IEnumerable<UartModelCommand> commands)
    {
        string? cmd = commands.Select(c => c.Tx).FirstOrDefault(x => Family(x) == "MODEL");
        return cmd is null || !cmd.Contains(',') ? "UNKNOWN" : cmd[(cmd.IndexOf(',') + 1)..].Trim();
    }
    static string Normalize(string text) => text.Trim().TrimEnd('\r', '\n');
    static string Family(string command) => Normalize(command).TrimStart(':', '*').Split(',', '?')[0].ToUpperInvariant();
}
