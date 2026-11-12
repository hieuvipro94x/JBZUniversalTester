using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.Services;

/// <summary>One-time, verified relocation of station History, preserving every source file.</summary>
public static class StationDatabaseMigrationService
{
    public static void EnsureForCurrentInstallation(bool allowNewDatabase = false)
    {
        string destination = RuntimePaths.DatabaseFile;
        string directory = Path.GetDirectoryName(destination)!;
        DirectoryInfo folder = Directory.CreateDirectory(directory);
        // The station history is shared by Windows operator accounts. Grant Modify
        // only on this application's Data folder, never on ProgramData itself.
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        DirectorySecurity acl = folder.GetAccessControl();
        bool writable = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>()
            .Any(rule => rule.IdentityReference.Equals(users) && rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & FileSystemRights.Modify) == FileSystemRights.Modify &&
                (rule.InheritanceFlags & (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)) ==
                (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));
        if (!writable)
        {
            acl.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            folder.SetAccessControl(acl);
        }
        Ensure(destination, RuntimePaths.AppDirectory,
            () => ProductionDataUpgradeService.FindPreviousVersionDirectories(RuntimePaths.AppDirectory), allowNewDatabase);
    }

    public static void Ensure(string destinationPath, string applicationDirectory, Func<IEnumerable<string>> previousDirectories, bool allowNewDatabase = false)
    {
        string destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // Serialize first-time relocation across sessions without an overwrite race.
        using var migrationLock = new FileStream(destination + ".relocation.lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        if (File.Exists(destination))
        {
            if (new FileInfo(destination).Length == 0)
                throw new InvalidDataException("Database ProgramData đang rỗng bất thường; giữ nguyên nguồn và không tự ghi đè: " + destination);
            return; // Warm startup never probes old version folders.
        }
        string adjacent = Path.Combine(Path.GetFullPath(applicationDirectory), "Data", "JBZUniversalTester.db");
        string legacy = Path.Combine(Path.GetFullPath(applicationDirectory), "Data", "History", "test-history.db");
        string? source = File.Exists(adjacent) ? adjacent : File.Exists(legacy) ? legacy : null;
        if (source is null)
        {
            string[] candidates = previousDirectories().Select(path => Path.Combine(Path.GetFullPath(path), "Data", "JBZUniversalTester.db"))
                .Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length > 0)
            {
                // Identical logical databases are interchangeable. Divergent histories
                // require running the new EXE from the actual operating folder.
                string? expected = null;
                foreach (string candidate in candidates)
                {
                    using var connection = OpenReadOnly(candidate);
                    string digest = Fingerprint(connection);
                    if (expected is not null && digest != expected)
                        throw new IOException("Tìm thấy nhiều database khác nhau. Hãy đặt EXE mới vào thư mục đang vận hành (có Data/JBZUniversalTester.db) rồi mở lại. Giữ nguyên tất cả database: " + string.Join("; ", candidates));
                    expected = digest;
                }
                source = candidates[0];
            }
        }
        if (source is null)
        {
            // An existing station CFG without a reachable DB must not silently
            // become a new empty station merely because the EXE was moved.
            if (!allowNewDatabase)
                throw new IOException("Không tìm thấy database cũ. Hãy chạy EXE mới từ thư mục đang vận hành có Data/JBZUniversalTester.db; chưa tạo database trống.");
            return; // Genuinely new installation; repository initializes the empty DB.
        }
        string temporary = destination + ".relocation-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var original = OpenReadOnly(source))
            using (var transaction = original.BeginTransaction(deferred: true))
            using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                string before = Fingerprint(original);
                copy.Open(); original.BackupDatabase(copy);
                string after = Fingerprint(copy);
                if (before != after) throw new InvalidDataException("Database chuyển vị trí không khớp bản gốc; giữ nguyên nguồn.");
            }
            File.Move(temporary, destination, overwrite: false);
            AsyncFileLogService.Current.Application($"DATABASE_RELOCATED source={source} destination={destination}; integrity=ok; all table contents verified; source retained");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2 }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static string Fingerprint(SqliteConnection connection)
    {
        using var check = connection.CreateCommand(); check.CommandText = "PRAGMA integrity_check;";
        using (var result = check.ExecuteReader())
            while (result.Read())
                if (!string.Equals(result.GetString(0), "ok", StringComparison.Ordinal))
                    throw new InvalidDataException("Database integrity_check failed: " + result.GetString(0));
        check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        var tables = new List<string>(); using (var reader = check.ExecuteReader()) while (reader.Read()) tables.Add(reader.GetString(0));
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        foreach (string table in tables)
        {
            writer.Write(table);
            using var columns = connection.CreateCommand(); columns.CommandText = "PRAGMA table_info(\"" + table.Replace("\"", "\"\"") + "\");";
            int count = 0; using (var reader = columns.ExecuteReader()) while (reader.Read()) { writer.Write(reader.GetString(1)); count++; }
            using var rows = connection.CreateCommand();
            rows.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\" ORDER BY " + string.Join(',', Enumerable.Range(1, count)) + ";";
            using var values = rows.ExecuteReader();
            while (values.Read())
            {
                writer.Write((byte)1);
                for (int i = 0; i < values.FieldCount; i++)
                {
                    object value = values.GetValue(i);
                    switch (value)
                    {
                        case DBNull: writer.Write((byte)0); break;
                        case long integer: writer.Write((byte)1); writer.Write(integer); break;
                        case double real: writer.Write((byte)2); writer.Write(real); break;
                        case byte[] bytes: writer.Write((byte)3); writer.Write(bytes.Length); writer.Write(bytes); break;
                        default: writer.Write((byte)4); writer.Write(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""); break;
                    }
                }
            }
            writer.Write((byte)0);
        }
        writer.Flush(); stream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash!);
    }
}
