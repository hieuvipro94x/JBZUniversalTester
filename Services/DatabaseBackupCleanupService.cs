using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.Services;

/// <summary>Deletes only automatic pre-schema copies after a successful database session.</summary>
public sealed class DatabaseBackupCleanupService
{
    public static DatabaseBackupCleanupService Current { get; } = new();
    private readonly object _gate = new();
    private readonly HashSet<string> _databases = new(StringComparer.OrdinalIgnoreCase);

    public void TrackInitializedDatabase(string path)
    {
        lock (_gate) _databases.Add(Path.GetFullPath(path));
    }

    public Task CleanupAsync() => Task.Run(() =>
    {
        string[] databases;
        lock (_gate) { databases = _databases.ToArray(); _databases.Clear(); }
        foreach (string database in databases)
        {
            try
            {
                if (!File.Exists(database)) continue;
                string directory = Path.GetDirectoryName(database)!;
                for (DirectoryInfo? ancestor = new(directory); ancestor is not null; ancestor = ancestor.Parent)
                    RejectLink(ancestor);
                RejectLink(new FileInfo(database));
                var pattern = new Regex("^" + Regex.Escape(Path.GetFileName(database)) +
                    @"\.pre-schema-v[0-9]+(?:\.[0-9]{8}_[0-9]{6}_[0-9]{7})?\.backup$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                string[] backups = Directory.EnumerateFiles(directory).Where(file => pattern.IsMatch(Path.GetFileName(file))).ToArray();
                if (backups.Length == 0) continue;
                // Production writer has drained before this worker runs. A missing,
                // unreadable or damaged live database must retain its recovery copies.
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 2 }.ToString()))
                {
                    connection.Open();
                    using var check = connection.CreateCommand();
                    check.CommandText = "PRAGMA integrity_check;";
                    if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Active database integrity check failed; backups retained.");
                    check.CommandText = "SELECT MAX(SchemaVersion) FROM SchemaInfo;";
                    if (Convert.ToInt32(check.ExecuteScalar()) != TestHistoryStore.CurrentSchemaVersion)
                        throw new InvalidDataException("Database schema is not initialized; backups retained.");
                }
                foreach (string backup in backups)
                {
                    try
                    {
                        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(backup)), directory, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Invalid database backup cleanup path.");
                        RejectLink(new FileInfo(backup));
                        File.Delete(backup);
                        AsyncFileLogService.Current.Application($"DATABASE_BACKUP_CLEANUP removed={backup}");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        AsyncFileLogService.Current.Error($"DATABASE_BACKUP_CLEANUP failed path={backup}; {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or System.Security.SecurityException)
            {
                AsyncFileLogService.Current.Error($"DATABASE_BACKUP_CLEANUP retained database={database}; {ex.Message}");
            }
        }
    });

    private static void RejectLink(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing linked database backup path: {entry.FullName}");
    }
}
