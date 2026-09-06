using System.Globalization;
using System.Text;

namespace JBZUniversalTester.Models;

public sealed record LicensePayload(
    string Product,
    string MachineId,
    string LicenseType,
    long IssuedUtcUnixSeconds)
{
    public const string ProtocolVersion = "JBZ1";

    public static bool TryParse(ReadOnlySpan<byte> bytes, out LicensePayload? payload)
    {
        payload = null;
        string[] parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 5 ||
            parts[0] != ProtocolVersion ||
            parts.Skip(1).Take(3).Any(string.IsNullOrWhiteSpace) ||
            !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long issued))
        {
            return false;
        }

        payload = new LicensePayload(parts[1], parts[2], parts[3], issued);
        return true;
    }
}
