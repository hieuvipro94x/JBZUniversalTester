using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace JBZUniversalTester.Services;

[Flags]
public enum SettingsPermission { None = 0, Io = 1, Relay = 2, Leak = 4, Resistance = 8, Label = 16, All = 31 }

public sealed record SettingsAccessSession(bool IsAdmin, SettingsPermission Permissions)
{
    public static SettingsAccessSession Administrator { get; } = new(true, SettingsPermission.All);
    public bool Allows(SettingsPermission permission) => IsAdmin || (Permissions & permission) == permission;
    public void Demand(SettingsPermission permission)
    {
        if (!Allows(permission)) throw new UnauthorizedAccessException("Bạn chưa được cấp quyền cho nhóm cài đặt này.");
    }
}

// Separate from portable production CFG: credentials persist across EXE versions
// for the current Windows user. No plaintext password is written to disk/logs.
public sealed class SettingsAccessService
{
    private const int Iterations = 210_000;
    private static readonly Lazy<SettingsAccessService> Shared = new(() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JBZUniversalTester", "settings-access.json")));
    public static SettingsAccessService Current => Shared.Value;
    private readonly string _path;
    private readonly object _gate = new();
    private AccessRecord _record;

    public SettingsAccessService(string path)
    {
        _path = Path.GetFullPath(path);
        if (File.Exists(_path)) _record = Read(_path);
        else if (File.Exists(_path + ".backup")) _record = Read(_path + ".backup");
        else
        {
            _record = Create("admin", SettingsPermission.None);
            Save(_record);
        }
    }

    public SettingsAccessSession OperatorSession
    {
        get => new(false, Volatile.Read(ref _record).OperatorPermissions);
    }

    public bool VerifyAdmin(string password)
    {
        AccessRecord snapshot = Volatile.Read(ref _record);
        bool valid = Verify(snapshot, password);
        return valid && ReferenceEquals(snapshot, Volatile.Read(ref _record));
    }

    public async Task<bool> VerifyAdminAsync(string password)
    {
        long started = Stopwatch.GetTimestamp();
        bool valid = await Task.Run(() => VerifyAdmin(password)).ConfigureAwait(false);
        AsyncFileLogService.Current.Performance($"AUTH_PERF phase=VERIFY_ADMIN elapsed_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.###}");
        return valid;
    }

    public void ChangePassword(SettingsAccessSession session, string currentPassword, string newPassword, string confirmation)
    {
        if (!session.IsAdmin) throw new UnauthorizedAccessException("Chỉ Admin được đổi mật khẩu.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length > 128)
            throw new InvalidOperationException("Mật khẩu mới phải có từ 1 đến 128 ký tự và không được chỉ chứa khoảng trắng.");
        if (newPassword != confirmation) throw new InvalidOperationException("Hai lần nhập mật khẩu mới không khớp.");
        lock (_gate)
        {
            if (!Verify(_record, currentPassword)) throw new UnauthorizedAccessException("Mật khẩu hiện tại không đúng.");
            AccessRecord updated = Create(newPassword, _record.OperatorPermissions);
            Save(updated);
            Volatile.Write(ref _record, updated);
        }
    }

    public void SetOperatorPermissions(SettingsAccessSession session, SettingsPermission permissions)
    {
        if (!session.IsAdmin) throw new UnauthorizedAccessException("Chỉ Admin được phân quyền.");
        if ((permissions & ~SettingsPermission.All) != 0) throw new ArgumentOutOfRangeException(nameof(permissions));
        lock (_gate)
        {
            AccessRecord updated = _record with { OperatorPermissions = permissions };
            Save(updated);
            Volatile.Write(ref _record, updated);
        }
    }

    private static AccessRecord Create(string password, SettingsPermission permissions)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(24);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return new(1, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash), permissions);
    }

    private static bool Verify(AccessRecord record, string password)
    {
        if (password.Length > 128) return false;
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(record.Salt),
            record.Iterations, HashAlgorithmName.SHA256, 32);
        try { return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(record.Hash)); }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }

    private static AccessRecord Read(string path)
    {
        // A damaged credential file must never silently reset the password to admin.
        AccessRecord record = JsonSerializer.Deserialize<AccessRecord>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Không đọc được thông tin phân quyền Cài đặt.");
        if (record.Version != 1 || record.Iterations is < 100_000 or > 1_000_000 ||
            Convert.FromBase64String(record.Salt).Length < 16 || Convert.FromBase64String(record.Hash).Length != 32 ||
            (record.OperatorPermissions & ~SettingsPermission.All) != 0)
            throw new InvalidDataException("Thông tin phân quyền Cài đặt không hợp lệ.");
        return record;
    }

    private void Save(AccessRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, record);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".backup");
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record AccessRecord(int Version, int Iterations, string Salt, string Hash,
        SettingsPermission OperatorPermissions);
}
