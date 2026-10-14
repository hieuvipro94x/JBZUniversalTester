using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.Services;

public sealed record RecentTestedProduct(ProductFileEntry File, DateTime TestedAt)
{
    public string TestedAtText => TestedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}

public static class RecentTestedProductService
{
    public static IReadOnlyList<RecentTestedProduct> Read(string databasePath,
        IReadOnlyList<ProductFileEntry> catalog, CancellationToken ct = default, int limit = 10)
    {
        if (!File.Exists(databasePath) || catalog.Count == 0 || limit <= 0)
            return Array.Empty<RecentTestedProduct>();
        var available = catalog.ToDictionary(file => file.FullPath, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RecentTestedProduct>();
        // Read existing history only; never initialize/migrate a database from the picker.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, DefaultTimeout = 2
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        // Find the latest result per model through the existing ModelId/ResultAt index.
        // Repeated tests of one model do not require reading its entire history.
        command.CommandText = """
            SELECT m.FilePath, t.ResultAt
            FROM Models m JOIN Tests t ON t.Id=(
                SELECT latest.Id FROM Tests latest
                WHERE latest.ModelId=m.Id AND latest.InspectionType='PRODUCT'
                ORDER BY latest.ResultAt DESC, latest.Id DESC LIMIT 1
            )
            ORDER BY t.ResultAt DESC, t.Id DESC;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            string path = reader.GetString(0);
            if (!available.TryGetValue(path, out var file) || !seen.Add(path)) continue;
            if (!DateTime.TryParse(reader.GetString(1), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var testedAt)) continue;
            result.Add(new RecentTestedProduct(file, testedAt));
            if (result.Count >= limit) break;
        }
        return result;
    }
}
