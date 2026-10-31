using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace JBZUniversalTester.ModelEditor.Models;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BaseInfo
{
    public string Model { get; set; } = "";
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string EoNo { get; set; } = "";
    public string Customer { get; set; } = "";
    public int UseCustomer { get; set; }
    public BaseInfo Clone() => (BaseInfo)MemberwiseClone();
}

public sealed class Connector
{
    public string Name { get; set; } = "";
    public int PinCount { get; set; }
    public int StartIo { get; set; }
    public bool Waterproof { get; set; }
    public int LeakChannel { get; set; }
    public Connector Clone() => (Connector)MemberwiseClone();
}

public sealed class Pin
{
    public int Io { get; set; }
    public string Connector { get; set; } = "";
    public int PinNo { get; set; }
    public string Line { get; set; } = "";
    public string Add { get; set; } = "";
    public string Thickness { get; set; } = "";
    public string Color { get; set; } = "";
    public string TestNo { get; set; } = "";
    public int SourceThtRowIndex { get; set; } = -1;
    public int MainIo { get; set; } = -1;
    public List<int> Coupling { get; set; } = [];
    public bool First => MainIo == -1;
    public Pin Clone() => new()
    {
        Io = Io,
        Connector = Connector,
        PinNo = PinNo,
        Line = Line,
        Add = Add,
        Thickness = Thickness,
        Color = Color,
        TestNo = TestNo,
        SourceThtRowIndex = SourceThtRowIndex,
        MainIo = MainIo,
        Coupling = [.. Coupling]
    };
}

public sealed class WireColor
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Value { get; set; } = "#FFFFFF";
    public WireColor Clone() => (WireColor)MemberwiseClone();
}

public sealed class ThtState
{
    public string Version { get; set; } = "V1.110";
    public uint Token { get; set; } = 0x389DEFB9;
    public List<string> ProductRow { get; set; } = [];
    public List<List<string>> ConnectorRows { get; set; } = [];
    public List<List<string>> PinRows { get; set; } = [];
    public List<List<string>> WireRows { get; set; } = [];
    public byte[] Tail { get; set; } = [];
    public byte[] SummaryInformation { get; set; } = [];

    // Khi mở một THT gốc, giữ nguyên toàn bộ container OLE và stream Contents.
    // Save sẽ vá đúng stream Contents vào container này thay vì dựng OLE mới.
    public byte[] OriginalFileBytes { get; set; } = [];
    public byte[] OriginalContents { get; set; } = [];

    public ThtState Clone() => new()
    {
        Version = Version,
        Token = Token,
        ProductRow = [.. ProductRow],
        ConnectorRows = ConnectorRows.Select(r => new List<string>(r)).ToList(),
        PinRows = PinRows.Select(r => new List<string>(r)).ToList(),
        WireRows = WireRows.Select(r => new List<string>(r)).ToList(),
        Tail = [.. Tail],
        SummaryInformation = [.. SummaryInformation],
        OriginalFileBytes = [.. OriginalFileBytes],
        OriginalContents = [.. OriginalContents]
    };
}

public sealed class ModelDocument
{
    public BaseInfo Base { get; set; } = new();
    public List<Connector> Connectors { get; set; } = [];
    public List<Pin> Pins { get; set; } = [];
    public bool UseImage { get; set; }
    public string ImageFile { get; set; } = "";
    public string? SourcePath { get; set; }
    public Dictionary<string, string> CommonExtras { get; set; } = new(StringComparer.Ordinal);
    public string SourceFormat { get; set; } = "model";
    public ThtState? ThtState { get; set; }

    public ModelDocument Clone() => new()
    {
        Base = Base.Clone(),
        Connectors = Connectors.Select(c => c.Clone()).ToList(),
        Pins = Pins.Select(p => p.Clone()).ToList(),
        UseImage = UseImage,
        ImageFile = ImageFile,
        SourcePath = SourcePath,
        CommonExtras = new Dictionary<string, string>(CommonExtras, StringComparer.Ordinal),
        SourceFormat = SourceFormat,
        ThtState = ThtState?.Clone()
    };
}

public sealed class ValidationException(string message) : Exception(message);
public sealed class ModelFormatException(string message) : Exception(message);
public sealed class ThtFormatException(string message) : Exception(message);
public sealed class ThtExportException(string message) : Exception(message);
