using System.IO.Compression;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using JBZUniversalTester.Models;
using JBZUniversalTester.Converters;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static class Program
{
    private static int Main()
    {
        (string Name, Action Run)[] tests =
        [
            ("Board capacity/address boundaries", TestBoardCapacity),
            ("Production scan accepts first frame after decoder sequence reset", TestProductionScanFirstFrameAfterSequenceReset),
            ("New version inherits station production data without overwrite", TestProductionDataUpgrade),
            ("Production/probe decoder separation", TestDecoderModes),
            ("10-card complete-frame stress", TestTenCardCompleteFrameStress),
            ("Startup connected-IO safety interlock", TestStartupIoInterlock),
            ("THT discard contact interlock and frame isolation", TestDiscardContactInterlock),
            ("Probe target-only touch detection", TestProbeTargetOnlyTouchDetection),
            ("Inline probe does not clear wiring faults", TestInlineProbeDoesNotClearWiringFaults),
            ("Htdrv endpoint/probe display cases", TestHtdrvEndpointProbeDisplayCases),
            ("500-cycle scan/probe/fault stress", TestFiveHundredCycleScanProbeFaultStress),
            ("Continuity/open/wrong/splice engine", TestEngineVectors),
            ("Pending continuity presentation and CLIP branch visibility", TestPendingContinuityPresentation),
            ("Production PASS gate minimal latency", TestProductionPassGateMinimalLatency),
            ("THT column semantics and string wire topology", TestThtColumnSemantics),
            ("Blank THT IO mapping compatibility", TestBlankThtIoMappingCompatibility),
            ("Learned diagnostic topology normalization and persistence", TestLearnedTopology),
            ("Relay PASS/FAIL safe ordering", TestRelayOrdering),
            ("History SQLite/search/CSV/XLSX native types", TestHistory),
            ("Legacy SQLite without SchemaInfo initializes safely", TestLegacyDatabaseWithoutSchemaInfo),
            ("History initialization waits for an active SQLite writer", TestHistoryInitializationWaitsForWriter),
            ("Production SQLite writer retries a transient lock", TestProductionPersistenceRetriesTransientLock),
            ("SQLite interrupted transaction reopens without deleting database", TestHistoryInterruptedTransactionRecovery),
            ("Canonical runtime paths and SQLite PartCnt authority", TestCanonicalRuntimePersistence),
            ("System log master switch preserves History", TestSystemLogMasterSwitch),
            ("ALL6 label data order", TestLabel),
            ("THT label renderer and LOT lifecycle", TestThtLabelAndLotLifecycle),
            ("PASS label snapshot/idempotency/traceability", TestLabelPrintingSafety),
            ("Standard product picker filter", TestProductPickerFilter),
            ("Fault display localization and detail", TestFaultDisplayFormatter),
            ("UI brush cache and engine change filter", TestUiPerformanceGuards),
            ("Duplicate CLIP fault rows do not lock hardware", TestDuplicateClipFaultRows),
            ("D2XX resistance selectors and ten-slot configuration", TestD2xxResistanceRouting),
            ("Leak connector mapping and PASS/FAIL presentation", TestWaterProofConfigurationAndPresentation),
            ("Final TestView status/master/device fault guards", TestFinalTestStatusGuards),
            ("Direct manual relay controls and production interlock", TestManualModeInterlock),
            ("START only arms and background scan survives cycle cancel", TestProductionScanTokenSurvivesCycleCancel),
            ("Production fault debounce and jig contact state", TestProductionFaultConfirmation),
            ("Incomplete product full release resets normal and CLIP cycle", TestIncompleteProductFullReleaseResetsClipCycle),
            ("Original PartCnt per-part counter compatibility", TestPartCounterStore),
            ("Original PHT20 PASS/ERR history compatibility", TestLegacyPhtHistory),
            ("Per-model production/probe maintenance counters", TestProductionCounters)
        ];

        int failed = 0;
        foreach ((string name, Action run) in tests)
        {
            try
            {
                run();
                Console.WriteLine($"PASS: {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL: {name}\n  {ex}");
            }
        }

        Console.WriteLine($"SELF-TEST SUMMARY: {tests.Length - failed}/{tests.Length} PASS");
        return failed == 0 ? 0 : 1;
    }

    private static void TestProductPickerFilter()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        MethodInfo filter = typeof(HomeViewModel).GetMethod(
            "IsSupportedProductFile",
            flags)
            ?? throw new InvalidOperationException("Product picker filter method not found.");
        FieldInfo filterText = typeof(HomeViewModel).GetField("ProductFileFilter", flags)
            ?? throw new InvalidOperationException("Product picker filter text not found.");

        bool Accepts(string fileName) =>
            (bool)(filter.Invoke(null, [fileName]) ?? false);

        Assert(Accepts("sample.tht"), ".tht must be visible");
        Assert(Accepts("sample.THT"), ".THT must be visible");
        Assert(Accepts("sample.model"), ".model must be visible");
        Assert(Accepts("sample.MODEL"), ".MODEL must be visible");
        Assert(!Accepts("sample.json"), ".json must be hidden");
        Assert(!Accepts("sample.jbzproduct.json"), ".jbzproduct.json must be hidden");
        Assert(!Accepts("sample.setup"), ".setup must be hidden");
        Assert(
            string.Equals(
                filterText.GetRawConstantValue()?.ToString(),
                "The Files (*.tht)|*.tht|All Files (*.*)|*.*",
                StringComparison.Ordinal),
            "Native dialog filter must match the original Htdrv .tht/all-files order");

        string pickerSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "ViewModels", "HomeViewModel.cs"));
        string pickerGuardSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "FixedPositionOpenFileDialogGuard.cs"));
        Assert(pickerSource.Contains("DefaultExt = \".tht\"", StringComparison.Ordinal) &&
               pickerSource.Contains("OriginalItemDirectory = @\"C:\\Item\"", StringComparison.Ordinal) &&
               pickerSource.Contains("FirstOrDefault(window => window.IsActive)", StringComparison.Ordinal) &&
               pickerSource.Contains("AutoUpgradeEnabled = false", StringComparison.Ordinal) &&
               pickerSource.Contains("dialog.ShowDialog(new NativeDialogOwner(owner))", StringComparison.Ordinal) &&
               pickerSource.Contains("FixedPositionOpenFileDialogGuard(owner)", StringComparison.Ordinal),
            "Product picker uses the original filter/directory and owner-bound classic native dialog");
        Assert(pickerGuardSource.Contains("GetWindow(handle, GwOwner) == _ownerHandle", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("MonitorFromWindow(_ownerHandle, MonitorDefaultToNearest)", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("GetMonitorInfo(monitor, ref monitorInfo)", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("OriginalDialogWidthDip = 555", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("OriginalDialogHeightDip = 408", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("VisualTreeHelper.GetDpi(owner)", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("DwmwaExtendedFrameBounds = 9", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("DwmGetWindowAttribute(", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("visibleWidth + hiddenFrameWidth", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("style & ~WsThickFrame & ~WsMaximizeBox", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("SwpNoZOrder | SwpNoActivate", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("DispatcherPriority.ApplicationIdle", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("if (IsWindow(wParam))", StringComparison.Ordinal) &&
               !pickerGuardSource.Contains("WmNcLButtonDown", StringComparison.Ordinal) &&
               !pickerGuardSource.Contains("ScMove", StringComparison.Ordinal) &&
               pickerGuardSource.Contains("ReleaseCreationHook();", StringComparison.Ordinal),
            "OpenFileDialog compensates the DWM frame for the original visible size, centers after Shell layout, and remains movable");
    }

    private static void TestDiscardContactInterlock()
    {
        const string thtText =
            "파트번호\t파트명\n" +
            "DISCARD-TEST\tDISCARD SENSOR\n\n" +
            "번 호\t커넥터\t핀 수\n" +
            "1\t1\t2\n\n" +
            "커넥터\t선이름\tI/O\t핀번호\n" +
            "1\tMC01\t1\t1\n" +
            "1\tMC01\t2\t2\n" +
            "_DISCARD\t\t97\t1\n" +
            "_DISCARD\t\t98\t2\n\n" +
            "선이름\t선연결\t굵기\t색깔\n" +
            "MC01\t\t0.5\tB";
        string root = Path.Combine(
            Path.GetTempPath(),
            "JBZDiscardThtTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "discard.tht");
            File.WriteAllBytes(path, BuildMinimalThtFile(thtText));
            ProductModel parsed = new ThtModelParser().Load(path);
            Assert(parsed.DiscardContactIo.SequenceEqual([97, 98]) &&
                   parsed.HasDiscardInterlock &&
                   parsed.Pins.All(pin => pin.IoNumber is not 97 and not 98) &&
                   parsed.MaxIo == 98,
                "THT parser must preserve two _DISCARD IO with PinNo 1/2 while excluding them from product topology");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        var model = new ProductModel
        {
            Pins = [new PinRecord("1", "MC01", 1, "1")],
            DiscardContactIo = [97, 98]
        };
        Assert(model.HasDiscardInterlock && model.MaxIo == 98,
            "_DISCARD pair must extend active scan range without becoming a product pin");

        ScanFrame raw = FrameSeq(
            1,
            (1, new[] { 18 }),
            (97, new[] { 98 }));
        Assert(DiscardContactInterlock.IsContactClosed(raw, model.DiscardContactIo),
            "Normally-open _DISCARD contact closes when the configured pair is connected");

        ScanFrame filtered = DiscardContactInterlock.RemoveDiscardIo(
            raw,
            model.DiscardContactIo);
        Assert(!filtered.ActiveIo.Contains(97) &&
               !filtered.ActiveIo.Contains(98) &&
               filtered.Connections.ContainsKey(1) &&
               !filtered.Connections.ContainsKey(97),
            "Discard IO must remain visible to its monitor but never enter continuity/fault evaluation");

        var tracker = new DiscardContactInterlock();
        tracker.Arm(contactClosed: false);
        Assert(tracker.Observe(contactClosed: true) == DiscardContactTransition.FirstPassDetected &&
               tracker.IsArmed && !tracker.IsCompleted,
            "The first fresh NG-bin sensor activation locks Production");
        Assert(tracker.Observe(contactClosed: true) == DiscardContactTransition.None,
            "A held contact cannot count as the second activation");
        Assert(tracker.Observe(contactClosed: false) == DiscardContactTransition.None &&
               tracker.Observe(contactClosed: true) == DiscardContactTransition.Completed &&
               tracker.IsCompleted,
            "The sensor must release before the second activation completes the interlock");

        tracker.Arm(contactClosed: true);
        Assert(tracker.Observe(contactClosed: true) == DiscardContactTransition.None &&
               tracker.Observe(contactClosed: false) == DiscardContactTransition.None &&
               tracker.Observe(contactClosed: true) == DiscardContactTransition.FirstPassDetected &&
               tracker.Observe(contactClosed: false) == DiscardContactTransition.None &&
               tracker.Observe(contactClosed: true) == DiscardContactTransition.Completed,
            "A sensor active at ARM must return open before two new activations can complete it");

        ScanFrame inputStyleSensorFrame = FrameSeq(2, (12, new[] { 97 }));
        Assert(DiscardContactInterlock.GetActiveContactIo(inputStyleSensorFrame, model.DiscardContactIo)
                .SequenceEqual([97]),
            "_DISCARD detection accepts a configured IO used as a target instead of requiring IO97<->IO98");

        var production = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            UseTestPointer = true
        };
        TestViewModel discardVm = CreateTestViewModel(production, out FakeBoard discardBoard);
        var discardModel = Model(("PAIR", new[] { 1, 18 }));
        discardModel.ModelName = "SELF-TEST-DISCARD-INTERLOCK";
        discardModel.PartNumber = "SELF-TEST-DISCARD-INTERLOCK";
        discardModel.DiscardContactIo = [97, 98];
        discardVm.LoadPreparedModelAsync(discardModel).GetAwaiter().GetResult();
        discardVm.StartProductionTestAsync().GetAwaiter().GetResult();
        int totalBeforeDiscard = discardVm.Total;
        int failBeforeDiscard = discardVm.Fail;

        discardBoard.Publish(FrameSeq(10, (12, new[] { 97 })));
        Assert(discardVm.IsProductRemovalPending &&
               discardVm.ProbeContacts.Select(row => row.WireName)
                   .SequenceEqual(["_DISCARD IO(97)", "_DISCARD IO(98)"]),
            "First _DISCARD activation locks TEST and temporarily shows both configured _DISCARD rows");
        discardBoard.Publish(FrameSeq(11));
        Assert(discardVm.IsProductRemovalPending,
            "Releasing the sensor after pass one keeps TEST locked");
        discardBoard.Publish(FrameSeq(12, (13, new[] { 98 })));
        Assert(!discardVm.IsProductRemovalPending &&
               discardVm.Total == totalBeforeDiscard &&
               discardVm.Fail == failBeforeDiscard,
            $"Second _DISCARD activation unlocks TEST without recording a product result " +
            $"(pending={discardVm.IsProductRemovalPending}, total={discardVm.Total}, fail={discardVm.Fail}, state={discardVm.State})");

        string testViewModelSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "ViewModels", "TestViewModel.cs"));
        Assert(
            System.Text.RegularExpressions.Regex.Matches(
                testViewModelSource,
                "new JBZUniversalTester\\.Views\\.FaultConfirmationWindow\\(").Count == 1 &&
            System.Text.RegularExpressions.Regex.Matches(
                testViewModelSource,
                "ShowFaultConfirmationDialog\\(").Count == 5,
            "Every product FAIL path must use the centralized _DISCARD confirmation dialog");

        int confirmationStart = testViewModelSource.IndexOf(
            "private void ShowFaultConfirmationDialog(",
            StringComparison.Ordinal);
        int waitingTextStart = testViewModelSource.IndexOf(
            "private static string FaultRemovalWaitingText(",
            confirmationStart,
            StringComparison.Ordinal);
        string confirmationMethod = testViewModelSource[confirmationStart..waitingTextStart];
        Assert(!confirmationMethod.Contains("DiscardPassword", StringComparison.Ordinal),
            "_DISCARD product FAIL confirmation must not request a password");

        string settingsXaml = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "ProductionSettingsPage.xaml"));
        Assert(!settingsXaml.Contains("Settings.DiscardPassword", StringComparison.Ordinal) &&
               !settingsXaml.Contains("Mật khẩu thùng lỗi", StringComparison.Ordinal),
            "Production settings no longer exposes an unused NG-bin password");

        int finalRejectStart = testViewModelSource.IndexOf(
            "private async Task HandleFinalPassRejectedAsync(",
            StringComparison.Ordinal);
        int nextMethodStart = testViewModelSource.IndexOf(
            "private async Task RecoverAfterUncommittedFailAsync(",
            finalRejectStart,
            StringComparison.Ordinal);
        string finalRejectMethod = testViewModelSource[finalRejectStart..nextMethodStart];
        Assert(finalRejectMethod.Contains(
                   "ShowFaultConfirmationDialog(faults, cycleModel);",
                   StringComparison.Ordinal) &&
               finalRejectMethod.Contains(
                   "ArmFaultProductRemoval(cycleModel);",
                   StringComparison.Ordinal) &&
               !finalRejectMethod.Contains(
                   "Interlocked.Exchange(ref _discardRequiredForFault, 0)",
                   StringComparison.Ordinal),
            "Final PASS rejection must require _DISCARD password and sensor completion like every other FAIL");
    }

    private static void TestProductionScanFirstFrameAfterSequenceReset()
    {
        var board = new FakeBoard();
        board.Publish(CreateProductionFrame(sequence: 1));
        board.SetAppliedScanCapacityForTest(1);
        board.StartScanCallback = current =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                current.Publish(CreateProductionFrame(sequence: 1));
            });
        };

        var supervisor = new ScanSupervisor(board, _ => { });
        supervisor.StartProductionScanAndVerifyFrameAsync(
                BoardCapacity.MaxGlobalIo,
                CancellationToken.None,
                "SELF_TEST_10_MODULES")
            .GetAwaiter()
            .GetResult();

        Assert(
            board.Commands.Count(command => command == "START") == 1,
            "Changing active capacity restarts firmware scan exactly once");

        var sameCapacityBoard = new FakeBoard();
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            sameCapacityBoard.Publish(CreateProductionFrame(sequence: 2));
        });
        new ScanSupervisor(sameCapacityBoard, _ => { })
            .StartProductionScanAndVerifyFrameAsync(
                BoardCapacity.MaxGlobalIo,
                CancellationToken.None,
                "SELF_TEST_SAME_CAPACITY")
            .GetAwaiter()
            .GetResult();
        Assert(
            sameCapacityBoard.Commands.All(command => command is not "START" and not "STOP"),
            "Same active capacity reuses the healthy scan without STOP/START");

        // Regression cho call path thật của TestViewModel: stream có thể đang chạy
        // active=1 từ lúc Connect, sau đó model được nạp và requested đổi thành 8.
        // EnsureContinuousProductionScanAsync không được return sớm chỉ vì IsScanning.
        TestViewModel vm = CreateTestViewModel(new ProductionSettings(), out FakeBoard vmBoard);
        vmBoard.SetRequestedScanCapacityForTest(8);
        vmBoard.SetAppliedScanCapacityForTest(1);
        MethodInfo ensureContinuous = typeof(TestViewModel).GetMethod(
            "EnsureContinuousProductionScanAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("EnsureContinuousProductionScanAsync method not found.");
        ((Task)ensureContinuous.Invoke(vm, [])!).GetAwaiter().GetResult();
        Assert(
            vmBoard.Commands.Count(command => command == "START") == 1 &&
            vmBoard.AppliedScanCapacity?.StartScanParameter == 8,
            "Running active=1 stream is reconciled to requested active=8 instead of returning early");

        Assert(
            ScanSupervisor.ResolveFirstFrameTimeoutMs(BoardCapacity.Create(10)) == 15_000,
            "Ten-module first-frame watchdog must allow the measured long hardware frame");
        Assert(
            ScanSupervisor.ResolveProductionStallTimeoutMs(BoardCapacity.Create(10)) == 17_500,
            "Ten-module background stall watchdog must not interrupt a valid frame");

        static ScanFrame CreateProductionFrame(long sequence) => new(
            DateTime.Now,
            BoardCapacity.MaxExpansionCardCount,
            new HashSet<int>(),
            [],
            true,
            0,
            sequence,
            new Dictionary<int, IReadOnlySet<int>>(),
            new Dictionary<int, int>(),
            BoardScanMode.Production);
    }

    private static void TestProductionDataUpgrade()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZUpgradeTest_" + Guid.NewGuid().ToString("N"));
        string older = Path.Combine(root, "V16.0.41");
        string previous = Path.Combine(root, "V16.0.44");
        string target = Path.Combine(root, "V16.0.45");
        try
        {
            Directory.CreateDirectory(Path.Combine(older, "Data"));
            Directory.CreateDirectory(Path.Combine(previous, "Data"));
            Directory.CreateDirectory(target);

            File.WriteAllText(Path.Combine(older, "production.statistics.json"), "older-statistics-with-real-production-data");
            File.WriteAllText(Path.Combine(older, "production.settings.json"), "{\"LotNo\":3456}");
            File.WriteAllText(Path.Combine(previous, "production.statistics.json"), "latest-statistics");
            File.WriteAllText(Path.Combine(previous, "production.settings.json"), "{\"LotNo\":2000}");
            File.WriteAllText(Path.Combine(previous, "JBZUniversalTester.cfg"), "[LotNo]3456\r\n");
            File.WriteAllText(Path.Combine(previous, "JBZUniversalTester.log"), "canonical-log");
            File.WriteAllText(Path.Combine(previous, "PartCnt.txt"), "PART-A 200000 4321");
            File.WriteAllBytes(
                Path.Combine(previous, "Data", "JBZUniversalTester.db"),
                [1, 2, 3, 4]);

            // A value already created on the destination machine is authoritative.
            File.WriteAllText(Path.Combine(target, "PartCnt.txt"), "PART-A 200000 5000");

            IReadOnlyList<string> migrated = ProductionDataUpgradeService.MigrateMissingProductionData(
                target,
                [previous, older]);

            Assert(
                !File.Exists(Path.Combine(target, "production.statistics.json")) &&
                !File.Exists(Path.Combine(target, "production.settings.json")),
                "Legacy JSON files must not be copied into the canonical runtime directory");
            Assert(
                File.ReadAllText(Path.Combine(target, "JBZUniversalTester.cfg")) == "[LotNo]3456\r\n",
                "Canonical CFG must be inherited");
            Assert(
                File.ReadAllText(Path.Combine(target, "PartCnt.txt")) == "PART-A 200000 5000",
                "Existing destination PartCnt must never be overwritten");
            Assert(
                File.ReadAllBytes(Path.Combine(target, "Data", "JBZUniversalTester.db"))
                    .SequenceEqual(new byte[] { 1, 2, 3, 4 }),
                "Canonical SQLite database must be inherited");
            Assert(
                !File.Exists(Path.Combine(target, "JBZUniversalTester.log")),
                "Runtime logs must never be inherited from an older version");
            Assert(migrated.Count == 2, "Only missing production state files must be reported as migrated");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void TestFaultDisplayFormatter()
    {
        var open = new FaultDetail
        {
            Type = ProductFaultType.OpenCircuit,
            ConnectorFrom = "CN1",
            PinFrom = "4",
            ConnectorTo = "CN3",
            PinTo = "6",
            WireColor = "WH"
        };
        OperatorFaultDisplay openOperator = FaultDisplayFormatter.FormatOperator(open);
        CustomerFaultDisplay openCustomer = FaultDisplayFormatter.FormatCustomer(open);
        Assert(openOperator.Title == "KIỂM TRA LỖI HỞ MẠCH", "Open operator instruction");
        Assert(openOperator.Lines.Any(line => line.Value.Contains("CN1 - Chân 4 ↔ CN3 - Chân 6", StringComparison.Ordinal)), "Open standard connection");
        Assert(openOperator.Lines.Any(line => line.Label == "Màu dây tiêu chuẩn" && line.Value == "TRẮNG"), "Open standard color");
        Assert(openOperator.Lines.Any(line => line.Value == "KHÔNG CÓ KẾT NỐI"), "Open actual condition");
        Assert(openCustomer.FaultType == "OPEN CIRCUIT" && openCustomer.Actual == "NO CONTINUITY", "Open customer mapping");

        var wrong = new FaultDetail
        {
            Type = ProductFaultType.WrongWiring,
            WireName = "W12",
            WireColor = "RED",
            ConnectorFrom = "CN1",
            PinFrom = "3",
            ConnectorTo = "CN2",
            PinTo = "8",
            ActualConnectorFrom = "CN1",
            ActualPinFrom = "3",
            ActualConnectorTo = "CN1",
            ActualPinTo = "5"
        };
        OperatorFaultDisplay wrongOperator = FaultDisplayFormatter.FormatOperator(wrong);
        Assert(wrongOperator.Title == "KIỂM TRA LỖI SAI DÂY", "Wrong connection operator instruction");
        Assert(wrongOperator.Lines.Any(line => line.Label == "Vị trí tiêu chuẩn"), "Wrong standard position");
        Assert(wrongOperator.Lines.Any(line => line.Label == "Vị trí thực tế"), "Wrong actual position");
        Assert(FaultDisplayFormatter.FormatCustomer(wrong).FaultType == "INCORRECT CONNECTION", "Wrong customer mapping");

        var shortFault = new FaultDetail
        {
            Type = ProductFaultType.ShortCircuit,
            ActualConnectorFrom = "CN4",
            ActualPinFrom = "2",
            ActualConnectorTo = "CN6",
            ActualPinTo = "9"
        };
        OperatorFaultDisplay shortOperator = FaultDisplayFormatter.FormatOperator(shortFault);
        Assert(shortOperator.Title == "KIỂM TRA LỖI CHẬP MẠCH", "Short operator instruction");
        Assert(shortOperator.Lines.Any(line => line.Value.Contains("CN4 - Chân 2 ↔ CN6 - Chân 9", StringComparison.Ordinal)), "Short actual connection");
        Assert(FaultDisplayFormatter.FormatCustomer(shortFault).FaultType == "SHORT CIRCUIT", "Short customer mapping");

        var resistanceHigh = new FaultDetail
        {
            Type = ProductFaultType.ResistanceOutOfRange,
            WireName = "CN3 Pin 4 ↔ CN5 Pin 2",
            ResistanceMin = 950,
            ResistanceMax = 1050,
            MeasuredResistance = 1370
        };
        OperatorFaultDisplay resistanceHighOperator = FaultDisplayFormatter.FormatOperator(resistanceHigh);
        CustomerFaultDisplay resistanceHighCustomer = FaultDisplayFormatter.FormatCustomer(resistanceHigh);
        Assert(resistanceHighOperator.Lines.Any(line => line.Value == "CAO HƠN GIỚI HẠN"), "Resistance high assessment");
        Assert(resistanceHighOperator.Title == "KIỂM TRA LỖI ĐIỆN TRỞ", "Resistance operator instruction");
        Assert(resistanceHighCustomer.Assessment == "ABOVE UPPER LIMIT", "Resistance high customer assessment");
        Assert(resistanceHighCustomer.Deviation == "+0.32 kΩ", "Resistance high deviation");

        var resistanceLow = new FaultDetail
        {
            Type = ProductFaultType.ResistanceOutOfRange,
            ResistanceMin = 950,
            ResistanceMax = 1050,
            MeasuredResistance = 900
        };
        Assert(FaultDisplayFormatter.FormatCustomer(resistanceLow).Assessment == "BELOW LOWER LIMIT", "Resistance low assessment");
        Assert(FaultDisplayFormatter.FormatCustomer(resistanceLow).Deviation == "-0.05 kΩ", "Resistance low deviation");

        Assert(FaultDisplayFormatter.OperatorFaultType("WRONG_WIRE_COLOR") == "SAI MÀU DÂY", "Wire color operator mapping");
        Assert(FaultDisplayFormatter.CustomerFaultType("TERMINAL_MISPOSITION") == "TERMINAL MISPOSITION", "Terminal customer mapping");
        Assert(FaultDisplayFormatter.CustomerFaultType("CROSSED_TERMINALS") == "CROSSED TERMINALS", "Crossed terminal mapping");
        Assert(FaultDisplayFormatter.FormatOperator(new FaultDetail()).Lines.Count > 0, "Missing fault fields remain displayable");
    }

    private static void TestUiPerformanceGuards()
    {
        Assert(!AsyncFileLogService.Current.FileLoggingEnabled,
            "Production startup keeps Data/Logs diagnostic file writing disabled");

        var red1 = WireColorToBrushConverter.ToBrush("R");
        var red2 = WireColorToBrushConverter.ToBrush("R");
        var stripe1 = WireColorToBrushConverter.ToBrush("W/B");
        var stripe2 = WireColorToBrushConverter.ToBrush("W/B");
        Assert(ReferenceEquals(red1, red2) && red1.IsFrozen, "Single wire color brush is cached and frozen");
        Assert(ReferenceEquals(stripe1, stripe2) && stripe1.IsFrozen, "Composite wire color brush is cached and frozen");
        AssertBalancedTwoColorBrush("R/W", "#ED0000", "#FFFFFF");
        AssertBalancedTwoColorBrush("W/R", "#FFFFFF", "#ED0000");

        AssertWireColorCells("B", "#101010", "#F8F8F6", "#F8F8F6", "#F8F8F6");
        AssertWireColorCells("B/G", "#101010", "#00D000", "#F8F8F6", "#F8F8F6");
        AssertWireColorCells("B/L", "#101010", "#0077FF", "#F8F8F6", "#F8F8F6");
        AssertWireColorCells("Gr/Br", "#808080", "#8A4300", "#F8F8F6", "#F8F8F6");
        AssertWireColorCells("B/Y", "#101010", "#FFFF00", "#F8F8F6", "#F8F8F6");
        AssertWireColorCells("R/W/R", "#ED0000", "#FFFFFF", "#ED0000", "#F8F8F6");
        AssertWireColorCells("B/G/L/Br", "#101010", "#00D000", "#0077FF", "#8A4300");
        AssertWireColorCells("", "#F8F8F6", "#F8F8F6", "#F8F8F6", "#F8F8F6");
        AssertWireColorCells("UNKNOWN", "#F8F8F6", "#F8F8F6", "#F8F8F6", "#F8F8F6");

        var wrongRow = new FaultRow { Kind = FaultKind.WrongWiring, Color = "B/Br" };
        Assert(BrushHex(wrongRow.RowBackgroundBrush) == "#3446A8", "Wrong row uses Pi blue");
        Assert(BrushHex(wrongRow.RowForegroundBrush) == "#FFFFFF", "Wrong row text is white");
        Assert(BrushHex(wrongRow.Color1Brush) == "#101010" && BrushHex(wrongRow.Color2Brush) == "#8A4300",
            "Wrong row color cells override semantic row background");

        var probeRow = new FaultRow { Kind = FaultKind.Probe, Color = "B/L" };
        Assert(BrushHex(probeRow.RowBackgroundBrush) == "#BDEEEE", "Probe row uses Pi cyan row");
        Assert(BrushHex(probeRow.Color1Brush) == "#101010" && BrushHex(probeRow.Color2Brush) == "#0077FF",
            "Probe row color cells override probe background");

        var openGreenRow = new FaultRow { Kind = FaultKind.MissingConnection, Color = "G" };
        var openBlueRow = new FaultRow { Kind = FaultKind.Open, Color = "L" };
        var normalCheckRow = new FaultRow { Kind = FaultKind.Start, Status = "KIỂM TRA" };
        Assert(BrushHex(openGreenRow.RowForegroundBrush) == "#0026D9" &&
               BrushHex(openBlueRow.RowForegroundBrush) == "#0026D9",
            "Open/missing rows use the original Htdrv blue text");
        Assert(BrushHex(normalCheckRow.RowForegroundBrush) == "#555555" &&
               normalCheckRow.Status == "KIỂM TRA",
            "Normal KIỂM TRA row and status use the original dark operator text");
        Assert(BrushHex(openGreenRow.WireColorBrush) == "#00D000" &&
               BrushHex(openGreenRow.WireColorForegroundBrush) == "#333333" &&
               BrushHex(openBlueRow.WireColorBrush) == "#0077FF" &&
               BrushHex(openBlueRow.WireColorForegroundBrush) == "#FFFFFF",
            "Single Màu cell matches original green/blue background and readable text");
        Assert(new FaultRow { Color = "B/G" }.WireColorBrush is LinearGradientBrush,
            "Multi-color wire is combined into one striped Màu cell");

        var disabled = new ResistanceChannelEditor(
            new ResistanceChannelSetting { Enabled = true, Name = "R3", Channel = 3, MinOhm = 1, MaxOhm = 2 },
            3)
        {
            Enabled = false,
            ChannelSelection = 0
        }.ToSetting();
        Assert(!disabled.Enabled && disabled.Channel == 0 && disabled.Name == "R3",
            "Resistance UI channel 'Không dùng' saves disabled without losing internal name");

        var enabled = new ResistanceChannelEditor(
            new ResistanceChannelSetting { Enabled = false, Name = "R1", Channel = 0, MinOhm = 0.1, MaxOhm = 0.5 },
            1)
        {
            Enabled = true,
            ChannelSelection = 2
        }.ToSetting();
        Assert(enabled.Enabled && enabled.Channel == 2 && enabled.MinOhm == 0.1 && enabled.MaxOhm == 0.5,
            "Resistance UI active channel saves enabled and preserves min/max");

        var resistanceProduction = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ResistanceChannels =
            [
                new ResistanceChannelSetting
                {
                    Enabled = true,
                    Name = "R1",
                    Channel = 2,
                    MinOhm = 100,
                    MaxOhm = 200
                }
            ]
        };
        ProductModel modelWithoutThtResistance = Model(("PAIR", new[] { 1, 18 }));
        List<ResistanceStep> configuredSteps =
            ResistanceMeasurementPlan.BuildEnabledSteps(resistanceProduction);
        Assert(modelWithoutThtResistance.ResistanceSteps.Count == 0 &&
               configuredSteps is [{ Name: "R1", Channel: 2, MinOhm: 100, MaxOhm: 200 }],
            "Canonical Production Settings plan creates steps without THT resistance/legacy RouteA/RouteB");

        using var engine = CreateEngine(out _);
        engine.SetModel(Model(("PAIR", new[] { 1, 18 })));
        int changed = 0;
        engine.Changed += (_, _) => changed++;

        ScanFrame frame = Frame((1, new[] { 18 }));
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < 10_000; index++)
            engine.ProcessFrame(frame);
        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert(changed <= 1, "Identical complete frames do not raise unbounded engine UI updates");
        Console.WriteLine(
            $"PERF: 10,000 identical ProcessFrame calls: {stopwatch.ElapsedMilliseconds} ms, {allocated:N0} bytes allocated");
    }

    private static void TestFinalTestStatusGuards()
    {
        var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
        ProductionConfigService.SetMasterFaultRequiredCountForPath(settings, "PN-0.tht", 0);
        ProductModel model0 = new() { ModelName = "PN-0", PartNumber = "PN-0", SourcePath = "PN-0.tht" };
        Assert(ProductionConfigService.GetMasterFaultRequiredCount(settings, model0) == 0,
            "Master minimum 0 must be preserved and disable Master");

        ProductionConfigService.SetMasterFaultRequiredCountForPath(settings, "PN-1.tht", 1);
        ProductModel model1 = new() { ModelName = "PN-1", PartNumber = "PN-1", SourcePath = "PN-1.tht" };
        Assert(ProductionConfigService.GetMasterFaultRequiredCount(settings, model1) == 1,
            "Master minimum 1 must be preserved");

        ProductionConfigService.SetMasterFaultRequiredCountForPath(settings, "PN-2.tht", 2);
        ProductModel model2 = new() { ModelName = "PN-2", PartNumber = "PN-2", SourcePath = "PN-2.tht" };
        Assert(ProductionConfigService.GetMasterFaultRequiredCount(settings, model2) == 2,
            "Master minimum 2 must be preserved");

        TestViewModel disabledMasterVm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        disabledMasterVm.LoadPreparedModelAsync(model0).GetAwaiter().GetResult();
        Assert(disabledMasterVm.MasterApproved, "Master min 0 unlocks production immediately");
        Assert(disabledMasterVm.MasterState == MasterSequenceState.Completed, "Master min 0 marks Master completed/disabled");
        Assert(!disabledMasterVm.IsMasterSequenceActive && !disabledMasterVm.IsMasterBannerVisible,
            "Master min 0 must not show Master banner");
        Assert(disabledMasterVm.ProductionEnabled, "Master min 0 allows production");
        Assert(disabledMasterVm.ResultStatusText == "SẴN SÀNG", "Ready result text is canonical");
        Assert(disabledMasterVm.StateBackground == "#FFF3A0" && disabledMasterVm.StateForeground == "#222222",
            "Ready status uses yellow/dark mapping");

        TestViewModel enabledMasterVm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 1 });
        enabledMasterVm.LoadPreparedModelAsync(model1).GetAwaiter().GetResult();
        Assert(!enabledMasterVm.MasterApproved && enabledMasterVm.IsMasterSequenceActive,
            "Master min 1 keeps Master workflow enabled");
        Assert(enabledMasterVm.MasterRequiredFaultCount == 1, "Master min 1 requires one unique fault");
        Assert(enabledMasterVm.ResultStatusText == "KIỂM TRA MASTER ĐẠT" &&
               enabledMasterVm.State == "KIỂM TRA MASTER ĐẠT" &&
               enabledMasterVm.StateBackground == "#FFF3A0",
            "Waiting Master uses the compact production display and canonical yellow background");

        TestViewModel statusVm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        statusVm.State = "PASS";
        Assert(statusVm.ResultStatusText == "PASS" && statusVm.StateBackground == "#2AA84A" && statusVm.StateForeground == "#FFFFFF",
            "PASS status mapping");
        statusVm.State = "PASS - THÁO SẢN PHẨM";
        Assert(statusVm.ResultStatusText == "THÁO SẢN PHẨM" && statusVm.StateBackground == "#2AA84A",
            "Committed PASS explicitly asks for product removal until ProductRemoved returns the UI to ready");
        statusVm.State = "ĐANG TEST LEAK";
        Assert(statusVm.ResultStatusText == "ĐANG TEST LEAK" &&
               statusVm.StateBackground == "#FFF3A0",
            "Leak stage has an explicit in-progress presentation before PASS");
        statusVm.State = "CHƯA ĐẠT";
        Assert(statusVm.ResultStatusText == "FAIL" && statusVm.StateBackground == "#C62828" && statusVm.StateForeground == "#FFFFFF",
            "FAIL status mapping");
        statusVm.State = "CHỜ THÁO SẢN PHẨM";
        Assert(statusVm.ResultStatusText == "THÁO SẢN PHẨM" &&
               statusVm.StateBackground == "#FFF3A0" &&
               statusVm.StateForeground == "#222222",
            "Removal interlock must not be presented as SẴN SÀNG");
        statusVm.State = "ĐANG KIỂM TRA...";
        Assert(statusVm.ResultStatusText == "ĐANG TEST" && statusVm.StateBackground == "#FFF3A0" && statusVm.StateForeground == "#222222",
            "Testing status mapping");
        statusVm.State = "ĐANG KẾT NỐI BO";
        Assert(statusVm.ResultStatusText == "ĐANG KẾT NỐI BO" &&
               statusVm.StateBackground == "#FFF3A0" &&
               statusVm.StateForeground == "#222222",
            "Board connection progress must not be presented as production testing");
        statusVm.State = "MODEL ĐÃ TẢI - BO CHƯA KẾT NỐI";
        Assert(statusVm.ResultStatusText == "CHƯA KẾT NỐI BO" &&
               statusVm.StateBackground == "#C62828" &&
               statusVm.StateForeground == "#FFFFFF",
            "Disconnected board must never be presented as ready");

        statusVm.LoadPreparedModelAsync(model0).GetAwaiter().GetResult();
        MethodInfo buildFinalPassRejectionFaults = typeof(TestViewModel).GetMethod(
            "BuildFinalPassRejectionFaults",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Final PASS rejection fault builder not found");
        FaultDetail[] finalRejectionFaults =
            (FaultDetail[])(buildFinalPassRejectionFaults.Invoke(statusVm, [model0])
                ?? throw new InvalidOperationException("Final PASS rejection fault builder returned null"));
        Assert(finalRejectionFaults.Length == 1 &&
               finalRejectionFaults[0].Message.Contains("Continuity=", StringComparison.Ordinal) &&
               finalRejectionFaults[0].Message.Contains("Resistance", StringComparison.Ordinal),
            "Final PASS rejection always provides operator-visible continuity/resistance detail");

        TestViewModel recoveryVm = CreateTestViewModel(
            new ProductionSettings { MasterFaultRequiredCount = 0 },
            out FakeBoard recoveryBoard);
        ProductModel recoveryModel = Model(("RECOVERY-PAIR", new[] { 1, 18 }));
        recoveryVm.LoadPreparedModelAsync(recoveryModel).GetAwaiter().GetResult();
        typeof(TestViewModel).GetField("_runtimeMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(recoveryVm, 1);
        TestEngine recoveryEngine =
            (TestEngine)(typeof(TestViewModel).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(recoveryVm) ?? throw new InvalidOperationException("Recovery TestEngine not found"));
        recoveryEngine.SetFrameProcessingEnabled(true);
        recoveryBoard.Publish(FrameSeq(1, (1, new[] { 18 })));
        Assert(recoveryVm.Faults.Count == 0, "Passed recovery model hides completed network rows before removal");
        long recoveryGeneration = (long)(typeof(TestViewModel).GetField(
            "_runtimeGeneration",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(recoveryVm) ?? 0L);
        MethodInfo recoverUncommittedFail = typeof(TestViewModel).GetMethod(
            "RecoverAfterUncommittedFailAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Uncommitted FAIL recovery method not found");
        Task publishRecoveryFrame = Task.Run(async () =>
        {
            await Task.Delay(50);
            recoveryBoard.Publish(FrameSeq(2));
        });
        ((Task)(recoverUncommittedFail.Invoke(
            recoveryVm,
            [recoveryModel, recoveryGeneration, CancellationToken.None, "SELF_TEST"])
            ?? throw new InvalidOperationException("Uncommitted FAIL recovery returned null")))
            .GetAwaiter()
            .GetResult();
        publishRecoveryFrame.GetAwaiter().GetResult();
        Assert(recoveryVm.ResultStatusText == "SẴN SÀNG" &&
               recoveryVm.Faults.Count == 2 &&
               !recoveryBoard.Commands.Contains("START") &&
               !recoveryBoard.Commands.Contains("SET:2"),
            "Rejected FAIL commit reuses healthy removal scan, restores IO rows, and cannot remain latched at KHÔNG ĐẠT");

        string xaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "TestWindow.xaml"));
        Assert(!xaml.Contains("ProbeToggleText", StringComparison.Ordinal) &&
               !xaml.Contains("HIỆN DÒ CHÂN", StringComparison.Ordinal) &&
               !xaml.Contains("ẨN DÒ CHÂN", StringComparison.Ordinal),
            "Production TestView must not expose a Probe toggle button");
        Assert(xaml.Contains(
                   "Text=\"{Binding Lot, Mode=OneWay}\" Style=\"{StaticResource PiCounterTextStyle}\"",
                   StringComparison.Ordinal) &&
               !xaml.Contains(
                   "Text=\"{Binding ProbeCycleCount, Mode=OneWay}\" Style=\"{StaticResource PiCounterTextStyle}\"",
                   StringComparison.Ordinal),
            "TestView Số LOT must display the daily accepted quantity, never the probe maintenance counter");
        Assert(xaml.Contains("x:Key=\"WireColorCellTemplate\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Key=\"HtdrvGridTextStyle\"", StringComparison.Ordinal) &&
               xaml.Contains("TestFaultGridFontSize", StringComparison.Ordinal) &&
               xaml.Contains("TestGridRowHeight", StringComparison.Ordinal) &&
               xaml.Contains("Header=\"M&#224;u\" Width=\"0.60*\" MinWidth=\"64\"", StringComparison.Ordinal) &&
               !xaml.Contains("Header=\"#1\"", StringComparison.Ordinal) &&
               !xaml.Contains("Header=\"#2\"", StringComparison.Ordinal) &&
               !xaml.Contains("Header=\"#3\"", StringComparison.Ordinal) &&
               !xaml.Contains("Header=\"#4\"", StringComparison.Ordinal),
            "TestView uses responsive bold wiring text and a narrow original-style Màu column without #1..#4");

        string testWindowSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "TestWindow.xaml.cs"));
        Assert(testWindowSource.Contains("ApplyResponsiveTestGridLayout", StringComparison.Ordinal) &&
               testWindowSource.Contains("Math.Min(widthScale, heightScale)", StringComparison.Ordinal) &&
               testWindowSource.Contains("TestGridMaximumScale = 1.25", StringComparison.Ordinal) &&
               testWindowSource.Contains("Resources[\"TestFaultGridFontSize\"]", StringComparison.Ordinal),
            "TestView keeps large 1280x768 text and scales grid typography/row spacing uniformly through Full HD");
        int yellowLedIndex = xaml.IndexOf("x:Name=\"YellowStatusLed\"", StringComparison.Ordinal);
        int whiteLedIndex = xaml.IndexOf("x:Name=\"WhiteStatusLed\"", StringComparison.Ordinal);
        int greenLedIndex = xaml.IndexOf("x:Name=\"GreenStatusLed\"", StringComparison.Ordinal);
        int redLedIndex = xaml.IndexOf("x:Name=\"RedStatusLed\"", StringComparison.Ordinal);
        Assert(yellowLedIndex >= 0 && yellowLedIndex < whiteLedIndex &&
               whiteLedIndex < greenLedIndex && greenLedIndex < redLedIndex &&
               testWindowSource.Contains("TimeSpan.FromMilliseconds(180)", StringComparison.Ordinal) &&
               testWindowSource.Contains("TimeSpan.FromMilliseconds(90)", StringComparison.Ordinal) &&
               testWindowSource.Contains("blink < 3", StringComparison.Ordinal) &&
               testWindowSource.Contains("viewModel.IsDeviceFault", StringComparison.Ordinal),
            "TestView LEDs keep YELLOW-WHITE-GREEN-RED order, pulse timing, three PASS blinks, and hardware-fault blackout");

        string mainWindowSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "MainWindow.xaml.cs"));
        Assert(mainWindowSource.Contains(
                   "StartTestButton.IsEnabled = _viewModel.Model is not null;",
                   StringComparison.Ordinal) &&
               mainWindowSource.Contains(
                   "SelectModelButton.IsEnabled = !blocked;",
                   StringComparison.Ordinal) &&
               mainWindowSource.Contains("offlinePreview: offlinePreview", StringComparison.Ordinal) &&
               mainWindowSource.Contains("autoStartProduction: boardConnected && hasCapacity", StringComparison.Ordinal),
            "Main actions allow offline model preview while production auto-start still requires a connected board");

        string mainViewModelSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "ViewModels", "MainViewModel.cs"));
        Assert(!mainViewModelSource.Contains(
                   "Chỉ được chọn mã hàng sau khi bo kết nối thành công.",
                   StringComparison.Ordinal),
            "Selecting and parsing a model is allowed without a connected board");
        Assert(mainViewModelSource.Contains("requireStartupIoClear: false", StringComparison.Ordinal),
            "Production startup accepts the first live frame for the remembered model without requiring a clean baseline");

        Assert(xaml.Contains("x:Name=\"OperationTablesHost\"", StringComparison.Ordinal) &&
               testWindowSource.Contains("OperationTablesHost.Visibility = Visibility.Collapsed;", StringComparison.Ordinal) &&
               testWindowSource.Contains("XEM MÃ HÀNG OFFLINE - BO CHƯA KẾT NỐI", StringComparison.Ordinal) &&
               testWindowSource.Contains("if (_autoStartProduction && !_offlinePreview)", StringComparison.Ordinal),
            "Offline TestWindow hides all operation tables and cannot ARM production testing");

        string testViewModelSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "ViewModels", "TestViewModel.cs"));
        Assert(testViewModelSource.Contains("resumeCurrentStartupModel", StringComparison.Ordinal) &&
               testViewModelSource.Contains("BoardFrameActivity?.Invoke(this, frame);", StringComparison.Ordinal),
            "Remembered-model START resumes real frame processing while UI LEDs observe the existing board frame stream");
        int initializeHardwareIndex = testViewModelSource.IndexOf(
            "await InitializeHardwareAsync();",
            StringComparison.Ordinal);
        int loadLastModelIndex = testViewModelSource.IndexOf(
            "await LoadLastTestedModelAsync();",
            StringComparison.Ordinal);
        Assert(initializeHardwareIndex >= 0 && loadLastModelIndex > initializeHardwareIndex,
            "Startup connects the board before loading the last product model");

        string settingsXaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "ProductionSettingsPage.xaml"));
        Assert(settingsXaml.Contains("Content=\"KẾT NỐI\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("Click=\"ConnectPrinter_Click\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("x:Name=\"PrinterConnectionStatusText\"", StringComparison.Ordinal),
            "Production settings exposes reconnectable printer control and connection status");
        Assert(settingsXaml.Contains("<ColumnDefinition Width=\"110\"/>", StringComparison.Ordinal) &&
               (settingsXaml.Contains("Content=\"QU&#201;T\"", StringComparison.Ordinal) ||
                settingsXaml.Contains("Content=\"QUÉT\"", StringComparison.Ordinal)) &&
               settingsXaml.Contains("Width=\"115\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("Grid.Row=\"2\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("Grid.Column=\"2\"", StringComparison.Ordinal),
            "Label printer controls use a compact three-row layout so manual resistance stays visible");
        Assert(settingsXaml.Contains("x:Name=\"RelayWiringModeComboBox\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("Settings.RelayWiringMode", StringComparison.Ordinal),
            "Production settings exposes one physical relay-role mapping and states FAIL behavior clearly");
        Assert(xaml.Contains("x:Name=\"TestHeaderSurface\" Width=\"1344\" Height=\"234\"", StringComparison.Ordinal) &&
               xaml.Contains("x:Name=\"TestAppVersionText\"", StringComparison.Ordinal) &&
               xaml.Contains("Grid.Row=\"8\"", StringComparison.Ordinal) &&
               xaml.Contains("ScrollViewer.HorizontalScrollBarVisibility\" Value=\"Auto\"", StringComparison.Ordinal) &&
               xaml.Contains("MinWidth=\"125\"", StringComparison.Ordinal),
            "TestView scales its fixed header and preserves table text at 1024/1368 widths");
        Assert(settingsXaml.Contains("<ScrollViewer Grid.Row=\"1\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("x:Name=\"SettingsPanelsHost\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("x:Name=\"LabelSettingsPanel\"", StringComparison.Ordinal),
            "Production settings wraps panels and scrolls instead of clipping at 1024x768");
        Assert(settingsXaml.Contains("Tag=\"TEM_BE_QR\"", StringComparison.Ordinal),
            "Production settings exposes the dedicated TEM BE QR selection");

        Assert(settingsXaml.Contains(
                   "Settings.EnableSystemLogs, Mode=TwoWay",
                   StringComparison.Ordinal) &&
               settingsXaml.Contains(
                   "Data/History vẫn luôn được lưu",
                   StringComparison.Ordinal),
            "Production settings exposes a system-log master switch that preserves History");

        string appSource = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "App.xaml.cs"));
        string soundSource = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Services", "AppSoundService.cs"));
        Assert(appSource.Contains("AppSoundService.Current.PlayStartup();", StringComparison.Ordinal) &&
               appSource.Contains("DispatcherPriority.ApplicationIdle", StringComparison.Ordinal) &&
               soundSource.Contains("SafePlaySync(player)", StringComparison.Ordinal) &&
               soundSource.Contains("_startupPlaybackActive", StringComparison.Ordinal),
            "START.wav begins after first-render idle and is protected from Probe reset/click interruption");
        Assert(typeof(AppSoundService).Assembly.GetManifestResourceNames().Any(name =>
                   name.EndsWith(".Assets.Sounds.COMPUTER.wav", StringComparison.OrdinalIgnoreCase)) &&
               soundSource.Contains("PlayProductStart()", StringComparison.Ordinal) &&
               soundSource.Contains("CreatePlayer(\"COMPUTER.wav\"", StringComparison.Ordinal) &&
               testViewModelSource.Contains(
                   "PlayProductStartSoundOnce(generation, preserveProductionFaultsForProbe);",
                   StringComparison.Ordinal) &&
               testViewModelSource.Contains(
                   "Interlocked.CompareExchange(ref _productStartSoundPlayed, 1, 0)",
                   StringComparison.Ordinal) &&
               testViewModelSource.Contains(
                   "CurrentProductionPhase != ProductionPhase.Continuity",
                   StringComparison.Ordinal),
            "COMPUTER.wav is embedded and requested once on the first real Production connection of each cycle");

        TestViewModel deviceFaultVm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        deviceFaultVm.LoadPreparedModelAsync(model0).GetAwaiter().GetResult();
        MethodInfo reportDeviceFault = typeof(TestViewModel).GetMethod(
            "ReportDeviceFaultForTest",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DeviceFault test reporter not found");
        for (int index = 0; index < 100; index++)
            reportDeviceFault.Invoke(deviceFaultVm, [new ArgumentOutOfRangeException("index", "simulated index fault"), -1]);
        Assert(deviceFaultVm.IsDeviceFault, "DeviceFault latches after index exception");
        Assert(deviceFaultVm.DeviceFaultTransitionCount == 1, "Repeated index exceptions produce one DeviceFault transition");
        Assert(deviceFaultVm.DeviceFaultDialogCount == 1, "Repeated index exceptions produce one operator dialog episode");
        Assert(deviceFaultVm.ResultStatusText == "LỖI THIẾT BỊ" &&
               deviceFaultVm.StateBackground == "#C62828" &&
               deviceFaultVm.StateForeground == "#FFFFFF",
            "DeviceFault status mapping");

        reportDeviceFault.Invoke(deviceFaultVm, [new InvalidOperationException("second episode"), -1]);
        Assert(deviceFaultVm.IsDeviceFault &&
               deviceFaultVm.DeviceFaultTransitionCount == 1 &&
               deviceFaultVm.DeviceFaultDialogCount == 1,
            "DeviceFault remains latched for the process lifetime and cannot be reset in-app");

        string testWindowXaml = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "TestWindow.xaml"));
        Assert(!testWindowXaml.Contains("ResetDeviceFaultCommand", StringComparison.Ordinal) &&
               !testWindowXaml.Contains("KH&#7902;I T&#7840;O L&#7840;I", StringComparison.Ordinal),
            "DeviceFault UI has no reinitialize button; operator must restart the application");
    }

    private static void TestManualModeInterlock()
    {
        var settings = new ProductionSettings
        {
            ManualModeEnabled = false,
            MasterFaultRequiredCount = 0,
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50
        };
        TestViewModel vm = CreateTestViewModel(settings, out FakeBoard board);
        vm.LoadPreparedModelAsync(Model(("PAIR", new[] { 1, 18 }))).GetAwaiter().GetResult();

        typeof(TestViewModel).GetField("_runtimeMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(vm, 1);
        Assert(vm.CanEnterManualMode && !vm.IsManualModeActive,
            "Manual relay menu is ready without a saved ManualModeEnabled setting");

        board.Commands.Clear();
        int relay = vm.SetManualRelayAsync(1, true).GetAwaiter().GetResult();
        Assert(relay == 1 && vm.IsManualModeActive && vm.State == "MANUAL" && !board.IsScanning,
            "Manual Relay 1 ON holds one relay and keeps Production scan stopped");
        Assert(board.Commands.Count(command => command == "OFF") >= 1 &&
               board.Commands.Last() == "SET:1",
            "Manual Relay 1 ON forces all relay OFF before selecting Relay 1");

        board.Commands.Clear();
        relay = vm.SetManualRelayAsync(2, true).GetAwaiter().GetResult();
        Assert(relay == 2 && vm.IsManualModeActive && !board.IsScanning,
            "Manual Relay 2 ON replaces Relay 1 while Manual remains active");
        Assert(string.Join(",", board.Commands) == "OFF,SET:2",
            "Manual relay switching is mutually exclusive: OFF before SET:2");

        board.Commands.Clear();
        vm.ResetManualOutputsAsync().GetAwaiter().GetResult();
        Assert(!vm.IsManualModeActive &&
               string.Join(",", board.Commands) == "OFF,RESET,OFF,START",
            "RESET while Relay 2 is ON forces OFF, clears the board, confirms OFF, then resumes scan");

        board.Commands.Clear();
        relay = vm.SetManualRelayAsync(1, true).GetAwaiter().GetResult();
        Assert(relay == 1 && vm.IsManualModeActive && board.Commands.Last() == "SET:1",
            "Manual Relay 1 can be selected again after RESET");

        board.Commands.Clear();
        relay = vm.SetManualRelayAsync(1, false).GetAwaiter().GetResult();
        Assert(relay == 0 && !vm.IsManualModeActive &&
               board.Commands.Contains("OFF") && board.Commands.Last() == "START",
            "TẮT TẤT CẢ forces both outputs OFF and resumes Production scan");

        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        Assert(vm.State != "MANUAL", "Production is no longer locked after direct Relay OFF/RESET");

        var faultSettings = new ProductionSettings
        {
            ManualModeEnabled = false,
            MasterFaultRequiredCount = 0,
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50
        };
        TestViewModel faultVm = CreateTestViewModel(faultSettings, out FakeBoard faultBoard);
        faultVm.LoadPreparedModelAsync(Model(("PAIR", new[] { 1, 18 }))).GetAwaiter().GetResult();
        faultBoard.ThrowOnSetRelay = true;
        try
        {
            faultVm.SetManualRelayAsync(1, true).GetAwaiter().GetResult();
            throw new InvalidOperationException("Manual relay failure should throw");
        }
        catch (InvalidOperationException)
        {
        }

        Assert(faultVm.IsDeviceFault, "Manual relay hardware failure latches DeviceFault");
        Assert(faultBoard.Commands.Contains("OFF"), "Manual relay failure attempts safe OFF");

        string settingsPageSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "ProductionSettingsPage.xaml.cs"));
        string mainWindowSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "MainWindow.xaml.cs"));
        string settingsXaml = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "ProductionSettingsPage.xaml"));
        Assert(settingsPageSource.Contains("await _main.Test.ResetManualOutputsAsync();", StringComparison.Ordinal) &&
               mainWindowSource.Contains("await settingsPage.ReleaseManualOutputsAsync();", StringComparison.Ordinal),
            "Every Settings-page close path awaits Manual RESET before releasing the page");
        Assert(settingsXaml.Contains("T&#7854;T T&#7844;T C&#7842;", StringComparison.Ordinal) &&
               !settingsXaml.Contains("ManualRelay2OffCommand", StringComparison.Ordinal),
            "Manual relay UI exposes the proven mutually-exclusive R1/R2 selector and one ALL OFF command");

        string d2xxSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Services", "D2xxBoardTransport.cs"));
        Assert(d2xxSource.Contains("const int RelayCommandSettleMs = 100;", StringComparison.Ordinal) &&
               d2xxSource.Contains("await Task.Delay(RelayCommandSettleMs, ct);", StringComparison.Ordinal),
            "D2XX waits for firmware to latch relay OFF before RESET or START_SCAN");
    }

    private static void TestStartupIoInterlock()
    {
        ScanFrame duplicatedDirections = FrameSeq(
            100,
            (1, new[] { 18 }),
            (18, new[] { 1 }));
        IReadOnlyList<StartupIoContactPair> normalized =
            StartupIoInterlock.FindConnectedPairs(duplicatedDirections);
        Assert(normalized.Count == 1 && normalized[0] == new StartupIoContactPair(1, 18),
            "Startup IO detector normalizes and de-duplicates bidirectional edges");

        var resumeProduction = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ProductSettleTimeMs = 0,
            WrongConnectionConfirmMs = 0,
            ShortCircuitConfirmMs = 0
        };
        TestViewModel resumeVm = CreateTestViewModel(
            resumeProduction,
            out FakeBoard resumeBoard,
            requireStartupIoClear: false);
        resumeVm.SetModel(Model(("RESUME-PAIR", new[] { 1, 18 })));
        resumeBoard.Publish(duplicatedDirections);
        Assert(resumeVm.IsProductRemovalPending,
            "Installed product in background locks changing the current model");
        AssertThrows<InvalidOperationException>(
            () => resumeVm.SetModel(Model(("OTHER", new[] { 2, 19 }))),
            "Background installed product cannot be assigned to another model");
        resumeVm.StartProductionTestAsync().GetAwaiter().GetResult();
        Assert(!resumeVm.IsProductRemovalPending &&
               !resumeVm.State.Contains("THÁO SẢN PHẨM", StringComparison.OrdinalIgnoreCase),
            "START resumes the remembered model without requiring a clean frame first");

        var production = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ProductSettleTimeMs = 0,
            WrongConnectionConfirmMs = 0,
            ShortCircuitConfirmMs = 0
        };
        TestViewModel vm = CreateTestViewModel(
            production,
            out FakeBoard board,
            requireStartupIoClear: true);
        ProductModel startupInterlockModel = Model(("PAIR", new[] { 1, 18 }));
        startupInterlockModel.ModelName = "SELF-TEST-STARTUP-INTERLOCK";
        startupInterlockModel.PartNumber = "SELF-TEST-STARTUP-INTERLOCK";
        vm.SetModel(startupInterlockModel);

        board.Publish(duplicatedDirections);
        Assert(vm.IsProductRemovalPending &&
               vm.ResultStatusText == "VUI LÒNG THÁO SẢN PHẨM",
            "Background startup scan locks product selection and START while a product or stuck pin remains");
        AssertThrows<InvalidOperationException>(
            () => vm.SetModel(Model(("OTHER", new[] { 2, 19 }))),
            "Product model cannot change while the startup removal gate is locked");
        int commandsBeforeBlockedStart = board.Commands.Count;
        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        Assert(board.Commands.Count == commandsBeforeBlockedStart && vm.IsProductRemovalPending,
            "START cannot arm production while the startup removal gate is locked");

        board.Publish(FrameSeq(101));
        Assert(!vm.IsProductRemovalPending && vm.ResultStatusText == "SẴN SÀNG",
            "A complete clean background frame unlocks product selection and START");

        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        int totalBeforeWarning = vm.Total;
        int passBeforeWarning = vm.Pass;
        int failBeforeWarning = vm.Fail;

        Assert(vm.State.Contains("ĐỒNG BỘ DỮ LIỆU BO", StringComparison.Ordinal) &&
               vm.ResultStatusText == "ĐỒNG BỘ BO",
            "Production waits for a clean baseline frame without reporting an IO-capacity warning");

        board.Publish(FrameSeq(102, (1, new[] { 18 }), (18, new[] { 1 })));
        Assert(vm.ResultStatusText == "VUI LÒNG THÁO SẢN PHẨM" &&
               vm.IsProductRemovalPending &&
               vm.Faults.Count == 1 &&
               vm.Faults[0].Kind == FaultKind.Info &&
               vm.Faults[0].ProductFaultType == ProductFaultType.None &&
               vm.Faults[0].ActualSourceIo == 1 &&
               vm.Faults[0].ActualTargetIo == 18,
            "A product left on the jig asks for removal without reporting a false IO fault");
        Assert(vm.Total == totalBeforeWarning &&
               vm.Pass == passBeforeWarning &&
               vm.Fail == failBeforeWarning &&
               !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            $"Startup IO warning cannot commit production or activate a relay " +
            $"(totals {totalBeforeWarning}/{passBeforeWarning}/{failBeforeWarning} -> " +
            $"{vm.Total}/{vm.Pass}/{vm.Fail}; commands={string.Join(',', board.Commands)})");

        board.Publish(FrameSeq(103));
        Assert(!vm.IsProductRemovalPending &&
               vm.State == "CHỜ LẮP SẢN PHẨM" && vm.ResultStatusText == "SẴN SÀNG",
            "A complete clean frame clears the startup interlock and arms Production");
    }

    private static void TestDuplicateClipFaultRows()
    {
        TestViewModel vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        MethodInfo synchronize = typeof(TestViewModel).GetMethod(
            "SynchronizeFaultRows",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Fault-row synchronizer not found");

        static FaultRow DuplicateRow(string status) => new()
        {
            Kind = FaultKind.WrongWiring,
            ProductFaultType = ProductFaultType.WrongWiring,
            FaultType = "SAI KẾT NỐI CLIP",
            Io = 7,
            ActualSourceIo = 7,
            ActualTargetIo = 11,
            Status = status
        };

        for (int index = 0; index < 7; index++)
            vm.Faults.Add(DuplicateRow($"old-{index}"));

        FaultRow[] desired = Enumerable.Range(0, 10)
            .Select(index => DuplicateRow($"new-{index}"))
            .ToArray();
        synchronize.Invoke(vm, [desired]);

        Assert(!vm.IsDeviceFault,
            "Duplicate CLIP display rows are a UI/configuration case, not unstable hardware");
        Assert(vm.Faults.Count == desired.Length &&
               vm.Faults.Select(row => row.Status).SequenceEqual(desired.Select(row => row.Status)),
            "Duplicate CLIP rows synchronize without an out-of-range Move");
    }

    private static void TestD2xxResistanceRouting()
    {
        for (int channel = D2xxResistanceRouting.MinChannel;
             channel <= D2xxResistanceRouting.MaxChannel;
             channel++)
        {
            byte selector = D2xxResistanceRouting.ToResistanceSelector(channel);
            Assert(selector == channel, $"CH{channel} selector must be direct value 0x{channel:X2}");
            Assert(
                D2xxResistanceRouting.BuildRouteB(channel).SequenceEqual(
                    new byte[] { 0x91, 0x00, 0x00, (byte)channel }),
                $"CH{channel} RouteB must end in 0x{channel:X2}");
        }

        Assert(D2xxResistanceRouting.BuildRouteA().SequenceEqual(
            new byte[] { 0x90, 0x00, 0x00, 0x01 }),
            "Resistance RouteA is canonical");
        Assert(D2xxResistanceRouting.ToResistanceSelector(3) != 0x04,
            "CH3 must not regress to bitmask 0x04");
        Assert(D2xxResistanceRouting.ToResistanceSelector(4) != 0x08,
            "CH4 must not regress to bitmask 0x08");
        Assert(D2xxResistanceRouting.ToResistanceSelector(5) != 0x10,
            "CH5 must not regress to bitmask 0x10");
        Assert(D2xxResistanceRouting.ToResistanceSelector(10) == 0x0A,
            "CH10 selector must be 0x0A");
        AssertThrows<ArgumentOutOfRangeException>(
            () => D2xxResistanceRouting.ToResistanceSelector(0),
            "CH0 must be rejected by D2XX routing");
        AssertThrows<ArgumentOutOfRangeException>(
            () => D2xxResistanceRouting.ToResistanceSelector(11),
            "CH11 must be rejected by D2XX routing");
        AssertThrows<ArgumentOutOfRangeException>(
            () => D2xxResistanceRouting.BuildRouteB(0),
            "CH0 route must be rejected");
        AssertThrows<ArgumentOutOfRangeException>(
            () => D2xxResistanceRouting.BuildRouteB(11),
            "CH11 route must be rejected");
        Assert(D2xxResistanceRouting.BuildReleaseRouteB().SequenceEqual(
                new byte[] { 0x91, 0x00, 0x00, 0x00 }) &&
               D2xxResistanceRouting.BuildReleaseRouteA().SequenceEqual(
                new byte[] { 0x90, 0x00, 0x00, 0x30 }),
            "Resistance release frames remain 91/00 and 90/30");

        var legacyFiveSlots = new ProductionSettings
        {
            RelayWiringMode = 1,
            ResistanceChannels =
            [
                new() { Enabled = true, Name = "R1", Channel = 8, MinOhm = 9, MaxOhm = 11 },
                new() { Enabled = false, Name = "R2", Channel = 2, MinOhm = 20, MaxOhm = 25 },
                new() { Enabled = true, Name = "R3", Channel = 10, MinOhm = 95, MaxOhm = 105 },
                new() { Enabled = true, Name = "R4", Channel = 4, MinOhm = 0.5, MaxOhm = 1.5 },
                new() { Enabled = false, Name = "R5", Channel = 5, MinOhm = 0, MaxOhm = 0 }
            ]
        };
        MethodInfo normalize = typeof(ProductionConfigService).GetMethod(
            "Normalize",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Production settings normalizer not found");
        normalize.Invoke(null, [legacyFiveSlots]);

        Assert(legacyFiveSlots.ResistanceChannels.Length == 10,
            "Five-slot configuration must migrate to ten slots");
        Assert(legacyFiveSlots.ResistanceChannels[0] is
            { Name: "R1", Enabled: true, Channel: 8, MinOhm: 9, MaxOhm: 11 },
            "R1 fields must survive migration");
        Assert(legacyFiveSlots.ResistanceChannels[2] is
            { Name: "R3", Enabled: true, Channel: 10, MinOhm: 95, MaxOhm: 105 },
            "R3 arbitrary CH10 mapping and limits must survive migration");
        Assert(legacyFiveSlots.ResistanceChannels[5] is
            { Name: "R6", Enabled: false, Channel: 6, MinOhm: 0, MaxOhm: 0 } &&
            legacyFiveSlots.ResistanceChannels[9] is
            { Name: "R10", Enabled: false, Channel: 10, MinOhm: 0, MaxOhm: 0 },
            "R6-R10 must be added disabled with useful default channels");

        Assert(ResistanceMeasurementPlan.BuildManualSteps(legacyFiveSlots, 0)
                .Select(step => step.Name)
                .SequenceEqual(new[] { "R1", "R3", "R4" }),
            "Manual ALL measures every currently enabled resistance slot");
        Assert(ResistanceMeasurementPlan.BuildManualSteps(legacyFiveSlots, 5) is
                [{ Name: "R5", Channel: 5 }],
            "Manual single CH may measure its configured slot even when automatic measurement is disabled");

        string settingsResistanceXaml = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "ProductionSettingsPage.xaml"));
        Assert(settingsResistanceXaml.Contains("ManualMeasureResistanceCommand", StringComparison.Ordinal) &&
               settingsResistanceXaml.Contains("ManualResistanceOptions", StringComparison.Ordinal) &&
               settingsResistanceXaml.Contains("ManualResistanceResults", StringComparison.Ordinal) &&
               settingsResistanceXaml.Contains("Value=\"ĐANG ĐO\"", StringComparison.Ordinal),
            "Settings exposes manual ALL/single-CH measurement and returned results");

        string cfgPath = Path.Combine(
            Path.GetTempPath(),
            $"jbz-resistance-{Guid.NewGuid():N}.cfg");
        try
        {
            ProductionConfigService.SaveLegacyCfg(legacyFiveSlots, cfgPath);
            MethodInfo loadEnglishCfg = typeof(ProductionConfigService).GetMethod(
                "LoadEnglishCfg",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("Legacy production settings loader not found");
            var loaded = (ProductionSettings)(loadEnglishCfg.Invoke(null, [cfgPath])
                ?? throw new InvalidOperationException("Legacy production settings loader returned null"));

            Assert(loaded.ResistanceChannels.Length == 10,
                "Saved configuration must load all ten resistance slots");
            Assert(loaded.ResistanceChannels[0] is
                { Name: "R1", Enabled: true, Channel: 8, MinOhm: 9, MaxOhm: 11 } &&
                loaded.ResistanceChannels[2] is
                { Name: "R3", Enabled: true, Channel: 10, MinOhm: 95, MaxOhm: 105 },
                "Save/load must preserve Enabled, Channel, MinOhm and MaxOhm");
            Assert(loaded.RelayWiringMode == 1 && loaded.FaultJigRelayNumber == 2,
                "Save/load must preserve reversed R1 MARKING / R2 JIG wiring and its FAIL eject relay");
        }
        finally
        {
            if (File.Exists(cfgPath))
                File.Delete(cfgPath);
        }

        var configured = new ProductionSettings
        {
            ResistanceChannels =
            [
                new() { Enabled = true, Name = "R1", Channel = 8, MinOhm = 1, MaxOhm = 2 },
                new() { Enabled = true, Name = "R2", Channel = 2, MinOhm = 3, MaxOhm = 4 },
                new() { Enabled = true, Name = "R3", Channel = 10, MinOhm = 5, MaxOhm = 6 },
                new() { Enabled = true, Name = "R4", Channel = 4, MinOhm = 7, MaxOhm = 8 },
                new() { Enabled = false, Name = "R5", Channel = 5, MinOhm = 9, MaxOhm = 10 },
                new() { Enabled = true, Name = "R6", Channel = 0, MinOhm = 11, MaxOhm = 12 },
                new() { Enabled = true, Name = "R7", Channel = 7, MinOhm = 0, MaxOhm = 10 },
                new() { Enabled = true, Name = "R8", Channel = 7, MinOhm = 0, MaxOhm = 10 }
            ]
        };
        List<ResistanceStep> steps = ResistanceMeasurementPlan.BuildEnabledSteps(configured);

        Assert(steps.Select(step => step.Name).SequenceEqual(new[] { "R1", "R2", "R3", "R4", "R7", "R8" }),
            "Runtime order must follow R slot order, not physical channel order");
        Assert(steps.Select(step => step.Channel).SequenceEqual(new[] { 8, 2, 10, 4, 7, 7 }),
            "Each R slot must retain its configured physical channel");
        Assert(steps.All(step => step.Name is not "R5" and not "R6"),
            "Disabled slots and Channel=0 slots must be skipped");

        var duplicateChannelPlan = new ProductionSettings
        {
            ResistanceChannels =
            [
                new() { Enabled = true, Name = "R1", Channel = 4, MinOhm = 1, MaxOhm = 2 },
                new() { Enabled = true, Name = "R2", Channel = 4, MinOhm = 3, MaxOhm = 4 }
            ]
        };
        Assert(
            ResistanceMeasurementPlan.BuildEnabledSteps(duplicateChannelPlan)
                .Select(step => step.Channel)
                .SequenceEqual(new[] { 4, 4 }),
            "Two R slots may independently select the same physical CH4");

        var malformed = new ProductionSettings
        {
            ResistanceChannels =
            [
                new() { Enabled = true, Name = "R2", Channel = 11, MinOhm = -5, MaxOhm = -10 },
                new() { Enabled = true, Name = "R1", Channel = 3, MinOhm = 5, MaxOhm = 2 },
                new() { Enabled = true, Name = "R1", Channel = 8, MinOhm = 1, MaxOhm = 2 },
                new() { Enabled = true, Name = "", Channel = 4, MinOhm = 1, MaxOhm = 3 }
            ]
        };
        normalize.Invoke(null, [malformed]);
        Assert(malformed.ResistanceChannels.Length == 10,
            "Duplicate/blank malformed settings normalize without crashing");
        Assert(malformed.ResistanceChannels[0] is
            { Name: "R1", Enabled: true, Channel: 8, MinOhm: 1, MaxOhm: 2 },
            "Duplicate R1 keeps the first fully valid record");
        Assert(malformed.ResistanceChannels[1] is
            { Name: "R2", Enabled: true, Channel: 10, MinOhm: 0, MaxOhm: 0 },
            "Out-of-range channel and invalid limits are safely clamped");

        var fakeBoard = new FakeBoard();
        var fakeVisa = new FakeKeysightVisaService(connected: true, measurement: 1.0);
        var fastApp = new AppSettings();
        fastApp.Test.ResistanceMinimumSettleMs = 0;
        fastApp.Test.ResistanceSampleIntervalMs = 0;
        // Giá trị legacy 3 không được phép làm engine đọc lặp lại.
        fastApp.Test.ResistanceStableSampleCount = 3;
        fastApp.Test.ResistanceStabilityTimeoutMs = 100;
        using (var measurementEngine = new TestEngine(fakeBoard, fakeVisa, fastApp, configured))
        {
            measurementEngine.SetModel(new ProductModel { ModelName = "R-PLAN" });
            List<ResistanceResult> measured = measurementEngine.MeasureResistanceAsync()
                .GetAwaiter().GetResult();

            Assert(fakeBoard.ResistanceSteps.Select(step => step.Name)
                    .SequenceEqual(new[] { "R1", "R2", "R3", "R4", "R7", "R8" }) &&
                   fakeBoard.ResistanceSteps.Select(step => step.Channel)
                    .SequenceEqual(new[] { 8, 2, 10, 4, 7, 7 }),
                "Engine routes valid slots in canonical R1-R10 order, including duplicate CH7");
            Assert(fakeBoard.ResistanceFrames.SelectMany(pair => pair)
                    .Chunk(8)
                    .All(bytes => bytes.Take(4).SequenceEqual(new byte[] { 0x90, 0x00, 0x00, 0x01 })),
                "Every measured slot starts with canonical 90 00 00 01");
            Assert(fakeBoard.ResistanceFrames
                    .Select(frame => frame[7])
                    .SequenceEqual(new byte[] { 0x08, 0x02, 0x0A, 0x04, 0x07, 0x07 }),
                "Engine emits direct CH8/CH2/CH10/CH4/CH7/CH7 selectors, never bitmasks");
            Assert(measured.Count == 6 && fakeVisa.MeasureCallCount == 6,
                "Disabled R5 and Channel=0 R6 create neither route nor Keysight call/result");
            Assert(fakeBoard.ReleaseResistanceRouteCount == 1 &&
                   fakeBoard.ReleaseResistanceFrames.SequenceEqual(
                       new byte[] { 0x91, 0x00, 0x00, 0x00, 0x90, 0x00, 0x00, 0x30 }),
                "Measurement route is released in finally after the sequence");
        }

        var manualBoard = new FakeBoard();
        var manualVisa = new FakeKeysightVisaService(connected: true, measurement: 5.5);
        using (var manualEngine = new TestEngine(manualBoard, manualVisa, fastApp, configured))
        {
            List<ResistanceStep> manualSteps = ResistanceMeasurementPlan.BuildManualSteps(configured, 10);
            var manualUpdates = new List<ResistanceResult>();
            List<ResistanceResult> manualResults = manualEngine
                .MeasureResistanceStepsAsync(manualSteps, manualUpdates.Add)
                .GetAwaiter()
                .GetResult();
            Assert(manualResults is [{ Name: "R3", Channel: 10 }] &&
                   manualBoard.ResistanceSteps.Select(step => step.Channel).SequenceEqual(new[] { 10 }) &&
                   manualVisa.MeasureCallCount == 1 &&
                   manualUpdates.Count == 2 &&
                   manualUpdates[0].ResultText == "ĐANG ĐO" &&
                   manualUpdates[1].ResultText == "PASS" &&
                   manualUpdates[1].Display != "—",
                "Manual single CH immediately reports measuring, then measured value and PASS");
        }

        var failureBoard = new FakeBoard();
        var failingVisa = new FakeKeysightVisaService(connected: true, measurement: 1.0)
        {
            ThrowOnMeasure = true
        };
        using (var failureEngine = new TestEngine(failureBoard, failingVisa, fastApp, duplicateChannelPlan))
        {
            failureEngine.SetModel(new ProductModel { ModelName = "R-FAIL" });
            AssertThrows<InvalidOperationException>(
                () => failureEngine.MeasureResistanceAsync().GetAwaiter().GetResult(),
                "Keysight failure must propagate to the production lifecycle");
            Assert(failureBoard.ReleaseResistanceRouteCount == 1,
                "Keysight failure still releases the resistance route in finally");
        }

        var allSkipped = new ProductionSettings
        {
            ResistanceChannels =
            [
                new() { Enabled = false, Name = "R1", Channel = 1 },
                new() { Enabled = true, Name = "R2", Channel = 0 }
            ]
        };
        var skippedBoard = new FakeBoard();
        var disconnectedVisa = new FakeKeysightVisaService(connected: false, measurement: 1.0);
        using (var skippedEngine = new TestEngine(skippedBoard, disconnectedVisa, fastApp, allSkipped))
        {
            skippedEngine.SetModel(new ProductModel { ModelName = "R-SKIP" });
            List<ResistanceResult> skipped = skippedEngine.MeasureResistanceAsync()
                .GetAwaiter().GetResult();
            Assert(skipped.Count == 0 &&
                   skippedBoard.ResistanceSteps.Count == 0 &&
                   disconnectedVisa.MeasureCallCount == 0,
                "All skipped slots return before VISA connection and produce no route/result");
        }
    }

    private static void TestWaterProofConfigurationAndPresentation()
    {
        var profile = new WaterProofModelSettings
        {
            Enabled = true,
            Channel1Enabled = true,
            Channel2Enabled = false,
            Channel3Enabled = true,
            Channel1Connector = "CN-A",
            Channel3Connector = "CN-C",
            PressTimeMs = 1200,
            WaitTimeMs = 600
        };

        WaterProofModelSettings clone = profile.Clone();
        Assert(clone.Channel1Connector == "CN-A" &&
               clone.Channel3Connector == "CN-C" &&
               clone.ConnectorForChannel(2) == string.Empty,
            "Leak profile clone preserves explicit THT connector mapping");

        var settings = new ProductionSettings();
        ProductionConfigService.SetWaterProofProfileForPath(settings, "LEAK-PART.tht", profile);
        WaterProofModelSettings loaded = ProductionConfigService.GetWaterProofProfileForPath(
            settings,
            "LEAK-PART.tht");
        Assert(loaded.Channel1Connector == "CN-A" && loaded.Channel3Connector == "CN-C",
            "Per-THT Leak profile stores connector mapping");

        Assert(WaterProofSerialService.BuildTestCommand(loaded) == ":TEST,1,0,1,0,1200,600\r\n",
            "Connector metadata must not change the proven Leak UART command format");

        var passRow = new WaterProofChannelResult
        {
            Channel = 1,
            Connector = "CN-A",
            IsMeasured = true,
            Passed = true
        };
        var failRow = new WaterProofChannelResult
        {
            Channel = 3,
            Connector = "CN-C",
            IsMeasured = true,
            Passed = false
        };
        Assert(passRow.ChannelText == "CH1 • CN-A" && passRow.ResultText == "PASS" &&
               failRow.ChannelText == "CH3 • CN-C" && failRow.ResultText == "FAIL",
            "Leak result rows identify both machine channel and mapped THT connector");

        passRow.PressPressure = 83.9;
        passRow.WaitPressure = 81.8;
        passRow.FirstResultPressure = 83.9;
        passRow.SecondResultPressure = 81.8;
        Assert(passRow.PressureText == "---",
            "Leak summary card does not display fill/hold pressure before a drop result exists");
        passRow.Leak = 2.1;
        Assert(passRow.PressureText == "2.1" && passRow.LeakText == "2.1",
            "Leak summary card displays machine-reported pressure drop instead of fill/hold pressure");

        string xaml = File.ReadAllText(
    Path.Combine(Environment.CurrentDirectory, "Views", "TestWindow.xaml"));

        Assert(
            xaml.Contains("Text=\"ĐỘ RÒ RỈ\"", StringComparison.Ordinal) &&
            xaml.Contains("Text=\"{Binding LeakText}\"", StringComparison.Ordinal) &&
            xaml.Contains("Text=\" kPa\"", StringComparison.Ordinal),
            "Leak summary card displays realtime leak value in kPa");

        // Regression: :PRESS lưu áp cuối làm baseline, từng :WAIT phải cập nhật
        // Leak ngay trên UI nhưng tuyệt đối chưa được chốt PASS/FAIL trước :RESULT.
        TestViewModel realtimeLeakVm = CreateTestViewModel(
            new ProductionSettings { MasterFaultRequiredCount = 0 });

        FieldInfo realtimeProfileField = typeof(TestViewModel).GetField(
            "_waterProofProfile",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak realtime profile field not found");

        MethodInfo resetRealtimeLeak = typeof(TestViewModel).GetMethod(
            "ResetWaterProofDisplay",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak realtime reset method not found");

        MethodInfo applyRealtimeLeak = typeof(TestViewModel).GetMethod(
            "ApplyWaterProofProgress",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak realtime progress method not found");

        realtimeProfileField.SetValue(
            realtimeLeakVm,
            new WaterProofModelSettings
            {
                Enabled = true,
                Channel1Enabled = true,
                Channel2Enabled = true,
                Channel3Enabled = true,
                LeakLimit = 2.0
            });

        resetRealtimeLeak.Invoke(realtimeLeakVm, null);

        Assert(
            realtimeLeakVm.WaterProofChannels.Count == 3 &&
            realtimeLeakVm.WaterProofChannels.All(row => !row.IsMeasured),
            "Leak realtime reset creates three enabled channels without marking a result");

        applyRealtimeLeak.Invoke(
            realtimeLeakVm,
            [
                new WaterProofProgress(
                    WaterProofStage.Pressurizing,
                    new double[] { 84.1, 84.2, 84.0 },
                    ":PRESS,84.1,84.2,84.0")
            ]);

        Assert(
            Math.Abs(realtimeLeakVm.WaterProofChannels[0].PressPressure.GetValueOrDefault() - 84.1) < 0.0001 &&
            Math.Abs(realtimeLeakVm.WaterProofChannels[1].PressPressure.GetValueOrDefault() - 84.2) < 0.0001 &&
            Math.Abs(realtimeLeakVm.WaterProofChannels[2].PressPressure.GetValueOrDefault() - 84.0) < 0.0001 &&
            realtimeLeakVm.WaterProofChannels[0].FirstPressureText == "84.1" &&
            realtimeLeakVm.WaterProofChannels[0].SecondPressureText == "---" &&
            realtimeLeakVm.WaterProofChannels.All(row => Math.Abs(row.Leak.GetValueOrDefault()) < 0.0001) &&
            realtimeLeakVm.WaterProofChannels.All(row => !row.IsMeasured),
            "PRESS updates live pressure/table baseline, starts Leak at zero, and cannot mark PASS/FAIL");

        applyRealtimeLeak.Invoke(
            realtimeLeakVm,
            [
                new WaterProofProgress(
                    WaterProofStage.Waiting,
                    new double[] { 84.0, 84.1, 83.9 },
                    ":WAIT,84.0,84.1,83.9")
            ]);

        Assert(
            Math.Abs(realtimeLeakVm.WaterProofChannels[0].Leak.GetValueOrDefault() - 0.1) < 0.0001 &&
            Math.Abs(realtimeLeakVm.WaterProofChannels[1].Leak.GetValueOrDefault() - 0.1) < 0.0001 &&
            Math.Abs(realtimeLeakVm.WaterProofChannels[2].Leak.GetValueOrDefault() - 0.1) < 0.0001 &&
            realtimeLeakVm.WaterProofChannels[0].SecondPressureText == "84.0" &&
            realtimeLeakVm.WaterProofStageText == "ĐANG ĐO ĐỘ RÒ",
            "First WAIT frame updates displayed hold pressure and Leak immediately for all channels");

        applyRealtimeLeak.Invoke(
            realtimeLeakVm,
            [
                new WaterProofProgress(
                    WaterProofStage.Waiting,
                    new double[] { 83.5, 83.7, 83.4 },
                    ":WAIT,83.5,83.7,83.4")
            ]);

        Assert(
            Math.Abs(realtimeLeakVm.WaterProofChannels[0].Leak.GetValueOrDefault() - 0.6) < 0.0001 &&
            Math.Abs(realtimeLeakVm.WaterProofChannels[1].Leak.GetValueOrDefault() - 0.5) < 0.0001 &&
            Math.Abs(realtimeLeakVm.WaterProofChannels[2].Leak.GetValueOrDefault() - 0.6) < 0.0001,
            "Later WAIT frames continuously replace the realtime Leak values");

        Assert(
            realtimeLeakVm.WaterProofChannels.All(row => !row.IsMeasured) &&
            realtimeLeakVm.WaterProofChannels.All(row => row.ResultText == "---"),
            "Realtime PRESS/WAIT display never publishes PASS/FAIL before the final RESULT");

        MethodInfo showLeakPanel = typeof(TestViewModel).GetMethod(
            "ShowWaterProofOperationPanel",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak operation panel method not found");
        realtimeLeakVm.SelectedOperationTabIndex = 0;
        showLeakPanel.Invoke(realtimeLeakVm, null);
        Assert(realtimeLeakVm.SelectedOperationTabIndex == 3,
            "Leak run switches to its detailed realtime table before RESULT");
        int resultStyleUses = xaml.Split(
            "CellStyle=\"{StaticResource PassFailResultCellStyle}\"",
            StringSplitOptions.None).Length - 1;
        Assert(resultStyleUses == 2,
            "Resistance and Leak result columns must share green PASS/red FAIL cell presentation");
        Assert(xaml.Contains("<Viewbox Margin=\"10\"", StringComparison.Ordinal) &&
               xaml.Contains("StretchDirection=\"DownOnly\"", StringComparison.Ordinal),
            "Large result text scales down to keep ĐẠT/KHÔNG ĐẠT/SẴN SÀNG inside its box");

        ProductModel connectorModel = HtdrvTwoEndpointModel();
        using TestEngine connectorEngine = CreateEngine(out _);
        connectorEngine.SetModel(connectorModel);
        Assert(!connectorEngine.IsConnectorConnected("1"),
            "Leak connector gate remains closed before the selected connector is fitted");
        connectorEngine.ProcessFrame(FrameSeq(1, (1, new[] { 2 })));
        Assert(connectorEngine.IsConnectorConnected("1") &&
               !connectorEngine.IsConnectorConnected("2") &&
               !connectorEngine.IsConnectorConnected(string.Empty),
            "Leak connector gate opens only for the exact connected THT connector");

        TestViewModel removalVm = CreateTestViewModel(
            new ProductionSettings { MasterFaultRequiredCount = 0 },
            out FakeBoard removalBoard);
        removalVm.LoadPreparedModelAsync(Model(("LEAK-PAIR", new[] { 1, 18 })))
            .GetAwaiter()
            .GetResult();
        typeof(TestViewModel).GetField("_runtimeMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(removalVm, 1);
        TestEngine removalEngine =
            (TestEngine)(typeof(TestViewModel).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(removalVm) ?? throw new InvalidOperationException("Leak removal TestEngine not found"));
        removalEngine.SetFrameProcessingEnabled(true);
        removalVm.SelectedOperationTabIndex = 3;

        MethodInfo armRemoval = typeof(TestViewModel).GetMethod(
            "ArmWaterProofFaultRemovalWait",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak removal arm method not found");
        armRemoval.Invoke(removalVm, null);

        FieldInfo waitForFaultRemoval = typeof(TestViewModel).GetField(
            "_waitForFaultProductRemoval",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak removal wait flag not found");
        Assert(removalVm.SelectedOperationTabIndex == 3 &&
               (bool)(waitForFaultRemoval.GetValue(removalVm) ?? false),
            "Leak FAIL keeps the result page visible and arms ProductRemoved confirmation");

        removalBoard.Publish(FrameSeq(1, (1, new[] { 18 })));
        Assert((bool)(waitForFaultRemoval.GetValue(removalVm) ?? false) &&
               removalVm.SelectedOperationTabIndex == 3,
            "Leak FAIL must keep its result table while any product IO remains connected");
        removalBoard.Publish(FrameSeq(2));
        Assert(!(bool)(waitForFaultRemoval.GetValue(removalVm) ?? true) &&
               removalVm.ResultStatusText == "SẴN SÀNG" &&
               removalVm.SelectedOperationTabIndex == 0,
            "Fresh empty frame after Leak FAIL resets the cycle instead of hanging on results");

        TestViewModel faultMainVm = CreateTestViewModel(
            new ProductionSettings { MasterFaultRequiredCount = 0 },
            out FakeBoard faultMainBoard);
        faultMainVm.LoadPreparedModelAsync(Model(("FAIL-PAIR", new[] { 1, 18 })))
            .GetAwaiter()
            .GetResult();
        typeof(TestViewModel).GetField("_runtimeMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(faultMainVm, 1);
        TestEngine faultMainEngine =
            (TestEngine)(typeof(TestViewModel).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(faultMainVm) ?? throw new InvalidOperationException("FAIL MainWindow TestEngine not found"));
        faultMainEngine.SetFrameProcessingEnabled(true);
        armRemoval.Invoke(faultMainVm, null);
        faultMainBoard.Publish(FrameSeq(20, (1, new[] { 18 })));
        faultMainVm.StopViewAsync().GetAwaiter().GetResult();
        Assert(faultMainVm.IsProductRemovalPending &&
               faultMainVm.State == "VUI LÒNG THÁO SẢN PHẨM",
            "Returning to MainWindow during FAIL removal preserves the shared removal lock and warning");
        faultMainBoard.Publish(FrameSeq(21, (1, new[] { 18 })));
        Assert(faultMainVm.IsProductRemovalPending,
            "FAIL MainWindow removal lock remains while any product connection is present");
        faultMainBoard.Publish(FrameSeq(22));
        Assert(!faultMainVm.IsProductRemovalPending &&
               faultMainVm.ResultStatusText == "SẴN SÀNG",
            "FAIL MainWindow removal lock clears only after a complete empty frame");

        FieldInfo cycleActiveAfterLeakFail = typeof(TestViewModel).GetField(
            "_cycleActive",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak cycle-active flag not found");
        FieldInfo postContinuityAfterLeakFail = typeof(TestViewModel).GetField(
            "_postContinuityStarted",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak post-continuity flag not found");
        FieldInfo waterProofRunningAfterLeakFail = typeof(TestViewModel).GetField(
            "_waterProofRunning",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak running flag not found");
        Assert((bool)(cycleActiveAfterLeakFail.GetValue(removalVm) ?? false) &&
               (int)(postContinuityAfterLeakFail.GetValue(removalVm) ?? -1) == 0 &&
               (int)(waterProofRunningAfterLeakFail.GetValue(removalVm) ?? -1) == 0,
            "Leak FAIL removal fully re-arms cycle 2 and releases both automatic-test locks");

        removalVm.SelectedOperationTabIndex = 3;
        MethodInfo armPassRemoval = typeof(TestViewModel).GetMethod(
            "ArmPassProductRemovalWait",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PASS removal arm method not found");
        armPassRemoval.Invoke(removalVm, null);
        FieldInfo waitForPassRemoval = typeof(TestViewModel).GetField(
            "_waitForProductRelease",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PASS removal wait flag not found");
        Assert((bool)(waitForPassRemoval.GetValue(removalVm) ?? false) &&
               removalVm.IsProductRemovalPending &&
               removalVm.SelectedOperationTabIndex == 3 &&
               removalVm.ResultStatusText == "THÁO SẢN PHẨM" &&
               removalVm.StateBackground == "#2AA84A",
            "Committed Leak PASS keeps the result table, stays green, and explicitly requests ProductRemoved before scan restart");
        removalVm.StopViewAsync().GetAwaiter().GetResult();
        Assert(removalVm.IsProductRemovalPending &&
               removalVm.State.Contains("VUI LÒNG THÁO SẢN PHẨM", StringComparison.Ordinal),
            "Returning to MainWindow preserves the committed PASS removal lock and background IO monitoring");
        removalBoard.Publish(FrameSeq(3, (1, new[] { 18 })));
        Assert((bool)(waitForPassRemoval.GetValue(removalVm) ?? false) &&
               removalVm.IsProductRemovalPending &&
               removalVm.SelectedOperationTabIndex == 3 &&
               removalVm.ResultStatusText == "VUI LÒNG THÁO SẢN PHẨM",
            "Leak PASS result table remains visible while any product IO is still connected");
        removalBoard.Publish(FrameSeq(4));
        FieldInfo cycleActiveAfterMainRemoval = typeof(TestViewModel).GetField(
            "_cycleActive",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PASS main-screen cycle-active flag not found");
        Assert(!(bool)(waitForPassRemoval.GetValue(removalVm) ?? true) &&
               !removalVm.IsProductRemovalPending &&
               !(bool)(cycleActiveAfterMainRemoval.GetValue(removalVm) ?? true) &&
               removalVm.ResultStatusText == "SẴN SÀNG" &&
               removalVm.SelectedOperationTabIndex == 0,
            "After committed Leak PASS, a fresh empty frame clears the MainWindow lock without auto-arming a new test");

        TestViewModel pauseVm = CreateTestViewModel(
            new ProductionSettings { MasterFaultRequiredCount = 0 },
            out FakeBoard pauseBoard);
        MethodInfo pauseD2xx = typeof(TestViewModel).GetMethod(
            "PauseProductionScanForWaterProofAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Leak D2XX pause method not found");
        ((Task)(pauseD2xx.Invoke(pauseVm, [CancellationToken.None])
            ?? throw new InvalidOperationException("Leak D2XX pause task not returned")))
            .GetAwaiter()
            .GetResult();
        Assert(!pauseBoard.IsScanning && pauseBoard.Commands.Contains("STOP"),
            "Leak stage pauses D2XX scan so pressure activity cannot invalidate continuity PASS");

        TestViewModel finalPassPauseVm = CreateTestViewModel(
            new ProductionSettings { MasterFaultRequiredCount = 0 },
            out FakeBoard finalPassPauseBoard);
        MethodInfo pauseFinalPass = typeof(TestViewModel).GetMethod(
            "PauseProductionScanForFinalPassAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Final PASS D2XX pause method not found");
        ((Task)(pauseFinalPass.Invoke(finalPassPauseVm, [CancellationToken.None])
            ?? throw new InvalidOperationException("Final PASS D2XX pause task not returned")))
            .GetAwaiter()
            .GetResult();
        Assert(!finalPassPauseBoard.IsScanning && finalPassPauseBoard.Commands.Contains("STOP"),
            "Every final PASS path freezes D2XX continuity before relay execution, even when Leak is disabled");

        MethodInfo shouldRestart = typeof(TestViewModel).GetMethod(
            "ShouldRestartAfterPass",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PASS restart policy method not found");
        Assert((bool)(shouldRestart.Invoke(null, [false, true]) ?? false) &&
               !(bool)(shouldRestart.Invoke(null, [false, false]) ?? true),
            "Completed Leak always enters removal/reset lifecycle even when legacy auto-restart is disabled");

        var idleLeakService = new WaterProofSerialService();
        Stopwatch disposeWatch = Stopwatch.StartNew();
        idleLeakService.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Assert(disposeWatch.ElapsedMilliseconds < 1_000,
            "Idle Leak COM disposal is bounded and cannot hang application shutdown");

        string leakServiceSource = File.ReadAllText(Path.Combine(
            Environment.CurrentDirectory,
            "Services",
            "WaterProofSerialService.cs"));
        Assert(
            leakServiceSource.Contains(
                "ReleaseRunPort(runNumber, port)",
                StringComparison.Ordinal) &&
            leakServiceSource.Contains(
                "WaitForPendingCloseBestEffortAsync",
                StringComparison.Ordinal) &&
            leakServiceSource.Contains(
                "next run will reconnect cleanly",
                StringComparison.Ordinal),
            "A completed Leak result releases only its owned COM session and waits bounded cleanup before cycle 2");
    }

    private static void TestProductionScanTokenSurvivesCycleCancel()
    {
        var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
        TestViewModel vm = CreateTestViewModel(settings, out FakeBoard board);
        vm.LoadPreparedModelAsync(Model(("PAIR", new[] { 1, 18 }))).GetAwaiter().GetResult();

        board.StopScanAsync().GetAwaiter().GetResult();
        int commandsBeforeArm = board.Commands.Count;
        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        Assert(board.Commands.Count == commandsBeforeArm && !board.IsScanning,
            "START does not reconnect, initialize, or start hardware when background scan is unavailable");

        board.StartScanAsync(BoardScanMode.Production, CancellationToken.None).GetAwaiter().GetResult();
        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        Assert(board.LastStartScanToken.HasValue, "Background lifecycle owns the production START_SCAN token");
        CancellationToken scanToken = board.LastStartScanToken.GetValueOrDefault();

        MethodInfo cancelCycle = typeof(TestViewModel).GetMethod(
            "CancelCycleOperations",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CancelCycleOperations method not found.");
        cancelCycle.Invoke(vm, []);

        Assert(board.IsScanning, "Canceling ProductCycleToken does not stop the background D2XX scan session");
        Assert(!scanToken.IsCancellationRequested, "START_SCAN token is independent from ProductCycleToken");
    }

    private static void TestProbeTargetOnlyTouchDetection()
    {
        ProductModel model = Model(("PAIR", new[] { 1, 18 }), ("PROBE-PIN", new[] { 113, 114 }));
        var frame = new ScanFrame(
            DateTime.Now,
            1,
            new HashSet<int> { 113 },
            [],
            false,
            0,
            7,
            new Dictionary<int, IReadOnlySet<int>>(),
            new Dictionary<int, int> { [113] = 1 },
            BoardScanMode.Production);

        IReadOnlyList<ProbeContactClassifier.Detection> detections =
            ProbeContactClassifier.DetectMany(frame, model, maxContacts: 2, boardCapacity: BoardCapacity.Create(10));

        Assert(detections.Count == 1 && detections[0].Io == 113,
            "Target-only production probe touch appears on TestWindow instead of being ignored");
    }

    private static void AssertWireColorCells(string code, string one, string two, string three, string four)
    {
        var row = new FaultRow { Color = code };
        Assert(row.WireColorText == code, $"Wire color text preserved for '{code}'");
        Assert(BrushHex(row.Color1Brush) == one, $"Color #1 for '{code}'");
        Assert(BrushHex(row.Color2Brush) == two, $"Color #2 for '{code}'");
        Assert(BrushHex(row.Color3Brush) == three, $"Color #3 for '{code}'");
        Assert(BrushHex(row.Color4Brush) == four, $"Color #4 for '{code}'");
    }

    private static void TestLegacyDatabaseWithoutSchemaInfo()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "JBZLegacyDatabaseWithoutSchemaInfo",
            Guid.NewGuid().ToString("N"));
        string dbPath = Path.Combine(root, "JBZUniversalTester.db");
        string backupPath = dbPath + $".pre-schema-v{TestHistoryStore.CurrentSchemaVersion}.backup";
        try
        {
            Directory.CreateDirectory(root);
            using (var legacy = new SqliteConnection($"Data Source={dbPath}"))
            {
                legacy.Open();
                using SqliteCommand create = legacy.CreateCommand();
                create.CommandText = "CREATE TABLE LegacyMarker(Value TEXT NOT NULL); " +
                                     "INSERT INTO LegacyMarker(Value) VALUES ('KEEP');";
                create.ExecuteNonQuery();
            }

            var store = new TestHistoryStore(dbPath);
            Assert(store.SchemaVersion == TestHistoryStore.CurrentSchemaVersion,
                "Legacy database initializes the current relational schema");
            Assert(File.Exists(backupPath),
                "Legacy database is backed up before schema migration");

            using var verify = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            verify.Open();
            using SqliteCommand probe = verify.CreateCommand();
            probe.CommandText = "SELECT " +
                                "(SELECT COUNT(*) FROM SchemaInfo), " +
                                "(SELECT COUNT(*) FROM LegacyMarker WHERE Value='KEEP');";
            using SqliteDataReader reader = probe.ExecuteReader();
            Assert(reader.Read() && reader.GetInt32(0) == 1 && reader.GetInt32(1) == 1,
                "Schema initialization preserves tables from a database without SchemaInfo");
            reader.Close();
            verify.Close();

            string v3Path = Path.Combine(root, "schema-v3.db");
            var v3Store = new TestHistoryStore(v3Path);
            v3Store.Add(new TestHistoryRecord
            {
                Started = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Local),
                Finished = new DateTime(2026, 9, 1, 8, 0, 1, DateTimeKind.Local),
                PartNumber = "V3-PART",
                ModelName = "V3-MODEL",
                ModelFile = "V3.tht",
                Result = "PASS",
                Passed = true,
                CycleId = "v3-existing-cycle"
            });
            SqliteConnection.ClearAllPools();
            using (var downgrade = new SqliteConnection($"Data Source={v3Path};Pooling=False"))
            {
                downgrade.Open();
                using SqliteCommand command = downgrade.CreateCommand();
                command.CommandText = """
                    DROP TABLE ActiveTestCycles;
                    UPDATE SchemaInfo SET SchemaVersion=3 WHERE Id=1;
                    """;
                command.ExecuteNonQuery();
            }

            var migratedV3 = new TestHistoryStore(v3Path);
            Assert(File.Exists(v3Path + $".pre-schema-v{TestHistoryStore.CurrentSchemaVersion}.backup"),
                "Schema v3 database is backed up before v4 migration");
            using var migratedVerify = new SqliteConnection($"Data Source={v3Path};Pooling=False");
            migratedVerify.Open();
            using SqliteCommand migratedProbe = migratedVerify.CreateCommand();
            migratedProbe.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ActiveTestCycles'),
                    (SELECT COUNT(*) FROM Tests WHERE CycleId='v3-existing-cycle');
                """;
            using SqliteDataReader migratedReader = migratedProbe.ExecuteReader();
            Assert(migratedReader.Read() && migratedReader.GetInt32(0) == 1 &&
                   migratedReader.GetInt32(1) == 1 &&
                   migratedV3.SchemaVersion == TestHistoryStore.CurrentSchemaVersion,
                "Schema v3 migration is idempotent and preserves existing test rows");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TestHistoryInitializationWaitsForWriter()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "JBZHistoryInitializationLockTests",
            Guid.NewGuid().ToString("N"));
        string dbPath = Path.Combine(root, "JBZUniversalTester.db");
        try
        {
            Directory.CreateDirectory(root);
            _ = new TestHistoryStore(dbPath);

            using var writerStarted = new ManualResetEventSlim(false);
            Task writer = Task.Run(() =>
            {
                using var connection = new SqliteConnection(
                    $"Data Source={dbPath};Cache=Shared;Pooling=False;Default Timeout=5");
                connection.Open();
                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE SchemaInfo SET UpdatedAt=UpdatedAt WHERE Id=1;";
                command.ExecuteNonQuery();
                writerStarted.Set();
                Thread.Sleep(250);
                transaction.Commit();
            });

            Assert(writerStarted.Wait(TimeSpan.FromSeconds(5)),
                "Concurrent SQLite writer entered its transaction");
            var reopened = new TestHistoryStore(dbPath);
            writer.GetAwaiter().GetResult();
            Assert(reopened.SchemaVersion == TestHistoryStore.CurrentSchemaVersion,
                "History store opens after the active writer commits instead of throwing SQLITE_LOCKED");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TestHistoryInterruptedTransactionRecovery()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "JBZHistoryInterruptedTransactionTests",
            Guid.NewGuid().ToString("N"));
        string dbPath = Path.Combine(root, "JBZUniversalTester.db");
        try
        {
            Directory.CreateDirectory(root);
            _ = new TestHistoryStore(dbPath);

            using (var connection = new SqliteConnection(
                       $"Data Source={dbPath};Pooling=False;Default Timeout=2"))
            {
                connection.Open();
                using (SqliteCommand seed = connection.CreateCommand())
                {
                    seed.CommandText =
                        "CREATE TABLE IF NOT EXISTS PowerLossProbe(Id INTEGER PRIMARY KEY, Value TEXT NOT NULL);" +
                        "INSERT OR REPLACE INTO PowerLossProbe(Id,Value) VALUES(1,'COMMITTED');";
                    seed.ExecuteNonQuery();
                }

                using SqliteTransaction interrupted = connection.BeginTransaction();
                using SqliteCommand update = connection.CreateCommand();
                update.Transaction = interrupted;
                update.CommandText = "UPDATE PowerLossProbe SET Value='UNCOMMITTED' WHERE Id=1;";
                update.ExecuteNonQuery();
                // Không Commit: mô phỏng transaction bị ngắt giữa chừng ở mức
                // ứng dụng. Dispose phải rollback phần chưa durable.
            }

            var reopened = new TestHistoryStore(dbPath);
            using var verify = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            verify.Open();
            using SqliteCommand command = verify.CreateCommand();
            command.CommandText =
                "SELECT Value, (SELECT quick_check FROM pragma_quick_check LIMIT 1) " +
                "FROM PowerLossProbe WHERE Id=1;";
            using SqliteDataReader reader = command.ExecuteReader();
            Assert(reader.Read() && reader.GetString(0) == "COMMITTED" && reader.GetString(1) == "ok",
                "WAL reopens cleanly and discards only the interrupted transaction");
            Assert(reopened.SchemaVersion == TestHistoryStore.CurrentSchemaVersion,
                "Recovered production database keeps the current schema");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TestProductionPersistenceRetriesTransientLock()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "JBZProductionPersistenceRetryTests",
            Guid.NewGuid().ToString("N"));
        string dbPath = Path.Combine(root, "JBZUniversalTester.db");
        ProductionPersistenceService? persistence = null;
        try
        {
            Directory.CreateDirectory(root);
            var repository = new TestHistoryStore(dbPath);
            persistence = new ProductionPersistenceService(
                repository,
                new ProductionSettings(),
                "SELF-TEST");
            persistence.Initialization.GetAwaiter().GetResult();

            using var writerStarted = new ManualResetEventSlim(false);
            Task writer = Task.Run(() =>
            {
                using var connection = new SqliteConnection(
                    $"Data Source={dbPath};Pooling=False;Default Timeout=2");
                connection.Open();
                using SqliteTransaction transaction = connection.BeginTransaction();
                using SqliteCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE SchemaInfo SET UpdatedAt=UpdatedAt WHERE Id=1;";
                command.ExecuteNonQuery();
                writerStarted.Set();
                Thread.Sleep(2150);
                transaction.Commit();
            });

            Assert(writerStarted.Wait(TimeSpan.FromSeconds(5)),
                "Transient SQLite writer acquired the database lock");
            var part = new PartIdentitySnapshot(
                "PN:LOCK-RETRY",
                "LOCK-RETRY",
                "", "", "", "", "", "");
            ProbeCounterSnapshot counter = persistence.IncrementProbeCounterAsync(part, 200000)
                .GetAwaiter().GetResult();
            writer.GetAwaiter().GetResult();
            Assert(counter.Counter == 1 && string.IsNullOrEmpty(persistence.LastDatabaseError),
                "Serialized writer retries BUSY and commits once after the transient lock clears");
        }
        finally
        {
            if (persistence is not null)
                persistence.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TestCanonicalRuntimePersistence()
    {
        Assert(Path.GetFileName(RuntimePaths.ConfigFile) == "JBZUniversalTester.cfg",
            "Canonical config filename");
        Assert(Path.GetFileName(RuntimePaths.PartCounterFile) == "PartCnt.txt",
            "Canonical PartCnt filename");
        Assert(Path.GetFileName(RuntimePaths.LogFile) == "JBZUniversalTester.log",
            "Canonical log filename");
        Assert(Path.GetFileName(RuntimePaths.DatabaseFile) == "JBZUniversalTester.db" &&
               string.Equals(Path.GetFileName(Path.GetDirectoryName(RuntimePaths.DatabaseFile)), "Data", StringComparison.OrdinalIgnoreCase),
            "Canonical database path");
        Assert(Path.GetFileName(RuntimePaths.CrashReportFile) == "JBZUniversalTester.RPT" &&
               string.Equals(Path.GetFileName(Path.GetDirectoryName(RuntimePaths.CrashReportFile)), "Crash", StringComparison.OrdinalIgnoreCase),
            "Canonical lazy crash-report path");
        Assert(RuntimePaths.PassRoot == @"C:\Pass" && RuntimePaths.ErrorRoot == @"C:\Error" &&
               RuntimePaths.ItemDirectory == @"C:\ITEM",
            "Canonical external ITEM/Pass/Error roots");

        bool crashDirectoryExisted = Directory.Exists(RuntimePaths.CrashDirectory);
        bool crashReportExisted = File.Exists(RuntimePaths.CrashReportFile);
        if (!crashReportExisted)
        {
            CrashReportService.Write(
                new InvalidOperationException("SIMULATED_CRASH_REPORT_TEST"),
                "SELF-TEST",
                "Model=TEST; Cycle=TEST-CYCLE");
            string report = File.ReadAllText(RuntimePaths.CrashReportFile, Encoding.UTF8);
            Assert(report.Contains("SIMULATED_CRASH_REPORT_TEST", StringComparison.Ordinal) &&
                   report.Contains("Model=TEST; Cycle=TEST-CYCLE", StringComparison.Ordinal) &&
                   report.Contains("AppVersion:", StringComparison.Ordinal),
                "Crash RPT is created lazily with exception and runtime context");
            File.Delete(RuntimePaths.CrashReportFile);
            if (!crashDirectoryExisted && Directory.Exists(RuntimePaths.CrashDirectory) &&
                !Directory.EnumerateFileSystemEntries(RuntimePaths.CrashDirectory).Any())
            {
                Directory.Delete(RuntimePaths.CrashDirectory);
            }
        }

        string root = Path.Combine(Path.GetTempPath(), "JBZCanonicalPersistenceTests", Guid.NewGuid().ToString("N"));
        string dbPath = Path.Combine(root, "Data", "JBZUniversalTester.db");
        string counterPath = Path.Combine(root, "PartCnt.txt");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(counterPath, "PART-A 50000 42\r\n", new UTF8Encoding(false));
            var repository = new TestHistoryStore(dbPath);
            var mirror = new PartCounterStore(counterPath);
            Assert(repository.ImportPartCountersOnce(mirror.ReadAll(), counterPath) == 1,
                "Existing PartCnt is imported only as initial migration input");
            ProbeCounterSnapshot imported = repository.GetAllProbeCounters().Single();
            Assert(imported.PartNumber == "PART-A" && imported.Counter == 42 && imported.ReplacementThreshold == 50000,
                "SQLite receives initial PartCnt counter and threshold");

            File.WriteAllText(counterPath, "PART-A 50000 999\r\n", new UTF8Encoding(false));
            Assert(repository.ImportPartCountersOnce(mirror.ReadAll(), counterPath) == 0 &&
                   repository.GetAllProbeCounters().Single().Counter == 42,
                "Later PartCnt edits cannot overwrite authoritative SQLite state");

            File.Delete(counterPath);
            mirror.MirrorAll(repository.GetAllProbeCounters());
            Assert(File.ReadAllText(counterPath, Encoding.UTF8) == "PART-A 50000 42\r\n",
                "Missing PartCnt mirror is rebuilt from SQLite");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void TestSystemLogMasterSwitch()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "JBZSystemLogSwitchTests",
            Guid.NewGuid().ToString("N"));
        string logsRoot = Path.Combine(root, "Logs");

        try
        {
            var logger = new AsyncFileLogService();
            try
            {
                logger.Configure(enabled: false, rootDirectory: logsRoot);
                logger.Application("NORMAL_LIFECYCLE_RECORD");
                Assert(!logger.FileLoggingEnabled && logger.Level == AppLogLevel.Normal,
                    "Disabled system logging blocks the canonical runtime log");
                Assert(!Directory.Exists(logsRoot),
                    "Disabled system logging does not create a log directory");

                logger.Configure(enabled: true);
                Assert(logger.FileLoggingEnabled && logger.Level == AppLogLevel.ProtocolTrace,
                    "Enabled system logging includes protocol trace level");
                logger.Board("ENABLED_PROTOCOL_TRACE", AppLogLevel.ProtocolTrace);

                logger.Configure(enabled: false);
                logger.Error("NORMAL_ERROR_AFTER_DIAGNOSTIC_DISABLE");
                Assert(!logger.FileLoggingEnabled,
                    "Disabling system logging takes effect without restarting the application");
            }
            finally
            {
                logger.Dispose();
            }

            string[] logFiles = Directory.EnumerateFiles(logsRoot, "*.log").ToArray();
            Assert(logFiles.Length == 1 && Path.GetFileName(logFiles[0]) == "JBZUniversalTester.log",
                "Runtime writes one canonical main log instead of category/day files");
            string logText = File.ReadAllText(logFiles[0]);
            Assert(!logText.Contains("NORMAL_LIFECYCLE_RECORD", StringComparison.Ordinal) &&
                   logText.Contains("ENABLED_PROTOCOL_TRACE", StringComparison.Ordinal) &&
                   !logText.Contains("NORMAL_ERROR_AFTER_DIAGNOSTIC_DISABLE", StringComparison.Ordinal),
                "Main log contains only records written while system logging is enabled");

            string historyPath = Path.Combine(root, "History", "test-history.db");
            var history = new TestHistoryStore(historyPath);
            DateTime now = DateTime.Now;
            history.Add(new TestHistoryRecord
            {
                Started = now,
                Finished = now,
                PartNumber = "LOG-OFF-HISTORY",
                Result = "PASS",
                Passed = true
            });
            Assert(File.Exists(historyPath) && history.Search(
                    new HistorySearchCriteria(null, null, null, "LOG-OFF-HISTORY", "ALL", 10)).Count == 1,
                "History remains writable while the system log master switch is off");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertBalancedTwoColorBrush(string code, string first, string second)
    {
        Assert(WireColorToBrushConverter.ToBrush(code) is LinearGradientBrush brush &&
               brush.GradientStops.Count == 4 &&
               brush.GradientStops[0].Offset == 0.0 &&
               brush.GradientStops[1].Offset == 0.5 &&
               brush.GradientStops[2].Offset == 0.5 &&
               brush.GradientStops[3].Offset == 1.0 &&
               $"#{brush.GradientStops[0].Color.R:X2}{brush.GradientStops[0].Color.G:X2}{brush.GradientStops[0].Color.B:X2}" == first &&
               $"#{brush.GradientStops[3].Color.R:X2}{brush.GradientStops[3].Color.G:X2}{brush.GradientStops[3].Color.B:X2}" == second,
            $"Two-color wire '{code}' keeps THT order and uses an exact 50/50 split");
    }

    private static string BrushHex(Brush brush)
    {
        if (brush is not SolidColorBrush solid)
            return "<non-solid>";

        Color color = solid.Color;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void TestBoardCapacity()
    {
        var defaultSettings = new ProductionSettings();
        Assert(
            defaultSettings.ExpansionCardCount == 2 &&
            defaultSettings.CardCount == 2 &&
            BoardCapacity.FromSettings(defaultSettings).TotalIoCapacity == 128,
            "A new station defaults to two expansion cards / 128 IO");

        for (int cardCount = 1; cardCount <= 10; cardCount++)
        {
            BoardCapacity everyCapacity = BoardCapacity.Create(cardCount);
            Assert(everyCapacity.ExpansionCardCount == cardCount &&
                   everyCapacity.TotalIoCapacity == cardCount * 64 &&
                   everyCapacity.StartScanParameter == cardCount,
                $"Every selectable card count {cardCount} maps to {cardCount * 64} IO and START_SCAN={cardCount}");
        }

        AssertCapacity(1, 2, 1, 64);
        AssertCapacity(2, 4, 2, 128);
        AssertCapacity(3, 6, 3, 192);
        AssertCapacity(4, 8, 4, 256);
        AssertCapacity(5, 10, 5, 320);
        AssertCapacity(10, 20, 10, 640);

        BoardCapacity capacity = BoardCapacity.Create(10);
        var mapper = new BoardAddressMapper(capacity);
        AssertAddress(mapper, 1, 1, 1, 1);
        AssertAddress(mapper, 32, 1, 1, 32);
        AssertAddress(mapper, 33, 1, 2, 1);
        AssertAddress(mapper, 64, 1, 2, 32);
        AssertAddress(mapper, 65, 2, 1, 1);
        AssertAddress(mapper, 96, 2, 1, 32);
        AssertAddress(mapper, 97, 2, 2, 1);
        AssertAddress(mapper, 128, 2, 2, 32);
        AssertAddress(mapper, 129, 3, 1, 1);
        AssertAddress(mapper, 160, 3, 1, 32);
        AssertAddress(mapper, 161, 3, 2, 1);
        AssertAddress(mapper, 192, 3, 2, 32);
        AssertAddress(mapper, 193, 4, 1, 1);
        AssertAddress(mapper, 224, 4, 1, 32);
        AssertAddress(mapper, 225, 4, 2, 1);
        AssertAddress(mapper, 256, 4, 2, 32);
        AssertAddress(mapper, 577, 10, 1, 1);
        AssertAddress(mapper, 608, 10, 1, 32);
        AssertAddress(mapper, 609, 10, 2, 1);
        AssertAddress(mapper, 640, 10, 2, 32);
        Assert(capacity.ContainsGlobalIo(640), "IO640 accepted");
        Assert(!capacity.ContainsGlobalIo(641), "IO641 rejected");

        (int MaxIo, int RequiredCards, long RequiredIo)[] requiredCases =
        [
            (1, 1, 64),
            (64, 1, 64),
            (65, 2, 128),
            (128, 2, 128),
            (129, 3, 192),
            (192, 3, 192),
            (193, 4, 256),
            (201, 4, 256),
            (256, 4, 256),
            (257, 5, 320),
            (640, 10, 640)
        ];
        foreach ((int maxIo, int requiredCards, long requiredIo) in requiredCases)
        {
            BoardScanCapacity requiredCapacity = BoardScanCapacity.Create(
                new ProductionSettings { ExpansionCardCount = 10 },
                maxIo);
            Assert(requiredCapacity.RequiredScanUnits == requiredCards &&
                   requiredCapacity.RequiredIoCapacity == requiredIo,
                $"MaxIO {maxIo} requires {requiredCards} card / {requiredIo} IO");
        }

        BoardScanCapacity insufficient = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 3 },
            201);
        Assert(!insufficient.IsModelWithinInstalledCapacity &&
               insufficient.Installed.TotalIoCapacity == 192 &&
               insufficient.RequiredScanUnits == 4 &&
               insufficient.RequiredIoCapacity == 256 &&
               insufficient.CapacityErrorMessage.Contains("192 / 256 IO", StringComparison.Ordinal),
            "Configured 3 / MaxIO 201 blocks Production and warns 192 / 256 IO");

        BoardScanCapacity configured4 = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 4 },
            201);
        Assert(configured4.IsModelWithinInstalledCapacity &&
               configured4.RequiredScanUnits == 4 &&
               configured4.StartScanParameter == 4,
            "Configured 4 / MaxIO 201 is valid and keeps START_SCAN=4");

        BoardScanCapacity configured10Model201 = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 10 },
            201);
        Assert(configured10Model201.IsModelWithinInstalledCapacity &&
               configured10Model201.RequiredScanUnits == 4 &&
               configured10Model201.InstalledScanUnits == 10 &&
               configured10Model201.ActiveScanUnits == 4 &&
               configured10Model201.StartScanParameter == 4 &&
               configured10Model201.ActiveIoCapacity == 256,
            "Installed 10 / MaxIO 201 keeps installed capacity but scans only the four required cards");

        BoardScanCapacity configured10Model512 = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 10 },
            512);
        Assert(configured10Model512.IsModelWithinInstalledCapacity &&
               configured10Model512.RequiredScanUnits == 8 &&
               configured10Model512.InstalledScanUnits == 10 &&
               configured10Model512.ActiveScanUnits == 8 &&
               configured10Model512.StartScanParameter == 8,
            "Installed 10 / MaxIO 512 scans only the eight required cards");

        BoardScanCapacity configured10Model37 = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 10 },
            37);
        Assert(configured10Model37.IsModelWithinInstalledCapacity &&
               configured10Model37.InstalledScanUnits == 10 &&
               configured10Model37.RequiredScanUnits == 1 &&
               configured10Model37.ActiveScanUnits == 1 &&
               configured10Model37.StartScanParameter == 1 &&
               configured10Model37.ActiveIoCapacity == 64,
            "The logged MaxIO 37 case scans 64 sources instead of all 640 installed sources");

        BoardScanCapacity configured10WithoutModel = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 10 },
            0);
        Assert(configured10WithoutModel.ActiveScanUnits == 10 &&
               configured10WithoutModel.StartScanParameter == 10 &&
               configured10WithoutModel.ActiveIoCapacity == 640,
            "Startup without a model and blank-THT IO mapping still scan every installed card");

        BoardScanCapacity overLimit = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 10 },
            641);
        Assert(!overLimit.IsModelWithinInstalledCapacity &&
               overLimit.RequiredScanUnits == 11 &&
               overLimit.CapacityErrorMessage.Contains("vượt giới hạn 640 IO", StringComparison.Ordinal),
            "MaxIO 641 is rejected as beyond the 10-card/640-IO hardware limit");

        BoardScanCapacity tenCardModel = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 10 },
            640);
        Assert(tenCardModel.IsModelWithinInstalledCapacity &&
               tenCardModel.RequiredScanUnits == 10 &&
               tenCardModel.ActiveScanUnits == 10 &&
               tenCardModel.ActiveIoCapacity == 640 &&
               tenCardModel.StartScanParameter == 10,
            "A valid IO640 model fits exactly in ten cards without a capacity warning");

        var offsetStart = new ProductionSettings
        {
            ExpansionCardCount = 4,
            StartCardNumber = 3
        };
        BoardCapacity offsetCapacity = BoardCapacity.FromSettings(offsetStart);
        Assert(offsetCapacity.FirstGlobalIo == 1 && offsetCapacity.LastGlobalIo == 256 &&
               offsetCapacity.TotalIoCapacity == 256 && offsetCapacity.StartCardNumber == 3 &&
               offsetCapacity.ScanCardCount == 6 && offsetCapacity.StartScanParameter == 6 &&
               offsetCapacity.FirstPhysicalIo == 129 && offsetCapacity.LastPhysicalIo == 384,
            "StartCard=3 keeps logical IO1-256 and scans the physical card 3-6 range");

        var offsetMapper = new BoardAddressMapper(offsetCapacity);
        AssertAddress(offsetMapper, 1, 3, 1, 1);
        AssertAddress(offsetMapper, 256, 6, 2, 32);
        Assert(!offsetMapper.TryDecode(0x80, BoardIoDecoder.SourceBase, 127, out _) &&
               offsetMapper.TryDecode(0x81, BoardIoDecoder.SourceBase, 0, out int firstOffsetIo) &&
               firstOffsetIo == 1 &&
               offsetMapper.TryDecode(0x82, BoardIoDecoder.SourceBase, 127, out int lastOffsetIo) &&
               lastOffsetIo == 256,
            "Decoder ignores cards before Start Card and maps the selected physical range to logical IO1-N");

        BoardScanCapacity offsetModel = BoardScanCapacity.Create(offsetStart, 201);
        BoardScanCapacity offsetSmallModel = BoardScanCapacity.Create(offsetStart, 37);
        BoardScanCapacity offsetTooLarge = BoardScanCapacity.Create(offsetStart, 257);
        Assert(offsetModel.IsModelWithinInstalledCapacity &&
               offsetModel.Active.ExpansionCardCount == 4 &&
               offsetModel.StartScanParameter == 6 &&
               offsetSmallModel.IsModelWithinInstalledCapacity &&
               offsetSmallModel.Active.ExpansionCardCount == 1 &&
               offsetSmallModel.StartScanParameter == 3 &&
               offsetSmallModel.ActiveIoCapacity == 64 &&
               !offsetTooLarge.IsModelWithinInstalledCapacity,
            "Start Card does not let a model exceed the configured logical card count");

        string settingsXaml = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "ProductionSettingsPage.xaml"));
        Assert(settingsXaml.Contains("Settings.ExpansionCardCount", StringComparison.Ordinal) &&
               settingsXaml.Contains("x:Name=\"TotalIoCapacityText\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("Settings.StartCardNumber", StringComparison.Ordinal) &&
               !settingsXaml.Contains("Settings.PhysicalCardCount", StringComparison.Ordinal) &&
               !settingsXaml.Contains("Settings.PortCount", StringComparison.Ordinal),
            "Production Settings exposes Start Card, ExpansionCardCount and read-only Total IO");

        string cfgPath = Path.Combine(Path.GetTempPath(), $"jbz-card-capacity-{Guid.NewGuid():N}.cfg");
        try
        {
            ProductionConfigService.SaveLegacyCfg(offsetStart, cfgPath);
            string cfg = File.ReadAllText(cfgPath);
            Assert(offsetStart.StartCardNumber == 3 && offsetStart.CardCount == 6 &&
                   cfg.Contains("[StartCardNumber]3", StringComparison.Ordinal) &&
                   cfg.Contains("[ExpansionCardCount]4", StringComparison.Ordinal) &&
                   cfg.Contains("[CardCount]6", StringComparison.Ordinal),
                "CFG persists Start Card, logical card count and firmware scan-through consistently");
        }
        finally
        {
            if (File.Exists(cfgPath))
                File.Delete(cfgPath);
        }
    }

    private static void AssertAddress(
        BoardAddressMapper mapper,
        int globalIo,
        int expansionCard,
        int port,
        int localIo)
    {
        BoardCardAddress address = mapper.GetCardAddress(globalIo);
        Assert(address.GlobalIoNumber == globalIo &&
               address.ExpansionCardNumber == expansionCard &&
               address.PortNumber == port &&
               address.LocalIoOnPort == localIo,
            $"IO{globalIo} => expansion card {expansionCard} / port {port} / local {localIo}");
    }

    private static void AssertCapacity(int expansion, int physical, int scan, int io)
    {
        BoardCapacity capacity = BoardCapacity.Create(expansion);
        Assert(capacity.PortCount == physical, $"Expansion {expansion}: internal port count");
        Assert(capacity.ScanCardCount == scan, $"Expansion {expansion}: scan");
        Assert(capacity.TotalIoCapacity == io, $"Expansion {expansion}: IO");
    }

    private static void TestDecoderModes()
    {
        var decoder = new BoardIoDecoder();
        decoder.ConfigureCapacity(BoardCapacity.Create(1));
        decoder.ConfigureMode(BoardScanMode.Production);
        byte[] smallRaw = BuildProductionScanFrame(1, 0x00, (1, 18));
        ScanFrame production = decoder.Feed(smallRaw).Single();
        Assert(production.Complete && production.Mode == BoardScanMode.Production, "Production complete");
        Assert(production.Connections.TryGetValue(1, out IReadOnlySet<int>? targets) && targets.SetEquals([18]), "IO1->IO18");
        Assert(production.SourceCount == 64 && production.ExpectedIoCount == 64 &&
               production.EndMarkerCode == 0x00 && production.UnknownBytes == 0,
            "Small production frame has strict coverage and C0 00 metadata");

        decoder.Reset();
        var replacementRaw = BuildProductionScanFrame(1, 0x00, (1, 2)).ToList();
        replacementRaw.RemoveRange(4, 2); // Bo thật có thể phát A0 01 thay cho source 80 01.
        ScanFrame targetReplacement = decoder.Feed(replacementRaw.ToArray()).Single();
        Assert(targetReplacement.Complete &&
               targetReplacement.SourceCount == 63 &&
               targetReplacement.ActiveIo.SetEquals([2]) &&
               targetReplacement.Connections.TryGetValue(1, out IReadOnlySet<int>? replacementTargets) &&
               replacementTargets.SetEquals([2]),
            "Target word replacing its own source still provides complete production coverage");
        using (TestEngine replacementEngine = CreateEngine(out _))
        {
            replacementEngine.SetModel(Model(("NAM", new[] { 1, 2 })));
            replacementEngine.ProcessFrame(targetReplacement);
            Assert(replacementEngine.ContinuityPassed,
                "Source-replacement frame reaches TestEngine and passes the NAM IO1-IO2 network");
        }

        decoder.Reset();
        Assert(decoder.Feed(smallRaw.AsSpan(0, smallRaw.Length - 1)).Count == 0,
            "Partial terminator is buffered");
        ScanFrame splitFrame = decoder.Feed(smallRaw.AsSpan(smallRaw.Length - 1)).Single();
        Assert(splitFrame.Connections.TryGetValue(1, out IReadOnlySet<int>? splitTargets) &&
               splitTargets.SetEquals([18]) && splitFrame.Complete,
            "Frame split across reads is reconstructed");

        decoder.Reset();
        byte[] secondRaw = BuildProductionScanFrame(1, 0x00, (2, 8));
        IReadOnlyList<ScanFrame> multiple = decoder.Feed(smallRaw.Concat(secondRaw).ToArray());
        Assert(multiple.Count == 2 &&
               multiple[0].Connections[1].SetEquals([18]) &&
               multiple[1].Connections[2].SetEquals([8]),
            "Multiple complete frames in one read are decoded");

        decoder.Reset();
        byte[] partialLarge = BuildProductionScanFrame(10, 0x01)
            .Take(300 * 2)
            .Concat(new byte[] { 0xC0, 0x01 })
            .ToArray();
        decoder.ConfigureCapacity(BoardCapacity.Create(10));
        ScanFrame incomplete = decoder.Feed(partialLarge).Single();
        Assert(!incomplete.Complete && incomplete.SourceCount == 300 &&
               incomplete.ExpectedIoCount == 640 && incomplete.EndMarkerCode == 0x01,
            "Partial 300/640 frame is diagnostic incomplete and cannot ARM");

        decoder.Reset();
        byte[] largeRaw = BuildProductionScanFrame(10, 0x01);
        ScanFrame large = decoder.Feed(largeRaw).Single();
        Assert(large.Complete && large.SourceCount == 640 && large.ExpectedIoCount == 640 &&
               large.EndMarkerCode == 0x01 && large.UnknownBytes == 0,
            "Ten-card 640-source frame accepts C0 01 without unknown bytes");

        decoder.Reset();
        Assert(decoder.Feed(largeRaw.AsSpan(0, largeRaw.Length - 1)).Count == 0,
            "Large C0 byte remains buffered across RX boundary");
        ScanFrame splitLarge = decoder.Feed([0x01]).Single();
        Assert(splitLarge.Complete && splitLarge.EndMarkerCode == 0x01,
            "Split C0/01 terminator is recognized");

        decoder.Reset();
        byte[] unknownEnd = BuildProductionScanFrame(8, 0x02);
        ScanFrame unknownTerminator = decoder.Feed(unknownEnd).Single();
        Assert(!unknownTerminator.Complete && !unknownTerminator.TerminatorKnown &&
               unknownTerminator.EndMarkerCode == 0x02,
            "C0 02 is diagnostic only, not guessed as a valid terminator");

        BoardScanCapacity configured10Required8 = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 10 },
            512);
        Assert(configured10Required8.InstalledScanUnits == 10 &&
               configured10Required8.RequiredScanUnits == 8 &&
               configured10Required8.ActiveScanUnits == 8 &&
               configured10Required8.StartScanParameter == 8 &&
               configured10Required8.ActiveIoCapacity == 512 &&
               configured10Required8.IsModelWithinInstalledCapacity,
            "Installed 10 / required 8 keeps installed capacity but scans active 8");

        BoardScanCapacity probeAllInstalled = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 2, UseTestPointer = true },
            maxGlobalIo: 18,
            scanAllInstalledIo: true);
        Assert(probeAllInstalled.InstalledScanUnits == 2 &&
               probeAllInstalled.ActiveScanUnits == 2 &&
               probeAllInstalled.ActiveIoCapacity == 128 &&
               probeAllInstalled.IsModelWithinInstalledCapacity,
            "Test pointer scans all configured IO: a two-card station exposes IO1-128 regardless of THT MaxIo");

        BoardScanCapacity insufficient = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 4 },
            512);
        Assert(insufficient.InstalledScanUnits == 4 && insufficient.RequiredScanUnits == 8 &&
               !insufficient.IsModelWithinInstalledCapacity,
            "Installed 4 / required 8 is an explicit capacity mismatch");

        Assert(BoardCapacity.Create(2).StartScanParameter == 2 &&
               BoardCapacity.Create(4).StartScanParameter == 4,
            "START_SCAN parameter follows BoardCapacity, not a hard-coded 02");

        decoder.ConfigureCapacity(BoardCapacity.Create(4, 3));
        decoder.ConfigureMode(BoardScanMode.Production);
        ScanFrame offsetFrame = decoder.Feed(
            BuildProductionScanFrame(6, 0x01, (129, 130))).Single();
        Assert(offsetFrame.Complete && offsetFrame.SourceCount == 256 &&
               offsetFrame.ExpectedIoCount == 256 &&
               offsetFrame.Connections.TryGetValue(1, out IReadOnlySet<int>? offsetTargets) &&
               offsetTargets.SetEquals([2]),
            "StartCard=3 discards physical cards 1-2 and exposes physical IO129 as logical IO1");

        var confirmationSettings = new ProductionSettings { IoConfirm1 = 2, IoConfirmN = 3 };
        var confirmationTransport = new D2xxBoardTransport(string.Empty, confirmationSettings);
        MethodInfo shouldPublish = typeof(D2xxBoardTransport).GetMethod(
            "ShouldPublishConfirmedFrame",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ShouldPublishConfirmedFrame method not found.");
        bool Confirm(ScanFrame frame) => (bool)(shouldPublish.Invoke(confirmationTransport, [frame]) ?? false);
        ScanFrame stableA = FrameSeq(1, (1, [2]));
        ScanFrame stableB = FrameSeq(2, (1, [3]));
        Assert(!Confirm(stableA) && !Confirm(stableB) && Confirm(stableB),
            "IO Confirm 1/2 resets when the complete logical snapshot changes");
        ScanFrame stableC = FrameSeq(3, (1, [4]));
        Assert(!Confirm(stableC) && !Confirm(stableC) && Confirm(stableC),
            "IO Confirm N=3 requires three consecutive matching snapshots after first confirmation");
        confirmationTransport.DisposeAsync().AsTask().GetAwaiter().GetResult();

        decoder.ConfigureCapacity(BoardCapacity.Create(4));
        decoder.ConfigureMode(BoardScanMode.Probe);
        ScanFrame touch5 = decoder.Feed([0xA0, 0x04]).Single();
        Assert(touch5.Mode == BoardScanMode.Probe && touch5.ActiveIo.SetEquals([5]), "Probe touch IO5");
        ScanFrame release5 = decoder.Feed([0x80, 0x04]).Single();
        Assert(release5.ActiveIo.Count == 0, "Probe release IO5");
        ScanFrame touch113 = decoder.Feed([0xA0, 0x70]).Single();
        Assert(touch113.ActiveIo.SetEquals([113]), "Probe unmapped IO113");

        // ConfigureMode phải reset source còn dở của decoder trước đó.
        decoder.ConfigureMode(BoardScanMode.Production);
        decoder.ConfigureCapacity(BoardCapacity.Create(1));
        ScanFrame noStaleSource = decoder.Feed(BuildProductionScanFrame(1, 0x00)).Single();
        Assert(noStaleSource.Connections.Values.All(targetsAfterSwitch => targetsAfterSwitch.Count == 0),
            "No stale source/target edge after mode switch");

        decoder.ConfigureCapacity(BoardCapacity.Create(3));
        _ = decoder.Feed(BuildProductionScanFrame(3, 0x00).AsSpan(0, 200));
        decoder.ConfigureCapacity(BoardCapacity.Create(10));
        ScanFrame afterCapacityChange = decoder.Feed(BuildProductionScanFrame(10, 0x01)).Single();
        Assert(afterCapacityChange.Complete &&
               afterCapacityChange.SourceCount == 640 &&
               afterCapacityChange.ExpectedIoCount == 640 &&
               afterCapacityChange.Sequence == 1,
            "Changing 3 -> 10 cards discards old partial data and resets frame sequence");
    }

    private static void TestTenCardCompleteFrameStress()
    {
        var decoder = new BoardIoDecoder();
        decoder.ConfigureCapacity(BoardCapacity.Create(10));
        decoder.ConfigureMode(BoardScanMode.Production);
        byte[] raw = BuildProductionScanFrame(10, 0x01);

        using TestEngine engine = CreateEngine(out FakeBoard board);
        engine.SetModel(Model(("PAIR", new[] { 1, 18 })));
        int changed = 0;
        engine.Changed += (_, _) => changed++;

        var supervisor = new ScanSupervisor(board, _ => { });
        bool restarted = supervisor.EnsureProductionScanAsync(640, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        const int frameCount = 500;
        long retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < frameCount; index++)
        {
            ScanFrame frame = decoder.Feed(raw).Single();
            board.Publish(frame);
            engine.ProcessFrame(frame);
        }
        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        long retainedAfter = GC.GetTotalMemory(forceFullCollection: true);
        int startCount = board.Commands.Count(command => command == "START");
        int stopCount = board.Commands.Count(command => command == "STOP");

        Assert(!restarted && startCount == 0 && stopCount == 0,
            "Ten-card stress reuses the healthy configured stream without repeated START/STOP");
        Assert(board.CompleteFramesReceived == frameCount &&
               engine.FramesProcessed == frameCount,
            "Ten-card stress processes all 500 complete 640-IO frames");
        Assert(changed <= 1,
            "Ten-card identical topology does not raise unbounded Changed/UI events");
        Assert(retainedAfter <= retainedBefore + (32L * 1024 * 1024),
            $"Ten-card stress retained memory stays bounded ({retainedBefore} -> {retainedAfter})");
        Console.WriteLine(
            $"10-CARD STRESS: frames={frameCount} elapsedMs={stopwatch.ElapsedMilliseconds} " +
            $"allocated={allocated:N0} changed={changed} START={startCount} STOP={stopCount}");
    }

    private static byte[] BuildProductionScanFrame(
        int scanUnits,
        byte terminatorCode,
        (int Source, int Target)? connection = null)
    {
        int ioCount = scanUnits * BoardCapacity.IoPerExpansionCard;
        var bytes = new List<byte>((ioCount * 2) + 4);
        for (int io = 1; io <= ioCount; io++)
        {
            int zeroBased = io - 1;
            bytes.Add(checked((byte)(BoardIoDecoder.SourceBase + zeroBased / BoardAddressMapper.IoPerProtocolBank)));
            bytes.Add(checked((byte)(zeroBased % BoardAddressMapper.IoPerProtocolBank)));

            if (connection is { } edge && edge.Source == io)
            {
                int targetZeroBased = edge.Target - 1;
                bytes.Add(checked((byte)(BoardIoDecoder.TargetBase + targetZeroBased / BoardAddressMapper.IoPerProtocolBank)));
                bytes.Add(checked((byte)(targetZeroBased % BoardAddressMapper.IoPerProtocolBank)));
            }
        }

        bytes.Add(BoardIoDecoder.WordEnd1);
        bytes.Add(terminatorCode);
        return bytes.ToArray();
    }

    private static void TestEngineVectors()
    {
        using var engine = CreateEngine(out _);

        ProductModel pair = Model(("PAIR", new[] { 1, 18 }));
        engine.SetModel(pair);
        IReadOnlyList<FaultRow> initialPairRows = engine.BuildRows().Where(row => row.WireName == "PAIR").ToArray();
        Assert(initialPairRows.Count == 2 &&
               initialPairRows.Any(row => row.Io == 1 && row.Kind == FaultKind.MissingConnection && row.FaultType == "Đơn" && row.Pin == "1") &&
               initialPairRows.Any(row => row.Io == 18 && row.Kind == FaultKind.MissingConnection && row.FaultType == "Đơn" && row.Pin == "18") &&
               initialPairRows.All(row => row.Status == "CHƯA KẾT NỐI") &&
               initialPairRows.All(row => !row.IoText.Contains("<->", StringComparison.Ordinal) &&
                                          !row.Pin.Contains("<->", StringComparison.Ordinal)),
            "Model load shows one Htdrv-style endpoint row per pin, not a merged IO/pin row");
        ScanFrame pairPassFrame = Frame((1, new[] { 18 }));
        engine.ProcessFrame(pairPassFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(pairPassFrame);
        IReadOnlyList<FaultRow> pairMapped = engine.BuildRows().Where(row => row.WireName == "PAIR").ToArray();
        Assert(engine.ContinuityPassed &&
               !engine.HasWiringFault &&
               pairMapped.Count == 0,
            "Expected IO1-IO18 pass removes connected production rows from TestWindow");
        engine.ProcessFrame(Frame((1, Array.Empty<int>())));
        IReadOnlyList<FaultRow> pairReleased = engine.BuildRows().Where(row => row.WireName == "PAIR").ToArray();
        Assert(pairReleased.Count == 2 &&
               pairReleased.All(row => row.FaultType == "Đơn" && row.Status == "CHƯA KẾT NỐI"),
            "Releasing/jig-open frame shows endpoint rows again");

        engine.SetModel(pair);
        ScanFrame wrongFrame = Frame((1, new[] { 40 }));
        engine.ProcessFrame(wrongFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(wrongFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs + 5);
        engine.ProcessFrame(wrongFrame);
        Assert(engine.HasWiringFault, "IO1-IO40 is wiring fault");

        engine.SetModel(pair);
        engine.ProcessFrame(Frame());
        Assert(engine.BuildRows().Count(row => row.Kind == FaultKind.MissingConnection) == 2,
            "Missing IO1-IO18 keeps both endpoint rows as display-only pending rows");
        Assert(!engine.BuildRows().Any(row => row.Kind == FaultKind.Open), "Missing IO1-IO18 is not a production fault row");
        Assert(!engine.HasConfirmedOpenCircuit, "Empty fixture is not inferred as product OPEN");

        engine.SetModel(pair);
        ScanFrame oneEndOnly = Frame((1, Array.Empty<int>()));
        engine.ProcessFrame(oneEndOnly);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 25);
        engine.ProcessFrame(oneEndOnly);
        Assert(engine.HasExpectedSourceCoverage &&
               !engine.ReadyToEvaluateProductFaults &&
               !engine.HasConfirmedOpenCircuit &&
               !engine.HasWiringFault &&
               engine.BuildRows().Any(row => row.Kind == FaultKind.MissingConnection),
            "One endpoint held during install remains display-only and does not become OPEN product FAIL");

        ProductModel twoPairs = Model(("PAIR-A", new[] { 1, 18 }), ("PAIR-B", new[] { 2, 8 }));
        engine.SetModel(twoPairs);
        ScanFrame twoPairPass = Frame((1, new[] { 18 }), (2, new[] { 8 }));
        engine.ProcessFrame(twoPairPass);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(twoPairPass);
        engine.ProcessFrame(Frame((2, new[] { 8 })));
        Assert(engine.IsPassReleaseStarted && !engine.IsProductReleased,
            "After PASS/eject, losing one required connection detects release start before full release");

        engine.SetModel(twoPairs);
        engine.ProcessFrame(Frame((1, new[] { 18 })));
        Assert(!engine.ReadyToEvaluateProductFaults &&
               !engine.HasConfirmedOpenCircuit &&
               !engine.HasWiringFault,
            "Partial source coverage is installing, not product FAIL");

        // Một cạnh sai vật lý phải được xác nhận ngay cả khi sản phẩm mới lắp
        // một phần. PASS vẫn bị khóa bởi coverage đầy đủ, nhưng không được xóa
        // candidate sai dây chỉ vì các source khác chưa xuất hiện.
        engine.SetModel(twoPairs);
        ScanFrame partialWrongWire = Frame((1, new[] { 40 }));
        engine.ProcessFrame(partialWrongWire);
        Thread.Sleep(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs + 5);
        engine.ProcessFrame(partialWrongWire);
        Assert(!engine.ReadyToEvaluateProductFaults && engine.HasWiringFault,
            "A stable wrong physical pair fails without waiting for full model source coverage");

        // Source==target là trạng thái một đầu chạm/từ decoder target-replace,
        // không phải một dây nối giữa hai đầu và không được tạo FAIL giả.
        engine.SetModel(twoPairs);
        ScanFrame oneEndTouch = Frame((1, new[] { 1 }));
        engine.ProcessFrame(oneEndTouch);
        Thread.Sleep(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs + 5);
        engine.ProcessFrame(oneEndTouch);
        Assert(!engine.HasWiringFault,
            "One-end source self-edge never becomes a wrong-wire FAIL");

        ScanFrame fullCoverageOpen = Frame((1, new[] { 18 }), (2, Array.Empty<int>()));
        engine.ProcessFrame(fullCoverageOpen);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(fullCoverageOpen);
        Assert(engine.ReadyToEvaluateProductFaults &&
               !engine.HasConfirmedOpenCircuit &&
               !engine.HasWiringFault &&
               engine.BuildRows().Count(row => row.Kind == FaultKind.MissingConnection) == 2,
            "Full source coverage with a pending pair keeps both endpoint rows display-only");

        ProductModel splice = Model(("SPLICE", new[] { 5, 20, 33 }));
        engine.SetModel(splice);
        ScanFrame spliceOpenFrame = Frame((5, new[] { 20 }));
        engine.ProcessFrame(spliceOpenFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(spliceOpenFrame);
        IReadOnlyList<FaultDetail> confirmedSpliceOpen = engine.BuildConfirmedOpenFaults();
        Assert(confirmedSpliceOpen.Count == 0 &&
               !engine.BuildRows().Any(row => row.Kind == FaultKind.Open) &&
               engine.BuildRows().Count(row => row.Kind == FaultKind.MissingConnection) == 3 &&
               engine.BuildRows().Where(row => row.WireName == "SPLICE")
                   .All(row => row.FaultType == "Nối chung" && row.Status == "CHƯA KẾT NỐI"),
            "Splice missing target is display-only, not production OPEN");

        engine.SetModel(splice);
        ScanFrame splicePassFrame = Frame((5, new[] { 20, 33 }));
        engine.ProcessFrame(splicePassFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(splicePassFrame);
        FaultRow[] spliceMapped = engine.BuildRows().Where(row => row.WireName == "SPLICE").ToArray();
        Assert(engine.ContinuityPassed &&
               !engine.HasWiringFault &&
               spliceMapped.Length == 0,
            "Splice component pass removes connected endpoint rows");

        engine.ProcessFrame(spliceOpenFrame);
        Assert(engine.BuildRows().Any(row => row.Kind == FaultKind.MissingConnection),
            "Removing a completed connection re-adds display-only pending row");

        ProductModel sharedCrimp = Model(
            ("BE31", new[] { 31, 518 }),
            ("BE32", new[] { 32, 518 }),
            ("BE33", new[] { 33, 518 }),
            ("BC13", new[] { 90, 518 }),
            ("BC14", new[] { 91, 518 }),
            ("IC03", new[] { 230, 518 }));
        engine.SetModel(sharedCrimp);
        engine.ProcessFrame(Frame((31, new[] { 32, 33, 90, 91, 230, 518 })));
        Assert(engine.ContinuityPassed &&
               !engine.HasWiringFault &&
               engine.PassedNets.Count == 6 &&
               !engine.BuildRows().Any(row =>
                   row.WireName is "BE31" or "BE32" or "BE33" or "BC13" or "BC14" or "IC03"),
            "Shared crimp IO518 passes all six nets and removes connector 29 rows when firmware reports one electrical component");

        engine.SetModel(sharedCrimp);
        engine.ProcessFrame(Frame((31, new[] { 32, 33, 90, 91, 518 })));
        Assert(!engine.ContinuityPassed &&
               engine.PassedNets.Count == 5 &&
               engine.BuildRows().Any(row => row.WireName == "IC03") &&
               !engine.BuildRows().Any(row =>
                   row.WireName is "BE31" or "BE32" or "BE33" or "BC13" or "BC14"),
            "Shared crimp keeps only the missing IC03 branch visible when IO230 is absent");

        engine.SetModel(pair);
        engine.ProcessFrame(new ScanFrame(
            DateTime.Now, 1, new HashSet<int> { 1, 40 }, [], true, 0, 1,
            new Dictionary<int, IReadOnlySet<int>> { [1] = new HashSet<int> { 40 } },
            new Dictionary<int, int> { [40] = 1 }, BoardScanMode.Probe));
        Assert(!engine.HasWiringFault && !engine.ContinuityPassed, "Probe frame never enters production evaluation");
    }

    private static void TestPendingContinuityPresentation()
    {
        using var engine = CreateEngine(out _);
        ProductModel twoPairs = Model(
            ("PAIR-A", new[] { 1, 3 }),
            ("PAIR-B", new[] { 2, 4 }));
        engine.SetModel(twoPairs);

        FaultRow[] initialRows = engine.BuildRows()
            .Where(row => row.WireName is "PAIR-A" or "PAIR-B")
            .ToArray();
        int[] initialOrder = initialRows.Select(row => row.Io).ToArray();
        Dictionary<int, int> initialDisplayOrder = initialRows.ToDictionary(row => row.Io, row => row.DisplayOrder);
        Assert(initialRows.Length == 4 && initialOrder.SequenceEqual([1, 3, 2, 4]),
            "Two pending pairs keep the model/display endpoint order");
        Assert(initialRows.All(row =>
                   row.Kind == FaultKind.MissingConnection &&
                   row.FaultType == "Đơn" &&
                   row.Status == "CHƯA KẾT NỐI") &&
               initialRows.All(row => row.FaultType != "KIỂM TRA" && row.Status != "CHỜ KẾT NỐI"),
            "Pending point-to-point rows use only Đơn / CHƯA KẾT NỐI");

        ScanFrame pairAPass = Frame((1, new[] { 3 }));
        engine.ProcessFrame(pairAPass);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(pairAPass);
        FaultRow[] afterPairA = engine.BuildRows()
            .Where(row => row.WireName is "PAIR-A" or "PAIR-B")
            .ToArray();
        Assert(afterPairA.Select(row => row.Io).SequenceEqual([2, 4]) &&
               afterPairA.All(row => row.Status == "CHƯA KẾT NỐI") &&
               !engine.ContinuityPassed,
            "Passing PAIR-A hides only IO1/IO3 and leaves PAIR-B pending");

        ScanFrame bothPass = Frame((1, new[] { 3 }), (2, new[] { 4 }));
        engine.ProcessFrame(bothPass);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(bothPass);
        Assert(engine.ContinuityPassed &&
               !engine.HasWiringFault &&
               !engine.BuildRows().Any(row => row.WireName is "PAIR-A" or "PAIR-B"),
            "Both correct pairs remove all normal pending rows without false wiring faults");

        engine.ProcessFrame(Frame((2, new[] { 4 })));
        FaultRow[] restoredPairA = engine.BuildRows().Where(row => row.WireName == "PAIR-A").ToArray();
        Assert(restoredPairA.Select(row => row.Io).SequenceEqual([1, 3]) &&
               restoredPairA.All(row =>
                   row.Status == "CHƯA KẾT NỐI" &&
                   row.DisplayOrder == initialDisplayOrder[row.Io]),
            "A lost completed pair reappears at its original DisplayOrder");

        ProductModel splice = Model(("SPLICE", new[] { 5, 20, 33 }));
        engine.SetModel(splice);
        FaultRow[] spliceRows = engine.BuildRows().Where(row => row.WireName == "SPLICE").ToArray();
        Assert(spliceRows.Length == 3 &&
               spliceRows.All(row => row.FaultType == "Nối chung" && row.Status == "CHƯA KẾT NỐI"),
            "A multi-endpoint network remains one splice topology presented as Nối chung");

        var common = new PinRecord("CLIP", "AO", 201, "AO", PinType: "AO", OriginalOrder: 1);
        var a1 = new PinRecord("CLIP", "a1", 202, "a1", PinType: "a1", OriginalOrder: 2);
        var a2 = new PinRecord("CLIP", "a2", 203, "a2", PinType: "a2", OriginalOrder: 3);
        var a3 = new PinRecord("CLIP", "a3", 204, "a3", PinType: "a3", OriginalOrder: 4);
        var clipModel = new ProductModel { ModelName = "CLIP", PartNumber = "CLIP" };
        clipModel.Pins.AddRange([common, a1, a2, a3]);
        clipModel.Clip = new ClipTopology(
            common,
            [
                new ClipBranch("a1", 1, 202, a1, null),
                new ClipBranch("a2", 2, 203, a2, null),
                new ClipBranch("a3", 3, 204, a3, null)
            ]);
        engine.SetModel(clipModel);
        FaultRow[] initialClipRows = engine.BuildRows().ToArray();
        Assert(initialClipRows.Length == 4 &&
               initialClipRows.All(row => row.FaultType == "Nối chung" && row.Status == "CHƯA KẾT NỐI"),
            "CLIP common and all unlatch branches use Nối chung / CHƯA KẾT NỐI");

        engine.ProcessFrame(Frame((201, new[] { 202 })));
        FaultRow[] remainingClipRows = engine.BuildRows().ToArray();
        Assert(remainingClipRows.Select(row => row.Io).Order().SequenceEqual([203, 204]) &&
               remainingClipRows.All(row => row.FaultType == "Nối chung" && row.Status == "CHƯA KẾT NỐI") &&
               !engine.HasWiringFault,
            "Latching CLIP a1 hides only common/a1 per existing common behavior; a2/a3 remain without false SHORT");
    }

    private static void TestProductionFaultConfirmation()
    {
        var settings = new ProductionSettings
        {
            OpenCircuitConfirmMs = 100,
            ShortCircuitConfirmMs = 80,
            WrongConnectionConfirmMs = 90,
            ProductSettleTimeMs = 50,
            JigContactUnstableWindowMs = 500
        };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero));
        var gate = new ProductionFaultConfirmationGate(settings, clock);
        var clean = new Dictionary<string, bool> { ["A"] = true, ["B"] = true };
        var openA = new Dictionary<string, bool> { ["A"] = false, ["B"] = true };

        gate.Observe(clean, [], hasProductActivity: true);
        clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultProductSettleTimeMs + 1));
        Assert(gate.Observe(clean, [], true).ProductStable, "Clean product settles before PASS");

        gate.Reset();
        gate.Observe(openA, [], hasProductActivity: false);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert(gate.Observe(openA, [], false).ConfirmedOpenKeys.Count == 0, "Jig empty is not inferred as product OPEN");

        gate.Reset();
        gate.Observe(openA, [], true);
        clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultProductSettleTimeMs - 1));
        Assert(gate.Observe(openA, [], true).ConfirmedOpenKeys.Count == 0, "OPEN is ignored before product settle gate");
        clock.Advance(TimeSpan.FromMilliseconds(2));
        Assert(gate.Observe(openA, [], true).ConfirmedOpenKeys.Count == 0, "OPEN remains ignored after product settle gate");
        Assert(gate.Observe(clean, [], true).ConfirmedOpenKeys.Count == 0, "OPEN recovery has no product fault to clear");

        gate.Reset();
        gate.Observe(clean, [], true);
        clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultProductSettleTimeMs + 1));
        gate.Observe(clean, [], true);
        gate.Observe(openA, [], true);
        clock.Advance(TimeSpan.FromMilliseconds(20));
        gate.Observe(clean, [], true);
        clock.Advance(TimeSpan.FromMilliseconds(20));
        gate.Observe(openA, [], true);
        clock.Advance(TimeSpan.FromMilliseconds(20));
        ProductionFaultConfirmationSnapshot bounce = gate.Observe(clean, [], true);
        Assert(!bounce.ContactUnstable && bounce.ConfirmedOpenKeys.Count == 0, "Repeated OPEN bounce is ignored, not product FAIL");
        clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultProductSettleTimeMs + 1));
        ProductionFaultConfirmationSnapshot recovered = gate.Observe(clean, [], true);
        Assert(!recovered.ContactUnstable && recovered.ProductStable, "Clean re-evaluation clears jig warning");

        ProductionFaultConfirmationSnapshot fullLoss = gate.Observe(
            new Dictionary<string, bool> { ["A"] = false, ["B"] = false },
            [],
            hasProductActivity: false);
        Assert(fullLoss.ContactUnstable && fullLoss.ConfirmedOpenKeys.Count == 0, "Full contact loss is not inferred as product OPEN");

        gate.Reset();
        var shortFault = new[] { new UnexpectedFaultObservation(1, 2, ProductFaultType.ShortCircuit) };
        gate.Observe(clean, shortFault, true);
        clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultShortCircuitConfirmMs - 1));
        Assert(gate.Observe(clean, shortFault, true).ConfirmedUnexpectedPairs.Count == 0,
            "Transient SHORT is not confirmed, without waiting for product settle first");
        clock.Advance(TimeSpan.FromMilliseconds(2));
        Assert(gate.Observe(clean, shortFault, true).ConfirmedUnexpectedPairs.Contains((1, 2)),
            "Stable SHORT is confirmed on the next stable frame without extra product-settle latency");
        Assert(gate.Observe(clean, [], true).ConfirmedUnexpectedPairs.Count == 0, "SHORT recovery resets candidate");

        var wrongFault = new[] { new UnexpectedFaultObservation(3, 4, ProductFaultType.WrongWiring) };
        gate.Observe(clean, wrongFault, true);
        clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs - 1));
        Assert(gate.Observe(clean, wrongFault, true).ConfirmedUnexpectedPairs.Count == 0, "Transient wrong connection not confirmed");
        clock.Advance(TimeSpan.FromMilliseconds(2));
        Assert(gate.Observe(clean, wrongFault, true).ConfirmedUnexpectedPairs.Contains((3, 4)), "Stable wrong connection confirmed");

        string testViewModelSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "ViewModels", "TestViewModel.cs"));
        Assert(testViewModelSource.IndexOf("TriggerPassUi();", StringComparison.Ordinal) <
               testViewModelSource.IndexOf("await PauseProductionScanForFinalPassAsync(ct);", StringComparison.Ordinal),
            "PASS UI is raised before waiting for the hardware scan to stop");

        string soundSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Services", "AppSoundService.cs"));
        int productSoundStart = soundSource.IndexOf("public void PlayProductStart()", StringComparison.Ordinal);
        int passSoundStart = soundSource.IndexOf("public void PlayTestOk()", StringComparison.Ordinal);
        string productSoundMethod = soundSource[productSoundStart..passSoundStart];
        Assert(productSoundMethod.Contains("SafePlay(player);", StringComparison.Ordinal) &&
               !productSoundMethod.Contains("SafePlaySync(player);", StringComparison.Ordinal),
            "Product-start sound never holds the UI sound path for the full WAV duration");

        string faultDialogXaml = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "FaultConfirmationWindow.xaml"));
        string faultDialogSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "FaultConfirmationWindow.xaml.cs"));
        Assert(faultDialogXaml.Contains("SizeToContent=\"Height\"", StringComparison.Ordinal) &&
               faultDialogXaml.Contains("x:Name=\"FaultTypeText\"", StringComparison.Ordinal) &&
               faultDialogXaml.Contains("x:Name=\"SummaryText\"", StringComparison.Ordinal) &&
               faultDialogSource.Contains("ApplyCompactSummary(summary);", StringComparison.Ordinal),
            "FAIL dialog uses a compact auto-height layout without duplicating its fault title");

        string mainWindowXaml = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "MainWindow.xaml"));
        string mainWindowSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "MainWindow.xaml.cs"));
        Assert(!mainWindowXaml.Contains("KẾT NỐI LẠI BO", StringComparison.Ordinal) &&
               !mainWindowXaml.Contains("Test.ConnectBoardCommand", StringComparison.Ordinal) &&
               mainWindowSource.Contains("StartupControlUnlockTimeout", StringComparison.Ordinal) &&
               mainWindowSource.Contains("Task.WhenAny(", StringComparison.Ordinal) &&
               mainWindowSource.Contains("ObserveDeferredStartupAsync(initialization)", StringComparison.Ordinal),
            "Slow board startup unlocks product selection while board reconnect remains fully automatic");

        string bootstrapSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Services", "StartupBootstrapService.cs"));
        Assert(bootstrapSource.Contains("Critical filesystem bootstrap completed.", StringComparison.Ordinal) &&
               !bootstrapSource.Contains("StartLegacyHistoryImportInBackground(", StringComparison.Ordinal) &&
               bootstrapSource.Contains("ImportLegacyHistoryForMaintenanceAsync(", StringComparison.Ordinal) &&
               !bootstrapSource.Contains("new ProductionPersistenceService(", StringComparison.Ordinal) &&
               bootstrapSource.Contains("IsRuntimeMigrationCompletedAsync", StringComparison.Ordinal) &&
               bootstrapSource.Contains("Task.Run(async () =>", StringComparison.Ordinal) &&
               bootstrapSource.Contains("Deferred legacy history import completed.", StringComparison.Ordinal),
            "Legacy import stays off startup and shares the production SQLite writer instead of creating a competing writer");

        string persistenceSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Services", "ProductionPersistenceService.cs"));
        Assert(persistenceSource.Contains("SQLITE_BUSY_RETRY", StringComparison.Ordinal) &&
               persistenceSource.Contains("exception.SqliteErrorCode is 5 or 6", StringComparison.Ordinal),
            "Production SQLite writer retries transient BUSY/LOCKED transactions");

        string mainViewModelSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "ViewModels", "MainViewModel.cs"));
        Assert(mainViewModelSource.Contains(
                   "Task.WhenAll(productionDataTask, boardInitializationTask)",
                   StringComparison.Ordinal) &&
               mainViewModelSource.Contains(
                   "await Test.InitializeAsync();",
                   StringComparison.Ordinal),
            "Board connects alongside DB bootstrap while model/history wait for canonical database migration");

        string historyPageXaml = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "HistoryPage.xaml"));
        string historyPageSource = File.ReadAllText(
            Path.Combine(Environment.CurrentDirectory, "Views", "HistoryPage.xaml.cs"));
        Assert(historyPageXaml.Contains("Content=\"NHẬP LỊCH SỬ CŨ\"", StringComparison.Ordinal) &&
               historyPageXaml.Contains("x:Name=\"CloseButton\"", StringComparison.Ordinal) &&
               historyPageSource.Contains("await _importLegacyHistoryAsync();", StringComparison.Ordinal) &&
               historyPageSource.Contains("CloseButton.IsEnabled = false", StringComparison.Ordinal),
            "Legacy history migration is explicit, uses the shared writer, and cannot return to Production while import is active");

        string appSource = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "App.xaml.cs"));
        Assert(appSource.Contains("Local\\JBZUniversalTester.Production", StringComparison.Ordinal) &&
               appSource.Contains("_ownsSingleInstanceMutex", StringComparison.Ordinal),
            "A station cannot open two app processes that compete for the same board and SQLite database");

        var invalid = new ProductionSettings
        {
            IoScanIntervalMs = -1,
            OpenCircuitConfirmMs = -1,
            ShortCircuitConfirmMs = -1,
            WrongConnectionConfirmMs = int.MaxValue,
            ProductSettleTimeMs = -1,
            JigContactUnstableWindowMs = -1,
            ProbeReplacementThreshold = -1
        };
        ProductionTimingPolicy.Normalize(invalid);
        Assert(invalid.IoScanIntervalMs == ProductionTimingPolicy.DefaultIoScanIntervalMs &&
               invalid.ShortCircuitConfirmMs == ProductionTimingPolicy.DefaultShortCircuitConfirmMs &&
               invalid.WrongConnectionConfirmMs == ProductionTimingPolicy.DefaultWrongConnectionConfirmMs &&
               invalid.ProductSettleTimeMs == ProductionTimingPolicy.DefaultProductSettleTimeMs &&
               invalid.JigContactUnstableWindowMs == ProductionTimingPolicy.DefaultJigContactUnstableWindowMs &&
               invalid.ProbeReplacementThreshold >= 1_000,
            "Timing settings normalize to internal defaults and maintenance setting keeps bounds");

        string settingsJson = JsonSerializer.Serialize(settings);
        ProductionSettings reloaded = JsonSerializer.Deserialize<ProductionSettings>(settingsJson)
            ?? throw new InvalidOperationException("Timing settings JSON reload");
        ProductionTimingPolicy.Normalize(reloaded);
        Assert(reloaded.OpenCircuitConfirmMs == 100 &&
               reloaded.ShortCircuitConfirmMs == ProductionTimingPolicy.DefaultShortCircuitConfirmMs &&
               reloaded.WrongConnectionConfirmMs == ProductionTimingPolicy.DefaultWrongConnectionConfirmMs &&
               reloaded.ProductSettleTimeMs == ProductionTimingPolicy.DefaultProductSettleTimeMs &&
               reloaded.JigContactUnstableWindowMs == ProductionTimingPolicy.DefaultJigContactUnstableWindowMs,
            "Legacy timing settings load but normalize to internal runtime defaults");
    }

    private static void TestIncompleteProductFullReleaseResetsClipCycle()
    {
        const string referenceThtPath = @"C:\ITEM\WH322244.tht";
        if (File.Exists(referenceThtPath))
        {
            ProductModel parsedReference = new ThtModelParser().Load(referenceThtPath);
            Assert(parsedReference.Clip is not null &&
                   parsedReference.Clip.CommonIo == 201 &&
                   parsedReference.Clip.Branches.Any(branch => branch.Name == "a1" && branch.TargetIo == 202) &&
                   parsedReference.Clip.Branches.Any(branch => branch.Name == "a2" && branch.TargetIo == 203) &&
                   parsedReference.Clip.Branches.Any(branch => branch.Name == "a3" && branch.TargetIo == 204),
                "WH322244 actual THT parses AO=IO201 and a1/a2/a3 from their configured IO columns");
        }

        var production = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ProductSettleTimeMs = 0,
            WrongConnectionConfirmMs = 0,
            ShortCircuitConfirmMs = 0
        };
        TestViewModel vm = CreateTestViewModel(production, out FakeBoard board);

        // Tham chiếu C:\ITEM\WH322244.tht: AO common dùng IO201;
        // a1/a2/a3 lấy IO thật từ cột I/O lần lượt 202/203/204.
        ProductModel model = Model(("NORMAL", new[] { 1, 18 }));
        var common = new PinRecord("CLIP", "AO", 201, "AO", PinType: "AO");
        var a1 = new PinRecord("CLIP", "a1", 202, "a1", PinType: "a1");
        var a2 = new PinRecord("CLIP", "a2", 203, "a2", PinType: "a2");
        var a3 = new PinRecord("CLIP", "a3", 204, "a3", PinType: "a3");
        model.Pins.AddRange([common, a1, a2, a3]);
        model.Clip = new ClipTopology(
            common,
            [
                new ClipBranch("a1", 1, 202, a1, null),
                new ClipBranch("a2", 2, 203, a2, null),
                new ClipBranch("a3", 3, 204, a3, null)
            ]);

        vm.SetModel(model);
        vm.StartProductionTestAsync().GetAwaiter().GetResult();

        // Lắp dở: một dây thường và hai nhánh CLIP đã nối.
        board.Publish(FrameSeq(
            1,
            (1, new[] { 18 }),
            (201, new[] { 202, 203 })));
        Assert(vm.PassedNetworkCount == 3 && vm.State != "SẴN SÀNG",
            "Normal wire and connected AO-aN branches are latched in the incomplete cycle");

        // Tháo dây thường nhưng AO-a1 vẫn còn: tuyệt đối chưa reset.
        board.Publish(FrameSeq(2, (201, new[] { 202 })));
        Thread.Sleep(ProductionTimingPolicy.DefaultJigContactUnstableWindowMs + 20);
        board.Publish(FrameSeq(3, (201, new[] { 202 })));
        Assert(vm.PassedNetworkCount > 0 && vm.State != "SẴN SÀNG",
            "One remaining AO-a1 connection prevents cycle reset");

        // Chỉ khi không còn bất kỳ cặp dây thường/CLIP nào và trạng thái rỗng
        // ổn định hết cửa sổ chống chập chờn thì mới xóa toàn bộ latch.
        board.Publish(FrameSeq(4));
        Thread.Sleep(ProductionTimingPolicy.DefaultJigContactUnstableWindowMs + 20);
        board.Publish(FrameSeq(5));
        Assert(vm.PassedNetworkCount == 0 &&
               vm.State == "SẴN SÀNG" &&
               vm.ResultStatusText == "SẴN SÀNG",
            "Full stable release clears normal/CLIP latches and returns the cycle to ready");
    }

    private static void TestPartCounterStore()
    {
        Assert(
            string.Equals(
                new PartCounterStore().StoragePath,
                Path.Combine(AppContext.BaseDirectory, "PartCnt.txt"),
                StringComparison.OrdinalIgnoreCase),
            "Default PartCnt path is only the file beside the running EXE");

        string root = Path.Combine(Path.GetTempPath(), "JBZSelfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "PartCnt.txt");
            var store = new PartCounterStore(path);
            ProductModel modelA = Model(("A", new[] { 1, 2 }));
            modelA.PartNumber = "M030066900";

            PartCounterEntry created = store.GetOrCreate(modelA, 7);
            Assert(created.ReplacementThreshold == 200_000 && created.Counter == 7,
                "New PartCnt row uses original default threshold and migrated counter");
            Assert(File.ReadAllText(path, Encoding.UTF8) == "M030066900 200000 7\r\n",
                "PartCnt row matches original three-column CRLF format");

            File.WriteAllText(path, "M030066900 10 9\r\nDONG_KHONG_HOP_LE\r\n", new UTF8Encoding(false));
            PartCounterEntry edited = store.GetOrCreate(modelA);
            Assert(edited.ReplacementThreshold == 10 && edited.Counter == 9,
                "Manual PartCnt threshold/counter edits are reloaded");

            PartCounterEntry incremented = store.Increment(modelA);
            Assert(incremented.Counter == 10 && incremented.ReplacementThreshold == 10,
                "PartCnt increments the selected part without changing its threshold");
            Assert(store.LastWarning.Length > 0 && File.ReadAllText(path).Contains("DONG_KHONG_HOP_LE", StringComparison.Ordinal),
                "Malformed user line is reported and preserved when PartCnt is rewritten");

            PartCounterEntry reset = store.Reset(modelA);
            Assert(reset.Counter == 0 && reset.ReplacementThreshold == 10,
                "Password-authorized maintenance reset can preserve threshold and zero counter");

            ProductModel modelB = Model(("B", new[] { 3, 4 }));
            modelB.PartNumber = "M030076100";
            PartCounterEntry b = store.GetOrCreate(modelB);
            for (int cycle = 0; cycle < 4; cycle++)
                b = store.Increment(modelB);

            Assert(b.Counter == 4 && store.GetOrCreate(modelA).Counter == 0,
                "PartCnt counters remain isolated by part number");
            Assert(
                File.ReadAllText(path, Encoding.UTF8) ==
                "M030066900 10 0\r\nM030076100 200000 4\r\nDONG_KHONG_HOP_LE\r\n",
                "PartCnt persists one independent row per part number");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestLegacyPhtHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZSelfTests", Guid.NewGuid().ToString("N"));
        string passRoot = Path.Combine(root, "Pass_");
        string errorRoot = Path.Combine(root, "Error_");
        Directory.CreateDirectory(root);
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding cp949 = Encoding.GetEncoding(949);
            Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var writer = new LegacyPhtHistoryService(passRoot, errorRoot);
            ProductModel model = Model(("PAIR", new[] { 1, 2 }));
            model.PartNumber = "sssss";
            model.ProductName = "VOLTAGE_6S";
            model.VehicleType = "AE EV PE";
            model.Eco = "AE EV PE";

            DateTime masterTime = new(2026, 8, 7, 16, 25, 39);
            string masterPath = writer.AppendMaster(model, masterTime, 7000, goodMaster: true);
            var pass = new CompletedTestResult
            {
                Started = new DateTime(2026, 8, 7, 16, 25, 40),
                Finished = new DateTime(2026, 8, 7, 16, 25, 47),
                Passed = true,
                ResultText = "PASS"
            };
            string passPath = writer.AppendProduct(model, pass, 7001);
            string passText = utf8.GetString(File.ReadAllBytes(passPath));
            string expectedPass =
                "|[정상마스터]101|..|260807|162539|2608077000|||sssss7000|VOLTAGE_6S|AE EV PE|AE EV PE||||\r\n" +
                "|1|..|260807|162547|2608077001|||sssss7001|VOLTAGE_6S|AE EV PE|AE EV PE||||\r\n";
            Assert(masterPath == passPath && passPath.EndsWith(
                    Path.Combine("Year2026", "Month08", "Day07.dat"),
                    StringComparison.OrdinalIgnoreCase),
                "PHT PASS path uses original Year/Month/Day.dat hierarchy");
            Assert(passText == expectedPass,
                "PHT PASS/master records match the supplied UTF-8 pipe format and append without truncation");

            var smallCounterPass = new CompletedTestResult
            {
                Started = new DateTime(2026, 7, 1, 18, 17, 0),
                Finished = new DateTime(2026, 7, 1, 18, 17, 9),
                Passed = true,
                ResultText = "PASS"
            };
            string smallCounterPath = writer.AppendProduct(model, smallCounterPass, 1);
            string smallCounterText = utf8.GetString(File.ReadAllBytes(smallCounterPath));
            Assert(smallCounterText == "|1|..|260701|181709|2607010001|||sssss0001|VOLTAGE_6S|AE EV PE|AE EV PE||||\r\n",
                "PHT PASS LOT is padded to four digits like the original application");

            model.PartNumber = "1200020430";
            var productionLotPass = new CompletedTestResult
            {
                Started = new DateTime(2026, 8, 26, 7, 53, 50),
                Finished = new DateTime(2026, 8, 26, 7, 53, 58),
                Passed = true,
                ResultText = "PASS"
            };
            string productionLotPath = writer.AppendProduct(model, productionLotPass, 2001);
            string productionLotText = utf8.GetString(File.ReadAllBytes(productionLotPath));
            Assert(productionLotText ==
                   "|1|..|260826|075358|2608262001|||12000204302001|VOLTAGE_6S|AE EV PE|AE EV PE||||\r\n",
                "PHT PASS record uses configured production LOT instead of PartCnt counter");

            model.ProductName = "BMS EXT";
            model.VehicleType = "US4 HEV";
            model.CustomerCode = "12000/20430";
            var failed = new CompletedTestResult
            {
                Started = new DateTime(2026, 8, 22, 8, 55, 34),
                Finished = new DateTime(2026, 8, 22, 8, 55, 36),
                Passed = false,
                ResultText = "FAIL",
                Faults =
                [
                    new FaultDetail
                    {
                        Type = ProductFaultType.ShortCircuit,
                        ActualSourceIo = 12,
                        ActualTargetIo = 14
                    },
                    new FaultDetail
                    {
                        Type = ProductFaultType.OpenCircuit,
                        ExpectedSourceIo = 10,
                        ExpectedTargetIo = 11
                    }
                ]
            };
            string errorPath = writer.AppendProduct(model, failed, 2000);
            byte[] errorBytes = File.ReadAllBytes(errorPath);
            string errorText = cp949.GetString(errorBytes);
            Assert(errorPath.EndsWith(
                    Path.Combine("Year2026", "Month08", "Day22.err"),
                    StringComparison.OrdinalIgnoreCase),
                "PHT ERR path uses original Year/Month/Day.err hierarchy");
            Assert(errorText.StartsWith(
                    "[정상마스터 Short Open] *BMS EXT; 1200020430; US4 HEV; 12000/20430; ; ; 2000 2026/08/22 08:55:34 - 08:55:36|\r\n",
                    StringComparison.Ordinal) &&
                   errorText.Contains(" >검사 IO:12\r\n -합선 IO:14", StringComparison.Ordinal) &&
                   errorText.Contains(" >검사 IO:10\r\n  단선 IO:11", StringComparison.Ordinal) &&
                   errorText.EndsWith("\r\n\r\n", StringComparison.Ordinal),
                "PHT ERR header, Korean IO detail and CRLF record separator match original format");
            Assert(errorBytes.Length > 4 && !(errorBytes[0] == 0xEF && errorBytes[1] == 0xBB && errorBytes[2] == 0xBF),
                "PHT shared files use CP949 without UTF-8 BOM");

            var reader = new LegacyPhtHistoryReader(passRoot, errorRoot);
            LegacyProductionSnapshot shared = reader.GetProductionSnapshot(
                model,
                new DateTime(2026, 8, 26, 12, 0, 0));
            Assert(shared.DailyTotal == 1 && shared.DailyPass == 1 && shared.DailyFail == 0 &&
                   shared.MonthlyTotal == 2 && shared.LifetimeTotal == 2,
                "Shared production counters include original PASS/ERR files and exclude MASTER rows");

            HistorySearchCriteria allCriteria = new(null, null, null, string.Empty, "ALL", 100);
            IReadOnlyList<TestHistoryRecord> legacyRows = reader.Search(allCriteria);
            Assert(legacyRows.Count == 4 && legacyRows.Count(row => row.Passed) == 3 &&
                   legacyRows.Count(row => !row.Passed) == 1,
                "Original PHT history reader returns every product record from both shared roots");
            Assert(reader.SearchForExport(allCriteria with { MaxRows = 1 }).Count == 4,
                "Original PHT export returns all matching rows instead of the screen row limit");

            TestHistoryRecord duplicateDetailed = legacyRows.Single(row =>
                row.Passed && row.PartNumber == "1200020430" && row.LotNo == 2001);
            duplicateDetailed = new TestHistoryRecord
            {
                Id = 123,
                Started = duplicateDetailed.Started,
                Finished = duplicateDetailed.Finished,
                PartNumber = duplicateDetailed.PartNumber,
                LotNo = duplicateDetailed.LotNo,
                Result = "PASS",
                Passed = true,
                CycleId = "NEW-APP-CYCLE"
            };
            IReadOnlyList<TestHistoryRecord> merged = LegacyPhtHistoryReader.MergeWithoutDuplicates(
                [duplicateDetailed], legacyRows, exportOrder: false);
            Assert(merged.Count == 4 && merged.Count(row => row.Id == 123) == 1,
                "Shared history merge keeps detailed new-app row and removes its compatible PHT duplicate");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestProductionCounters()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZSelfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "production.statistics.json");
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 9, 8, 0, 0, TimeSpan.Zero));
            var store = new ProductionStatisticsStore(path, clock);
            ProductModel modelA = Model(("A", new[] { 1, 2 }));
            modelA.ModelName = "MODEL-A";
            modelA.PartNumber = "ABC123";
            ProductModel modelB = Model(("B", new[] { 3, 4 }));
            modelB.ModelName = "MODEL-B";
            modelB.PartNumber = "XYZ456";

            store.RecordProbeCycle(modelA, 2);
            store.Record(modelA, true, 1, "PASS");
            store.Record(modelA, false, 2, "FAIL");
            store.Record(modelB, true, 3, "PASS");

            ModelProductionStatistics a = store.Get(modelA);
            ModelProductionStatistics b = store.Get(modelB);
            Assert(a.DailyTestCount == 2 && a.DailyPassCount == 1 && a.DailyFailCount == 1 &&
                   a.MonthlyTestCount == 2 && a.LifetimeTestCount == 2,
                "Model A production periods/lifetime and daily result split");
            Assert(a.ProbeCycleCount == 1 && b.ProbeCycleCount == 0 && b.LifetimeTestCount == 1, "Per-model counter isolation");

            ProductModel renamedModelA = Model(("A-RENAMED", new[] { 5, 6 }));
            renamedModelA.ModelName = "MODEL-A-NEW-REVISION";
            renamedModelA.PartNumber = " abc123 ";
            Assert(store.Get(renamedModelA).Total == 2,
                "Same part number restores production count across model name changes");
            store.Record(renamedModelA, true, 4, "PASS");
            Assert(store.Get(modelA).Total == 3 && store.Get(modelB).Total == 1,
                "Production count is isolated by part number and shared by revisions");

            var restarted = new ProductionStatisticsStore(path, clock);
            Assert(restarted.Get(modelA).ProbeCycleCount == 1 && restarted.Get(modelA).LifetimeTestCount == 3, "Counters persist after restart");

            clock.Advance(TimeSpan.FromDays(1));
            ModelProductionStatistics rolledDay = restarted.Get(modelA);
            Assert(rolledDay.DailyTestCount == 0 && rolledDay.DailyPassCount == 0 &&
                   rolledDay.DailyFailCount == 0 && rolledDay.MonthlyTestCount == 3 &&
                   rolledDay.ProbeCycleCount == 1,
                "Daily production resets without resetting month or cumulative PartCnt mirror");
            restarted.Record(modelA, true, 4, "PASS");
            ModelProductionStatistics newDay = restarted.Get(modelA);
            Assert(newDay.DailyTestCount == 1 && newDay.DailyPassCount == 1 &&
                   newDay.DailyFailCount == 0 && newDay.MonthlyTestCount == 4,
                "New day increments the correct daily production counters");

            TestViewModel lotDisplayVm = CreateTestViewModel(
                new ProductionSettings { LotNo = 2000, MasterFaultRequiredCount = 0 });
            MethodInfo applyDailyStatistics = typeof(TestViewModel).GetMethod(
                "ApplyDailyProductionStatistics",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Daily production UI method not found");
            applyDailyStatistics.Invoke(lotDisplayVm,
            [
                new ModelProductionStatistics
                {
                    DailyTestCount = 10,
                    DailyPassCount = 10,
                    DailyFailCount = 0
                }
            ]);
            Assert(lotDisplayVm.Lot == "2010" && lotDisplayVm.Total == 10 &&
                   lotDisplayVm.Pass == 10 && lotDisplayVm.Fail == 0,
                "LOT display is starting LOT 2000 plus daily PASS 10");
            applyDailyStatistics.Invoke(lotDisplayVm, [new ModelProductionStatistics()]);
            Assert(lotDisplayVm.Lot == "2000" && lotDisplayVm.Total == 0,
                "New daily period resets production to zero and LOT display to its starting value");

            clock.Advance(TimeSpan.FromDays(24));
            Assert(restarted.Get(modelA).MonthlyTestCount == 0 && restarted.Get(modelA).ProbeCycleCount == 1, "Monthly period rolls without resetting probe");

            ModelProductionStatistics due = restarted.RecordProbeCycle(modelA, 2);
            Assert(due.ProbeCycleCount == 2 && due.ProbeCycleCount >= due.ProbeReplacementThreshold, "Probe replacement threshold transition");
            long lifetimeBeforeReset = due.LifetimeTestCount;
            ProbeMaintenanceRecord maintenance = restarted.ResetProbeCycle(modelA, 2, "ADMIN", "STATION-1");
            ModelProductionStatistics reset = restarted.Get(modelA);
            Assert(reset.ProbeCycleCount == 0 && reset.LifetimeTestCount == lifetimeBeforeReset, "Probe reset does not reset production counters");
            Assert(maintenance.PreviousProbeCycleCount == 2 && maintenance.Action == "PROBE PIN REPLACED", "Maintenance record values");
            Assert(restarted.GetMaintenanceRecords(modelA).Count == 1 && restarted.GetMaintenanceRecords(modelB).Count == 0, "Maintenance history separated per model");

            Assert(!AdminAuthenticationService.Verify(string.Empty, string.Empty), "Empty admin password cannot authorize reset");
            Assert(!AdminAuthenticationService.Verify("admin-secret", "wrong"), "Wrong admin password rejected");
            Assert(AdminAuthenticationService.Verify("admin-secret", "admin-secret"), "Configured admin password accepted");
            Assert(!AdminAuthenticationService.VerifyProbeMaintenance("wrong"),
                "Probe maintenance rejects a wrong independent password");
            Assert(AdminAuthenticationService.VerifyProbeMaintenance("admin"),
                "Probe maintenance uses the independent admin password");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestProductionPassGateMinimalLatency()
    {
        var slowConfirmProduction = new ProductionSettings
        {
            IoConfirm1 = 5,
            IoConfirmN = 5,
            OpenCircuitConfirmMs = 500,
            ShortCircuitConfirmMs = 500,
            WrongConnectionConfirmMs = 500,
            ProductSettleTimeMs = 500,
            JigContactUnstableWindowMs = 500,
            MasterFaultRequiredCount = 0,
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50,
            PassMarkingToJigDelayMs = 0,
            PageDelay = 5000
        };

        using TestEngine oneNetEngine = CreateEngine(out _, slowConfirmProduction);
        oneNetEngine.SetModel(Model(("ONLY", new[] { 1, 86 })));
        oneNetEngine.ProcessFrame(FrameSeq(1, (1, new[] { 86 })));
        PassGateDiagnostics oneNetGate = oneNetEngine.GetPassGateDiagnostics();
        Assert(oneNetEngine.ExpectedNetCount == 1 &&
               oneNetGate.PassedNetCount == 1 &&
               oneNetEngine.ContinuityPassed,
            "One-network product PASSes on the first valid complete frame");

        using TestEngine reverseOneNetEngine = CreateEngine(out _, slowConfirmProduction);
        reverseOneNetEngine.SetModel(Model(("ONLY", new[] { 1, 86 })));
        reverseOneNetEngine.ProcessFrame(FrameSeq(1, (86, new[] { 1 })));
        Assert(reverseOneNetEngine.ContinuityPassed &&
               reverseOneNetEngine.ReadyToEvaluateProductFaults,
            "Reverse-direction continuity both passes and opens the production PASS gate");

        using TestEngine reverseMultiNetEngine = CreateEngine(out _, slowConfirmProduction);
        reverseMultiNetEngine.SetModel(Model(
            ("1", new[] { 13, 10 }),
            ("2", new[] { 14, 9 }),
            ("CLIP1", new[] { 19, 20 }),
            ("RET1", new[] { 21, 22 })));
        reverseMultiNetEngine.ProcessFrame(FrameSeq(
            221,
            (10, new[] { 13 }),
            (9, new[] { 14 }),
            (20, new[] { 19 }),
            (22, new[] { 21 })));
        PassGateDiagnostics reverseMultiNetGate = reverseMultiNetEngine.GetPassGateDiagnostics();
        Assert(reverseMultiNetGate.PassedNetCount == 4 &&
               reverseMultiNetGate.RemainingNetworks.Count == 0 &&
               reverseMultiNetEngine.ContinuityPassed &&
               reverseMultiNetEngine.ReadyToEvaluateProductFaults &&
               !reverseMultiNetEngine.HasWiringFault,
            "Four-network frame from affected machine PASSes even when every valid edge is source-reversed");

        using TestEngine criticalTopologyEngine = CreateEngine(out FakeBoard criticalBoard, slowConfirmProduction);
        ProductModel criticalTopology = Model(("~1", new[] { 1, 2 }));
        criticalTopologyEngine.SetModel(criticalTopology);
        PassGateDiagnostics criticalInitial = criticalTopologyEngine.GetPassGateDiagnostics();
        IReadOnlyList<FaultRow> criticalInitialRows = criticalTopologyEngine.BuildRows();
        Assert(criticalTopologyEngine.ExpectedNetCount == 1 &&
               criticalInitial.PassedNetCount == 0 &&
               !criticalTopologyEngine.ContinuityPassed &&
               !criticalTopologyEngine.HasWiringFault &&
               criticalInitialRows.Count(row => row.ProductFaultType != ProductFaultType.None) == 0 &&
               criticalInitialRows.Any(row =>
                   row.Kind == FaultKind.MissingConnection &&
                   row.RelatedIos.SequenceEqual([1, 2])),
            "Critical topology: two THT endpoints in one wire/net build one missing display row, not two product faults");

        var rawTopologyDecoder = new BoardIoDecoder();
        rawTopologyDecoder.ConfigureCapacity(BoardCapacity.Create(1));
        rawTopologyDecoder.ConfigureMode(BoardScanMode.Production);
        ScanFrame rawTopologyFrame = rawTopologyDecoder
            .Feed([0x80, 0x00, 0xA0, 0x01, 0xC0, 0x00])
            .Single();
        Assert(rawTopologyFrame.Connections.TryGetValue(1, out IReadOnlySet<int>? rawTopologyTargets) &&
               rawTopologyTargets.SetEquals([2]),
            "Raw protocol 80 00 A0 01 C0 00 decodes as SOURCE IO1 -> TARGET IO2");

        criticalTopologyEngine.ProcessFrame(FrameSeq(2, (2, new[] { 1 })));
        PassGateDiagnostics criticalPassed = criticalTopologyEngine.GetPassGateDiagnostics();
        IReadOnlyList<FaultRow> criticalPassedRows = criticalTopologyEngine.BuildRows();
        FaultRow[] criticalMapped = criticalPassedRows.Where(row => row.WireName == "~1").ToArray();
        Assert(criticalTopologyEngine.ExpectedNetCount == 1 &&
               criticalPassed.PassedNetCount == 1 &&
               criticalTopologyEngine.ContinuityPassed &&
               !criticalTopologyEngine.HasWiringFault &&
               criticalMapped.Length == 0,
            "Critical topology: IO1/IO2 pass removes connected endpoint rows");

        bool criticalPassCommitted = criticalTopologyEngine.CompletePassAsync([])
            .GetAwaiter()
            .GetResult();
        Assert(criticalPassCommitted &&
               criticalBoard.Commands.Contains("SET:2") &&
               criticalBoard.Commands.Contains("SET:1"),
            "Critical topology: no-resistance MasterMinimum=0 equivalent can commit PASS immediately after continuity");

        ProductModel twoWireModel = Model(("PAIR-A", new[] { 1, 86 }), ("PAIR-B", new[] { 2, 87 }));
        using TestEngine twoNetEngine = CreateEngine(out _, slowConfirmProduction);
        twoNetEngine.SetModel(twoWireModel);
        Assert(twoNetEngine.ExpectedNetCount == 2, "Two-wire model builds exactly two expected production networks");

        twoNetEngine.ProcessFrame(FrameSeq(2, (1, new[] { 86 })));
        PassGateDiagnostics partialGate = twoNetEngine.GetPassGateDiagnostics();
        Assert(!twoNetEngine.ContinuityPassed &&
               partialGate.PassedNetCount == 1 &&
               partialGate.RemainingNetworks.Count == 1 &&
               partialGate.RemainingNetworks.Single().Display.Contains("IO2<->IO87", StringComparison.Ordinal),
            "Two-wire model at 1/2 blocks PASS and reports the exact remaining network");

        twoNetEngine.ProcessFrame(FrameSeq(3, (1, new[] { 86 }), (2, new[] { 87 })));
        PassGateDiagnostics cleanGate = twoNetEngine.GetPassGateDiagnostics();
        Assert(twoNetEngine.ContinuityPassed &&
               cleanGate.PassedNetCount == 2 &&
               cleanGate.RemainingNetworks.Count == 0,
            "Two-wire model reaches 2/2 PASS on the first full complete frame");

        using TestEngine wrongEngine = CreateEngine(out _, slowConfirmProduction);
        wrongEngine.SetModel(twoWireModel);
        wrongEngine.ProcessFrame(FrameSeq(4, (1, new[] { 86, 87 }), (2, new[] { 87 })));
        PassGateDiagnostics wrongGate = wrongEngine.GetPassGateDiagnostics();
        Assert(!wrongEngine.ContinuityPassed &&
               wrongGate.PassedNetCount == 2 &&
               wrongGate.WrongCandidateCount > 0 &&
               wrongGate.WrongConfirmedCount == 0,
            "Wrong candidate blocks PASS immediately without waiting for confirmed FAIL");

        using TestEngine shortEngine = CreateEngine(out _, slowConfirmProduction);
        shortEngine.SetModel(twoWireModel);
        shortEngine.ProcessFrame(FrameSeq(5, (1, new[] { 86 }), (2, new[] { 87 }), (86, new[] { 87 })));
        PassGateDiagnostics shortGate = shortEngine.GetPassGateDiagnostics();
        Assert(!shortEngine.ContinuityPassed &&
               shortGate.PassedNetCount == 2 &&
               shortGate.ShortCandidateCount > 0 &&
               shortGate.ShortConfirmedCount == 0,
            "Short candidate blocks PASS immediately without applying FAIL debounce to good nets");

        TestViewModel vm = CreateTestViewModel(slowConfirmProduction, out FakeBoard board);
        vm.SetModel(twoWireModel);
        Assert(vm.ProductionEnabled, "MasterMinimum=0 leaves production enabled for two-wire PASS flow");

        board.Publish(FrameSeq(10));
        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        board.Publish(FrameSeq(10, (1, new[] { 86 }), (2, new[] { 87 })));
        Assert(vm.PassedNetworkCount == 0, "Reused background scan rejects stale pre-cycle complete frame");
        board.Publish(FrameSeq(11, (1, new[] { 86 }), (2, new[] { 87 })));
        Assert(vm.PassedNetworkCount == 2, "Fresh post-ARM frame can satisfy two-wire PASS gate");

        MethodInfo normalizeWire = typeof(ThtModelParser).GetMethod(
            "NormalizeWireIdentity",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("NormalizeWireIdentity method not found.");
        string plainWire = (string)(normalizeWire.Invoke(null, ["c1"]) ?? string.Empty);
        string markedWire = (string)(normalizeWire.Invoke(null, ["\u25C9c1"]) ?? string.Empty);
        string tildeWire = (string)(normalizeWire.Invoke(null, ["~1"]) ?? string.Empty);
        Assert(plainWire == "c1" && markedWire == "c1" && tildeWire == "~1",
            "THT wire-name marker/icon characters do not split a visible two-pin wire into separate nets and ~ remains a valid wire name");

        MethodInfo waitMethod = typeof(TestViewModel).GetMethod(
            "WaitForProbeRelayInterlockAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Probe relay interlock method not found.");
        Stopwatch sw = Stopwatch.StartNew();
        ((Task)waitMethod.Invoke(vm, [CancellationToken.None])!).GetAwaiter().GetResult();
        sw.Stop();
        Assert(sw.ElapsedMilliseconds < 50, "Inactive Probe interlock returns without adding PASS delay");

    }

    private static void TestInlineProbeDoesNotClearWiringFaults()
    {
        var production = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ProductSettleTimeMs = 0,
            WrongConnectionConfirmMs = 500,
            ShortCircuitConfirmMs = 500
        };

        ProductModel model = Model(("PAIR-A", new[] { 1, 86 }), ("PAIR-B", new[] { 2, 87 }));
        TestViewModel vm = CreateTestViewModel(production, out FakeBoard board);
        vm.SetModel(model);
        vm.StartProductionTestAsync().GetAwaiter().GetResult();

        board.Publish(FrameSeq(21, (1, new[] { 86 }), (2, new[] { 87 }), (86, new[] { 87 })));
        TestEngine engine = (TestEngine)(typeof(TestViewModel).GetField(
            "_engine",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(vm) ?? throw new InvalidOperationException("TestViewModel engine field not found"));
        PassGateDiagnostics beforeProbe = engine.GetPassGateDiagnostics();
        Assert(beforeProbe.ShortCandidateCount + beforeProbe.WrongCandidateCount > 0,
            "Wiring fault candidate exists before inline Probe frame");
        long processedBeforeProbe = vm.ProductionFramesProcessed;

        board.Publish(FrameSeq(
            22,
            Enumerable.Range(10, 20)
                .Select(source => (source, new[] { 1 }))
                .ToArray()));

        Assert(vm.HasInlineProbeContacts, "Inline Probe frame appears as transient UI state");
        Assert(vm.ProductionFramesProcessed > processedBeforeProbe,
            "Inline Probe candidate is still processed by Production TestEngine");
        PassGateDiagnostics afterProbe = engine.GetPassGateDiagnostics();
        Assert(afterProbe.ShortCandidateCount + afterProbe.WrongCandidateCount > 0,
            "Inline Probe frame must not clear existing SHORT/WRONG state");

        TestViewModel cleanProbeVm = CreateTestViewModel(production, out FakeBoard cleanProbeBoard);
        cleanProbeVm.SetModel(model);
        cleanProbeVm.StartProductionTestAsync().GetAwaiter().GetResult();
        cleanProbeBoard.Publish(FrameSeq(
            23,
            Enumerable.Range(10, 20)
                .Select(source => (source, new[] { 1 }))
                .ToArray()));
        Assert(cleanProbeVm.HasInlineProbeContacts &&
               !cleanProbeVm.Faults.Any(row => row.Kind is FaultKind.WrongWiring or FaultKind.Short),
            "Inline Probe contact is display-only and cannot create a new WRONG/SHORT fault");

        var pointerDisabled = new ProductionSettings
        {
            UseTestPointer = false,
            MasterFaultRequiredCount = 0,
            ProductSettleTimeMs = 0,
            WrongConnectionConfirmMs = 0,
            ShortCircuitConfirmMs = 0
        };
        TestViewModel pointerDisabledVm = CreateTestViewModel(pointerDisabled, out FakeBoard disabledBoard);
        pointerDisabledVm.SetModel(model);
        pointerDisabledVm.StartProductionTestAsync().GetAwaiter().GetResult();
        disabledBoard.Publish(new ScanFrame(
            DateTime.Now,
            1,
            new HashSet<int> { 113 },
            [],
            false,
            0,
            30,
            new Dictionary<int, IReadOnlySet<int>>(),
            new Dictionary<int, int> { [113] = 1 },
            BoardScanMode.Production));
        Assert(!pointerDisabledVm.HasInlineProbeContacts,
            "UseTestPointer=false disables inline Probe display");

        MethodInfo waitMethod = typeof(TestViewModel).GetMethod(
            "WaitForProbeRelayInterlockAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Probe relay interlock method not found.");
        Stopwatch sw = Stopwatch.StartNew();
        ((Task)waitMethod.Invoke(pointerDisabledVm, [CancellationToken.None])!).GetAwaiter().GetResult();
        sw.Stop();
        Assert(sw.ElapsedMilliseconds < 50, "UseTestPointer=false disables Probe relay interlock");
    }

    private static void TestHtdrvEndpointProbeDisplayCases()
    {
        ProductModel model = HtdrvTwoEndpointModel();
        using TestEngine engine = CreateEngine(out _);
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(1, (1, Array.Empty<int>())));

        FaultRow[] openRows = engine.BuildRows().Where(row => row.WireName == "1").ToArray();
        Assert(openRows.Length == 2 &&
               openRows.Any(row => row.Io == 1 && row.FaultType == "Đơn" && row.Status == "CHƯA KẾT NỐI" && row.Connector == "1" && row.Pin == "1" && row.IoCnPnText == "1-1-1") &&
               openRows.Any(row => row.Io == 2 && row.FaultType == "Đơn" && row.Status == "CHƯA KẾT NỐI" && row.Connector == "1" && row.Pin == "2" && row.IoCnPnText == "2-1-2") &&
               openRows.All(row => !row.IoText.Contains("<->", StringComparison.Ordinal) && !row.Pin.Contains("<->", StringComparison.Ordinal)),
            "CASE A: Open display uses one endpoint row per pin with IO-CN-PN metadata");

        var production = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ProductSettleTimeMs = 1_000,
            WrongConnectionConfirmMs = 0,
            ShortCircuitConfirmMs = 0,
            UseTestPointer = true
        };
        TestViewModel vm = CreateTestViewModel(production, out FakeBoard board);
        vm.SetModel(model);
        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        board.Publish(FrameSeq(10, (1, Array.Empty<int>())));
        board.Publish(FrameSeq(
            11,
            Enumerable.Range(20, 20)
                .Select(source => (source, new[] { 1 }))
                .ToArray()));

        Assert(vm.HasInlineProbeContacts &&
               vm.Faults.Any(row => row.Kind == FaultKind.Probe &&
                                    row.Io == 0 &&
                                    row.WireName == "IO(1)" &&
                                    row.FaultType.Length == 0 &&
                                    row.Connector.Length == 0 &&
                                    row.Pin.Length == 0 &&
                                    row.Section.Length == 0 &&
                                    row.Color.Length == 0 &&
                                    row.Status.Length == 0) &&
               vm.Faults.Any(row => row.Io == 2 && row.FaultType == "Đơn" && row.Status == "CHƯA KẾT NỐI"),
            "CASE B: Probe shows only IO(1) in WireName while the IO2 open row remains");

        board.Publish(FrameSeq(12));
        Assert(!vm.HasInlineProbeContacts &&
               vm.Faults.Any(row => row.Io == 2 && row.FaultType == "Đơn" && row.Status == "CHƯA KẾT NỐI"),
            "CASE C: Probe release removes only Probe presentation and keeps production open row");

        ScanFrame unmappedPairFrame = FrameSeq(14, (23, new[] { 25 }));
        Assert(ProbeContactClassifier.DetectMany(
                   unmappedPairFrame,
                   model,
                   maxContacts: 2,
                   boardCapacity: BoardCapacity.Create(10)).Count == 0,
            "CASE C2: an ordinary IO23<->IO25 edge has no Probe signature");
        using TestEngine unmappedPairEngine = CreateEngine(out _, production);
        unmappedPairEngine.SetModel(model);
        unmappedPairEngine.ProcessFrame(unmappedPairFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs + 20);
        unmappedPairEngine.ProcessFrame(unmappedPairFrame with { Sequence = 15 });
        PassGateDiagnostics unmappedDiagnostics = unmappedPairEngine.GetPassGateDiagnostics();
        Assert(unmappedDiagnostics.WrongConfirmedCount == 1 &&
               unmappedPairEngine.HasWiringFault &&
               unmappedPairEngine.WiringFaults.Any(fault =>
                   fault.SourceIo == 23 &&
                   fault.TargetIo == 25 &&
                   fault.FaultType == ProductFaultType.WrongWiring),
            "CASE C2: two IO absent from THT become confirmed WRONG and must enter the FAIL confirmation flow");

        ProductModel shortModel = Model(("PAIR-A", new[] { 1, 86 }), ("PAIR-B", new[] { 2, 87 }));
        var shortProduction = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ProductSettleTimeMs = 0,
            WrongConnectionConfirmMs = 0,
            ShortCircuitConfirmMs = 0,
            UseTestPointer = true
        };
        TestViewModel shortVm = CreateTestViewModel(shortProduction, out FakeBoard shortBoard);
        shortVm.SetModel(shortModel);
        shortVm.StartProductionTestAsync().GetAwaiter().GetResult();
        MethodInfo refreshFaults = typeof(TestViewModel).GetMethod(
            "RefreshFaults",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RefreshFaults method not found.");
        shortBoard.Publish(FrameSeq(20, (1, new[] { 86 }), (2, new[] { 87 }), (86, new[] { 87 })));
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        shortBoard.Publish(FrameSeq(21, (1, new[] { 86 }), (2, new[] { 87 }), (86, new[] { 87 })));
        Thread.Sleep(ProductionTimingPolicy.DefaultShortCircuitConfirmMs + 5);
        shortBoard.Publish(FrameSeq(22, (1, new[] { 86 }), (2, new[] { 87 }), (86, new[] { 87 })));
        refreshFaults.Invoke(shortVm, []);
        TestEngine shortEngine = (TestEngine)(typeof(TestViewModel).GetField(
            "_engine",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(shortVm) ?? throw new InvalidOperationException("TestViewModel engine field not found"));
        PassGateDiagnostics shortDiagnostics = shortEngine.GetPassGateDiagnostics();
        Assert(shortVm.Faults.Any(row => row.Kind == FaultKind.Short),
            "CASE D setup: real SHORT is confirmed before Probe observation. Rows=" +
            string.Join(" | ", shortVm.Faults.Select(row => $"{row.Kind}/{row.FaultType}/IO{row.Io}/{row.Status}")) +
            $" Diagnostics shortCandidate={shortDiagnostics.ShortCandidateCount} shortConfirmed={shortDiagnostics.ShortConfirmedCount} wrongCandidate={shortDiagnostics.WrongCandidateCount} wrongConfirmed={shortDiagnostics.WrongConfirmedCount}");
        shortBoard.Publish(FrameSeq(
            23,
            Enumerable.Range(30, 20)
                .Select(source => (source, new[] { 86 }))
                .ToArray()));
        refreshFaults.Invoke(shortVm, []);
        Assert(shortVm.Faults.Any(row => row.Kind == FaultKind.Probe && row.WireName == "IO(86)") &&
               shortVm.Faults.Any(row => row.Kind == FaultKind.Short),
            "CASE D: Probe row and real SHORT row can coexist; SHORT remains visible");

        TestViewModel unusedVm = CreateTestViewModel(production, out FakeBoard unusedBoard);
        unusedVm.SetModel(model);
        unusedVm.StartProductionTestAsync().GetAwaiter().GetResult();
        long processedBeforeUnusedProbe = unusedVm.ProductionFramesProcessed;
        unusedBoard.Publish(FrameSeq(
            30,
            Enumerable.Range(40, 20)
                .Select(source => (source, new[] { 7 }))
                .ToArray()));
        Assert(unusedVm.HasInlineProbeContacts &&
               unusedVm.Faults.Any(row =>
                   row.Kind == FaultKind.Probe &&
                   row.Io == 0 &&
                   row.WireName == "IO(7)" &&
                   row.FaultType.Length == 0 &&
                   row.Connector.Length == 0 &&
                   row.Status.Length == 0) &&
               unusedVm.ProductionFramesProcessed > processedBeforeUnusedProbe,
            "CASE E: Unused probe IO is presentation-only and the frame still reaches Production TestEngine");

        TestViewModel testPinVm = CreateTestViewModel(production, out FakeBoard testPinBoard);
        testPinVm.SetModel(model);
        testPinVm.StartProductionTestAsync().GetAwaiter().GetResult();
        testPinVm.StartProbeScanAsync().GetAwaiter().GetResult();
        Assert(testPinBoard.CurrentScanMode == BoardScanMode.Production &&
               testPinVm.State != "ĐANG DÒ CHÂN",
            "TestPin observer does not switch transport out of Production mode or change production state");
        long processedBeforeTestPin = testPinVm.ProductionFramesProcessed;
        testPinBoard.Publish(FrameSeq(
            40,
            Enumerable.Range(60, 20)
                .Select(source => (source, new[] { 1 }))
                .ToArray()));
        Assert(testPinVm.HasInlineProbeContacts &&
               testPinVm.ProductionFramesProcessed > processedBeforeTestPin,
            "StartProbeScanAsync enables Probe/TestPin observation on Production stream");
    }

    private static void TestFiveHundredCycleScanProbeFaultStress()
    {
        var production = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ProductSettleTimeMs = 10_000,
            WrongConnectionConfirmMs = 0,
            ShortCircuitConfirmMs = 0,
            UseTestPointer = true
        };
        ProductModel model = Model(("PAIR-A", new[] { 1, 86 }), ("PAIR-B", new[] { 2, 87 }));
        TestViewModel vm = CreateTestViewModel(production, out FakeBoard board);
        vm.SetModel(model);
        vm.StartProductionTestAsync().GetAwaiter().GetResult();

        int threadCountBefore = Process.GetCurrentProcess().Threads.Count;
        int handleCountBefore = Process.GetCurrentProcess().HandleCount;
        long memoryBefore = GC.GetTotalMemory(forceFullCollection: true);

        for (int cycle = 1; cycle <= 500; cycle++)
        {
            long seq = 1000 + cycle;
            switch (cycle % 5)
            {
                case 0:
                    board.Publish(FrameSeq(seq, (1, new[] { 86 }), (2, new[] { 87 })));
                    break;
                case 1:
                    board.Publish(FrameSeq(seq, (1, Array.Empty<int>())));
                    break;
                case 2:
                    board.Publish(FrameSeq(seq, (1, new[] { 86, 87 }), (2, new[] { 87 })));
                    break;
                case 3:
                    board.Publish(FrameSeq(
                        seq,
                        Enumerable.Range(10, 20)
                            .Select(source => (source, new[] { 1 }))
                            .ToArray()));
                    break;
                default:
                    board.Publish(FrameSeq(seq));
                    break;
            }
        }

        long memoryAfter = GC.GetTotalMemory(forceFullCollection: true);
        int threadCountAfter = Process.GetCurrentProcess().Threads.Count;
        int handleCountAfter = Process.GetCurrentProcess().HandleCount;

        Assert(board.FramesReceived >= 500, "Stress: FramesReceived keeps increasing through 500 cycles");
        Assert(vm.ProductionFramesProcessed >= 500, "Stress: Probe/fault frames are not suppressed before TestEngine");
        Assert(board.IsScanning, "Stress: Product/probe processing does not stop D2XX scan");
        Assert(board.Commands.Count(command => command == "START") <= 1,
            "Stress: logical cycles reuse one healthy production scan");
        Assert(threadCountAfter <= threadCountBefore + 4,
            $"Stress: thread count remains bounded ({threadCountBefore} -> {threadCountAfter})");
        Assert(handleCountAfter <= handleCountBefore + 16,
            $"Stress: handle count remains bounded ({handleCountBefore} -> {handleCountAfter})");
        Assert(memoryAfter <= memoryBefore + (32L * 1024 * 1024),
            $"Stress: managed memory remains bounded ({memoryBefore} -> {memoryAfter})");
        Assert(vm.Faults.Count(row => row.Kind == FaultKind.Probe && row.Io == 1) <= 1,
            "Stress: Probe display does not duplicate the same IO row");
        Assert(!vm.IsDeviceFault, "Stress: no DeviceFault/deadlock from mixed scan/probe/fault frames");
    }

    private static void TestThtColumnSemantics()
    {
        var production = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50,
            PassMarkingToJigDelayMs = 0,
            ProductSettleTimeMs = 0,
            WrongConnectionConfirmMs = 0,
            ShortCircuitConfirmMs = 0
        };

        ProductModel supplied = TopologyModel(
            new Terminal(1, "1", "1", "2", "~1", "B"),
            new Terminal(2, "1", "2", "2", "~1", "B"));
        Assert(supplied.Connectors.Count == 1 &&
               supplied.Connectors[0].ConnectorId == "1" &&
               supplied.Connectors[0].DeclaredPinCount == 2 &&
               supplied.Connectors[0].Pins.Count == 2 &&
               supplied.Connectors[0].Pins.Any(pin => pin.LocalPinNumber == "1" && pin.PhysicalIo == 1) &&
               supplied.Connectors[0].Pins.Any(pin => pin.LocalPinNumber == "2" && pin.PhysicalIo == 2),
            "Connector/Pin metadata remains separate from tester I/O");
        Assert(supplied.Nets.Count == 1 &&
               supplied.Nets[0].Name == "~1" &&
               supplied.Nets[0].IoNumbers.SequenceEqual([1, 2]),
            "Supplied two-row THT builds one string WireName network");

        using TestEngine suppliedEngine = CreateEngine(out _, production);
        suppliedEngine.SetModel(supplied);
        suppliedEngine.ProcessFrame(FrameSeq(1));
        Assert(suppliedEngine.ExpectedNetCount == 1 &&
               suppliedEngine.GetPassGateDiagnostics().PassedNetCount == 0 &&
               !suppliedEngine.ContinuityPassed &&
               !suppliedEngine.HasWiringFault,
            "Missing expected WireName network is live progress, not product FAIL");
        suppliedEngine.ProcessFrame(FrameSeq(2, (2, new[] { 1 })));
        FaultRow[] suppliedMapped = suppliedEngine.BuildRows().Where(row => row.WireName == "~1").ToArray();
        Assert(suppliedEngine.GetPassGateDiagnostics().PassedNetCount == 1 &&
               suppliedEngine.ContinuityPassed &&
               !suppliedEngine.HasWiringFault &&
               suppliedMapped.Length == 0,
            "IO1/IO2 pass removes connected endpoint rows from production table");

        foreach (string wireName in new[] { "1", "2", "BG1", "BG2", "EA1", "MC01", "~1", "A01", "B/G1" })
        {
            ProductModel model = TopologyModel(
                new Terminal(1, "C1", "1", "2", wireName),
                new Terminal(20, "C2", "7", "8", wireName));
            Assert(model.Nets.Count == 1 &&
                   model.Nets[0].Name == wireName &&
                   model.Nets[0].IoNumbers.SequenceEqual([1, 20]),
                $"WireName '{wireName}' remains a string network identifier");
        }

        ProductModel stringDistinct = TopologyModel(
            new Terminal(1, "C1", "1", "2", "1"),
            new Terminal(2, "C1", "2", "2", "01"),
            new Terminal(3, "C1", "3", "3", "MC01"),
            new Terminal(4, "C1", "4", "4", "MC1"),
            new Terminal(5, "C1", "5", "5", "BG1"),
            new Terminal(6, "C1", "6", "6", "BG01"));
        Assert(stringDistinct.Nets.Count == 0,
            "Different WireName strings are not merged by numeric or invented normalization");

        using TestEngine differentWireEngine = CreateEngine(out _, production);
        differentWireEngine.SetModel(TopologyModel(
            new Terminal(1, "1", "1", "2", "1"),
            new Terminal(2, "1", "2", "2", "2")));
        differentWireEngine.ProcessFrame(FrameSeq(3, (1, new[] { 2 })));
        PassGateDiagnostics differentWireGate = differentWireEngine.GetPassGateDiagnostics();
        Assert(!differentWireEngine.ContinuityPassed &&
               differentWireGate.PassedNetCount == 0 &&
               differentWireGate.WrongCandidateCount + differentWireGate.ShortCandidateCount > 0,
            "Same connector and PinCount do not make different WireNames a valid connection");

        using TestEngine samePinDifferentConnector = CreateEngine(out _, production);
        samePinDifferentConnector.SetModel(TopologyModel(
            new Terminal(1, "1", "1", "10", "A"),
            new Terminal(11, "2", "1", "20", "B")));
        samePinDifferentConnector.ProcessFrame(FrameSeq(4, (1, new[] { 11 })));
        PassGateDiagnostics samePinGate = samePinDifferentConnector.GetPassGateDiagnostics();
        Assert(!samePinDifferentConnector.ContinuityPassed &&
               samePinGate.WrongCandidateCount + samePinGate.ShortCandidateCount > 0,
            "Same local Pin number across connectors is unrelated unless WireName matches");

        using TestEngine acrossConnector = CreateEngine(out _, production);
        acrossConnector.SetModel(TopologyModel(
            new Terminal(1, "1", "1", "10", "ABC"),
            new Terminal(40, "2", "8", "20", "ABC")));
        acrossConnector.ProcessFrame(FrameSeq(5, (40, new[] { 1 })));
        Assert(acrossConnector.ExpectedNetCount == 1 && acrossConnector.ContinuityPassed,
            "Same WireName across different connectors is one valid electrical network");

        using TestEngine threeEndpoint = CreateEngine(out _, production);
        threeEndpoint.SetModel(TopologyModel(
            new Terminal(1, "1", "1", "10", "A"),
            new Terminal(5, "1", "5", "10", "A"),
            new Terminal(20, "2", "4", "20", "A")));
        threeEndpoint.ProcessFrame(FrameSeq(6, (1, new[] { 5 }), (5, new[] { 20 })));
        Assert(threeEndpoint.ExpectedNetCount == 1 && threeEndpoint.ContinuityPassed,
            "Three endpoints with the same WireName are one connected-component network, not three networks");

        ProductModel pinCountOnly = TopologyModel(
            new Terminal(1, "1", "1", "2", "A"),
            new Terminal(2, "1", "2", "2", "B"));
        Assert(pinCountOnly.Nets.Count == 0,
            "Connector PinCount metadata does not create electrical networks");

        ProductModel blankWire = TopologyModel(
            new Terminal(1, "1", "1", "2", ""),
            new Terminal(2, "1", "2", "2", ""));
        Assert(blankWire.Nets.Count == 0 && blankWire.Connectors.Single().DeclaredPinCount == 2,
            "Blank WireName rows do not form one giant blank network");

        ProductModel mismatch = TopologyModel(
            new Terminal(1, "1", "1", "10", "M"),
            new Terminal(2, "1", "2", "12", "M"));
        Assert(mismatch.TopologyWarnings.Any(warning =>
                warning.Contains("MODEL_WARNING_CONNECTOR_PINCOUNT_MISMATCH", StringComparison.Ordinal)),
            "Connector PinCount mismatch is logged as model warning, not used as topology");

        TestViewModel vm = CreateTestViewModel(production);
        vm.SetModel(supplied);
        Assert(vm.ExpectedNetworkCount == 1 && vm.ProductionEnabled,
            "Production/Master UI uses the same corrected WireName topology source of truth");

        ProductModel wh322244Extract = TopologyModel(
            new Terminal(1, "1", "1", "43", "BG01"),
            new Terminal(2, "1", "2", "43", "BG02"),
            new Terminal(3, "1", "3", "43", "BF03"),
            new Terminal(4, "1", "4", "43", ""),
            new Terminal(5, "1", "5", "43", ""),
            new Terminal(16, "1", "16", "43", "BG16"),
            new Terminal(34, "1", "34", "43", "NUT01"),
            new Terminal(35, "1", "35", "43", "NUT01"),
            new Terminal(43, "1", "43", "43", "TAIL43"),
            new Terminal(44, "2", "1", "6", "FH01"),
            new Terminal(49, "2", "6", "6", "TAIL49"),
            new Terminal(79, "7", "1", "30", "BG16"),
            new Terminal(86, "7", "8", "30", "BG01"),
            new Terminal(87, "7", "9", "30", "BG02"),
            new Terminal(122, "8", "14", "34", "BF03"));

        Assert(PinByIo(wh322244Extract, 1) is { Connector: "1", PinNumber: "1", WireName: "BG01" } &&
               PinByIo(wh322244Extract, 2) is { Connector: "1", PinNumber: "2", WireName: "BG02" } &&
               PinByIo(wh322244Extract, 43) is { Connector: "1", PinNumber: "43" } &&
               PinByIo(wh322244Extract, 44) is { Connector: "2", PinNumber: "1", WireName: "FH01" } &&
               PinByIo(wh322244Extract, 49) is { Connector: "2", PinNumber: "6" } &&
               PinByIo(wh322244Extract, 79) is { Connector: "7", PinNumber: "1", WireName: "BG16" } &&
               PinByIo(wh322244Extract, 86) is { Connector: "7", PinNumber: "8", WireName: "BG01" } &&
               PinByIo(wh322244Extract, 87) is { Connector: "7", PinNumber: "9", WireName: "BG02" } &&
               PinByIo(wh322244Extract, 122) is { Connector: "8", PinNumber: "14", WireName: "BF03" },
            "WH322244 extracted THT rows preserve Connector/WireName/Physical I/O/Local Pin semantics");

        Assert(ConnectorPinCount(wh322244Extract, "1") == 43 &&
               ConnectorPinCount(wh322244Extract, "2") == 6 &&
               ConnectorPinCount(wh322244Extract, "7") == 30 &&
               ConnectorPinCount(wh322244Extract, "8") == 34,
            "WH322244 connector pin counts remain connector metadata");

        Assert(NetIos(wh322244Extract, "BG01").SequenceEqual([1, 86]) &&
               NetIos(wh322244Extract, "BG02").SequenceEqual([2, 87]) &&
               NetIos(wh322244Extract, "BF03").SequenceEqual([3, 122]) &&
               NetIos(wh322244Extract, "BG16").SequenceEqual([16, 79]) &&
               NetIos(wh322244Extract, "NUT01").SequenceEqual([34, 35]),
            "WH322244 extracted networks are grouped by string WireName only");

        const string multiPartText =
            "파트번호\t파트명\tECO\tNCO\tALC\n" +
            "PART-A\tPRODUCT A\tECO-A\tNCO-A\tALC-A\n" +
            "PART-B\tPRODUCT B\tECO-B\tNCO-B\tALC-B\n\n" +
            "번 호\t커넥터\t핀 수\n" +
            "1\tCN1\t2\n\n" +
            "커넥터\t선이름\tI/O\t핀번호\n" +
            "CN1\tW1\t1\t1\n" +
            "CN1\tW1\t2\t2\n\n" +
            "선이름\t선연결\t굵기\t색깔\n" +
            "W1\t\t0.5\tR";
        string multiPartRoot = Path.Combine(
            Path.GetTempPath(), "JBZMultiPartThtTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(multiPartRoot);
        try
        {
            string multiPartPath = Path.Combine(multiPartRoot, "multi-part.tht");
            File.WriteAllBytes(multiPartPath, BuildMinimalThtFile(multiPartText));
            IReadOnlyList<ProductModel> parts = new ThtModelParser().LoadAll(multiPartPath);
            Assert(parts.Count == 2 &&
                   parts[0].PartNumber == "PART-A" && parts[0].ProductName == "PRODUCT A" &&
                   parts[1].PartNumber == "PART-B" && parts[1].ProductName == "PRODUCT B" &&
                   parts.All(part => part.Nets.Single().IoNumbers.SequenceEqual([1, 2])) &&
                   PartSelectionWindow.PartKey(parts[0]) != PartSelectionWindow.PartKey(parts[1]),
                "Multi-Part THT returns one explicit candidate per Part row without inferring topology conversion");
        }
        finally
        {
            Directory.Delete(multiPartRoot, recursive: true);
        }
    }

    private static void TestRelayOrdering()
    {
        using TestEngine engine = CreateEngine(out FakeBoard board);
        engine.SetModel(Model(("PAIR", new[] { 1, 18 })));
        ScanFrame passFrame = Frame((1, new[] { 18 }));
        engine.ProcessFrame(passFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(passFrame);
        bool ok = engine.CompletePassAsync([]).GetAwaiter().GetResult();
        Assert(ok, "PASS relay workflow accepted");
        Assert(board.Commands.Count(command => command == "SET:2") == 1, "PASS R2 exactly once");
        Assert(board.Commands.Count(command => command == "SET:1") == 1, "PASS R1 exactly once");
        Assert(board.Commands.IndexOf("SET:2") < board.Commands.IndexOf("SET:1"), "PASS R2 before R1");
        Assert(board.Commands.Skip(board.Commands.IndexOf("SET:2") + 1).TakeWhile(c => c != "SET:1").Contains("OFF"), "R2 OFF before R1 ON");

        board.Commands.Clear();
        engine.EjectFaultProductAsync().GetAwaiter().GetResult();
        Assert(board.Commands.Count(command => command == "SET:1") == 1, "FAIL R1 exactly once");
        Assert(!board.Commands.Contains("SET:2"), "FAIL never marks R2");
        Assert(board.Commands.Last() == "OFF", "FAIL ends OFF");

        using TestEngine latchedLeakEngine = CreateEngine(out FakeBoard latchedLeakBoard);
        latchedLeakEngine.SetModel(Model(("LEAK-PAIR", new[] { 1, 18 })));
        latchedLeakEngine.ProcessFrame(passFrame);
        latchedLeakEngine.ProcessFrame(Frame());
        Assert(!latchedLeakEngine.ContinuityPassed,
            "Trailing empty STOP frame can clear live continuity after Leak starts");
        bool latchedLeakPass = latchedLeakEngine.CompletePassAsync(
            [],
            continuityAlreadyValidated: true).GetAwaiter().GetResult();
        Assert(latchedLeakPass && latchedLeakBoard.Commands.Contains("SET:1"),
            "Leak PASS uses the pre-Leak validated continuity latch instead of becoming a false product FAIL");

        board.Commands.Clear();
        using (var cts = new CancellationTokenSource(10))
        {
            bool canceled = false;
            try
            {
                engine.PulseJigRelayAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            Assert(canceled, "Relay pulse cancellation is observed");
            Assert(board.Commands.Contains("SET:1") && board.Commands.Last() == "OFF",
                "Relay cancellation still ends with safe OFF");
        }

        board.Commands.Clear();
        board.ThrowOnSetRelay = true;
        bool failedSet = false;
        try
        {
            engine.PulseJigRelayAsync().GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            failedSet = true;
        }
        finally
        {
            board.ThrowOnSetRelay = false;
        }

        Assert(failedSet && board.Commands.Last() == "OFF",
            "Relay set exception still forces safe OFF");

        var noMarkingProduction = new ProductionSettings
        {
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50,
            PassMarkingToJigDelayMs = 0,
            ProductSettleTimeMs = 0,
            PassMarkingRelayEnabled = false
        };
        using TestEngine noMarkingEngine = CreateEngine(out FakeBoard noMarkingBoard, noMarkingProduction);
        noMarkingEngine.SetModel(Model(("PAIR", new[] { 1, 18 })));
        noMarkingEngine.ProcessFrame(passFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        noMarkingEngine.ProcessFrame(passFrame);
        bool noMarkingOk = noMarkingEngine.CompletePassAsync([]).GetAwaiter().GetResult();
        Assert(noMarkingOk, "PASS relay workflow accepts disabled marking option");
        Assert(!noMarkingBoard.Commands.Contains("SET:2") &&
               noMarkingBoard.Commands.Count(command => command == "SET:1") == 1,
            "Disabled PASS marking skips R2 and still opens JIG once");

        var reversedFaultJigProduction = new ProductionSettings
        {
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50,
            RelayWiringMode = 1,
            JigEjectRelayEnabled = true,
            PassMarkingRelayEnabled = true
        };
        using TestEngine reversedFaultJigEngine = CreateEngine(
            out FakeBoard reversedFaultJigBoard,
            reversedFaultJigProduction);
        reversedFaultJigEngine.EjectFaultProductAsync().GetAwaiter().GetResult();
        Assert(reversedFaultJigBoard.Commands.Count(command => command == "SET:2") == 1 &&
               !reversedFaultJigBoard.Commands.Contains("SET:1") &&
               reversedFaultJigBoard.Commands.Last() == "OFF",
            "FAIL confirmation pulses only the configured physical JIG relay and never runs the PASS sequence");
        reversedFaultJigBoard.Commands.Clear();
        reversedFaultJigEngine.EjectMasterSampleAsync().GetAwaiter().GetResult();
        Assert(reversedFaultJigBoard.Commands.Count(command => command == "SET:2") == 1 &&
               !reversedFaultJigBoard.Commands.Contains("SET:1"),
            "Reversed machine ejects Master on physical JIG R2 without pulsing physical MARKING R1");

        var reversedRelayProduction = new ProductionSettings
        {
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50,
            PassMarkingToJigDelayMs = 0,
            ProductSettleTimeMs = 0,
            RelayWiringMode = 1
        };
        using TestEngine reversedRelayEngine = CreateEngine(out FakeBoard reversedRelayBoard, reversedRelayProduction);
        reversedRelayEngine.SetModel(Model(("PAIR", new[] { 1, 18 })));
        reversedRelayEngine.ProcessFrame(passFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        reversedRelayEngine.ProcessFrame(passFrame);
        bool reversedRelayOk = reversedRelayEngine.CompletePassAsync([]).GetAwaiter().GetResult();
        Assert(reversedRelayOk, "PASS relay workflow accepts reversed R1 MARKING / R2 JIG wiring");
        Assert(reversedRelayBoard.Commands.IndexOf("SET:1") >= 0 &&
               reversedRelayBoard.Commands.IndexOf("SET:2") >= 0 &&
               reversedRelayBoard.Commands.IndexOf("SET:1") < reversedRelayBoard.Commands.IndexOf("SET:2"),
            "Reversed machine still MARKS on R1 before opening JIG on R2");
    }

    private static void TestHistory()
    {
        Assert(
            ProgramIdentityService.BuildHtdrvName() ==
            $"JBZUniversalTester V{ProgramIdentityService.VersionText}",
            "HtdrvName is exactly the current software name and release version");

        string root = Path.Combine(Path.GetTempPath(), "JBZSelfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            DateTime finished = new(2026, 8, 9, 14, 7, 8, DateTimeKind.Local);
            var record = new TestHistoryRecord
            {
                Started = finished.AddSeconds(-5),
                Finished = finished,
                InstallStartedAt = finished.AddSeconds(-5),
                TestStartedAt = finished.AddSeconds(-3),
                ResultAt = finished,
                RemovalStartedAt = null,
                RemovedAt = null,
                InspectionType = HistoryInspectionType.Product,
                PartName = "PRODUCT",
                PartNumber = "NI375C1000",
                VehicleType = "NE N EV",
                Eco = "NE N EV",
                Alc = "NI375/C1000",
                LotNo = 2001,
                ProductionCounter = 321,
                Result = "PASS",
                Passed = true,
                ModelName = "MODEL-A",
                ModelFile = @"C:\Models\A.tht",
                HtdrvName = "JBZUniversalTester V15.2.0",
                LotText = "VOLVO Radio",
                InspectionTrace =
                    "14:07:08 회로검사:PASS 14:07:08~14:07:08 저항검사 [CH1: 100 Ω < 101.5 Ω < 110 Ω :PASS]",
                OpenCount = 0,
                BarcodeValue = "NI375C10002608092001",
                LabelProfile = "KS91",
                LabelTemplateType = LabelSettings.LargeTemplate,
                LabelPayload = "N\r\nNI375C10002608092001\r\nP1\r\n",
                PrintStatus = LabelPrintStatus.Printed.ToString(),
                CycleId = "history-cycle",
                Resistance = "CH1=101.5 Ω(PASS)",
                MeasuredResistance = 101.5,
                ResistanceMin = 100,
                ResistanceMax = 110
            };

            var store = new TestHistoryStore(Path.Combine(root, "history.db"));
            store.UpsertActiveCycle(
                record.CycleId,
                record.PartNumber,
                record.ModelFile,
                record.Started,
                "TEST_STARTED");
            Assert(CountActiveCycles(store.DatabasePath, record.CycleId) == 1,
                "Active production cycle is persisted before final PASS/FAIL commit");
            store.Add(record);
            Assert(CountActiveCycles(store.DatabasePath, record.CycleId) == 0,
                "Final result atomically clears its active cycle");
            store.UpsertActiveCycle(
                record.CycleId,
                record.PartNumber,
                record.ModelFile,
                record.Started,
                "RETRY");
            store.Add(record);
            Assert(CountActiveCycles(store.DatabasePath, record.CycleId) == 0,
                "Idempotent duplicate commit also clears a recreated active-cycle row");
            DateTime removalStarted = finished.AddMilliseconds(250);
            DateTime removedAt = removalStarted.AddSeconds(2);
            Assert(store.UpdateRemovalTiming(record.CycleId, removalStarted, null),
                "History removal start updates the immutable cycle row");
            Assert(store.UpdateRemovalTiming(record.CycleId, removalStarted.AddSeconds(1), removedAt),
                "History removal completion updates the immutable cycle row");
            IReadOnlyList<TestHistoryRecord> found = store.Search(new HistorySearchCriteria(
                finished.Date, finished.Date.AddDays(1), 2001, "NI375", "PASS"));
            Assert(found.Count == 1 && found[0].PartNumber == "NI375C1000" &&
                   found[0].VehicleType == "NE N EV" && found[0].ProductionCounter == 321 &&
                   found[0].LabelTemplateType == LabelSettings.LargeTemplate &&
                   found[0].InspectionType == HistoryInspectionType.Product &&
                   found[0].InstallDurationSeconds == 2 &&
                   found[0].TestDurationSeconds == 3 &&
                   found[0].RemovalDurationSeconds == 2 &&
                   found[0].RemovalStartedAt == removalStarted &&
                   found[0].RemovedAt == removedAt &&
                   found[0].LotText == "VOLVO Radio" &&
                   found[0].InspectionTrace.Contains("저항검사", StringComparison.Ordinal) &&
                   found[0].LabelPayload == record.LabelPayload,
                "SQLite search preserves 14-column values, phase trace and immutable print payload");

            string csv = Path.Combine(root, "history.csv");
            HistoryExportService.ExportCsv(csv, found);
            byte[] csvBytes = File.ReadAllBytes(csv);
            Assert(csvBytes.Length >= 3 && !(csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF),
                "ALL13 CSV uses CP949 without UTF-8 BOM");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string csvText = File.ReadAllText(csv, Encoding.GetEncoding(949));
            Assert(csvText.StartsWith(
                    "일 자,시 간,파 일,품 명,품 번,차 종,Lot,결 과,순 번,검 사 기 록,바코드,200 %,수입검사,프로그램\n",
                    StringComparison.Ordinal),
                "History CSV preserves the exact original 14-column Korean header");
            Assert(csvText.Contains(
                    "2026-08-09,14:07:05,A.tht,PRODUCT,NI375C1000,NE N EV,VOLVO Radio,합격,2001",
                    StringComparison.Ordinal) &&
                   csvText.Contains("장착 14:07:03~14:07:05(2.000초) 14:07:05 검사시작", StringComparison.Ordinal) &&
                   csvText.Contains("저항검사 [CH1: 100 Ω < 101.5 Ω < 110 Ω :PASS]", StringComparison.Ordinal) &&
                   csvText.Contains("탈거 14:07:08~14:07:10(2.000초)", StringComparison.Ordinal) &&
                   csvText.Contains(",NI375C10002608092001,,,JBZUniversalTester V15.2.0", StringComparison.Ordinal) &&
                   !csvText.Contains("N\r\nNI375C10002608092001", StringComparison.Ordinal),
                "Sample history CSV keeps three test phases, sequence and barcode without raw EPL payload");

            string xlsx = Path.Combine(root, "history.xlsx");
            HistoryExportService.ExportXlsx(xlsx, found);
            using ZipArchive archive = ZipFile.OpenRead(xlsx);
            string sheet = ReadEntry(archive, "xl/worksheets/sheet1.xml");
            string styles = ReadEntry(archive, "xl/styles.xml");
            Assert(sheet.Contains("<c r=\"A2\" s=\"2\"><v>", StringComparison.Ordinal) &&
                   sheet.Contains("<c r=\"B2\" s=\"4\"><v>", StringComparison.Ordinal),
                "XLSX date and time use separate native numeric cells");
            Assert(sheet.Contains("<c r=\"I2\"><v>2001</v></c>", StringComparison.Ordinal),
                "XLSX sequence uses the PASS LOT number");
            Assert(sheet.Contains("바코드", StringComparison.Ordinal) &&
                   sheet.Contains("<c r=\"K2\" t=\"inlineStr\"><is><t xml:space=\"preserve\">NI375C10002608092001</t>", StringComparison.Ordinal) &&
                   !sheet.Contains("N&#xD;", StringComparison.Ordinal) &&
                   sheet.Contains("autoFilter ref=\"A1:N2\"", StringComparison.Ordinal),
                "XLSX preserves Korean headers, barcode value and 14-column filter");
            Assert(styles.Contains("numFmtId=\"164\"", StringComparison.Ordinal) &&
                   styles.Contains("numFmtId=\"165\"", StringComparison.Ordinal) &&
                   styles.Contains("wrapText=\"1\"", StringComparison.Ordinal),
                "XLSX date, time and wrapped test-log styles");

            string historyXaml = File.ReadAllText(
                Path.Combine(Environment.CurrentDirectory, "Views", "HistoryPage.xaml"));
            string[] sampleHeaders =
            [
                "Ngày", "Thời gian", "File", "Tên sản phẩm", "Mã hàng", "Loại xe", "LOT",
                "Kết quả", "Số thứ tự", "Hồ sơ kiểm tra", "Mã vạch", "200 %",
                "Kiểm tra đầu vào", "Chương trình"
            ];
            int previousHeader = -1;
            foreach (string header in sampleHeaders)
            {
                int headerIndex = historyXaml.IndexOf($"Header=\"{header}\"", previousHeader + 1, StringComparison.Ordinal);
                Assert(headerIndex > previousHeader, $"History UI Vietnamese 14-column order: {header}");
                previousHeader = headerIndex;
            }
            Assert(historyXaml.Contains("CanUserResizeColumns=\"False\"", StringComparison.Ordinal) &&
                   historyXaml.Contains("CanUserReorderColumns=\"False\"", StringComparison.Ordinal) &&
                   historyXaml.Contains("CanUserSortColumns=\"False\"", StringComparison.Ordinal) &&
                   historyXaml.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Visible\"", StringComparison.Ordinal) &&
                   historyXaml.Contains("Header=\"Hồ sơ kiểm tra\" Width=\"1200\"", StringComparison.Ordinal),
                "History UI locks column layout and keeps the inspection record widest with horizontal scrolling");

            var failed = new TestHistoryRecord
            {
                Finished = finished,
                Passed = false,
                Result = "FAIL",
                FaultCode = "OPEN_CIRCUIT",
                FaultType = "DÂY CHƯA KẾT NỐI",
                FaultDetailsJson = JsonSerializer.Serialize(new[]
                {
                    new FaultDetail
                    {
                        Type = ProductFaultType.OpenCircuit,
                        ConnectorFrom = "CN1",
                        PinFrom = "4",
                        ConnectorTo = "CN3",
                        PinTo = "6",
                        WireColor = "WHITE"
                    }
                })
            };
            string customerCsv = Path.Combine(root, "customer-fault.csv");
            HistoryExportService.ExportCsv(customerCsv, [failed]);
            string customerText = File.ReadAllText(customerCsv, Encoding.GetEncoding(949));
            Assert(customerText.Contains(",불량,,", StringComparison.Ordinal) &&
                   customerText.Contains("단선 CN1-4↔CN3-6", StringComparison.Ordinal) &&
                   !customerText.Contains("OPEN CIRCUIT", StringComparison.Ordinal),
                "History CSV FAIL uses concise Korean fault detail and blank accepted LOT");

            var masterBad = new TestHistoryRecord
            {
                Started = finished,
                Finished = finished.AddSeconds(1),
                InstallStartedAt = finished,
                TestStartedAt = finished.AddMilliseconds(250),
                ResultAt = finished.AddSeconds(1),
                InspectionType = HistoryInspectionType.MasterBad,
                Passed = true,
                InspectionTrace = "14:07:09 회로검사:FAIL",
                FaultDetailsJson = failed.FaultDetailsJson
            };
            Assert(masterBad.IsMasterRecord &&
                   masterBad.ExportAcceptedLotNo is null &&
                   masterBad.ExportResultText == "합격" &&
                   masterBad.ExportBarcodeText.Length == 0 &&
                   masterBad.ExportTestLogText.Contains("회로검사:FAIL", StringComparison.Ordinal) &&
                   masterBad.ExportTestLogText.Contains("단선 CN1-4↔CN3-6", StringComparison.Ordinal),
                "MASTER BAD history is separate from production and preserves Korean fault evidence");

            var normalProduct = new TestHistoryRecord
            {
                Started = finished,
                Finished = finished.AddSeconds(1),
                TestStartedAt = finished,
                ResultAt = finished.AddSeconds(1),
                Passed = true,
                InspectionTrace = "14:07:09 회로검사:PASS"
            };
            var leakProduct = new TestHistoryRecord
            {
                Started = finished,
                Finished = finished.AddSeconds(6),
                TestStartedAt = finished,
                ResultAt = finished.AddSeconds(6),
                Passed = true,
                InspectionTrace =
                    "14:07:09 회로검사:PASS 14:07:10~14:07:14 기밀검사 " +
                    "[CH1/CN1: 92.3→92 Δ0.3≤20:PASS]"
            };
            Assert(normalProduct.ExportTestLogText.Contains("회로검사:PASS", StringComparison.Ordinal) &&
                   !normalProduct.ExportTestLogText.Contains("저항검사", StringComparison.Ordinal) &&
                   !normalProduct.ExportTestLogText.Contains("기밀검사", StringComparison.Ordinal) &&
                   record.ExportTestLogText.Contains("저항검사", StringComparison.Ordinal) &&
                   leakProduct.ExportTestLogText.Contains("기밀검사", StringComparison.Ordinal) &&
                   leakProduct.ExportTestLogText.Contains("CH1/CN1", StringComparison.Ordinal),
                "Normal, resistance and Leak products share one concise Korean inspection-record column");

            var wrongWiring = new FaultDetail
            {
                Type = ProductFaultType.WrongWiring,
                WireName = "W1",
                ConnectorFrom = "CN1",
                PinFrom = "03",
                ConnectorTo = "CN2",
                PinTo = "07",
                ActualConnectorFrom = "CN1",
                ActualPinFrom = "03",
                ActualConnectorTo = "CN3",
                ActualPinTo = "02"
            };
            Assert(
                KoreanHistoryFormatter.FormatFault(wrongWiring) ==
                "오배선 W1 정상 CN1-03↔CN2-07 실제 CN1-03↔CN3-02",
                "Wrong-wiring history names the expected and actual connector/pin in concise Korean");

            var resistanceFault = new FaultDetail
            {
                Type = ProductFaultType.ResistanceOutOfRange,
                WireName = "R-CH1",
                MeasuredResistance = 115.25,
                ResistanceMin = 100,
                ResistanceMax = 110
            };
            Assert(
                KoreanHistoryFormatter.FormatFault(resistanceFault) ==
                "저항불량 R-CH1 115.25Ω 기준 100~110Ω",
                "Resistance history keeps measured value and limits in concise Korean");

            var exportStore = new TestHistoryStore(Path.Combine(root, "history-export.db"));
            DateTime monthStart = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Local);
            exportStore.Add(new TestHistoryRecord
            {
                Started = monthStart.AddDays(2),
                Finished = monthStart.AddDays(2).AddSeconds(1),
                PartNumber = "PART-A",
                ModelFile = @"D:\Models\C.tht",
                Result = "PASS",
                Passed = true,
                CycleId = "export-part-a"
            });
            exportStore.Add(new TestHistoryRecord
            {
                Started = monthStart.AddDays(1),
                Finished = monthStart.AddDays(1).AddSeconds(1),
                PartNumber = "PART-Z",
                ModelFile = @"D:\Models\B.tht",
                Result = "PASS",
                Passed = true,
                CycleId = "export-part-z-b"
            });
            exportStore.Add(new TestHistoryRecord
            {
                Started = monthStart,
                Finished = monthStart.AddSeconds(1),
                PartNumber = "PART-Z",
                ModelFile = @"D:\Models\A.tht",
                Result = "PASS",
                Passed = true,
                CycleId = "export-part-z-a"
            });

            var monthlyCriteria = new HistorySearchCriteria(
                monthStart,
                monthStart.AddMonths(1).AddTicks(-1),
                null,
                string.Empty,
                "ALL",
                MaxRows: 1);
            IReadOnlyList<TestHistoryRecord> limitedRows = exportStore.Search(monthlyCriteria);
            IReadOnlyList<TestHistoryRecord> allExportRows = exportStore.SearchForExport(monthlyCriteria);
            Assert(limitedRows.Count == 1 && allExportRows.Count == 3,
                "History export is independent from the DataGrid row limit");
            Assert(allExportRows[0].PartNumber == "PART-A" &&
                   allExportRows[1].CycleId == "export-part-z-a" &&
                   allExportRows[2].CycleId == "export-part-z-b",
                "History export sorts by part number, test start time and stable Id");
            Assert(allExportRows[1].ExportModelFileName == "A.tht" &&
                   allExportRows[2].ExportModelFileName == "B.tht",
                "Changing A.tht to B.tht snapshots B.tht only for the new cycle");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestLabel()
    {
        var data = new LabelPrintData("PRODUCT", "NI375C1000", "NE N EV", "", "NI375/C1000", 2001,
            new DateTime(2024, 7, 15, 14, 7, 8));
        string epl = EplLabelService.BuildPassLabel(data, new LabelSettings());
        int part = epl.IndexOf("NI375C1000", StringComparison.Ordinal);
        int eco = epl.IndexOf("NE N EV", part + 1, StringComparison.Ordinal);
        int name = epl.IndexOf("PRODUCT", eco + 1, StringComparison.Ordinal);
        int serial = epl.IndexOf("2407152001WH", name + 1, StringComparison.Ordinal);
        int barcode = epl.IndexOf("NI375C10002407152001", serial + 1, StringComparison.Ordinal);
        Assert(part >= 0 && part < eco && eco < name && name < serial && serial < barcode, "ALL6 EPL value order");
    }

    private static void TestThtLabelAndLotLifecycle()
    {
        var unconfiguredPrinter = new LabelPrintService();
        LabelPrinterConnectionResult unconfiguredConnection = unconfiguredPrinter
            .ConnectAsync(new LabelSettings())
            .GetAwaiter()
            .GetResult();
        Assert(!unconfiguredConnection.Connected &&
               unconfiguredConnection.Message.Contains("Chưa chọn cổng COM", StringComparison.Ordinal),
            "Printer connect requires an explicitly configured saved COM port");
        unconfiguredPrinter.DisposeAsync().AsTask().GetAwaiter().GetResult();

        DateTime testedAt = new(2026, 8, 22, 10, 15, 30, DateTimeKind.Local);
        var data = new LabelPrintData(
            "VOLTAGE_6S",
            "BE331-G2000",
            "AE EV PE",
            "NCO-7",
            "SQDZQ7V7001",
            2044,
            testedAt,
            "AE EV PE",
            "SQDZQ7V7001",
            "cycle-a");
        const string template = "FR\"KS91\"\n?\n{PART_NUMBER}\n{PRODUCT_NAME}\n{VEHICLE_TYPE}\n{CUSTOMER_CODE}\n{LOT_NO}\n{TEST_DATE}\n{TEST_TIME}\n{CYCLE_ID}\nP1";
        string rendered = LabelTemplateRenderer.Render(template, data);
        Assert(rendered == "FR\"KS91\"\n?\nBE331-G2000\nVOLTAGE_6S\nAE EV PE\nSQDZQ7V7001\n2044\n20260822\n101530\ncycle-a\nP1",
            "Template variables render from immutable THT product snapshot");

        var resolverModel = new ProductModel
        {
            PartNumber = data.PartNumber,
            ProductName = data.PartName,
            Eco = data.Eco,
            VehicleType = data.VehicleType,
            CustomerCode = data.CustomerCode,
            LabelVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FACTORY"] = "WH",
                ["LINE"] = "KS91",
                ["PART_NUMBER"] = "MUST-NOT-OVERRIDE-MODEL"
            }
        };
        var resolverSettings = new LabelSettings { FormatName = "KS91" };
        IReadOnlyDictionary<string, string> resolved =
            LabelVariableResolver.Resolve(resolverModel, data, resolverSettings);
        string dynamicRendered = LabelTemplateRenderer.Render(
            "{part_number}|{FACTORY}|{LINE}|{DATE_YYMMDD}|{DATE_YYYYMMDD}|{TIME_HHMMSS}|$DATA$|$LOTNO$|$PARTNO|$PARTNAME",
            resolved,
            resolverModel.PartNumber,
            resolverSettings.FormatName);
        Assert(dynamicRendered ==
               "MUST-NOT-OVERRIDE-MODEL|WH|KS91|260822|20260822|101530|260822|2044|MUST-NOT-OVERRIDE-MODEL|VOLTAGE_6S",
            "Canonical, arbitrary THT, runtime date and verified legacy aliases render case-insensitively");
        AssertThrows<InvalidDataException>(
            () => LabelTemplateRenderer.Render(
                "N\n{ABC_NOT_DEFINED}\n$INTERFACE$\nP1",
                resolved,
                resolverModel.PartNumber,
                resolverSettings.FormatName),
            "Unknown canonical/SendSerial variables block printing before transport");

        resolverModel.LabelVariables["INTERFACE"] = "IF-A";
        string productA = LabelTemplateRenderer.Render(
            "{PART_NUMBER}|{INTERFACE}",
            LabelVariableResolver.Resolve(resolverModel, data, resolverSettings));
        var productBModel = new ProductModel
        {
            PartNumber = "PART-B",
            ProductName = "PRODUCT-B",
            LabelVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["INTERFACE"] = "IF-B"
            }
        };
        LabelPrintData productBData = data with { PartNumber = "PART-B", PartName = "PRODUCT-B" };
        string productB = LabelTemplateRenderer.Render(
            "{PART_NUMBER}|{INTERFACE}",
            LabelVariableResolver.Resolve(productBModel, productBData, resolverSettings));
        Assert(productA == "MUST-NOT-OVERRIDE-MODEL|IF-A" && productB == "PART-B|IF-B" && productA != productB,
            "Two THT-derived product snapshots render different labels without product-specific code");

        DateTime lotClock = new(2026, 8, 29, 8, 0, 0);
        var settings = new ProductionSettings { LotNo = 2044, LotNoDate = "2026-08-29" };
        int persistCount = 0;
        var lots = new LotSequenceService(settings, _ => persistCount++, () => lotClock);
        long cycleA = lots.ReserveForCycle("cycle-a");
        Assert(cycleA == 2044 && lots.ReserveForCycle("cycle-a") == 2044,
            "Duplicate PASS callback keeps the same reserved LOT");
        Assert(lots.NextLot == 2044 && persistCount == 0,
            "Reservation/printer failure does not advance persisted LOT");
        Assert(lots.TryCommitSuccessfulPrint("cycle-a", 2044, out string errorA) && errorA.Length == 0,
            "Successful print commits reserved LOT");
        Assert(lots.NextLot == 2045 && persistCount == 1,
            "Successful print advances and persists next LOT exactly once");

        long cycleB = lots.ReserveForCycle("cycle-b");
        Assert(cycleB == 2045 && lots.NextLot == 2045,
            "Next PASS receives the next LOT without early commit");
        Assert(!lots.TryCommitSuccessfulPrint("cycle-b", 2046, out _),
            "Mismatched LOT cannot commit");
        Assert(lots.NextLot == 2045,
            "Failed/mismatched print leaves next LOT unchanged for retry");

        var restartedSettings = new ProductionSettings
        {
            LotNo = settings.LotNo,
            LotNoDate = settings.LotNoDate,
            LotSettingsByProduct = settings.LotSettingsByProduct.ToDictionary(
                pair => pair.Key,
                pair => new ProductLotSettings
                {
                    StartLotNo = pair.Value.StartLotNo,
                    LotNo = pair.Value.LotNo,
                    LotNoDate = pair.Value.LotNoDate
                },
                StringComparer.OrdinalIgnoreCase)
        };
        var restarted = new LotSequenceService(restartedSettings, _ => { }, () => lotClock);
        Assert(restarted.NextLot == 2045,
            "Restart resumes from last successfully committed LOT");

        lotClock = lotClock.AddDays(1);
        Assert(restarted.StartLot == 2044 &&
               restarted.NextLot == 2044 &&
               restartedSettings.LotNoDate == "2026-08-30",
            "New production day resets next LOT to the per-product starting LOT");

        var migratedSettings = new ProductionSettings { LotNo = 9876 };
        int migrationPersistCount = 0;
        var migratedLots = new LotSequenceService(
            migratedSettings,
            _ => migrationPersistCount++,
            () => lotClock);
        Assert(
            migratedLots.NextLot == 9876 &&
            migratedSettings.LotNoDate == "2026-08-30" &&
            migrationPersistCount == 0,
            "First upgrade stamps LOTNO date in memory without constructor disk I/O or discarding the current sequence");

        var perProductSettings = new ProductionSettings
        {
            LotNoDate = "2026-08-30",
            LotSettingsByProduct = new Dictionary<string, ProductLotSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["PART-2000"] = new() { StartLotNo = 2000, LotNo = 2000, LotNoDate = "2026-08-30" },
                ["PART-7000"] = new() { StartLotNo = 7000, LotNo = 7000, LotNoDate = "2026-08-30" },
                ["PART-5000"] = new() { StartLotNo = 5000, LotNo = 5000, LotNoDate = "2026-08-30" }
            }
        };
        var perProductLots = new LotSequenceService(perProductSettings, _ => { }, () => lotClock);
        perProductLots.SelectProduct("PART-2000", migrateCurrentLotIfMissing: false);
        Assert(perProductLots.NextLot == 2000, "PART-2000 loads its own starting LOT");
        Assert(perProductLots.TryCommitSuccessfulPrint(
                "part-2000-cycle",
                perProductLots.ReserveForCycle("part-2000-cycle"),
                out _),
            "PART-2000 commits its own LOT");
        perProductLots.SelectProduct("PART-7000", migrateCurrentLotIfMissing: false);
        Assert(perProductLots.NextLot == 7000, "PART-7000 loads its own starting LOT");
        perProductLots.SelectProduct("PART-5000", migrateCurrentLotIfMissing: false);
        Assert(perProductLots.NextLot == 5000, "PART-5000 loads its own starting LOT");
        perProductLots.SelectProduct("PART-2000", migrateCurrentLotIfMissing: false);
        Assert(perProductLots.NextLot == 2001,
            "Switching back restores the previously advanced LOT for that product");
        lotClock = lotClock.AddDays(1);
        Assert(perProductLots.NextLot == 2000,
            "PART-2000 returns to its own starting LOT when the production date changes");

        string root = Path.Combine(Path.GetTempPath(), "JBZLabelProfileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string templatePath = Path.Combine(root, "EXTERNAL_VERIFIED.epl");
            string rawDestination = Path.Combine(root, "raw-printer.bin");
            File.WriteAllText(templatePath, """
                N
                PART={PART_NUMBER}
                NAME={PRODUCT_NAME}
                VEHICLE={VEHICLE_TYPE}
                CUSTOMER={CUSTOMER_CODE}
                ECO={ECO}
                NCO={NCO}
                ALC={ALC}
                LOT={LOT_NO}
                DATE={TEST_DATE}
                TIME={TEST_TIME}
                BAR={BARCODE}
                P1
                """ + "\n", new UTF8Encoding(false));

            var history = new TestHistoryRecord
            {
                Finished = testedAt,
                PartName = data.PartName,
                PartNumber = data.PartNumber,
                Eco = data.Eco,
                Nco = data.Nco,
                Alc = data.Alc,
                LotNo = data.LotNo,
                ModelName = "MODEL-LABEL",
                ModelFile = "MODEL-LABEL.tht",
                CycleId = data.CycleId
            };
            var model = new ProductModel
            {
                ProductName = data.PartName,
                PartNumber = data.PartNumber,
                VehicleType = data.VehicleType,
                CustomerCode = data.CustomerCode,
                Eco = data.Eco,
                Nco = data.Nco,
                Alc = data.Alc,
                LabelTemplate = new LabelTemplateDefinition(
                    ProfileId: "EXTERNAL_VERIFIED",
                    BarcodeTemplate: "{PART_NUMBER}-{LOT_NO}")
            };
            var labelSettings = new LabelSettings
            {
                FormatName = "EXTERNAL_VERIFIED",
                TemplatePath = templatePath,
                RawDestination = rawDestination,
                EncodingName = "us-ascii",
                Copies = 1
            };

            LabelPrintRequest request = LabelPrintRequest.Capture(history, model, labelSettings);
            string expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LabelGolden", "EXTERNAL_VERIFIED_expected.epl"))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert(request.Payload.Replace("\r\n", "\n", StringComparison.Ordinal) == expected,
                "External verified profile matches golden EPL byte layout");

            LabelPrintTransportResult printed = new LabelPrintService()
                .PrintPassLabelAsync(request)
                .GetAwaiter()
                .GetResult();
            Assert(printed.Printed && File.ReadAllBytes(rawDestination).SequenceEqual(Encoding.ASCII.GetBytes(request.Payload)),
                "Raw/LPT transport writes the rendered payload byte-for-byte");

            File.WriteAllText(templatePath, "N\nRELOADED={PART_NUMBER}\nP1\n", new UTF8Encoding(false));
            LabelPrintRequest reloaded = LabelPrintRequest.Capture(history, model, labelSettings);
            Assert(reloaded.Payload == "N\nRELOADED=BE331-G2000\nP1\n",
                "External template is re-read after editor save without app/model restart");

            string helperPrintFile = Path.Combine(root, "helper-print.txt");
            labelSettings.RawDestination = string.Empty;
            labelSettings.ExternalHelperPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            labelSettings.ExternalHelperArgument = "/d /c exit 0";
            labelSettings.ExternalPrintFile = helperPrintFile;
            LabelPrintRequest helperRequest = LabelPrintRequest.Capture(history, model, labelSettings);
            LabelPrintTransportResult helperResult = new LabelPrintService()
                .PrintPassLabelAsync(helperRequest)
                .GetAwaiter()
                .GetResult();
            Assert(helperResult.Printed &&
                   File.ReadAllBytes(helperPrintFile).SequenceEqual(Encoding.ASCII.GetBytes(helperRequest.Payload)),
                "External helper transport writes print file and requires successful helper exit");

            File.WriteAllText(templatePath, "N\n$KIND\nP1\n", new UTF8Encoding(false));
            AssertThrows<InvalidDataException>(
                () => LabelPrintRequest.Capture(history, model, labelSettings),
                "Unverified legacy token is blocked instead of guessed");

            labelSettings.TemplatePath = string.Empty;
            model.LabelTemplate = new LabelTemplateDefinition(ProfileId: "KETQU");
            AssertThrows<InvalidDataException>(
                () => LabelPrintRequest.Capture(history, model, labelSettings),
                "KETQU without original template/trace is marked NEEDS_ORIGINAL_TRACE");

            model = ParseThtLabelModelText("12000/20430/1");
            labelSettings.ExternalHelperPath = string.Empty;
            labelSettings.ExternalHelperArgument = string.Empty;
            labelSettings.ExternalPrintFile = "print.txt";
            history.LotNo = 2001;
            history.Finished = new DateTime(2026, 8, 27, 8, 9, 10);
            labelSettings.TemplateType = LabelSettings.LargeTemplate;
            LabelPrintRequest largeSuffix1 = LabelPrintRequest.Capture(history, model, labelSettings);
            Assert(model.Alc == "12000/20430/1" &&
                   largeSuffix1.Data.Barcode == "KL375C100026082720011" &&
                   largeSuffix1.Payload.Contains("26082720011WH", StringComparison.Ordinal) &&
                   largeSuffix1.Payload.Contains("KL375C100026082720011", StringComparison.Ordinal),
                "TEM_TO reads ALC /1 from THT and appends 1 immediately after LOTNO");

            const string editedLargeTemplate = "N\nEDITED={PART_NUMBER}\nP1\n";
            BuiltInLabelTemplateStore.SaveOverride(
                labelSettings,
                LabelSettings.LargeTemplate,
                editedLargeTemplate);
            LabelPrintRequest editedLarge = LabelPrintRequest.Capture(history, model, labelSettings);
            Assert(editedLarge.Payload == "N\nEDITED=KL375C1000\nP1\n" &&
                   BuiltInLabelTemplateStore.LoadOverride(labelSettings, LabelSettings.LargeTemplate) == editedLargeTemplate,
                "Built-in label editor override is Base64-backed and used without a Labels directory");
            BuiltInLabelTemplateStore.ClearOverride(labelSettings, LabelSettings.LargeTemplate);

            LabelPrintData suffix2Data = largeSuffix1.Data with { Alc = "12000/20430/2" };
            LabelIdentity suffix2Identity = EplLabelService.BuildIdentity(suffix2Data);
            LabelIdentity noSuffixIdentity = EplLabelService.BuildIdentity(
                suffix2Data with { Alc = "12000/20430" });
            Assert(suffix2Identity.SerialText == "26082720012WH" &&
                   suffix2Identity.BarcodeValue == "KL375C100026082720012" &&
                   noSuffixIdentity.SerialText == "2608272001WH" &&
                   noSuffixIdentity.BarcodeValue == "KL375C10002608272001",
                "TEM_TO supports ALC /2 and keeps legacy output when ALC has no suffix");

            var qrModel = new ProductModel
            {
                ModelName = "K32000-22401R-260224",
                PartNumber = "K32000-22402",
                ProductName = "WIRING ASSY-MAIN",
                Alc = "HE EV",
                Eco = "HE EV",
                VehicleType = "HE EV",
                SourcePath = Path.Combine(
                    Path.GetTempPath(),
                    "K32000-22401R-260224.tht"),
                LabelTemplate = new LabelTemplateDefinition(ProfileId: LabelSettings.SmallQrTemplate)
            };
            history.LotNo = 4;
            history.Finished = new DateTime(2026, 8, 27, 8, 9, 10);
            labelSettings.TemplateType = LabelSettings.SmallQrTemplate;
            LabelPrintRequest qrLabel = LabelPrintRequest.Capture(history, qrModel, labelSettings);
            IReadOnlyDictionary<string, string> qrValues =
                LabelVariableResolver.Resolve(qrModel, qrLabel.Data, labelSettings);
            string normalizedQrPayload = qrLabel.Payload.Replace("\r\n", "\n", StringComparison.Ordinal);
            byte[] qrPayloadBytes = Encoding.ASCII.GetBytes(qrLabel.Payload);
            int qrCrLfCount = qrLabel.Payload.Count(character => character == '\n');
            int qrBareLfCount = qrPayloadBytes
                .Select((value, index) => (value, index))
                .Count(item => item.value == (byte)'\n' &&
                               (item.index == 0 || qrPayloadBytes[item.index - 1] != (byte)'\r'));
            Assert(LabelProfileResolver.NormalizeTemplateType("TEM_BE_QR") == LabelSettings.SmallQrTemplate &&
                   qrLabel.Profile.Id == LabelSettings.SmallQrTemplate &&
                   qrLabel.Profile.Mode == LabelPrintMode.ExternalTemplate &&
                   qrValues["LOT_NO"] == "0004" &&
                   qrValues["LOT_NO_3"] == "004" &&
                   qrValues["MODEL_FILE_NAME"] == "K32000-22401R-260224.tht" &&
                   qrValues["SMALL_QR_BARCODE"] == "K32000-22402,2608270004" &&
                   qrLabel.Data.Barcode == "K32000-22402,2608270004" &&
                   qrLabel.Data.BarcodePrint == "K32000-22402,2608270004" &&
                   normalizedQrPayload.Contains("b200,13,Q,S3,V00\",\"V05V06", StringComparison.Ordinal) &&
                   normalizedQrPayload.Contains(
                       "?\nK32000-22402\nWIRING ASSY-MAIN\nHE EV\nHE EV\n" +
                       "K32000-22401R-260224.tht\n260827\n0004\n004\nQ\n8\n27\nP1",
                       StringComparison.Ordinal),
                "TEM_BE_QR follows 60-15 EPL, reads product fields from THT and encodes PartNumber,yyMMddLOT4");
            Assert(qrCrLfCount > 0 && qrBareLfCount == 0 &&
                   qrLabel.Payload.EndsWith("P1\r\n", StringComparison.Ordinal) &&
                   !qrLabel.Payload.Contains("X180,0,4,620,100", StringComparison.Ordinal) &&
                   qrLabel.Payload.Contains("A450,72,0,2,1,1,N,V05\"-\"V06", StringComparison.Ordinal) &&
                   qrLabel.Payload.Contains("b200,13,Q,S3,V00\",\"V05V06", StringComparison.Ordinal),
                "TEM_BE_QR matches the original helper's stored-form variables, removes comment lines and terminates P1 with CRLF");

            history.LotNo = 2001;
            LabelPrintRequest qrLot2001 = LabelPrintRequest.Capture(history, qrModel, labelSettings);
            IReadOnlyDictionary<string, string> qrLot2001Values =
                LabelVariableResolver.Resolve(qrModel, qrLot2001.Data, labelSettings);
            Assert(qrLot2001Values["LOT_NO"] == "0001" &&
                   qrLot2001Values["LOT_NO_3"] == "001" &&
                   qrLot2001.Data.Barcode == "K32000-22402,2608270001" &&
                   qrLot2001.Payload.Contains(
                       "260827\r\n0001\r\n001\r\nQ\r\n8\r\n27\r\nP1\r\n",
                       StringComparison.Ordinal),
                "TEM_BE_QR converts internal LOT 2001 to original-label daily sequence 0001/001");

            labelSettings.TemplateType = LabelSettings.SmallTemplate;

            LabelPrintRequest SmallLabel(long lot, DateTime date)
            {
                history.LotNo = lot;
                history.Finished = date;
                return LabelPrintRequest.Capture(history, model, labelSettings);
            }

            LabelPrintRequest smallLabel = SmallLabel(1, new DateTime(2026, 8, 25, 8, 9, 10));
            IReadOnlyDictionary<string, string> smallValues =
                LabelVariableResolver.Resolve(model, smallLabel.Data, labelSettings);
            const string expectedSmallBarcode = "KL375C1000,SQDZQ8P0001";
            Assert(smallValues["YEAR_CODE"] == "Q" &&
                   smallValues["MONTH_CODE"] == "8" &&
                   smallValues["DAY_CODE"] == "P" &&
                   smallValues["LOT_NO"] == "0001",
                "TEM_BE resolves 2026/year, month 8, day 25 and four-digit LOT codes");
            Assert(smallLabel.Copies == 1,
                "TEM_BE keeps the configured single print copy");
            Assert(smallLabel.Payload.Contains(expectedSmallBarcode, StringComparison.Ordinal) &&
                   smallLabel.Data.Barcode == expectedSmallBarcode &&
                   smallLabel.Data.BarcodePrint == expectedSmallBarcode,
                "TEM_BE DATA5 and immutable barcode snapshot use the same SQDZ canonical value from THT data");

            Assert(SmallLabel(25, new DateTime(2026, 8, 25)).Data.Barcode.EndsWith("0025", StringComparison.Ordinal),
                "TEM_BE formats LOT 25 as 0025");
            Assert(SmallLabel(7001, new DateTime(2026, 8, 25)).Data.Barcode.EndsWith("7001", StringComparison.Ordinal),
                "TEM_BE keeps four-digit LOT 7001 unchanged");
            Assert(SmallLabel(1, new DateTime(2026, 8, 1)).Data.Barcode == "KL375C1000,SQDZQ810001",
                "TEM_BE keeps day 1 as digit 1");
            Assert(SmallLabel(1, new DateTime(2026, 8, 26)).Data.Barcode == "KL375C1000,SQDZQ8Q0001",
                "TEM_BE maps day 26 to Q");

            model.PartNumber = "BE331H6000";
            void AssertSmallDateCode(DateTime date, string expectedDateCode, string expectedBarcode)
            {
                LabelPrintRequest request = SmallLabel(7002, date);
                IReadOnlyDictionary<string, string> values =
                    LabelVariableResolver.Resolve(model, request.Data, labelSettings);
                string actualDateCode =
                    values["YEAR_CODE"] + values["MONTH_CODE"] + values["DAY_CODE"];
                Assert(actualDateCode == expectedDateCode &&
                       request.Data.Barcode == expectedBarcode &&
                       values["SMALL_LABEL_BARCODE"] == expectedBarcode,
                    $"TEM_BE date {date:dd/MM/yyyy} resolves to {expectedDateCode}");
            }

            AssertSmallDateCode(
                new DateTime(2026, 7, 29),
                "Q7T",
                "BE331H6000,SQDZQ7T7002");
            AssertSmallDateCode(
                new DateTime(2026, 8, 26),
                "Q8Q",
                "BE331H6000,SQDZQ8Q7002");
            AssertSmallDateCode(
                new DateTime(2026, 8, 31),
                "Q8V",
                "BE331H6000,SQDZQ8V7002");
            AssertSmallDateCode(
                new DateTime(2026, 10, 10),
                "QAA",
                "BE331H6000,SQDZQAA7002");
            AssertSmallDateCode(
                new DateTime(2026, 12, 31),
                "QCV",
                "BE331H6000,SQDZQCV7002");
            AssertSmallDateCode(
                new DateTime(2025, 1, 1),
                "P11",
                "BE331H6000,SQDZP117002");

            InvalidDataException undefinedYear = AssertThrows<InvalidDataException>(
                () => SmallLabel(1, new DateTime(2036, 8, 25)),
                "TEM_BE year 2036 must be rejected before creating a print request");
            Assert(undefinedYear.Message.Contains("LABEL_DATE_CODE_UNDEFINED", StringComparison.Ordinal) &&
                   undefinedYear.Message.Contains("Year=2036", StringComparison.Ordinal),
                "TEM_BE year 2036 reports the undefined date-code marker and exact year");

            string smallDuplicatePath = Path.Combine(root, "LastSmallBarcode.txt");
            var smallDuplicateGuard = new LabelDuplicateGuard(smallDuplicatePath);
            smallDuplicateGuard.RecordSuccessfulPrint(smallLabel, testedAt);
            Assert(smallDuplicateGuard.LoadLast()?.Barcode == expectedSmallBarcode,
                "TEM_BE duplicate state persists the physical SQDZ barcode");

            model.PartNumber = "1200020430";
            model.ProductName = "BMS EXT";
            model.Eco = "US4 HEV";
            model.VehicleType = model.Eco;
            model.Alc = "12000/20430";
            model.CustomerCode = model.Alc;
            model.LabelVariables.Clear();
            history.LotNo = 7001;
            history.Finished = new DateTime(2026, 7, 31, 8, 9, 10);
            labelSettings.TemplateType = LabelSettings.LargeTemplate;
            LabelPrintRequest largeLabel = LabelPrintRequest.Capture(history, model, labelSettings);
            byte[] expectedLargePayload = File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "LabelGolden", "TEM_TO_regression_expected.epl"));
            Assert(Encoding.UTF8.GetBytes(largeLabel.Payload).SequenceEqual(expectedLargePayload),
                "TEM_TO payload remains byte-for-byte identical for the locked regression input");

            string duplicatePath = Path.Combine(root, "LastBarcode.txt");
            var duplicateGuard = new LabelDuplicateGuard(duplicatePath);
            duplicateGuard.RecordSuccessfulPrint(request, testedAt);
            LabelDuplicateRecord savedDuplicate = duplicateGuard.LoadLast()
                ?? throw new InvalidOperationException("LastBarcode state was not persisted.");
            Assert(savedDuplicate.Barcode == "BE331-G2000-2044" &&
                   savedDuplicate.CycleId == "cycle-a" &&
                   savedDuplicate.LotNo == 2044,
                "LastBarcode compatibility state records barcode/cycle/LOT after successful print");

            var stressSettings = new ProductionSettings { LotNo = 3000 };
            var stressLots = new LotSequenceService(stressSettings, _ => { });
            for (int cycle = 0; cycle < 100; cycle++)
            {
                if (cycle % 2 != 0)
                    continue;
                string cycleId = $"stress-{cycle}";
                long lot = stressLots.ReserveForCycle(cycleId);
                Assert(stressLots.TryCommitSuccessfulPrint(cycleId, lot, out _),
                    $"100-cycle label stress commits cycle {cycle}");
            }
            Assert(stressLots.NextLot == 3050,
                "100-cycle PASS/FAIL stress has no skipped or duplicate committed LOT");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestLabelPrintingSafety()
    {
        MethodInfo shouldAutoPrint = typeof(TestViewModel).GetMethod(
            "ShouldAutoPrintLabel",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PASS label eligibility helper not found.");
        Assert((bool)(shouldAutoPrint.Invoke(null, [true, true]) ?? false),
            "Final PASS with auto-print enabled is eligible for one print transaction");
        Assert(!(bool)(shouldAutoPrint.Invoke(null, [false, true]) ?? true) &&
               !(bool)(shouldAutoPrint.Invoke(null, [true, false]) ?? true),
            "NG/FAIL and disabled auto-print never create an automatic print transaction");

        MethodInfo hasLabelTransport = typeof(TestViewModel).GetMethod(
            "HasConfiguredLabelTransport",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Label transport eligibility helper not found.");
        Assert(!(bool)(hasLabelTransport.Invoke(null, [new LabelSettings()]) ?? true) &&
               (bool)(hasLabelTransport.Invoke(null, [new LabelSettings { PrinterCom = "COM3" }]) ?? false) &&
               (bool)(hasLabelTransport.Invoke(null, [new LabelSettings { PrinterName = "ZDesigner" }]) ?? false),
            "Auto-print is skipped on stations without a configured printer transport");

        DateTime finished = new(2026, 8, 10, 9, 8, 7, DateTimeKind.Local);
        var history = new TestHistoryRecord
        {
            Started = finished.AddSeconds(-1),
            Finished = finished,
            PartName = "PRODUCT-A",
            PartNumber = "PART-A",
            Eco = "ECO-A",
            LotNo = 31415,
            Result = "PASS",
            Passed = true,
            ModelName = "MODEL-A",
            ModelFile = @"D:\models\A.tht",
            CycleId = "cycle-a",
            PrintStatus = LabelPrintStatus.NotRequested.ToString()
        };
        var settings = new LabelSettings
        {
            PrinterName = "ZEBRA-A",
            WidthMm = 90,
            HeightMm = 15,
            FormatName = "KS91-A",
            Copies = 1
        };

        var labelModel = new ProductModel
        {
            ProductName = history.PartName,
            PartNumber = history.PartNumber,
            Eco = history.Eco,
            Nco = history.Nco,
            Alc = history.Alc,
            VehicleType = history.Eco,
            CustomerCode = history.Alc,
            LabelTemplate = new LabelTemplateDefinition(
                "N\n{PART_NUMBER}\n{PRODUCT_NAME}\n{LOT_NO}\nP1",
                ProfileId: "KS91-A")
        };
        LabelPrintRequest request = LabelPrintRequest.Capture(history, labelModel, settings);
        LabelIdentity identity = EplLabelService.BuildIdentity(request.Data);
        history.LabelSerial = identity.SerialText;
        history.BarcodeValue = identity.BarcodeValue;
        Assert(history.ExportBarcodeText.Length == 0,
            "Auto-print disabled/not requested hides any prepared barcode from history export");
        history.BarcodeValue = string.Empty;
        history.LabelProfile = request.FormatName;
        history.Printer = request.Printer;
        history.LabelCopies = request.Copies;
        Assert(history.ExportBarcodeText.Length == 0,
            "PASS history keeps barcode blank before the printer confirms success");

        // UI/current model changes after PASS must not mutate the queued label.
        history.PartNumber = "PART-B";
        history.ModelName = "MODEL-B";
        settings.PrinterName = "ZEBRA-B";
        settings.Copies = 2;
        string epl = EplLabelService.BuildPassLabel(request);
        Assert(epl.Contains("PART-A", StringComparison.Ordinal) &&
               !epl.Contains("PART-B", StringComparison.Ordinal), "PASS cycle keeps model A snapshot");
        Assert(request.ModelName == "MODEL-A" && request.PrinterName == "ZEBRA-A" && request.Copies == 1,
            "Printer/model/copies snapshot is immutable");
        Assert(identity.BarcodeValue == "PART-A26081031415", "Barcode is deterministic from PASS snapshot");

        MethodInfo tryCapturePassLabel = typeof(TestViewModel).GetMethod(
            "TryCapturePassLabel",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PASS label isolation helper not found.");
        var unresolvedLabelModel = new ProductModel
        {
            ModelName = "NAM",
            PartNumber = "NAM",
            LabelTemplate = new LabelTemplateDefinition(ProfileId: "MISSING_TEST_PROFILE")
        };
        var unresolvedLabelSettings = new LabelSettings { FormatName = "KS91" };
        object?[] captureArguments =
        [
            history,
            unresolvedLabelModel,
            unresolvedLabelSettings,
            null,
            null,
            null
        ];
        bool unresolvedCaptured = (bool)(tryCapturePassLabel.Invoke(null, captureArguments) ?? true);
        Assert(!unresolvedCaptured &&
               captureArguments[3] is null &&
               captureArguments[4] is null &&
               captureArguments[5] is string captureError &&
               captureError.Contains("NEEDS_ORIGINAL_TRACE", StringComparison.Ordinal),
            "Unresolved KS91 label configuration is contained as a print failure and cannot escape into PASS/ProductRemoved lifecycle");

        var qrHistory = new TestHistoryRecord
        {
            Finished = finished,
            PartName = "QR PRODUCT",
            PartNumber = "K32000-22402",
            Alc = "12000/20430/2",
            LotNo = 1,
            Result = "PASS",
            Passed = true,
            ModelName = "QR MODEL",
            ModelFile = "QR.model.tht",
            CycleId = "cycle-qr"
        };
        var qrModel = new ProductModel
        {
            ProductName = qrHistory.PartName,
            PartNumber = qrHistory.PartNumber,
            Eco = "HE EV",
            VehicleType = "HE EV",
            Alc = qrHistory.Alc,
            CustomerCode = qrHistory.Alc
        };
        var qrSettings = new LabelSettings { TemplateType = LabelSettings.SmallQrTemplate };
        object?[] qrCaptureArguments = [qrHistory, qrModel, qrSettings, null, null, null];
        bool qrCaptured = (bool)(tryCapturePassLabel.Invoke(null, qrCaptureArguments) ?? false);
        Assert(qrCaptured &&
               qrCaptureArguments[3] is LabelPrintRequest capturedQrRequest &&
               capturedQrRequest.Data.Barcode == "K32000-22402,2608100001" &&
               qrCaptureArguments[4] is LabelIdentity capturedQrIdentity &&
               capturedQrIdentity.SerialText == "2608101WH" &&
               capturedQrIdentity.BarcodeValue == "K32000-22402,2608100001",
            "TEM_BE_QR PASS history keeps QR barcode and does not apply TEM_TO ALC suffix");

        string root = Path.Combine(Path.GetTempPath(), "JBZLabelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Persist the original PASS identity, not the later mutable UI values.
            history.PartNumber = request.Data.PartNumber;
            history.ModelName = request.ModelName;
            var store = new TestHistoryStore(Path.Combine(root, "history.db"));
            long id = store.Add(history);
            Assert(store.TryBeginFirstPrint(id, request.CycleId), "First PASS callback claims print transaction");
            Assert(!store.TryBeginFirstPrint(id, request.CycleId), "Duplicate PASS callback is blocked");
            store.UpdateLabelPrintOutcome(
                id,
                request.CycleId,
                LabelPrintStatus.Failed,
                null,
                "printer offline");
            TestHistoryRecord failedPrint = store.Search(new HistorySearchCriteria(
                null, null, 31415, "PART-A", "PASS", 10)).Single();
            Assert(failedPrint.BarcodeValue.Length == 0 && failedPrint.ExportBarcodeText.Length == 0,
                "Failed or disabled printing never writes barcode into history");
            Assert(store.TryBeginFirstPrint(id, request.CycleId), "Explicit retry reuses the failed cycle/LOT transaction");
            Assert(!store.TryBeginFirstPrint(id, request.CycleId), "Concurrent retry callback is blocked while Pending");
            store.UpdateLabelPrintOutcome(
                id,
                request.CycleId,
                LabelPrintStatus.Printed,
                finished.AddMilliseconds(250),
                "software-test",
                identity.BarcodeValue);
            store.IncrementLabelReprint(
                id,
                request.CycleId,
                finished.AddMilliseconds(500),
                "manual-reprint");

            TestHistoryRecord saved = store.Search(new HistorySearchCriteria(
                null, null, 31415, "PART-A", "PASS", 10)).Single();
            Assert(saved.CycleId == "cycle-a" && saved.PrintStatus == "Printed", "Cycle/print status traceability");
            Assert(saved.BarcodeValue == identity.BarcodeValue &&
                   saved.LabelSerial == identity.SerialText &&
                   saved.LabelProfile == "KS91-A" &&
                   saved.Printer == "ZEBRA-A" &&
                   saved.LabelCopies == 1 &&
                   saved.ReprintCount == 1, "Manual reprint keeps the PASS identity and increments only reprint count");
            Assert(saved.PrintTimestamp.HasValue, "Printed transaction records timestamp");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static TestEngine CreateEngine(out FakeBoard board, ProductionSettings? productionOverride = null)
    {
        board = new FakeBoard();
        var app = new AppSettings();
        app.Board.RequiredStableFrames = 1;
        var production = productionOverride ?? new ProductionSettings
        {
            IoConfirm1 = 1,
            IoConfirmN = 1,
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50,
            PassMarkingToJigDelayMs = 0,
            OpenCircuitConfirmMs = 0,
            ShortCircuitConfirmMs = 0,
            WrongConnectionConfirmMs = 0,
            ProductSettleTimeMs = 0,
            JigContactUnstableWindowMs = 0
        };
        return new TestEngine(board, new KeysightVisaService(), app, production);
    }

    private static TestViewModel CreateTestViewModel(ProductionSettings production)
        => CreateTestViewModel(production, out _);

    private static TestViewModel CreateTestViewModel(
        ProductionSettings production,
        out FakeBoard board,
        bool requireStartupIoClear = false)
    {
        board = new FakeBoard();
        var app = new AppSettings();
        var engine = new TestEngine(board, new KeysightVisaService(), app, production);
        return new TestViewModel(
            new MainViewModel(),
            engine,
            board,
            new KeysightVisaService(),
            new WaterProofSerialService(),
            app,
            production,
            new LegacyPhtHistoryService(enabled: false),
            requireStartupIoClear);
    }

    private static ProductModel Model(params (string Name, int[] Io)[] nets)
    {
        var model = new ProductModel { ModelName = "SELF-TEST", PartNumber = "SELF-TEST" };
        foreach ((string name, int[] io) in nets)
        {
            PinRecord[] pins = io.Select(value => new PinRecord("C", name, value, value.ToString())).ToArray();
            model.Pins.AddRange(pins);
            model.Nets.Add(new WireNet(name, io, pins));
        }
        return model;
    }

    private static ProductModel HtdrvTwoEndpointModel()
    {
        var model = new ProductModel { ModelName = "HTDRV-ENDPOINT", PartNumber = "HTDRV-ENDPOINT" };
        var pin1 = new PinRecord("1", "1", 1, "1", Section: "0.5", Color: "R", OriginalOrder: 1);
        var pin2 = new PinRecord("1", "1", 2, "2", Section: "0.5", Color: "R", OriginalOrder: 2);
        model.Pins.AddRange([pin1, pin2]);
        model.Nets.Add(new WireNet("1", [1, 2], [pin1, pin2]));
        model.Connectors.Add(new ConnectorDefinition(
            "1",
            2,
            [
                new ConnectorPin("1", 1, "1", pin1),
                new ConnectorPin("2", 2, "1", pin2)
            ]));
        return model;
    }

    private static ProductModel HtdrvShortModel()
    {
        var model = new ProductModel { ModelName = "HTDRV-SHORT", PartNumber = "HTDRV-SHORT" };
        var a1 = new PinRecord("7", "A", 7, "1", Section: "0.5", Color: "R", OriginalOrder: 1);
        var a2 = new PinRecord("7", "A", 8, "2", Section: "0.5", Color: "R", OriginalOrder: 2);
        var b1 = new PinRecord("9", "B", 9, "1", Section: "0.5", Color: "B", OriginalOrder: 3);
        var b2 = new PinRecord("9", "B", 10, "2", Section: "0.5", Color: "B", OriginalOrder: 4);
        model.Pins.AddRange([a1, a2, b1, b2]);
        model.Nets.Add(new WireNet("A", [7, 8], [a1, a2]));
        model.Nets.Add(new WireNet("B", [9, 10], [b1, b2]));
        return model;
    }

    private static ProductModel TopologyModel(params Terminal[] terminals)
    {
        var model = new ProductModel { ModelName = "THT-TOPOLOGY", PartNumber = "THT-TOPOLOGY" };

        foreach (Terminal terminal in terminals)
        {
            model.Pins.Add(new PinRecord(
                terminal.Connector,
                terminal.WireName,
                terminal.Io,
                terminal.Pin,
                Section: string.Empty,
                Color: terminal.Color,
                ConnectorPinCount: terminal.PinCount,
                PinType: terminal.PinType,
                WireConnection: terminal.WireConnection));
        }

        model.Nets = model.Pins
            .Where(pin => !string.IsNullOrWhiteSpace(pin.WireName))
            .GroupBy(pin => pin.WireName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                PinRecord[] pins = group.ToArray();
                int[] ios = pins.Select(pin => pin.IoNumber).Distinct().ToArray();
                return new WireNet(group.Key, ios, pins);
            })
            .Where(net => net.IoNumbers.Count >= 2)
            .ToList();

        model.Connectors = model.Pins
            .Where(pin => !string.IsNullOrWhiteSpace(pin.Connector))
            .GroupBy(pin => pin.Connector.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                int[] declaredCounts = group
                    .Select(pin => int.TryParse(pin.ConnectorPinCount, out int value) && value > 0 ? value : 0)
                    .Where(value => value > 0)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray();
                if (declaredCounts.Length > 1)
                {
                    model.TopologyWarnings.Add(
                        $"MODEL_WARNING_CONNECTOR_PINCOUNT_MISMATCH connector={group.Key} values=[{string.Join(",", declaredCounts)}]");
                }

                ConnectorPin[] pins = group
                    .Select(pin => new ConnectorPin(pin.PinNumber, pin.IoNumber, pin.WireName, pin))
                    .OrderBy(pin => pin.LocalPinNumber, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(pin => pin.PhysicalIo)
                    .ToArray();

                return new ConnectorDefinition(
                    group.Key,
                    declaredCounts.Length > 0 ? declaredCounts[0] : null,
                    pins);
            })
            .OrderBy(connector => connector.ConnectorId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return model;
    }

    private sealed record Terminal(
        int Io,
        string Connector,
        string Pin,
        string PinCount,
        string WireName,
        string Color = "B",
        string PinType = "",
        string WireConnection = "");

    private static PinRecord? PinByIo(ProductModel model, int io) =>
        model.Pins.FirstOrDefault(pin => pin.IoNumber == io);

    private static int? ConnectorPinCount(ProductModel model, string connector) =>
        model.Connectors.FirstOrDefault(item =>
            string.Equals(item.ConnectorId, connector, StringComparison.OrdinalIgnoreCase))
            ?.DeclaredPinCount;

    private static int[] NetIos(ProductModel model, string wireName) =>
        model.Nets.FirstOrDefault(net =>
            string.Equals(net.Name, wireName, StringComparison.OrdinalIgnoreCase))
            ?.IoNumbers.OrderBy(io => io).ToArray() ?? [];

    private static ScanFrame Frame(params (int Source, int[] Targets)[] connections)
        => FrameSeq(1, connections);

    private static ScanFrame FrameSeq(long sequence, params (int Source, int[] Targets)[] connections)
    {
        Dictionary<int, IReadOnlySet<int>> map = connections.ToDictionary(
            pair => pair.Source,
            pair => (IReadOnlySet<int>)pair.Targets.ToHashSet());
        HashSet<int> active = map.Keys.Concat(map.Values.SelectMany(values => values)).ToHashSet();
        Dictionary<int, int> hits = map.Values.SelectMany(values => values)
            .GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        return new ScanFrame(DateTime.Now, 1, active, [], true, 0, sequence, map, hits, BoardScanMode.Production);
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using StreamReader reader = new(archive.GetEntry(name)?.Open() ?? throw new InvalidOperationException(name));
        return reader.ReadToEnd();
    }

    private static int CountActiveCycles(string databasePath, string cycleId)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ActiveTestCycles WHERE CycleId=$CycleId;";
        command.Parameters.AddWithValue("$CycleId", cycleId);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static TException AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new InvalidOperationException(message);
    }

    private static ProductModel ParseThtLabelModelText(string alc = "ALC-FROM-THT")
    {
        string modelText =
            "파트번호\t파트명\tECO\tNCO\tALC\n" +
            $"KL375C1000\tVOLTAGE_6S\tAE EV PE\tNCO-7\t{alc}\n\n" +
            "번 호\t커넥터\t핀 수\n" +
            "1\tCN1\t2\n\n" +
            "커넥터\t선이름\tI/O\t핀번호\n" +
            "CN1\tW1\t1\t1\n" +
            "CN1\tW1\t2\t2\n\n" +
            "선이름\t선연결\t굵기\t색깔\n" +
            "W1\t\t0.5\tR";

        string root = Path.Combine(Path.GetTempPath(), "JBZThtLabelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "SQDZ-label-source.tht");
            File.WriteAllBytes(path, BuildMinimalThtFile(modelText));
            return new ThtModelParser().Load(path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestBlankThtIoMappingCompatibility()
    {
        const string blankModelText =
            "파트번호\t파트명\n" +
            "1\tIO MAPPING\n\n" +
            "번 호\t커넥터\t핀 수\n\n" +
            "커넥터\t선이름\tI/O\t핀번호\n\n" +
            "선이름\t선연결\t굵기\t색깔";

        string root = Path.Combine(
            Path.GetTempPath(),
            "JBZBlankThtTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "blank.tht");
            File.WriteAllBytes(path, BuildMinimalThtFile(blankModelText));
            ProductModel model = new ThtModelParser().Load(path);

            Assert(model.IsIoMappingTemplate &&
                   model.Pins.Count == 0 &&
                   model.Nets.Count == 0 &&
                   model.MaxIo == 0,
                "A structurally valid THT with an empty Pin table loads as IO mapping template");

            string invalidPath = Path.Combine(root, "invalid-pin.tht");
            File.WriteAllBytes(
                invalidPath,
                BuildMinimalThtFile(blankModelText.Replace(
                    "커넥터\t선이름\tI/O\t핀번호\n\n",
                    "커넥터\t선이름\tI/O\t핀번호\nCN1\tW1\tNOT_IO\t1\n\n",
                    StringComparison.Ordinal)));
            AssertThrows<InvalidDataException>(
                () => new ThtModelParser().Load(invalidPath),
                "A non-empty Pin table with invalid IO must not be mistaken for a blank mapping THT");

            string? compatibilityFile = Environment.GetEnvironmentVariable(
                "JBZ_THT_COMPAT_FILE");
            if (!string.IsNullOrWhiteSpace(compatibilityFile))
            {
                ProductModel compatibilityModel = new ThtModelParser().Load(compatibilityFile);
                Assert(compatibilityModel.IsIoMappingTemplate,
                    $"Compatibility THT '{compatibilityFile}' loads as IO mapping template");
            }

            BoardCapacity capacity = BoardCapacity.Create(1);
            IReadOnlyList<FaultRow> connectionRows = IoMappingFramePresenter.BuildRows(
                FrameSeq(1, (2, new[] { 1, 3 }), (1, new[] { 2 })),
                capacity);
            Assert(connectionRows.Count == 2 &&
                   connectionRows.Any(row => row.ActualSourceIo == 1 && row.ActualTargetIo == 2) &&
                   connectionRows.Any(row => row.ActualSourceIo == 2 && row.ActualTargetIo == 3),
                "IO mapping canonicalizes duplicate directions and shows every live connection pair");

            IReadOnlyList<FaultRow> probeRows = IoMappingFramePresenter.BuildRows(
                FrameSeq(
                    2,
                    Enumerable.Range(10, 20)
                        .Select(source => (source, new[] { 7 }))
                        .ToArray()),
                capacity);
            Assert(probeRows.Count == 1 &&
                   probeRows[0].Kind == FaultKind.Probe &&
                   probeRows[0].Io == 7,
                "Probe sweep in blank THT shows the touched IO instead of false wiring pairs");

            var production = new ProductionSettings
            {
                MasterFaultRequiredCount = 0,
                UseTestPointer = true
            };
            TestViewModel vm = CreateTestViewModel(production, out FakeBoard board);
            vm.SetModel(model);
            vm.StartProductionTestAsync().GetAwaiter().GetResult();
            board.Publish(FrameSeq(3, (4, new[] { 9 })));

            Assert(vm.IsIoMappingMode &&
                   vm.Faults.Count == 1 &&
                   vm.Faults[0].ActualSourceIo == 4 &&
                   vm.Faults[0].ActualTargetIo == 9 &&
                   vm.Total == 0 && vm.Pass == 0 && vm.Fail == 0 &&
                   !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                "Blank THT observation never commits production or activates a relay");

            board.Publish(FrameSeq(
                4,
                Enumerable.Range(10, 20)
                    .Select(source => (source, new[] { 7 }))
                    .ToArray()));
            Assert(AppSoundService.Current.IsTestPointContactSoundActive,
                "Blank THT Probe TOUCH starts continuous TESTPOINT sound");

            board.Publish(FrameSeq(5));
            Assert(!AppSoundService.Current.IsTestPointContactSoundActive,
                "Blank THT Probe RELEASE stops TESTPOINT sound immediately");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TestLearnedTopology()
    {
        LearnedTopologySnapshot snapshot = TopologyLearningService.BuildSnapshot(
            FrameSeq(
                80,
                (1, new[] { 18 }),
                (18, new[] { 1, 35 }),
                (35, new[] { 18 }),
                (2, new[] { 19 }),
                (19, new[] { 2 })),
            BoardCapacity.Create(1));

        Assert(snapshot.Networks.Count == 2 &&
               snapshot.Networks[0].Ios.SequenceEqual(new[] { 1, 18, 35 }) &&
               snapshot.Networks[1].Ios.SequenceEqual(new[] { 2, 19 }) &&
               snapshot.Rows[0].Connection == "IO(1) ↔ IO(18) ↔ IO(35)",
            "Learning canonicalizes bidirectional edges into stable connected components");

        string root = Path.Combine(Path.GetTempPath(), "JBZLearnedTopologyTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "SAMPLE.jbzscan.json");
            var profile = new LearnedTopologyProfile
            {
                ProductCode = "SAMPLE",
                CreatedAt = DateTime.Now,
                ExpansionCardCount = 1,
                FirstIo = 1,
                LastIo = 64,
                RequiredStableFrames = 20,
                ObservedStableFrames = 20,
                Networks = snapshot.Networks.Select(network => new LearnedTopologyNetwork
                {
                    Name = network.Name,
                    Ios = network.Ios.ToList()
                }).ToList()
            };
            TopologyLearningService.SaveAsync(path, profile).GetAwaiter().GetResult();
            string json = File.ReadAllText(path);
            Assert(json.Contains("\"ProfileType\": \"DiagnosticContinuity\"", StringComparison.Ordinal) &&
                   json.Contains("\"ProductCode\": \"SAMPLE\"", StringComparison.Ordinal) &&
                   !File.Exists(path + ".tmp"),
                "Learned topology is atomically persisted as an explicitly diagnostic non-THT profile");

            string mainXaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "MainWindow.xaml"));
            string learningXaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "TopologyLearningWindow.xaml"));
            Assert(mainXaml.Contains("Content=\"QUÉT / HỌC MÃ\"", StringComparison.Ordinal) &&
                   learningXaml.Contains("Không phải file THT", StringComparison.Ordinal) &&
                   learningXaml.Contains("EnableRowVirtualization=\"True\"", StringComparison.Ordinal),
                "MainWindow exposes the diagnostic learning workflow with a virtualized safety-labelled grid");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] BuildMinimalThtFile(string modelText)
    {
        const int sectorSize = 512;
        const uint freeSector = 0xFFFFFFFF;
        const uint endOfChain = 0xFFFFFFFE;
        const uint fatSector = 0xFFFFFFFD;
        const int contentSectorCount = 8;
        const int contentStartSector = 2;

        byte[] file = new byte[sectorSize * (1 + 2 + contentSectorCount)];
        byte[] signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        signature.CopyTo(file, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(24, 2), 0x003E);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26, 2), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28, 2), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(30, 2), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(32, 2), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(44, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(48, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(56, 4), 4096);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(60, 4), endOfChain);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(68, 4), endOfChain);
        for (int index = 0; index < 109; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                file.AsSpan(76 + index * sizeof(uint), sizeof(uint)),
                index == 0 ? 0u : freeSector);
        }

        Span<byte> fat = file.AsSpan(sectorSize, sectorSize);
        for (int index = 0; index < fat.Length / sizeof(uint); index++)
            BinaryPrimitives.WriteUInt32LittleEndian(fat.Slice(index * sizeof(uint), sizeof(uint)), freeSector);
        BinaryPrimitives.WriteUInt32LittleEndian(fat.Slice(0, 4), fatSector);
        BinaryPrimitives.WriteUInt32LittleEndian(fat.Slice(4, 4), endOfChain);
        for (int index = 0; index < contentSectorCount; index++)
        {
            uint next = index == contentSectorCount - 1
                ? endOfChain
                : (uint)(contentStartSector + index + 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                fat.Slice((contentStartSector + index) * sizeof(uint), sizeof(uint)),
                next);
        }

        Span<byte> directory = file.AsSpan(sectorSize * 2, sectorSize);
        WriteDirectoryEntry(directory.Slice(0, 128), "Root Entry", 5, endOfChain, 0);
        WriteDirectoryEntry(
            directory.Slice(128, 128),
            "Contents",
            2,
            contentStartSector,
            contentSectorCount * sectorSize);

        using var content = new MemoryStream();
        using (var writer = new BinaryWriter(content, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0x389DEFB9u);
            WriteMfcCString(writer, "TEST", Encoding.ASCII);
            WriteMfcCString(writer, modelText, Encoding.GetEncoding(949));
        }

        byte[] contentBytes = content.ToArray();
        Assert(contentBytes.Length <= contentSectorCount * sectorSize,
            "Synthetic .tht Contents stream exceeds its OLE sector allocation");
        contentBytes.CopyTo(file, sectorSize * (contentStartSector + 1));
        return file;

        static void WriteDirectoryEntry(
            Span<byte> entry,
            string name,
            byte type,
            uint startSector,
            long streamSize)
        {
            byte[] nameBytes = Encoding.Unicode.GetBytes(name + '\0');
            nameBytes.CopyTo(entry);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(64, 2), checked((ushort)nameBytes.Length));
            entry[66] = type;
            entry[67] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(68, 4), freeSector);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(72, 4), freeSector);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(76, 4), freeSector);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(116, 4), startSector);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(120, 8), checked((ulong)streamSize));
        }

        static void WriteMfcCString(BinaryWriter writer, string value, Encoding encoding)
        {
            byte[] bytes = encoding.GetBytes(value);
            if (bytes.Length < byte.MaxValue)
            {
                writer.Write((byte)bytes.Length);
            }
            else if (bytes.Length < ushort.MaxValue)
            {
                writer.Write(byte.MaxValue);
                writer.Write((ushort)bytes.Length);
            }
            else
            {
                writer.Write(byte.MaxValue);
                writer.Write(ushort.MaxValue);
                writer.Write((uint)bytes.Length);
            }

            writer.Write(bytes);
        }
    }

    private sealed class FakeBoard : IBoardTransport
    {
        public List<string> Commands { get; } = [];
        public List<ResistanceStep> ResistanceSteps { get; } = [];
        public List<byte[]> ResistanceFrames { get; } = [];
        public byte[] ReleaseResistanceFrames { get; private set; } = [];
        public int ReleaseResistanceRouteCount { get; private set; }
        public bool ThrowOnSetRelay { get; set; }
        public bool IsConnected => true;
        public bool IsScanning { get; private set; } = true;
        public BoardScanMode CurrentScanMode { get; private set; } = BoardScanMode.Production;
        public BoardCapacity InstalledCapacity { get; private set; } = BoardCapacity.Create(10);
        public BoardCapacity Capacity { get; private set; } = BoardCapacity.Create(10);
        public BoardCapacity? AppliedScanCapacity { get; private set; } = BoardCapacity.Create(10);
        public BoardScanCapacity ScanCapacity { get; private set; } = BoardScanCapacity.Create(
            new ProductionSettings { ExpansionCardCount = 10 },
            640);
        public DateTime LastFrameTimestampUtc { get; private set; } = DateTime.UtcNow;
        public long LastFrameSequence { get; private set; }
        public long LastCompleteFrameSequence { get; private set; }
        public long FramesReceived { get; private set; }
        public long CompleteFramesReceived { get; private set; }
        public int LastFrameSourceCount { get; private set; }
        public byte? LastFrameEndMarkerCode { get; private set; }
        public int LastFrameUnknownBytes { get; private set; }
        public void SetAppliedScanCapacityForTest(int scanUnits) =>
            AppliedScanCapacity = BoardCapacity.Create(scanUnits);
        public void SetRequestedScanCapacityForTest(int scanUnits) =>
            Capacity = BoardCapacity.Create(scanUnits);
        public CancellationToken? LastStartScanToken { get; private set; }
        public Action<FakeBoard>? StartScanCallback { get; set; }
        private event EventHandler<ScanFrame>? FrameReceivedCore;
        public event EventHandler<ScanFrame>? FrameReceived { add { FrameReceivedCore += value; } remove { FrameReceivedCore -= value; } }
        public event EventHandler<string>? Log { add { } remove { } }
        public void Publish(ScanFrame frame)
        {
            LastFrameTimestampUtc = DateTime.UtcNow;
            LastFrameSequence = frame.Sequence;
            FramesReceived++;
            LastFrameSourceCount = frame.SourceCount;
            LastFrameEndMarkerCode = frame.EndMarkerCode;
            LastFrameUnknownBytes = frame.UnknownBytes;
            if (frame.Mode == BoardScanMode.Production &&
                frame.Complete && frame.UnknownBytes == 0 && frame.TerminatorKnown)
            {
                LastCompleteFrameSequence = frame.Sequence;
                CompleteFramesReceived++;
            }
            FrameReceivedCore?.Invoke(this, frame);
        }
        public Task<BoardConnectionInfo> ConnectAsync(CancellationToken ct = default) => Task.FromResult(new BoardConnectionInfo("Fake", "Fake"));
        public Task DisconnectAsync() { IsScanning = false; return Task.CompletedTask; }
        public Task HandshakeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetClearAsync(CancellationToken ct = default) { Commands.Add("RESET"); return Task.CompletedTask; }
        public void ConfigureActiveScanRange(int maxIo) { }
        public Task StartScanAsync(BoardScanMode mode = BoardScanMode.Production, CancellationToken ct = default) { IsScanning = true; CurrentScanMode = mode; AppliedScanCapacity = Capacity; LastStartScanToken = ct; Commands.Add("START"); StartScanCallback?.Invoke(this); return Task.CompletedTask; }
        public Task StopScanAsync(CancellationToken ct = default) { IsScanning = false; Commands.Add("STOP"); return Task.CompletedTask; }
        public Task EnterIdleAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SelectResistanceRouteAsync(ResistanceStep step, CancellationToken ct = default)
        {
            ResistanceSteps.Add(step);
            ResistanceFrames.Add(
                D2xxResistanceRouting.BuildRouteA()
                    .Concat(D2xxResistanceRouting.BuildRouteB(step.Channel))
                    .ToArray());
            return Task.CompletedTask;
        }
        public Task ReleaseResistanceRouteAsync(CancellationToken ct = default)
        {
            ReleaseResistanceRouteCount++;
            ReleaseResistanceFrames = D2xxResistanceRouting.BuildReleaseRouteB()
                .Concat(D2xxResistanceRouting.BuildReleaseRouteA())
                .ToArray();
            Commands.Add("RELEASE_R");
            return Task.CompletedTask;
        }
        public Task SetRelayAsync(int relay, CancellationToken ct = default)
        {
            Commands.Add($"SET:{relay}");
            if (ThrowOnSetRelay)
                throw new InvalidOperationException("Simulated relay set failure");

            return Task.CompletedTask;
        }
        public Task AllRelaysOffAsync(CancellationToken ct = default) { Commands.Add("OFF"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeKeysightVisaService(bool connected, double measurement)
        : KeysightVisaService
    {
        public override bool IsConnected => connected;
        public int MeasureCallCount { get; private set; }
        public bool ThrowOnMeasure { get; set; }

        public override double MeasureResistance(string command = ":MEASURE:RES?")
        {
            MeasureCallCount++;
            if (ThrowOnMeasure)
                throw new InvalidOperationException("Simulated Keysight failure");
            return measurement;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private long _timestamp;

        public ManualTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override long TimestampFrequency => 1_000;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan value)
        {
            _utcNow = _utcNow.Add(value);
            _timestamp += checked((long)Math.Round(value.TotalMilliseconds));
        }
    }
}
