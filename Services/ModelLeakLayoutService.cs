using System.IO;
using System.Text.Json;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>Imports explicit channel assignments exported by ModelCreator, preserving machine settings.</summary>
public static class ModelLeakLayoutService
{
    public sealed record Channel(int ChannelNumber, string Connector);
    public sealed record Layout(int Version, List<Channel> Channels, string ModelSha256 = "");

    public static WaterProofModelSettings Apply(string? modelPath, WaterProofModelSettings profile, bool preserveSavedSettings = false)
    {
        var result = profile.Clone();
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath + ".leak.json")) return result;
        Layout layout;
        try
        {
            layout = JsonSerializer.Deserialize<Layout>(File.ReadAllText(modelPath + ".leak.json"))
                ?? throw new JsonException("Empty leak layout");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Cấu hình kênh leak không hợp lệ: {modelPath}.leak.json", ex);
        }
        if (layout.Version != 1 || layout.Channels is null || layout.Channels.Any(c => c is null ||
            c.ChannelNumber is < 1 or > 3 || string.IsNullOrWhiteSpace(c.Connector)) ||
            layout.Channels.Select(c => c.ChannelNumber).Distinct().Count() != layout.Channels.Count ||
            layout.Channels.Select(c => c.Connector).Distinct(StringComparer.OrdinalIgnoreCase).Count() != layout.Channels.Count)
            throw new InvalidDataException("Cấu hình leak phải gán riêng mỗi CH1/CH2/CH3 cho một connector.");

        if (!string.Equals(layout.ModelSha256,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(modelPath))),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File cấu hình leak không khớp THT. Hãy chép đúng cặp THT và .tht.leak.json.");

        var model = new ThtModelParser().Load(modelPath);
        if (layout.Channels.Any(c => !model.Connectors.Any(connector =>
            connector.ConnectorId.Equals(c.Connector, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Connector trong cấu hình leak không tồn tại trong file THT đang mở.");

        result.Enabled = layout.Channels.Count > 0;
        result.Channel1Enabled = result.Channel2Enabled = result.Channel3Enabled = false;
        result.Channel1Connector = result.Channel2Connector = result.Channel3Connector = string.Empty;
        foreach (var channel in layout.Channels)
        {
            switch (channel.ChannelNumber)
            {
                case 1: result.Channel1Enabled = true; result.Channel1Connector = channel.Connector; break;
                case 2: result.Channel2Enabled = true; result.Channel2Connector = channel.Connector; break;
                case 3: result.Channel3Enabled = true; result.Channel3Connector = channel.Connector; break;
            }
        }
        if (preserveSavedSettings)
        {
            // Sidecar provides defaults; operator choices remain authoritative after Save.
            result.Enabled = profile.Enabled;
            if (!string.IsNullOrWhiteSpace(profile.Channel1Connector)) result.Channel1Connector = profile.Channel1Connector;
            if (!string.IsNullOrWhiteSpace(profile.Channel2Connector)) result.Channel2Connector = profile.Channel2Connector;
            if (!string.IsNullOrWhiteSpace(profile.Channel3Connector)) result.Channel3Connector = profile.Channel3Connector;
            result.Channel1Enabled = profile.Channel1Enabled && !string.IsNullOrWhiteSpace(result.Channel1Connector);
            result.Channel2Enabled = profile.Channel2Enabled && !string.IsNullOrWhiteSpace(result.Channel2Connector);
            result.Channel3Enabled = profile.Channel3Enabled && !string.IsNullOrWhiteSpace(result.Channel3Connector);
        }
        return result;
    }
}
