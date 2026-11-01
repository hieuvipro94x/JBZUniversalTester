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

    private static void TestSoftwareObserverDoesNotFaultBoard()
    {
        var vm = CreateRiskReviewVm();
        try
        {
            Type runtimeMode = typeof(TestViewModel).GetNestedType("RuntimeMode", BindingFlags.NonPublic)!;
            RiskInvoke(vm, "SwitchRuntimeMode", Enum.Parse(runtimeMode, "Probe"));
            typeof(TestViewModel).GetField("_probeSessionActive", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, 1);
            int notifications = 0;
            vm.ScanFrameReceived += (_, _) =>
            {
                if (++notifications == 1) throw new InvalidOperationException("Injected presentation subscriber failure");
            };
            var frame = new ScanFrame(DateTime.Now, 10, new HashSet<int>(), [], false, 0, 0, Mode: BoardScanMode.Probe);
            RiskInvoke(vm, "ProcessBoardFrameReceived", frame);
            Assert(!vm.IsDeviceFault && vm.DeviceFaultTransitionCount == 0,
                "A software frame subscriber failure is logged without a board fault or restart request");
            RiskInvoke(vm, "ProcessBoardFrameReceived", frame);
            Assert(notifications == 2 && !vm.IsDeviceFault, "The next Probe event still reaches subscribers after an observer failure");
        }
        finally { vm.ShutdownAsync().GetAwaiter().GetResult(); }
    }

    private static void TestBackgroundStateDispatch()
    {
        OnSettingsDispatcher(async () =>
        {
            var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            var vm = CreateRiskReviewVm();
            int uiThread = Environment.CurrentManagedThreadId;
            var stateThreads = new List<int>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.State)) stateThreads.Add(Environment.CurrentManagedThreadId);
            };
            try
            {
                Task.Run(() => vm.State = "WORKER STATE").GetAwaiter().GetResult();
                Assert(vm.State == "WORKER STATE" && stateThreads.Count == 0,
                    "Logical State is synchronous on the worker while WPF notification waits for Dispatcher");
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                Assert(stateThreads.SequenceEqual([uiThread]), "Worker State setter raises its binding only on UI");
                bool throwOnce = true;
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(vm.State) && throwOnce)
                    { throwOnce = false; throw new InvalidOperationException("Injected WPF observer error"); }
                };
                vm.State = "OBSERVER ERROR";
                vm.State = "OBSERVER RECOVERED";
                Assert(!vm.IsDeviceFault && vm.State == "OBSERVER RECOVERED", "A WPF observer error does not escape or fault the board");
                var frame = new ScanFrame(DateTime.Now, 10, new HashSet<int> { 1, 2 }, [], true, 0, 1,
                    new Dictionary<int, IReadOnlySet<int>> { [1] = new HashSet<int> { 2 } });
                // Worker can finish without waiting for the UI to process its notification.
                Task.Run(() => RiskInvoke(vm, "HandleBackgroundProductRemovalInterlock", frame, 0L)).GetAwaiter().GetResult();
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                Assert(vm.State == "THÁO SẢN PHẨM" && stateThreads.Count > 0 && stateThreads.All(id => id == uiThread),
                    "Background contact detection publishes State on the WPF Dispatcher");
                Task.Run(() => RiskInvoke(vm, "HandleBackgroundProductRemovalInterlock", frame with
                { ActiveIo = new HashSet<int>(), ConnectionsBySource = new Dictionary<int, IReadOnlySet<int>>(), Sequence = 2 }, 0L)).GetAwaiter().GetResult();
                Type runtimeMode = typeof(TestViewModel).GetNestedType("RuntimeMode", BindingFlags.NonPublic)!;
                RiskInvoke(vm, "SwitchRuntimeMode", Enum.Parse(runtimeMode, "Production"));
                vm.State = "NEW CONTEXT";
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                Assert(vm.State == "NEW CONTEXT", "Old Background UI callback cannot overwrite a new runtime generation");
            }
            finally { await vm.ShutdownAsync(); app.Shutdown(); }
        });
    }

    private static void TestDetachedTaskObservation()
    {
        var vm = CreateRiskReviewVm();
        try
        {
            ((Task)RiskInvoke(vm, "ObserveDetachedTaskAsync", Task.FromException(new IOException("Injected task failure")), "SELFTEST")!)
                .GetAwaiter().GetResult();
            var board = (FakeBoard)typeof(TestViewModel).GetField("_board", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
            Assert(!vm.IsDeviceFault && vm.State.Contains("LỖI XỬ LÝ PHẦN MỀM") && board.Commands.Count == 0,
                "Detached task errors are observed and reported without issuing any board/relay operation");
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var observation = (Task)RiskInvoke(vm, "ObserveDetachedTaskAsync", pending.Task, "OLD_CONTEXT")!;
            Type runtimeMode = typeof(TestViewModel).GetNestedType("RuntimeMode", BindingFlags.NonPublic)!;
            RiskInvoke(vm, "SwitchRuntimeMode", Enum.Parse(runtimeMode, "Production"));
            vm.State = "NEW CONTEXT";
            pending.SetException(new IOException("Old task failed"));
            observation.GetAwaiter().GetResult();
            Assert(vm.State == "NEW CONTEXT", "A failed detached task from an old runtime cannot overwrite new State");
        }
        finally { vm.ShutdownAsync().GetAwaiter().GetResult(); }
    }
}
