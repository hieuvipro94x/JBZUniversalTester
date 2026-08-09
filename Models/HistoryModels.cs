namespace JBZUniversalTester.Models;

public sealed class TestHistoryRecord
{
    public long Id { get; set; }
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public string PartName { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string Eco { get; set; } = string.Empty;
    public string Nco { get; set; } = string.Empty;
    public string Alc { get; set; } = string.Empty;
    public long LotNo { get; set; }
    public string Result { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string ModelFile { get; set; } = string.Empty;
    public string HtdrvName { get; set; } = string.Empty;
    public int OpenCount { get; set; }
    public int WrongCount { get; set; }
    public int ShortCount { get; set; }
    public string Resistance { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceNumber { get; set; } = string.Empty;
    public string OperatorCompany { get; set; } = string.Empty;
    public string ProductionLine { get; set; } = string.Empty;
    public string FaultType { get; set; } = string.Empty;
    public string FaultCode { get; set; } = string.Empty;
    public int? ExpectedSourceIo { get; set; }
    public int? ExpectedTargetIo { get; set; }
    public int? ActualSourceIo { get; set; }
    public int? ActualTargetIo { get; set; }
    public string FaultDetailsJson { get; set; } = string.Empty;
    public string FaultSummary { get; set; } = string.Empty;
    public double? MeasuredResistance { get; set; }
    public double? ResistanceMin { get; set; }
    public double? ResistanceMax { get; set; }

    public string DateText => Finished.ToString("yyyy/MM/dd");
    public string TimeText => Finished.ToString("HH:mm:ss");
    public string PassedText => Passed ? "PASS" : "FAIL";
    public string ExpectedConnectionText => ExpectedSourceIo is int source && ExpectedTargetIo is int target
        ? $"IO {source} → IO {target}"
        : string.Empty;
    public string ActualConnectionText => ActualSourceIo is int source && ActualTargetIo is int target
        ? $"IO {source} → IO {target}"
        : string.Empty;
}

public sealed record HistorySearchCriteria(
    DateTime? From,
    DateTime? To,
    long? LotNo,
    string PartKeyword,
    string Result,
    int MaxRows = 5000);

public sealed record LabelPrintData(
    string PartName,
    string PartNumber,
    string Eco,
    string Nco,
    string Alc,
    long LotNo,
    DateTime TestedAt);
