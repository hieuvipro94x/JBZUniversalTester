using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using JBZUniversalTester.Models;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.Services;

/// <summary>
/// SQLite production repository. The old TestHistory table is retained as a
/// read-only migration source; all new reads/writes use the relational schema.
/// </summary>
public sealed class TestHistoryStore
{
    public const int CurrentSchemaVersion = 3;
    private static readonly object SchemaGate = new();
    private readonly string _path;

    public TestHistoryStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? RuntimePaths.DatabaseFile
            : Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        lock (SchemaGate)
            LastMigrationReport = Initialize();
    }

    public string DatabasePath => _path;
    public int SchemaVersion => CurrentSchemaVersion;
    public DatabaseMigrationReport LastMigrationReport { get; }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(
            $"Data Source={_path};Cache=Private;Pooling=True;Default Timeout=5");
        connection.Open();
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private DatabaseMigrationReport Initialize()
    {
        using SqliteConnection connection = Open();
        CreateMigrationBackupIfRequired(connection);
        using (SqliteCommand pragma = connection.CreateCommand())
        {
            // WAL lets History readers coexist with the serialized production
            // writer. NORMAL keeps WAL durable against process crash while
            // avoiding a full storage flush for every individual statement.
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
        }

        using SqliteTransaction transaction = connection.BeginTransaction();
        CreateSchema(connection, transaction);
        int existingVersion = ReadSchemaVersion(connection, transaction);
        if (existingVersion < CurrentSchemaVersion &&
            TableExists(connection, transaction, "TestHistory"))
        {
            EnsureLegacyColumns(connection, transaction);
        }
        DatabaseMigrationReport report = existingVersion < CurrentSchemaVersion
            ? MigrateLegacyHistory(connection, transaction)
            : ReadMigrationReport(connection, transaction);
        WriteSchemaInfo(connection, transaction, report);
        transaction.Commit();
        return report;
    }

    private void CreateMigrationBackupIfRequired(SqliteConnection source)
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length == 0)
            return;

        int version = 0;
        using (SqliteCommand tableProbe = source.CreateCommand())
        {
            tableProbe.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name='SchemaInfo' LIMIT 1;";
            if (tableProbe.ExecuteScalar() is not null)
            {
                using SqliteCommand versionProbe = source.CreateCommand();
                versionProbe.CommandText = "SELECT COALESCE(MAX(SchemaVersion), 0) FROM SchemaInfo;";
                version = Convert.ToInt32(
                    versionProbe.ExecuteScalar() ?? 0,
                    CultureInfo.InvariantCulture);
            }
        }
        if (version >= CurrentSchemaVersion)
            return;

        string backupPath = _path + $".pre-schema-v{CurrentSchemaVersion}.backup";
        if (File.Exists(backupPath))
            return;

        using var destination = new SqliteConnection($"Data Source={backupPath}");
        destination.Open();
        source.BackupDatabase(destination);
        AsyncFileLogService.Current.Application(
            $"DATABASE_MIGRATION_BACKUP schema={version}->{CurrentSchemaVersion} path={backupPath}");
    }

    private static void CreateSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SchemaInfo
            (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                SchemaVersion INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                AppVersion TEXT NOT NULL,
                MigrationVersion TEXT NOT NULL,
                MigrationReportJson TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS Parts
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PartKey TEXT NOT NULL UNIQUE,
                PartNumber TEXT NOT NULL DEFAULT '',
                PartName TEXT NOT NULL DEFAULT '',
                VehicleType TEXT NOT NULL DEFAULT '',
                Eco TEXT NOT NULL DEFAULT '',
                Nco TEXT NOT NULL DEFAULT '',
                Alc TEXT NOT NULL DEFAULT '',
                CustomerCode TEXT NOT NULL DEFAULT '',
                FirstUseAt TEXT NOT NULL,
                LastUseAt TEXT NOT NULL,
                TotalTests INTEGER NOT NULL DEFAULT 0,
                TotalPass INTEGER NOT NULL DEFAULT 0,
                TotalFail INTEGER NOT NULL DEFAULT 0,
                ProbeCounter INTEGER NOT NULL DEFAULT 0,
                ProbeReplacementThreshold INTEGER NOT NULL DEFAULT 200000,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Models
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PartId INTEGER NOT NULL,
                ModelKey TEXT NOT NULL UNIQUE,
                FilePath TEXT NOT NULL DEFAULT '',
                FileName TEXT NOT NULL DEFAULT '',
                FileHash TEXT NOT NULL DEFAULT '',
                FileLength INTEGER NOT NULL DEFAULT 0,
                FileModifiedAt TEXT NULL,
                ModelName TEXT NOT NULL DEFAULT '',
                MaxIo INTEGER NOT NULL DEFAULT 0,
                FirstUsedAt TEXT NOT NULL,
                LastUsedAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS ProductionRuns
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAt TEXT NOT NULL,
                FinishedAt TEXT NULL,
                DeviceName TEXT NOT NULL DEFAULT '',
                DeviceNumber TEXT NOT NULL DEFAULT '',
                ProductionLine TEXT NOT NULL DEFAULT '',
                OperatorCompany TEXT NOT NULL DEFAULT '',
                OperatorName TEXT NOT NULL DEFAULT '',
                AppVersion TEXT NOT NULL DEFAULT '',
                MachineName TEXT NOT NULL DEFAULT '',
                WindowsVersion TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ConfigSnapshots
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConfigHash TEXT NOT NULL UNIQUE,
                CreatedAt TEXT NOT NULL,
                AppVersion TEXT NOT NULL DEFAULT '',
                BoardMode TEXT NOT NULL DEFAULT '',
                ExpansionCardCount INTEGER NOT NULL DEFAULT 0,
                StartCardNumber INTEGER NOT NULL DEFAULT 0,
                UsbDelay INTEGER NOT NULL DEFAULT 0,
                RelayWiringMode INTEGER NOT NULL DEFAULT 0,
                JigEjectRelayEnabled INTEGER NOT NULL DEFAULT 0,
                PassMarkingRelayEnabled INTEGER NOT NULL DEFAULT 0,
                MasterFaultRequiredCount INTEGER NOT NULL DEFAULT 0,
                UseTestPointer INTEGER NOT NULL DEFAULT 0,
                ResistanceConfigJson TEXT NOT NULL DEFAULT '',
                WaterProofConfigJson TEXT NOT NULL DEFAULT '',
                LabelConfigJson TEXT NOT NULL DEFAULT '',
                MachineConfigJson TEXT NOT NULL DEFAULT '',
                FullConfigJson TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS Tests
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyHistoryId INTEGER NULL UNIQUE,
                CycleId TEXT NOT NULL UNIQUE,
                RunId INTEGER NULL,
                PartId INTEGER NOT NULL,
                ModelId INTEGER NOT NULL,
                ConfigId INTEGER NOT NULL,
                InspectionType TEXT NOT NULL DEFAULT 'PRODUCT',
                Lot INTEGER NOT NULL DEFAULT 0,
                ProductionCounter INTEGER NOT NULL DEFAULT 0,
                StartedAt TEXT NOT NULL,
                InstallStartedAt TEXT NULL,
                TestStartedAt TEXT NULL,
                ContinuityCompletedAt TEXT NULL,
                ResistanceStartedAt TEXT NULL,
                ResistanceCompletedAt TEXT NULL,
                WaterProofStartedAt TEXT NULL,
                WaterProofCompletedAt TEXT NULL,
                ResultAt TEXT NOT NULL,
                RemovalStartedAt TEXT NULL,
                RemovedAt TEXT NULL,
                FinishedAt TEXT NOT NULL,
                Passed INTEGER NOT NULL DEFAULT 0,
                Result TEXT NOT NULL DEFAULT '',
                ResultCode TEXT NOT NULL DEFAULT '',
                Barcode TEXT NOT NULL DEFAULT '',
                LabelSerial TEXT NOT NULL DEFAULT '',
                ResistanceSummary TEXT NOT NULL DEFAULT '',
                WaterProofSummary TEXT NOT NULL DEFAULT '',
                DeviceName TEXT NOT NULL DEFAULT '',
                DeviceNumber TEXT NOT NULL DEFAULT '',
                OperatorCompany TEXT NOT NULL DEFAULT '',
                ProductionLine TEXT NOT NULL DEFAULT '',
                AppVersion TEXT NOT NULL DEFAULT '',
                HtdrvName TEXT NOT NULL DEFAULT '',
                LotText TEXT NOT NULL DEFAULT '',
                InspectionTrace TEXT NOT NULL DEFAULT '',
                OpenCount INTEGER NOT NULL DEFAULT 0,
                WrongCount INTEGER NOT NULL DEFAULT 0,
                ShortCount INTEGER NOT NULL DEFAULT 0,
                FaultType TEXT NOT NULL DEFAULT '',
                FaultSummary TEXT NOT NULL DEFAULT '',
                FaultDetailsJson TEXT NOT NULL DEFAULT '',
                LabelProfile TEXT NOT NULL DEFAULT '',
                LabelTemplateType TEXT NOT NULL DEFAULT '',
                LabelPayload TEXT NOT NULL DEFAULT '',
                PrintStatus TEXT NOT NULL DEFAULT 'NotRequested',
                PrintTimestamp TEXT NULL,
                Printer TEXT NOT NULL DEFAULT '',
                LabelCopies INTEGER NOT NULL DEFAULT 0,
                ReprintCount INTEGER NOT NULL DEFAULT 0,
                PrintMessage TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (RunId) REFERENCES ProductionRuns(Id) ON DELETE RESTRICT,
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE RESTRICT,
                FOREIGN KEY (ModelId) REFERENCES Models(Id) ON DELETE RESTRICT,
                FOREIGN KEY (ConfigId) REFERENCES ConfigSnapshots(Id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS TestFaults
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TestId INTEGER NOT NULL,
                FaultOrder INTEGER NOT NULL,
                FaultType TEXT NOT NULL DEFAULT '',
                FaultCode TEXT NOT NULL DEFAULT '',
                Message TEXT NOT NULL DEFAULT '',
                ExpectedSourceIo INTEGER NULL,
                ExpectedTargetIo INTEGER NULL,
                ActualSourceIo INTEGER NULL,
                ActualTargetIo INTEGER NULL,
                ConnectorFrom TEXT NOT NULL DEFAULT '',
                PinFrom TEXT NOT NULL DEFAULT '',
                ConnectorTo TEXT NOT NULL DEFAULT '',
                PinTo TEXT NOT NULL DEFAULT '',
                ActualConnectorFrom TEXT NOT NULL DEFAULT '',
                ActualPinFrom TEXT NOT NULL DEFAULT '',
                ActualConnectorTo TEXT NOT NULL DEFAULT '',
                ActualPinTo TEXT NOT NULL DEFAULT '',
                WireName TEXT NOT NULL DEFAULT '',
                WireColor TEXT NOT NULL DEFAULT '',
                RelatedIosJson TEXT NOT NULL DEFAULT '',
                MeasuredResistance REAL NULL,
                ResistanceMin REAL NULL,
                ResistanceMax REAL NULL,
                WaterProofChannel INTEGER NULL,
                LeakValue REAL NULL,
                LeakLimit REAL NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (TestId) REFERENCES Tests(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ResistanceMeasurements
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TestId INTEGER NOT NULL,
                Channel INTEGER NOT NULL,
                Name TEXT NOT NULL DEFAULT '',
                MeasuredOhm REAL NULL,
                MinOhm REAL NOT NULL DEFAULT 0,
                MaxOhm REAL NOT NULL DEFAULT 0,
                Passed INTEGER NOT NULL DEFAULT 0,
                SampleCount INTEGER NOT NULL DEFAULT 0,
                StabilizationMs INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (TestId) REFERENCES Tests(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS WaterProofMeasurements
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TestId INTEGER NOT NULL,
                Channel INTEGER NOT NULL,
                Enabled INTEGER NOT NULL DEFAULT 0,
                FirstPressure REAL NOT NULL DEFAULT 0,
                SecondPressure REAL NOT NULL DEFAULT 0,
                Leak REAL NOT NULL DEFAULT 0,
                Passed INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (TestId) REFERENCES Tests(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ProbeMaintenanceEvents
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PartId INTEGER NOT NULL,
                EventType TEXT NOT NULL,
                CounterBefore INTEGER NOT NULL,
                CounterAfter INTEGER NOT NULL,
                ThresholdValue INTEGER NOT NULL,
                OccurredAt TEXT NOT NULL,
                OperatorName TEXT NOT NULL DEFAULT '',
                Memo TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE RESTRICT
            );

            CREATE TABLE IF NOT EXISTS LegacyImportState
            (
                Path TEXT PRIMARY KEY,
                Length INTEGER NOT NULL,
                LastWriteTime TEXT NOT NULL,
                ContentHash TEXT NOT NULL DEFAULT '',
                ImportedAt TEXT NOT NULL,
                RecordCount INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS RuntimeMigrationState
            (
                MigrationKey TEXT PRIMARY KEY,
                CompletedAt TEXT NOT NULL,
                Details TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_Models_PartId ON Models(PartId);
            CREATE INDEX IF NOT EXISTS IX_Models_FileHash ON Models(FileHash);
            CREATE INDEX IF NOT EXISTS IX_Models_FilePath ON Models(FilePath);
            CREATE INDEX IF NOT EXISTS IX_Tests_ResultAt ON Tests(ResultAt DESC);
            CREATE INDEX IF NOT EXISTS IX_Tests_Part_ResultAt ON Tests(PartId, ResultAt DESC);
            CREATE INDEX IF NOT EXISTS IX_Tests_Result ON Tests(Result);
            CREATE INDEX IF NOT EXISTS IX_Tests_InspectionType ON Tests(InspectionType);
            CREATE INDEX IF NOT EXISTS IX_Tests_Lot ON Tests(Lot);
            CREATE INDEX IF NOT EXISTS IX_Tests_AppVersion ON Tests(AppVersion);
            CREATE INDEX IF NOT EXISTS IX_TestFaults_TestId ON TestFaults(TestId);
            CREATE INDEX IF NOT EXISTS IX_TestFaults_Type ON TestFaults(FaultType);
            CREATE INDEX IF NOT EXISTS IX_TestFaults_ActualSourceIo ON TestFaults(ActualSourceIo);
            CREATE INDEX IF NOT EXISTS IX_TestFaults_ActualTargetIo ON TestFaults(ActualTargetIo);
            CREATE INDEX IF NOT EXISTS IX_TestFaults_ExpectedSourceIo ON TestFaults(ExpectedSourceIo);
            CREATE INDEX IF NOT EXISTS IX_TestFaults_ExpectedTargetIo ON TestFaults(ExpectedTargetIo);
            CREATE INDEX IF NOT EXISTS IX_TestFaults_WireName ON TestFaults(WireName);
            """;
        command.ExecuteNonQuery();
    }

    private static int ReadSchemaVersion(SqliteConnection connection, SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(SchemaVersion), 0) FROM SchemaInfo;";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private DatabaseMigrationReport MigrateLegacyHistory(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        if (!TableExists(connection, transaction, "TestHistory"))
            return BuildMigrationReport(connection, transaction, 0, 0, 0, 0, 0);

        List<TestHistoryRecord> legacyRows = ReadLegacyRows(connection, transaction);
        long migrated = 0;
        long faults = 0;
        long malformed = 0;
        long duplicates = 0;
        foreach (TestHistoryRecord row in legacyRows)
        {
            ProductionResultCommitRequest request = CreateLegacyRequest(row, ref malformed);
            ProductionCommitResult result = CommitCore(
                connection,
                transaction,
                request,
                runId: null,
                legacyHistoryId: row.Id);
            if (result.AlreadyCommitted)
                duplicates++;
            else
            {
                migrated++;
                faults += request.Faults.Count;
            }
        }

        RebuildPartAggregates(connection, transaction);
        return BuildMigrationReport(
            connection, transaction, legacyRows.Count, migrated, faults, malformed, duplicates);
    }

    private static bool TableExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$Name LIMIT 1;";
        command.Parameters.AddWithValue("$Name", tableName);
        return command.ExecuteScalar() is not null;
    }

    private static void EnsureLegacyColumns(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        (string Name, string Definition)[] columns =
        [
            ("VehicleType", "TEXT NOT NULL DEFAULT ''"),
            ("ProductionCounter", "INTEGER NOT NULL DEFAULT 0"),
            ("FaultType", "TEXT NOT NULL DEFAULT ''"),
            ("FaultCode", "TEXT NOT NULL DEFAULT ''"),
            ("ExpectedSourceIo", "INTEGER NULL"),
            ("ExpectedTargetIo", "INTEGER NULL"),
            ("ActualSourceIo", "INTEGER NULL"),
            ("ActualTargetIo", "INTEGER NULL"),
            ("FaultDetailsJson", "TEXT NOT NULL DEFAULT ''"),
            ("FaultSummary", "TEXT NOT NULL DEFAULT ''"),
            ("MeasuredResistance", "REAL NULL"),
            ("ResistanceMin", "REAL NULL"),
            ("ResistanceMax", "REAL NULL"),
            ("CycleId", "TEXT NOT NULL DEFAULT ''"),
            ("LabelSerial", "TEXT NOT NULL DEFAULT ''"),
            ("BarcodeValue", "TEXT NOT NULL DEFAULT ''"),
            ("LabelProfile", "TEXT NOT NULL DEFAULT ''"),
            ("LabelTemplateType", "TEXT NOT NULL DEFAULT ''"),
            ("LabelPayload", "TEXT NOT NULL DEFAULT ''"),
            ("PrintStatus", "TEXT NOT NULL DEFAULT 'NotRequested'"),
            ("PrintTimestamp", "TEXT NULL"),
            ("Printer", "TEXT NOT NULL DEFAULT ''"),
            ("LabelCopies", "INTEGER NOT NULL DEFAULT 0"),
            ("ReprintCount", "INTEGER NOT NULL DEFAULT 0"),
            ("PrintMessage", "TEXT NOT NULL DEFAULT ''"),
            ("InstallStartedAt", "TEXT NULL"),
            ("TestStartedAt", "TEXT NULL"),
            ("ResultAt", "TEXT NULL"),
            ("RemovalStartedAt", "TEXT NULL"),
            ("RemovedAt", "TEXT NULL"),
            ("InspectionType", "TEXT NOT NULL DEFAULT 'PRODUCT'"),
            ("LotText", "TEXT NOT NULL DEFAULT ''"),
            ("InspectionTrace", "TEXT NOT NULL DEFAULT ''")
        ];

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "PRAGMA table_info(TestHistory);";
            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
                existing.Add(reader.GetString(1));
        }

        foreach ((string name, string definition) in columns)
        {
            if (existing.Contains(name))
                continue;
            using SqliteCommand alter = connection.CreateCommand();
            alter.Transaction = transaction;
            alter.CommandText = $"ALTER TABLE TestHistory ADD COLUMN {name} {definition};";
            alter.ExecuteNonQuery();
        }
    }

    private static List<TestHistoryRecord> ReadLegacyRows(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
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
            FROM TestHistory ORDER BY Id;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<TestHistoryRecord>();
        while (reader.Read())
            rows.Add(ReadRecord(reader));
        return rows;
    }

    private static ProductionResultCommitRequest CreateLegacyRequest(
        TestHistoryRecord row,
        ref long malformedFaultJson)
    {
        string identity = !string.IsNullOrWhiteSpace(row.PartNumber)
            ? row.PartNumber
            : !string.IsNullOrWhiteSpace(row.ModelName) ? row.ModelName : row.ModelFile;
        var part = new PartIdentitySnapshot(
            (!string.IsNullOrWhiteSpace(row.PartNumber) ? "PN:" : "MODEL:") +
                PartIdentitySnapshot.NormalizeKey(identity),
            row.PartNumber,
            row.PartName,
            row.VehicleType,
            row.Eco,
            row.Nco,
            row.Alc,
            string.Empty);
        string modelIdentity = !string.IsNullOrWhiteSpace(row.ModelFile)
            ? row.ModelFile
            : row.ModelName;
        var model = new ModelIdentitySnapshot(
            "LEGACY:" + PartIdentitySnapshot.NormalizeKey(modelIdentity),
            row.ModelFile,
            Path.GetFileName(row.ModelFile),
            string.Empty,
            0,
            null,
            row.ModelName,
            0);
        string appVersion = string.IsNullOrWhiteSpace(row.HtdrvName)
            ? "LEGACY"
            : row.HtdrvName;
        var config = new ProductionConfigSnapshot(
            "LEGACY", appVersion, "", 0, 0, 0, 0, false, false, 0, false,
            "", "", "", "", "");

        List<FaultPersistenceSnapshot> faults = [];
        if (!string.IsNullOrWhiteSpace(row.FaultDetailsJson))
        {
            try
            {
                FaultDetail[] parsed = JsonSerializer.Deserialize<FaultDetail[]>(row.FaultDetailsJson) ?? [];
                faults.AddRange(parsed.Select(FaultPersistenceSnapshot.Capture));
            }
            catch
            {
                malformedFaultJson++;
            }
        }
        if (faults.Count == 0 &&
            (!string.IsNullOrWhiteSpace(row.FaultCode) || !string.IsNullOrWhiteSpace(row.FaultType)))
        {
            ProductFaultType type = ParseFaultType(row.FaultCode);
            faults.Add(FaultPersistenceSnapshot.Capture(new FaultDetail
            {
                Type = type,
                Message = row.FaultSummary,
                ExpectedSourceIo = row.ExpectedSourceIo,
                ExpectedTargetIo = row.ExpectedTargetIo,
                ActualSourceIo = row.ActualSourceIo,
                ActualTargetIo = row.ActualTargetIo,
                MeasuredResistance = row.MeasuredResistance,
                ResistanceMin = row.ResistanceMin,
                ResistanceMax = row.ResistanceMax
            }, 0));
        }

        if (string.IsNullOrWhiteSpace(row.CycleId))
            row.CycleId = $"legacy-sqlite-{row.Id}";
        return new(
            row.ClonePersistenceSnapshot(),
            part,
            model,
            config,
            faults,
            [],
            [],
            !HistoryInspectionType.IsMaster(row.InspectionType));
    }

    private static ProductFaultType ParseFaultType(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "OPEN_CIRCUIT" => ProductFaultType.OpenCircuit,
        "WRONG_WIRING" => ProductFaultType.WrongWiring,
        "SHORT_CIRCUIT" => ProductFaultType.ShortCircuit,
        "RESISTANCE_OUT_OF_RANGE" => ProductFaultType.ResistanceOutOfRange,
        "WATERPROOF_LEAK" => ProductFaultType.WaterProofLeak,
        "SYSTEM_DEVICE_ERROR" => ProductFaultType.SystemDeviceError,
        _ => ProductFaultType.None
    };

    private static void WriteSchemaInfo(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DatabaseMigrationReport report)
    {
        string now = DateTime.Now.ToString("O", CultureInfo.InvariantCulture);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SchemaInfo
                (Id, SchemaVersion, CreatedAt, UpdatedAt, AppVersion, MigrationVersion, MigrationReportJson)
            VALUES
                (1, $Version, $Now, $Now, $AppVersion, $MigrationVersion, $Report)
            ON CONFLICT(Id) DO UPDATE SET
                SchemaVersion=excluded.SchemaVersion,
                UpdatedAt=excluded.UpdatedAt,
                AppVersion=excluded.AppVersion,
                MigrationVersion=excluded.MigrationVersion,
                MigrationReportJson=excluded.MigrationReportJson;
            """;
        command.Parameters.AddWithValue("$Version", CurrentSchemaVersion);
        command.Parameters.AddWithValue("$Now", now);
        command.Parameters.AddWithValue("$AppVersion", ProgramIdentityService.VersionText);
        command.Parameters.AddWithValue("$MigrationVersion", "RELATIONAL_V2");
        command.Parameters.AddWithValue("$Report", JsonSerializer.Serialize(report));
        command.ExecuteNonQuery();
    }

    private static DatabaseMigrationReport ReadMigrationReport(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT MigrationReportJson FROM SchemaInfo WHERE Id=1;";
        string json = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
        try
        {
            return JsonSerializer.Deserialize<DatabaseMigrationReport>(json)
                ?? BuildMigrationReport(connection, transaction, 0, 0, 0, 0, 0);
        }
        catch
        {
            return BuildMigrationReport(connection, transaction, 0, 0, 0, 0, 0);
        }
    }

    private static DatabaseMigrationReport BuildMigrationReport(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long legacyTests,
        long migratedTests,
        long migratedFaults,
        long malformed,
        long duplicates)
    {
        long parts = ScalarLong(connection, transaction, "SELECT COUNT(*) FROM Parts;");
        long models = ScalarLong(connection, transaction, "SELECT COUNT(*) FROM Models;");
        long configs = ScalarLong(connection, transaction, "SELECT COUNT(*) FROM ConfigSnapshots;");
        long pass = ScalarLong(connection, transaction,
            "SELECT COUNT(*) FROM Tests WHERE InspectionType='PRODUCT' AND Passed=1;");
        long fail = ScalarLong(connection, transaction,
            "SELECT COUNT(*) FROM Tests WHERE InspectionType='PRODUCT' AND Passed=0;");
        return new(
            CurrentSchemaVersion,
            legacyTests,
            migratedTests,
            migratedFaults,
            malformed,
            parts,
            models,
            configs,
            duplicates,
            pass,
            fail);
    }

    private static long ScalarLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    public long StartProductionRun(ProductionSettings settings, string appVersion)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ProductionRuns
            (StartedAt, DeviceName, DeviceNumber, ProductionLine, OperatorCompany,
             OperatorName, AppVersion, MachineName, WindowsVersion, CreatedAt)
            VALUES
            ($Now, $DeviceName, $DeviceNumber, $Line, $Company, '', $AppVersion,
             $Machine, $Windows, $Now);
            SELECT last_insert_rowid();
            """;
        string now = DateTime.Now.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.AddWithValue("$Now", now);
        command.Parameters.AddWithValue("$DeviceName", settings.DeviceName ?? string.Empty);
        command.Parameters.AddWithValue("$DeviceNumber", settings.DeviceNumber ?? string.Empty);
        command.Parameters.AddWithValue("$Line", settings.ProductionLine ?? string.Empty);
        command.Parameters.AddWithValue("$Company", settings.OperatorCompany ?? string.Empty);
        command.Parameters.AddWithValue("$AppVersion", appVersion);
        command.Parameters.AddWithValue("$Machine", Environment.MachineName);
        command.Parameters.AddWithValue("$Windows", Environment.OSVersion.VersionString);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    public void FinishProductionRun(long runId)
    {
        if (runId <= 0) return;
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE ProductionRuns SET FinishedAt=COALESCE(FinishedAt,$Now) WHERE Id=$Id;";
        command.Parameters.AddWithValue("$Now", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Id", runId);
        command.ExecuteNonQuery();
    }

    public ProductionCommitResult CommitResult(ProductionResultCommitRequest request, long? runId)
    {
        ArgumentNullException.ThrowIfNull(request);
        long started = Stopwatch.GetTimestamp();
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        ProductionCommitResult result = CommitCore(connection, transaction, request, runId, null);
        transaction.Commit();
        AsyncFileLogService.Current.Performance(
            $"DB_COMMIT cycle={request.History.CycleId} test_id={result.TestId} " +
            $"duplicate={result.AlreadyCommitted} duration_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.###} " +
            $"FAULT_INSERT count={request.Faults.Count}");
        return result;
    }

    private static ProductionCommitResult CommitCore(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProductionResultCommitRequest request,
        long? runId,
        long? legacyHistoryId)
    {
        DateTime usedAt = request.History.ResultAt ?? request.History.Finished;
        long partId = UpsertPart(connection, transaction, request.Part, usedAt);
        long modelId = UpsertModel(connection, transaction, partId, request.Model, usedAt);
        long configId = UpsertConfig(connection, transaction, request.Config);
        long existingId = FindTestId(connection, transaction, request.History.CycleId);
        if (existingId > 0)
        {
            return BuildCommitResult(connection, transaction, existingId, true, partId, request.History);
        }

        long testId = InsertTest(
            connection, transaction, request, runId, partId, modelId, configId, legacyHistoryId);
        InsertFaults(connection, transaction, testId, request.Faults);
        InsertResistance(connection, transaction, testId, request.Resistance);
        InsertWaterProof(connection, transaction, testId, request.WaterProof);

        if (request.UpdateProductionTotals)
        {
            using SqliteCommand aggregate = connection.CreateCommand();
            aggregate.Transaction = transaction;
            aggregate.CommandText = """
                UPDATE Parts SET
                    TotalTests=TotalTests+1,
                    TotalPass=TotalPass+$Pass,
                    TotalFail=TotalFail+$Fail,
                    LastUseAt=$UsedAt,
                    UpdatedAt=$UsedAt
                WHERE Id=$PartId;
                """;
            aggregate.Parameters.AddWithValue("$Pass", request.History.Passed ? 1 : 0);
            aggregate.Parameters.AddWithValue("$Fail", request.History.Passed ? 0 : 1);
            aggregate.Parameters.AddWithValue("$UsedAt", usedAt.ToString("O", CultureInfo.InvariantCulture));
            aggregate.Parameters.AddWithValue("$PartId", partId);
            aggregate.ExecuteNonQuery();
        }

        return BuildCommitResult(connection, transaction, testId, false, partId, request.History);
    }

    private static long UpsertPart(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PartIdentitySnapshot part,
        DateTime usedAt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Parts
            (PartKey, PartNumber, PartName, VehicleType, Eco, Nco, Alc, CustomerCode,
             FirstUseAt, LastUseAt, CreatedAt, UpdatedAt)
            VALUES
            ($Key,$Number,$Name,$Vehicle,$Eco,$Nco,$Alc,$Customer,$At,$At,$At,$At)
            ON CONFLICT(PartKey) DO UPDATE SET
                PartNumber=excluded.PartNumber,
                PartName=excluded.PartName,
                VehicleType=excluded.VehicleType,
                Eco=excluded.Eco,
                Nco=excluded.Nco,
                Alc=excluded.Alc,
                CustomerCode=excluded.CustomerCode,
                LastUseAt=excluded.LastUseAt,
                UpdatedAt=excluded.UpdatedAt
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$Key", part.PartKey);
        command.Parameters.AddWithValue("$Number", part.PartNumber);
        command.Parameters.AddWithValue("$Name", part.PartName);
        command.Parameters.AddWithValue("$Vehicle", part.VehicleType);
        command.Parameters.AddWithValue("$Eco", part.Eco);
        command.Parameters.AddWithValue("$Nco", part.Nco);
        command.Parameters.AddWithValue("$Alc", part.Alc);
        command.Parameters.AddWithValue("$Customer", part.CustomerCode);
        command.Parameters.AddWithValue("$At", usedAt.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private static long UpsertModel(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long partId,
        ModelIdentitySnapshot model,
        DateTime usedAt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Models
            (PartId, ModelKey, FilePath, FileName, FileHash, FileLength, FileModifiedAt,
             ModelName, MaxIo, FirstUsedAt, LastUsedAt, CreatedAt, UpdatedAt)
            VALUES
            ($PartId,$Key,$Path,$File,$Hash,$Length,$Modified,$Name,$MaxIo,$At,$At,$At,$At)
            ON CONFLICT(ModelKey) DO UPDATE SET
                PartId=excluded.PartId,
                FilePath=excluded.FilePath,
                FileName=excluded.FileName,
                FileHash=excluded.FileHash,
                FileLength=excluded.FileLength,
                FileModifiedAt=excluded.FileModifiedAt,
                ModelName=excluded.ModelName,
                MaxIo=excluded.MaxIo,
                LastUsedAt=excluded.LastUsedAt,
                UpdatedAt=excluded.UpdatedAt
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$PartId", partId);
        command.Parameters.AddWithValue("$Key", model.ModelKey);
        command.Parameters.AddWithValue("$Path", model.FilePath);
        command.Parameters.AddWithValue("$File", model.FileName);
        command.Parameters.AddWithValue("$Hash", model.FileHash);
        command.Parameters.AddWithValue("$Length", model.FileLength);
        AddNullable(command, "$Modified", model.FileModifiedAt?.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Name", model.ModelName);
        command.Parameters.AddWithValue("$MaxIo", model.MaxIo);
        command.Parameters.AddWithValue("$At", usedAt.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private static long UpsertConfig(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProductionConfigSnapshot config)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ConfigSnapshots
            (ConfigHash,CreatedAt,AppVersion,BoardMode,ExpansionCardCount,StartCardNumber,
             UsbDelay,RelayWiringMode,JigEjectRelayEnabled,PassMarkingRelayEnabled,
             MasterFaultRequiredCount,UseTestPointer,ResistanceConfigJson,WaterProofConfigJson,
             LabelConfigJson,MachineConfigJson,FullConfigJson)
            VALUES
            ($Hash,$At,$App,$Board,$Cards,$Start,$Usb,$Relay,$Jig,$Marking,$Master,$Probe,
             $Resistance,$Water,$Label,$Machine,$Full)
            ON CONFLICT(ConfigHash) DO UPDATE SET ConfigHash=excluded.ConfigHash
            RETURNING Id;
            """;
        command.Parameters.AddWithValue("$Hash", config.ConfigHash);
        command.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$App", config.AppVersion);
        command.Parameters.AddWithValue("$Board", config.BoardMode);
        command.Parameters.AddWithValue("$Cards", config.ExpansionCardCount);
        command.Parameters.AddWithValue("$Start", config.StartCardNumber);
        command.Parameters.AddWithValue("$Usb", config.UsbDelay);
        command.Parameters.AddWithValue("$Relay", config.RelayWiringMode);
        command.Parameters.AddWithValue("$Jig", config.JigEjectRelayEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$Marking", config.PassMarkingRelayEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$Master", config.MasterFaultRequiredCount);
        command.Parameters.AddWithValue("$Probe", config.UseTestPointer ? 1 : 0);
        command.Parameters.AddWithValue("$Resistance", config.ResistanceConfigJson);
        command.Parameters.AddWithValue("$Water", config.WaterProofConfigJson);
        command.Parameters.AddWithValue("$Label", config.LabelConfigJson);
        command.Parameters.AddWithValue("$Machine", config.MachineConfigJson);
        command.Parameters.AddWithValue("$Full", config.FullConfigJson);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private static long FindTestId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string cycleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM Tests WHERE CycleId=$CycleId LIMIT 1;";
        command.Parameters.AddWithValue("$CycleId", cycleId);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private static long InsertTest(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProductionResultCommitRequest request,
        long? runId,
        long partId,
        long modelId,
        long configId,
        long? legacyHistoryId)
    {
        TestHistoryRecord h = request.History;
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Tests
            (LegacyHistoryId,CycleId,RunId,PartId,ModelId,ConfigId,InspectionType,Lot,
             ProductionCounter,StartedAt,InstallStartedAt,TestStartedAt,ResultAt,
             RemovalStartedAt,RemovedAt,FinishedAt,Passed,Result,ResultCode,Barcode,
             LabelSerial,ResistanceSummary,WaterProofSummary,DeviceName,DeviceNumber,
             OperatorCompany,ProductionLine,AppVersion,HtdrvName,LotText,InspectionTrace,
             OpenCount,WrongCount,ShortCount,FaultType,FaultSummary,FaultDetailsJson,
             LabelProfile,LabelTemplateType,LabelPayload,PrintStatus,PrintTimestamp,
             Printer,LabelCopies,ReprintCount,PrintMessage,CreatedAt)
            VALUES
            ($Legacy,$Cycle,$Run,$Part,$Model,$Config,$Inspection,$Lot,$Counter,$Started,
             $Install,$TestStarted,$ResultAt,$RemovalStarted,$Removed,$Finished,$Passed,
             $Result,$ResultCode,$Barcode,$LabelSerial,$Resistance,'',$DeviceName,$DeviceNumber,
             $Company,$Line,$App,$Htdrv,$LotText,$Trace,$Open,$Wrong,$Short,$FaultType,
             $FaultSummary,$FaultJson,$LabelProfile,$Template,$Payload,$PrintStatus,$PrintAt,
             $Printer,$Copies,$Reprints,$PrintMessage,$CreatedAt);
            SELECT last_insert_rowid();
            """;
        AddNullable(command, "$Legacy", legacyHistoryId);
        command.Parameters.AddWithValue("$Cycle", h.CycleId);
        AddNullable(command, "$Run", runId);
        command.Parameters.AddWithValue("$Part", partId);
        command.Parameters.AddWithValue("$Model", modelId);
        command.Parameters.AddWithValue("$Config", configId);
        command.Parameters.AddWithValue("$Inspection", h.InspectionType);
        command.Parameters.AddWithValue("$Lot", h.LotNo);
        command.Parameters.AddWithValue("$Counter", h.ProductionCounter);
        command.Parameters.AddWithValue("$Started", h.Started.ToString("O", CultureInfo.InvariantCulture));
        AddNullable(command, "$Install", h.InstallStartedAt?.ToString("O", CultureInfo.InvariantCulture));
        AddNullable(command, "$TestStarted", h.TestStartedAt?.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ResultAt", (h.ResultAt ?? h.Finished).ToString("O", CultureInfo.InvariantCulture));
        AddNullable(command, "$RemovalStarted", h.RemovalStartedAt?.ToString("O", CultureInfo.InvariantCulture));
        AddNullable(command, "$Removed", h.RemovedAt?.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Finished", h.Finished.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Passed", h.Passed ? 1 : 0);
        command.Parameters.AddWithValue("$Result", h.Result);
        command.Parameters.AddWithValue("$ResultCode", h.FaultCode);
        command.Parameters.AddWithValue("$Barcode", h.BarcodeValue);
        command.Parameters.AddWithValue("$LabelSerial", h.LabelSerial);
        command.Parameters.AddWithValue("$Resistance", h.Resistance);
        command.Parameters.AddWithValue("$DeviceName", h.DeviceName);
        command.Parameters.AddWithValue("$DeviceNumber", h.DeviceNumber);
        command.Parameters.AddWithValue("$Company", h.OperatorCompany);
        command.Parameters.AddWithValue("$Line", h.ProductionLine);
        command.Parameters.AddWithValue("$App", request.Config.AppVersion);
        command.Parameters.AddWithValue("$Htdrv", h.HtdrvName);
        command.Parameters.AddWithValue("$LotText", h.LotText);
        command.Parameters.AddWithValue("$Trace", h.InspectionTrace);
        command.Parameters.AddWithValue("$Open", h.OpenCount);
        command.Parameters.AddWithValue("$Wrong", h.WrongCount);
        command.Parameters.AddWithValue("$Short", h.ShortCount);
        command.Parameters.AddWithValue("$FaultType", h.FaultType);
        command.Parameters.AddWithValue("$FaultSummary", h.FaultSummary);
        command.Parameters.AddWithValue("$FaultJson", h.FaultDetailsJson);
        command.Parameters.AddWithValue("$LabelProfile", h.LabelProfile);
        command.Parameters.AddWithValue("$Template", h.LabelTemplateType);
        command.Parameters.AddWithValue("$Payload", h.LabelPayload);
        command.Parameters.AddWithValue("$PrintStatus", h.PrintStatus);
        AddNullable(command, "$PrintAt", h.PrintTimestamp?.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Printer", h.Printer);
        command.Parameters.AddWithValue("$Copies", h.LabelCopies);
        command.Parameters.AddWithValue("$Reprints", h.ReprintCount);
        command.Parameters.AddWithValue("$PrintMessage", h.PrintMessage);
        command.Parameters.AddWithValue("$CreatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private static void InsertFaults(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long testId,
        IReadOnlyList<FaultPersistenceSnapshot> faults)
    {
        foreach (FaultPersistenceSnapshot fault in faults)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO TestFaults
                (TestId,FaultOrder,FaultType,FaultCode,Message,ExpectedSourceIo,ExpectedTargetIo,
                 ActualSourceIo,ActualTargetIo,ConnectorFrom,PinFrom,ConnectorTo,PinTo,
                 ActualConnectorFrom,ActualPinFrom,ActualConnectorTo,ActualPinTo,WireName,
                 WireColor,RelatedIosJson,MeasuredResistance,ResistanceMin,ResistanceMax,CreatedAt)
                VALUES
                ($Test,$Order,$Type,$Code,$Message,$ES,$ET,$AS,$AT,$CF,$PF,$CT,$PT,
                 $ACF,$APF,$ACT,$APT,$Wire,$Color,$Related,$Measured,$Min,$Max,$At);
                """;
            command.Parameters.AddWithValue("$Test", testId);
            command.Parameters.AddWithValue("$Order", fault.Order);
            command.Parameters.AddWithValue("$Type", fault.Type.ToString());
            command.Parameters.AddWithValue("$Code", fault.Code);
            command.Parameters.AddWithValue("$Message", fault.Message);
            AddNullable(command, "$ES", fault.ExpectedSourceIo);
            AddNullable(command, "$ET", fault.ExpectedTargetIo);
            AddNullable(command, "$AS", fault.ActualSourceIo);
            AddNullable(command, "$AT", fault.ActualTargetIo);
            command.Parameters.AddWithValue("$CF", fault.ConnectorFrom);
            command.Parameters.AddWithValue("$PF", fault.PinFrom);
            command.Parameters.AddWithValue("$CT", fault.ConnectorTo);
            command.Parameters.AddWithValue("$PT", fault.PinTo);
            command.Parameters.AddWithValue("$ACF", fault.ActualConnectorFrom);
            command.Parameters.AddWithValue("$APF", fault.ActualPinFrom);
            command.Parameters.AddWithValue("$ACT", fault.ActualConnectorTo);
            command.Parameters.AddWithValue("$APT", fault.ActualPinTo);
            command.Parameters.AddWithValue("$Wire", fault.WireName);
            command.Parameters.AddWithValue("$Color", fault.WireColor);
            command.Parameters.AddWithValue("$Related", fault.RelatedIosJson);
            AddNullable(command, "$Measured", fault.MeasuredResistance);
            AddNullable(command, "$Min", fault.ResistanceMin);
            AddNullable(command, "$Max", fault.ResistanceMax);
            command.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }

    private static void InsertResistance(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long testId,
        IReadOnlyList<ResistancePersistenceSnapshot> rows)
    {
        foreach (ResistancePersistenceSnapshot row in rows)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO ResistanceMeasurements
                (TestId,Channel,Name,MeasuredOhm,MinOhm,MaxOhm,Passed,SampleCount,StabilizationMs)
                VALUES ($Test,$Channel,$Name,$Value,$Min,$Max,$Passed,$Samples,$Stabilization);
                """;
            command.Parameters.AddWithValue("$Test", testId);
            command.Parameters.AddWithValue("$Channel", row.Channel);
            command.Parameters.AddWithValue("$Name", row.Name);
            AddNullable(command, "$Value", row.MeasuredOhm);
            command.Parameters.AddWithValue("$Min", row.MinOhm);
            command.Parameters.AddWithValue("$Max", row.MaxOhm);
            command.Parameters.AddWithValue("$Passed", row.Passed ? 1 : 0);
            command.Parameters.AddWithValue("$Samples", row.SampleCount);
            command.Parameters.AddWithValue("$Stabilization", row.StabilizationMs);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertWaterProof(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long testId,
        IReadOnlyList<WaterProofPersistenceSnapshot> rows)
    {
        foreach (WaterProofPersistenceSnapshot row in rows)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO WaterProofMeasurements
                (TestId,Channel,Enabled,FirstPressure,SecondPressure,Leak,Passed)
                VALUES ($Test,$Channel,$Enabled,$First,$Second,$Leak,$Passed);
                """;
            command.Parameters.AddWithValue("$Test", testId);
            command.Parameters.AddWithValue("$Channel", row.Channel);
            command.Parameters.AddWithValue("$Enabled", row.Enabled ? 1 : 0);
            command.Parameters.AddWithValue("$First", row.FirstPressure);
            command.Parameters.AddWithValue("$Second", row.SecondPressure);
            command.Parameters.AddWithValue("$Leak", row.Leak);
            command.Parameters.AddWithValue("$Passed", row.Passed ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static ProductionCommitResult BuildCommitResult(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long testId,
        bool duplicate,
        long partId,
        TestHistoryRecord history)
    {
        ProductionStatisticsSnapshot statistics = QueryStatistics(
            connection, transaction, partId, history.ResultAt ?? history.Finished);
        ProbeCounterSnapshot probe = QueryProbeCounter(connection, transaction, partId);
        return new(testId, duplicate, statistics, probe);
    }

    public ProductionStatisticsSnapshot GetStatistics(PartIdentitySnapshot part, DateTime now)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand find = connection.CreateCommand();
        find.CommandText = "SELECT Id FROM Parts WHERE PartKey=$Key;";
        find.Parameters.AddWithValue("$Key", part.PartKey);
        long partId = Convert.ToInt64(find.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        if (partId <= 0)
            return new(0, 0, 0, 0, 0, 0, 0, 0, string.Empty);
        using SqliteTransaction transaction = connection.BeginTransaction();
        ProductionStatisticsSnapshot result = QueryStatistics(connection, transaction, partId, now);
        transaction.Commit();
        return result;
    }

    private static ProductionStatisticsSnapshot QueryStatistics(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long partId,
        DateTime now)
    {
        DateTime dayStart = now.Date;
        DateTime dayEnd = dayStart.AddDays(1);
        DateTime monthStart = new(now.Year, now.Month, 1);
        DateTime monthEnd = monthStart.AddMonths(1);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                SUM(CASE WHEN t.ResultAt >= $DayStart AND t.ResultAt < $DayEnd THEN 1 ELSE 0 END),
                SUM(CASE WHEN t.ResultAt >= $DayStart AND t.ResultAt < $DayEnd AND t.Passed=1 THEN 1 ELSE 0 END),
                SUM(CASE WHEN t.ResultAt >= $DayStart AND t.ResultAt < $DayEnd AND t.Passed=0 THEN 1 ELSE 0 END),
                SUM(CASE WHEN t.ResultAt >= $MonthStart AND t.ResultAt < $MonthEnd THEN 1 ELSE 0 END),
                p.TotalTests,p.TotalPass,p.TotalFail,
                COALESCE((SELECT Lot FROM Tests lt WHERE lt.PartId=p.Id AND lt.InspectionType='PRODUCT' ORDER BY lt.ResultAt DESC,lt.Id DESC LIMIT 1),0),
                COALESCE((SELECT Result FROM Tests rt WHERE rt.PartId=p.Id AND rt.InspectionType='PRODUCT' ORDER BY rt.ResultAt DESC,rt.Id DESC LIMIT 1),'')
            FROM Parts p
            LEFT JOIN Tests t ON t.PartId=p.Id AND t.InspectionType='PRODUCT'
            WHERE p.Id=$PartId
            GROUP BY p.Id;
            """;
        command.Parameters.AddWithValue("$DayStart", dayStart.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$DayEnd", dayEnd.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$MonthStart", monthStart.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$MonthEnd", monthEnd.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$PartId", partId);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return new(0, 0, 0, 0, 0, 0, 0, 0, string.Empty);
        return new(
            reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
            reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
            reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.GetString(8));
    }

    public ProbeCounterSnapshot GetProbeCounter(PartIdentitySnapshot part, long defaultThreshold)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        long partId = UpsertPart(connection, transaction, part, DateTime.Now);
        EnsureProbeThreshold(connection, transaction, partId, defaultThreshold);
        ProbeCounterSnapshot result = QueryProbeCounter(connection, transaction, partId);
        transaction.Commit();
        return result;
    }

    public IReadOnlyList<ProbeCounterSnapshot> GetAllProbeCounters()
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                CASE WHEN PartNumber<>'' THEN PartNumber
                     WHEN PartName<>'' THEN PartName
                     ELSE REPLACE(PartKey,'PN:','') END,
                ProbeReplacementThreshold,
                ProbeCounter
            FROM Parts
            ORDER BY PartNumber COLLATE NOCASE,PartName COLLATE NOCASE,PartKey COLLATE NOCASE;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var result = new List<ProbeCounterSnapshot>();
        while (reader.Read())
        {
            result.Add(new ProbeCounterSnapshot(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2)));
        }
        return result;
    }

    public int ImportPartCountersOnce(IReadOnlyList<PartCounterEntry> entries, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(entries);
        const string migrationKey = "PARTCNT_INITIAL_IMPORT_V1";
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM RuntimeMigrationState WHERE MigrationKey=$Key LIMIT 1;";
            check.Parameters.AddWithValue("$Key", migrationKey);
            if (check.ExecuteScalar() is not null)
            {
                transaction.Commit();
                return 0;
            }
        }

        int imported = 0;
        foreach (PartCounterEntry entry in entries)
        {
            string partNumber = entry.PartNumber.Trim();
            if (partNumber.Length == 0)
                continue;

            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Parts
                    (PartKey,PartNumber,PartName,ProbeCounter,ProbeReplacementThreshold,
                     FirstUseAt,LastUseAt,CreatedAt,UpdatedAt)
                VALUES ($Key,$Number,'',$Counter,$Threshold,$At,$At,$At,$At)
                ON CONFLICT(PartKey) DO UPDATE SET
                    ProbeCounter=CASE WHEN Parts.ProbeCounter=0 THEN excluded.ProbeCounter ELSE Parts.ProbeCounter END,
                    ProbeReplacementThreshold=CASE
                        WHEN Parts.ProbeCounter=0 OR Parts.ProbeReplacementThreshold<=0
                        THEN excluded.ProbeReplacementThreshold
                        ELSE Parts.ProbeReplacementThreshold END,
                    UpdatedAt=excluded.UpdatedAt;
                """;
            command.Parameters.AddWithValue("$Key", "PN:" + PartIdentitySnapshot.NormalizeKey(partNumber));
            command.Parameters.AddWithValue("$Number", partNumber);
            command.Parameters.AddWithValue("$Counter", Math.Max(0, entry.Counter));
            command.Parameters.AddWithValue("$Threshold", Math.Max(1, entry.ReplacementThreshold));
            command.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
            imported++;
        }

        using (SqliteCommand state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = """
                INSERT INTO RuntimeMigrationState (MigrationKey,CompletedAt,Details)
                VALUES ($Key,$At,$Details);
                """;
            state.Parameters.AddWithValue("$Key", migrationKey);
            state.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            state.Parameters.AddWithValue("$Details", $"source={sourcePath}; rows={imported}");
            state.ExecuteNonQuery();
        }

        transaction.Commit();
        return imported;
    }

    public bool IsRuntimeMigrationCompleted(string migrationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationKey);
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM RuntimeMigrationState WHERE MigrationKey=$Key LIMIT 1;";
        command.Parameters.AddWithValue("$Key", migrationKey);
        return command.ExecuteScalar() is not null;
    }

    public void CompleteRuntimeMigration(string migrationKey, string details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationKey);
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RuntimeMigrationState (MigrationKey,CompletedAt,Details)
            VALUES ($Key,$At,$Details)
            ON CONFLICT(MigrationKey) DO UPDATE SET
                CompletedAt=excluded.CompletedAt,Details=excluded.Details;
            """;
        command.Parameters.AddWithValue("$Key", migrationKey);
        command.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Details", details ?? string.Empty);
        command.ExecuteNonQuery();
    }

    public ProbeCounterSnapshot IncrementProbeCounter(PartIdentitySnapshot part, long threshold)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        long partId = UpsertPart(connection, transaction, part, DateTime.Now);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Parts SET
                ProbeCounter=ProbeCounter+1,
                ProbeReplacementThreshold=$Threshold,
                UpdatedAt=$At
            WHERE Id=$Id;
            """;
        command.Parameters.AddWithValue("$Threshold", Math.Max(1, threshold));
        command.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Id", partId);
        command.ExecuteNonQuery();
        ProbeCounterSnapshot result = QueryProbeCounter(connection, transaction, partId);
        transaction.Commit();
        return result;
    }

    public ProbeCounterSnapshot ResetProbeCounter(
        PartIdentitySnapshot part,
        long threshold,
        string operatorName,
        string memo)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        long partId = UpsertPart(connection, transaction, part, DateTime.Now);
        ProbeCounterSnapshot before = QueryProbeCounter(connection, transaction, partId);
        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Parts SET ProbeCounter=0,ProbeReplacementThreshold=$Threshold,UpdatedAt=$At
                WHERE Id=$Id;
                """;
            update.Parameters.AddWithValue("$Threshold", Math.Max(1, threshold));
            update.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            update.Parameters.AddWithValue("$Id", partId);
            update.ExecuteNonQuery();
        }
        using (SqliteCommand audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO ProbeMaintenanceEvents
                (PartId,EventType,CounterBefore,CounterAfter,ThresholdValue,OccurredAt,OperatorName,Memo)
                VALUES ($Part,'PROBE_PIN_REPLACED',$Before,0,$Threshold,$At,$Operator,$Memo);
                """;
            audit.Parameters.AddWithValue("$Part", partId);
            audit.Parameters.AddWithValue("$Before", before.Counter);
            audit.Parameters.AddWithValue("$Threshold", Math.Max(1, threshold));
            audit.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            audit.Parameters.AddWithValue("$Operator", operatorName ?? string.Empty);
            audit.Parameters.AddWithValue("$Memo", memo ?? string.Empty);
            audit.ExecuteNonQuery();
        }
        ProbeCounterSnapshot result = QueryProbeCounter(connection, transaction, partId);
        transaction.Commit();
        return result;
    }

    private static void EnsureProbeThreshold(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long partId,
        long threshold)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Parts SET ProbeReplacementThreshold=CASE
                WHEN ProbeReplacementThreshold<=0 THEN $Threshold ELSE ProbeReplacementThreshold END
            WHERE Id=$Id;
            """;
        command.Parameters.AddWithValue("$Threshold", Math.Max(1, threshold));
        command.Parameters.AddWithValue("$Id", partId);
        command.ExecuteNonQuery();
    }

    private static ProbeCounterSnapshot QueryProbeCounter(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long partId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT PartNumber,ProbeReplacementThreshold,ProbeCounter FROM Parts WHERE Id=$Id;";
        command.Parameters.AddWithValue("$Id", partId);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2))
            : new(string.Empty, 200_000, 0);
    }

    private static void RebuildPartAggregates(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Parts SET
                TotalTests=(SELECT COUNT(*) FROM Tests t WHERE t.PartId=Parts.Id AND t.InspectionType='PRODUCT'),
                TotalPass=(SELECT COUNT(*) FROM Tests t WHERE t.PartId=Parts.Id AND t.InspectionType='PRODUCT' AND t.Passed=1),
                TotalFail=(SELECT COUNT(*) FROM Tests t WHERE t.PartId=Parts.Id AND t.InspectionType='PRODUCT' AND t.Passed=0);
            """;
        command.ExecuteNonQuery();
    }

    public long Add(TestHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.CycleId))
            record.CycleId = Guid.NewGuid().ToString("N");
        long malformed = 0;
        ProductionResultCommitRequest request = CreateLegacyRequest(record, ref malformed);
        ProductionCommitResult result = CommitResult(request, null);
        record.Id = result.TestId;
        return result.TestId;
    }

    public LegacyImportResult ImportLegacyFile(
        LegacyImportFile file,
        string contentHash,
        IReadOnlyList<TestHistoryRecord> records)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(records);
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        if (IsLegacyFileUnchanged(connection, transaction, file, contentHash))
        {
            transaction.Commit();
            return new LegacyImportResult(file.Path, records.Count, 0, records.Count, true);
        }

        int imported = 0;
        int existing = 0;
        long malformed = 0;
        foreach (TestHistoryRecord record in records)
        {
            ProductionResultCommitRequest request = CreateLegacyRequest(record, ref malformed);
            if (FindEquivalentLegacyTest(connection, transaction, request) > 0)
            {
                existing++;
                continue;
            }

            ProductionCommitResult result = CommitCore(
                connection, transaction, request, runId: null, legacyHistoryId: null);
            if (result.AlreadyCommitted)
                existing++;
            else
                imported++;
        }

        using (SqliteCommand state = connection.CreateCommand())
        {
            state.Transaction = transaction;
            state.CommandText = """
                INSERT INTO LegacyImportState
                    (Path,Length,LastWriteTime,ContentHash,ImportedAt,RecordCount)
                VALUES ($Path,$Length,$Write,$Hash,$At,$Count)
                ON CONFLICT(Path) DO UPDATE SET
                    Length=excluded.Length,
                    LastWriteTime=excluded.LastWriteTime,
                    ContentHash=excluded.ContentHash,
                    ImportedAt=excluded.ImportedAt,
                    RecordCount=excluded.RecordCount;
                """;
            state.Parameters.AddWithValue("$Path", file.Path);
            state.Parameters.AddWithValue("$Length", file.Length);
            state.Parameters.AddWithValue("$Write", file.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
            state.Parameters.AddWithValue("$Hash", contentHash);
            state.Parameters.AddWithValue("$At", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            state.Parameters.AddWithValue("$Count", records.Count);
            state.ExecuteNonQuery();
        }

        RebuildPartAggregates(connection, transaction);
        transaction.Commit();
        AsyncFileLogService.Current.Performance(
            $"LEGACY_IMPORT records={records.Count} imported={imported} existing={existing} malformed={malformed} file={file.Path}");
        return new LegacyImportResult(file.Path, records.Count, imported, existing, false);
    }

    public bool IsLegacyImportRequired(LegacyImportFile file, string contentHash)
    {
        using SqliteConnection connection = Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        bool required = !IsLegacyFileUnchanged(connection, transaction, file, contentHash);
        transaction.Commit();
        return required;
    }

    private static bool IsLegacyFileUnchanged(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LegacyImportFile file,
        string contentHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM LegacyImportState
            WHERE Path=$Path AND Length=$Length AND LastWriteTime=$Write AND ContentHash=$Hash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$Path", file.Path);
        command.Parameters.AddWithValue("$Length", file.Length);
        command.Parameters.AddWithValue("$Write", file.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Hash", contentHash);
        return command.ExecuteScalar() is not null;
    }

    private static long FindEquivalentLegacyTest(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProductionResultCommitRequest request)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT t.Id
            FROM Tests t
            JOIN Parts p ON p.Id=t.PartId
            WHERE p.PartKey=$PartKey AND t.Lot=$Lot AND t.Passed=$Passed
              AND t.FinishedAt=$Finished AND t.InspectionType=$Inspection
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$PartKey", request.Part.PartKey);
        command.Parameters.AddWithValue("$Lot", request.History.LotNo);
        command.Parameters.AddWithValue("$Passed", request.History.Passed ? 1 : 0);
        command.Parameters.AddWithValue("$Finished", request.History.Finished.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Inspection", request.History.InspectionType);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<TestHistoryRecord> Search(HistorySearchCriteria criteria) =>
        SearchCore(criteria, exportAll: false);

    public IReadOnlyList<TestHistoryRecord> SearchForExport(HistorySearchCriteria criteria) =>
        SearchCore(criteria, exportAll: true);

    private IReadOnlyList<TestHistoryRecord> SearchCore(HistorySearchCriteria criteria, bool exportAll)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        long started = Stopwatch.GetTimestamp();
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        var clauses = new List<string>();
        if (criteria.From is DateTime from)
        {
            clauses.Add("t.StartedAt >= $From");
            command.Parameters.AddWithValue("$From", from.ToString("O", CultureInfo.InvariantCulture));
        }
        if (criteria.To is DateTime to)
        {
            clauses.Add("t.StartedAt <= $To");
            command.Parameters.AddWithValue("$To", to.ToString("O", CultureInfo.InvariantCulture));
        }
        if (criteria.LotNo is long lot)
        {
            clauses.Add("t.Lot=$Lot");
            command.Parameters.AddWithValue("$Lot", lot);
        }
        if (!string.IsNullOrWhiteSpace(criteria.PartKeyword))
        {
            clauses.Add("(p.PartNumber LIKE $Part OR p.PartName LIKE $Part OR m.ModelName LIKE $Part OR t.FaultSummary LIKE $Part)");
            command.Parameters.AddWithValue("$Part", $"%{criteria.PartKeyword.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(criteria.Result) &&
            !criteria.Result.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            clauses.Add("(t.Result LIKE $Result OR t.FaultType LIKE $Result OR t.ResultCode LIKE $Result)");
            command.Parameters.AddWithValue("$Result", $"%{criteria.Result.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(criteria.InspectionType))
        {
            clauses.Add("t.InspectionType=$Inspection");
            command.Parameters.AddWithValue("$Inspection", criteria.InspectionType.Trim());
        }
        if (!string.IsNullOrWhiteSpace(criteria.FaultType))
        {
            clauses.Add("EXISTS(SELECT 1 FROM TestFaults f WHERE f.TestId=t.Id AND (f.FaultType LIKE $Fault OR f.FaultCode LIKE $Fault))");
            command.Parameters.AddWithValue("$Fault", $"%{criteria.FaultType.Trim()}%");
        }
        if (criteria.Io is int io)
        {
            clauses.Add("EXISTS(SELECT 1 FROM TestFaults f WHERE f.TestId=t.Id AND $Io IN (f.ExpectedSourceIo,f.ExpectedTargetIo,f.ActualSourceIo,f.ActualTargetIo))");
            command.Parameters.AddWithValue("$Io", io);
        }
        if (!string.IsNullOrWhiteSpace(criteria.WireName))
        {
            clauses.Add("EXISTS(SELECT 1 FROM TestFaults f WHERE f.TestId=t.Id AND f.WireName LIKE $Wire)");
            command.Parameters.AddWithValue("$Wire", $"%{criteria.WireName.Trim()}%");
        }
        if (!string.IsNullOrWhiteSpace(criteria.CycleId))
        {
            clauses.Add("t.CycleId=$Cycle");
            command.Parameters.AddWithValue("$Cycle", criteria.CycleId.Trim());
        }
        if (!string.IsNullOrWhiteSpace(criteria.AppVersion))
        {
            clauses.Add("t.AppVersion LIKE $AppVersion");
            command.Parameters.AddWithValue("$AppVersion", $"%{criteria.AppVersion.Trim()}%");
        }

        int limit = Math.Clamp(criteria.MaxRows, 1, 50_000);
        int offset = Math.Max(0, criteria.Offset);
        string order = exportAll
            ? "ORDER BY p.PartNumber COLLATE NOCASE,t.StartedAt,t.Id"
            : $"ORDER BY t.FinishedAt DESC,t.Id DESC LIMIT {limit} OFFSET {offset}";
        command.CommandText = $"""
            SELECT
                t.Id,t.StartedAt,t.FinishedAt,p.PartName,p.PartNumber,p.VehicleType,p.Eco,p.Nco,p.Alc,
                t.Lot,t.ProductionCounter,t.Result,t.Passed,m.ModelName,m.FilePath,t.HtdrvName,
                t.OpenCount,t.WrongCount,t.ShortCount,t.ResistanceSummary,
                t.DeviceName,t.DeviceNumber,t.OperatorCompany,t.ProductionLine,
                t.FaultType,t.ResultCode,
                (SELECT ExpectedSourceIo FROM TestFaults WHERE TestId=t.Id ORDER BY FaultOrder LIMIT 1),
                (SELECT ExpectedTargetIo FROM TestFaults WHERE TestId=t.Id ORDER BY FaultOrder LIMIT 1),
                (SELECT ActualSourceIo FROM TestFaults WHERE TestId=t.Id ORDER BY FaultOrder LIMIT 1),
                (SELECT ActualTargetIo FROM TestFaults WHERE TestId=t.Id ORDER BY FaultOrder LIMIT 1),
                t.FaultDetailsJson,t.FaultSummary,
                (SELECT MeasuredResistance FROM TestFaults WHERE TestId=t.Id ORDER BY FaultOrder LIMIT 1),
                (SELECT ResistanceMin FROM TestFaults WHERE TestId=t.Id ORDER BY FaultOrder LIMIT 1),
                (SELECT ResistanceMax FROM TestFaults WHERE TestId=t.Id ORDER BY FaultOrder LIMIT 1),
                t.CycleId,t.LabelSerial,t.Barcode,t.LabelProfile,t.PrintStatus,t.PrintTimestamp,
                t.Printer,t.LabelCopies,t.ReprintCount,t.PrintMessage,t.LabelTemplateType,t.LabelPayload,
                t.InstallStartedAt,t.TestStartedAt,t.ResultAt,t.RemovalStartedAt,t.RemovedAt,
                t.InspectionType,t.LotText,t.InspectionTrace
            FROM Tests t
            JOIN Parts p ON p.Id=t.PartId
            JOIN Models m ON m.Id=t.ModelId
            {(clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses))}
            {order};
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<TestHistoryRecord>();
        while (reader.Read())
            rows.Add(ReadRecord(reader));
        AsyncFileLogService.Current.Performance(
            $"HISTORY_QUERY rows={rows.Count} duration_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.###}");
        return rows;
    }

    public bool UpdateRemovalTiming(string cycleId, DateTime removalStartedAt, DateTime? removedAt)
    {
        if (string.IsNullOrWhiteSpace(cycleId)) return false;
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Tests SET
                RemovalStartedAt=COALESCE(RemovalStartedAt,$Started),
                RemovedAt=CASE WHEN $Removed IS NULL THEN RemovedAt ELSE COALESCE(RemovedAt,$Removed) END
            WHERE CycleId=$Cycle;
            """;
        command.Parameters.AddWithValue("$Started", removalStartedAt.ToString("O", CultureInfo.InvariantCulture));
        AddNullable(command, "$Removed", removedAt?.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Cycle", cycleId.Trim());
        return command.ExecuteNonQuery() == 1;
    }

    public bool TryBeginFirstPrint(long historyId, string cycleId)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Tests SET PrintStatus=$Pending,PrintMessage='',Barcode=''
            WHERE Id=$Id AND CycleId=$Cycle AND PrintStatus IN ($None,$Failed);
            """;
        command.Parameters.AddWithValue("$Pending", LabelPrintStatus.Pending.ToString());
        command.Parameters.AddWithValue("$None", LabelPrintStatus.NotRequested.ToString());
        command.Parameters.AddWithValue("$Failed", LabelPrintStatus.Failed.ToString());
        command.Parameters.AddWithValue("$Id", historyId);
        command.Parameters.AddWithValue("$Cycle", cycleId);
        return command.ExecuteNonQuery() == 1;
    }

    public void IncrementLabelReprint(long historyId, string cycleId, DateTime printedAt, string message)
    {
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Tests SET ReprintCount=ReprintCount+1,PrintTimestamp=$At,PrintMessage=$Message
            WHERE Id=$Id AND CycleId=$Cycle AND PrintStatus=$Printed;
            """;
        command.Parameters.AddWithValue("$At", printedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Message", message ?? string.Empty);
        command.Parameters.AddWithValue("$Id", historyId);
        command.Parameters.AddWithValue("$Cycle", cycleId);
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
        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Tests SET PrintStatus=$Status,PrintTimestamp=$At,PrintMessage=$Message,
                Barcode=CASE WHEN $Status=$Printed AND $Barcode<>'' THEN $Barcode ELSE '' END
            WHERE Id=$Id AND CycleId=$Cycle;
            """;
        command.Parameters.AddWithValue("$Status", status.ToString());
        command.Parameters.AddWithValue("$Printed", LabelPrintStatus.Printed.ToString());
        command.Parameters.AddWithValue("$Barcode", printedBarcode?.Trim() ?? string.Empty);
        AddNullable(command, "$At", printTimestamp?.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$Message", message ?? string.Empty);
        command.Parameters.AddWithValue("$Id", historyId);
        command.Parameters.AddWithValue("$Cycle", cycleId);
        command.ExecuteNonQuery();
    }

    private static TestHistoryRecord ReadRecord(SqliteDataReader reader) => new()
    {
        Id=reader.GetInt64(0), Started=ParseDate(reader.GetString(1)), Finished=ParseDate(reader.GetString(2)),
        PartName=reader.GetString(3), PartNumber=reader.GetString(4), VehicleType=reader.GetString(5),
        Eco=reader.GetString(6), Nco=reader.GetString(7), Alc=reader.GetString(8), LotNo=reader.GetInt64(9),
        ProductionCounter=reader.GetInt64(10), Result=reader.GetString(11), Passed=reader.GetInt64(12)!=0,
        ModelName=reader.GetString(13), ModelFile=reader.GetString(14), HtdrvName=reader.GetString(15),
        OpenCount=reader.GetInt32(16), WrongCount=reader.GetInt32(17), ShortCount=reader.GetInt32(18),
        Resistance=reader.GetString(19), DeviceName=reader.GetString(20), DeviceNumber=reader.GetString(21),
        OperatorCompany=reader.GetString(22), ProductionLine=reader.GetString(23), FaultType=reader.GetString(24),
        FaultCode=reader.GetString(25), ExpectedSourceIo=GetNullableInt(reader,26), ExpectedTargetIo=GetNullableInt(reader,27),
        ActualSourceIo=GetNullableInt(reader,28), ActualTargetIo=GetNullableInt(reader,29), FaultDetailsJson=reader.GetString(30),
        FaultSummary=reader.GetString(31), MeasuredResistance=GetNullableDouble(reader,32), ResistanceMin=GetNullableDouble(reader,33),
        ResistanceMax=GetNullableDouble(reader,34), CycleId=reader.GetString(35), LabelSerial=reader.GetString(36),
        BarcodeValue=reader.GetString(37), LabelProfile=reader.GetString(38), PrintStatus=reader.GetString(39),
        PrintTimestamp=GetNullableDate(reader,40), Printer=reader.GetString(41), LabelCopies=reader.GetInt32(42),
        ReprintCount=reader.GetInt32(43), PrintMessage=reader.GetString(44), LabelTemplateType=reader.GetString(45),
        LabelPayload=reader.GetString(46), InstallStartedAt=GetNullableDate(reader,47), TestStartedAt=GetNullableDate(reader,48),
        ResultAt=GetNullableDate(reader,49), RemovalStartedAt=GetNullableDate(reader,50), RemovedAt=GetNullableDate(reader,51),
        InspectionType=reader.GetString(52), LotText=reader.GetString(53), InspectionTrace=reader.GetString(54)
    };

    private static int? GetNullableInt(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static double? GetNullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    private static DateTime? GetNullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseDate(reader.GetString(ordinal));
    private static DateTime ParseDate(string value) =>
        DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out DateTime parsed) ? parsed : DateTime.MinValue;
    private static void AddNullable(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
