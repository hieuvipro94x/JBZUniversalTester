using System.IO;

namespace JBZUniversalTester.Services;

/// <summary>Removes backup folders associated with THT saves during this application session.</summary>
public sealed class ThtBackupCleanupService
{
    public static ThtBackupCleanupService Current { get; } = new();
    private readonly object _gate = new();
    private readonly HashSet<string> _parents = new(StringComparer.OrdinalIgnoreCase);

    public void TrackSavedFile(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Path.GetExtension(full).Equals(".tht", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only a saved THT can register backup cleanup.", nameof(path));
        lock (_gate) _parents.Add(Path.GetDirectoryName(full)!);
    }

    public Task CleanupAsync() => Task.Run(() =>
    {
        string[] parents;
        lock (_gate) { parents = _parents.ToArray(); _parents.Clear(); }
        foreach (string parent in parents)
        {
            string backup = Path.GetFullPath(Path.Combine(parent, "ThtBackups"));
            try
            {
                // Check the final absolute target before recursive deletion. Never follow
                // a junction/symlink in its ancestry or inside the backup folder.
                if (!string.Equals(Path.GetDirectoryName(backup), parent, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetFileName(backup), "ThtBackups", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Invalid THT backup cleanup path.");
                if (!Directory.Exists(backup)) continue;
                for (DirectoryInfo? ancestor = new(backup); ancestor is not null; ancestor = ancestor.Parent)
                    RejectLink(ancestor);
                ValidateTree(new DirectoryInfo(backup));
                Directory.Delete(backup, recursive: true);
                AsyncFileLogService.Current.Application($"THT_BACKUP_CLEANUP removed={backup}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                AsyncFileLogService.Current.Error($"THT_BACKUP_CLEANUP failed path={backup}; {ex.Message}");
            }
        }
    });

    private static void RejectLink(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing linked backup path: {entry.FullName}");
    }

    private static void ValidateTree(DirectoryInfo directory)
    {
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            RejectLink(entry);
            if (entry is DirectoryInfo child) ValidateTree(child);
        }
    }
}
