using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using JBZUniversalTester.Models;
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
            ("Relay PASS/FAIL safe ordering", TestRelayOrdering),
            ("History SQLite/search/CSV/XLSX native types", TestHistory),
            ("ALL6 label data order", TestLabel),
            ("PASS label snapshot/idempotency/traceability", TestLabelPrintingSafety),
            ("Pi legacy golden compiler", TestPiCompiler),
            ("Standard product picker filter", TestProductPickerFilter),
            ("Fault display localization and detail", TestFaultDisplayFormatter),
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
                "Mã hàng JBZ (*.tht;*.model)|*.tht;*.model",
                StringComparison.Ordinal),
            "Standard dialog filter must contain only .tht and .model");
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
        FaultRow initialMissing = engine.BuildRows().Single(row => row.Kind == FaultKind.MissingConnection);
        Assert(initialMissing.ProductFaultType == ProductFaultType.None &&
               initialMissing.IoText == "IO1 <-> IO18" &&
               initialMissing.Pin == "1 <-> 18" &&
               initialMissing.WireName == "PAIR",
            "Model load shows one display-only row with full expected pair metadata");
        ScanFrame pairPassFrame = Frame((1, new[] { 18 }));
        engine.ProcessFrame(pairPassFrame);
        Thread.Sleep(ProductionTimingPolicy.DefaultProductSettleTimeMs + 5);
        engine.ProcessFrame(pairPassFrame);
        Assert(engine.ContinuityPassed &&
               !engine.HasWiringFault &&
               !engine.BuildRows().Any(row => row.Kind == FaultKind.MissingConnection),
            "Expected IO1-IO18 passes and pending row disappears");

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
        Assert(engine.ContinuityPassed &&
               !engine.HasWiringFault &&
               !engine.BuildRows().Any(row => row.Kind == FaultKind.MissingConnection),
            "Splice component passes and pending row disappears");

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
                PartName = "UART", PartNumber = "NI375C1000", Eco = "NE N EV", Alc = "NI375/C1000",
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
        var data = new LabelPrintData("UART", "NI375C1000", "NE N EV", "", "NI375/C1000", 2001,
            new DateTime(2024, 7, 15, 14, 7, 8));
        string epl = EplLabelService.BuildPassLabel(data, new LabelSettings());
        int part = epl.IndexOf("NI375C1000", StringComparison.Ordinal);
        int eco = epl.IndexOf("NE N EV", part + 1, StringComparison.Ordinal);
        int name = epl.IndexOf("UART", eco + 1, StringComparison.Ordinal);
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

    private static void TestPiCompiler()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZSelfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "1.model");
            string p1 = string.Join('|', new[] { "1", "C1", "1", "L1", "", "", "", "", "-1", "2" });
            string p2 = string.Join('|', new[] { "2", "C1", "2", "L1", "", "", "", "", "1", "" });
            File.WriteAllText(path, $"[Common]\nModel=111\nName=222\n[Connector]\nCount=1\nC1=C1|2\n[Pin]\nCount=2\nP1={p1}\nP2={p2}\n", Encoding.UTF8);
            UartModelProfile profile = PiLegacyModelCompiler.Compile(PiLegacyModelParser.Load(path));
            string[] actual = profile.Commands.Select(command => command.Tx).ToArray();
            string[] expected =
            [
                ":MODEL,1", ":PINCOUNT,1", ":PINDATA,0,1,0,1,0,0",
                ":ARRAYCOUNT,1", ":ARRAY,0,1,2", ":CONCOUNT,1",
                ":CON,0,4,0,0,5000,65535", ":CONNECTORCOUNT,1",
                ":CONNECTOR,0,1,2", ":FINISH"
            ];
            Assert(actual.SequenceEqual(expected), "Golden Pi command sequence");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static TestEngine CreateEngine(out FakeBoard board)
    {
        board = new FakeBoard();
        var app = new AppSettings();
        app.Board.RequiredStableFrames = 1;
        var production = new ProductionSettings
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

    private static ScanFrame Frame(params (int Source, int[] Targets)[] connections)
    {
        Dictionary<int, IReadOnlySet<int>> map = connections.ToDictionary(
            pair => pair.Source,
            pair => (IReadOnlySet<int>)pair.Targets.ToHashSet());
        HashSet<int> active = map.Keys.Concat(map.Values.SelectMany(values => values)).ToHashSet();
        Dictionary<int, int> hits = map.Values.SelectMany(values => values)
            .GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        return new ScanFrame(DateTime.Now, 1, active, [], true, 0, 1, map, hits, BoardScanMode.Production);
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
        public bool IsConnected => true;
        public bool IsScanning { get; private set; } = true;
        public BoardCapacity Capacity { get; private set; } = BoardCapacity.Create(10);
        public event EventHandler<ScanFrame>? FrameReceived { add { } remove { } }
        public event EventHandler<string>? Log { add { } remove { } }
        public Task<BoardConnectionInfo> ConnectAsync(CancellationToken ct = default) => Task.FromResult(new BoardConnectionInfo("Fake", "Fake"));
        public Task DisconnectAsync() { IsScanning = false; return Task.CompletedTask; }
        public Task HandshakeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResetClearAsync(CancellationToken ct = default) { Commands.Add("RESET"); return Task.CompletedTask; }
        public void ConfigureScanRange(int maxIo) { }
        public Task StartScanAsync(BoardScanMode mode = BoardScanMode.Production, CancellationToken ct = default) { IsScanning = true; Commands.Add("START"); return Task.CompletedTask; }
        public Task StopScanAsync(CancellationToken ct = default) { IsScanning = false; Commands.Add("STOP"); return Task.CompletedTask; }
        public Task EnterIdleAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SelectResistanceRouteAsync(ResistanceStep step, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseResistanceRouteAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SetRelayAsync(int relay, CancellationToken ct = default) { Commands.Add($"SET:{relay}"); return Task.CompletedTask; }
        public Task AllRelaysOffAsync(CancellationToken ct = default) { Commands.Add("OFF"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
