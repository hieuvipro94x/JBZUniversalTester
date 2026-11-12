namespace JBZUniversalTester.ModelEditor.Services;

public static class WireColorInput
{
    public static string Normalize(string input, IEnumerable<string> colorCodes)
    {
        var text = input.Trim();
        var codes = colorCodes.Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal).ToArray();

        string? Resolve(string value)
        {
            if (codes.Contains(value, StringComparer.Ordinal)) return value;
            var matches = codes.Where(code => code.Equals(value, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        // An existing single code wins: BR is Br; use B/R for black/red.
        if (Resolve(text) is { } single) return single;
        var parts = text.Replace('\\', '/').Split('/');
        if (parts.Length > 1)
        {
            var resolved = parts.Select(part => Resolve(part.Trim())).ToArray();
            return resolved.All(part => part is not null) ? string.Join("/", resolved) : text;
        }

        var pairs = new HashSet<string>(StringComparer.Ordinal);
        for (var split = 1; split < text.Length; split++)
        {
            if (Resolve(text[..split]) is { } first && Resolve(text[split..]) is { } second)
                pairs.Add($"{first}/{second}");
        }
        // Do not guess when more than one pair matches a custom palette.
        return pairs.Count == 1 ? pairs.Single() : text;
    }
}
