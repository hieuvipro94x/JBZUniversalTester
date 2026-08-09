namespace JBZUniversalTester.Models;

public sealed class TestHistoryRecord
{
    private CustomerFaultDisplay? _customerFault;

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
    public string CycleId { get; set; } = string.Empty;
    public string LabelSerial { get; set; } = string.Empty;
    public string BarcodeValue { get; set; } = string.Empty;
    public string LabelProfile { get; set; } = string.Empty;
    public string PrintStatus { get; set; } = LabelPrintStatus.NotRequested.ToString();
    public DateTime? PrintTimestamp { get; set; }
    public string Printer { get; set; } = string.Empty;
    public int LabelCopies { get; set; }
    public int ReprintCount { get; set; }
    public string PrintMessage { get; set; } = string.Empty;

    public string DateText => Finished.ToString("yyyy/MM/dd");
    public string TimeText => Finished.ToString("HH:mm:ss");
    public string PassedText => Passed ? "PASS" : "FAIL";
    public string ExpectedConnectionText => ExpectedSourceIo is int source && ExpectedTargetIo is int target
        ? $"IO {source} → IO {target}"
        : string.Empty;
    public string ActualConnectionText => ActualSourceIo is int source && ActualTargetIo is int target
        ? $"IO {source} → IO {target}"
        : string.Empty;

    public CustomerFaultDisplay CustomerFault =>
        _customerFault ??= FaultDisplayFormatter.FormatCustomer(this);
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

public enum LabelPrintStatus
{
    NotRequested,
    Pending,
    Printed,
    Failed,
    Unknown
}

/// <summary>
/// Immutable snapshot created at PASS confirmation. Printing must never read the
/// mutable current model or Settings UI after this request has been created.
/// </summary>
public sealed record LabelPrintRequest(
    string CycleId,
    LabelPrintData Data,
    string ModelName,
    string ModelFile,
    string PrinterName,
    string PrinterCom,
    int WidthMm,
    int HeightMm,
    string FormatName,
    int BaudRate,
    int WriteTimeoutMs,
    int Copies)
{
    public string Printer => !string.IsNullOrWhiteSpace(PrinterCom)
        ? PrinterCom.Trim()
        : PrinterName.Trim();

    public static LabelPrintRequest Capture(TestHistoryRecord history, LabelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(settings);

        return new LabelPrintRequest(
            history.CycleId,
            new LabelPrintData(
                history.PartName,
                history.PartNumber,
                history.Eco,
                history.Nco,
                history.Alc,
                history.LotNo,
                history.Finished),
            history.ModelName,
            history.ModelFile,
            settings.PrinterName ?? string.Empty,
            settings.PrinterCom ?? string.Empty,
            Math.Clamp(settings.WidthMm, 20, 200),
            Math.Clamp(settings.HeightMm, 10, 150),
            settings.FormatName ?? string.Empty,
            Math.Clamp(settings.BaudRate, 1200, 921600),
            Math.Clamp(settings.WriteTimeoutMs, 500, 30_000),
            Math.Clamp(settings.Copies, 1, 20));
    }
}

public sealed record LabelIdentity(string SerialText, string BarcodeValue);

public sealed record LabelPrintTransportResult(bool Printed, string Message);
