using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JBZUniversalTester.Services;

/// <summary>
/// Cấu hình ngoài EXE.
/// File tự tạo tại:
/// C:\ProgramData\JBZUniversalTester\appsettings.json
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

    public static string SettingsDirectory =>
    AppContext.BaseDirectory;

    public static string SettingsPath =>
        Path.Combine(SettingsDirectory, "appsettings.json");

    public static AppSettings Load()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            if (!File.Exists(SettingsPath))
            {
                var defaultSettings = new AppSettings();
                defaultSettings.Save();
                return defaultSettings;
            }

            string json = File.ReadAllText(
                SettingsPath,
                Encoding.UTF8);

            AppSettings settings =
                JsonSerializer.Deserialize<AppSettings>(
                    json,
                    JsonOptions)
                ?? new AppSettings();

            settings.Normalize();
            settings.Save();

            return settings;
        }
        catch (JsonException)
        {
            BackupInvalidFile();

            var defaultSettings = new AppSettings();
            defaultSettings.Save();

            return defaultSettings;
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(SettingsDirectory);

        string json = JsonSerializer.Serialize(
            this,
            JsonOptions);

        string temporaryPath = SettingsPath + ".tmp";

        File.WriteAllText(
            temporaryPath,
            json,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));

        try
        {
            if (File.Exists(SettingsPath))
            {
                string backupPath = SettingsPath + ".bak";

                File.Replace(
                    temporaryPath,
                    SettingsPath,
                    backupPath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(
                    temporaryPath,
                    SettingsPath);
            }
        }
        catch (PlatformNotSupportedException)
        {
            ReplaceUsingCopy(temporaryPath);
        }
        catch (IOException)
        {
            ReplaceUsingCopy(temporaryPath);
        }
    }

    private static void ReplaceUsingCopy(
        string temporaryPath)
    {
        File.Copy(
            temporaryPath,
            SettingsPath,
            overwrite: true);

        File.Delete(temporaryPath);
    }

    private void Normalize()
    {
        Board ??= new BoardSettings();
        Keysight ??= new KeysightSettings();
        Test ??= new TestSettings();
        Storage ??= new StorageSettings();

        Test.ResistanceChannels ??= [];
        Test.ResistanceMinimumSettleMs = Math.Clamp(Test.ResistanceMinimumSettleMs, 0, 60_000);
        Test.ResistanceSampleIntervalMs = Math.Clamp(Test.ResistanceSampleIntervalMs, 0, 10_000);
        Test.ResistanceStableSampleCount = Math.Clamp(Test.ResistanceStableSampleCount, 1, 20);
        Test.ResistanceStableAbsoluteToleranceOhm = Math.Max(0, Test.ResistanceStableAbsoluteToleranceOhm);
        Test.ResistanceStableRelativeTolerancePercent = Math.Max(0, Test.ResistanceStableRelativeTolerancePercent);
        Test.ResistanceStabilityTimeoutMs = Math.Clamp(Test.ResistanceStabilityTimeoutMs, 100, 120_000);
    }

    private static void BackupInvalidFile()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            string stamp =
                DateTime.Now.ToString("yyyyMMdd_HHmmss");

            string backupPath = Path.Combine(
                SettingsDirectory,
                $"appsettings.invalid_{stamp}.json");

            File.Copy(
                SettingsPath,
                backupPath,
                overwrite: true);
        }
        catch
        {
            // Không để lỗi sao lưu làm dừng ứng dụng.
        }
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
    public int ResistanceStableSampleCount { get; set; } = 3;
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
