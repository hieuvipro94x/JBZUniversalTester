using JBZUniversalTester.Models;
using JBZUniversalTester.Versioning;

namespace JBZUniversalTester.Services;

public static class ProgramIdentityService
{
    public const string ProgramName = "JBZUniversalTester";

    public static string VersionText => AppVersion.ProductVersion;

    public static string AssemblyVersionText => AppVersion.AssemblyVersion;

    public static string FileVersionText => AppVersion.FileVersion;

    public static string InformationalVersionText => AppVersion.InformationalVersion;

    public static string BuildHtdrvName(ProductionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return $"{ProgramName} V{VersionText} " +
               $"[Card Count]{settings.ExpansionCardCount} " +
               $"[USB Delay]{settings.UsbDelay} " +
               $"[R1 JIG]{settings.Relay1JigPulseMs}ms " +
               $"[R2 MARKING]{settings.Relay2MarkingPulseMs}ms " +
               $"[R2->R1]{settings.PassMarkingToJigDelayMs}ms";
    }
}
