using JBZUniversalTester.Core;
using JBZUniversalTester.Converters;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
namespace JBZUniversalTester.Models;

public enum FaultKind { Start, Open, MissingConnection, WrongWiring, Short, Resistance, Info, Probe }

/// <summary>Chế độ giải mã cùng stream scan của bo.</summary>
public enum BoardScanMode { Production, Probe }

public sealed class FaultRowCollection : ObservableCollection<FaultRow>
{
    public void ReplaceAll(IReadOnlyList<FaultRow> rows)
    {
        Items.Clear();
        foreach (FaultRow row in rows)
            Items.Add(row);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}

public sealed class FaultRow : ObservableObject
{
    private static readonly Brush PiFieldBrush = Frozen(System.Windows.Media.Color.FromRgb(0xF8, 0xF8, 0xF6));
    private static readonly Brush PiTextBrush = Frozen(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly Brush PiDarkTextBrush = Frozen(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly Brush PiWrongRowBrush = Frozen(System.Windows.Media.Color.FromRgb(0x34, 0x46, 0xA8));
    private static readonly Brush PiProbeRowBrush = Frozen(System.Windows.Media.Color.FromRgb(0xBD, 0xEE, 0xEE));
    private static readonly Brush PiNetworkPassRowBrush = Frozen(System.Windows.Media.Color.FromRgb(0xDF, 0xF4, 0xE3));
    private static readonly Brush PiNetworkPassTextBrush = Frozen(System.Windows.Media.Color.FromRgb(0x14, 0x6B, 0x2E));
    private static readonly Brush PiFailBrush = Frozen(System.Windows.Media.Color.FromRgb(0xC6, 0x28, 0x28));
    private static readonly Brush PiOpenTextBrush = Frozen(System.Windows.Media.Color.FromRgb(0x00, 0x26, 0xD9));
    private static readonly Brush WhiteBrush = Brushes.White;

    string _status = "";
    string? _presentationKey;
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
    public string IoCnPnOverride { get; init; } = "";
    public int DisplayOrder { get; init; } = int.MaxValue;

    // Không bind enum/custom converter trực tiếp trong XAML để WPF Designer
    // có thể render ngay cả trước lần build đầu tiên.
    public string KindName => Kind.ToString();
    public string IoText => Io > 0
        ? Io.ToString(CultureInfo.InvariantCulture)
        : string.Empty;
    public string IoCnPnText => !string.IsNullOrWhiteSpace(IoCnPnOverride)
        ? IoCnPnOverride
        : Io > 0
        ? string.Join("-", new[]
            {
                Io.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(Connector) ? null : Connector.Trim(),
                string.IsNullOrWhiteSpace(Pin) ? null : Pin.Trim()
            }.Where(value => !string.IsNullOrWhiteSpace(value)))
        : string.Empty;
    public string WireColorText => WireColorToBrushConverter.ToDisplayCode(Color);
    public Brush WireColorBrush => WireColorToBrushConverter.ToBrush(Color);
    public Brush WireColorForegroundBrush => ResolveWireColorForeground();
    public Brush Color1Brush => TokenBrush(0);
    public Brush Color2Brush => TokenBrush(1);
    public Brush Color3Brush => TokenBrush(2);
    public Brush Color4Brush => TokenBrush(3);
    public bool HasColor1 => HasColorToken(0);
    public bool HasColor2 => HasColorToken(1);
    public bool HasColor3 => HasColorToken(2);
    public bool HasColor4 => HasColorToken(3);
    public bool IsNetworkPassed =>
        Kind == FaultKind.Info &&
        string.Equals(FaultType, "THÔNG MẠCH", StringComparison.OrdinalIgnoreCase);

    public Brush RowBackgroundBrush => Kind switch
    {
        FaultKind.WrongWiring or FaultKind.Short => PiWrongRowBrush,
        FaultKind.Probe => PiProbeRowBrush,
        FaultKind.Resistance => PiFailBrush,
        FaultKind.Info when IsNetworkPassed => PiNetworkPassRowBrush,
        _ => PiFieldBrush
    };
    public Brush RowForegroundBrush => Kind switch
    {
        FaultKind.WrongWiring or FaultKind.Short or FaultKind.Resistance => WhiteBrush,
        FaultKind.Open or FaultKind.MissingConnection => PiOpenTextBrush,
        FaultKind.Probe => PiDarkTextBrush,
        FaultKind.Info when IsNetworkPassed => PiNetworkPassTextBrush,
        _ => PiTextBrush
    };
    public string ColorName => WireColorToBrushConverter.ToVietnameseName(Color);
    public string ProbeIoText => $"IO ({Io})";
    public string ProbeWireText => string.IsNullOrWhiteSpace(WireName) ? string.Empty : $"Tên dây {WireName}";
    public string ProbeColorText => string.IsNullOrWhiteSpace(ColorName) ? string.Empty : $"Màu {ColorName}";
    public string PresentationKey => _presentationKey ??=
        $"{(int)Kind}|{(int)ProductFaultType}|{Io}|{Connector}|{Pin}|{WireName}|{Splice}|" +
        $"{ExpectedSourceIo}|{ExpectedTargetIo}|{ActualSourceIo}|{ActualTargetIo}";

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

    private Brush TokenBrush(int index)
    {
        IReadOnlyList<string> tokens = WireColorToBrushConverter.Tokenize(Color);
        if (index < 0 || index >= tokens.Count)
            return PiFieldBrush;

        Brush brush = WireColorToBrushConverter.ToTokenBrush(Color, index);
        return brush == Brushes.Transparent ? PiFieldBrush : brush;
    }

    private bool HasColorToken(int index)
    {
        IReadOnlyList<string> tokens = WireColorToBrushConverter.Tokenize(Color);
        return index >= 0 && index < tokens.Count;
    }

    private Brush ResolveWireColorForeground()
    {
        Brush background = Color1Brush;
        if (background is not SolidColorBrush solid || !HasColor1)
            return RowForegroundBrush;

        Color color = solid.Color;
        double luminance =
            (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
        return luminance >= 0.48 ? PiDarkTextBrush : WhiteBrush;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }
}

public sealed class ResistanceResult : ObservableObject
{
    private const double KiloOhm = 1_000;
    private const double MegaOhm = 1_000_000;

    private double? _valueOhm;
    private bool _isOpen;
    private bool _isMeasured;
    private bool _isStable;
    private bool _passed;
    private string _measurementStatus = "—";

    public string Name { get; init; } = "";
    public int Channel { get; init; }
    public string ChannelText => Channel > 0 ? $"CH{Channel}" : string.Empty;
    public double? ValueOhm
    {
        get => _valueOhm;
        set
        {
            if (Set(ref _valueOhm, value))
            {
                if (value is not null && !_isMeasured)
                    _isMeasured = true;
                Raise(nameof(Display));
                Raise(nameof(MinDisplayText));
                Raise(nameof(MaxDisplayText));
                Raise(nameof(DisplayUnitText));
                Raise(nameof(ResultText));
            }
        }
    }
    public double MinOhm { get; init; }
    public double MaxOhm { get; init; }
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (Set(ref _isOpen, value))
            {
                if (value)
                    _isMeasured = true;
                Raise(nameof(Display));
                Raise(nameof(MinDisplayText));
                Raise(nameof(MaxDisplayText));
                Raise(nameof(DisplayUnitText));
                Raise(nameof(ResultText));
            }
        }
    }
    public bool IsStable
    {
        get => _isStable;
        set
        {
            if (Set(ref _isStable, value))
                Raise(nameof(ResultText));
        }
    }
    public bool Passed
    {
        get => _passed;
        set
        {
            if (Set(ref _passed, value))
                Raise(nameof(ResultText));
        }
    }
    public string MeasurementStatus
    {
        get => _measurementStatus;
        set
        {
            if (Set(ref _measurementStatus, string.IsNullOrWhiteSpace(value) ? "—" : value))
                Raise(nameof(ResultText));
        }
    }
    public int SampleCount { get; set; }
    public long StabilizationTimeMs { get; set; }
    public string MinDisplayText => FormatOhm(MinOhm, DisplayScale);
    public string MaxDisplayText => FormatOhm(MaxOhm, DisplayScale);
    public string DisplayUnitText => DisplayScale.UnitLog;
    public string Display => !_isMeasured ? "—" : IsOpen ? "OPEN" : ValueOhm is null ? "—" : FormatOhm(ValueOhm.Value, DisplayScale);
    public string ResultText => !_isMeasured && MeasurementStatus != "—" ? MeasurementStatus : !_isMeasured ? "—" : MeasurementStatus == "UNSTABLE" ? "UNSTABLE" : Passed ? "PASS" : "FAIL";

    private ResistanceDisplayScale DisplayScale => ChooseDisplayScale(ValueOhm, MinOhm, MaxOhm);

    private static ResistanceDisplayScale ChooseDisplayScale(params double?[] values)
    {
        double maximum = values
            .Where(value => value is double finite && double.IsFinite(finite))
            .Select(value => Math.Abs(value!.Value))
            .DefaultIfEmpty(0)
            .Max();

        if (maximum >= MegaOhm)
            return new ResistanceDisplayScale(MegaOhm, "MΩ", "MOhm", "0.000");
        if (maximum >= KiloOhm)
            return new ResistanceDisplayScale(KiloOhm, "kΩ", "kOhm", "0.000");
        return new ResistanceDisplayScale(1, "Ω", "Ohm", "0.00");
    }

    private static string FormatOhm(double valueOhm, ResistanceDisplayScale scale) =>
        (valueOhm / scale.Divisor).ToString(scale.Format, CultureInfo.InvariantCulture) + " " + scale.Unit;

    private readonly record struct ResistanceDisplayScale(
        double Divisor,
        string Unit,
        string UnitLog,
        string Format);
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
    BoardScanMode Mode = BoardScanMode.Production,
    int ExpectedIoCount = 0,
    int SourceCount = 0,
    byte? EndMarkerCode = null,
    int ScanUnitCount = 0,
    bool TerminatorKnown = true,
    long ScanGeneration = 0)
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
