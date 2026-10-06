using System.IO;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestStationIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZStationIdentityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "machine-a", "StationIdentity.txt");
            string first = StationIdentityService.GetOrCreateMachineCode(file);
            Assert(first.StartsWith("MAY-", StringComparison.Ordinal) &&
                   Guid.TryParseExact(first[4..], "N", out _), "Station code is an automatically generated GUID");
            byte[] original = File.ReadAllBytes(file);
            Assert(StationIdentityService.GetOrCreateMachineCode(file) == first &&
                   original.SequenceEqual(File.ReadAllBytes(file)),
                "Restart/new version adopts the persisted machine code without rewriting it");
            string second = StationIdentityService.GetOrCreateMachineCode(
                Path.Combine(root, "machine-b", "StationIdentity.txt"));
            Assert(second != first, "A separate PC identity location creates an independent backup code");
            string concurrentFile = Path.Combine(root, "concurrent", "StationIdentity.txt");
            string[] concurrent = new string[16];
            Parallel.For(0, concurrent.Length, index =>
                concurrent[index] = StationIdentityService.GetOrCreateMachineCode(concurrentFile));
            Assert(concurrent.Distinct(StringComparer.Ordinal).Count() == 1 &&
                   !Directory.EnumerateFiles(Path.GetDirectoryName(concurrentFile)!, "*.tmp").Any(),
                "Simultaneous app initializers publish/adopt exactly one identity and clean temporary files");
            string corrupt = Path.Combine(root, "corrupt.txt");
            File.WriteAllText(corrupt, "broken identity");
            bool rejected = false;
            try { StationIdentityService.GetOrCreateMachineCode(corrupt); }
            catch (InvalidDataException) { rejected = true; }
            Assert(rejected && File.ReadAllText(corrupt) == "broken identity",
                "Invalid station identity is reported and preserved, never silently replaced");
            Assert(StationIdentityService.IdentityFile.StartsWith(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    StringComparison.OrdinalIgnoreCase),
                "Runtime identity is outside copied portable release directories and shared across Windows users");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
