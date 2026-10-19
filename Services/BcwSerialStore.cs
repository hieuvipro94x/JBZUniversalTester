using System.Globalization;
using System.IO;
using System.Text.Json;
using JBZUniversalTester.Models;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.Services;

/// <summary>Independent label journal: never migrates or resets production History/LOT.</summary>
public sealed class BcwSerialStore
{
    private readonly string _path;
    private readonly bool _inheritPreviousVersion;
    private static readonly object InheritanceGate = new();
    public BcwSerialStore(string? path = null)
    {
        _path = path ?? Path.Combine(RuntimePaths.DataDirectory, "BCW-labels.db");
        _inheritPreviousVersion = path is null;
    }

    private SqliteConnection Open()
    {
        lock (InheritanceGate) return OpenCore();
    }

    private SqliteConnection OpenCore()
    {
        if (_inheritPreviousVersion)
        {
            lock (InheritanceGate)
                if (!File.Exists(_path)) ProductionDataUpgradeService.MigrateBcwForCurrentVersion();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _path, DefaultTimeout = 2, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA busy_timeout=2000;
            CREATE TABLE IF NOT EXISTS BcwPrints (
                CycleId TEXT PRIMARY KEY, Scope TEXT NOT NULL, Serial INTEGER NOT NULL,
                State TEXT NOT NULL, RequestJson TEXT NOT NULL, Message TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL, ProductionCapture INTEGER NOT NULL DEFAULT 0, UNIQUE(Scope,Serial));
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('BcwPrints') WHERE name='ProductionCapture';";
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            // The independent label journal changes its reservation policy. Keep
            // a SQLite-consistent copy and migrate without changing any row.
            command.CommandText = "SELECT COUNT(*) FROM BcwPrints;";
            long previousRows = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = _path + ".before-always-print-" + Guid.NewGuid().ToString("N") + ".db" }.ToString()))
            {
                backup.Open();
                connection.BackupDatabase(backup);
            }
            using var migration = connection.BeginTransaction(deferred: false);
            command.Transaction = migration;
            command.CommandText = """
                ALTER TABLE BcwPrints ADD COLUMN ProductionCapture INTEGER NOT NULL DEFAULT 0;
                DROP INDEX IF EXISTS IX_Bcw_ActiveScope;
                CREATE UNIQUE INDEX IX_Bcw_ActiveScope ON BcwPrints(Scope) WHERE State<>'Sent' AND ProductionCapture=0;
                """;
            command.ExecuteNonQuery();
            command.CommandText = "SELECT COUNT(*) FROM BcwPrints;";
            if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) != previousRows)
                throw new InvalidDataException("BCW journal migration changed the stored row count.");
            command.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("BCW journal integrity check failed; migration rolled back.");
            migration.Commit();
            command.Transaction = null;
        }
        command.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Bcw_ActiveScope ON BcwPrints(Scope) WHERE State<>'Sent' AND ProductionCapture=0;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static string Scope(DateTime localDate, string vehicle, string position) =>
        localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" + vehicle + "|" + position;

    public long Peek(DateTime localDate, string vehicle, string position)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Serial),0)+1 FROM BcwPrints WHERE Scope=$Scope AND (State='Sent' OR ProductionCapture=1);";
        command.Parameters.AddWithValue("$Scope", Scope(localDate, vehicle, position));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public (LabelPrintRequest Request, bool AlreadySent) Prepare(LabelPrintRequest request, DateTime localNow, bool explicitReprint,
        bool productionCapture = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CycleId);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT RequestJson,State FROM BcwPrints WHERE CycleId=$Cycle;";
        command.Parameters.AddWithValue("$Cycle", request.CycleId);
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                var saved = JsonSerializer.Deserialize<LabelPrintRequest>(reader.GetString(0))!;
                string state = reader.GetString(1);
                if (state != "Sent" && !explicitReprint && !productionCapture)
                    throw new InvalidOperationException("BCW: tem này đang chờ xử lý; dùng IN LẠI BCW để gửi đúng serial đã lưu.");
                var restored = saved with { PrinterCom = request.PrinterCom, PrinterName = request.PrinterName,
                    BaudRate = request.BaudRate, WriteTimeoutMs = request.WriteTimeoutMs, RawDestination = request.RawDestination };
                reader.Close();
                transaction.Commit();
                return (restored, state == "Sent" && !explicitReprint);
            }
        }
        string scope = Scope(localNow, request.Data.BcwVehicleCode, request.Data.BcwPosition);
        command.CommandText = "SELECT CycleId FROM BcwPrints WHERE Scope=$Scope AND State<>'Sent' AND ProductionCapture=0 LIMIT 1;";
        command.Parameters.AddWithValue("$Scope", scope);
        if (command.ExecuteScalar() is not null && !productionCapture)
            throw new InvalidOperationException("BCW: có tem lỗi/chưa rõ kết quả trong ngày này. Kiểm tra máy in rồi dùng IN LẠI BCW trước khi tạo serial mới.");
        command.CommandText = "SELECT COALESCE(MAX(Serial),0)+1 FROM BcwPrints WHERE Scope=$Scope;";
        long sequence = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        LabelPrintRequest effective = BcwLabelService.Bind(request, localNow, sequence);
        command.CommandText = "INSERT INTO BcwPrints(CycleId,Scope,Serial,State,RequestJson,UpdatedAt,ProductionCapture) VALUES($Cycle,$Scope,$Serial,'Prepared',$Json,$At,$Production);";
        command.Parameters.AddWithValue("$Production", productionCapture ? 1 : 0);
        command.Parameters.AddWithValue("$Serial", sequence);
        command.Parameters.AddWithValue("$Json", JsonSerializer.Serialize(effective));
        command.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        transaction.Commit();
        AsyncFileLogService.Current.Application($"BCW_PREPARED cycle={effective.CycleId} scope={scope} serial={sequence:D5} QR={effective.Data.Barcode}");
        return (effective, false);
    }

    public void Outcome(string cycleId, string state, string message)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE BcwPrints SET State=$State,Message=$Message,UpdatedAt=$At WHERE CycleId=$Cycle AND (State<>'Sent' OR $State='Sent');";
        command.Parameters.AddWithValue("$State", state);
        command.Parameters.AddWithValue("$Message", message);
        command.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Cycle", cycleId);
        command.ExecuteNonQuery();
        AsyncFileLogService.Current.Application($"BCW_STATE cycle={cycleId} state={state} message={message}");
    }

    public LabelPrintRequest? Latest(bool pendingOnly = false)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RequestJson FROM BcwPrints " + (pendingOnly ? "WHERE State<>'Sent' " : "") + "ORDER BY UpdatedAt DESC LIMIT 1;";
        string? json = command.ExecuteScalar() as string;
        return json is null ? null : JsonSerializer.Deserialize<LabelPrintRequest>(json);
    }

    public LabelPrintRequest? Find(string cycleId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RequestJson FROM BcwPrints WHERE CycleId=$Cycle;";
        command.Parameters.AddWithValue("$Cycle", cycleId);
        string? json = command.ExecuteScalar() as string;
        return json is null ? null : JsonSerializer.Deserialize<LabelPrintRequest>(json);
    }
}
