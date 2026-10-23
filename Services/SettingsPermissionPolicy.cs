using System.Text.Json;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

internal static class SettingsPermissionPolicy
{
    public static SettingsPermission GroupFor(string property)
    {
        if (property.StartsWith("WaterProof", StringComparison.OrdinalIgnoreCase)) return SettingsPermission.Leak;
        if (property == nameof(ProductionSettings.ResistanceDelayMs)) return SettingsPermission.Relay;
        if (property.StartsWith("Resistance", StringComparison.Ordinal)) return SettingsPermission.Resistance;
        if (property == nameof(ProductionSettings.Label) || property == nameof(ProductionSettings.AutoPrintLabelOnPass))
            return SettingsPermission.Label;
        if (property.Contains("Relay", StringComparison.Ordinal) || property.StartsWith("PassMarking", StringComparison.Ordinal) ||
            property is nameof(ProductionSettings.StampDelay) or nameof(ProductionSettings.ShieldDelay) or
            nameof(ProductionSettings.OversizeWaitSeconds) or nameof(ProductionSettings.ScrollDelay) or nameof(ProductionSettings.PageDelay))
            return SettingsPermission.Relay;
        return SettingsPermission.Io;
    }

    public static string DeniedSnapshot(ProductionSettings settings, SettingsAccessSession access) =>
        JsonSerializer.Serialize(typeof(ProductionSettings).GetProperties()
            .Where(property => property.CanRead && !access.Allows(GroupFor(property.Name)))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(property => property.Name, property => property.GetValue(settings)));
}
