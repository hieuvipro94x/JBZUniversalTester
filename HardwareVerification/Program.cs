using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

Console.OutputEncoding = new UTF8Encoding(false);

string? serial = Value("--serial");
int passiveSeconds = IntValue("--passive-seconds", 0, 0, 86_400);
int connectCycles = IntValue("--connect-cycles", 0, 0, 30);
int scanCycles = IntValue("--scan-cycles", 0, 0, 100);
int expansionCards = IntValue("--expansion-cards", 2, 1, BoardIoDecoder.MaxExpansionCardCount);
bool routeResistance = Has("--route-resistance");
bool measureResistance = Has("--measure-resistance");
bool verifySupervisor = Has("--verify-supervisor");
bool verifyVisa = Has("--visa");
string? tracePath = Value("--trace");
string? modelDirectory = Value("--model-directory");
string? diagnosticModelPath = Value("--model");
ProductModel? diagnosticModel = null;

Console.WriteLine($"UTC={DateTime.UtcNow:O} Architecture={RuntimeInformation.ProcessArchitecture}");
uint d2xxVersion;
IReadOnlyList<D2xxDeviceInfo> devices;
try
{
    d2xxVersion = D2xxBoardTransport.GetD2xxLibraryVersion();
    devices = D2xxBoardTransport.EnumerateDevices();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FTDI_ENUM_FAIL: {ex.GetType().Name}: {ex.Message}");
    return 2;
}

Console.WriteLine($"D2XX_VERSION=0x{d2xxVersion:X8} DEVICE_COUNT={devices.Count}");
foreach (D2xxDeviceInfo device in devices)
{
    Console.WriteLine(
        $"FTDI Description=\"{device.Description}\" Serial=\"{device.Serial}\" " +
        $"VIDPID=0x{device.Id:X8} Location=0x{device.LocationId:X8} Type={device.Type} Open={device.IsOpen}");
}

if (verifyVisa)
{
    try
    {
        using var visa = new KeysightVisaService();
        IReadOnlyList<string> resources = visa.DiscoverUsbInstruments();
        Console.WriteLine($"VISA_USB_COUNT={resources.Count}");
        foreach (string resource in resources)
            Console.WriteLine($"VISA_RESOURCE={resource}");

        if (resources.Count > 0)
        {
            string idn = visa.ConnectAutomatic();
            Console.WriteLine($"KEYSIGHT_IDN={idn} RESOURCE={visa.ConnectedResource}");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"VISA_VERIFY_FAIL: {ex.GetType().Name}: {ex.Message}");
    }
}

if (!string.IsNullOrWhiteSpace(modelDirectory))
{
    string fullDirectory = Path.GetFullPath(modelDirectory);
    if (!Directory.Exists(fullDirectory))
    {
        Console.Error.WriteLine($"MODEL_DIRECTORY_NOT_FOUND: {fullDirectory}");
        return 6;
    }

    var parser = new ThtModelParser();
    int modelFailures = 0;
    foreach (string modelPath in Directory.EnumerateFiles(fullDirectory, "*.tht", SearchOption.TopDirectoryOnly)
                 .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
    {
        try
        {
            ProductModel model = parser.Load(modelPath);
            int requiredCards = BoardCapacity.RequiredScanUnitsForIo(model.MaxIo);
            Console.WriteLine(
                $"MODEL_PARSE PASS file=\"{Path.GetFileName(modelPath)}\" part=\"{model.PartNumber}\" " +
                $"pins={model.Pins.Count} nets={model.Nets.Count} maxIo={model.MaxIo} " +
                $"requiredCards={requiredCards} warnings={model.TopologyWarnings.Count}");
            foreach (string warning in model.TopologyWarnings)
                Console.WriteLine($"MODEL_WARNING file=\"{Path.GetFileName(modelPath)}\" {warning}");
        }
        catch (Exception ex)
        {
            modelFailures++;
            Console.Error.WriteLine(
                $"MODEL_PARSE FAIL file=\"{Path.GetFileName(modelPath)}\" {ex.GetType().Name}: {ex.Message}");
        }
    }

    if (modelFailures > 0)
        return 7;
}

if (!string.IsNullOrWhiteSpace(diagnosticModelPath))
{
    string fullModelPath = Path.GetFullPath(diagnosticModelPath);
    if (!File.Exists(fullModelPath))
    {
        Console.Error.WriteLine($"MODEL_NOT_FOUND: {fullModelPath}");
        return 8;
    }

    try
    {
        diagnosticModel = new ThtModelParser().Load(fullModelPath);
        int requiredCards = BoardCapacity.RequiredScanUnitsForIo(diagnosticModel.MaxIo);
        Console.WriteLine(
            $"MODEL_DIAGNOSTIC PASS file=\"{Path.GetFileName(fullModelPath)}\" " +
            $"part=\"{diagnosticModel.PartNumber}\" pins={diagnosticModel.Pins.Count} " +
            $"nets={diagnosticModel.Nets.Count} maxIo={diagnosticModel.MaxIo} requiredCards={requiredCards}");
        if (requiredCards > expansionCards)
        {
            Console.Error.WriteLine(
                $"MODEL_CAPACITY_FAIL: model requires {requiredCards} card(s), configured {expansionCards}.");
            return 9;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"MODEL_DIAGNOSTIC FAIL {ex.GetType().Name}: {ex.Message}");
        return 10;
    }
}

if (passiveSeconds == 0 && connectCycles == 0 && scanCycles == 0 &&
    !routeResistance && !measureResistance && !verifySupervisor)
    return 0;

D2xxDeviceInfo[] targets = devices.Where(IsTargetBoard).ToArray();
if (targets.Length == 0)
{
    Console.Error.WriteLine("HARDWARE_UNAVAILABLE: không tìm thấy FT245R USB FIFO VID/PID 0403:6001.");
    return 3;
}

if (!string.IsNullOrWhiteSpace(serial))
    targets = targets.Where(item => item.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase)).ToArray();

if (targets.Length != 1)
{
    Console.Error.WriteLine(
        $"HARDWARE_AMBIGUOUS: tìm thấy {targets.Length} bo phù hợp; phải truyền đúng --serial.");
    return 4;
}

if (targets[0].IsOpen)
{
    Console.Error.WriteLine(
        $"HARDWARE_OCCUPIED: bo {targets[0].Serial} đang được một owner khác mở.");
    return 5;
}

var production = new ProductionSettings
{
    ExpansionCardCount = expansionCards,
    CardCount = BoardIoDecoder.ScanCardCountFromExpansionCards(expansionCards)
};

if (connectCycles > 0)
{
    for (int cycle = 1; cycle <= connectCycles; cycle++)
    {
        await using var cycleBoard = new D2xxBoardTransport(targets[0].Serial, production);
        await cycleBoard.ConnectAsync();
        await cycleBoard.DisconnectAsync();
        Console.WriteLine($"CONNECT_CYCLE {cycle}/{connectCycles} PASS");
    }
}

if (scanCycles > 0 || passiveSeconds > 0 || routeResistance || measureResistance || verifySupervisor)
{
    await using var board = new D2xxBoardTransport(targets[0].Serial, production);
    StreamWriter? trace = null;
    long completeFrames = 0;
    long incompleteFrames = 0;
    long unknownBytes = 0;
    long lastFrameTicks = 0;
    long maxFrameGapTicks = 0;
    long firstFrameTicks = 0;
    long rxBytes = 0;
    long txBytes = 0;
    long modelFrames = 0;
    long modelPassFrames = 0;
    long modelFaultFrames = 0;
    string modelSignature = string.Empty;
    TestEngine? diagnosticEngine = null;
    KeysightVisaService? diagnosticVisa = null;

    if (diagnosticModel is not null)
    {
        diagnosticVisa = new KeysightVisaService();
        diagnosticEngine = new TestEngine(
            board,
            diagnosticVisa,
            new AppSettings(),
            production);
        diagnosticEngine.SetModel(diagnosticModel);
    }

    if (!string.IsNullOrWhiteSpace(tracePath))
    {
        string fullTracePath = Path.GetFullPath(tracePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullTracePath)!);
        trace = new StreamWriter(fullTracePath, append: false, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    board.ProtocolTrace += (_, item) =>
    {
        if (item.Direction == "RX") Interlocked.Add(ref rxBytes, item.Data.Length);
        else Interlocked.Add(ref txBytes, item.Data.Length);
        trace?.WriteLine(
            $"{item.TimestampUtc:O} sw={item.StopwatchTimestamp} {item.Direction} {Convert.ToHexString(item.Data)}");
    };
    board.FrameReceived += (_, frame) =>
    {
        long now = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(ref firstFrameTicks, now, 0) != 0)
        {
            long previous = Interlocked.Exchange(ref lastFrameTicks, now);
            if (previous > 0)
                UpdateMaximum(ref maxFrameGapTicks, now - previous);
        }
        else
        {
            Interlocked.Exchange(ref lastFrameTicks, now);
        }

        if (frame.Complete) Interlocked.Increment(ref completeFrames);
        else Interlocked.Increment(ref incompleteFrames);
        Interlocked.Add(ref unknownBytes, frame.UnknownBytes);

        if (diagnosticEngine is not null && frame.Complete && frame.UnknownBytes == 0)
        {
            diagnosticEngine.ProcessFrame(frame);
            PassGateDiagnostics gate = diagnosticEngine.GetPassGateDiagnostics();
            Interlocked.Increment(ref modelFrames);
            if (gate.ContinuityPassed)
                Interlocked.Increment(ref modelPassFrames);
            if (gate.HasWiringFault)
                Interlocked.Increment(ref modelFaultFrames);

            string connections = string.Join(
                ",",
                frame.Connections
                    .Where(pair => pair.Value.Count > 0)
                    .SelectMany(pair => pair.Value.Select(target => $"{pair.Key}->{target}"))
                    .OrderBy(value => value, StringComparer.Ordinal));
            string nextSignature =
                $"passed={gate.PassedNetCount}/{gate.ExpectedNetCount}|activity={gate.HasProductActivity}|" +
                $"fault={gate.HasWiringFault}|connections={connections}";
            if (!string.Equals(nextSignature, modelSignature, StringComparison.Ordinal))
            {
                modelSignature = nextSignature;
                string remaining = string.Join(" | ", gate.RemainingNetworks.Select(item => item.Display));
                Console.WriteLine(
                    $"MODEL_FRAME seq={frame.Sequence} passed={gate.PassedNetCount}/{gate.ExpectedNetCount} " +
                    $"continuity={gate.ContinuityPassed} activity={gate.HasProductActivity} " +
                    $"wrong={gate.WrongConfirmedCount} short={gate.ShortConfirmedCount} " +
                    $"connections=\"{connections}\" remaining=\"{remaining}\"");
            }
        }
    };

    try
    {
        BoardConnectionInfo connection = await board.ConnectAsync();
        Console.WriteLine($"CONNECTED Description=\"{connection.Description}\" Serial=\"{connection.SerialNumber}\"");

        // HardwareVerification không có model THT bắt buộc. --expansion-cards
        // là configured range và cũng là START_SCAN range cần thử.
        int verificationMaxIo = diagnosticModel is { MaxIo: > 0 }
            ? diagnosticModel.MaxIo
            : checked(expansionCards * BoardCapacity.IoPerExpansionCard);
        board.ConfigureActiveScanRange(verificationMaxIo);

        if (verifySupervisor)
        {
            var supervisor = new ScanSupervisor(board, message => Console.WriteLine($"SUPERVISOR {message}"));
            var verifyWatch = Stopwatch.StartNew();
            await supervisor.StartProductionScanAndVerifyFrameAsync(
                verificationMaxIo,
                CancellationToken.None,
                $"HARDWARE_VERIFY_{expansionCards}_MODULES");
            verifyWatch.Stop();
            Console.WriteLine(
                $"SUPERVISOR_VERIFY PASS elapsedMs={verifyWatch.Elapsed.TotalMilliseconds:0.###} " +
                $"frames={board.FramesReceived} timeoutMs={ScanSupervisor.ResolveFirstFrameTimeoutMs(board.Capacity)}");
            await board.StopScanAsync();
        }

        if (scanCycles > 0)
        {
            for (int cycle = 1; cycle <= scanCycles; cycle++)
            {
                await board.StartScanAsync(BoardScanMode.Production);
                await Task.Delay(20);
                await board.StopScanAsync();
                Console.WriteLine($"SCAN_CYCLE {cycle}/{scanCycles} PASS");
            }
        }

        if (passiveSeconds > 0)
        {
            TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            var wall = Stopwatch.StartNew();
            await board.StartScanAsync(BoardScanMode.Production);
            await Task.Delay(TimeSpan.FromSeconds(passiveSeconds));
            await board.StopScanAsync();
            wall.Stop();
            TimeSpan cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;
            double cpuPercent = (cpuAfter - cpuBefore).TotalMilliseconds /
                                wall.Elapsed.TotalMilliseconds /
                                Environment.ProcessorCount * 100.0;
            string passiveSummary =
                $"PASSIVE seconds={wall.Elapsed.TotalSeconds:0.###} cpu={cpuPercent:0.###}% " +
                $"rxBytes={rxBytes} txBytes={txBytes} completeFrames={completeFrames} " +
                $"incompleteFrames={incompleteFrames} unknownBytes={unknownBytes} " +
                $"maxFrameGapMs={maxFrameGapTicks * 1000.0 / Stopwatch.Frequency:0.###}";
            Console.WriteLine(passiveSummary);
            if (diagnosticEngine is not null)
            {
                Console.WriteLine(
                    $"MODEL_SUMMARY frames={modelFrames} continuityPassFrames={modelPassFrames} " +
                    $"faultFrames={modelFaultFrames} final=\"{modelSignature}\"");
            }
        }

        if (routeResistance)
        {
            await board.StopScanAsync();
            await board.ResetClearAsync();
            for (int channel = 1; channel <= 10; channel++)
            {
                var step = new ResistanceStep($"R{channel}", channel, 0, double.MaxValue);
                await board.SelectResistanceRouteAsync(step);
                Console.WriteLine($"RESISTANCE_ROUTE CH{channel} PASS selector=0x{channel:X2}");
            }
        }

        if (measureResistance)
        {
            using var measurementVisa = new KeysightVisaService();
            string idn = measurementVisa.ConnectAutomatic();
            Console.WriteLine(
                $"RESISTANCE_MEASURE VISA_CONNECTED idn=\"{idn}\" resource=\"{measurementVisa.ConnectedResource}\"");

            var measurementSettings = new AppSettings();
            using var measurementEngine = new TestEngine(
                board,
                measurementVisa,
                measurementSettings,
                production);
            ResistanceStep[] steps = Enumerable.Range(1, 10)
                .Select(channel => new ResistanceStep(
                    $"R{channel}",
                    channel,
                    0,
                    double.MaxValue))
                .ToArray();
            var measurementWatch = Stopwatch.StartNew();
            List<ResistanceResult> results = await measurementEngine.MeasureResistanceStepsAsync(
                steps,
                update =>
                {
                    if (update.ResultText == "ĐANG ĐO")
                    {
                        Console.WriteLine(
                            $"RESISTANCE_MEASURE {update.Name}/CH{update.Channel} state=MEASURING elapsedMs={measurementWatch.Elapsed.TotalMilliseconds:0.###}");
                        return;
                    }

                    Console.WriteLine(
                        $"RESISTANCE_MEASURE {update.Name}/CH{update.Channel} value=\"{update.Display}\" " +
                        $"result={update.ResultText} samples={update.SampleCount} stableMs={update.StabilizationTimeMs} " +
                        $"elapsedMs={measurementWatch.Elapsed.TotalMilliseconds:0.###} raw=\"{measurementVisa.LastRawResistanceResponse}\"");
                });
            measurementWatch.Stop();
            Console.WriteLine(
                $"RESISTANCE_MEASURE SUMMARY channels={results.Count} pass={results.Count(result => result.Passed)} " +
                $"fail={results.Count(result => !result.Passed)} elapsedMs={measurementWatch.Elapsed.TotalMilliseconds:0.###}");
        }
    }
    finally
    {
        try { await board.ReleaseResistanceRouteAsync(CancellationToken.None); } catch (Exception ex) { Console.Error.WriteLine($"RELEASE_FAIL: {ex.Message}"); }
        try { await board.AllRelaysOffAsync(CancellationToken.None); } catch (Exception ex) { Console.Error.WriteLine($"RELAYS_OFF_FAIL: {ex.Message}"); }
        try { await board.StopScanAsync(CancellationToken.None); } catch (Exception ex) { Console.Error.WriteLine($"STOP_FAIL: {ex.Message}"); }
        try { await board.DisconnectAsync(); } catch (Exception ex) { Console.Error.WriteLine($"DISCONNECT_FAIL: {ex.Message}"); }
        diagnosticEngine?.Dispose();
        diagnosticVisa?.Dispose();
        trace?.Dispose();
    }
}

return 0;

bool Has(string key) => args.Contains(key, StringComparer.OrdinalIgnoreCase);

string? Value(string key)
{
    int index = Array.FindIndex(args, value => value.Equals(key, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

int IntValue(string key, int fallback, int minimum, int maximum)
{
    string? text = Value(key);
    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
        ? Math.Clamp(value, minimum, maximum)
        : fallback;
}

static bool IsTargetBoard(D2xxDeviceInfo device) =>
    device.Id == 0x04036001 &&
    device.Description.Contains("FT245R", StringComparison.OrdinalIgnoreCase) &&
    device.Description.Contains("USB FIFO", StringComparison.OrdinalIgnoreCase);

static void UpdateMaximum(ref long target, long value)
{
    long current;
    do
    {
        current = Volatile.Read(ref target);
        if (value <= current)
            return;
    }
    while (Interlocked.CompareExchange(ref target, value, current) != current);
}
