using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace JBZUniversalTester.Services;

public sealed record MachineIdentity(string WindowsInstallationId, string CpuId, string SystemDiskId);

public interface IMachineIdentitySource
{
    MachineIdentity Read();
}

/// <summary>JBZ_MACHINE_FINGERPRINT_2026-09-06</summary>
public sealed class MachineFingerprintService
{
    private readonly IMachineIdentitySource _source;

    public MachineFingerprintService(IMachineIdentitySource? source = null) =>
        _source = source ?? new WindowsMachineIdentitySource();

    public string GetRegistrationId() => ComputeRegistrationId(_source.Read());

    public static string ComputeRegistrationId(MachineIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string windows = Normalize(identity.WindowsInstallationId);
        string cpu = Normalize(identity.CpuId);
        string disk = Normalize(identity.SystemDiskId);
        if (windows.Length == 0 || cpu.Length == 0 || disk.Length == 0)
            throw new InvalidOperationException("Machine identity is incomplete.");

        string canonical = $"JBZ-MACHINE-V1|WIN={windows}|CPU={cpu}|DISK={disk}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash.AsSpan(0, 16));
    }

    public static string FormatForDisplay(string normalizedId)
    {
        string normalized = Normalize(normalizedId);
        return string.Join("-", Enumerable.Range(0, (normalized.Length + 3) / 4)
            .Select(index => normalized.Substring(index * 4, Math.Min(4, normalized.Length - index * 4))));
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = new StringBuilder(value.Length);
        foreach (char ch in value.Trim().ToUpperInvariant())
        {
            if (!char.IsControl(ch) && !char.IsWhiteSpace(ch) && char.IsLetterOrDigit(ch))
                result.Append(ch);
        }
        return result.ToString();
    }
}

internal sealed class WindowsMachineIdentitySource : IMachineIdentitySource
{
    public MachineIdentity Read() => new(
        ReadWindowsInstallationId(),
        ReadCpuIdentity(),
        ReadSystemDiskIdentity());

    private static string ReadWindowsInstallationId()
    {
        string machineGuid = ReadRegistry64(
            @"SOFTWARE\Microsoft\Cryptography",
            "MachineGuid");
        if (MachineFingerprintService.Normalize(machineGuid).Length > 0)
            return machineGuid;

        AsyncFileLogService.Current.Error("LICENSE fingerprint: MachineGuid unavailable; using Windows installation fallback.");
        string productId = ReadRegistry64(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "ProductId");
        string installDate = ReadRegistry64(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "InstallDate");
        string fallback = productId + installDate;
        return MachineFingerprintService.Normalize(fallback).Length > 0
            ? fallback
            : throw new InvalidOperationException("Windows installation identity is unavailable.");
    }

    private static string ReadCpuIdentity()
    {
        string[] processorIds = QueryValues("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
        if (processorIds.Length > 0)
            return string.Join("", processorIds.OrderBy(value => value, StringComparer.Ordinal));

        AsyncFileLogService.Current.Error("LICENSE fingerprint: ProcessorId unavailable; using stable CPU properties.");
        string[] fallbacks = QueryCompositeValues(
            "SELECT Manufacturer,Name,Family,Model,Stepping,Architecture FROM Win32_Processor",
            "Manufacturer", "Name", "Family", "Model", "Stepping", "Architecture");
        if (fallbacks.Length > 0)
            return string.Join("", fallbacks.OrderBy(value => value, StringComparer.Ordinal));

        string registryFallback = ReadRegistry64(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString") + ReadRegistry64(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "Identifier");
        return MachineFingerprintService.Normalize(registryFallback).Length > 0
            ? registryFallback
            : throw new InvalidOperationException("CPU identity is unavailable.");
    }

    private static string ReadSystemDiskIdentity()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\')
            ?? throw new InvalidOperationException("Windows system volume is unavailable.");
        string logicalPath = $"Win32_LogicalDisk.DeviceID=\"{EscapeWmiPath(root)}\"";

        foreach (ManagementObject partition in QueryAssociators(
                     logicalPath,
                     "Win32_LogicalDiskToPartition"))
        {
            using (partition)
            {
                foreach (ManagementObject disk in QueryAssociators(
                             partition.Path.RelativePath,
                             "Win32_DiskDriveToDiskPartition"))
                {
                    using (disk)
                    {
                        string serial = CleanValue(disk["SerialNumber"]);
                        if (serial.Length > 0)
                            return serial;

                        AsyncFileLogService.Current.Error("LICENSE fingerprint: system disk serial unavailable; using stable disk properties.");
                        string fallback = string.Concat(
                            CleanValue(disk["PNPDeviceID"]),
                            CleanValue(disk["Signature"]),
                            CleanValue(disk["Model"]),
                            CleanValue(disk["Size"]));
                        if (fallback.Length > 0)
                            return fallback;
                    }
                }
            }
        }

        throw new InvalidOperationException("System disk identity is unavailable.");
    }

    private static string ReadRegistry64(string subKey, string valueName)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = baseKey.OpenSubKey(subKey, writable: false);
            return key?.GetValue(valueName)?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"LICENSE fingerprint registry read failed ({valueName}): {ex}");
            return string.Empty;
        }
    }

    private static string[] QueryValues(string query, string property) =>
        QueryCompositeValues(query, property);

    private static string[] QueryCompositeValues(string query, params string[] properties)
    {
        var values = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    string value = string.Concat(properties.Select(property => CleanValue(item[property])));
                    if (value.Length > 0)
                        values.Add(value);
                }
            }
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"LICENSE fingerprint WMI query failed: {ex}");
        }
        return values.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<ManagementObject> QueryAssociators(string relativePath, string associationClass)
    {
        using var searcher = new ManagementObjectSearcher(
            $"ASSOCIATORS OF {{{relativePath}}} WHERE AssocClass={associationClass}");
        using ManagementObjectCollection results = searcher.Get();
        return results.Cast<ManagementObject>().ToArray();
    }

    private static string CleanValue(object? value) =>
        MachineFingerprintService.Normalize(value?.ToString());

    private static string EscapeWmiPath(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
