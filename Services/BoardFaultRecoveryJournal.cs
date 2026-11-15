using System.IO;
using System.Text.Json;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>Durable fallback for a USB fault while SQLite is temporarily unavailable.</summary>
public static class BoardFaultRecoveryJournal
{
    public static string JournalPath(string database) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database))!, "BoardFaultRecovery.json");

    public static void Write(string database, BoardFaultRecovery recovery)
    {
        string path = JournalPath(database);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, recovery);
                file.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Replay(TestHistoryStore store)
    {
        string path = JournalPath(store.DatabasePath);
        if (!File.Exists(path)) return;
        BoardFaultRecovery recovery = JsonSerializer.Deserialize<BoardFaultRecovery>(File.ReadAllBytes(path))
            ?? throw new InvalidDataException("Không đọc được dữ liệu khôi phục sau mất kết nối bo.");
        store.SaveBoardFaultRecovery(recovery);
        try { File.Delete(path); } // Only after the history/checkpoint transaction committed.
        catch (IOException ex) { AsyncFileLogService.Current.Error($"Board recovery journal cleanup: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { AsyncFileLogService.Current.Error($"Board recovery journal cleanup: {ex.Message}"); }
    }

    public static void Clear(string database)
    {
        string path = JournalPath(database);
        if (File.Exists(path)) File.Delete(path);
    }
}
