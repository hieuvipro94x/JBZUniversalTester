using System.IO.Compression;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using JBZUniversalTester.Models;
using JBZUniversalTester.Converters;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static class Program
{
    private static int Main()
    {
        (string Name, Action Run)[] tests =
        [
            ("Board capacity/address boundaries", TestBoardCapacity),
            ("Production/probe decoder separation", TestDecoderModes),
            ("Startup connected-IO safety interlock", TestStartupIoInterlock),
            ("Probe target-only touch detection", TestProbeTargetOnlyTouchDetection),
            ("Inline probe does not clear wiring faults", TestInlineProbeDoesNotClearWiringFaults),
            ("Htdrv endpoint/probe display cases", TestHtdrvEndpointProbeDisplayCases),
            ("100-cycle scan/probe/fault stress", TestHundredCycleScanProbeFaultStress),
            ("Continuity/open/wrong/splice engine", TestEngineVectors),
            ("Production PASS gate minimal latency", TestProductionPassGateMinimalLatency),
            ("THT column semantics and string wire topology", TestThtColumnSemantics),
            ("Relay PASS/FAIL safe ordering", TestRelayOrdering),
            ("History SQLite/search/CSV/XLSX native types", TestHistory),
            ("ALL6 label data order", TestLabel),
            ("THT label renderer and LOT lifecycle", TestThtLabelAndLotLifecycle),
            ("PASS label snapshot/idempotency/traceability", TestLabelPrintingSafety),
            ("Standard product picker filter", TestProductPickerFilter),
            ("Fault display localization and detail", TestFaultDisplayFormatter),
            ("UI brush cache and engine change filter", TestUiPerformanceGuards),
            ("D2XX resistance selectors and ten-slot configuration", TestD2xxResistanceRouting),
            ("Leak connector mapping and PASS/FAIL presentation", TestWaterProofConfigurationAndPresentation),
            ("Final TestView status/master/device fault guards", TestFinalTestStatusGuards),
            ("Manual mode relay interlock and production lock", TestManualModeInterlock),
            ("Production scan token survives cycle cancel", TestProductionScanTokenSurvivesCycleCancel),
            ("Production fault debounce and jig contact state", TestProductionFaultConfirmation),
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
                "JBZ THT (*.tht)|*.tht|Mã hàng legacy (*.model)|*.model",
                StringComparison.Ordinal),
            "Standard dialog filter must prefer .tht first and keep .model as legacy");
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
        Assert(openOperator.Title == "HỞ MẠCH", "Open operator title");
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
        Assert(wrongOperator.Title == "SAI KẾT NỐI", "Wrong connection operator title");
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
        Assert(shortOperator.Title == "CHẬP MẠCH", "Short operator title");
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
        var red1 = WireColorToBrushConverter.ToBrush("R");
        var red2 = WireColorToBrushConverter.ToBrush("R");
        var stripe1 = WireColorToBrushConverter.ToBrush("W/B");
        var stripe2 = WireColorToBrushConverter.ToBrush("W/B");
        Assert(ReferenceEquals(red1, red2) && red1.IsFrozen, "Single wire color brush is cached and frozen");
        Assert(ReferenceEquals(stripe1, stripe2) && stripe1.IsFrozen, "Composite wire color brush is cached and frozen");

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
        Assert(enabledMasterVm.ResultStatusText == "CHỜ MASTER" && enabledMasterVm.StateBackground == "#FFF3A0",
            "Waiting Master status is canonical yellow");

        TestViewModel statusVm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        statusVm.State = "PASS";
        Assert(statusVm.ResultStatusText == "ĐẠT" && statusVm.StateBackground == "#2AA84A" && statusVm.StateForeground == "#FFFFFF",
            "PASS status mapping");
        statusVm.State = "PASS - CHỜ THÁO SẢN PHẨM";
        Assert(statusVm.ResultStatusText == "ĐẠT" && statusVm.StateBackground == "#2AA84A",
            "Committed PASS remains green until ProductRemoved returns the UI to ready");
        statusVm.State = "ĐANG TEST LEAK";
        Assert(statusVm.ResultStatusText == "ĐANG TEST LEAK" &&
               statusVm.StateBackground == "#FFF3A0",
            "Leak stage has an explicit in-progress presentation before PASS");
        statusVm.State = "CHƯA ĐẠT";
        Assert(statusVm.ResultStatusText == "KHÔNG ĐẠT" && statusVm.StateBackground == "#C62828" && statusVm.StateForeground == "#FFFFFF",
            "FAIL status mapping");
        statusVm.State = "CHỜ THÁO SẢN PHẨM";
        Assert(statusVm.ResultStatusText == "CHỜ THÁO" &&
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
               recoveryBoard.Commands.Contains("START") &&
               !recoveryBoard.Commands.Contains("SET:2"),
            "Rejected FAIL commit restarts removal scan, restores IO rows, and cannot remain latched at KHÔNG ĐẠT");

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
            "TestView Số LOT must display configured starting LOT, never the probe maintenance counter");

        string settingsXaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "ProductionSettingsPage.xaml"));
        Assert(settingsXaml.Contains("Content=\"KẾT NỐI\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("Click=\"ConnectPrinter_Click\"", StringComparison.Ordinal) &&
               settingsXaml.Contains("x:Name=\"PrinterConnectionStatusText\"", StringComparison.Ordinal),
            "Production settings exposes reconnectable printer control and connection status");

        TestViewModel deviceFaultVm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        deviceFaultVm.LoadPreparedModelAsync(model0).GetAwaiter().GetResult();
        MethodInfo reportDeviceFault = typeof(TestViewModel).GetMethod(
            "ReportDeviceFaultForTest",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DeviceFault test reporter not found");
        MethodInfo resetDeviceFault = typeof(TestViewModel).GetMethod(
            "ResetDeviceFaultForTestAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DeviceFault test reset not found");

        for (int index = 0; index < 100; index++)
            reportDeviceFault.Invoke(deviceFaultVm, [new ArgumentOutOfRangeException("index", "simulated index fault"), -1]);
        Assert(deviceFaultVm.IsDeviceFault, "DeviceFault latches after index exception");
        Assert(deviceFaultVm.DeviceFaultTransitionCount == 1, "Repeated index exceptions produce one DeviceFault transition");
        Assert(deviceFaultVm.DeviceFaultDialogCount == 1, "Repeated index exceptions produce one operator dialog episode");
        Assert(deviceFaultVm.ResultStatusText == "LỖI THIẾT BỊ" &&
               deviceFaultVm.StateBackground == "#C62828" &&
               deviceFaultVm.StateForeground == "#FFFFFF",
            "DeviceFault status mapping");

        ((Task)(resetDeviceFault.Invoke(deviceFaultVm, []) ?? throw new InvalidOperationException("DeviceFault reset returned null")))
            .GetAwaiter()
            .GetResult();
        Assert(!deviceFaultVm.IsDeviceFault, "DeviceFault reset clears latch");
        Assert(deviceFaultVm.ResultStatusText == "SẴN SÀNG", "DeviceFault reset returns to ready presentation");
        reportDeviceFault.Invoke(deviceFaultVm, [new InvalidOperationException("second episode"), -1]);
        Assert(deviceFaultVm.DeviceFaultTransitionCount == 2 &&
               deviceFaultVm.DeviceFaultDialogCount == 2,
            "A later independent DeviceFault episode may show one new dialog");
    }

    private static void TestManualModeInterlock()
    {
        var settings = new ProductionSettings { ManualModeEnabled = true, MasterFaultRequiredCount = 0 };
        TestViewModel vm = CreateTestViewModel(settings, out FakeBoard board);
        vm.LoadPreparedModelAsync(Model(("PAIR", new[] { 1, 18 }))).GetAwaiter().GetResult();

        typeof(TestViewModel).GetField("_runtimeMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(vm, 1);
        Assert(vm.CanEnterManualMode, "Background production scan does not lock Manual Relay menu");

        vm.EnterManualModeAsync().GetAwaiter().GetResult();
        Assert(vm.IsManualModeActive && vm.State == "MANUAL" && !board.IsScanning,
            "Entering Manual locks production scan and shows MANUAL");
        Assert(board.Commands.Contains("OFF"), "Entering Manual sends safe relay OFF");

        board.Commands.Clear();
        int relay = vm.SetManualRelayAsync(1, true).GetAwaiter().GetResult();
        Assert(relay == 1, "Relay 1 ON state updates only after command completes");
        Assert(string.Join(",", board.Commands) == "OFF,SET:1",
            "Relay 1 ON performs all-off before SetRelay(1)");

        board.Commands.Clear();
        relay = vm.SetManualRelayAsync(2, true).GetAwaiter().GetResult();
        Assert(relay == 2, "Relay 2 ON replaces Relay 1");
        Assert(string.Join(",", board.Commands) == "OFF,SET:2",
            "Relay 2 ON performs all-off before SetRelay(2)");

        board.Commands.Clear();
        relay = vm.SetManualRelayAsync(2, false).GetAwaiter().GetResult();
        Assert(relay == 0 && string.Join(",", board.Commands) == "OFF",
            "Relay OFF leaves all outputs off");

        board.Commands.Clear();
        vm.ResetManualOutputsAsync().GetAwaiter().GetResult();
        Assert(string.Join(",", board.Commands) == "OFF,RESET,OFF",
            "Manual RESET uses existing reset path and leaves relay OFF");

        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        Assert(vm.State == "MANUAL", "Production start is blocked while Manual is ON");

        settings.ManualModeEnabled = false;
        board.Commands.Clear();
        vm.ExitManualModeAsync().GetAwaiter().GetResult();
        Assert(!vm.IsManualModeActive && board.Commands.Contains("OFF") && board.Commands.Contains("START"),
            "Exiting Manual sends safe OFF before production background scan can resume");

        var faultSettings = new ProductionSettings { ManualModeEnabled = true, MasterFaultRequiredCount = 0 };
        TestViewModel faultVm = CreateTestViewModel(faultSettings, out FakeBoard faultBoard);
        faultVm.LoadPreparedModelAsync(Model(("PAIR", new[] { 1, 18 }))).GetAwaiter().GetResult();
        faultVm.EnterManualModeAsync().GetAwaiter().GetResult();
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
        vm.SetModel(Model(("PAIR", new[] { 1, 18 })));
        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        int totalBeforeWarning = vm.Total;
        int passBeforeWarning = vm.Pass;
        int failBeforeWarning = vm.Fail;

        Assert(vm.State.Contains("KIỂM TRA IO BAN ĐẦU", StringComparison.Ordinal),
            "Production does not present READY before receiving a clean baseline frame");

        board.Publish(duplicatedDirections);
        Assert(vm.ResultStatusText == "CẢNH BÁO IO" &&
               vm.Faults.Count == 1 &&
               vm.Faults[0].Status.Contains("IO1", StringComparison.Ordinal) &&
               vm.Faults[0].Status.Contains("IO18", StringComparison.Ordinal),
            "Connected startup pins block READY and identify the exact IO pair");
        Assert(vm.Total == totalBeforeWarning &&
               vm.Pass == passBeforeWarning &&
               vm.Fail == failBeforeWarning &&
               !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Startup IO warning cannot commit production or activate a relay");

        board.Publish(FrameSeq(101));
        Assert(vm.State == "CHỜ LẮP SẢN PHẨM" && vm.ResultStatusText == "SẴN SÀNG",
            "A complete clean frame clears the startup interlock and arms Production");
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
        fastApp.Test.ResistanceStableSampleCount = 1;
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

        string xaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "TestWindow.xaml"));
        Assert(xaml.Contains("Text=\"ĐỘ RÒ LỚN NHẤT\"", StringComparison.Ordinal) &&
               xaml.Contains("Text=\" kPa\"", StringComparison.Ordinal),
            "Leak summary card identifies the machine-reported leak value and displays kPa");
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
               removalVm.SelectedOperationTabIndex == 3 &&
               removalVm.ResultStatusText == "ĐẠT" &&
               removalVm.StateBackground == "#2AA84A",
            "Committed Leak PASS keeps the result table, stays green, and arms ProductRemoved before scan restart");
        removalBoard.Publish(FrameSeq(3, (1, new[] { 18 })));
        Assert((bool)(waitForPassRemoval.GetValue(removalVm) ?? false) &&
               removalVm.SelectedOperationTabIndex == 3 &&
               removalVm.ResultStatusText == "ĐẠT",
            "Leak PASS result table remains visible while any product IO is still connected");
        removalBoard.Publish(FrameSeq(4));
        Assert(!(bool)(waitForPassRemoval.GetValue(removalVm) ?? true) &&
               removalVm.ResultStatusText == "SẴN SÀNG" &&
               removalVm.SelectedOperationTabIndex == 0,
            "After committed Leak PASS, a fresh empty frame confirms removal and returns to ready");

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
        Assert(leakServiceSource.Contains(
                   "await DisconnectAsync(cancellationToken).ConfigureAwait(false);",
                   StringComparison.Ordinal) &&
               leakServiceSource.Contains(
                   "next run will reconnect cleanly",
                   StringComparison.Ordinal),
            "A completed Leak result closes its COM session so cycle 2 cannot reuse stale machine state");
    }

    private static void TestProductionScanTokenSurvivesCycleCancel()
    {
        var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
        TestViewModel vm = CreateTestViewModel(settings, out FakeBoard board);
        vm.LoadPreparedModelAsync(Model(("PAIR", new[] { 1, 18 }))).GetAwaiter().GetResult();

        board.StopScanAsync().GetAwaiter().GetResult();
        vm.StartProductionTestAsync().GetAwaiter().GetResult();

        Assert(board.LastStartScanToken.HasValue, "Production start sends START_SCAN when scan is not already alive");
        CancellationToken scanToken = board.LastStartScanToken.GetValueOrDefault();

        MethodInfo cancelCycle = typeof(TestViewModel).GetMethod(
            "CancelCycleOperations",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CancelCycleOperations method not found.");
        cancelCycle.Invoke(vm, []);

        Assert(board.IsScanning, "Canceling ProductCycleToken does not stop D2XX scan session");
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

    private static string BrushHex(Brush brush)
    {
        if (brush is not SolidColorBrush solid)
            return "<non-solid>";

        Color color = solid.Color;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void TestBoardCapacity()
    {
        AssertCapacity(1, 2, 1, 64);
        AssertCapacity(2, 4, 2, 128);
        AssertCapacity(5, 10, 5, 320);
        AssertCapacity(10, 20, 10, 640);

        BoardCapacity capacity = BoardCapacity.Create(10);
        var mapper = new BoardAddressMapper(capacity);
        Assert(mapper.GetCardAddress(1) == new BoardCardAddress(1, 1, 1, 1, 1), "IO1 mapping");
        Assert(mapper.GetCardAddress(32).LocalIoNumber == 32, "IO32 local");
        Assert(mapper.GetCardAddress(33).PhysicalCardNumber == 2, "IO33 card");
        Assert(mapper.GetCardAddress(64).LocalIoNumber == 32, "IO64 local");
        Assert(mapper.GetCardAddress(65).PhysicalCardNumber == 3, "IO65 card");
        Assert(capacity.ContainsGlobalIo(640), "IO640 accepted");
        Assert(!capacity.ContainsGlobalIo(641), "IO641 rejected");
    }

    private static void AssertCapacity(int expansion, int physical, int scan, int io)
    {
        BoardCapacity capacity = BoardCapacity.Create(expansion);
        Assert(capacity.PhysicalCardCount == physical, $"Expansion {expansion}: physical");
        Assert(capacity.ScanCardCount == scan, $"Expansion {expansion}: scan");
        Assert(capacity.TotalIoCapacity == io, $"Expansion {expansion}: IO");
    }

    private static void TestDecoderModes()
    {
        var decoder = new BoardIoDecoder();
        decoder.ConfigureCapacity(BoardCapacity.Create(2));
        decoder.ConfigureMode(BoardScanMode.Production);
        ScanFrame production = decoder.Feed([0x80, 0x00, 0xA0, 0x11, 0xC0, 0x00]).Single();
        Assert(production.Complete && production.Mode == BoardScanMode.Production, "Production complete");
        Assert(production.Connections.TryGetValue(1, out IReadOnlySet<int>? targets) && targets.SetEquals([18]), "IO1->IO18");

        decoder.Reset();
        Assert(decoder.Feed([0x80]).Count == 0, "Partial source byte is buffered");
        ScanFrame splitFrame = decoder.Feed([0x00, 0xA0, 0x11, 0xC0, 0x00]).Single();
        Assert(splitFrame.Connections.TryGetValue(1, out IReadOnlySet<int>? splitTargets) &&
               splitTargets.SetEquals([18]), "Frame split across reads is reconstructed");

        decoder.Reset();
        IReadOnlyList<ScanFrame> multiple = decoder.Feed([
            0x80, 0x00, 0xA0, 0x11, 0xC0, 0x00,
            0x80, 0x01, 0xA0, 0x07, 0xC0, 0x00
        ]);
        Assert(multiple.Count == 2 &&
               multiple[0].Connections[1].SetEquals([18]) &&
               multiple[1].Connections[2].SetEquals([8]),
            "Multiple complete frames in one read are decoded");

        decoder.Reset();
        Assert(decoder.Feed([0x80, 0x00, 0xA0, 0x11]).Count == 0, "Incomplete frame does not publish");
        ScanFrame completedAfterTail = decoder.Feed([0xC0, 0x00]).Single();
        Assert(completedAfterTail.Connections[1].SetEquals([18]), "Incomplete frame completes after tail read");

        decoder.Reset();
        ScanFrame resynced = decoder.Feed([0x55, 0x80, 0x00, 0xA0, 0x11, 0xC0, 0x00]).Single();
        Assert(resynced.UnknownBytes == 1 &&
               resynced.Connections[1].SetEquals([18]), "Unknown byte is skipped and decoder resyncs");

        Assert(BoardCapacity.Create(2).StartScanParameter == 2 &&
               BoardCapacity.Create(4).StartScanParameter == 4,
            "START_SCAN parameter follows BoardCapacity, not a hard-coded 02");

        decoder.ConfigureMode(BoardScanMode.Probe);
        ScanFrame touch5 = decoder.Feed([0xA0, 0x04]).Single();
        Assert(touch5.Mode == BoardScanMode.Probe && touch5.ActiveIo.SetEquals([5]), "Probe touch IO5");
        ScanFrame release5 = decoder.Feed([0x80, 0x04]).Single();
        Assert(release5.ActiveIo.Count == 0, "Probe release IO5");
        ScanFrame touch113 = decoder.Feed([0xA0, 0x70]).Single();
        Assert(touch113.ActiveIo.SetEquals([113]), "Probe unmapped IO113");

        // ConfigureMode phải reset source còn dở của decoder trước đó.
        decoder.ConfigureMode(BoardScanMode.Production);
        ScanFrame noStaleSource = decoder.Feed([0xA0, 0x11, 0xC0, 0x00]).Single();
        Assert(noStaleSource.Connections.Count == 0, "No stale source after mode switch");
    }

    private static void TestEngineVectors()
    {
        using var engine = CreateEngine(out _);

        ProductModel pair = Model(("PAIR", new[] { 1, 18 }));
        engine.SetModel(pair);
        IReadOnlyList<FaultRow> initialPairRows = engine.BuildRows().Where(row => row.WireName == "PAIR").ToArray();
        Assert(initialPairRows.Count == 2 &&
               initialPairRows.Any(row => row.Io == 1 && row.Kind == FaultKind.Info && row.FaultType == "KIỂM TRA" && row.Pin == "1") &&
               initialPairRows.Any(row => row.Io == 18 && row.Kind == FaultKind.MissingConnection && row.FaultType == "HỞ MẠCH" && row.Pin == "18") &&
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
               pairReleased.Any(row => row.Io == 18 && row.FaultType == "HỞ MẠCH"),
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
        Assert(engine.BuildRows().Count(row => row.Kind == FaultKind.MissingConnection) == 1,
            "Missing IO1-IO18 is a display-only pending row");
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
        ScanFrame fullCoverageOpen = Frame((1, new[] { 18 }), (2, Array.Empty<int>()));
        engine.ProcessFrame(fullCoverageOpen);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(fullCoverageOpen);
        Assert(engine.ReadyToEvaluateProductFaults &&
               !engine.HasConfirmedOpenCircuit &&
               !engine.HasWiringFault &&
               engine.BuildRows().Count(row => row.Kind == FaultKind.MissingConnection) == 1,
            "Full source coverage with missing endpoint remains display-only");

        ProductModel splice = Model(("SPLICE", new[] { 5, 20, 33 }));
        engine.SetModel(splice);
        ScanFrame spliceOpenFrame = Frame((5, new[] { 20 }));
        engine.ProcessFrame(spliceOpenFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(spliceOpenFrame);
        IReadOnlyList<FaultDetail> confirmedSpliceOpen = engine.BuildConfirmedOpenFaults();
        Assert(confirmedSpliceOpen.Count == 0 &&
               !engine.BuildRows().Any(row => row.Kind == FaultKind.Open) &&
               engine.BuildRows().Count(row => row.Kind == FaultKind.MissingConnection) == 1,
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

        engine.SetModel(pair);
        engine.ProcessFrame(new ScanFrame(
            DateTime.Now, 1, new HashSet<int> { 1, 40 }, [], true, 0, 1,
            new Dictionary<int, IReadOnlySet<int>> { [1] = new HashSet<int> { 40 } },
            new Dictionary<int, int> { [40] = 1 }, BoardScanMode.Probe));
        Assert(!engine.HasWiringFault && !engine.ContinuityPassed, "Probe frame never enters production evaluation");
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
        gate.Observe(clean, [], true);
        clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultProductSettleTimeMs + 1));
        gate.Observe(clean, [], true);
        gate.Observe(clean, shortFault, true);
        clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultShortCircuitConfirmMs - 1));
        Assert(gate.Observe(clean, shortFault, true).ConfirmedUnexpectedPairs.Count == 0, "Transient SHORT not confirmed");
        clock.Advance(TimeSpan.FromMilliseconds(2));
        Assert(gate.Observe(clean, shortFault, true).ConfirmedUnexpectedPairs.Contains((1, 2)), "Stable SHORT confirmed");
        Assert(gate.Observe(clean, [], true).ConfirmedUnexpectedPairs.Count == 0, "SHORT recovery resets candidate");

        var wrongFault = new[] { new UnexpectedFaultObservation(3, 4, ProductFaultType.WrongWiring) };
        gate.Observe(clean, wrongFault, true);
        clock.Advance(TimeSpan.FromMilliseconds(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs - 1));
        Assert(gate.Observe(clean, wrongFault, true).ConfirmedUnexpectedPairs.Count == 0, "Transient wrong connection not confirmed");
        clock.Advance(TimeSpan.FromMilliseconds(2));
        Assert(gate.Observe(clean, wrongFault, true).ConfirmedUnexpectedPairs.Contains((3, 4)), "Stable wrong connection confirmed");

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

    private static void TestPartCounterStore()
    {
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
            Assert(b.Counter == 0 && store.GetOrCreate(modelA).Counter == 0,
                "PartCnt counters remain isolated by part number");
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
            var writer = new LegacyPhtHistoryService(passRoot, errorRoot);
            ProductModel model = Model(("PAIR", new[] { 1, 2 }));
            model.PartNumber = "sssss";

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
            string passText = cp949.GetString(File.ReadAllBytes(passPath));
            string expectedPass =
                "|[정상마스터]101|..|260807|162539|2608077000|||sssss7000|||||||\r\n" +
                "|1|..|260807|162547|2608077001|||sssss7001|||||||\r\n";
            Assert(masterPath == passPath && passPath.EndsWith(
                    Path.Combine("Year2026", "Month08", "Day07.dat"),
                    StringComparison.OrdinalIgnoreCase),
                "PHT PASS path uses original Year/Month/Day.dat hierarchy");
            Assert(passText == expectedPass,
                "PHT PASS/master records match original CP949 pipe format and append without truncation");

            var smallCounterPass = new CompletedTestResult
            {
                Started = new DateTime(2026, 7, 1, 18, 17, 0),
                Finished = new DateTime(2026, 7, 1, 18, 17, 9),
                Passed = true,
                ResultText = "PASS"
            };
            string smallCounterPath = writer.AppendProduct(model, smallCounterPass, 1);
            string smallCounterText = cp949.GetString(File.ReadAllBytes(smallCounterPath));
            Assert(smallCounterText == "|1|..|260701|181709|2607010001|||sssss0001|||||||\r\n",
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
            string productionLotText = cp949.GetString(File.ReadAllBytes(productionLotPath));
            Assert(productionLotText ==
                   "|1|..|260826|075358|2608262001|||12000204302001|||||||\r\n",
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

        board.Publish(FrameSeq(10, (1, new[] { 86 }), (2, new[] { 87 })));
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
               openRows.Any(row => row.Io == 1 && row.FaultType == "KIỂM TRA" && row.Connector == "1" && row.Pin == "1" && row.IoCnPnText == "1-1-1") &&
               openRows.Any(row => row.Io == 2 && row.FaultType == "HỞ MẠCH" && row.Connector == "1" && row.Pin == "2" && row.IoCnPnText == "2-1-2") &&
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
               vm.Faults.Any(row => row.Kind == FaultKind.Probe && row.Io == 1 && row.FaultType == "KIỂM TRA") &&
               vm.Faults.Any(row => row.Io == 2 && row.FaultType == "HỞ MẠCH"),
            "CASE B: Probe IO1 is displayed while the IO2 open row remains");

        board.Publish(FrameSeq(12));
        Assert(!vm.HasInlineProbeContacts &&
               vm.Faults.Any(row => row.Io == 2 && row.FaultType == "HỞ MẠCH"),
            "CASE C: Probe release removes only Probe presentation and keeps production open row");

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
        Assert(shortVm.Faults.Any(row => row.Kind == FaultKind.Probe && row.Io == 86) &&
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
                   row.Io == 7 &&
                   row.FaultType == "KIỂM TRA" &&
                   row.Connector.Contains("KHÔNG SỬ DỤNG IO(7)", StringComparison.Ordinal)) &&
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

    private static void TestHundredCycleScanProbeFaultStress()
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

        for (int cycle = 1; cycle <= 100; cycle++)
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

        Assert(board.FramesReceived >= 100, "Stress: FramesReceived keeps increasing");
        Assert(vm.ProductionFramesProcessed >= 100, "Stress: Probe/fault frames are not suppressed before TestEngine");
        Assert(board.IsScanning, "Stress: Product/probe processing does not stop D2XX scan");
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

        var jigFirstProduction = new ProductionSettings
        {
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50,
            PassMarkingToJigDelayMs = 0,
            ProductSettleTimeMs = 0,
            PassJigRelayFirst = true
        };
        using TestEngine jigFirstEngine = CreateEngine(out FakeBoard jigFirstBoard, jigFirstProduction);
        jigFirstEngine.SetModel(Model(("PAIR", new[] { 1, 18 })));
        jigFirstEngine.ProcessFrame(passFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        jigFirstEngine.ProcessFrame(passFrame);
        bool jigFirstOk = jigFirstEngine.CompletePassAsync([]).GetAwaiter().GetResult();
        Assert(jigFirstOk, "PASS relay workflow accepts JIG-first option");
        Assert(jigFirstBoard.Commands.IndexOf("SET:1") >= 0 &&
               jigFirstBoard.Commands.IndexOf("SET:2") >= 0 &&
               jigFirstBoard.Commands.IndexOf("SET:1") < jigFirstBoard.Commands.IndexOf("SET:2"),
            "JIG-first option runs R1 before R2");
    }

    private static void TestHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZSelfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            DateTime finished = new(2026, 8, 9, 14, 7, 8, DateTimeKind.Local);
            var record = new TestHistoryRecord
            {
                Started = finished.AddSeconds(-2),
                Finished = finished,
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
                ModelFile = @"C:\Models\WH322882.setup",
                HtdrvName = "JBZUniversalTester V15.2.0",
                OpenCount = 0,
                MeasuredResistance = 101.5,
                ResistanceMin = 100,
                ResistanceMax = 110
            };

            var store = new TestHistoryStore(Path.Combine(root, "history.db"));
            store.Add(record);
            IReadOnlyList<TestHistoryRecord> found = store.Search(new HistorySearchCriteria(
                finished.Date, finished.Date.AddDays(1), 2001, "NI375", "PASS"));
            Assert(found.Count == 1 && found[0].PartNumber == "NI375C1000" &&
                   found[0].VehicleType == "NE N EV" && found[0].ProductionCounter == 321,
                "SQLite search date/LOT/product/result and ALL13 fields");

            string csv = Path.Combine(root, "history.csv");
            HistoryExportService.ExportCsv(csv, found);
            byte[] csvBytes = File.ReadAllBytes(csv);
            Assert(csvBytes.Length >= 3 && !(csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF),
                "ALL13 CSV uses CP949 without UTF-8 BOM");
            Assert(!csvBytes.Contains((byte)'\r'), "ALL13 CSV uses LF line endings");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string[] csvLines = File.ReadAllLines(csv, Encoding.GetEncoding(949));
            Assert(csvLines[0] == "일 자,시 간,파 일,품 명,품 번,차 종,Lot,결 과,순 번,검 사 기 록,바코드,200 %,수입검사,프로그램",
                "ALL13 CSV exact 14-column header");
            Assert(csvLines[1].StartsWith("2026-08-09,14:07:06,WH322882.setup,PRODUCT,NI375C1000,NE N EV,,합격,321,검사시작 14:07:06 회로검사:PASS", StringComparison.Ordinal),
                "ALL13 CSV PASS row mapping");

            string xlsx = Path.Combine(root, "history.xlsx");
            HistoryExportService.ExportXlsx(xlsx, found);
            using ZipArchive archive = ZipFile.OpenRead(xlsx);
            string sheet = ReadEntry(archive, "xl/worksheets/sheet1.xml");
            string styles = ReadEntry(archive, "xl/styles.xml");
            Assert(sheet.Contains("<c r=\"A2\" s=\"2\"><v>", StringComparison.Ordinal), "XLSX DateTime native numeric");
            Assert(sheet.Contains("<c r=\"G2\"><v>2001</v></c>", StringComparison.Ordinal), "XLSX LOT native number");
            Assert(sheet.Contains("<c r=\"AB2\"><v>101.5</v></c>", StringComparison.Ordinal), "XLSX resistance native number");
            Assert(styles.Contains("numFmtId=\"164\"", StringComparison.Ordinal), "XLSX DateTime number format");

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
            Assert(customerText.Contains(",불량,    ,", StringComparison.Ordinal),
                "ALL13 CSV FAIL result and blank sequence");
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

        var settings = new ProductionSettings { LotNo = 2044 };
        int persistCount = 0;
        var lots = new LotSequenceService(settings, _ => persistCount++);
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

        var restartedSettings = new ProductionSettings { LotNo = settings.LotNo };
        var restarted = new LotSequenceService(restartedSettings, _ => { });
        Assert(restarted.NextLot == 2045,
            "Restart resumes from last successfully committed LOT");

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

            model = ParseThtLabelModelText();
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
            const string expectedSmallBarcode = "KL375C1000,SQDZQ8Y0001";
            Assert(smallValues["YEAR_CODE"] == "Q" &&
                   smallValues["MONTH_CODE"] == "8" &&
                   smallValues["DAY_CODE"] == "Y" &&
                   smallValues["LOT_NO"] == "0001",
                "TEM_BE resolves confirmed 2026/year, non-padded month, day 25 and four-digit LOT codes");
            Assert(smallLabel.Copies == 1 &&
                   smallLabel.Payload.Contains(
                       $"KL375C1000\nAE EV PE\nVOLTAGE_6S\n2608251WH\n{expectedSmallBarcode}\nP1",
                       StringComparison.Ordinal) &&
                   smallLabel.Data.Barcode == expectedSmallBarcode &&
                   smallLabel.Data.BarcodePrint == expectedSmallBarcode,
                "TEM_BE DATA5 and immutable barcode snapshot use the same SQDZ canonical value from THT data");

            Assert(SmallLabel(25, new DateTime(2026, 8, 25)).Data.Barcode.EndsWith("0025", StringComparison.Ordinal),
                "TEM_BE formats LOT 25 as 0025");
            Assert(SmallLabel(7001, new DateTime(2026, 8, 25)).Data.Barcode.EndsWith("7001", StringComparison.Ordinal),
                "TEM_BE keeps four-digit LOT 7001 unchanged");
            Assert(SmallLabel(1, new DateTime(2026, 8, 1)).Data.Barcode == "KL375C1000,SQDZQ8A0001",
                "TEM_BE maps day 1 to A");
            Assert(SmallLabel(1, new DateTime(2026, 8, 26)).Data.Barcode == "KL375C1000,SQDZQ8Z0001",
                "TEM_BE maps day 26 to Z");

            InvalidDataException undefinedDay = AssertThrows<InvalidDataException>(
                () => SmallLabel(1, new DateTime(2026, 8, 27)),
                "TEM_BE day 27 must be rejected before creating a print request");
            Assert(undefinedDay.Message.Contains("LABEL_DATE_CODE_UNDEFINED", StringComparison.Ordinal) &&
                   undefinedDay.Message.Contains("Day=27", StringComparison.Ordinal),
                "TEM_BE day 27 reports the undefined date-code marker and exact day");

            InvalidDataException undefinedYear = AssertThrows<InvalidDataException>(
                () => SmallLabel(1, new DateTime(2027, 8, 25)),
                "TEM_BE year 2027 must be rejected before creating a print request");
            Assert(undefinedYear.Message.Contains("LABEL_DATE_CODE_UNDEFINED", StringComparison.Ordinal) &&
                   undefinedYear.Message.Contains("Year=2027", StringComparison.Ordinal),
                "TEM_BE year 2027 reports the undefined date-code marker and exact year");

            string smallDuplicatePath = Path.Combine(root, "LastSmallBarcode.txt");
            var smallDuplicateGuard = new LabelDuplicateGuard(smallDuplicatePath);
            smallDuplicateGuard.RecordSuccessfulPrint(smallLabel, testedAt);
            Assert(smallDuplicateGuard.LoadLast()?.Barcode == expectedSmallBarcode,
                "TEM_BE duplicate state persists the physical SQDZ barcode");

            model.PartNumber = "1200020430";
            model.ProductName = "BMS EXT";
            model.Eco = "US4 HEV";
            model.VehicleType = model.Eco;
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
        history.LabelProfile = request.FormatName;
        history.Printer = request.Printer;
        history.LabelCopies = request.Copies;

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
            Assert(store.TryBeginFirstPrint(id, request.CycleId), "Explicit retry reuses the failed cycle/LOT transaction");
            Assert(!store.TryBeginFirstPrint(id, request.CycleId), "Concurrent retry callback is blocked while Pending");
            store.UpdateLabelPrintOutcome(
                id,
                request.CycleId,
                LabelPrintStatus.Printed,
                finished.AddMilliseconds(250),
                "software-test");
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

    private static ProductModel ParseThtLabelModelText()
    {
        const string modelText =
            "파트번호\t파트명\tECO\tNCO\tALC\n" +
            "KL375C1000\tVOLTAGE_6S\tAE EV PE\tNCO-7\tALC-FROM-THT\n\n" +
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
        public BoardCapacity Capacity { get; private set; } = BoardCapacity.Create(10);
        public DateTime LastFrameTimestampUtc { get; private set; } = DateTime.UtcNow;
        public long LastFrameSequence { get; private set; }
        public long FramesReceived { get; private set; }
        public CancellationToken? LastStartScanToken { get; private set; }
        private event EventHandler<ScanFrame>? FrameReceivedCore;
        public event EventHandler<ScanFrame>? FrameReceived { add { FrameReceivedCore += value; } remove { FrameReceivedCore -= value; } }
        public event EventHandler<string>? Log { add { } remove { } }
        public void Publish(ScanFrame frame)
        {
            LastFrameTimestampUtc = DateTime.UtcNow;
            LastFrameSequence = frame.Sequence;
            FramesReceived++;
            FrameReceivedCore?.Invoke(this, frame);
        }
        public Task<BoardConnectionInfo> ConnectAsync(CancellationToken ct = default) => Task.FromResult(new BoardConnectionInfo("Fake", "Fake"));
        public Task DisconnectAsync() { IsScanning = false; return Task.CompletedTask; }
        public Task HandshakeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetClearAsync(CancellationToken ct = default) { Commands.Add("RESET"); return Task.CompletedTask; }
        public void ConfigureScanRange(int maxIo) { }
        public Task StartScanAsync(BoardScanMode mode = BoardScanMode.Production, CancellationToken ct = default) { IsScanning = true; CurrentScanMode = mode; LastStartScanToken = ct; Commands.Add("START"); return Task.CompletedTask; }
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
