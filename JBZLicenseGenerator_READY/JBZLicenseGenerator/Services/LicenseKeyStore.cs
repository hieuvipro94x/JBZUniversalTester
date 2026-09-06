using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace JBZLicenseGenerator.Services;

/// <summary>
/// Private key CHỈ thuộc tool JBZLicenseGenerator.
/// Tuyệt đối không copy private key sang JBZUniversalTester.
/// </summary>
public sealed class LicenseKeyStore
{
    private const string PrivateKeyFileName = "jbz-license-private.pem";
    private const string PublicKeyFileName = "jbz-license-public.pem";
    private const string EmbeddedPrivateKey = "JBZLicenseGenerator.Keys.jbz-license-private.pem";
    private const string EmbeddedPublicKey = "JBZLicenseGenerator.Keys.jbz-license-public.pem";

    public string KeyDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JBZ",
        "LicenseGenerator");

    public string PrivateKeyPath => Path.Combine(KeyDirectory, PrivateKeyFileName);
    public string PublicKeyPath => Path.Combine(KeyDirectory, PublicKeyFileName);

    public bool HasPrivateKey => HasEmbeddedResource(EmbeddedPrivateKey) || File.Exists(PrivateKeyPath);
    public bool HasPublicKey => HasEmbeddedResource(EmbeddedPublicKey) || File.Exists(PublicKeyPath);

    public void CreateNewKeyPair()
    {
        Directory.CreateDirectory(KeyDirectory);

        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        File.WriteAllText(
            PrivateKeyPath,
            ecdsa.ExportPkcs8PrivateKeyPem(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        File.WriteAllText(
            PublicKeyPath,
            ecdsa.ExportSubjectPublicKeyInfoPem(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public ECDsa LoadPrivateKey()
    {
        if (!HasPrivateKey)
            throw new InvalidOperationException(
                "Chưa có PRIVATE KEY. Hãy bấm KHỞI TẠO KHÓA trước.");

        string pem = ReadEmbeddedResource(EmbeddedPrivateKey)
            ?? File.ReadAllText(PrivateKeyPath, Encoding.UTF8);
        ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return ecdsa;
    }

    public ECDsa LoadPublicKey()
    {
        if (!HasPublicKey)
            throw new InvalidOperationException(
                "Không tìm thấy PUBLIC KEY của bộ cấp phép.");

        string pem = ReadEmbeddedResource(EmbeddedPublicKey)
            ?? File.ReadAllText(PublicKeyPath, Encoding.UTF8);
        ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return ecdsa;
    }

    public string ReadPublicKeyPem() =>
        ReadEmbeddedResource(EmbeddedPublicKey)
        ?? (File.Exists(PublicKeyPath) ? File.ReadAllText(PublicKeyPath, Encoding.UTF8) : string.Empty);

    public string GetPublicKeyFingerprint()
    {
        if (!HasPublicKey)
            return "CHƯA CÓ";

        using ECDsa ecdsa = LoadPublicKey();
        byte[] publicKey = ecdsa.ExportSubjectPublicKeyInfo();
        byte[] hash = SHA256.HashData(publicKey);

        return Convert.ToHexString(hash);
    }

    public bool IsMatchingKeyPair()
    {
        if (!HasPrivateKey || !HasPublicKey)
            return false;

        try
        {
            byte[] probe = Encoding.UTF8.GetBytes("JBZ-LICENSE-KEY-PAIR-CHECK");
            using ECDsa privateKey = LoadPrivateKey();
            using ECDsa publicKey = LoadPublicKey();
            byte[] signature = privateKey.SignData(
                probe,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return publicKey.VerifyData(
                probe,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch
        {
            return false;
        }
    }

    public void OpenKeyFolder()
    {
        Directory.CreateDirectory(KeyDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = KeyDirectory,
            UseShellExecute = true
        });
    }

    private static bool HasEmbeddedResource(string name) =>
        typeof(LicenseKeyStore).Assembly.GetManifestResourceInfo(name) is not null;

    private static string? ReadEmbeddedResource(string name)
    {
        using Stream? stream = typeof(LicenseKeyStore).Assembly.GetManifestResourceStream(name);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
