using System.Reflection;
using System.Security.Cryptography;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Services;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestStartupModelPreparation()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-StartupPrepare-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "WARM-A.tht");
            IntegratedThtStore.Save(new ModelDocument
            {
                Base = new() { Model = "WARM-A", Number = "WARM-PART", Name = "STARTUP FIXTURE" },
                Connectors = [new() { Name = "1", PinCount = 2, StartIo = 1 }],
                Pins = [new() { Io = 1, Connector = "1", PinNo = 1, Line = "W1" },
                        new() { Io = 2, Connector = "1", PinNo = 2, Line = "W1" }]
            }, path, null);
            byte[] original = File.ReadAllBytes(path);
            foreach (bool failData in new[] { false, true })
            {
                var dataReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var main = new MainViewModel(new AppSettings(), new ProductionSettings { LastThtPath = path },
                    productionDataPreparation: dataReady.Task);
                try
                {
                    Task waiting = (Task)typeof(MainViewModel).GetMethod("PrepareModelAfterDataAsync",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [dataReady.Task])!;
                    FieldInfo prepared = typeof(TestViewModel).GetField("_startupModelPreparationTask",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    Assert(!waiting.IsCompleted && prepared.GetValue(main.Test) is null && !main.Test.IsBoardConnected,
                        "Model preparation waits for database readiness without opening hardware");
                    if (failData) dataReady.SetException(new InvalidDataException("Simulated database failure"));
                    else dataReady.SetResult();
                    bool rejected = false;
                    try { waiting.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
                    catch (InvalidDataException) { rejected = true; }
                    Assert(failData ? rejected && prepared.GetValue(main.Test) is null
                                    : !rejected && prepared.GetValue(main.Test) is Task { IsCompletedSuccessfully: true },
                        "A failed database cannot prepare or install startup topology");
                    Assert(main.Model is null && main.Test.ModelName == "" && !main.Test.IsBoardConnected,
                        "Data readiness alone cannot install a model or connect the board");
                }
                finally { main.ShutdownAsync().GetAwaiter().GetResult(); }
            }
            foreach (bool stale in new[] { false, true })
            {
                var production = new ProductionSettings
                {
                    LastThtPath = path, ShowDiscardConfigurationNotice = false,
                    HistoryDirectory = Path.Combine(root, stale ? "stale" : "current")
                };
                var vm = CreateTestViewModel(production, out FakeBoard board);
                var connection = new TaskCompletionSource<BoardConnectionInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                board.SetConnectionStateForTest(false);
                board.PendingConnection = connection;
                try
                {
                    int commands = board.Commands.Count;
                    Task preparation = vm.PrepareStartupModelAsync();
                    Assert(ReferenceEquals(preparation, vm.PrepareStartupModelAsync()),
                        "Startup shares one immutable model preparation task");
                    preparation.GetAwaiter().GetResult();
                    Assert(vm.ModelName == "" && board.ConnectAttempts == 0 && board.Commands.Count == commands &&
                           vm.Total == 0 && vm.Pass == 0 && vm.Fail == 0,
                        "Startup parsing cannot install a model, connect hardware, fire outputs or change counts");
                    Task initialization = vm.InitializeAsync();
                    Assert(!initialization.IsCompleted && vm.ModelName == "" && board.ConnectAttempts == 1,
                        "Prepared topology remains uncommitted while board connection is pending");
                    if (stale)
                    {
                        FieldInfo generation = vm.GetType().GetField("_modelLoadGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!;
                        generation.SetValue(vm, (int)generation.GetValue(vm)! + 1);
                    }
                    board.SetConnectionStateForTest(true);
                    connection.SetResult(new BoardConnectionInfo("Fake", "Fake"));
                    initialization.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    Assert(stale ? vm.ModelName == "" : vm.CurrentModelPath == path && vm.ModelName == "WARM-A",
                        "Startup commits only the current prepared model after hardware is ready");
                    Assert(File.ReadAllBytes(path).SequenceEqual(original), "Startup preparation leaves its THT unchanged");
                }
                finally
                {
                    connection.TrySetResult(new BoardConnectionInfo("Fake", "Fake"));
                    vm.ShutdownAsync().GetAwaiter().GetResult();
                }
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private static void TestStartupUpgradeFastPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-StartupFastPath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        try
        {
            string database = Path.Combine(root, "Data", "JBZUniversalTester.db");
            var store = new TestHistoryStore(database);
            store.Add(new TestHistoryRecord { Started = DateTime.Now, Finished = DateTime.Now,
                PartNumber = "KEEP-STARTUP-HISTORY", Result = "PASS", Passed = true });
            File.WriteAllText(Path.Combine(root, "JBZUniversalTester.cfg"), "[LotNo]7000");
            File.WriteAllText(Path.Combine(root, "PartCnt.txt"), "KEEP 200000 10");
            File.WriteAllBytes(Path.Combine(root, "Data", "BCW-labels.db"), [1]);
            SqliteConnection.ClearAllPools();
            var hashes = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
            IEnumerable<string> UnavailableSources()
            {
                throw new InvalidOperationException("Existing target files must bypass source enumeration");
#pragma warning disable CS0162
                yield break;
#pragma warning restore CS0162
            }
            Assert(ProductionDataUpgradeService.MigrateMissingProductionData(root, UnavailableSources()).Count == 0 &&
                   hashes.All(pair => SHA256.HashData(File.ReadAllBytes(pair.Key)).SequenceEqual(pair.Value)),
                "Warm startup bypasses old-version probing without changing configuration or production files");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            Assert((string?)command.ExecuteScalar() == "ok", "Startup fast path preserves SQLite integrity");
            long[] expected = [1, 0, 0, 0]; int index = 0;
            foreach (string table in new[] { "Tests", "TestFaults", "ResistanceMeasurements", "WaterProofMeasurements" })
            {
                command.CommandText = $"SELECT COUNT(*) FROM {table};";
                Assert(Convert.ToInt64(command.ExecuteScalar()) == expected[index++], "Startup fast path preserves " + table);
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
