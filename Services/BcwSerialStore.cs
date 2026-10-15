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
                UpdatedAt TEXT NOT NULL, UNIQUE(Scope,Serial));
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Bcw_ActiveScope ON BcwPrints(Scope) WHERE State<>'Sent';
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static string Scope(DateTime localDate, string vehicle, string position) =>
        localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" + vehicle + "|" + position;

    public long Peek(DateTime localDate, string vehicle, string position)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Serial),0)+1 FROM BcwPrints WHERE Scope=$Scope AND State='Sent';";
        command.Parameters.AddWithValue("$Scope", Scope(localDate, vehicle, position));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public (LabelPrintRequest Request, bool AlreadySent) Prepare(LabelPrintRequest request, DateTime localNow, bool explicitReprint)
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
                if (state != "Sent" && !explicitReprint)
                    throw new InvalidOperationException("BCW: tem này đang chờ xử lý; dùng IN LẠI BCW để gửi đúng serial đã lưu.");
                var restored = saved with { PrinterCom = request.PrinterCom, PrinterName = request.PrinterName,
                    BaudRate = request.BaudRate, WriteTimeoutMs = request.WriteTimeoutMs, RawDestination = request.RawDestination };
                reader.Close();
                transaction.Commit();
                return (restored, state == "Sent" && !explicitReprint);
            }
        }
        string scope = Scope(localNow, request.Data.BcwVehicleCode, request.Data.BcwPosition);
        command.CommandText = "SELECT CycleId FROM BcwPrints WHERE Scope=$Scope AND State<>'Sent' LIMIT 1;";
        command.Parameters.AddWithValue("$Scope", scope);
        if (command.ExecuteScalar() is not null)
            throw new InvalidOperationException("BCW: có tem lỗi/chưa rõ kết quả trong ngày này. Kiểm tra máy in rồi dùng IN LẠI BCW trước khi tạo serial mới.");
        command.CommandText = "SELECT COALESCE(MAX(Serial),0)+1 FROM BcwPrints WHERE Scope=$Scope;";
        long sequence = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        LabelPrintRequest effective = BcwLabelService.Bind(request, localNow, sequence);
        command.CommandText = "INSERT INTO BcwPrints(CycleId,Scope,Serial,State,RequestJson,UpdatedAt) VALUES($Cycle,$Scope,$Serial,'Prepared',$Json,$At);";
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
