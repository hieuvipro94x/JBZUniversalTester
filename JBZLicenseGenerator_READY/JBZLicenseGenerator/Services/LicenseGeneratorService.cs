using System.Security.Cryptography;
using JBZLicenseGenerator.Models;

namespace JBZLicenseGenerator.Services;

public sealed class LicenseGeneratorService
{
    public const string ProductName = "JBZUniversalTester";
    public const string LicenseTypePerpetual = "PERPETUAL";
    public const string ProductionPublicKeySha256 =
        "CE13F191DE2267081FBEBACF77DAFBEFC2A24CA4DFE4A92701C4318DFF284542";

    private readonly LicenseKeyStore _keyStore;

    public LicenseGeneratorService(LicenseKeyStore keyStore)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
    }

    public bool IsReadyForProduction(out string message)
    {
        if (!_keyStore.HasPrivateKey || !_keyStore.HasPublicKey)
        {
            message = "CHƯA CÓ BỘ KHÓA CẤP PHÉP CHÍNH THỨC.";
            return false;
        }

        if (!_keyStore.IsMatchingKeyPair())
        {
            message = "PRIVATE KEY VÀ PUBLIC KEY KHÔNG KHỚP NHAU.";
            return false;
        }

        if (!string.Equals(
                _keyStore.GetPublicKeyFingerprint(),
                ProductionPublicKeySha256,
                StringComparison.Ordinal))
        {
            message = "BỘ KHÓA KHÔNG KHỚP VỚI JBZUniversalTester PRODUCTION.";
            return false;
        }

        message = "SẴN SÀNG CẤP MÃ ĐĂNG KÝ.";
        return true;
    }

    public string CreatePerpetualActivationCode(string registrationId)
    {
        if (!IsReadyForProduction(out string readinessMessage))
            throw new InvalidOperationException(readinessMessage);

        string machineId = RegistrationId.Normalize(registrationId);
        if (!RegistrationId.IsValid(machineId))
        {
            throw new ArgumentException(
                "ID đăng ký phải có từ 8 đến 64 ký tự chữ/số.",
                nameof(registrationId));
        }

        var payload = new LicensePayload(
            ProductName,
            machineId,
            LicenseTypePerpetual,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        byte[] payloadBytes = payload.ToCanonicalBytes();

        using ECDsa privateKey = _keyStore.LoadPrivateKey();
        byte[] signature = privateKey.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return string.Join(
            ".",
            LicensePayload.ProtocolVersion,
            Base64Url.Encode(payloadBytes),
            Base64Url.Encode(signature));
    }

    public bool VerifyActivationCode(
        string activationCode,
        string expectedRegistrationId,
        out string message)
    {
        message = string.Empty;

        string expectedMachineId = RegistrationId.Normalize(expectedRegistrationId);
        if (!RegistrationId.IsValid(expectedMachineId))
        {
            message = "ID đăng ký kiểm tra không hợp lệ.";
            return false;
        }

        string[] parts = (activationCode ?? string.Empty).Trim().Split('.');
        if (parts.Length != 3 ||
            !string.Equals(parts[0], LicensePayload.ProtocolVersion, StringComparison.Ordinal))
        {
            message = "Mã đăng ký không đúng định dạng JBZ1.";
            return false;
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Base64Url.Decode(parts[1]);
            signature = Base64Url.Decode(parts[2]);
        }
        catch
        {
            message = "Mã đăng ký bị hỏng hoặc không đúng Base64Url.";
            return false;
        }

        using ECDsa publicKey = _keyStore.LoadPublicKey();
        bool signatureValid = publicKey.VerifyData(
            payloadBytes,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        if (!signatureValid)
        {
            message = "Chữ ký license không hợp lệ.";
            return false;
        }

        if (!LicensePayload.TryParse(payloadBytes, out LicensePayload? payload) ||
            payload is null)
        {
            message = "Không đọc được payload license.";
            return false;
        }

        if (!string.Equals(payload.Product, ProductName, StringComparison.Ordinal))
        {
            message = "License không thuộc JBZUniversalTester.";
            return false;
        }

        if (!string.Equals(payload.MachineId, expectedMachineId, StringComparison.Ordinal))
        {
            message =
                $"License thuộc máy khác. ID trong license: {RegistrationId.FormatForDisplay(payload.MachineId)}";
            return false;
        }

        if (!string.Equals(payload.LicenseType, LicenseTypePerpetual, StringComparison.Ordinal))
        {
            message = $"Loại license không được hỗ trợ: {payload.LicenseType}";
            return false;
        }

        message = "Mã đăng ký hợp lệ.";
        return true;
    }
}
