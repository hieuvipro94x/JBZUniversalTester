using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed class LicenseVerificationService
{
    public const string ProductName = "JBZUniversalTester";
    public const string PerpetualLicenseType = "PERPETUAL";
    private const string PublicKeyResource = "JBZUniversalTester.Assets.License.jbz-license-public.pem";
    private readonly string _publicKeyPem;
    private readonly string _licenseFile;

    public LicenseVerificationService(string? publicKeyPem = null, string? licenseFile = null)
    {
        _publicKeyPem = publicKeyPem ?? ReadEmbeddedPublicKey();
        _licenseFile = licenseFile ?? RuntimePaths.LicenseFile;
    }

    public bool ValidateStoredLicense(string registrationId)
    {
        try
        {
            return File.Exists(_licenseFile) &&
                   VerifyActivationCode(File.ReadAllText(_licenseFile), registrationId, _publicKeyPem);
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"LICENSE stored validation failed: {ex}");
            return false;
        }
    }

    public bool ActivateAndSave(string activationCode, string registrationId)
    {
        if (!VerifyActivationCode(activationCode, registrationId, _publicKeyPem))
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_licenseFile)!);
            string temporaryFile = _licenseFile + ".tmp";
            File.WriteAllText(temporaryFile, activationCode.Trim());
            File.Move(temporaryFile, _licenseFile, overwrite: true);
            return ValidateStoredLicense(registrationId);
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"LICENSE activation save failed: {ex}");
            return false;
        }
    }

    /// <summary>JBZ_LICENSE_GATE_2026-09-06</summary>
    public static bool VerifyActivationCode(
        string? activationCode,
        string expectedRegistrationId,
        string publicKeyPem)
    {
        try
        {
            string expectedMachineId = MachineFingerprintService.Normalize(expectedRegistrationId);
            if (expectedMachineId.Length is < 8 or > 64)
                return false;

            string[] parts = (activationCode ?? string.Empty).Trim().Split('.');
            if (parts.Length != 3 || parts[0] != LicensePayload.ProtocolVersion)
                return false;

            byte[] payloadBytes = Base64Url.Decode(parts[1]);
            byte[] signature = Base64Url.Decode(parts[2]);
            using ECDsa publicKey = ECDsa.Create();
            publicKey.ImportFromPem(publicKeyPem);
            if (!publicKey.VerifyData(
                    payloadBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return false;
            }

            return LicensePayload.TryParse(payloadBytes, out LicensePayload? payload) &&
                   payload is not null &&
                   payload.Product == ProductName &&
                   MachineFingerprintService.Normalize(payload.MachineId) == expectedMachineId &&
                   payload.LicenseType == PerpetualLicenseType;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadEmbeddedPublicKey()
    {
        Assembly assembly = typeof(LicenseVerificationService).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(PublicKeyResource)
            ?? throw new InvalidOperationException("Embedded license public key is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
