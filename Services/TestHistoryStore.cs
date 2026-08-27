using System.IO;
using JBZUniversalTester.Models;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.Services;

public sealed class TestHistoryStore
{
    private readonly string _path;

    public TestHistoryStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(AppContext.BaseDirectory, "Data", "History", "test-history.db")
            : Path.GetFullPath(path);

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        Initialize();
    }

    public string DatabasePath => _path;

    private SqliteConnection Open() =>
        new($"Data Source={_path};Cache=Shared");

    private void Initialize()
    {
        using SqliteConnection connection = Open();
        connection.Open();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS TestHistory
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Started TEXT NOT NULL,
                    Finished TEXT NOT NULL,
                    PartName TEXT NOT NULL DEFAULT '',
                    PartNumber TEXT NOT NULL DEFAULT '',
                    VehicleType TEXT NOT NULL DEFAULT '',
                    Eco TEXT NOT NULL DEFAULT '',
                    Nco TEXT NOT NULL DEFAULT '',
                    Alc TEXT NOT NULL DEFAULT '',
                    LotNo INTEGER NOT NULL DEFAULT 0,
                    ProductionCounter INTEGER NOT NULL DEFAULT 0,
                    Result TEXT NOT NULL DEFAULT '',
                    Passed INTEGER NOT NULL DEFAULT 0,
                    ModelName TEXT NOT NULL DEFAULT '',
                    ModelFile TEXT NOT NULL DEFAULT '',
                    HtdrvName TEXT NOT NULL DEFAULT '',
                    OpenCount INTEGER NOT NULL DEFAULT 0,
                    WrongCount INTEGER NOT NULL DEFAULT 0,
                    ShortCount INTEGER NOT NULL DEFAULT 0,
                    Resistance TEXT NOT NULL DEFAULT '',
                    DeviceName TEXT NOT NULL DEFAULT '',
                    DeviceNumber TEXT NOT NULL DEFAULT '',
                    OperatorCompany TEXT NOT NULL DEFAULT '',
                    ProductionLine TEXT NOT NULL DEFAULT '',
                    FaultType TEXT NOT NULL DEFAULT '',
                    FaultCode TEXT NOT NULL DEFAULT '',
                    ExpectedSourceIo INTEGER NULL,
                    ExpectedTargetIo INTEGER NULL,
                    ActualSourceIo INTEGER NULL,
                    ActualTargetIo INTEGER NULL,
                    FaultDetailsJson TEXT NOT NULL DEFAULT '',
                    FaultSummary TEXT NOT NULL DEFAULT '',
                    MeasuredResistance REAL NULL,
                    ResistanceMin REAL NULL,
                    ResistanceMax REAL NULL,
                    CycleId TEXT NOT NULL DEFAULT '',
                    LabelSerial TEXT NOT NULL DEFAULT '',
                    BarcodeValue TEXT NOT NULL DEFAULT '',
                    LabelProfile TEXT NOT NULL DEFAULT '',
                    LabelTemplateType TEXT NOT NULL DEFAULT '',
                    LabelPayload TEXT NOT NULL DEFAULT '',
                    PrintStatus TEXT NOT NULL DEFAULT 'NotRequested',
                    PrintTimestamp TEXT NULL,
                    Printer TEXT NOT NULL DEFAULT '',
                    LabelCopies INTEGER NOT NULL DEFAULT 0,
                    ReprintCount INTEGER NOT NULL DEFAULT 0,
                    PrintMessage TEXT NOT NULL DEFAULT '',
                    InstallStartedAt TEXT NULL,
                    TestStartedAt TEXT NULL,
                    ResultAt TEXT NULL,
                    RemovalStartedAt TEXT NULL,
                    RemovedAt TEXT NULL,
                    InspectionType TEXT NOT NULL DEFAULT 'PRODUCT',
                    LotText TEXT NOT NULL DEFAULT '',
                    InspectionTrace TEXT NOT NULL DEFAULT ''
                );

                CREATE INDEX IF NOT EXISTS IX_TestHistory_Finished
                    ON TestHistory(Finished);
                CREATE INDEX IF NOT EXISTS IX_TestHistory_LotNo
                    ON TestHistory(LotNo);
                CREATE INDEX IF NOT EXISTS IX_TestHistory_PartNumber
                    ON TestHistory(PartNumber);
                CREATE INDEX IF NOT EXISTS IX_TestHistory_PartName
                    ON TestHistory(PartName);
                CREATE INDEX IF NOT EXISTS IX_TestHistory_ExportOrder
                    ON TestHistory(PartNumber, Started, Id);
                """;
            command.ExecuteNonQuery();
        }

        // Migration không phá DB V12.9 cũ: bổ sung từng cột nếu DB đã tồn tại.
        EnsureColumn(connection, "FaultType", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "VehicleType", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "ProductionCounter", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "FaultCode", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "ExpectedSourceIo", "INTEGER NULL");
        EnsureColumn(connection, "ExpectedTargetIo", "INTEGER NULL");
        EnsureColumn(connection, "ActualSourceIo", "INTEGER NULL");
        EnsureColumn(connection, "ActualTargetIo", "INTEGER NULL");
        EnsureColumn(connection, "FaultDetailsJson", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "FaultSummary", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "MeasuredResistance", "REAL NULL");
        EnsureColumn(connection, "ResistanceMin", "REAL NULL");
        EnsureColumn(connection, "ResistanceMax", "REAL NULL");
        EnsureColumn(connection, "CycleId", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "LabelSerial", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "BarcodeValue", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "LabelProfile", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "LabelTemplateType", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "LabelPayload", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "PrintStatus", "TEXT NOT NULL DEFAULT 'NotRequested'");
        EnsureColumn(connection, "PrintTimestamp", "TEXT NULL");
        EnsureColumn(connection, "Printer", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "LabelCopies", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "ReprintCount", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "PrintMessage", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "InstallStartedAt", "TEXT NULL");
        EnsureColumn(connection, "TestStartedAt", "TEXT NULL");
        EnsureColumn(connection, "ResultAt", "TEXT NULL");
        EnsureColumn(connection, "RemovalStartedAt", "TEXT NULL");
        EnsureColumn(connection, "RemovedAt", "TEXT NULL");
        EnsureColumn(connection, "InspectionType", "TEXT NOT NULL DEFAULT 'PRODUCT'");
        EnsureColumn(connection, "LotText", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "InspectionTrace", "TEXT NOT NULL DEFAULT ''");

        using SqliteCommand cycleIndex = connection.CreateCommand();
        cycleIndex.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS UX_TestHistory_CycleId
                ON TestHistory(CycleId)
                WHERE CycleId <> '';
            """;
        cycleIndex.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string columnName, string definition)
    {
        using SqliteCommand read = connection.CreateCommand();
        read.CommandText = "PRAGMA table_info(TestHistory);";
        using SqliteDataReader reader = read.ExecuteReader();
        bool exists = false;
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }
        reader.Close();

        if (exists)
            return;

        using SqliteCommand alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE TestHistory ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    public long Add(TestHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using SqliteConnection connection = Open();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO TestHistory
            (
                Started, Finished, PartName, PartNumber, VehicleType, Eco, Nco, Alc,
                LotNo, ProductionCounter, Result, Passed, ModelName, ModelFile, HtdrvName,
                OpenCount, WrongCount, ShortCount, Resistance,
                DeviceName, DeviceNumber, OperatorCompany, ProductionLine,
                FaultType, FaultCode, ExpectedSourceIo, ExpectedTargetIo,
                ActualSourceIo, ActualTargetIo, FaultDetailsJson, FaultSummary,
                MeasuredResistance, ResistanceMin, ResistanceMax,
                CycleId, LabelSerial, BarcodeValue, LabelProfile, PrintStatus,
                PrintTimestamp, Printer, LabelCopies, ReprintCount, PrintMessage,
                LabelTemplateType, LabelPayload,
                InstallStartedAt, TestStartedAt, ResultAt, RemovalStartedAt, RemovedAt,
                InspectionType, LotText, InspectionTrace
            )
            VALUES
            (
                $Started, $Finished, $PartName, $PartNumber, $VehicleType, $Eco, $Nco, $Alc,
                $LotNo, $ProductionCounter, $Result, $Passed, $ModelName, $ModelFile, $HtdrvName,
                $OpenCount, $WrongCount, $ShortCount, $Resistance,
                $DeviceName, $DeviceNumber, $OperatorCompany, $ProductionLine,
                $FaultType, $FaultCode, $ExpectedSourceIo, $ExpectedTargetIo,
                $ActualSourceIo, $ActualTargetIo, $FaultDetailsJson, $FaultSummary,
                $MeasuredResistance, $ResistanceMin, $ResistanceMax,
                $CycleId, $LabelSerial, $BarcodeValue, $LabelProfile, $PrintStatus,
                $PrintTimestamp, $Printer, $LabelCopies, $ReprintCount, $PrintMessage,
                $LabelTemplateType, $LabelPayload,
                $InstallStartedAt, $TestStartedAt, $ResultAt, $RemovalStartedAt, $RemovedAt,
                $InspectionType, $LotText, $InspectionTrace
            );
            SELECT last_insert_rowid();
            """;

        AddParameters(command, record);
        object? scalar = command.ExecuteScalar();
        long id = Convert.ToInt64(scalar ?? 0L);
        record.Id = id;
        return id;
    }

    public IReadOnlyList<TestHistoryRecord> Search(HistorySearchCriteria criteria) =>
        SearchCore(criteria, exportAll: false);

    public IReadOnlyList<TestHistoryRecord> SearchForExport(HistorySearchCriteria criteria) =>
        SearchCore(criteria, exportAll: true);

    private IReadOnlyList<TestHistoryRecord> SearchCore(
        HistorySearchCriteria criteria,
        bool exportAll)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        using SqliteConnection connection = Open();
        connection.Open();

        var clauses = new List<string>();
        using SqliteCommand command = connection.CreateCommand();

        if (criteria.From is DateTime from)
        {
            clauses.Add("Started >= $From");
            command.Parameters.AddWithValue("$From", from.ToString("O"));
        }

        if (criteria.To is DateTime to)
        {
            clauses.Add("Started <= $To");
            command.Parameters.AddWithValue("$To", to.ToString("O"));
        }

        if (criteria.LotNo is long lotNo)
        {
            clauses.Add("LotNo = $LotNo");
            command.Parameters.AddWithValue("$LotNo", lotNo);
        }

        if (!string.IsNullOrWhiteSpace(criteria.PartKeyword))
        {
            clauses.Add("(PartNumber LIKE $Part OR PartName LIKE $Part OR ModelName LIKE $Part OR FaultSummary LIKE $Part)");
            command.Parameters.AddWithValue("$Part", $"%{criteria.PartKeyword.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(criteria.Result) &&
            !criteria.Result.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            clauses.Add("(Result LIKE $Result OR FaultType LIKE $Result OR FaultCode LIKE $Result)");
            command.Parameters.AddWithValue("$Result", $"%{criteria.Result.Trim()}%");
        }

        string orderAndLimit;
        if (exportAll)
        {
            orderAndLimit = "ORDER BY PartNumber COLLATE NOCASE ASC, Started ASC, Id ASC";
        }
        else
        {
            int limit = Math.Clamp(criteria.MaxRows, 1, 50_000);
            orderAndLimit = $"ORDER BY Finished DESC, Id DESC LIMIT {limit}";
        }

        command.CommandText = $"""
            SELECT
                Id, Started, Finished, PartName, PartNumber, VehicleType, Eco, Nco, Alc,
                LotNo, ProductionCounter, Result, Passed, ModelName, ModelFile, HtdrvName,
                OpenCount, WrongCount, ShortCount, Resistance,
                DeviceName, DeviceNumber, OperatorCompany, ProductionLine,
                FaultType, FaultCode, ExpectedSourceIo, ExpectedTargetIo,
                ActualSourceIo, ActualTargetIo, FaultDetailsJson, FaultSummary,
                MeasuredResistance, ResistanceMin, ResistanceMax,
                CycleId, LabelSerial, BarcodeValue, LabelProfile, PrintStatus,
                PrintTimestamp, Printer, LabelCopies, ReprintCount, PrintMessage,
                LabelTemplateType, LabelPayload,
                InstallStartedAt, TestStartedAt, ResultAt, RemovalStartedAt, RemovedAt,
                InspectionType, LotText, InspectionTrace
            FROM TestHistory
            {(clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses))}
            {orderAndLimit};
            """;

        using SqliteDataReader reader = command.ExecuteReader();
        var result = new List<TestHistoryRecord>();

        while (reader.Read())
            result.Add(ReadRecord(reader));

        return result;
    }

    private static void AddParameters(SqliteCommand command, TestHistoryRecord record)
    {
        command.Parameters.AddWithValue("$Started", record.Started.ToString("O"));
        command.Parameters.AddWithValue("$Finished", record.Finished.ToString("O"));
        command.Parameters.AddWithValue("$PartName", record.PartName ?? string.Empty);
        command.Parameters.AddWithValue("$PartNumber", record.PartNumber ?? string.Empty);
        command.Parameters.AddWithValue("$VehicleType", record.VehicleType ?? string.Empty);
        command.Parameters.AddWithValue("$Eco", record.Eco ?? string.Empty);
        command.Parameters.AddWithValue("$Nco", record.Nco ?? string.Empty);
        command.Parameters.AddWithValue("$Alc", record.Alc ?? string.Empty);
        command.Parameters.AddWithValue("$LotNo", record.LotNo);
        command.Parameters.AddWithValue("$ProductionCounter", record.ProductionCounter);
        command.Parameters.AddWithValue("$Result", record.Result ?? string.Empty);
        command.Parameters.AddWithValue("$Passed", record.Passed ? 1 : 0);
        command.Parameters.AddWithValue("$ModelName", record.ModelName ?? string.Empty);
        command.Parameters.AddWithValue("$ModelFile", record.ModelFile ?? string.Empty);
        command.Parameters.AddWithValue("$HtdrvName", record.HtdrvName ?? string.Empty);
        command.Parameters.AddWithValue("$OpenCount", record.OpenCount);
        command.Parameters.AddWithValue("$WrongCount", record.WrongCount);
        command.Parameters.AddWithValue("$ShortCount", record.ShortCount);
        command.Parameters.AddWithValue("$Resistance", record.Resistance ?? string.Empty);
        command.Parameters.AddWithValue("$DeviceName", record.DeviceName ?? string.Empty);
        command.Parameters.AddWithValue("$DeviceNumber", record.DeviceNumber ?? string.Empty);
        command.Parameters.AddWithValue("$OperatorCompany", record.OperatorCompany ?? string.Empty);
        command.Parameters.AddWithValue("$ProductionLine", record.ProductionLine ?? string.Empty);
        command.Parameters.AddWithValue("$FaultType", record.FaultType ?? string.Empty);
        command.Parameters.AddWithValue("$FaultCode", record.FaultCode ?? string.Empty);
        AddNullable(command, "$ExpectedSourceIo", record.ExpectedSourceIo);
        AddNullable(command, "$ExpectedTargetIo", record.ExpectedTargetIo);
        AddNullable(command, "$ActualSourceIo", record.ActualSourceIo);
        AddNullable(command, "$ActualTargetIo", record.ActualTargetIo);
        command.Parameters.AddWithValue("$FaultDetailsJson", record.FaultDetailsJson ?? string.Empty);
        command.Parameters.AddWithValue("$FaultSummary", record.FaultSummary ?? string.Empty);
        AddNullable(command, "$MeasuredResistance", record.MeasuredResistance);
        AddNullable(command, "$ResistanceMin", record.ResistanceMin);
        AddNullable(command, "$ResistanceMax", record.ResistanceMax);
        command.Parameters.AddWithValue("$CycleId", record.CycleId ?? string.Empty);
        command.Parameters.AddWithValue("$LabelSerial", record.LabelSerial ?? string.Empty);
        command.Parameters.AddWithValue("$BarcodeValue", record.BarcodeValue ?? string.Empty);
        command.Parameters.AddWithValue("$LabelProfile", record.LabelProfile ?? string.Empty);
        command.Parameters.AddWithValue("$PrintStatus", record.PrintStatus ?? LabelPrintStatus.NotRequested.ToString());
        AddNullable(command, "$PrintTimestamp", record.PrintTimestamp?.ToString("O"));
        command.Parameters.AddWithValue("$Printer", record.Printer ?? string.Empty);
        command.Parameters.AddWithValue("$LabelCopies", record.LabelCopies);
        command.Parameters.AddWithValue("$ReprintCount", record.ReprintCount);
        command.Parameters.AddWithValue("$PrintMessage", record.PrintMessage ?? string.Empty);
        command.Parameters.AddWithValue("$LabelTemplateType", record.LabelTemplateType ?? string.Empty);
        command.Parameters.AddWithValue("$LabelPayload", record.LabelPayload ?? string.Empty);
        AddNullable(command, "$InstallStartedAt", record.InstallStartedAt?.ToString("O"));
        AddNullable(command, "$TestStartedAt", record.TestStartedAt?.ToString("O"));
        AddNullable(command, "$ResultAt", record.ResultAt?.ToString("O"));
        AddNullable(command, "$RemovalStartedAt", record.RemovalStartedAt?.ToString("O"));
        AddNullable(command, "$RemovedAt", record.RemovedAt?.ToString("O"));
        command.Parameters.AddWithValue(
            "$InspectionType",
            string.IsNullOrWhiteSpace(record.InspectionType)
                ? HistoryInspectionType.Product
                : record.InspectionType.Trim());
        command.Parameters.AddWithValue("$LotText", record.LotText ?? string.Empty);
        command.Parameters.AddWithValue("$InspectionTrace", record.InspectionTrace ?? string.Empty);
    }

    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static TestHistoryRecord ReadRecord(SqliteDataReader reader)
    {
        return new TestHistoryRecord
        {
            Id = reader.GetInt64(0),
            Started = ParseDate(reader.GetString(1)),
            Finished = ParseDate(reader.GetString(2)),
            PartName = reader.GetString(3),
            PartNumber = reader.GetString(4),
            VehicleType = reader.GetString(5),
            Eco = reader.GetString(6),
            Nco = reader.GetString(7),
            Alc = reader.GetString(8),
            LotNo = reader.GetInt64(9),
            ProductionCounter = reader.GetInt64(10),
            Result = reader.GetString(11),
            Passed = reader.GetInt64(12) != 0,
            ModelName = reader.GetString(13),
            ModelFile = reader.GetString(14),
            HtdrvName = reader.GetString(15),
            OpenCount = reader.GetInt32(16),
            WrongCount = reader.GetInt32(17),
            ShortCount = reader.GetInt32(18),
            Resistance = reader.GetString(19),
            DeviceName = reader.GetString(20),
            DeviceNumber = reader.GetString(21),
            OperatorCompany = reader.GetString(22),
            ProductionLine = reader.GetString(23),
            FaultType = reader.GetString(24),
            FaultCode = reader.GetString(25),
            ExpectedSourceIo = GetNullableInt(reader, 26),
            ExpectedTargetIo = GetNullableInt(reader, 27),
            ActualSourceIo = GetNullableInt(reader, 28),
            ActualTargetIo = GetNullableInt(reader, 29),
            FaultDetailsJson = reader.GetString(30),
            FaultSummary = reader.GetString(31),
            MeasuredResistance = GetNullableDouble(reader, 32),
            ResistanceMin = GetNullableDouble(reader, 33),
            ResistanceMax = GetNullableDouble(reader, 34),
            CycleId = reader.GetString(35),
            LabelSerial = reader.GetString(36),
            BarcodeValue = reader.GetString(37),
            LabelProfile = reader.GetString(38),
            PrintStatus = reader.GetString(39),
            PrintTimestamp = reader.IsDBNull(40) ? null : ParseDate(reader.GetString(40)),
            Printer = reader.GetString(41),
            LabelCopies = reader.GetInt32(42),
            ReprintCount = reader.GetInt32(43),
            PrintMessage = reader.GetString(44),
            LabelTemplateType = reader.GetString(45),
            LabelPayload = reader.GetString(46),
            InstallStartedAt = GetNullableDate(reader, 47),
            TestStartedAt = GetNullableDate(reader, 48),
            ResultAt = GetNullableDate(reader, 49),
            RemovalStartedAt = GetNullableDate(reader, 50),
            RemovedAt = GetNullableDate(reader, 51),
            InspectionType = reader.GetString(52),
            LotText = reader.GetString(53),
            InspectionTrace = reader.GetString(54)
        };
    }

    /// <summary>
    /// Persists the removal interval for one immutable production cycle. COALESCE
    /// protects the first observed timestamps from duplicate/stale callbacks.
    /// </summary>
    public bool UpdateRemovalTiming(string cycleId, DateTime removalStartedAt, DateTime? removedAt)
    {
        if (string.IsNullOrWhiteSpace(cycleId))
            return false;

        using SqliteConnection connection = Open();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TestHistory
            SET RemovalStartedAt = COALESCE(RemovalStartedAt, $RemovalStartedAt),
                RemovedAt = CASE
                    WHEN $RemovedAt IS NULL THEN RemovedAt
                    ELSE COALESCE(RemovedAt, $RemovedAt)
                END
            WHERE CycleId = $CycleId;
            """;
        command.Parameters.AddWithValue("$RemovalStartedAt", removalStartedAt.ToString("O"));
        AddNullable(command, "$RemovedAt", removedAt?.ToString("O"));
        command.Parameters.AddWithValue("$CycleId", cycleId.Trim());
        return command.ExecuteNonQuery() == 1;
    }

    /// <summary>
    /// Atomically claims the only allowed first-print transaction for a cycle.
    /// A Pending/Printed/Failed/Unknown transaction is never started again.
    /// </summary>
    public bool TryBeginFirstPrint(long historyId, string cycleId)
    {
        if (historyId <= 0 || string.IsNullOrWhiteSpace(cycleId))
            return false;

        using SqliteConnection connection = Open();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TestHistory
            SET PrintStatus = $Pending,
                PrintMessage = '',
                BarcodeValue = ''
            WHERE Id = $Id
              AND CycleId = $CycleId
              AND PrintStatus IN ($NotRequested, $Failed);
            """;
        command.Parameters.AddWithValue("$Pending", LabelPrintStatus.Pending.ToString());
        command.Parameters.AddWithValue("$NotRequested", LabelPrintStatus.NotRequested.ToString());
        command.Parameters.AddWithValue("$Failed", LabelPrintStatus.Failed.ToString());
        command.Parameters.AddWithValue("$Id", historyId);
        command.Parameters.AddWithValue("$CycleId", cycleId);
        return command.ExecuteNonQuery() == 1;
    }

    public void IncrementLabelReprint(long historyId, string cycleId, DateTime printedAt, string message)
    {
        using SqliteConnection connection = Open();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TestHistory
            SET ReprintCount = ReprintCount + 1,
                PrintTimestamp = $PrintTimestamp,
                PrintMessage = $PrintMessage
            WHERE Id = $Id
              AND CycleId = $CycleId
              AND PrintStatus = $Printed;
            """;
        command.Parameters.AddWithValue("$PrintTimestamp", printedAt.ToString("O"));
        command.Parameters.AddWithValue("$PrintMessage", message ?? string.Empty);
        command.Parameters.AddWithValue("$Id", historyId);
        command.Parameters.AddWithValue("$CycleId", cycleId);
        command.Parameters.AddWithValue("$Printed", LabelPrintStatus.Printed.ToString());
        command.ExecuteNonQuery();
    }

    public void UpdateLabelPrintOutcome(
        long historyId,
        string cycleId,
        LabelPrintStatus status,
        DateTime? printTimestamp,
        string message,
        string? printedBarcode = null)
    {
        if (historyId <= 0 || string.IsNullOrWhiteSpace(cycleId))
            return;

        using SqliteConnection connection = Open();
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE TestHistory
            SET PrintStatus = $Status,
                PrintTimestamp = $PrintTimestamp,
                PrintMessage = $PrintMessage,
                BarcodeValue = CASE
                    WHEN $Status = $Printed AND $BarcodeValue <> '' THEN $BarcodeValue
                    ELSE ''
                END
            WHERE Id = $Id
              AND CycleId = $CycleId;
            """;
        command.Parameters.AddWithValue("$Status", status.ToString());
        command.Parameters.AddWithValue("$Printed", LabelPrintStatus.Printed.ToString());
        command.Parameters.AddWithValue("$BarcodeValue", printedBarcode?.Trim() ?? string.Empty);
        AddNullable(command, "$PrintTimestamp", printTimestamp?.ToString("O"));
        command.Parameters.AddWithValue("$PrintMessage", message ?? string.Empty);
        command.Parameters.AddWithValue("$Id", historyId);
        command.Parameters.AddWithValue("$CycleId", cycleId);
        command.ExecuteNonQuery();
    }

    private static int? GetNullableInt(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static double? GetNullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static DateTime? GetNullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed)
            ? parsed
            : DateTime.MinValue;
}
