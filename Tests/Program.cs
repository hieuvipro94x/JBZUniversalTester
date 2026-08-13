using System.IO.Compression;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
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
            ("Continuity/open/wrong/splice engine", TestEngineVectors),
            ("Production PASS gate minimal latency", TestProductionPassGateMinimalLatency),
            ("THT column semantics and string wire topology", TestThtColumnSemantics),
            ("Connector grouping and clip latch memory", TestConnectorGroupingAndClipLatchMemory),
            ("Relay PASS/FAIL safe ordering", TestRelayOrdering),
            ("History SQLite/search/CSV/XLSX native types", TestHistory),
            ("ALL6 label data order", TestLabel),
            ("PASS label snapshot/idempotency/traceability", TestLabelPrintingSafety),
            ("Board connection recovery guards", TestBoardConnectionRecoveryGuards),
            ("Probe/resistance transition regression guards", TestProbeResistanceTransitionRegression),
            ("Resistance unit display scaling", TestResistanceUnitDisplayScaling),
            ("Auto resistance stability flow", TestAutoResistanceStabilityFlow),
            ("Standard product picker filter", TestProductPickerFilter),
            ("Fault display localization and detail", TestFaultDisplayFormatter),
            ("UI brush cache and engine change filter", TestUiPerformanceGuards),
            ("Continuous scan frame accounting and UI coalesce", TestContinuousScanFrameAccounting),
            ("Inline probe realtime display guards", TestInlineProbeRealtimeDisplayGuards),
            ("Final TestView status/master/device fault guards", TestFinalTestStatusGuards),
            ("Manual mode relay interlock and production lock", TestManualModeInterlock),
            ("Production fault debounce and jig contact state", TestProductionFaultConfirmation),
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
        Assert(resistanceHighCustomer.Deviation == "+0.320 kΩ", "Resistance high deviation");

        var resistanceLow = new FaultDetail
        {
            Type = ProductFaultType.ResistanceOutOfRange,
            ResistanceMin = 950,
            ResistanceMax = 1050,
            MeasuredResistance = 900
        };
        Assert(FaultDisplayFormatter.FormatCustomer(resistanceLow).Assessment == "BELOW LOWER LIMIT", "Resistance low assessment");
        Assert(FaultDisplayFormatter.FormatCustomer(resistanceLow).Deviation == "-0.050 kΩ", "Resistance low deviation");

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
        AssertWireColorVisibility("R", true, false, false, false);
        AssertWireColorVisibility("R/L", true, true, false, false);
        AssertWireColorVisibility("R/L/Y", true, true, true, false);
        AssertWireColorVisibility("R/L/Y/B", true, true, true, true);
        AssertWireColorVisibility("W", true, false, false, false);

        var wrongRow = new FaultRow { Kind = FaultKind.WrongWiring, Color = "B/Br" };
        Assert(BrushHex(wrongRow.RowBackgroundBrush) == "#3446A8", "Wrong row uses Pi blue");
        Assert(BrushHex(wrongRow.RowForegroundBrush) == "#FFFFFF", "Wrong row text is white");
        Assert(BrushHex(wrongRow.Color1Brush) == "#101010" && BrushHex(wrongRow.Color2Brush) == "#8A4300",
            "Wrong row color cells override semantic row background");

        var probeRow = new FaultRow { Kind = FaultKind.Probe, Color = "B/L" };
        Assert(BrushHex(probeRow.RowBackgroundBrush) == "#BDEEEE", "Probe row uses Pi cyan row");
        Assert(BrushHex(probeRow.Color1Brush) == "#101010" && BrushHex(probeRow.Color2Brush) == "#0077FF",
            "Probe row color cells override probe background");

        var visibility = new IntEqualsToVisibilityConverter();
        Assert(
            Equals(visibility.Convert(1, typeof(Visibility), "1", CultureInfo.InvariantCulture), Visibility.Visible) &&
            Equals(visibility.Convert(0, typeof(Visibility), "1", CultureInfo.InvariantCulture), Visibility.Collapsed),
            "Resistance table visibility follows SelectedOperationTabIndex");

        var openResistance = new ResistanceResult
        {
            Name = "KIỂM TRA ĐIỆN TRỞ",
            Channel = 3,
            MinOhm = 3000,
            MaxOhm = 5000,
            IsOpen = true,
            Passed = false
        };
        Assert(openResistance.ChannelText == "CH3", "Resistance channel displays as CH number");
        Assert(openResistance.Display == "OPEN" && openResistance.ResultText == "FAIL",
            "Keysight open resistance displays OPEN value and FAIL result");

        string testWindowXaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "TestWindow.xaml"));
        Assert(testWindowXaml.Contains("x:Name=\"ResistanceGrid\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Header=\"LO&#7840;I\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Header=\"K&#202;NH\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Header=\"MIN\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Header=\"GI&#193; TR&#7882;\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Header=\"MAX\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Header=\"K&#7870;T QU&#7842;\"", StringComparison.Ordinal),
            "Resistance grid uses the compact six-column production layout");
        Assert(!testWindowXaml.Contains("Temperature", StringComparison.OrdinalIgnoreCase) &&
               !testWindowXaml.Contains("NHI&#7878;T", StringComparison.OrdinalIgnoreCase),
            "Resistance grid must not include temperature column");
        Assert(!testWindowXaml.Contains("K&#7871;t n&#7889;i m&#7841;ng", StringComparison.OrdinalIgnoreCase) &&
               !testWindowXaml.Contains("NetworkConnectionTextConverter", StringComparison.Ordinal) &&
               !testWindowXaml.Contains("D&#226;y d&#7853;p n&#7889;i", StringComparison.OrdinalIgnoreCase) &&
               testWindowXaml.Contains("Header=\"Lo&#7841;i\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Header=\"Tr&#7841;ng th&#225;i\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Binding=\"{Binding Status}\"", StringComparison.Ordinal),
            "Production table removes the network connection presentation column completely");
        Assert(testWindowXaml.Contains("RowHeight\" Value=\"34\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Width=\"52\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Height=\"28\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Width=\"54\" MinWidth=\"54\" MaxWidth=\"54\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Visibility=\"{Binding HasColor1, Converter={StaticResource BoolToVisibilityConverter}}\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Visibility=\"{Binding HasColor4, Converter={StaticResource BoolToVisibilityConverter}}\"", StringComparison.Ordinal),
            "Production color blocks are wide, close together, and empty slots do not draw fake color blocks");
        Assert(testWindowXaml.Contains("CanUserSortColumns\" Value=\"False\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("CanUserReorderColumns\" Value=\"False\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("CanUserResizeColumns\" Value=\"False\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("Header=\"CONNECTOR\"", StringComparison.Ordinal) &&
               testWindowXaml.Contains("CanUserSort=\"False\"", StringComparison.Ordinal) &&
               !testWindowXaml.Contains("SortMemberPath", StringComparison.Ordinal),
            "Production grid locks operator sorting/reordering/resizing and shows CONNECTOR header");
        string testWindowCode = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "TestWindow.xaml.cs"));
        Assert(testWindowCode.Contains("ConnectorColumn.Visibility = Visibility.Visible;", StringComparison.Ordinal) &&
               !testWindowCode.Contains("ConnectorColumn.Visibility = viewModel.ShowConnector", StringComparison.Ordinal),
            "Production TestWindow must always show the CONNECTOR column");

        var disabled = new ResistanceChannelEditor(
            new ResistanceChannelSetting { Enabled = true, Name = "R3", Channel = 3, MinOhm = 1, MaxOhm = 2 },
            3)
        {
            ChannelSelection = 0
        }.ToSetting();
        Assert(!disabled.Enabled && disabled.Channel == 0 && disabled.Name == "R3",
            "Resistance UI channel 'Không dùng' saves disabled without losing internal name");

        var enabled = new ResistanceChannelEditor(
            new ResistanceChannelSetting { Enabled = false, Name = "R1", Channel = 0, MinOhm = 0.1, MaxOhm = 0.5 },
            1)
        {
            ChannelSelection = 2
        }.ToSetting();
        Assert(enabled.Enabled && enabled.Channel == 2 && enabled.MinOhm == 0.1 && enabled.MaxOhm == 0.5,
            "Resistance UI active channel saves enabled and preserves min/max");

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

    private static void TestContinuousScanFrameAccounting()
    {
        var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
        TestViewModel vm = CreateTestViewModel(settings, out FakeBoard board);
        ProductModel model = Model(("PAIR-A", new[] { 1, 18 }), ("PAIR-B", new[] { 2, 8 }));
        vm.LoadPreparedModelAsync(model).GetAwaiter().GetResult();
        vm.StartProductionTestAsync().GetAwaiter().GetResult();

        const int FrameCount = 1000;
        for (int index = 1; index <= FrameCount; index++)
            board.Publish(FrameSeq(index, (1, new[] { 18 })));

        Assert(board.IsScanning && board.CurrentScanMode == BoardScanMode.Production,
            "Continuous production scan remains running while partial networks update UI");
        Assert(vm.ProductionFramesReceived == FrameCount &&
               vm.ProductionFramesProcessed == FrameCount &&
               vm.ProductionFramesDropped == 0,
            "Every parsed production frame is delivered to TestEngine with zero business frame drops");
        Assert(vm.ProductionFramesRoutedToProbe == 0,
            "Normal continuity frames are not routed to Probe");
        Assert(vm.PassedNetworkCount == 1 &&
               vm.ExpectedNetworkCount == 2 &&
               vm.Faults.Select(row => row.IoText).SequenceEqual(["2", "8"]),
            "UI may show only the remaining endpoint rows while engine continues processing all frames");
        Assert(vm.EngineUiUpdatesRendered < FrameCount,
            "Identical scan frames are coalesced at UI/state level, not dropped before TestEngine");
    }

    private static void TestInlineProbeRealtimeDisplayGuards()
    {
        var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
        TestViewModel vm = CreateTestViewModel(settings, out FakeBoard board);
        ProductModel model = TopologyModel(
            new Terminal(23, "C3", "5", "64", "BG2", "R/Y"),
            new Terminal(55, "C8", "2", "64", "BG2", "R/Y"),
            new Terminal(40, "C5", "1", "64", "SHORT-A", "B"),
            new Terminal(41, "C5", "2", "64", "SHORT-B", "W"));
        vm.LoadPreparedModelAsync(model).GetAwaiter().GetResult();
        vm.StartProductionTestAsync().GetAwaiter().GetResult();
        board.Commands.Clear();

        board.Publish(ProbeFanInFrame(10, 23, Enumerable.Range(1, 16).Where(io => io != 23)));
        FaultRow probe = vm.Faults.Single(row => row.Kind == FaultKind.Probe);
        Assert(probe.Io == 23 &&
               probe.Connector == "C3" &&
               probe.Pin == "5" &&
               probe.WireName == "BG2" &&
               probe.Color == "R/Y",
            "Probe touch shows the exact mapped endpoint row in the main TestWindow table");
        Assert(vm.ProbeContacts.Count == 1 &&
               vm.ProbeContacts[0].Io == 23 &&
               board.IsScanning &&
               !board.Commands.Contains("STOP") &&
               vm.WrongCount == 0 &&
               vm.ShortCount == 0 &&
               vm.Fail == 0 &&
               vm.Total == 0,
            "Probe touch does not stop scan, raise faults, counters, relay, or product result");

        board.Publish(FrameSeq(11));
        Assert(!vm.Faults.Any(row => row.Kind == FaultKind.Probe) &&
               vm.ProbeContacts.Count == 0 &&
               board.IsScanning &&
               !board.Commands.Contains("STOP"),
            "Probe release removes the probe row without resetting scan or production state");

        using TestEngine shortEngine = CreateEngine(out _, new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            WrongConnectionConfirmMs = 0,
            ShortCircuitConfirmMs = 0,
            ProductSettleTimeMs = 0
        });
        shortEngine.SetModel(model);
        shortEngine.ProcessFrame(FrameSeq(12, (40, new[] { 41 })));
        Assert(shortEngine.GetPassGateDiagnostics().WrongCandidateCount +
               shortEngine.GetPassGateDiagnostics().ShortCandidateCount > 0,
            "Real product wrong/short remains detected; probe handling does not globally suppress fault evaluation");
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
        statusVm.State = "CHƯA ĐẠT";
        Assert(statusVm.ResultStatusText == "KHÔNG ĐẠT" && statusVm.StateBackground == "#C62828" && statusVm.StateForeground == "#FFFFFF",
            "FAIL status mapping");
        statusVm.State = "ĐANG KIỂM TRA...";
        Assert(statusVm.ResultStatusText == "ĐANG TEST" && statusVm.StateBackground == "#FFF3A0" && statusVm.StateForeground == "#222222",
            "Testing status mapping");

        string xaml = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "TestWindow.xaml"));
        Assert(!xaml.Contains("ProbeToggleText", StringComparison.Ordinal) &&
               !xaml.Contains("HIỆN DÒ CHÂN", StringComparison.Ordinal) &&
               !xaml.Contains("ẨN DÒ CHÂN", StringComparison.Ordinal),
            "Production TestView must not expose a Probe toggle button");

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

    private static void AssertWireColorCells(string code, string one, string two, string three, string four)
    {
        var row = new FaultRow { Color = code };
        Assert(row.WireColorText == code, $"Wire color text preserved for '{code}'");
        Assert(BrushHex(row.Color1Brush) == one, $"Color #1 for '{code}'");
        Assert(BrushHex(row.Color2Brush) == two, $"Color #2 for '{code}'");
        Assert(BrushHex(row.Color3Brush) == three, $"Color #3 for '{code}'");
        Assert(BrushHex(row.Color4Brush) == four, $"Color #4 for '{code}'");
    }

    private static void AssertWireColorVisibility(string code, bool one, bool two, bool three, bool four)
    {
        var row = new FaultRow { Color = code };
        Assert(row.HasColor1 == one, $"Color #1 visibility for '{code}'");
        Assert(row.HasColor2 == two, $"Color #2 visibility for '{code}'");
        Assert(row.HasColor3 == three, $"Color #3 visibility for '{code}'");
        Assert(row.HasColor4 == four, $"Color #4 visibility for '{code}'");
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
        IReadOnlyList<FaultRow> initialPairRows = engine.BuildRows()
            .Where(row => row.Kind == FaultKind.MissingConnection)
            .ToArray();
        Assert(initialPairRows.Count == 2 &&
               initialPairRows.All(row => row.ProductFaultType == ProductFaultType.None) &&
               initialPairRows.Select(row => row.IoText).SequenceEqual(["1", "18"]) &&
               initialPairRows.Select(row => row.Pin).SequenceEqual(["1", "18"]) &&
               initialPairRows.All(row => row.WireName == "PAIR") &&
               initialPairRows.All(row => !row.IoText.Contains("<->", StringComparison.Ordinal) &&
                                          !row.Pin.Contains("<->", StringComparison.Ordinal)),
            "Model load shows one display-only endpoint row per THT pin, not one merged network row");
        ScanFrame pairPassFrame = Frame((1, new[] { 18 }));
        engine.ProcessFrame(pairPassFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(pairPassFrame);
        IReadOnlyList<FaultRow> passedPairRows = engine.BuildRows();
        Assert(engine.ContinuityPassed &&
               !engine.HasWiringFault &&
               passedPairRows.Count == 0,
            "Expected IO1-IO18 passes and both endpoint rows disappear from the visible production table");

        engine.ProcessFrame(Frame());
        IReadOnlyList<FaultRow> reopenedPairRows = engine.BuildRows();
        Assert(!engine.ContinuityPassed &&
               reopenedPairRows.Count(row => row.Kind == FaultKind.MissingConnection) == 2 &&
               reopenedPairRows.Select(row => row.IoText).SequenceEqual(["1", "18"]),
            "Opening a previously passed pair restores both endpoint rows in model order");

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
            "Missing IO1-IO18 is two display-only pending endpoint rows");
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
               engine.BuildRows().Count(row => row.Kind == FaultKind.MissingConnection) == 2,
            "Full source coverage with missing endpoint remains display-only endpoint rows");

        ProductModel splice = Model(("SPLICE", new[] { 5, 20, 33 }));
        engine.SetModel(splice);
        ScanFrame spliceOpenFrame = Frame((5, new[] { 20 }));
        engine.ProcessFrame(spliceOpenFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(spliceOpenFrame);
        IReadOnlyList<FaultDetail> confirmedSpliceOpen = engine.BuildConfirmedOpenFaults();
        Assert(confirmedSpliceOpen.Count == 0 &&
               !engine.BuildRows().Any(row => row.Kind == FaultKind.Open) &&
               engine.BuildRows().Count(row => row.Kind == FaultKind.MissingConnection) == 3,
            "Splice missing target is display-only endpoint rows, not production OPEN");

        engine.SetModel(splice);
        ScanFrame splicePassFrame = Frame((5, new[] { 20, 33 }));
        engine.ProcessFrame(splicePassFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(splicePassFrame);
        Assert(engine.ContinuityPassed &&
               !engine.HasWiringFault &&
               engine.BuildRows().Count == 0,
            "Splice component passes and all endpoint rows disappear from the visible table");

        engine.ProcessFrame(spliceOpenFrame);
        IReadOnlyList<FaultRow> reopenedSpliceRows = engine.BuildRows();
        Assert(reopenedSpliceRows.Count(row => row.Kind == FaultKind.MissingConnection) == 3 &&
               reopenedSpliceRows.Select(row => row.IoText).SequenceEqual(["5", "20", "33"]),
            "Removing a completed splice re-adds all pending endpoint rows in model order");

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
            Assert(a.DailyTestCount == 2 && a.MonthlyTestCount == 2 && a.LifetimeTestCount == 2, "Model A production periods/lifetime");
            Assert(a.ProbeCycleCount == 1 && b.ProbeCycleCount == 0 && b.LifetimeTestCount == 1, "Per-model counter isolation");

            var restarted = new ProductionStatisticsStore(path, clock);
            Assert(restarted.Get(modelA).ProbeCycleCount == 1 && restarted.Get(modelA).LifetimeTestCount == 2, "Counters persist after restart");

            clock.Advance(TimeSpan.FromDays(1));
            Assert(restarted.Get(modelA).DailyTestCount == 0 && restarted.Get(modelA).MonthlyTestCount == 2, "Daily period rolls without resetting month/probe");
            restarted.Record(modelA, true, 4, "PASS");
            Assert(restarted.Get(modelA).DailyTestCount == 1 && restarted.Get(modelA).MonthlyTestCount == 3, "New day increments correct logical period");

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
        Assert(reverseOneNetEngine.ContinuityPassed,
            "Two-pin continuity PASSes when the board reports the electrical edge in reverse direction");

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
               criticalInitialRows.Count(row => row.Kind == FaultKind.MissingConnection) == 2 &&
               criticalInitialRows.Select(row => row.IoText).SequenceEqual(["1", "2"]) &&
               criticalInitialRows.Select(row => row.Pin).SequenceEqual(["1", "2"]) &&
               criticalInitialRows.All(row => row.WireName == "~1") &&
               criticalInitialRows.All(row => !row.IoText.Contains("<->", StringComparison.Ordinal) &&
                                              !row.Pin.Contains("<->", StringComparison.Ordinal)),
            "Critical topology: two THT endpoints in one wire/net build two endpoint display rows, not product faults");

        criticalTopologyEngine.ProcessFrame(FrameSeq(2, (2, new[] { 1 })));
        PassGateDiagnostics criticalPassed = criticalTopologyEngine.GetPassGateDiagnostics();
        IReadOnlyList<FaultRow> criticalPassedRows = criticalTopologyEngine.BuildRows();
        Assert(criticalTopologyEngine.ExpectedNetCount == 1 &&
               criticalPassed.PassedNetCount == 1 &&
               criticalTopologyEngine.ContinuityPassed &&
               !criticalTopologyEngine.HasWiringFault &&
               criticalPassedRows.Count == 0,
            "Critical topology: IO1/IO2 makes the single logical network present and hides both endpoint rows");

        bool criticalPassCommitted = criticalTopologyEngine.CompletePassAsync([])
            .GetAwaiter()
            .GetResult();
        Assert(criticalPassCommitted &&
               criticalBoard.Commands.Contains("SET:2") &&
               criticalBoard.Commands.Contains("SET:1"),
            "Critical topology: no-resistance MasterMinimum=0 equivalent can commit PASS immediately after continuity");

        criticalTopologyEngine.ProcessFrame(FrameSeq(3));
        IReadOnlyList<FaultRow> criticalReopenedRows = criticalTopologyEngine.BuildRows();
        Assert(!criticalTopologyEngine.ContinuityPassed &&
               criticalReopenedRows.Count(row => row.Kind == FaultKind.MissingConnection) == 2 &&
               criticalReopenedRows.Select(row => row.IoText).SequenceEqual(["1", "2"]),
            "Critical topology: when the network opens again, endpoint rows return without reload");

        ProductModel twoWireModel = Model(("PAIR-A", new[] { 1, 86 }), ("PAIR-B", new[] { 2, 87 }));
        using TestEngine twoNetEngine = CreateEngine(out _, slowConfirmProduction);
        twoNetEngine.SetModel(twoWireModel);
        Assert(twoNetEngine.ExpectedNetCount == 2, "Two-wire model builds exactly two expected production networks");

        twoNetEngine.ProcessFrame(FrameSeq(2, (1, new[] { 86 })));
        PassGateDiagnostics partialGate = twoNetEngine.GetPassGateDiagnostics();
        IReadOnlyList<FaultRow> partialRows = twoNetEngine.BuildRows();
        Assert(!twoNetEngine.ContinuityPassed &&
               partialGate.PassedNetCount == 1 &&
               partialGate.RemainingNetworks.Count == 1 &&
               partialGate.RemainingNetworks.Single().Display.Contains("IO2<->IO87", StringComparison.Ordinal) &&
               partialRows.Select(row => row.IoText).SequenceEqual(["2", "87"]),
            "Two-wire model at 1/2 blocks PASS and shows only the remaining endpoint rows");

        twoNetEngine.ProcessFrame(FrameSeq(3));
        IReadOnlyList<FaultRow> reopenedTwoNetRows = twoNetEngine.BuildRows();
        Assert(reopenedTwoNetRows.Select(row => row.IoText).SequenceEqual(["1", "86", "2", "87"]),
            "Two-wire model restores opened network endpoint rows at their original positions");

        twoNetEngine.ProcessFrame(FrameSeq(4, (1, new[] { 86 }), (2, new[] { 87 })));
        PassGateDiagnostics cleanGate = twoNetEngine.GetPassGateDiagnostics();
        Assert(twoNetEngine.ContinuityPassed &&
               cleanGate.PassedNetCount == 2 &&
               cleanGate.RemainingNetworks.Count == 0 &&
               twoNetEngine.BuildRows().Count == 0,
            "Two-wire model reaches 2/2 PASS and hides all endpoint rows on the first full complete frame");

        using TestEngine wrongEngine = CreateEngine(out _, slowConfirmProduction);
        wrongEngine.SetModel(twoWireModel);
        wrongEngine.ProcessFrame(FrameSeq(4, (1, new[] { 86, 87 }), (2, new[] { 87 })));
        PassGateDiagnostics wrongGate = wrongEngine.GetPassGateDiagnostics();
        Assert(!wrongEngine.ContinuityPassed &&
               wrongGate.PassedNetCount == 2 &&
               wrongGate.WrongCandidateCount > 0 &&
               wrongGate.WrongConfirmedCount == 0 &&
               wrongEngine.BuildRows().Count > 0,
            "Wrong candidate blocks PASS immediately and does not hide rows like a clean PASS");

        using TestEngine shortEngine = CreateEngine(out _, slowConfirmProduction);
        shortEngine.SetModel(twoWireModel);
        shortEngine.ProcessFrame(FrameSeq(5, (1, new[] { 86 }), (2, new[] { 87 }), (86, new[] { 87 })));
        PassGateDiagnostics shortGate = shortEngine.GetPassGateDiagnostics();
        Assert(!shortEngine.ContinuityPassed &&
               shortGate.PassedNetCount == 2 &&
               shortGate.ShortCandidateCount > 0 &&
               shortGate.ShortConfirmedCount == 0 &&
               shortEngine.BuildRows().Count > 0,
            "Short candidate blocks PASS immediately and does not hide rows like a clean PASS");

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
        Assert(suppliedEngine.GetPassGateDiagnostics().PassedNetCount == 1 &&
               suppliedEngine.ContinuityPassed &&
               !suppliedEngine.HasWiringFault &&
               !suppliedEngine.BuildRows().Any(row => row.Kind == FaultKind.MissingConnection),
            "IO1<->IO2 presents the one logical WireName network and removes missing row");

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

    private static void TestConnectorGroupingAndClipLatchMemory()
    {
        using TestEngine groupingEngine = CreateEngine(out _);
        ProductModel grouping = TopologyModel(
            new Terminal(1, "1", "1", "12", "Z"),
            new Terminal(60, "6", "5", "12", "Z"),
            new Terminal(2, "1", "2", "12", "X"),
            new Terminal(59, "6", "4", "12", "X"),
            new Terminal(10, "2", "1", "12", "Y"),
            new Terminal(70, "7", "1", "12", "Y"),
            new Terminal(20, "10", "1", "12", "W"),
            new Terminal(80, "10", "2", "12", "W"));
        groupingEngine.SetModel(grouping);

        string[] initialConnectors = groupingEngine.BuildRows()
            .Where(row => row.Kind == FaultKind.MissingConnection)
            .Select(row => row.Connector)
            .ToArray();
        Assert(initialConnectors.SequenceEqual(["1", "6", "1", "6", "2", "7", "10", "10"]),
            "Production rows are ordered by connector relation group, then network, then endpoint, not global WireName alphabet");

        using TestEngine referenceEngine = CreateEngine(out _);
        ProductModel reference = TopologyModel(
            new Terminal(71, "5", "1", "12", "M1C8"),
            new Terminal(263, "18", "3", "12", "M1C8"),
            new Terminal(160, "5", "2", "12", "M1C6"),
            new Terminal(254, "18", "8", "12", "M1C6"),
            new Terminal(338, "5", "3", "12", "M1C0"),
            new Terminal(379, "18", "6", "12", "M1C0"),
            new Terminal(420, "CN1", "6", "12", "M2C1"),
            new Terminal(430, "CN7", "3", "12", "M2C1"),
            new Terminal(421, "CN1", "7", "12", "M2C2"),
            new Terminal(431, "CN7", "8", "12", "M2C2"));
        referenceEngine.SetModel(reference);
        IReadOnlyList<FaultRow> referenceRows = referenceEngine.BuildRows()
            .Where(row => row.Kind == FaultKind.MissingConnection)
            .ToArray();
        Assert(referenceRows.Select(row => $"{row.Connector}|{row.Pin}|{row.WireName}").SequenceEqual([
                   "5|1|M1C8",
                   "18|3|M1C8",
                   "5|2|M1C6",
                   "18|8|M1C6",
                   "5|3|M1C0",
                   "18|6|M1C0",
                   "CN1|6|M2C1",
                   "CN7|3|M2C1",
                   "CN1|7|M2C2",
                   "CN7|8|M2C2"
               ]),
            "Connector relation pairs keep raw connector text and show peer endpoints as adjacent rows for arbitrary connector names");

        using TestEngine normalEngine = CreateEngine(out _);
        ProductModel normal = TopologyModel(
            new Terminal(1, "1", "1", "2", "NORMAL"),
            new Terminal(2, "2", "1", "2", "NORMAL"));
        normalEngine.SetModel(normal);
        normalEngine.ProcessFrame(FrameSeq(1, (1, new[] { 2 })));
        Assert(normalEngine.BuildRows().Count == 0, "Normal network hides after connected PASS");
        normalEngine.ProcessFrame(FrameSeq(2));
        Assert(normalEngine.BuildRows().Select(row => row.IoText).SequenceEqual(["1", "2"]),
            "Normal network reappears when opened again");

        using TestEngine clipEngine = CreateEngine(out _);
        ProductModel clip = ClipModel();
        clipEngine.SetModel(clip);
        Assert(ClipVisibleNames(clipEngine).SequenceEqual(["A0", "a1", "a2", "a3", "a4", "a5"]),
            "CLIP starts with A0 common and branch positions visible in THT order");

        clipEngine.ProcessFrame(FrameSeq(10, (100, new[] { 101 })));
        Assert(ClipVisibleNames(clipEngine).SequenceEqual(["a2", "a3", "a4", "a5"]) &&
               clipEngine.GetPassGateDiagnostics().PassedNetCount == 1,
            "A0->A1 latches only A1 and does not auto-latch other clip positions");

        clipEngine.ProcessFrame(FrameSeq(11));
        Assert(ClipVisibleNames(clipEngine).SequenceEqual(["a2", "a3", "a4", "a5"]),
            "Latched clip A1 does not reappear after the electrical edge opens in the same cycle");

        clipEngine.Reset();
        Assert(ClipVisibleNames(clipEngine).SequenceEqual(["a2", "a3", "a4", "a5"]),
            "Technical Reset/scan restart does not clear clip latch memory");
        Assert(clipEngine.GetPassGateDiagnostics().PassedNetCount == 1 &&
               clipEngine.MissingConnectionCount == 4,
            "Latched clip remains counted as passed after technical reset");

        clipEngine.ProcessFrame(FrameSeq(12, (100, new[] { 103 })));
        Assert(ClipVisibleNames(clipEngine).SequenceEqual(["a2", "a4", "a5"]) &&
               clipEngine.GetPassGateDiagnostics().PassedNetCount == 2,
            "Second clip position latches independently");

        clipEngine.ResetProductCycle();
        Assert(ClipVisibleNames(clipEngine).SequenceEqual(["A0", "a1", "a2", "a3", "a4", "a5"]) &&
               clipEngine.GetPassGateDiagnostics().PassedNetCount == 0,
            "ProductRemovedConfirmed/new cycle clears ClipMemory and restores all clip rows");

        clipEngine.ProcessFrame(FrameSeq(13, (100, new[] { 101 })));
        clipEngine.SetModel(ClipModel("MODEL-B"));
        Assert(ClipVisibleNames(clipEngine).SequenceEqual(["A0", "a1", "a2", "a3", "a4", "a5"]),
            "Model change clears clip latch state and prevents leakage into the next model");

        IReadOnlyList<FaultRow> clipRows = clipEngine.BuildRows();
        Assert(clipRows.Select(row => row.IoText).SequenceEqual(["100", "101", "102", "103", "104", "105"]) &&
               clipRows.All(row => row.Status == "CHỜ KẾT NỐI") &&
               clipRows.All(row => !row.Splice.Contains("A0(IO", StringComparison.Ordinal) &&
                                   !row.Splice.Contains("->", StringComparison.Ordinal) &&
                                   !row.WireName.StartsWith("CLIP ", StringComparison.OrdinalIgnoreCase)),
            "Production CLIP rows show A0 plus endpoint I/O and THT branch names without A0/common debug text");

        using TestEngine clipTopEngine = CreateEngine(out _);
        ProductModel clipTop = ClipModelWithNormalRows();
        clipTopEngine.SetModel(clipTop);
        IReadOnlyList<FaultRow> clipTopRows = clipTopEngine.BuildRows();
        Assert(clipTopRows.Select(row => row.WireName).SequenceEqual(["A0", "a1", "a2", "BG01", "BG01"]) &&
               clipTopRows.Select(row => row.IoText).SequenceEqual(["201", "202", "203", "1", "86"]),
            "Unfinished CLIP rows stay at the top before normal network rows regardless of normal THT order");

        clipTopEngine.ProcessFrame(FrameSeq(20, (201, new[] { 202 })));
        Assert(clipTopEngine.BuildRows().Select(row => row.WireName).SequenceEqual(["a2", "BG01", "BG01"]),
            "After first CLIP latch, A0 and the confirmed branch hide while unfinished CLIP remains above normal rows");

        clipTopEngine.ProcessFrame(FrameSeq(21));
        Assert(clipTopEngine.BuildRows().Select(row => row.WireName).SequenceEqual(["a2", "BG01", "BG01"]),
            "Latched CLIP branch and A0 do not restore when electrical contact opens in the same product cycle");

        clipTopEngine.ProcessFrame(FrameSeq(22, (201, new[] { 203 })));
        Assert(clipTopEngine.BuildRows().Select(row => row.WireName).SequenceEqual(["BG01", "BG01"]),
            "When all CLIP branches are latched, only normal network rows remain");

        clipTopEngine.ResetProductCycle();
        Assert(clipTopEngine.BuildRows().Select(row => row.WireName).SequenceEqual(["A0", "a1", "a2", "BG01", "BG01"]),
            "ProductRemoved/new cycle restores the full CLIP display block including A0");
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
    }

    private static void TestBoardConnectionRecoveryGuards()
    {
        using TestEngine engine = CreateEngine(out FakeBoard board);
        engine.SetModel(Model(("PAIR", new[] { 1, 18 })));

        for (int index = 0; index < 100; index++)
            engine.EjectFaultProductAsync().GetAwaiter().GetResult();

        Assert(board.DisconnectCount == 0, "FAIL confirmation relay must not disconnect the board");
        Assert(board.Commands.Count(command => command == "SET:1") == 100,
            "100 FAIL confirmations pulse Relay 1 exactly once each");
        Assert(!board.Commands.Contains("SET:2"),
            "100 FAIL confirmations never pulse Relay 2 MARKING");
        Assert(board.Commands.Last() == "OFF",
            "Repeated FAIL confirmations end with all relays OFF");

        string root = Environment.CurrentDirectory;
        string d2xx = File.ReadAllText(Path.Combine(root, "Services", "D2xxBoardTransport.cs"));
        string vm = File.ReadAllText(Path.Combine(root, "ViewModels", "TestViewModel.cs"));

        Assert(d2xx.Contains("D2XX PREPARE INVALIDATED after {reason}", StringComparison.Ordinal) &&
               d2xx.Contains("await WriteAsync(routeB, ct);", StringComparison.Ordinal) &&
               d2xx.Contains("_scanPrepared = false;", StringComparison.Ordinal),
            "Relay/resistance operations must invalidate D2XX scan preparation");
        Assert(vm.Contains("StartProductionScanAndVerifyFrameAsync", StringComparison.Ordinal) &&
               vm.Contains("WaitForNextProductionFrameAsync", StringComparison.Ordinal) &&
               vm.Contains("FAIL_CONFIRM_RELAY", StringComparison.Ordinal) &&
               vm.Contains("PASS_RELAY_SEQUENCE", StringComparison.Ordinal),
            "Post-relay production restarts must use first-frame watchdog recovery");
    }

    private static void TestProbeResistanceTransitionRegression()
    {
        var resistanceOn = new ProductionSettings
        {
            MasterFaultRequiredCount = 0,
            ProductSettleTimeMs = 0,
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50,
            PassMarkingToJigDelayMs = 0,
            ResistanceChannels =
            [
                new() { Enabled = true, Name = "R3", Channel = 3, MinOhm = 3000, MaxOhm = 5000 },
                new() { Enabled = true, Name = "R4", Channel = 4, MinOhm = 3000, MaxOhm = 5000 }
            ]
        };
        TestViewModel partialVm = CreateTestViewModel(resistanceOn, out FakeBoard partialBoard);
        ProductModel resistanceModel = Model(("PAIR-A", new[] { 1, 18 }), ("PAIR-B", new[] { 2, 19 }));
        resistanceModel.ResistanceSteps.Add(new ResistanceStep("R3", 3, 3000, 5000));
        resistanceModel.ResistanceSteps.Add(new ResistanceStep("R4", 4, 3000, 5000));
        partialVm.LoadPreparedModelAsync(resistanceModel).GetAwaiter().GetResult();
        partialVm.StartProductionTestAsync().GetAwaiter().GetResult();

        partialBoard.Commands.Clear();
        partialBoard.Publish(FrameSeq(2, (1, new[] { 18 })));

        Assert(partialVm.SelectedOperationTabIndex == 0,
            "Resistance view must not appear before continuity is complete");
        Assert(partialVm.ProductionFramesProcessed == 1 && partialVm.ProductionFramesDropped == 0,
            "Partial continuity frames keep flowing through TestEngine with zero drops");
        Assert(!partialBoard.Commands.Contains("STOP") && !partialBoard.Commands.Contains("RESET"),
            "Resistance path must not stop/reset board before continuity is complete");

        var resistanceOff = new ProductionSettings
        {
            Relay1JigPulseMs = 50,
            Relay2MarkingPulseMs = 50,
            PassMarkingToJigDelayMs = 0,
            ResistanceChannels =
            [
                new() { Enabled = false, Name = "R3", Channel = 3, MinOhm = 3000, MaxOhm = 5000 }
            ]
        };
        using TestEngine engine = CreateEngine(out _, resistanceOff);
        engine.SetModel(resistanceModel);
        List<ResistanceResult> offResults = engine.MeasureResistanceAsync().GetAwaiter().GetResult();
        Assert(offResults.Count == 0,
            "Resistance OFF with model resistance steps must not require Keysight or produce measurements");

        using TestEngine passEngine = CreateEngine(out _, resistanceOff);
        passEngine.SetModel(resistanceModel);
        ScanFrame passFrame = FrameSeq(3, (1, new[] { 18 }), (2, new[] { 19 }));
        passEngine.ProcessFrame(passFrame);
        passEngine.ProcessFrame(passFrame);
        Assert(passEngine.CompletePassAsync([]).GetAwaiter().GetResult(),
            "Resistance OFF lets continuity PASS use the normal PASS flow even when model has resistance steps");

        MethodInfo prepareRows = typeof(TestViewModel).GetMethod(
            "PrepareResistanceRows",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PrepareResistanceRows not found.");
        prepareRows.Invoke(partialVm, [resistanceModel]);
        Assert(partialVm.Resistance.Select(row => row.ChannelText).SequenceEqual(["CH3", "CH4"]),
            "Resistance ON CH3/CH4 creates only selected channel rows");
    }

    private static void TestResistanceUnitDisplayScaling()
    {
        static ResistanceResult Row(double min, double? value, double max, bool open = false)
        {
            var row = new ResistanceResult
            {
                Name = "R1",
                Channel = 1,
                MinOhm = min,
                MaxOhm = max,
                IsStable = true,
                MeasurementStatus = open ? "FAIL" : "PASS",
                IsOpen = open
            };
            if (value is double measured)
                row.ValueOhm = measured;
            row.Passed = !open && value is double actual && actual >= min && actual <= max;
            return row;
        }

        ResistanceResult kiloFail = Row(8000, 7895, 11000);
        Assert(kiloFail.MinDisplayText == "8.000 kΩ" &&
               kiloFail.Display == "7.895 kΩ" &&
               kiloFail.MaxDisplayText == "11.000 kΩ" &&
               !kiloFail.Passed &&
               kiloFail.ResultText == "FAIL",
            "7895 Ohm against 8000-11000 Ohm displays kOhm and remains FAIL");

        ResistanceResult kiloPass = Row(8000, 8950, 11000);
        Assert(kiloPass.MinDisplayText == "8.000 kΩ" &&
               kiloPass.Display == "8.950 kΩ" &&
               kiloPass.MaxDisplayText == "11.000 kΩ" &&
               kiloPass.Passed,
            "8950 Ohm against 8000-11000 Ohm displays kOhm and PASS");

        ResistanceResult lowOhm = Row(2, 5.2, 10);
        Assert(lowOhm.MinDisplayText == "2.00 Ω" &&
               lowOhm.Display == "5.20 Ω" &&
               lowOhm.MaxDisplayText == "10.00 Ω" &&
               lowOhm.Passed,
            "Low-ohm rows stay in Ohm with two decimals");

        ResistanceResult megaOhm = Row(1_000_000, 1_500_000, 2_000_000);
        Assert(megaOhm.MinDisplayText == "1.000 MΩ" &&
               megaOhm.Display == "1.500 MΩ" &&
               megaOhm.MaxDisplayText == "2.000 MΩ" &&
               megaOhm.Passed,
            "Megaohm rows use one shared MOhm display scale");

        ResistanceResult open = Row(8000, null, 11000, open: true);
        Assert(open.Display == "OPEN" && open.ResultText == "FAIL" && !open.Passed,
            "OPEN displays OPEN and never participates as a numeric value");
    }

    private static void TestAutoResistanceStabilityFlow()
    {
        static AppSettings FastResistanceSettings() => new()
        {
            Keysight = new KeysightSettings
            {
                SettleDelayMs = 0,
                Command = ":MEASURE:RES?"
            },
            Test = new TestSettings
            {
                ResistanceMinimumSettleMs = 0,
                ResistanceSampleIntervalMs = 0,
                ResistanceStableSampleCount = 3,
                ResistanceStableAbsoluteToleranceOhm = 2,
                ResistanceStableRelativeTolerancePercent = 0.1,
                ResistanceStabilityTimeoutMs = 100,
                ResistanceOpenThreshold = 1e30
            }
        };

        var production = new ProductionSettings
        {
            ResistanceChannels =
            [
                new() { Enabled = true, Name = "R1", Channel = 1, MinOhm = 3000, MaxOhm = 5000 },
                new() { Enabled = true, Name = "R3", Channel = 3, MinOhm = 3000, MaxOhm = 5000 }
            ]
        };
        ProductModel model = Model(("PAIR", new[] { 1, 2 }));
        model.ResistanceSteps.Add(new ResistanceStep("R1", 1, 3000, 5000));
        model.ResistanceSteps.Add(new ResistanceStep("R2", 2, 3000, 5000));
        model.ResistanceSteps.Add(new ResistanceStep("R3", 3, 3000, 5000));

        var board = new FakeBoard();
        var visa = new FakeKeysightVisaService(
            new Dictionary<int, Queue<double>>
            {
                [1] = new([4000.1, 4000.2, 4000.1]),
                [3] = new([3700, 3900, 4119.9, 4120.2, 4120.1])
            },
            () => board.LastResistanceChannel);
        using var engine = new TestEngine(board, visa, FastResistanceSettings(), production);
        engine.SetModel(model);
        List<ResistanceResult> updates = [];
        List<ResistanceResult> results = engine.MeasureResistanceAsync(updates.Add).GetAwaiter().GetResult();

        Assert(board.Commands.SequenceEqual(["STOP", "RESET", "ROUTE:1", "ROUTE:3", "RELEASE"]),
            "Resistance measures only enabled selected channels and switches CH1 then CH3 without CH2");
        Assert(results.Count == 2 &&
               results[0].Channel == 1 &&
               results[0].Passed &&
               results[0].SampleCount == 3 &&
               results[1].Channel == 3 &&
               results[1].Passed &&
               results[1].SampleCount == 5,
            "Fast and slow stable channels wait for stable windows and then PASS");
        Assert(updates.Count >= 4 &&
               updates[0].ResultText == "ĐANG ĐO" &&
               updates.Any(row => row.Channel == 3 && row.ResultText == "PASS"),
            "Resistance rows are updated at channel start and per-channel completion");

        var failBoard = new FakeBoard();
        var failVisa = new FakeKeysightVisaService(
            new Dictionary<int, Queue<double>> { [1] = new([6100, 6099.8, 6100.1]) },
            () => failBoard.LastResistanceChannel);
        using var failEngine = new TestEngine(failBoard, failVisa, FastResistanceSettings(), new ProductionSettings
        {
            ResistanceChannels = [new() { Enabled = true, Name = "R1", Channel = 1, MinOhm = 3000, MaxOhm = 5000 }]
        });
        ProductModel failModel = Model(("PAIR", new[] { 1, 2 }));
        failModel.ResistanceSteps.Add(new ResistanceStep("R1", 1, 3000, 5000));
        failEngine.SetModel(failModel);
        ResistanceResult outOfRange = failEngine.MeasureResistanceAsync().GetAwaiter().GetResult().Single();
        Assert(outOfRange.IsStable && !outOfRange.Passed && outOfRange.ValueOhm is > 6000,
            "Stable out-of-range resistance is a limit FAIL, not unstable");

        var openBoard = new FakeBoard();
        var openVisa = new FakeKeysightVisaService(
            new Dictionary<int, Queue<double>> { [1] = new([9.9e37, 9.9e37, 9.9e37]) },
            () => openBoard.LastResistanceChannel);
        using var openEngine = new TestEngine(openBoard, openVisa, FastResistanceSettings(), new ProductionSettings
        {
            ResistanceChannels = [new() { Enabled = true, Name = "R1", Channel = 1, MinOhm = 3000, MaxOhm = 5000 }]
        });
        ProductModel openModel = Model(("PAIR", new[] { 1, 2 }));
        openModel.ResistanceSteps.Add(new ResistanceStep("R1", 1, 3000, 5000));
        openEngine.SetModel(openModel);
        ResistanceResult open = openEngine.MeasureResistanceAsync().GetAwaiter().GetResult().Single();
        Assert(open.IsOpen && open.Display == "OPEN" && open.ResultText == "FAIL",
            "OPEN is confirmed separately and displayed as OPEN/FAIL");

        var unstableBoard = new FakeBoard();
        Queue<double> unstableSamples = new(Enumerable.Range(0, 200)
            .Select(index => index % 2 == 0 ? 3000.0 : 5000.0));
        var unstableVisa = new FakeKeysightVisaService(
            new Dictionary<int, Queue<double>> { [1] = unstableSamples },
            () => unstableBoard.LastResistanceChannel);
        AppSettings unstableSettings = FastResistanceSettings();
        unstableSettings.Test.ResistanceSampleIntervalMs = 1;
        unstableSettings.Test.ResistanceStabilityTimeoutMs = 5;
        using var unstableEngine = new TestEngine(unstableBoard, unstableVisa, unstableSettings, new ProductionSettings
        {
            ResistanceChannels = [new() { Enabled = true, Name = "R1", Channel = 1, MinOhm = 3000, MaxOhm = 5000 }]
        });
        ProductModel unstableModel = Model(("PAIR", new[] { 1, 2 }));
        unstableModel.ResistanceSteps.Add(new ResistanceStep("R1", 1, 3000, 5000));
        unstableEngine.SetModel(unstableModel);
        ResistanceResult unstable = unstableEngine.MeasureResistanceAsync().GetAwaiter().GetResult().Single();
        Assert(!unstable.IsStable && unstable.MeasurementStatus == "UNSTABLE" && !unstable.Passed,
            "Unstable readings time out as UNSTABLE and never fake PASS from the last reading");
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
                Started = finished.AddSeconds(-2), Finished = finished,
                PartName = "PRODUCT", PartNumber = "NI375C1000", Eco = "NE N EV", Alc = "NI375/C1000",
                LotNo = 2001, Result = "PASS", Passed = true, ModelName = "MODEL-A",
                HtdrvName = "JBZUniversalTester V15.2.0", OpenCount = 0,
                MeasuredResistance = 101.5, ResistanceMin = 100, ResistanceMax = 110
            };

            var store = new TestHistoryStore(Path.Combine(root, "history.db"));
            store.Add(record);
            IReadOnlyList<TestHistoryRecord> found = store.Search(new HistorySearchCriteria(
                finished.Date, finished.Date.AddDays(1), 2001, "NI375", "PASS"));
            Assert(found.Count == 1 && found[0].PartNumber == "NI375C1000", "SQLite search date/LOT/product/result");

            string csv = Path.Combine(root, "history.csv");
            HistoryExportService.ExportCsv(csv, found);
            byte[] csvBytes = File.ReadAllBytes(csv);
            Assert(csvBytes.Length >= 3 && csvBytes[0] == 0xEF && csvBytes[1] == 0xBB && csvBytes[2] == 0xBF, "CSV UTF-8 BOM");
            string[] csvLines = File.ReadAllLines(csv, Encoding.UTF8);
            Assert(csvLines[0].Split(',').Length == 34 && csvLines[1].Contains("2026/08/09 14:07:08"), "CSV columns/date");
            Assert(csvLines[0].Contains("Fault Type", StringComparison.Ordinal) &&
                   csvLines[0].Contains("Standard", StringComparison.Ordinal) &&
                   csvLines[0].Contains("Actual", StringComparison.Ordinal) &&
                   !csvLines[0].Contains("Mong đợi", StringComparison.OrdinalIgnoreCase), "CSV English customer headers");

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
            string customerText = File.ReadAllText(customerCsv, Encoding.UTF8);
            Assert(customerText.Contains("OPEN CIRCUIT", StringComparison.Ordinal), "Customer export technical fault name");
            Assert(customerText.Contains("NO CONTINUITY", StringComparison.Ordinal), "Customer export actual condition");
            Assert(!customerText.Contains("DÂY CHƯA KẾT NỐI", StringComparison.Ordinal), "Customer export does not use operator fault name");
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

    private static void TestLabelPrintingSafety()
    {
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

        LabelPrintRequest request = LabelPrintRequest.Capture(history, settings);
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
                LabelPrintStatus.Printed,
                finished.AddMilliseconds(250),
                "software-test");

            TestHistoryRecord saved = store.Search(new HistorySearchCriteria(
                null, null, 31415, "PART-A", "PASS", 10)).Single();
            Assert(saved.CycleId == "cycle-a" && saved.PrintStatus == "Printed", "Cycle/print status traceability");
            Assert(saved.BarcodeValue == identity.BarcodeValue &&
                   saved.LabelSerial == identity.SerialText &&
                   saved.LabelProfile == "KS91-A" &&
                   saved.Printer == "ZEBRA-A" &&
                   saved.LabelCopies == 1 &&
                   saved.ReprintCount == 0, "Persisted label identity matches PASS cycle");
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

    private static TestViewModel CreateTestViewModel(ProductionSettings production, out FakeBoard board)
    {
        board = new FakeBoard();
        var app = new AppSettings();
        var engine = new TestEngine(board, new KeysightVisaService(), app, production);
        return new TestViewModel(new MainViewModel(), engine, board, new KeysightVisaService(), app, production);
    }

    private static ProductModel Model(params (string Name, int[] Io)[] nets)
    {
        var model = new ProductModel { ModelName = "SELF-TEST", PartNumber = "SELF-TEST" };
        foreach ((string name, int[] io) in nets)
        {
            PinRecord[] pins = io
                .Select((value, index) => new PinRecord(
                    "C",
                    name,
                    value,
                    value.ToString(),
                    OriginalOrder: model.Pins.Count + index + 1))
                .ToArray();
            model.Pins.AddRange(pins);
            model.Nets.Add(new WireNet(name, io, pins));
        }
        return model;
    }

    private static ProductModel TopologyModel(params Terminal[] terminals)
    {
        var model = new ProductModel { ModelName = "THT-TOPOLOGY", PartNumber = "THT-TOPOLOGY" };

        for (int i = 0; i < terminals.Length; i++)
        {
            Terminal terminal = terminals[i];
            model.Pins.Add(new PinRecord(
                terminal.Connector,
                terminal.WireName,
                terminal.Io,
                terminal.Pin,
                Section: string.Empty,
                Color: terminal.Color,
                ConnectorPinCount: terminal.PinCount,
                PinType: terminal.PinType,
                WireConnection: terminal.WireConnection,
                OriginalOrder: i + 1));
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

    private static ProductModel ClipModel(string name = "CLIP-MODEL")
    {
        var model = new ProductModel { ModelName = name, PartNumber = name };
        PinRecord common = new("CLIP", "CLIP-A0", 100, "A0", PinType: "A0", OriginalOrder: 1);
        model.Pins.Add(common);

        var branches = new List<ClipBranch>();
        for (int i = 1; i <= 5; i++)
        {
            PinRecord clipPin = new("CLIP", $"CLIP-A{i}", 100 + i, $"A{i}", PinType: $"a{i}", OriginalOrder: i + 1);
            model.Pins.Add(clipPin);
            branches.Add(new ClipBranch($"a{i}", i, 100 + i, clipPin, null));
        }

        model.Clip = new ClipTopology(common, branches);
        return model;
    }

    private static ProductModel ClipModelWithNormalRows()
    {
        var model = new ProductModel { ModelName = "CLIP-NORMAL", PartNumber = "CLIP-NORMAL" };
        PinRecord normal1 = new("CN1", "BG01", 1, "1", OriginalOrder: 1);
        PinRecord normal2 = new("CN7", "BG01", 86, "8", OriginalOrder: 2);
        PinRecord common = new("CLIP", "A0", 201, "A0", PinType: "A0", OriginalOrder: 3);
        PinRecord a1 = new("CLIP", "a1", 202, "a1", PinType: "a1", OriginalOrder: 4);
        PinRecord a2 = new("CLIP", "a2", 203, "a2", PinType: "a2", OriginalOrder: 5);
        model.Pins.AddRange([normal1, normal2, common, a1, a2]);
        model.Nets.Add(new WireNet("BG01", [1, 86], [normal1, normal2]));
        model.Clip = new ClipTopology(common, [
            new ClipBranch("a1", 1, 202, a1, null),
            new ClipBranch("a2", 2, 203, a2, null)
        ]);
        return model;
    }

    private static string[] ClipVisibleNames(TestEngine engine) =>
        engine.BuildRows()
            .Where(row =>
                row.WireName.Equals("A0", StringComparison.OrdinalIgnoreCase) ||
                row.WireName.StartsWith("a", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.WireName)
            .ToArray();

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

    private static ScanFrame ProbeFanInFrame(long sequence, int targetIo, IEnumerable<int> sources)
    {
        Dictionary<int, IReadOnlySet<int>> map = sources
            .Distinct()
            .ToDictionary(
                source => source,
                _ => (IReadOnlySet<int>)new HashSet<int> { targetIo });
        HashSet<int> active = map.Keys.Concat([targetIo]).ToHashSet();

        return new ScanFrame(
            DateTime.Now,
            1,
            active,
            [],
            true,
            0,
            sequence,
            map,
            new Dictionary<int, int> { [targetIo] = map.Count },
            BoardScanMode.Production);
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

    private sealed class FakeBoard : IBoardTransport
    {
        public List<string> Commands { get; } = [];
        public bool ThrowOnSetRelay { get; set; }
        public int DisconnectCount { get; private set; }
        public bool IsConnected => true;
        public bool IsScanning { get; private set; } = true;
        public BoardScanMode CurrentScanMode { get; private set; } = BoardScanMode.Production;
        public BoardCapacity Capacity { get; private set; } = BoardCapacity.Create(10);
        private event EventHandler<ScanFrame>? FrameReceivedCore;
        public event EventHandler<ScanFrame>? FrameReceived { add { FrameReceivedCore += value; } remove { FrameReceivedCore -= value; } }
        public event EventHandler<string>? Log { add { } remove { } }
        public void Publish(ScanFrame frame) => FrameReceivedCore?.Invoke(this, frame);
        public Task<BoardConnectionInfo> ConnectAsync(CancellationToken ct = default) => Task.FromResult(new BoardConnectionInfo("Fake", "Fake"));
        public Task DisconnectAsync() { DisconnectCount++; IsScanning = false; Commands.Add("DISCONNECT"); return Task.CompletedTask; }
        public Task HandshakeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetClearAsync(CancellationToken ct = default) { Commands.Add("RESET"); return Task.CompletedTask; }
        public void ConfigureScanRange(int maxIo) { }
        public Task StartScanAsync(BoardScanMode mode = BoardScanMode.Production, CancellationToken ct = default) { IsScanning = true; CurrentScanMode = mode; Commands.Add("START"); return Task.CompletedTask; }
        public Task StopScanAsync(CancellationToken ct = default) { IsScanning = false; Commands.Add("STOP"); return Task.CompletedTask; }
        public Task EnterIdleAsync(CancellationToken ct = default) => Task.CompletedTask;
        public int LastResistanceChannel { get; private set; }
        public Task SelectResistanceRouteAsync(ResistanceStep step, CancellationToken ct = default)
        {
            LastResistanceChannel = step.Channel;
            Commands.Add($"ROUTE:{step.Channel}");
            return Task.CompletedTask;
        }
        public Task ReleaseResistanceRouteAsync(CancellationToken ct = default)
        {
            LastResistanceChannel = 0;
            Commands.Add("RELEASE");
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

    private sealed class FakeKeysightVisaService : KeysightVisaService
    {
        private readonly Dictionary<int, Queue<double>> _samples;
        private readonly Func<int> _currentChannel;
        private readonly Dictionary<int, double> _lastSample = [];

        public FakeKeysightVisaService(
            Dictionary<int, Queue<double>> samples,
            Func<int> currentChannel)
        {
            _samples = samples;
            _currentChannel = currentChannel;
        }

        public override bool IsConnected => true;
        public override string InstrumentId => "KEYSIGHT TECHNOLOGIES,34461A,SELFTEST";
        public override string ConnectAutomatic(string? preferredResource = null) => InstrumentId;

        public override double MeasureResistance(string command = ":MEASURE:RES?")
        {
            int channel = _currentChannel();
            if (!_samples.TryGetValue(channel, out Queue<double>? queue))
                throw new InvalidOperationException($"No fake Keysight sample for CH{channel}");

            if (queue.Count == 0)
                return _lastSample[channel];

            double sample = queue.Dequeue();
            _lastSample[channel] = sample;
            return sample;
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
