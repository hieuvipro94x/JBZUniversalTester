using JBZLicenseGenerator.Services;
using JBZUniversalTester.Services;

const string registrationId = "86BF19488CD6E3BE476AC02FC080ACE8";
var keyStore = new LicenseKeyStore();
var generator = new LicenseGeneratorService(keyStore);
if (!generator.IsReadyForProduction(out string readiness))
    throw new InvalidOperationException(readiness);

string activationCode = generator.CreatePerpetualActivationCode(registrationId);
bool generatorVerified = generator.VerifyActivationCode(activationCode, registrationId, out _);
bool productionVerified = LicenseVerificationService.VerifyActivationCode(
    activationCode,
    registrationId,
    keyStore.ReadPublicKeyPem());
Console.WriteLine($"GENERATOR_READY={readiness}");
Console.WriteLine($"GENERATOR_VERIFY={generatorVerified}");
Console.WriteLine($"PRODUCTION_VERIFY={productionVerified}");
return generatorVerified && productionVerified ? 0 : 1;
