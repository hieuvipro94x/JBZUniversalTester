using JBZUniversalTester.Core;
using JBZUniversalTester.Converters;
using System.Windows.Media;
namespace JBZUniversalTester.Models;

public enum FaultKind { Start, Open, WrongWiring, Short, Resistance, Info, Probe }

/// <summary>Chế độ giải mã cùng stream scan của bo.</summary>
public enum BoardScanMode { Production, Probe }

public sealed class FaultRow : ObservableObject
{
    string _status = "";
    public FaultKind Kind { get; init; }
    public ProductFaultType ProductFaultType { get; init; } = ProductFaultType.None;
    public string FaultCode => FaultTypeCatalog.Code(ProductFaultType);
    public string FaultType { get; init; } = "";
    public int Io { get; init; }
    public int? ExpectedSourceIo { get; init; }
    public int? ExpectedTargetIo { get; init; }
    public int? ActualSourceIo { get; init; }
    public int? ActualTargetIo { get; init; }
    public int[] RelatedIos { get; init; } = [];
    public string Connector { get; init; } = "";
    public string Pin { get; init; } = "";
    public string WireName { get; init; } = "";
    public string Splice { get; init; } = "";
    public string Section { get; init; } = "";
    public string Color { get; init; } = "";

    // Không bind enum/custom converter trực tiếp trong XAML để WPF Designer
    // có thể render ngay cả trước lần build đầu tiên.
    public string KindName => Kind.ToString();
    public Brush WireColorBrush => WireColorToBrushConverter.ToBrush(Color);
    public string ColorName => WireColorToBrushConverter.ToVietnameseName(Color);
    public string ProbeIoText => $"IO ({Io})";
    public string ProbeWireText => string.IsNullOrWhiteSpace(WireName) ? string.Empty : $"Tên dây {WireName}";
    public string ProbeColorText => string.IsNullOrWhiteSpace(ColorName) ? string.Empty : $"Màu {ColorName}";

    public FaultDetail ToFaultDetail() => new()
    {
        Type = ProductFaultType,
        ExpectedSourceIo = ExpectedSourceIo,
        ExpectedTargetIo = ExpectedTargetIo,
        ActualSourceIo = ActualSourceIo,
        ActualTargetIo = ActualTargetIo,
        RelatedIos = RelatedIos.Length > 0
            ? RelatedIos
            : new[] { ExpectedSourceIo, ExpectedTargetIo, ActualSourceIo, ActualTargetIo, Io }
                .Where(value => value.HasValue && value.Value > 0)
                .Select(value => value!.Value)
                .Distinct()
                .ToArray(),
        ConnectorFrom = Connector,
        PinFrom = Pin,
        WireName = WireName,
        WireColor = Color,
        Message = Status
    };

    public string Status { get => _status; set => Set(ref _status, value); }
}

public sealed class ResistanceResult : ObservableObject
{
    public string Name { get; init; } = "";
    public int Channel { get; init; }
    public double? ValueOhm { get; set; }
    public double MinOhm { get; init; }
    public double MaxOhm { get; init; }
    public bool IsOpen { get; set; }
    public bool Passed { get; set; }
    public string Display => IsOpen ? "∞" : ValueOhm is null ? "—" : ValueOhm >= 1_000_000 ? $"{ValueOhm / 1_000_000:0.000} MΩ" : ValueOhm >= 1000 ? $"{ValueOhm / 1000:0.000} kΩ" : $"{ValueOhm:0.000} Ω";
    public string ResultText => IsOpen ? "OPEN" : Passed ? "PASS" : "FAIL";
}

public sealed record ScanFrame(
    DateTime Timestamp,
    int CardNumber,
    IReadOnlySet<int> ActiveIo,
    byte[] Raw,
    bool Complete = true,
    int UnknownBytes = 0,
    long Sequence = 0,
    IReadOnlyDictionary<int, IReadOnlySet<int>>? ConnectionsBySource = null,
    IReadOnlyDictionary<int, int>? TargetHitCounts = null,
    BoardScanMode Mode = BoardScanMode.Production)
{
    public IReadOnlyDictionary<int, IReadOnlySet<int>> Connections =>
        ConnectionsBySource ?? new Dictionary<int, IReadOnlySet<int>>();

    public IReadOnlyDictionary<int, int> TargetHits =>
        TargetHitCounts ?? new Dictionary<int, int>();
}

public sealed record WiringFaultPair(
    int SourceIo,
    int TargetIo,
    string Reason,
    ProductFaultType FaultType = ProductFaultType.WrongWiring,
    int? ExpectedSourceIo = null,
    int? ExpectedTargetIo = null);

public sealed record BoardConnectionInfo(string Description, string SerialNumber);
public sealed record TestSummary(DateTime Started, DateTime Finished, string Model, string Barcode, bool Passed, int OpenCount, int WrongCount, int ShortCount, IReadOnlyList<ResistanceResult> Resistance);
