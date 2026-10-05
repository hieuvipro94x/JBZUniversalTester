using System.Globalization;

namespace JBZUniversalTester.Models;

/// <summary>
/// Builds compact Korean fault text for customer history exports from canonical
/// structured fault data. Operator/customer UI localization remains unchanged.
/// </summary>
public static class KoreanHistoryFormatter
{
    private const int MaximumExportedFaults = 3;

    public static string FormatMasterTrace(string trace)
    {
        string visible = string.Join(" | ", trace.Split('|')
            .Select(part => part.Trim())
            .Where(part => !(part.StartsWith("Phiên ", StringComparison.Ordinal) &&
                             Guid.TryParse(part[6..].Trim(), out _))));
        // Translate the known persisted Master audit fields without changing the DB.
        (string Source, string Korean)[] fields =
        [
            ("MASTER ĐẦU CA", "작업 시작 전 마스터 검사"),
            ("Ngày sản xuất", "생산일"),
            ("MẪU YÊU CẦU:", "필수 샘플:"),
            ("Mẫu NG -", "불량 마스터 -"),
            ("Mẫu ĐẠT", "정상 마스터"),
            ("Số điểm", "검출 수"),
            ("Xác nhận KHÔNG ĐẠT", "확인 불합격"),
            ("Xác nhận ĐẠT", "확인 합격"),
            ("Xác nhận", "확인"),
            ("TUỘT TUÝT / ĐỨT DÂY", "핀 빠짐 / 단선"),
            ("SAI DÂY", "오배선"),
            ("CHẬP MẠCH", "단락"),
            ("KẾT NỐI", "연결"),
            ("KHÔNG ĐẠT", "불합격"),
            ("ĐẠT", "정상")
        ];
        foreach ((string source, string korean) in fields)
            visible = visible.Replace(source, korean, StringComparison.Ordinal);
        return visible;
    }

    public static string FormatFaults(TestHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        IReadOnlyList<FaultDetail> faults = FaultDisplayFormatter.DeserializeFaults(record.FaultDetailsJson);
        if (faults.Count == 0)
            faults = [CreateFallback(record)];

        string[] details = faults
            .Where(fault => fault.Type != ProductFaultType.None)
            .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
            .Take(MaximumExportedFaults)
            .Select(FormatFault)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (details.Length == 0)
            return KoreanFaultType(record.FaultCode, record.FaultType);

        int remaining = Math.Max(0, faults.Count - details.Length);
        string result = string.Join("; ", details);
        return remaining > 0 ? $"{result}; 외 {remaining}건" : result;
    }

    public static string FormatFault(FaultDetail fault)
    {
        ArgumentNullException.ThrowIfNull(fault);

        string wire = Clean(fault.WireName);
        string standard = StandardConnection(fault);
        string actual = ActualConnection(fault);

        return fault.Type switch
        {
            ProductFaultType.OpenCircuit => Join("단선", wire, standard),
            ProductFaultType.WrongWiring =>
                Join("오배선", wire, Prefix("정상", standard), Prefix("실제", actual)),
            ProductFaultType.ShortCircuit => Join("합선", actual),
            ProductFaultType.ResistanceOutOfRange =>
                Join("저항불량", wire, ResistanceDetail(fault)),
            ProductFaultType.WaterProofLeak => Join("누설불량", wire),
            ProductFaultType.SystemDeviceError => "검사기오류",
            _ => "불량"
        };
    }

    public static string FormatResistanceSummary(string? value)
    {
        string text = Clean(value);
        if (text.Length == 0)
            return string.Empty;

        return text
            .Replace("(PASS)", "(합격)", StringComparison.OrdinalIgnoreCase)
            .Replace("(FAIL)", "(불량)", StringComparison.OrdinalIgnoreCase)
            .Replace("(UNSTABLE)", "(불안정)", StringComparison.OrdinalIgnoreCase)
            .Replace("=OPEN(", "=단선(", StringComparison.OrdinalIgnoreCase);
    }

    private static FaultDetail CreateFallback(TestHistoryRecord record) => new()
    {
        Type = ParseFaultType(record.FaultCode, record.FaultType),
        ExpectedSourceIo = record.ExpectedSourceIo,
        ExpectedTargetIo = record.ExpectedTargetIo,
        ActualSourceIo = record.ActualSourceIo,
        ActualTargetIo = record.ActualTargetIo,
        MeasuredResistance = record.MeasuredResistance,
        ResistanceMin = record.ResistanceMin,
        ResistanceMax = record.ResistanceMax
    };

    private static ProductFaultType ParseFaultType(string? code, string? legacyName = null)
    {
        string value = string.IsNullOrWhiteSpace(code)
            ? Clean(legacyName).ToUpperInvariant()
            : code.Trim().ToUpperInvariant();
        return value switch
        {
            "OPEN_CIRCUIT" or "OPEN CIRCUIT" or "DÂY CHƯA KẾT NỐI" or "HỞ MẠCH" => ProductFaultType.OpenCircuit,
            "WRONG_WIRING" or "WRONG_CONNECTION" or "INCORRECT CONNECTION" or "SAI KẾT NỐI" => ProductFaultType.WrongWiring,
            "SHORT_CIRCUIT" or "SHORT CIRCUIT" or "CHẬP MẠCH" => ProductFaultType.ShortCircuit,
            "RESISTANCE_OUT_OF_RANGE" or "RESISTANCE OUT OF SPECIFICATION" or "ĐIỆN TRỞ KHÔNG ĐẠT" => ProductFaultType.ResistanceOutOfRange,
            "WATERPROOF_LEAK" or "WATERPROOF / LEAK TEST FAILURE" or "KÍN NƯỚC KHÔNG ĐẠT" => ProductFaultType.WaterProofLeak,
            "SYSTEM_DEVICE_ERROR" or "TEST SYSTEM ERROR" => ProductFaultType.SystemDeviceError,
            _ => ProductFaultType.None
        };
    }

    private static string KoreanFaultType(string? code, string? legacyName = null) =>
        ParseFaultType(code, legacyName) switch
    {
        ProductFaultType.OpenCircuit => "단선",
        ProductFaultType.WrongWiring => "오배선",
        ProductFaultType.ShortCircuit => "합선",
        ProductFaultType.ResistanceOutOfRange => "저항불량",
        ProductFaultType.WaterProofLeak => "누설불량",
        ProductFaultType.SystemDeviceError => "검사기오류",
        _ => "불량"
    };

    private static string StandardConnection(FaultDetail fault) => JoinConnection(
        Position(fault.ConnectorFrom, fault.PinFrom, fault.ExpectedSourceIo),
        Position(fault.ConnectorTo, fault.PinTo, fault.ExpectedTargetIo),
        fault.RelatedIos);

    private static string ActualConnection(FaultDetail fault) => JoinConnection(
        Position(fault.ActualConnectorFrom, fault.ActualPinFrom, fault.ActualSourceIo),
        Position(fault.ActualConnectorTo, fault.ActualPinTo, fault.ActualTargetIo),
        fault.RelatedIos);

    private static string Position(string? connector, string? pin, int? io)
    {
        string connectorText = Clean(connector);
        string pinText = Clean(pin);
        if (connectorText.Length > 0 && pinText.Length > 0)
            return $"{connectorText}-{pinText}";
        if (connectorText.Length > 0)
            return connectorText;
        return io is > 0 ? $"IO{io.Value}" : string.Empty;
    }

    private static string JoinConnection(string source, string target, IEnumerable<int> relatedIos)
    {
        if (source.Length > 0 && target.Length > 0)
            return $"{source}↔{target}";
        if (source.Length > 0 || target.Length > 0)
            return source.Length > 0 ? source : target;
        return string.Join("↔", relatedIos.Where(io => io > 0).Distinct().Select(io => $"IO{io}"));
    }

    private static string ResistanceDetail(FaultDetail fault)
    {
        string measured = fault.MeasuredResistance is double value
            ? $"{value.ToString("0.###", CultureInfo.InvariantCulture)}Ω"
            : string.Empty;
        string limits = fault.ResistanceMin is double min && fault.ResistanceMax is double max
            ? $"기준 {min.ToString("0.###", CultureInfo.InvariantCulture)}~{max.ToString("0.###", CultureInfo.InvariantCulture)}Ω"
            : string.Empty;
        return Join(measured, limits);
    }

    private static string Prefix(string prefix, string value) =>
        value.Length == 0 ? string.Empty : $"{prefix} {value}";

    private static string Join(params string[] parts) =>
        string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}
