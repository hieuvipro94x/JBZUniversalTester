using System.IO;
using System.Text.Json;

namespace JBZUniversalTester.Models;

public sealed class ProductBundle
{
    public string SourcePath { get; init; } = string.Empty;
    public string PartNumber { get; init; } = string.Empty;
    public string D2xxThtPath { get; init; } = string.Empty;

    public static ProductBundle Load(string path)
    {
        string full=Path.GetFullPath(path); using JsonDocument doc=JsonDocument.Parse(File.ReadAllText(full)); JsonElement r=doc.RootElement;
        string dir=Path.GetDirectoryName(full)!;
        string Resolve(string value)=>string.IsNullOrWhiteSpace(value)?string.Empty:Path.GetFullPath(Path.IsPathRooted(value)?value:Path.Combine(dir,value));
        string d2="";
        if(r.TryGetProperty("d2xx",out var d)&&d.TryGetProperty("tht",out var t)) d2=Resolve(t.GetString()??"");
        return new ProductBundle { SourcePath=full, PartNumber=r.TryGetProperty("partNumber",out var pn)?pn.GetString()??Path.GetFileNameWithoutExtension(full):Path.GetFileNameWithoutExtension(full), D2xxThtPath=d2 };
    }
}
