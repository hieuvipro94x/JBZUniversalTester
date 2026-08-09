using System.IO.Compression;
using System.Reflection;
using System.Text;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
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
            ("Production/probe decoder separation", TestDecoderModes),
            ("Continuity/open/wrong/splice engine", TestEngineVectors),
            ("Relay PASS/FAIL safe ordering", TestRelayOrdering),
            ("History SQLite/search/CSV/XLSX native types", TestHistory),
            ("ALL6 label data order", TestLabel),
            ("Pi legacy golden compiler", TestPiCompiler),
            ("Product picker extension filter", TestProductPickerFilter)
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
        MethodInfo filter = typeof(ProductFilePickerWindow).GetMethod(
            "IsSupportedProductFile",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Product picker filter method not found.");

        bool Accepts(string fileName) =>
            (bool)(filter.Invoke(null, [fileName]) ?? false);

        Assert(Accepts("sample.tht"), ".tht must be visible");
        Assert(Accepts("sample.THT"), ".THT must be visible");
        Assert(Accepts("sample.model"), ".model must be visible");
        Assert(Accepts("sample.MODEL"), ".MODEL must be visible");
        Assert(!Accepts("sample.json"), ".json must be hidden");
        Assert(!Accepts("sample.jbzproduct.json"), ".jbzproduct.json must be hidden");
        Assert(!Accepts("sample.setup"), ".setup must be hidden");
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
        engine.ProcessFrame(Frame((1, new[] { 18 })));
        Assert(engine.ContinuityPassed && !engine.HasWiringFault, "Expected IO1-IO18 passes");

        engine.SetModel(pair);
        engine.ProcessFrame(Frame((1, new[] { 40 })));
        Assert(engine.HasWiringFault, "IO1-IO40 is wiring fault");

        engine.SetModel(pair);
        engine.ProcessFrame(Frame());
        Assert(engine.BuildRows().Any(row => row.Kind == FaultKind.Open), "Missing IO1-IO18 is open");

        ProductModel splice = Model(("SPLICE", new[] { 5, 20, 33 }));
        engine.SetModel(splice);
        engine.ProcessFrame(Frame((5, new[] { 20, 33 })));
        Assert(engine.ContinuityPassed && !engine.HasWiringFault, "Splice component passes");

        engine.SetModel(pair);
        engine.ProcessFrame(new ScanFrame(
            DateTime.Now, 1, new HashSet<int> { 1, 40 }, [], true, 0, 1,
            new Dictionary<int, IReadOnlySet<int>> { [1] = new HashSet<int> { 40 } },
            new Dictionary<int, int> { [40] = 1 }, BoardScanMode.Probe));
        Assert(!engine.HasWiringFault && !engine.ContinuityPassed, "Probe frame never enters production evaluation");
    }

    private static void TestRelayOrdering()
    {
        using TestEngine engine = CreateEngine(out FakeBoard board);
        engine.SetModel(Model(("PAIR", new[] { 1, 18 })));
        engine.ProcessFrame(Frame((1, new[] { 18 })));
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
            Assert(csvLines[0].Split(',').Length == 30 && csvLines[1].Contains("2026/08/09 14:07:08"), "CSV columns/date");

            string xlsx = Path.Combine(root, "history.xlsx");
            HistoryExportService.ExportXlsx(xlsx, found);
            using ZipArchive archive = ZipFile.OpenRead(xlsx);
            string sheet = ReadEntry(archive, "xl/worksheets/sheet1.xml");
            string styles = ReadEntry(archive, "xl/styles.xml");
            Assert(sheet.Contains("<c r=\"A2\" s=\"2\"><v>", StringComparison.Ordinal), "XLSX DateTime native numeric");
            Assert(sheet.Contains("<c r=\"G2\"><v>2001</v></c>", StringComparison.Ordinal), "XLSX LOT native number");
            Assert(sheet.Contains("<c r=\"X2\"><v>101.5</v></c>", StringComparison.Ordinal), "XLSX resistance native number");
            Assert(styles.Contains("numFmtId=\"164\"", StringComparison.Ordinal), "XLSX DateTime number format");
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
            PassMarkingToJigDelayMs = 0
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
}
