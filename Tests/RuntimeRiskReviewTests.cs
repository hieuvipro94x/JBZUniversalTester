using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static TestViewModel CreateRiskReviewVm()
    {
        var board = new FakeBoard();
        var visa = new KeysightVisaService();
        var settings = new AppSettings();
        var production = new ProductionSettings();
        return new(new MainViewModel(), new TestEngine(board, visa, settings, production),
            board, visa, new WaterProofSerialService(), settings, production);
    }

    private static object? RiskInvoke(TestViewModel vm, string method, params object?[] args) =>
        typeof(TestViewModel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, args);

    private static void TestCycleTokenCancellationRace()
    {
        var vm = CreateRiskReviewVm();
        try
        {
            var old = (CancellationToken)RiskInvoke(vm, "BeginCycleOperations")!;
            using var registration = old.Register(() => RiskInvoke(vm, "CancelCycleOperations"));
            var next = (CancellationToken)RiskInvoke(vm, "BeginCycleOperations")!;
            Assert(next.IsCancellationRequested,
                "Old cancellation callback cancels and disposes the new CTS before Begin returns; captured token must remain usable");
        }
        finally { vm.ShutdownAsync().GetAwaiter().GetResult(); }
    }

    private static void TestHistoryRepositoryIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-risk-repository-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var vm = CreateRiskReviewVm();
        var store = new TestHistoryStore(Path.Combine(root, "history.db"));
        try
        {
            typeof(TestViewModel).GetField("_historyStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, store);
            var persistence = (ProductionPersistenceService)typeof(TestViewModel)
                .GetProperty("ProductionPersistence", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            persistence.Initialization.GetAwaiter().GetResult();
            RiskInvoke(vm, "RefreshProductionUiSettings");
            var repository = typeof(TestViewModel).GetProperty("HistoryStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm);
            var writerRepository = typeof(ProductionPersistenceService).GetField("_repository", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(persistence);
            Assert(ReferenceEquals(store, repository) && ReferenceEquals(repository, writerRepository),
                "Refreshing settings must preserve the writer's single repository instance and temp database path");
        }
        finally
        {
            vm.ShutdownAsync().GetAwaiter().GetResult();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
