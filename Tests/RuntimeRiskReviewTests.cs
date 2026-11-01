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
}
