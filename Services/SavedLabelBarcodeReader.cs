using System.Text;
using System.Text.RegularExpressions;

namespace JBZUniversalTester.Services;

// Read only the verified EPL barcode expressions already present in saved jobs.
// Never rebuild an old label from today's model, date, settings or LOT counter.
internal static partial class SavedLabelBarcodeReader
{
    public static string Read(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return string.Empty;
        string[] lines = payload.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var variables = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match definition = VariableDefinition().Match(line.Trim());
            if (definition.Success && !variables.ContainsKey(definition.Groups[1].Value))
                variables.Add(definition.Groups[1].Value, variables.Count);
        }
        int prompt = Array.FindIndex(lines, line => line.Trim() == "?");
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match barcode = BarcodeCommand().Match(line.Trim());
            if (!barcode.Success) continue;
            var value = new StringBuilder();
            bool complete = true;
            foreach (Match token in ExpressionToken().Matches(barcode.Groups[1].Value))
            {
                if (token.Value[0] == '"') value.Append(token.Value.AsSpan(1, token.Value.Length - 2));
                else if (prompt >= 0 && variables.TryGetValue(token.Value, out int index) && prompt + 1 + index < lines.Length)
                    value.Append(lines[prompt + 1 + index]);
                else { complete = false; break; }
            }
            if (complete && value.Length > 0) values.Add(value.ToString());
        }
        return values.Count == 1 ? values.Single() : string.Empty;
    }

    [GeneratedRegex("^(V[0-9]{2}),", RegexOptions.CultureInvariant)]
    private static partial Regex VariableDefinition();
    [GeneratedRegex("^[bB]-?[0-9]+,-?[0-9]+,[^\\r\\n]*?,((?:V[0-9]{2}|\"[^\"]*\")+)$", RegexOptions.CultureInvariant)]
    private static partial Regex BarcodeCommand();
    [GeneratedRegex("V[0-9]{2}|\"[^\"]*\"", RegexOptions.CultureInvariant)]
    private static partial Regex ExpressionToken();
}
