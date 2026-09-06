namespace JBZLicenseGenerator.Services;

internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static byte[] Decode(string value)
    {
        string normalized = value
            .Replace('-', '+')
            .Replace('_', '/');

        int padding = (4 - normalized.Length % 4) % 4;
        if (padding > 0)
            normalized += new string('=', padding);

        return Convert.FromBase64String(normalized);
    }
}
