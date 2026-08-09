using System.Text.Json;

namespace JBZUniversalTester.Models;

public enum ProductFaultType
{
    None = 0,
    OpenCircuit = 1,
    WrongWiring = 2,
    ShortCircuit = 3,
    ResistanceOutOfRange = 4,
    SystemDeviceError = 100
}

public static class FaultTypeCatalog
{
    public static string Code(ProductFaultType type) => type switch
    {
        ProductFaultType.OpenCircuit => "OPEN_CIRCUIT",
        ProductFaultType.WrongWiring => "WRONG_WIRING",
        ProductFaultType.ShortCircuit => "SHORT_CIRCUIT",
        ProductFaultType.ResistanceOutOfRange => "RESISTANCE_OUT_OF_RANGE",
        ProductFaultType.SystemDeviceError => "SYSTEM_DEVICE_ERROR",
        _ => "NONE"
    };

    public static string DisplayName(ProductFaultType type) => type switch
    {
        ProductFaultType.OpenCircuit => "DÂY CHƯA KẾT NỐI",
        ProductFaultType.WrongWiring => "ĐẤU SAI",
        ProductFaultType.ShortCircuit => "CHẬP MẠCH",
        ProductFaultType.ResistanceOutOfRange => "ĐIỆN TRỞ KHÔNG ĐẠT",
        ProductFaultType.SystemDeviceError => "LỖI THIẾT BỊ / HỆ THỐNG",
        _ => string.Empty
    };

    public static int Priority(ProductFaultType type) => type switch
    {
        ProductFaultType.ShortCircuit => 0,
        ProductFaultType.WrongWiring => 1,
        ProductFaultType.OpenCircuit => 2,
        ProductFaultType.ResistanceOutOfRange => 3,
        _ => 99
    };
}

public sealed class FaultDetail
{
    public ProductFaultType Type { get; set; }
    public string Code => FaultTypeCatalog.Code(Type);
    public string Name => FaultTypeCatalog.DisplayName(Type);
    public int? ExpectedSourceIo { get; set; }
    public int? ExpectedTargetIo { get; set; }
    public int? ActualSourceIo { get; set; }
    public int? ActualTargetIo { get; set; }
    public int[] RelatedIos { get; set; } = [];
    public string ConnectorFrom { get; set; } = string.Empty;
    public string PinFrom { get; set; } = string.Empty;
    public string ConnectorTo { get; set; } = string.Empty;
    public string PinTo { get; set; } = string.Empty;
    // Với Wrong Wiring, ConnectorTo/PinTo là đích MONG ĐỢI; các trường
    // Actual* giữ metadata của đích THỰC TẾ để ErrorLog/FaultDetailsJson
    // vẫn đọc lại được chính xác ngay cả khi THT sau này thay đổi.
    public string ActualConnectorFrom { get; set; } = string.Empty;
    public string ActualPinFrom { get; set; } = string.Empty;
    public string ActualConnectorTo { get; set; } = string.Empty;
    public string ActualPinTo { get; set; } = string.Empty;
    public string WireName { get; set; } = string.Empty;
    public string WireColor { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double? MeasuredResistance { get; set; }
    public double? ResistanceMin { get; set; }
    public double? ResistanceMax { get; set; }

    public string ExpectedText => ExpectedSourceIo is int source && ExpectedTargetIo is int target
        ? $"IO {source} → IO {target}"
        : string.Empty;

    public string ActualText => ActualSourceIo is int source && ActualTargetIo is int target
        ? $"IO {source} → IO {target}"
        : RelatedIos.Length > 1
            ? string.Join(" ↔ ", RelatedIos.Select(io => $"IO {io}"))
            : string.Empty;

    public string Summary
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Message))
                return Message;
            if (!string.IsNullOrWhiteSpace(ExpectedText) && !string.IsNullOrWhiteSpace(ActualText))
                return $"Mong đợi: {ExpectedText} | Thực tế: {ActualText}";
            if (!string.IsNullOrWhiteSpace(ExpectedText))
                return ExpectedText;
            return ActualText;
        }
    }
}

public sealed class CompletedTestResult
{
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public bool Passed { get; set; }
    public string ResultText { get; set; } = string.Empty;
    public IReadOnlyList<FaultDetail> Faults { get; set; } = [];
    public IReadOnlyList<ResistanceResult> Resistance { get; set; } = [];

    public FaultDetail? PrimaryFault => Faults
        .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
        .FirstOrDefault();

    public string FaultDetailsJson => JsonSerializer.Serialize(Faults);
}
