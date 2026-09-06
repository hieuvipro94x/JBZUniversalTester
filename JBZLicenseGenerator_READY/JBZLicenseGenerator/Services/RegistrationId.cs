using System.Text;
using System.IO;
namespace JBZLicenseGenerator.Services;

public static class RegistrationId
{
    /// <summary>
    /// Chuẩn hóa ID để người dùng nhập "685c8455", "685C-8455" hay có khoảng trắng
    /// vẫn tạo cùng một mã đăng ký.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (char ch in value.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
        }

        return builder.ToString();
    }

    public static bool IsValid(string normalized) =>
        normalized.Length is >= 8 and <= 64 &&
        normalized.All(char.IsLetterOrDigit);

    public static string FormatForDisplay(string normalized)
    {
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        var parts = Enumerable
            .Range(0, (normalized.Length + 3) / 4)
            .Select(index =>
            {
                int start = index * 4;
                int length = Math.Min(4, normalized.Length - start);
                return normalized.Substring(start, length);
            });

        return string.Join("-", parts);
    }
}
