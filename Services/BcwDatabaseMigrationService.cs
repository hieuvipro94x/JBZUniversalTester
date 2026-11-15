using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.Services;

/// <summary>Adds the BCW journal to the initialized station DB, retaining every legacy source.</summary>
public static class BcwDatabaseMigrationService
{
    private const string MigrationKey = "BCW_SHARED_DATABASE_V1";
    private static readonly object Gate = new();
    private static readonly string[] HistoryTables =
        ["Tests", "TestFaults", "ResistanceMeasurements", "WaterProofMeasurements"];

    public static void EnsureForCurrentInstallation()
    {
        // Self-tests must never discover/import the real station's sidecars.
        Ensure(RuntimePaths.DatabaseFile, () =>
        {
            if (AppContext.GetData("JBZUniversalTester.SelfTest.DatabaseFile") is string) return [];
            string[] local = new[]
            {
                Path.Combine(RuntimePaths.AppDirectory, "Data", "BCW-labels.db"),
                Path.Combine(Path.GetDirectoryName(RuntimePaths.DatabaseFile)!, "BCW-labels.db")
            }.Where(File.Exists).ToArray();
            return local.Length > 0 ? local : ProductionDataUpgradeService
                .FindPreviousVersionDirectories(RuntimePaths.AppDirectory)
                .Select(path => Path.Combine(path, "Data", "BCW-labels.db"));
        });
    }

    public static void Ensure(string database, Func<IEnumerable<string>> legacySources)
    {
        lock (Gate)
        {
            string destination = Path.GetFullPath(database);
            // The History relocation/bootstrap owns DB creation. Never create an
            // empty station DB merely because a label consumer was opened early.
            using var connection = Open(destination, SqliteOpenMode.ReadWrite);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM RuntimeMigrationState WHERE MigrationKey=$Key;";
            command.Parameters.AddWithValue("$Key", MigrationKey);
            if (command.ExecuteScalar() is not null) return;
            using var migrationLock = new FileStream(destination + ".bcw-merge.lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            if (command.ExecuteScalar() is not null) return;

            string[] sources = legacySources().Where(File.Exists).Select(Path.GetFullPath)
                .Where(path => !string.Equals(path, destination, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            VerifyIntegrity(connection);
            string backup = destination + ".pre-bcw-merge-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fffffff", CultureInfo.InvariantCulture) + ".backup";
            using (var copy = Open(backup, SqliteOpenMode.ReadWriteCreate))
            {
                connection.BackupDatabase(copy);
                VerifyIntegrity(copy);
            }

            using (var transaction = connection.BeginTransaction(deferred: false))
            {
                command.Transaction = transaction;
                long[] before = HistoryTables.Select(table => Count(connection, transaction, table)).ToArray();
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS BcwPrints (
                        CycleId TEXT PRIMARY KEY, Scope TEXT NOT NULL, Serial INTEGER NOT NULL,
                        State TEXT NOT NULL, RequestJson TEXT NOT NULL, Message TEXT NOT NULL DEFAULT '',
                        UpdatedAt TEXT NOT NULL, ProductionCapture INTEGER NOT NULL DEFAULT 0, UNIQUE(Scope,Serial));
                    """;
                command.ExecuteNonQuery();
                if (!HasCaptureColumn(connection, transaction))
                {
                    command.CommandText = """
                        ALTER TABLE BcwPrints ADD COLUMN ProductionCapture INTEGER NOT NULL DEFAULT 0;
                        DROP INDEX IF EXISTS IX_Bcw_ActiveScope;
                        """;
                    command.ExecuteNonQuery();
                }
                command.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Bcw_ActiveScope ON BcwPrints(Scope) WHERE State<>'Sent' AND ProductionCapture=0;";
                command.ExecuteNonQuery();
                long existingLabels = Count(connection, transaction, "BcwPrints");
                long imported = 0;
                foreach (string source in sources)
                    imported += Import(connection, transaction, source);
                if (existingLabels + imported == 0)
                {
                    command.CommandText = "SELECT 1 FROM Tests WHERE LabelTemplateType='BCW' COLLATE NOCASE LIMIT 1;";
                    if (command.ExecuteScalar() is not null)
                        throw new InvalidDataException("BCW: History đã có tem BCW nhưng không tìm thấy nhật ký serial. Chưa tạo bộ đếm trống. Đặt DB BCW của máy đang vận hành tại Data\\BCW-labels.db cạnh EXE rồi mở lại.");
                }
                if (Count(connection, transaction, "BcwPrints") != existingLabels + imported ||
                    HistoryTables.Where((table, index) => Count(connection, transaction, table) != before[index]).Any())
                    throw new InvalidDataException("BCW merge changed an unexpected row count; migration rolled back.");
                command.CommandText = "PRAGMA foreign_key_check;";
                using (var reader = command.ExecuteReader())
                    if (reader.Read()) throw new InvalidDataException("BCW merge found orphan data; migration rolled back.");
                VerifyIntegrity(connection, transaction);
                command.CommandText = "INSERT INTO RuntimeMigrationState(MigrationKey,CompletedAt,Details) VALUES($Key,$At,$Details);";
                command.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$Details", $"imported={imported}; sources={string.Join("; ", sources)}; backup={backup}");
                command.ExecuteNonQuery();
                transaction.Commit();
            }
            VerifyIntegrity(connection);
            AsyncFileLogService.Current.Application($"BCW_DATABASE_MERGED destination={destination}; backup={backup}; sources retained; integrity=ok");
        }
    }

    private static long Import(SqliteConnection target, SqliteTransaction transaction, string path)
    {
        using var source = Open(path, SqliteOpenMode.ReadOnly);
        using var snapshot = source.BeginTransaction(deferred: true);
        VerifyIntegrity(source, snapshot);
        using var read = source.CreateCommand();
        read.Transaction = snapshot;
        read.CommandText = "SELECT CycleId,Scope,Serial,State,RequestJson,Message,UpdatedAt," +
            (HasCaptureColumn(source, snapshot) ? "ProductionCapture" : "0") + " FROM BcwPrints ORDER BY CycleId;";
        using var rows = read.ExecuteReader();
        using var find = target.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT CycleId,Scope,Serial,State,RequestJson,Message,UpdatedAt,ProductionCapture FROM BcwPrints WHERE CycleId=$Cycle;";
        find.Parameters.Add("$Cycle", SqliteType.Text);
        using var insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO BcwPrints(CycleId,Scope,Serial,State,RequestJson,Message,UpdatedAt,ProductionCapture) VALUES($V0,$V1,$V2,$V3,$V4,$V5,$V6,$V7);";
        for (int i = 0; i < 8; i++) insert.Parameters.Add(new SqliteParameter("$V" + i, DBNull.Value));
        long imported = 0;
        while (rows.Read())
        {
            find.Parameters[0].Value = rows.GetString(0);
            using (var existing = find.ExecuteReader())
            {
                if (existing.Read())
                {
                    for (int i = 0; i < 8; i++)
                        if (!Equals(existing.GetValue(i), rows.GetValue(i)))
                            throw Conflict(path, rows.GetString(0));
                    continue;
                }
            }
            for (int i = 0; i < 8; i++) insert.Parameters[i].Value = rows.GetValue(i);
            try { insert.ExecuteNonQuery(); }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            { throw Conflict(path, rows.GetString(0), ex); }
            imported++;
        }
        return imported;
    }

    private static InvalidDataException Conflict(string source, string cycle, Exception? inner = null) => new(
        $"BCW: dữ liệu CycleId/serial/trạng thái tem xung đột. Chưa gộp dữ liệu; giữ nguyên nguồn. Đặt DB BCW của máy đang vận hành tại Data\\BCW-labels.db cạnh EXE rồi mở lại. Source={source}; CycleId={cycle}", inner);

    private static bool HasCaptureColumn(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('BcwPrints') WHERE name='ProductionCapture';";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
    }

    private static long Count(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM " + table;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void VerifyIntegrity(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA integrity_check;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetString(0) != "ok") throw new InvalidDataException("BCW merge SQLite integrity_check: " + reader.GetString(0));
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 2 }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }
}
