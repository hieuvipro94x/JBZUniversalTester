using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JBZ.Licensing;

public sealed record RegistrationRequest(int Version, string Product, string InstallationId, string MachineHash, string ComputerName);
public sealed record LicenseGrant(int Version, string Product, string LicenseId, string InstallationId, string MachineHash,
    string ComputerName, string Customer, string Station, DateTimeOffset IssuedAtUtc, string Developer);

// Shared wire format. This file contains verification only, never an issuer private key.
public static class LicenseProtocol
{
    public const string Product = "JBZUniversalTester";
    public const string Developer = "Vũ Tuấn Nam";
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEgFEDlxBsXVY/Orwl/mISNksfof2ZwCNSmHuml816pVs75WPydellRbuy80e2eyphxW96ga4YEDDm7SmtIKxuew==";
    public const int MaximumCodeLength = 8192;
    public static string NormalizeCode(string value)
    {
        if (value.Length > MaximumCodeLength) throw new FormatException("Mã quá dài.");
        return string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
    }
    public static string EncodeRequest(RegistrationRequest request)
    {
        Validate(request);
        string body = Encode(JsonSerializer.SerializeToUtf8Bytes(request));
        string checksum = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes("JBZR1." + body)))[..16];
        return "JBZR1." + body + "." + checksum;
    }
    public static RegistrationRequest DecodeRequest(string code)
    {
        string[] parts = NormalizeCode(code).Split('.');
        if (parts.Length != 3 || parts[0] != "JBZR1") throw new FormatException("Mã đăng ký không đúng định dạng JBZ.");
        string checksum = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1])))[..16];
        if (!string.Equals(checksum, parts[2], StringComparison.Ordinal)) throw new FormatException("Mã đăng ký bị thiếu hoặc thay đổi. Hãy sao chép lại toàn bộ mã.");
        var request = JsonSerializer.Deserialize<RegistrationRequest>(Decode(parts[1])) ?? throw new FormatException("Mã đăng ký trống.");
        Validate(request); return request;
    }
    private static void Validate(RegistrationRequest r)
    {
        if (r.Version != 1 || r.Product != Product || !Guid.TryParseExact(r.InstallationId,"N",out _) ||
            r.MachineHash is null || r.MachineHash.Length != 64 || !r.MachineHash.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(r.ComputerName) || r.ComputerName.Length > 128)
            throw new FormatException("Thông tin đăng ký máy không hợp lệ.");
    }
    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    public static byte[] Decode(string text)
    {
        if (text.Length > MaximumCodeLength || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new FormatException("Mã hóa đăng ký không hợp lệ.");
        string value=text.Replace('-','+').Replace('_','/'); return Convert.FromBase64String(value.PadRight((value.Length+3)/4*4,'='));
    }
    public static LicenseGrant Verify(string code, RegistrationRequest request, string publicKey = PublicKey)
    {
        Validate(request);
        string[] parts = NormalizeCode(code).Split('.');
        if (parts.Length != 3 || parts[0] != "JBZL1") throw new FormatException("Mã kích hoạt không đúng định dạng.");
        byte[] body=Decode(parts[1]), signature=Decode(parts[2]);
        using var key=ECDsa.Create(); byte[] spki=Convert.FromBase64String(publicKey);
        key.ImportSubjectPublicKeyInfo(spki,out int consumed);
        if (consumed!=spki.Length || key.KeySize!=256 || signature.Length!=64 ||
            !key.VerifyData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new CryptographicException("Mã kích hoạt không có chữ ký hợp lệ.");
        var grant=JsonSerializer.Deserialize<LicenseGrant>(body) ?? throw new FormatException("License trống.");
        if (grant.Version!=1 || grant.Product!=Product || !Guid.TryParseExact(grant.LicenseId,"N",out _) ||
            grant.InstallationId!=request.InstallationId || grant.MachineHash!=request.MachineHash)
            throw new CryptographicException("Mã kích hoạt không dành cho máy hoặc bản cài Windows này.");
        return grant;
    }
}
