using System.IO;
using System.Text.Json;
using JBZUniversalTester.ModelEditor.Models;

namespace JBZUniversalTester.ModelEditor.Services;

/// <summary>Channel assignments travel beside the model; machine parameters stay in the tester.</summary>
public static class LeakLayoutService
{
    public sealed record Channel(int ChannelNumber, string Connector);
    public sealed record Layout(int Version, List<Channel> Channels, string ModelSha256 = "");
    public static string LayoutPath(string modelPath) => modelPath + ".leak.json";

    public static void Validate(IEnumerable<Connector> connectors)
    {
        var selected = connectors.Where(c => c.Waterproof).ToList();
        if (selected.Any(c => c.LeakChannel is < 1 or > 3))
            throw new ValidationException("Hãy chọn CH1, CH2 hoặc CH3 cho từng connector kiểm tra kín nước.");
        if (selected.GroupBy(c => c.LeakChannel).Any(g => g.Count() > 1))
            throw new ValidationException("Mỗi kênh leak chỉ được gán cho một connector.");
    }

    public static string? Prepare(ModelDocument document, string target)
    {
        // Legacy files only contain a boolean flag, with no physical channel assignment.
        if (!document.Connectors.Any(c => c.LeakChannel != 0) &&
            !File.Exists(LayoutPath(target)) &&
            (document.SourcePath is null || !File.Exists(LayoutPath(document.SourcePath))))
            return null;
        Validate(document.Connectors);
        return JsonSerializer.Serialize(new Layout(1, document.Connectors.Where(c => c.Waterproof)
            .Select(c => new Channel(c.LeakChannel, c.Name)).OrderBy(c => c.ChannelNumber).ToList()),
            new JsonSerializerOptions { WriteIndented = true });
    }

    public static void Save(string modelPath, string? json)
    {
        if (json is null) return;
        var layout = JsonSerializer.Deserialize<Layout>(json)! with
        {
            ModelSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(modelPath)))
        };
        json = JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true });
        var target = LayoutPath(modelPath);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, json); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static void Load(string modelPath, List<Connector> connectors)
    {
        var path = LayoutPath(modelPath);
        if (!File.Exists(path)) return;
        Layout layout;
        try { layout = JsonSerializer.Deserialize<Layout>(File.ReadAllText(path)) ?? throw new JsonException(); }
        catch (JsonException ex) { throw new ValidationException($"Cấu hình leak không hợp lệ: {path}. {ex.Message}"); }
        if (layout.Version != 1 || layout.Channels is null)
            throw new ValidationException($"Phiên bản cấu hình leak không hỗ trợ: {path}");
        if (!string.Equals(layout.ModelSha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(modelPath))),
            StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("File cấu hình leak không khớp file model. Hãy dùng đúng cặp file đã lưu.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in layout.Channels)
        {
            if (assignment is null)
                throw new ValidationException("Dòng cấu hình leak không hợp lệ.");
            var connector = connectors.SingleOrDefault(c => c.Name.Equals(assignment.Connector, StringComparison.OrdinalIgnoreCase));
            if (connector is null || !connector.Waterproof || !names.Add(connector.Name))
                throw new ValidationException($"Cấu hình leak không khớp connector: {assignment.Connector}");
            connector.LeakChannel = assignment.ChannelNumber;
        }
        Validate(connectors);
    }
}
