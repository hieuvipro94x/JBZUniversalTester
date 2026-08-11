using System.IO;
using System.Text;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Models;

public sealed record PiLegacyPin(
    int RowIndex,
    int PhysicalPin,
    string Connector,
    string LocalPin,
    string LineName,
    string Splice,
    string Gauge,
    string Color,
    string Parent,
    IReadOnlyList<int> Targets,
    string[] Fields);

public sealed class PiLegacyModel
{
    public string SourcePath { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ProductNo { get; init; } = string.Empty;
    public string VehicleType { get; init; } = string.Empty;
    public IReadOnlyList<string> ConnectorNames { get; init; } = [];
    public IReadOnlyList<int> ConnectorPinCounts { get; init; } = [];
    public IReadOnlyList<PiLegacyPin> Pins { get; init; } = [];

    public int PhysicalPinCount => Pins.Count;

    public ProductModel ToProductModel()
    {
        var result = new ProductModel
        {
            ModelName = ModelName,
            PartNumber = ProductNo.Length > 0 ? ProductNo : ModelName,
            ProductName = DisplayName,
            VehicleType = VehicleType,
            SourcePath = SourcePath
        };
        result.Pins = Pins.Select(p => new PinRecord(
            p.Connector,
            p.LineName,
            p.PhysicalPin,
            p.LocalPin,
            p.Splice,
            p.Gauge,
            p.Color)).ToList();
        foreach (PiLegacyPin source in Pins.Where(p => p.Parent is "-1" or ""))
        {
            if (source.Targets.Count == 0) continue;
            var ios = new List<int> { source.PhysicalPin };
            ios.AddRange(source.Targets);
            var pins = ios.Select(io => result.Pins.FirstOrDefault(p => p.IoNumber == io))
                .Where(p => p is not null).Cast<PinRecord>().ToList();
            result.Nets.Add(new WireNet(source.LineName, ios, pins));
        }
        return result;
    }
}

public static class PiLegacyModelParser
{
    public static PiLegacyModel Load(string path)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Không tìm thấy file .model của Pi.", full);
        IniLite ini = IniLite.Load(full);
        foreach (string required in new[] { "Common", "Connector", "Pin" })
            if (!ini.HasSection(required)) throw new InvalidDataException($"File .model thiếu [{required}].");

        string fileStem = Path.GetFileNameWithoutExtension(full).Trim();
        // Golden Pi compiler dùng STEM của tên file làm :MODEL, không dùng Common/Model.
        // Ví dụ 1.model có Common/Model=111 nhưng firmware nhận :MODEL,1.
        string modelName = fileStem;
        int connectorCount = ParseInt(ini.Get("Connector", "Count"), "Connector/Count");
        int pinCount = ParseInt(ini.Get("Pin", "Count"), "Pin/Count");

        var connectorNames = new List<string>();
        var connectorPinCounts = new List<int>();
        for (int i=1;i<=connectorCount;i++)
        {
            string value = ini.Get("Connector", $"C{i}");
            if (value.Length == 0) throw new InvalidDataException($"Thiếu Connector/C{i}.");
            string[] parts = value.Split('|');
            if (parts.Length < 2) throw new InvalidDataException($"Connector/C{i} không hợp lệ.");
            connectorNames.Add(parts[0].Trim());
            connectorPinCounts.Add(ParseInt(parts[1], $"Connector/C{i} pin count"));
        }
        if (connectorPinCounts.Sum() != pinCount)
            throw new InvalidDataException($"Tổng pin connector={connectorPinCounts.Sum()} nhưng Pin/Count={pinCount}.");

        var pins = new List<PiLegacyPin>();
        var seen = new HashSet<int>();
        for (int i=1;i<=pinCount;i++)
        {
            string value = ini.Get("Pin", $"P{i}");
            if (value.Length == 0) throw new InvalidDataException($"Thiếu Pin/P{i}.");
            string[] f = value.Split('|');
            if (f.Length != 10) throw new InvalidDataException($"Pin/P{i} phải có 10 trường, nhận {f.Length}.");
            int physical = ParseInt(f[0], $"Pin/P{i} physical");
            if (!seen.Add(physical)) throw new InvalidDataException($"Physical pin bị trùng: {physical}.");
            if (!connectorNames.Contains(f[1].Trim(), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException($"Pin/P{i} dùng connector không tồn tại: {f[1]}.");
            var targets = new List<int>();
            foreach (string token in f[9].Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                targets.Add(ParseInt(token, $"Pin/P{i} target"));
            pins.Add(new PiLegacyPin(i, physical, f[1].Trim(), f[2].Trim(), f[3].Trim(),
                f[4].Trim(), ReadGauge(f), ReadColor(f), f[8].Trim(), targets, f));
        }

        var physicalSet = pins.Select(p=>p.PhysicalPin).ToHashSet();
        foreach (PiLegacyPin p in pins)
        {
            foreach (int target in p.Targets)
                if (!physicalSet.Contains(target)) throw new InvalidDataException($"P{p.RowIndex} target {target} không tồn tại.");
            if (p.Parent.Length > 0 && p.Parent != "-1")
            {
                int parent = ParseInt(p.Parent, $"P{p.RowIndex} parent");
                if (!physicalSet.Contains(parent)) throw new InvalidDataException($"P{p.RowIndex} parent {parent} không tồn tại.");
            }
        }

        return new PiLegacyModel
        {
            SourcePath = full,
            ModelName = modelName,
            DisplayName = ini.Get("Common", "Name").Trim(),
            ProductNo = FirstNonEmpty(ini.Get("Common", "No"), ini.Get("Common", "Customer"), fileStem),
            VehicleType = ini.Get("Common", "Kind").Trim(),
            ConnectorNames = connectorNames,
            ConnectorPinCounts = connectorPinCounts,
            Pins = pins
        };
    }

    static int ParseInt(string value, string label) => int.TryParse(value.Trim(), out int n)
        ? n : throw new InvalidDataException($"{label} không phải số nguyên: '{value}'.");
    static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
    static string ReadGauge(string[] f) => f.Length > 5 ? f[5].Trim().Replace(',', '.') : string.Empty;
    static string ReadColor(string[] f)
    {
        foreach (int idx in new[] {6,7}) if (f.Length > idx && !string.IsNullOrWhiteSpace(f[idx])) return f[idx].Trim();
        return string.Empty;
    }
}

public sealed class PiSetupProfile
{
    public string SourcePath { get; init; } = string.Empty;
    public string LinkedModelPath { get; init; } = string.Empty;
    public IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>> Sections { get; init; } = new Dictionary<string,IReadOnlyDictionary<string,string>>();
    public int PinChangeCount => TryInt("PinChange","Count");
    public int PinChangeCurrent => TryInt("PinChange","Current");
    public bool BarcodeEnabled => Get("Barcode","Use") == "1";
    public bool PreTestEnabled => Get("PreTest","Use") == "1";
    public string Get(string section,string key) => Sections.TryGetValue(section,out var values) && values.TryGetValue(key,out string? v) ? v : string.Empty;
    int TryInt(string s,string k)=>int.TryParse(Get(s,k),out int n)?n:0;

    public static PiSetupProfile Load(string path)
    {
        string full=Path.GetFullPath(path);
        IniLite ini=IniLite.Load(full);
        return new PiSetupProfile
        {
            SourcePath=full,
            LinkedModelPath=ini.Get("Common","Model").Trim(),
            Sections=ini.Export()
        };
    }
}

internal sealed class IniLite
{
    readonly Dictionary<string,Dictionary<string,string>> _sections=new(StringComparer.OrdinalIgnoreCase);
    public bool HasSection(string s)=>_sections.ContainsKey(s);
    public string Get(string s,string k)=>_sections.TryGetValue(s,out var d)&&d.TryGetValue(k,out string? v)?v:string.Empty;
    public IReadOnlyDictionary<string,IReadOnlyDictionary<string,string>> Export()=>_sections.ToDictionary(k=>k.Key,v=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>(v.Value,StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
    public static IniLite Load(string path)
    {
        byte[] bytes=File.ReadAllBytes(path); string text=Decode(bytes); var ini=new IniLite(); Dictionary<string,string>? current=null;
        // UTF-8 BOM đứng trước section đầu tiên sẽ biến "[Common]" thành
        // "\uFEFF[Common]" và làm parser báo thiếu section dù file hợp lệ.
        text = text.TrimStart('\uFEFF');
        foreach(string raw in text.Replace("\r\n","\n").Split('\n'))
        {
            string line=raw.Trim(); if(line.Length==0||line.StartsWith(';')||line.StartsWith('#')) continue;
            if(line.StartsWith('[')&&line.EndsWith(']')) { string name=line[1..^1].Trim(); current=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); ini._sections[name]=current; continue; }
            int eq=line.IndexOf('='); if(eq<0||current is null) continue; current[line[..eq].Trim()]=line[(eq+1)..].Trim();
        }
        return ini;
    }
    static string Decode(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach(int cp in new[]{65001,949,51949,1252,1258}) { try { return Encoding.GetEncoding(cp,new EncoderExceptionFallback(),new DecoderExceptionFallback()).GetString(bytes); } catch {} }
        return Encoding.Latin1.GetString(bytes);
    }
}
