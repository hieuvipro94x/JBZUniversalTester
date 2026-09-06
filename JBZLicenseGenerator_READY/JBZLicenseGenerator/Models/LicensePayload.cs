using System.Globalization;
using System.Text;

namespace JBZLicenseGenerator.Models;

/// <summary>
/// Payload canonical được ký bởi private key.
/// Giữ format này ổn định để ứng dụng JBZUniversalTester xác minh chính xác về sau.
/// </summary>
public sealed record LicensePayload(
    string Product,
    string MachineId,
    string LicenseType,
    long IssuedUtcUnixSeconds)
{
    public const string ProtocolVersion = "JBZ1";

    public byte[] ToCanonicalBytes()
    {
        string canonical = string.Join(
            "|",
            ProtocolVersion,
            Product,
            MachineId,
            LicenseType,
            IssuedUtcUnixSeconds.ToString(CultureInfo.InvariantCulture));

        return Encoding.UTF8.GetBytes(canonical);
    }

    public static bool TryParse(ReadOnlySpan<byte> bytes, out LicensePayload? payload)
    {
        payload = null;
        string text;
        try
        {
            text = Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return false;
        }

        string[] parts = text.Split('|');
        if (parts.Length != 5 ||
            !string.Equals(parts[0], ProtocolVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            string.IsNullOrWhiteSpace(parts[2]) ||
            string.IsNullOrWhiteSpace(parts[3]) ||
            !long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long issued))
        {
            return false;
        }

        payload = new LicensePayload(
            parts[1],
            parts[2],
            parts[3],
            issued);

        return true;
    }
}
