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
                    Eco TEXT NOT NULL DEFAULT '',
                    Nco TEXT NOT NULL DEFAULT '',
                    Alc TEXT NOT NULL DEFAULT '',
                    LotNo INTEGER NOT NULL DEFAULT 0,
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
                    ResistanceMax REAL NULL
                );

                CREATE INDEX IF NOT EXISTS IX_TestHistory_Finished
                    ON TestHistory(Finished);
                CREATE INDEX IF NOT EXISTS IX_TestHistory_LotNo
                    ON TestHistory(LotNo);
                CREATE INDEX IF NOT EXISTS IX_TestHistory_PartNumber
                    ON TestHistory(PartNumber);
                CREATE INDEX IF NOT EXISTS IX_TestHistory_PartName
                    ON TestHistory(PartName);
                """;
            command.ExecuteNonQuery();
        }

        // Migration không phá DB V12.9 cũ: bổ sung từng cột nếu DB đã tồn tại.
        EnsureColumn(connection, "FaultType", "TEXT NOT NULL DEFAULT ''");
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
                Started, Finished, PartName, PartNumber, Eco, Nco, Alc,
                LotNo, Result, Passed, ModelName, ModelFile, HtdrvName,
                OpenCount, WrongCount, ShortCount, Resistance,
                DeviceName, DeviceNumber, OperatorCompany, ProductionLine,
                FaultType, FaultCode, ExpectedSourceIo, ExpectedTargetIo,
                ActualSourceIo, ActualTargetIo, FaultDetailsJson, FaultSummary,
                MeasuredResistance, ResistanceMin, ResistanceMax
            )
            VALUES
            (
                $Started, $Finished, $PartName, $PartNumber, $Eco, $Nco, $Alc,
                $LotNo, $Result, $Passed, $ModelName, $ModelFile, $HtdrvName,
                $OpenCount, $WrongCount, $ShortCount, $Resistance,
                $DeviceName, $DeviceNumber, $OperatorCompany, $ProductionLine,
                $FaultType, $FaultCode, $ExpectedSourceIo, $ExpectedTargetIo,
                $ActualSourceIo, $ActualTargetIo, $FaultDetailsJson, $FaultSummary,
                $MeasuredResistance, $ResistanceMin, $ResistanceMax
            );
            SELECT last_insert_rowid();
            """;

        AddParameters(command, record);
        object? scalar = command.ExecuteScalar();
        long id = Convert.ToInt64(scalar ?? 0L);
        record.Id = id;
        return id;
    }

    public IReadOnlyList<TestHistoryRecord> Search(HistorySearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        using SqliteConnection connection = Open();
        connection.Open();

        var clauses = new List<string>();
        using SqliteCommand command = connection.CreateCommand();

        if (criteria.From is DateTime from)
        {
            clauses.Add("Finished >= $From");
            command.Parameters.AddWithValue("$From", from.ToString("O"));
        }

        if (criteria.To is DateTime to)
        {
            clauses.Add("Finished <= $To");
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

        int limit = Math.Clamp(criteria.MaxRows, 1, 50_000);
        command.CommandText = $"""
            SELECT
                Id, Started, Finished, PartName, PartNumber, Eco, Nco, Alc,
                LotNo, Result, Passed, ModelName, ModelFile, HtdrvName,
                OpenCount, WrongCount, ShortCount, Resistance,
                DeviceName, DeviceNumber, OperatorCompany, ProductionLine,
                FaultType, FaultCode, ExpectedSourceIo, ExpectedTargetIo,
                ActualSourceIo, ActualTargetIo, FaultDetailsJson, FaultSummary,
                MeasuredResistance, ResistanceMin, ResistanceMax
            FROM TestHistory
            {(clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses))}
            ORDER BY Finished DESC, Id DESC
            LIMIT {limit};
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
        command.Parameters.AddWithValue("$Eco", record.Eco ?? string.Empty);
        command.Parameters.AddWithValue("$Nco", record.Nco ?? string.Empty);
        command.Parameters.AddWithValue("$Alc", record.Alc ?? string.Empty);
        command.Parameters.AddWithValue("$LotNo", record.LotNo);
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
            Eco = reader.GetString(5),
            Nco = reader.GetString(6),
            Alc = reader.GetString(7),
            LotNo = reader.GetInt64(8),
            Result = reader.GetString(9),
            Passed = reader.GetInt64(10) != 0,
            ModelName = reader.GetString(11),
            ModelFile = reader.GetString(12),
            HtdrvName = reader.GetString(13),
            OpenCount = reader.GetInt32(14),
            WrongCount = reader.GetInt32(15),
            ShortCount = reader.GetInt32(16),
            Resistance = reader.GetString(17),
            DeviceName = reader.GetString(18),
            DeviceNumber = reader.GetString(19),
            OperatorCompany = reader.GetString(20),
            ProductionLine = reader.GetString(21),
            FaultType = reader.GetString(22),
            FaultCode = reader.GetString(23),
            ExpectedSourceIo = GetNullableInt(reader, 24),
            ExpectedTargetIo = GetNullableInt(reader, 25),
            ActualSourceIo = GetNullableInt(reader, 26),
            ActualTargetIo = GetNullableInt(reader, 27),
            FaultDetailsJson = reader.GetString(28),
            FaultSummary = reader.GetString(29),
            MeasuredResistance = GetNullableDouble(reader, 30),
            ResistanceMin = GetNullableDouble(reader, 31),
            ResistanceMax = GetNullableDouble(reader, 32)
        };
    }

    private static int? GetNullableInt(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static double? GetNullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime parsed)
            ? parsed
            : DateTime.MinValue;
}
