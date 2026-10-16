using System.Globalization;

namespace JBZUniversalTester.Models;

public static class MasterHistoryPresentation
{
    public static string InspectionName(TestHistoryRecord record)
    {
        if (string.Equals(record.InspectionType, HistoryInspectionType.MasterGood, StringComparison.OrdinalIgnoreCase))
            return "정상 마스터 검사";

        // Read the actual sample field, never the list of all requested samples.
        string? sample = (record.InspectionTrace ?? string.Empty).Split('|').Select(part => part.Trim())
            .FirstOrDefault(part => part.StartsWith("Mẫu NG - ", StringComparison.OrdinalIgnoreCase));
        foreach (MasterSampleType type in MasterSampleCatalog.RequiredFaultSamples)
            if (sample?.Equals("Mẫu NG - " + MasterSampleCatalog.Name(type), StringComparison.OrdinalIgnoreCase) == true)
                return type switch
                {
                    MasterSampleType.ShortCircuit => "단락 마스터 검사",
                    MasterSampleType.OpenCircuit => "핀 빠짐 / 단선 마스터 검사",
                    _ => "오배선 마스터 검사"
                };
        return "불량 마스터 검사";
    }

    public static string VisibleAudit(string trace) => KoreanHistoryFormatter.FormatMasterTrace(trace)
        .Replace("Phiên ", "세션 ", StringComparison.Ordinal)
        .Replace("không xác định", "미확인", StringComparison.Ordinal)
        .Replace("Mẫu NG", "불량 마스터", StringComparison.Ordinal);

    public static string BuildLog(TestHistoryRecord record)
    {
        var lines = new List<string>
        {
            "작업 시작 전 마스터 검사 • " + InspectionName(record),
            $"장착: {Time(record.InstallStartedAt ?? record.Started)} → {Time(record.TestStartedAt)}" +
                Duration(record.InstallDurationSeconds),
            $"검사 시작: {Time(record.TestStartedAt)}",
            $"확인 {(record.Passed ? "합격" : "불합격")}: {Time(record.ResultAt)}" +
                " • 검사/확인 시간" + Duration(record.TestDurationSeconds),
            "총 장착/검사 시간" + Duration(record.InstallAndTestDurationSeconds),
            $"탈거: {Time(record.RemovalStartedAt)} → {Time(record.RemovedAt)}" +
                Duration(record.RemovalDurationSeconds)
        };
        if (record.ResultAt is null)
            lines.Add($"기존 기록 종료시각: {Time(record.Finished)} (별도 확인시각 없음)");
        if (!string.IsNullOrWhiteSpace(record.InspectionTrace))
            lines.Add(VisibleAudit(record.InspectionTrace));
        IReadOnlyList<FaultDetail> faults = FaultDisplayFormatter.DeserializeFaults(record.FaultDetailsJson);
        string details = string.Join("; ", faults.Select(KoreanHistoryFormatter.FormatFault));
        if (!string.IsNullOrWhiteSpace(details))
            lines.Add("불량 상세: " + details);
        else if (!string.IsNullOrWhiteSpace(record.FaultSummary))
            lines.Add("불량 상세: " + KoreanHistoryFormatter.FormatFaults(record));
        if (!string.IsNullOrWhiteSpace(record.Resistance))
            lines.Add("저항: " + KoreanHistoryFormatter.FormatResistanceSummary(record.Resistance));
        return string.Join(Environment.NewLine, lines);
    }

    // Preserve the customer export language and column layout. All timestamps
    // come from stored stage fields; missing legacy stages stay unconfirmed.
    public static string ExportTiming(TestHistoryRecord record) =>
        $"장착시작 {ExportTime(record.InstallStartedAt ?? record.Started)} " +
        $"장착시간 {Seconds(record.InstallDurationSeconds)} " +
        $"검사시작 {ExportTime(record.TestStartedAt)} " +
        $"확인시각 {ExportTime(record.ResultAt)} " +
        $"확인시간 {Seconds(record.TestDurationSeconds)} | ";

    private static string Time(DateTime? value) =>
        value?.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "—";
    private static string ExportTime(DateTime? value) => value.HasValue ? Time(value) : "미확인";
    private static string Seconds(double? value) =>
        value?.ToString("0.000", CultureInfo.InvariantCulture) + (value.HasValue ? "초" : "미확인");
    private static string Duration(double? value) => value.HasValue
        ? $" ({value.Value.ToString("0.000", CultureInfo.InvariantCulture)}초)"
        : " (기록 미완료)";
}
