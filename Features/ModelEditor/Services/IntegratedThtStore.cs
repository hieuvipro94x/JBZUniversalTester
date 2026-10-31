using System.IO;
using System.Security.Cryptography;
using JBZUniversalTester.ModelEditor.Models;

namespace JBZUniversalTester.ModelEditor.Services;

/// <summary>Native THT editing only; no MODEL conversion or hardware ownership.</summary>
public static class IntegratedThtStore
{
    private static readonly object Gate = new();
    public static string Fingerprint(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static string Archive(string path, string? expectedHash)
    {
        lock (Gate)
        {
            path = Path.GetFullPath(path);
            if (!path.EndsWith(".tht", StringComparison.OrdinalIgnoreCase) ||
                expectedHash is null || Fingerprint(path) != expectedHash)
                throw new IOException("THT đã thay đổi hoặc chưa được mở. Hãy mở lại file trước khi xóa.");
            string backups = Path.Combine(Path.GetDirectoryName(path)!, "ThtBackups");
            Directory.CreateDirectory(backups);
            string archived = Path.Combine(backups, $"{Path.GetFileNameWithoutExtension(path)}.deleted.{DateTime.Now:yyyyMMdd_HHmmss_fff}.{Guid.NewGuid():N}.tht");
            File.Move(path, archived);
            try { if (File.Exists(path + ".leak.json")) File.Move(path + ".leak.json", archived + ".leak.json"); }
            catch { File.Move(archived, path); throw; }
            return archived;
        }
    }

    public static ModelDocument Save(ModelDocument document, string target, string? expectedHash)
    {
        lock (Gate)
        {
            target = Path.GetFullPath(target);
            if (!target.EndsWith(".tht", StringComparison.OrdinalIgnoreCase)) throw new IOException("Chỉ lưu định dạng THT.");
            string directory = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(directory);
            string staging = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp.tht");
            string sidecar = target + ".leak.json";
            byte[]? original = File.Exists(target) ? File.ReadAllBytes(target) : null;
            byte[]? originalLeak = File.Exists(sidecar) ? File.ReadAllBytes(sidecar) : null;
            if (expectedHash is not null && (original is null || !Convert.ToHexString(SHA256.HashData(original)).Equals(expectedHash, StringComparison.Ordinal)))
                throw new IOException("THT đã thay đổi sau khi mở. Hãy mở lại file trước khi lưu.");
            bool replaced = false;
            try
            {
                ThtFileService.Save(document, staging);
                ThtFileService.VerifyExport(staging);
                var parsed = new JBZUniversalTester.Services.ThtModelParser().Load(staging);
                if (parsed.MaxIo > JBZUniversalTester.Models.BoardCapacity.MaxGlobalIo)
                    throw new IOException("THT có IO vượt phạm vi hỗ trợ của UniversalTester.");
                if (File.Exists(staging + ".leak.json"))
                    _ = JBZUniversalTester.Services.ModelLeakLayoutService.Apply(staging, new());
                if ((original is null && File.Exists(target)) ||
                    (original is not null && (!File.Exists(target) || !File.ReadAllBytes(target).SequenceEqual(original))))
                    throw new IOException("THT vừa thay đổi bởi chương trình khác. Giữ nguyên file và mở lại.");
                if ((originalLeak is null && File.Exists(sidecar)) ||
                    (originalLeak is not null && (!File.Exists(sidecar) || !File.ReadAllBytes(sidecar).SequenceEqual(originalLeak))))
                    throw new IOException("Cấu hình Leak vừa thay đổi. Hãy mở lại đúng cặp THT và Leak.");
                string backups = Path.Combine(directory, "ThtBackups");
                Directory.CreateDirectory(backups);
                string backup = Path.Combine(backups, $"{Path.GetFileNameWithoutExtension(target)}.{DateTime.Now:yyyyMMdd_HHmmss_fff}.{Guid.NewGuid():N}.tht");
                if (original is null) File.Move(staging, target);
                else File.Replace(staging, target, backup);
                replaced = true;
                try
                {
                    if (File.Exists(staging + ".leak.json"))
                    {
                        if (originalLeak is null) File.Move(staging + ".leak.json", sidecar);
                        else File.Replace(staging + ".leak.json", sidecar, backup + ".leak.json");
                    }
                    return ThtFileService.Load(target);
                }
                catch
                {
                    if (original is null) File.Delete(target);
                    else { File.WriteAllBytes(staging, original); File.Replace(staging, target, null); }
                    if (originalLeak is null) { if (File.Exists(sidecar)) File.Delete(sidecar); }
                    else if (!File.ReadAllBytes(sidecar).SequenceEqual(originalLeak))
                    { File.WriteAllBytes(staging + ".leak.json", originalLeak); File.Replace(staging + ".leak.json", sidecar, null); }
                    throw;
                }
            }
            catch (Exception ex)
            {
                JBZUniversalTester.Services.AsyncFileLogService.Current.Error($"THT EDIT save failed: {target}; replaced={replaced}; {ex}");
                throw;
            }
            finally
            {
                if (File.Exists(staging)) File.Delete(staging);
                if (File.Exists(staging + ".leak.json")) File.Delete(staging + ".leak.json");
            }
        }
    }
}
