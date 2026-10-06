using System.IO;

namespace JBZUniversalTester.Services;

/// <summary>Station identity stays on the PC, outside portable release/config folders.</summary>
public static class StationIdentityService
{
    public static string IdentityFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "JBZUniversalTester", "StationIdentity.txt");

    public static string GetOrCreateMachineCode(string? identityFile = null)
    {
        string path = identityFile ?? IdentityFile;
        if (File.Exists(path))
            return ReadCode(path);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, Guid.NewGuid().ToString("N"));
            try
            {
                // Atomic publication: two app versions/users must adopt one identity.
                File.Move(temporary, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another initializer published first; adopt its complete file below.
            }
            return ReadCode(path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string ReadCode(string path)
    {
        string value = File.ReadAllText(path).Trim();
        if (!Guid.TryParseExact(value, "N", out Guid id) || id == Guid.Empty)
            throw new InvalidDataException(
                $"File mã máy không hợp lệ: {path}. Giữ nguyên file để kiểm tra; không tự cấp mã khác.");
        return "MAY-" + id.ToString("N").ToUpperInvariant();
    }
}
