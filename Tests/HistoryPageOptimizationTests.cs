using System.Diagnostics;
using System.IO;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void CopyHistoryFixtureRows(string path, int copiesPerSeed)
    {
        using var db = new SqliteConnection("Data Source=" + path);
        db.Open();
        var columns = new List<string>();
        using (var schema = db.CreateCommand())
        {
            schema.CommandText = "PRAGMA table_info(Tests);";
            using var r = schema.ExecuteReader();
            while (r.Read()) if (r.GetString(1) != "Id") columns.Add(r.GetString(1));
        }
        using var command = db.CreateCommand();
        // The SELECT is snapshotted before insertion; all parent identities stay
        // attached to their originals. Only this temporary test DB is mutated.
        command.CommandText = $"""
            WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<$Copies),
                seeds AS MATERIALIZED (SELECT * FROM Tests)
            INSERT INTO Tests({string.Join(',', columns)})
            SELECT {string.Join(',', columns.Select(c => c == "CycleId" ? "t.CycleId||'-copy-'||x" : "t.\"" + c + "\""))}
            FROM seeds t CROSS JOIN n;
            """;
        command.Parameters.AddWithValue("$Copies", copiesPerSeed);
        command.ExecuteNonQuery();
    }

    private static void TestHistoryPageOrdinalPrefixes()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-page-prefix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "history.db");
        try
        {
            var store = new TestHistoryStore(path);
            DateTime at = new(2026, 11, 18, 8, 0, 0);
            for (int i = 0; i < 8; i++)
                store.Add(new TestHistoryRecord
                {
                    CycleId = "prefix-" + i, PartNumber = i % 2 == 0 ? "PREFIX-A" : "PREFIX-B",
                    ModelFile = "PREFIX.tht", ProductionBatchKey = i < 4 ? "BATCH-1" : "BATCH-2",
                    Started = at.AddSeconds(i % 3), TestStartedAt = i % 2 == 0 ? null : at.AddSeconds(i % 3),
                    Finished = at.AddMinutes(20 - i), Passed = i % 3 != 0, Result = i % 3 != 0 ? "PASS" : "FAIL",
                    InspectionType = i == 7 ? HistoryInspectionType.MasterGood : HistoryInspectionType.Product
                });
            CopyHistoryFixtureRows(path, 150);
            var criteria = new HistorySearchCriteria(null, null, null, "", "ALL", MaxRows: 37);
            foreach (var filter in new[] { criteria, criteria with { Result = "FAIL" },
                         criteria with { PartKeyword = "PREFIX-B" },
                         criteria with { From = at.AddSeconds(1), To = at.AddSeconds(2) },
                         criteria with { InspectionType = HistoryInspectionType.MasterGood },
                         criteria with { From = at.AddDays(1) } })
            {
                var expected = store.EnumerateForExport(filter).Select(r => (r.Id, r.HistoryOrdinal)).ToArray();
                var actual = new List<(long, long?)>();
                var cursor = filter;
                while (true)
                {
                    var page = store.Search(cursor);
                    if (page.Count == 0) break;
                    actual.AddRange(page.Select(r => (r.Id, r.HistoryOrdinal)));
                    var last = page[^1];
                    cursor = filter with { BeforeHistoryAt = last.EffectiveTestStartedAt, BeforeId = last.Id };
                    Assert(actual.Count <= expected.Length, "History keyset cannot repeat already loaded rows");
                }
                Assert(actual.SequenceEqual(expected),
                    "Paged prefixes preserve global batch ordinals, legacy timestamps, ties and filters: " + filter);
                Assert(store.GetHistorySummary(filter).Total == expected.Length,
                    "Full summary remains independent of incremental page loading");
                Assert(store.Search(filter with { Offset = 37 }).Select(r => (r.Id, r.HistoryOrdinal))
                    .SequenceEqual(expected.Skip(37).Take(37)), "Offset callers retain the same ordering and ordinals");
            }
            using var db = new SqliteConnection("Data Source=" + path); db.Open();
            using var command = db.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
            Assert((string)command.ExecuteScalar()! == "ok", "History prefix reads preserve SQLite integrity");
            command.CommandText = "PRAGMA foreign_key_check";
            Assert(command.ExecuteScalar() is null, "History prefix reads preserve parent/child relationships");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private static void BenchmarkHistoryPagePrefixes()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-million-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "history.db");
        try
        {
            var store = new TestHistoryStore(path);
            DateTime at = new(2026, 11, 18, 8, 0, 0);
            store.Add(new TestHistoryRecord { CycleId = "million", PartNumber = "MILLION", ModelFile = "million.tht",
                Started = at, Finished = at, Passed = true, Result = "PASS" });
            CopyHistoryFixtureRows(path, 999_999);
            using var db = new SqliteConnection("Data Source=" + path); db.Open();
            foreach (int offset in new[] { 0, 999_800 })
            {
                var expected = new List<(long, long)>();
                var timer = Stopwatch.StartNew();
                using (var old = db.CreateCommand())
                {
                    old.CommandText = $"""
                        SELECT t.Id,o.Ordinal FROM Tests t LEFT JOIN (
                            SELECT Id,ROW_NUMBER() OVER(PARTITION BY PartId,ProductionBatchKey
                                ORDER BY COALESCE(TestStartedAt,StartedAt),Id) Ordinal
                            FROM Tests WHERE InspectionType='PRODUCT') o ON o.Id=t.Id
                        ORDER BY COALESCE(t.TestStartedAt,t.StartedAt),t.Id LIMIT 200 OFFSET {offset};
                        """;
                    using var rows = old.ExecuteReader();
                    while (rows.Read()) expected.Add((rows.GetInt64(0), rows.GetInt64(1)));
                }
                long oldMs = timer.ElapsedMilliseconds;
                timer.Restart();
                var criteria = new HistorySearchCriteria(null, null, null, "", "ALL");
                if (offset > 0) criteria = criteria with { BeforeHistoryAt = at, BeforeId = offset };
                var actual = store.Search(criteria);
                long newMs = timer.ElapsedMilliseconds;
                Assert(actual.Select(r => (r.Id, r.HistoryOrdinal!.Value)).SequenceEqual(expected),
                    "Million-row benchmark matches baseline ordinals at offset " + offset);
                Console.WriteLine($"HISTORY_MILLION offset={offset} baseline_ms={oldMs} optimized_ms={newMs} rows={actual.Count}");
            }
            using var check = db.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
            Assert((string)check.ExecuteScalar()! == "ok", "Million-row fixture integrity remains ok");
            Console.WriteLine("HISTORY_MILLION integrity=ok; fixture-only database; production untouched");
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
