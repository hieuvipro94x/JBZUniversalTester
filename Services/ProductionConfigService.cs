using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public static class ProductionConfigService
{
    private static readonly string[] LegacyTimingKeys =
    [
        nameof(ProductionSettings.IoScanIntervalMs),
        nameof(ProductionSettings.ShortCircuitConfirmMs),
        nameof(ProductionSettings.WrongConnectionConfirmMs),
        nameof(ProductionSettings.ProductSettleTimeMs),
        nameof(ProductionSettings.JigContactUnstableWindowMs),
        nameof(ProductionSettings.ShortConfirmMs)
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string ConfigDirectory => AppContext.BaseDirectory;
    public static string JsonPath => Path.Combine(ConfigDirectory, "production.settings.json");
    public static string LegacyCfgPath => Path.Combine(ConfigDirectory, "UniversalTester.cfg");

    public static ProductionSettings Load()
    {
        Directory.CreateDirectory(ConfigDirectory);
        ProductionSettings settings;

        try
        {
            if (File.Exists(JsonPath))
            {
                string json = File.ReadAllText(JsonPath, Encoding.UTF8);
                settings = JsonSerializer.Deserialize<ProductionSettings>(json, JsonOptions)
                           ?? new ProductionSettings();

                // Migration cũ: Lot chuỗi -> LotNo.
                if (!json.Contains("\"LotNo\"", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(settings.Lot, NumberStyles.Integer, CultureInfo.InvariantCulture, out long legacyLot) &&
                    legacyLot >= 0)
                {
                    settings.LotNo = legacyLot;
                }

                // V15.2 migration: cấu hình cũ chỉ có StampDelay="R1,R2".
                // Chỉ migrate khi JSON chưa có các field relay tách riêng.
                if (!json.Contains("\"Relay1JigPulseMs\"", StringComparison.OrdinalIgnoreCase) &&
                    StampDelayParser.TryParse(settings.StampDelay, out int oldR1, out int oldR2))
                {
                    settings.Relay1JigPulseMs = oldR1;
                    settings.Relay2MarkingPulseMs = oldR2;
                }

                if (!json.Contains("\"PassMarkingToJigDelayMs\"", StringComparison.OrdinalIgnoreCase))
                    settings.PassMarkingToJigDelayMs = 430;
            }
            else if (File.Exists(LegacyCfgPath))
            {
                settings = LoadEnglishCfg(LegacyCfgPath);
            }
            else
            {
                settings = new ProductionSettings();
            }
        }
        catch (JsonException ex)
        {
            BackupInvalidConfig(JsonPath);
            AsyncFileLogService.Current.Error($"production.settings.json invalid: {ex.Message}");
            settings = new ProductionSettings();
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Load production settings failed: {ex.Message}");
            settings = new ProductionSettings();
        }

        Normalize(settings);
        return settings;
    }

    public static void ReloadInto(ProductionSettings target)
    {
        ArgumentNullException.ThrowIfNull(target);
        ProductionSettings source = Load();

        foreach (var property in typeof(ProductionSettings).GetProperties())
        {
            if (property.CanRead && property.CanWrite)
                property.SetValue(target, property.GetValue(source));
        }
    }

    public static void Save(ProductionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(ConfigDirectory);
        Normalize(settings);

        string json = SerializeSettingsForSave(settings);
        AtomicWrite(JsonPath, json);
        SaveLegacyCfg(settings, LegacyCfgPath);
    }

    private static string SerializeSettingsForSave(ProductionSettings settings)
    {
        JsonNode? node = JsonSerializer.SerializeToNode(settings, JsonOptions);
        if (node is not JsonObject root)
            return JsonSerializer.Serialize(settings, JsonOptions);

        foreach (string key in LegacyTimingKeys)
            root.Remove(key);

        return root.ToJsonString(JsonOptions);
    }
    /// <summary>
    /// V12: UniversalTester.cfg dùng tên key tiếng Anh 100% và ghi ĐẦY ĐỦ
    /// mọi trường trên màn Cài đặt. Tên method được giữ để code cũ vẫn gọi được.
    /// </summary>
    public static void SaveLegacyCfg(ProductionSettings settings, string path)
    {
        Normalize(settings);
        var lines = new List<string>
        {
            $"[BoardMode]{settings.BoardMode}",
            $"[UartPort]{settings.UartPort}",
            $"[LastUartModelPath]{settings.LastUartModelPath}",
            $"[CardCount]{settings.CardCount}",
            $"[ExpansionCardCount]{settings.ExpansionCardCount}",
            $"[IoConfirm1]{settings.IoConfirm1}",
            $"[IoConfirmN]{settings.IoConfirmN}",
            $"[UsbDelay]{settings.UsbDelay}",
            $"[StartCardNumber]{settings.StartCardNumber}",
            $"[UseTestPointer]{Bool(settings.UseTestPointer)}",
            $"[AutoMasterSequence]{Bool(settings.AutoMasterSequence)}",
            $"[MasterFaultRequiredCount]{settings.MasterFaultRequiredCount}",
            $"[WaterproofSerialPort]{settings.WaterproofSerialPort}",

            $"[LotNo]{settings.LotNo}",
            $"[Lot]{settings.Lot}",
            $"[DeviceName]{settings.DeviceName}",
            $"[DeviceNumber]{settings.DeviceNumber}",
            $"[OperatorCompany]{settings.OperatorCompany}",
            $"[ProductionLine]{settings.ProductionLine}",
            $"[TemperatureTolerance]{F(settings.TemperatureTolerance)}",
            $"[MinimumErrorLogValue]{settings.MinimumErrorLogValue}",
            $"[AutoSaveErrors]{Bool(settings.AutoSaveErrors)}",
            $"[ProbeReplacementThreshold]{settings.ProbeReplacementThreshold}",
            $"[Relay1JigPulseMs]{settings.Relay1JigPulseMs}",
            $"[Relay2MarkingPulseMs]{settings.Relay2MarkingPulseMs}",
            $"[PassMarkingToJigDelayMs]{settings.PassMarkingToJigDelayMs}",
            $"[StampDelayMs]{settings.Relay1JigPulseMs},{settings.Relay2MarkingPulseMs}", // compatibility
            $"[OversizeWaitSeconds]{settings.OversizeWaitSeconds}",
            $"[ShieldDelayMs]{settings.ShieldDelay}",
            $"[ResistanceDelayMs]{settings.ResistanceDelayMs}",
            $"[SettingsPassword]{settings.Password}",

            $"[ItemHeight]{settings.ItemHeight}",
            $"[ScrollDelayMs]{settings.ScrollDelay}",
            $"[PageDelayMs]{settings.PageDelay}",
            $"[ShowTitle]{Bool(settings.ShowTitle)}",
            $"[ShowConnector]{Bool(settings.ShowConnector)}",

            $"[LastThtPath]{settings.LastThtPath}",
            $"[AutoPrintLabelOnPass]{Bool(settings.AutoPrintLabelOnPass)}",
            $"[HistoryDirectory]{settings.HistoryDirectory}",

            $"[LabelPrinterName]{settings.Label.PrinterName}",
            $"[LabelPrinterCom]{settings.Label.PrinterCom}",
            $"[LabelWidthMm]{settings.Label.WidthMm}",
            $"[LabelHeightMm]{settings.Label.HeightMm}",
            $"[LabelFormatName]{settings.Label.FormatName}",
            $"[LabelBaudRate]{settings.Label.BaudRate}",
            $"[LabelWriteTimeoutMs]{settings.Label.WriteTimeoutMs}",
            $"[LabelCopies]{settings.Label.Copies}"
        };

        foreach ((string modelKey, int requiredCount) in settings.MasterFaultCountsByModel
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            string encodedKey = Uri.EscapeDataString(modelKey);
            lines.Add($"[MasterFault.{encodedKey}]{requiredCount}");
        }

        foreach (ResistanceChannelSetting resistance in settings.ResistanceChannels)
        {
            lines.Add(
                $"[Resistance.{resistance.Name}]" +
                $"{Bool(resistance.Enabled)};{resistance.Channel};" +
                $"{F(resistance.MinOhm)};{F(resistance.MaxOhm)}");
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        AtomicWrite(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    public static string GetMasterModelKey(ProductModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!string.IsNullOrWhiteSpace(model.PartNumber))
            return model.PartNumber.Trim();
        if (!string.IsNullOrWhiteSpace(model.ModelName))
            return model.ModelName.Trim();
        return GetMasterModelKeyFromPath(model.SourcePath);
    }

    public static string GetMasterModelKeyFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "DEFAULT";
        string file = Path.GetFileNameWithoutExtension(path.Trim());
        return string.IsNullOrWhiteSpace(file) ? "DEFAULT" : file.Trim();
    }

    public static int GetMasterFaultRequiredCount(ProductionSettings settings, ProductModel model)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(model);
        Normalize(settings);

        string primaryKey = GetMasterModelKey(model);
        if (settings.MasterFaultCountsByModel.TryGetValue(primaryKey, out int count))
            return Math.Clamp(count, 1, 99);

        string pathKey = GetMasterModelKeyFromPath(model.SourcePath);
        if (settings.MasterFaultCountsByModel.TryGetValue(pathKey, out count))
            return Math.Clamp(count, 1, 99);

        if (!string.IsNullOrWhiteSpace(model.ModelName) &&
            settings.MasterFaultCountsByModel.TryGetValue(model.ModelName.Trim(), out count))
            return Math.Clamp(count, 1, 99);

        return settings.MasterFaultRequiredCount;
    }

    public static int GetMasterFaultRequiredCountForPath(ProductionSettings settings, string? path)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        string key = GetMasterModelKeyFromPath(path);
        return settings.MasterFaultCountsByModel.TryGetValue(key, out int count)
            ? Math.Clamp(count, 1, 99)
            : settings.MasterFaultRequiredCount;
    }

    public static void SetMasterFaultRequiredCountForPath(ProductionSettings settings, string? path, int count)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        int normalized = Math.Clamp(count, 1, 99);
        string key = GetMasterModelKeyFromPath(path);
        if (string.Equals(key, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            settings.MasterFaultRequiredCount = normalized;
        else
            settings.MasterFaultCountsByModel[key] = normalized;
    }

    public static void EnsureSavedOnStartup(ProductionSettings settings)
    {
        try { Save(settings); }
        catch { /* startup không được treo chỉ vì thư mục readonly */ }
    }

    private static ProductionSettings LoadEnglishCfg(string path)
    {
        var settings = new ProductionSettings();
        var map = File.ReadLines(path, Encoding.UTF8)
            .Select(ParseCfgLine)
            .Where(x => x.Key.Length > 0)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        // Migration từ CFG gốc Htdrv: đọc cả key tiếng Hàn đã thấy trong file cũ,
        // sau lần Save tiếp theo toàn bộ file sẽ được ghi lại bằng key tiếng Anh.
        // V12.9 migration: [카드 수] của Htdrv là byte xx của START_SCAN
        // (trace command=4 -> diagnostic 256 I/O), không phải card vật lý 32 I/O.
        // Nếu file mới có ExpansionCardCount thì ưu tiên key mới; nếu không dùng
        // CardCount/카드 수 làm số scan-unit 64 I/O.
        string boardModeText = S(map, "BoardMode", settings.BoardMode.ToString());
        if (Enum.TryParse(boardModeText, true, out BoardMode parsedBoardMode))
            settings.BoardMode = parsedBoardMode;
        settings.UartPort = S(map, "UartPort", settings.UartPort);
        settings.LastUartModelPath = S(map, "LastUartModelPath", settings.LastUartModelPath);

        int legacyScanCount = IAny(map, settings.CardCount, "CardCount", "카드 수");
        int expansionModules = I(map, "ExpansionCardCount", legacyScanCount);
        settings.ExpansionCardCount = Math.Clamp(
            expansionModules,
            1,
            BoardCapacity.MaxExpansionModuleCount);
        settings.CardCount = settings.ExpansionCardCount;
        settings.IoConfirm1 = IAny(map, settings.IoConfirm1, "IoConfirm1", "IO1 확인");
        settings.IoConfirmN = IAny(map, settings.IoConfirmN, "IoConfirmN", "IOn 확인");
        settings.UsbDelay = IAny(map, settings.UsbDelay, "UsbDelay", "USB 지연");
        settings.StartCardNumber = I(map, "StartCardNumber", settings.StartCardNumber);
        settings.UseTestPointer = B(map, "UseTestPointer", settings.UseTestPointer);
        settings.AutoMasterSequence = B(map, "AutoMasterSequence", settings.AutoMasterSequence);
        settings.MasterFaultRequiredCount = I(map, "MasterFaultRequiredCount", settings.MasterFaultRequiredCount);
        settings.WaterproofSerialPort = I(map, "WaterproofSerialPort", settings.WaterproofSerialPort);

        foreach ((string key, string value) in map)
        {
            const string prefix = "MasterFault.";
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                continue;
            }

            string modelKey = Uri.UnescapeDataString(key[prefix.Length..]);
            if (!string.IsNullOrWhiteSpace(modelKey))
                settings.MasterFaultCountsByModel[modelKey] = count;
        }

        settings.LotNo = L(map, "LotNo", settings.LotNo);
        settings.Lot = S(map, "Lot", settings.Lot);
        settings.DeviceName = S(map, "DeviceName", settings.DeviceName);
        settings.DeviceNumber = S(map, "DeviceNumber", settings.DeviceNumber);
        settings.OperatorCompany = S(map, "OperatorCompany", settings.OperatorCompany);
        settings.ProductionLine = S(map, "ProductionLine", settings.ProductionLine);
        settings.TemperatureTolerance = D(map, "TemperatureTolerance", settings.TemperatureTolerance);
        settings.MinimumErrorLogValue = I(map, "MinimumErrorLogValue", settings.MinimumErrorLogValue);
        settings.AutoSaveErrors = B(map, "AutoSaveErrors", settings.AutoSaveErrors);

        settings.IoScanIntervalMs = I(map, "IoScanIntervalMs", settings.IoScanIntervalMs);
        settings.OpenCircuitConfirmMs = I(map, "OpenCircuitConfirmMs", settings.OpenCircuitConfirmMs);
        settings.ShortConfirmMs = I(map, "ShortConfirmMs", settings.ShortConfirmMs);
        settings.ShortCircuitConfirmMs = I(map, "ShortCircuitConfirmMs", settings.ShortCircuitConfirmMs);
        settings.WrongConnectionConfirmMs = I(map, "WrongConnectionConfirmMs", settings.WrongConnectionConfirmMs);
        settings.ProductSettleTimeMs = I(map, "ProductSettleTimeMs", settings.ProductSettleTimeMs);
        settings.JigContactUnstableWindowMs = I(map, "JigContactUnstableWindowMs", settings.JigContactUnstableWindowMs);
        settings.ProbeReplacementThreshold = L(map, "ProbeReplacementThreshold", settings.ProbeReplacementThreshold);
        settings.StampDelay = SAny(map, settings.StampDelay, "StampDelayMs", "스탬프 지연(msec)");
        bool hasSplitRelay = map.ContainsKey("Relay1JigPulseMs") || map.ContainsKey("Relay2MarkingPulseMs");
        if (hasSplitRelay)
        {
            settings.Relay1JigPulseMs = I(map, "Relay1JigPulseMs", settings.Relay1JigPulseMs);
            settings.Relay2MarkingPulseMs = I(map, "Relay2MarkingPulseMs", settings.Relay2MarkingPulseMs);
        }
        else if (StampDelayParser.TryParse(settings.StampDelay, out int legacyR1, out int legacyR2))
        {
            settings.Relay1JigPulseMs = legacyR1;
            settings.Relay2MarkingPulseMs = legacyR2;
        }
        settings.PassMarkingToJigDelayMs = I(map, "PassMarkingToJigDelayMs", settings.PassMarkingToJigDelayMs);
        settings.OversizeWaitSeconds = I(map, "OversizeWaitSeconds", settings.OversizeWaitSeconds);
        settings.ShieldDelay = I(map, "ShieldDelayMs", settings.ShieldDelay);
        settings.ResistanceDelayMs = I(map, "ResistanceDelayMs", settings.ResistanceDelayMs);
        settings.Password = S(map, "SettingsPassword", settings.Password);

        settings.ItemHeight = I(map, "ItemHeight", settings.ItemHeight);
        settings.ScrollDelay = I(map, "ScrollDelayMs", settings.ScrollDelay);
        settings.PageDelay = I(map, "PageDelayMs", settings.PageDelay);
        settings.ShowTitle = B(map, "ShowTitle", settings.ShowTitle);
        settings.ShowConnector = B(map, "ShowConnector", settings.ShowConnector);

        settings.LastThtPath = S(map, "LastThtPath", settings.LastThtPath);
        settings.AutoPrintLabelOnPass = B(map, "AutoPrintLabelOnPass", settings.AutoPrintLabelOnPass);
        settings.HistoryDirectory = S(map, "HistoryDirectory", settings.HistoryDirectory);

        settings.Label.PrinterName = S(map, "LabelPrinterName", settings.Label.PrinterName);
        settings.Label.PrinterCom = S(map, "LabelPrinterCom", settings.Label.PrinterCom);
        settings.Label.WidthMm = I(map, "LabelWidthMm", settings.Label.WidthMm);
        settings.Label.HeightMm = I(map, "LabelHeightMm", settings.Label.HeightMm);
        settings.Label.FormatName = S(map, "LabelFormatName", settings.Label.FormatName);
        settings.Label.BaudRate = I(map, "LabelBaudRate", settings.Label.BaudRate);
        settings.Label.WriteTimeoutMs = I(map, "LabelWriteTimeoutMs", settings.Label.WriteTimeoutMs);
        settings.Label.Copies = I(map, "LabelCopies", settings.Label.Copies);

        foreach (ResistanceChannelSetting channel in settings.ResistanceChannels)
        {
            if (!map.TryGetValue($"Resistance.{channel.Name}", out string? text) ||
                string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            string[] parts = text.Split(';');
            if (parts.Length > 0) channel.Enabled = ParseBool(parts[0], channel.Enabled);
            if (parts.Length > 1 && int.TryParse(parts[1], out int c)) channel.Channel = c;
            if (parts.Length > 2 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double min)) channel.MinOhm = min;
            if (parts.Length > 3 && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double max)) channel.MaxOhm = max;
        }

        return settings;
    }

    private static void Normalize(ProductionSettings settings)
    {
        if (!Enum.IsDefined(typeof(BoardMode), settings.BoardMode))
            settings.BoardMode = BoardMode.Auto;
        settings.UartPort = (settings.UartPort ?? string.Empty).Trim().ToUpperInvariant();
        settings.LastUartModelPath = (settings.LastUartModelPath ?? string.Empty).Trim();

        settings.Label ??= new LabelSettings();
        settings.ResistanceChannels ??= [];
        settings.MasterFaultCountsByModel ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (settings.MasterFaultCountsByModel.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            settings.MasterFaultCountsByModel = new Dictionary<string, int>(
                settings.MasterFaultCountsByModel,
                StringComparer.OrdinalIgnoreCase);
        }

        // V12.9.5: Master luôn tự động trong Production. Không còn đường manual song song.
        settings.AutoMasterSequence = true;
        settings.MasterFaultRequiredCount = Math.Clamp(settings.MasterFaultRequiredCount, 1, 99);

        foreach (string key in settings.MasterFaultCountsByModel.Keys.ToArray())
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                settings.MasterFaultCountsByModel.Remove(key);
                continue;
            }
            settings.MasterFaultCountsByModel[key] = Math.Clamp(settings.MasterFaultCountsByModel[key], 1, 99);
        }

        if (settings.LotNo < 0) settings.LotNo = 0;
        ProductionTimingPolicy.Normalize(settings);
        settings.UsbDelay = Math.Clamp(settings.UsbDelay, 1, 16);
        settings.IoConfirm1 = Math.Clamp(settings.IoConfirm1, 0, 127);
        settings.IoConfirmN = Math.Clamp(settings.IoConfirmN, 0, 31);
        settings.ExpansionCardCount = Math.Clamp(
            settings.ExpansionCardCount,
            1,
            BoardCapacity.MaxExpansionModuleCount);

        int activePhysicalCards =
            settings.ExpansionCardCount * BoardCapacity.PhysicalCardsPerExpansionModule;
        int maxStartCard = Math.Max(
            1,
            BoardCapacity.MaxPhysicalCardCount - activePhysicalCards + 1);
        settings.StartCardNumber = Math.Clamp(settings.StartCardNumber, 1, maxStartCard);

        BoardCapacity capacity = BoardCapacity.FromSettings(settings);
        settings.CardCount = capacity.ScanCardCount;
        settings.WaterproofSerialPort = Math.Clamp(settings.WaterproofSerialPort, 0, 999);

        // V15.2: ba thông số relay độc lập. 50..5000 ms tránh pulse bằng 0 hoặc giữ relay quá lâu do nhập nhầm.
        settings.Relay1JigPulseMs = Math.Clamp(settings.Relay1JigPulseMs, 50, 5_000);
        settings.Relay2MarkingPulseMs = Math.Clamp(settings.Relay2MarkingPulseMs, 50, 5_000);
        settings.PassMarkingToJigDelayMs = Math.Clamp(settings.PassMarkingToJigDelayMs, 0, 5_000);
        settings.StampDelay = $"{settings.Relay1JigPulseMs},{settings.Relay2MarkingPulseMs}"; // compatibility only

        settings.OversizeWaitSeconds = Math.Clamp(settings.OversizeWaitSeconds, 0, 86_400);
        settings.ShieldDelay = Math.Clamp(settings.ShieldDelay, 0, 60_000);
        settings.ResistanceDelayMs = Math.Clamp(settings.ResistanceDelayMs, 0, 60_000);
        settings.ItemHeight = Math.Clamp(settings.ItemHeight, 24, 80);
        settings.ScrollDelay = Math.Clamp(settings.ScrollDelay, 0, 5000);
        settings.PageDelay = Math.Clamp(settings.PageDelay, 0, 5000);
        settings.MinimumErrorLogValue = Math.Max(0, settings.MinimumErrorLogValue);

        settings.HistoryDirectory = string.IsNullOrWhiteSpace(settings.HistoryDirectory)
            ? "Data/History"
            : settings.HistoryDirectory.Trim();

        settings.Label.WidthMm = Math.Clamp(settings.Label.WidthMm, 20, 200);
        settings.Label.HeightMm = Math.Clamp(settings.Label.HeightMm, 10, 150);
        settings.Label.FormatName = string.IsNullOrWhiteSpace(settings.Label.FormatName) ? "KS91" : settings.Label.FormatName.Trim();
        settings.Label.BaudRate = Math.Clamp(settings.Label.BaudRate, 1200, 921600);
        settings.Label.WriteTimeoutMs = Math.Clamp(settings.Label.WriteTimeoutMs, 500, 30_000);
        settings.Label.Copies = Math.Clamp(settings.Label.Copies, 1, 20);

        EnsureResistanceChannels(settings);
    }

    private static void EnsureResistanceChannels(ProductionSettings settings)
    {
        var byName = settings.ResistanceChannels
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        var list = new List<ResistanceChannelSetting>();
        for (int i = 1; i <= 5; i++)
        {
            string name = $"R{i}";
            if (!byName.TryGetValue(name, out ResistanceChannelSetting? item))
            {
                item = new ResistanceChannelSetting { Name = name, Channel = i };
            }
            item.Name = name;
            item.Channel = Math.Clamp(item.Channel, 1, 5);
            item.MinOhm = Math.Max(0, item.MinOhm);
            item.MaxOhm = Math.Max(item.MinOhm, item.MaxOhm);
            list.Add(item);
        }
        settings.ResistanceChannels = list.ToArray();
    }

    private static (string Key, string Value) ParseCfgLine(string raw)
    {
        string line = raw.Trim();
        if (!line.StartsWith('[')) return (string.Empty, string.Empty);
        int close = line.IndexOf(']');
        if (close <= 1) return (string.Empty, string.Empty);
        return (line[1..close].Trim(), line[(close + 1)..].Trim());
    }

    private static string S(Dictionary<string, string> map, string key, string fallback) => map.TryGetValue(key, out string? value) ? value : fallback;
    private static string SAny(Dictionary<string, string> map, string fallback, params string[] keys)
    {
        foreach (string key in keys)
            if (map.TryGetValue(key, out string? value)) return value;
        return fallback;
    }
    private static int I(Dictionary<string, string> map, string key, int fallback) => map.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
    private static int IAny(Dictionary<string, string> map, int fallback, params string[] keys)
    {
        foreach (string key in keys)
            if (map.TryGetValue(key, out string? value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n;
        return fallback;
    }
    private static long L(Dictionary<string, string> map, string key, long fallback) => map.TryGetValue(key, out string? value) && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : fallback;
    private static double D(Dictionary<string, string> map, string key, double fallback) => map.TryGetValue(key, out string? value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : fallback;
    private static bool B(Dictionary<string, string> map, string key, bool fallback) => map.TryGetValue(key, out string? value) ? ParseBool(value, fallback) : fallback;
    private static bool ParseBool(string value, bool fallback) => value.Trim() switch { "1" => true, "0" => false, _ when bool.TryParse(value, out bool b) => b, _ => fallback };
    private static int Bool(bool value) => value ? 1 : 0;
    private static string F(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static void BackupInvalidConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            string directory = Path.GetDirectoryName(path) ?? ConfigDirectory;
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string backup = Path.Combine(
                directory,
                $"{name}.invalid_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
            File.Copy(path, backup, overwrite: true);
        }
        catch
        {
        }
    }

    private static void AtomicWrite(string path, string text)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        if (File.Exists(path))
        {
            try { File.Replace(tmp, path, path + ".bak", true); }
            catch { File.Copy(tmp, path, true); File.Delete(tmp); }
        }
        else
        {
            File.Move(tmp, path);
        }
    }
}
