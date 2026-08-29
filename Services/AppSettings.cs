using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace JBZUniversalTester.Services;

/// <summary>
/// Hardware/test settings retained as a typed compatibility model. Runtime
/// values are read from and written to JBZUniversalTester.cfg; appsettings.json
/// is a read-only migration input.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public int SchemaVersion { get; set; } = 1;

    public BoardSettings Board { get; set; } = new();
    public KeysightSettings Keysight { get; set; } = new();
    public TestSettings Test { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }

    public static string SettingsDirectory => RuntimePaths.AppDirectory;
    public static string SettingsPath => RuntimePaths.ConfigFile;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return LoadFromCfg(SettingsPath);

            return LoadLegacyJson();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Normalize();
        ProductionConfigService.SaveAppSettings(this);
    }

    internal static AppSettings LoadLegacyJson()
    {
        if (!File.Exists(RuntimePaths.LegacyAppSettingsJson))
            return new AppSettings();

        string json = File.ReadAllText(RuntimePaths.LegacyAppSettingsJson, Encoding.UTF8);
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                               ?? new AppSettings();
        settings.Normalize();
        return settings;
    }

    internal static AppSettings LoadFromCfg(string path)
    {
        var map = ProductionConfigService.ReadCfgLines(path)
            .Select(ParseCfgLine)
            .Where(item => item.Key.Length > 0)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        var settings = new AppSettings();

        settings.Board.FtdiSerial = S(map, "App.Board.FtdiSerial", settings.Board.FtdiSerial);
        settings.Board.RequiredStableFrames = I(map, "App.Board.RequiredStableFrames", settings.Board.RequiredStableFrames);
        settings.Keysight.Resource = S(map, "App.Keysight.Resource", settings.Keysight.Resource);
        settings.Keysight.Command = S(map, "App.Keysight.Command", settings.Keysight.Command);
        settings.Keysight.SettleDelayMs = I(map, "App.Keysight.SettleDelayMs", settings.Keysight.SettleDelayMs);
        settings.Test.RelayPulseMs = I(map, "App.Test.RelayPulseMs", settings.Test.RelayPulseMs);
        settings.Test.RelayInterlockMs = I(map, "App.Test.RelayInterlockMs", settings.Test.RelayInterlockMs);
        settings.Test.PostResistanceRelayDelayMs = I(map, "App.Test.PostResistanceRelayDelayMs", settings.Test.PostResistanceRelayDelayMs);
        settings.Test.PostRelayRestartDelayMs = I(map, "App.Test.PostRelayRestartDelayMs", settings.Test.PostRelayRestartDelayMs);
        settings.Test.AutoRestartAfterPass = B(map, "App.Test.AutoRestartAfterPass", settings.Test.AutoRestartAfterPass);
        settings.Test.FaultEjectRelay = I(map, "App.Test.FaultEjectRelay", settings.Test.FaultEjectRelay);
        settings.Test.FaultEjectPulseMs = I(map, "App.Test.FaultEjectPulseMs", settings.Test.FaultEjectPulseMs);
        settings.Test.ResistanceOpenThreshold = D(map, "App.Test.ResistanceOpenThreshold", settings.Test.ResistanceOpenThreshold);
        settings.Test.ResistanceMinimumSettleMs = I(map, "App.Test.ResistanceMinimumSettleMs", settings.Test.ResistanceMinimumSettleMs);
        settings.Test.ResistanceSampleIntervalMs = I(map, "App.Test.ResistanceSampleIntervalMs", settings.Test.ResistanceSampleIntervalMs);
        settings.Test.ResistanceStableSampleCount = I(map, "App.Test.ResistanceStableSampleCount", settings.Test.ResistanceStableSampleCount);
        settings.Test.ResistanceStableAbsoluteToleranceOhm = D(map, "App.Test.ResistanceStableAbsoluteToleranceOhm", settings.Test.ResistanceStableAbsoluteToleranceOhm);
        settings.Test.ResistanceStableRelativeTolerancePercent = D(map, "App.Test.ResistanceStableRelativeTolerancePercent", settings.Test.ResistanceStableRelativeTolerancePercent);
        settings.Test.ResistanceStabilityTimeoutMs = I(map, "App.Test.ResistanceStabilityTimeoutMs", settings.Test.ResistanceStabilityTimeoutMs);
        settings.Storage.Database = S(map, "App.Storage.Database", settings.Storage.Database);
        settings.Storage.Logs = S(map, "App.Storage.Logs", settings.Storage.Logs);
        settings.Storage.Models = S(map, "App.Storage.Models", settings.Storage.Models);
        settings.Storage.LastTestedModelFile = S(map, "App.Storage.LastTestedModelFile", settings.Storage.LastTestedModelFile);
        settings.Normalize();
        return settings;
    }

    internal IEnumerable<string> ToCfgLines()
    {
        Normalize();
        yield return $"[App.Board.FtdiSerial]{Board.FtdiSerial}";
        yield return $"[App.Board.RequiredStableFrames]{Board.RequiredStableFrames}";
        yield return $"[App.Keysight.Resource]{Keysight.Resource}";
        yield return $"[App.Keysight.Command]{Keysight.Command}";
        yield return $"[App.Keysight.SettleDelayMs]{Keysight.SettleDelayMs}";
        yield return $"[App.Test.RelayPulseMs]{Test.RelayPulseMs}";
        yield return $"[App.Test.RelayInterlockMs]{Test.RelayInterlockMs}";
        yield return $"[App.Test.PostResistanceRelayDelayMs]{Test.PostResistanceRelayDelayMs}";
        yield return $"[App.Test.PostRelayRestartDelayMs]{Test.PostRelayRestartDelayMs}";
        yield return $"[App.Test.AutoRestartAfterPass]{Bool(Test.AutoRestartAfterPass)}";
        yield return $"[App.Test.FaultEjectRelay]{Test.FaultEjectRelay}";
        yield return $"[App.Test.FaultEjectPulseMs]{Test.FaultEjectPulseMs}";
        yield return $"[App.Test.ResistanceOpenThreshold]{F(Test.ResistanceOpenThreshold)}";
        yield return $"[App.Test.ResistanceMinimumSettleMs]{Test.ResistanceMinimumSettleMs}";
        yield return $"[App.Test.ResistanceSampleIntervalMs]{Test.ResistanceSampleIntervalMs}";
        yield return $"[App.Test.ResistanceStableSampleCount]{Test.ResistanceStableSampleCount}";
        yield return $"[App.Test.ResistanceStableAbsoluteToleranceOhm]{F(Test.ResistanceStableAbsoluteToleranceOhm)}";
        yield return $"[App.Test.ResistanceStableRelativeTolerancePercent]{F(Test.ResistanceStableRelativeTolerancePercent)}";
        yield return $"[App.Test.ResistanceStabilityTimeoutMs]{Test.ResistanceStabilityTimeoutMs}";
    }

    private static (string Key, string Value) ParseCfgLine(string raw)
    {
        string line = raw.Trim();
        if (!line.StartsWith('[')) return (string.Empty, string.Empty);
        int close = line.IndexOf(']');
        return close <= 1
            ? (string.Empty, string.Empty)
            : (line[1..close].Trim(), line[(close + 1)..].Trim());
    }

    private static string S(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out string? value) ? value : fallback;
    private static int I(Dictionary<string, string> map, string key, int fallback) =>
        map.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number : fallback;
    private static double D(Dictionary<string, string> map, string key, double fallback) =>
        map.TryGetValue(key, out string? value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number : fallback;
    private static bool B(Dictionary<string, string> map, string key, bool fallback) =>
        map.TryGetValue(key, out string? value)
            ? value.Trim() switch { "1" => true, "0" => false, _ when bool.TryParse(value, out bool parsed) => parsed, _ => fallback }
            : fallback;
    private static int Bool(bool value) => value ? 1 : 0;
    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);

    private void Normalize()
    {
        Board ??= new BoardSettings();
        Keysight ??= new KeysightSettings();
        Test ??= new TestSettings();
        Storage ??= new StorageSettings();

        Test.ResistanceChannels ??= [];
        Test.ResistanceMinimumSettleMs = Math.Clamp(Test.ResistanceMinimumSettleMs, 0, 60_000);
        Test.ResistanceSampleIntervalMs = Math.Clamp(Test.ResistanceSampleIntervalMs, 0, 10_000);
        // Mỗi kênh chỉ đọc Keysight đúng một lần; ghi đè cấu hình cũ từng lưu 3 mẫu.
        Test.ResistanceStableSampleCount = 1;
        Test.ResistanceStableAbsoluteToleranceOhm = Math.Max(0, Test.ResistanceStableAbsoluteToleranceOhm);
        Test.ResistanceStableRelativeTolerancePercent = Math.Max(0, Test.ResistanceStableRelativeTolerancePercent);
        Test.ResistanceStabilityTimeoutMs = Math.Clamp(Test.ResistanceStabilityTimeoutMs, 100, 120_000);
    }

}

public sealed class BoardSettings
{
    public string FtdiSerial { get; set; } = string.Empty;
    public int RequiredStableFrames { get; set; } = 1;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class KeysightSettings
{
    public string Resource { get; set; } = "";
    public string Command { get; set; } = ":MEASURE:RES?";
    public int SettleDelayMs { get; set; } = 300;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class TestSettings
{
    public int RelayPulseMs { get; set; } = 250;
    public int RelayInterlockMs { get; set; } = 430;
    public int PostResistanceRelayDelayMs { get; set; } = 0;
    public int PostRelayRestartDelayMs { get; set; } = 200;
    public bool AutoRestartAfterPass { get; set; } = true;

    // Legacy compatibility only. Từ V12.7, lỗi/chập/probe KHÔNG được phép
    // kích relay; hai field này chỉ còn để đọc appsettings cũ, không dùng
    // trong workflow lỗi. Relay tự động chỉ chạy khi PASS hợp lệ.
    public int FaultEjectRelay { get; set; } = 1;
    public int FaultEjectPulseMs { get; set; } = 250;

    public double ResistanceOpenThreshold { get; set; } = 1e30;
    public int ResistanceMinimumSettleMs { get; set; } = 300;
    public int ResistanceSampleIntervalMs { get; set; } = 50;
    public int ResistanceStableSampleCount { get; set; } = 1;
    public double ResistanceStableAbsoluteToleranceOhm { get; set; } = 5;
    public double ResistanceStableRelativeTolerancePercent { get; set; } = 0.2;
    public int ResistanceStabilityTimeoutMs { get; set; } = 3000;

    public List<ResistanceChannelSettings> ResistanceChannels
    {
        get;
        set;
    } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class ResistanceChannelSettings
{
    public string Name { get; set; } = "R1";
    public int Channel { get; set; } = 1;

    public double MinOhm { get; set; } = 8000;
    public double MaxOhm { get; set; } = 10000;

    public string RouteA { get; set; } = "90 00 00 01";
    public string RouteB { get; set; } = "91 00 00 01";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class StorageSettings
{
    public string Database { get; set; } = "Data/results.db";
    public string Logs { get; set; } = "Data/Logs";
    public string Models { get; set; } = "Data/Models";
    public string LastTestedModelFile { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}
