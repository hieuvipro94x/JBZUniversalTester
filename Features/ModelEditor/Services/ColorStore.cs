using System.Text;
using JBZUniversalTester.ModelEditor.Models;
using System.IO;
namespace JBZUniversalTester.ModelEditor.Services;

public sealed class ColorStore
{
    public static readonly IReadOnlyList<WireColor> Defaults = new[]
    {
        new WireColor { Code="B", Name="Đen", Value="#000000" },
        new WireColor { Code="W", Name="Trắng", Value="#FFFFFF" },
        new WireColor { Code="R", Name="Đỏ", Value="#FF0000" },
        new WireColor { Code="G", Name="Xanh lá", Value="#008000" },
        new WireColor { Code="L", Name="Xanh dương", Value="#0000FF" },
        new WireColor { Code="Y", Name="Vàng", Value="#FFFF00" },
        new WireColor { Code="O", Name="Cam", Value="#FFA500" },
        new WireColor { Code="Br", Name="Nâu", Value="#8B4513" },
        new WireColor { Code="P", Name="Hồng", Value="#FF69B4" },
        new WireColor { Code="Gr", Name="Xám", Value="#808080" },
    };

    public string PathValue { get; }
    public ColorStore(string? path = null)
    {
        PathValue = path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JBZUniversalTester", "ModelEditor", "colors.ini");
    }

    public List<WireColor> Load()
    {
        if (!File.Exists(PathValue)) return Defaults.Select(c => c.Clone()).ToList();
        try
        {
            var lines = File.ReadAllLines(PathValue, Encoding.UTF8);
            var section = false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Equals("[Colors]", StringComparison.Ordinal)) { section = true; continue; }
                if (line.StartsWith('[')) { section = false; continue; }
                if (!section) continue;
                var eq = raw.IndexOf('='); if (eq < 0) continue;
                values[raw[..eq].Trim()] = raw[(eq + 1)..];
            }
            var count = values.TryGetValue("Count", out var ctext) && int.TryParse(ctext, out var n) ? n : 0;
            var colors = new List<WireColor>();
            for (var i = 1; i <= count; i++)
            {
                if (!values.TryGetValue($"Color #{i}", out var text)) continue;
                var f = text.Split('|');
                if (f.Length >= 3 && !string.IsNullOrWhiteSpace(f[0]))
                    colors.Add(new WireColor { Code = f[0].Trim(), Name = f[1].Trim(), Value = f[2].Trim() });
            }
            return colors.Count > 0 ? colors : Defaults.Select(c => c.Clone()).ToList();
        }
        catch (Exception ex)
        {
            JBZUniversalTester.Services.AsyncFileLogService.Current.Error($"THT editor cannot load color catalog {PathValue}: {ex.Message}");
            return Defaults.Select(c => c.Clone()).ToList();
        }
    }

    public void Save(IReadOnlyList<WireColor> colors)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathValue)!);
        var lines = new List<string> { "[Colors]", $"Count={colors.Count}" };
        for (var i = 0; i < colors.Count; i++)
            lines.Add($"Color #{i + 1}={colors[i].Code}|{colors[i].Name}|{colors[i].Value}");
        File.WriteAllText(PathValue, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    }
}
