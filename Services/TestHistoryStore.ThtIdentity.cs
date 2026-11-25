using System.Globalization;
using System.IO;
using JBZUniversalTester.Models;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.Services;

public sealed partial class TestHistoryStore
{
    public ProductionStatisticsSnapshot GetFileStatistics(ModelIdentitySnapshot model, PartIdentitySnapshot part, DateTime now)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        RegisterThtFileIdentity(connection, transaction, model, part);
        var result = QueryFileStatistics(connection, transaction, ThtStatisticsFileName(model.FilePath, model.ModelName), now);
        transaction.Commit();
        return result;
    }

    private static void RegisterThtFileIdentity(SqliteConnection connection, SqliteTransaction transaction,
        ModelIdentitySnapshot model, PartIdentitySnapshot part)
    {
        string file = ThtStatisticsFileName(model.FilePath, model.ModelName);
        if (file.Length == 0) return;
        string root = string.Empty;
        using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT CanonicalFileName FROM ThtFileIdentities WHERE FileName=$File;";
            existing.Parameters.AddWithValue("$File", file);
            root = Convert.ToString(existing.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
        }
        if (root.Length == 0)
        {
            // Do not pin a new identity while its source is temporarily unavailable.
            // Raw filename counters remain usable and a later load can retry detection.
            if (model.FileHash.Length != 64 || !File.Exists(model.FilePath)) return;
            root = file;
            // Never silently merge a filename which already has independent
            // production history. Missing/ambiguous evidence retains isolation.
            using var history = connection.CreateCommand(); history.Transaction = transaction;
            history.CommandText = "SELECT 1 FROM ThtFileCounters WHERE FileName=$File AND TotalTests>0;";
            history.Parameters.AddWithValue("$File", file);
            if (history.ExecuteScalar() is null && model.FileHash.Length == 64 && File.Exists(model.FilePath))
            {
                var candidates = new List<(string File, string Root, string Path, string Name)>();
                using (var find = connection.CreateCommand())
                {
                    find.Transaction = transaction;
                    find.CommandText = """
                        SELECT FileName,CanonicalFileName,LastPath,ModelName FROM ThtFileIdentities
                        WHERE FileHash=$Hash COLLATE NOCASE AND PartNumber=$Part COLLATE NOCASE
                        UNION
                        SELECT m.FileName,m.FileName,m.FilePath,m.ModelName FROM Models m
                        JOIN Parts p ON p.Id=m.PartId
                        WHERE m.FileHash=$Hash COLLATE NOCASE AND p.PartNumber=$Part COLLATE NOCASE
                          AND NOT EXISTS (SELECT 1 FROM ThtFileIdentities i WHERE i.FileName=UPPER(m.FileName));
                        """;
                    find.Parameters.AddWithValue("$Hash", model.FileHash);
                    find.Parameters.AddWithValue("$Part", part.PartNumber);
                    using var reader = find.ExecuteReader();
                    while (reader.Read()) candidates.Add((ThtStatisticsFileName(reader.GetString(0)),
                        ThtStatisticsFileName(reader.GetString(1)),reader.GetString(2),reader.GetString(3)));
                }
                var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in candidates)
                {
                    if (candidate.File == file || !SameStableModelName(candidate.Name, candidate.File, model.ModelName, file) ||
                        !IsMissingRenameSource(candidate.Path, model.FilePath)) continue;
                    bool stillAvailable = false;
                    using (var members = connection.CreateCommand())
                    {
                        members.Transaction = transaction;
                        members.CommandText = "SELECT LastPath FROM ThtFileIdentities WHERE CanonicalFileName=$Root;";
                        members.Parameters.AddWithValue("$Root", candidate.Root);
                        using var reader = members.ExecuteReader();
                        while (reader.Read())
                            if (!IsMissingRenameSource(reader.GetString(0), model.FilePath)) { stillAvailable = true; break; }
                    }
                    using var produced = connection.CreateCommand(); produced.Transaction = transaction;
                    produced.CommandText = """
                        SELECT 1 FROM ThtFileCounters c WHERE c.TotalTests>0 AND
                        (c.FileName=$Root OR EXISTS(SELECT 1 FROM ThtFileIdentities i
                            WHERE i.FileName=c.FileName AND i.CanonicalFileName=$Root)) LIMIT 1;
                        """;
                    produced.Parameters.AddWithValue("$Root", candidate.Root);
                    if (!stillAvailable && produced.ExecuteScalar() is not null) roots.Add(candidate.Root);
                }
                if (roots.Count == 1)
                {
                    root = roots.Single();
                    AsyncFileLogService.Current.Application($"THT_RENAME_IDENTITY file={file}; original={root}; SHA256 and part verified; source no longer exists");
                }
                else if (roots.Count > 1)
                    AsyncFileLogService.Current.Error($"THT_RENAME_AMBIGUOUS file={file}; candidates={roots.Count}; retaining separate counters");
            }
        }
        using var save = connection.CreateCommand(); save.Transaction = transaction;
        save.CommandText = """
            INSERT INTO ThtFileIdentities(FileName,CanonicalFileName,LastPath,FileHash,PartNumber,ModelName)
            VALUES($File,$Root,$Path,$Hash,$Part,$Name)
            ON CONFLICT(FileName) DO UPDATE SET LastPath=excluded.LastPath,FileHash=excluded.FileHash,
                PartNumber=excluded.PartNumber,ModelName=excluded.ModelName;
            """;
        save.Parameters.AddWithValue("$File", file); save.Parameters.AddWithValue("$Root", root);
        save.Parameters.AddWithValue("$Path", model.FilePath); save.Parameters.AddWithValue("$Hash", model.FileHash);
        save.Parameters.AddWithValue("$Part", part.PartNumber); save.Parameters.AddWithValue("$Name", model.ModelName);
        save.ExecuteNonQuery();
    }

    private static bool SameStableModelName(string first, string firstFile, string second, string secondFile)
    {
        // ThtModelParser derives ModelName from the filename. Only a separate
        // internal name, when actually available, may constrain rename identity.
        static string Stable(string name, string file) =>
            string.Equals(name.Trim(), Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)
                ? string.Empty : name.Trim();
        return string.Equals(Stable(first, firstFile), Stable(second, secondFile), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingRenameSource(string previous, string current)
    {
        if (string.IsNullOrWhiteSpace(previous) || string.IsNullOrWhiteSpace(current)) return false;
        string? directory = Path.GetDirectoryName(Path.GetFullPath(previous));
        if (!string.Equals(directory, Path.GetDirectoryName(Path.GetFullPath(current)), StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(directory)) return false;
        try { _ = File.GetAttributes(previous); return false; }
        catch (FileNotFoundException) { return true; }
        catch (IOException ex)
        {
            AsyncFileLogService.Current.Application($"THT_RENAME_SOURCE_UNAVAILABLE path={previous}; {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            AsyncFileLogService.Current.Application($"THT_RENAME_SOURCE_UNAVAILABLE path={previous}; {ex.Message}");
            return false;
        }
    }
}
