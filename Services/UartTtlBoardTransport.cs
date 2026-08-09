using System.IO.Ports;
using System.Text;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// Windows implementation of the proven Raspberry-Pi JBZ ASCII protocol.
/// Electrical/protocol settings are identical to V11 on Pi: 115200 8N1,
/// CRLF, no software/hardware flow-control.
/// </summary>
public sealed class UartTtlBoardTransport : IBoardTransport, IFirmwareProtocolBoard
{
    readonly ProductionSettings _production;
    readonly SemaphoreSlim _writeLock = new(1, 1);
    readonly SemaphoreSlim _connectLock = new(1, 1);
    SerialPort? _serial;
    CancellationTokenSource? _readerCts;
    Task? _readerTask;
    BoardCapacity _capacity;
    bool _scanning;
    int? _pendingMaxExt;
    int _maxExtSent;
    int _disposed;

    public UartTtlBoardTransport(ProductionSettings production)
    {
        _production = production;
        _capacity = BoardCapacity.FromSettings(production);
    }

    public bool IsConnected => _serial?.IsOpen == true;
    public bool IsScanning => IsConnected && _scanning;
    public BoardCapacity Capacity => _capacity;
    public bool UsesFirmwareCycleResult => true;
    public string ActivePort => _serial?.PortName ?? string.Empty;

    // UART firmware phát sự kiện mức cao qua ProtocolEventReceived và không có
    // ScanFrame nhị phân. Giữ event no-op để thực thi cùng IBoardTransport.
    public event EventHandler<ScanFrame>? FrameReceived
    {
        add { }
        remove { }
    }
    public event EventHandler<string>? Log;
    public event EventHandler<BoardProtocolEvent>? ProtocolEventReceived;

    public async Task<BoardConnectionInfo> ConnectAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _connectLock.WaitAsync(ct);
        try
        {
            if (IsConnected)
                return new BoardConnectionInfo("JBZ UART TTL", ActivePort);

            string port = await ResolvePortAsync(ct);
            var serial = CreatePort(port);
            serial.Open();
            serial.DiscardInBuffer();
            serial.DiscardOutBuffer();
            _serial = serial;
            _readerCts = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReaderLoopAsync(_readerCts.Token));

            Log?.Invoke(this, $"UART OPEN {port} 115200 8N1 CRLF flow=NONE");
            string idn = await QueryLineAsync("*IDN?", "IDN", TimeSpan.FromSeconds(2), ct);
            string model = string.Empty;
            try
            {
                model = await QueryLineAsync(":MODELNAME?", "MODELNAME", TimeSpan.FromSeconds(1), ct);
            }
            catch (TimeoutException)
            {
                Log?.Invoke(this, "UART MODELNAME timeout - vẫn giữ kết nối vì *IDN? hợp lệ.");
            }

            Log?.Invoke(this, $"UART IDENTIFIED {port}: {idn}" +
                (string.IsNullOrWhiteSpace(model) ? string.Empty : $" | {model}"));
            _scanning = true; // listening only; DOES NOT send :START here.
            return new BoardConnectionInfo("JBZ UART TTL", port);
        }
        catch
        {
            await CloseSerialAsync();
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        _scanning = false;
        _pendingMaxExt = null;
        Interlocked.Exchange(ref _maxExtSent, 0);
        await CloseSerialAsync();
    }

    public async Task HandshakeAsync(CancellationToken ct = default)
    {
        _ = await QueryLineAsync("*IDN?", "IDN", TimeSpan.FromSeconds(2), ct);
    }

    public async Task ResetClearAsync(CancellationToken ct = default)
    {
        // UART firmware uses CLEAR as an RX event. RESET is only needed after
        // model upload/bootloader and must not be sent during production cycles.
        await Task.CompletedTask;
    }

    public void ConfigureScanRange(int maxIo)
    {
        _capacity = BoardCapacity.FromSettings(_production);
    }

    public Task StartScanAsync(BoardScanMode mode = BoardScanMode.Production, CancellationToken ct = default)
    {
        // Unlike D2XX, UART firmware has no harmless continuous scan command.
        // Mark the listener active but do NOT start a product cycle here.
        _scanning = IsConnected;
        return Task.CompletedTask;
    }

    public Task StopScanAsync(CancellationToken ct = default)
    {
        _scanning = false;
        return Task.CompletedTask;
    }

    public Task EnterIdleAsync(CancellationToken ct = default)
    {
        _pendingMaxExt = null;
        Interlocked.Exchange(ref _maxExtSent, 0);
        return Task.CompletedTask;
    }

    public Task SelectResistanceRouteAsync(ResistanceStep step, CancellationToken ct = default) =>
        throw new NotSupportedException("UART TTL firmware dùng :RESISTORTEST, không dùng relay route D2XX.");

    public Task ReleaseResistanceRouteAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SetRelayAsync(int relay, CancellationToken ct = default) =>
        throw new NotSupportedException("Relay D2XX không tồn tại trên backend UART TTL. Dùng protocol PASSPEN/UNCONNECT.");

    public Task AllRelaysOffAsync(CancellationToken ct = default) => Task.CompletedTask;


    public async Task<string> QueryModelNameAsync(CancellationToken ct = default)
    {
        string raw = await QueryLineAsync(":MODELNAME?", "MODELNAME", TimeSpan.FromSeconds(2), ct);
        string[] parts = raw.Split(',');
        return parts.Length > 1 ? parts[1].Trim() : string.Empty;
    }

    public async Task UploadModelProfileAsync(UartModelProfile profile, CancellationToken ct = default)
    {
        EnsureOpen();
        foreach (UartModelCommand command in profile.Commands)
        {
            string ack = await QueryExpectedAsync(command, ct);
            Log?.Invoke(this, $"UART MODEL ACK {ack}");
        }
        _production.LastUartModelPath = profile.SourcePath;
    }

    async Task<string> QueryExpectedAsync(UartModelCommand command, CancellationToken ct)
    {
        Drain("ACK");
        await SendLineAsync(command.Tx, ct);
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(100, command.TimeoutMs));
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            lock (_waitGate)
            {
                if (_waitQueues.TryGetValue("ACK", out Queue<BoardProtocolEvent>? q))
                {
                    while (q.Count > 0)
                    {
                        string raw = q.Dequeue().Raw;
                        bool match = command.ExpectMode.Equals("prefix", StringComparison.OrdinalIgnoreCase)
                            ? raw.StartsWith(command.ExpectValue, StringComparison.OrdinalIgnoreCase)
                            : raw.Equals(command.ExpectValue, StringComparison.OrdinalIgnoreCase);
                        if (match) return raw;
                        if (raw.StartsWith(":ERROR", StringComparison.OrdinalIgnoreCase) || raw.StartsWith(":NAK", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Firmware từ chối {command.Tx}: {raw}");
                    }
                }
            }
            await Task.Delay(10, ct);
        }
        throw new TimeoutException($"Timeout chờ ACK '{command.ExpectValue}' sau {command.Tx}");
    }

    public async Task StartFirmwareCycleAsync(int maxExt = 0, CancellationToken ct = default)
    {
        EnsureOpen();
        _pendingMaxExt = maxExt;
        Interlocked.Exchange(ref _maxExtSent, 0);
        _scanning = true;
        await SendLineAsync(":START", ct);
    }

    public Task SendPassPenAsync(int delayMs, int pinCount, CancellationToken ct = default) =>
        SendLineAsync($":PASSPEN,{Math.Max(0, delayMs)},{Math.Max(1, pinCount)}", ct);

    public Task RequestUnconnectAsync(int delayMs, int pinCount, CancellationToken ct = default) =>
        SendLineAsync($":UNCONNECT,{Math.Max(0, delayMs)},{Math.Max(1, pinCount)}", ct);

    async Task ReaderLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                SerialPort? serial = _serial;
                if (serial is null || !serial.IsOpen)
                    return;

                string line = await Task.Run(serial.ReadLine, ct);
                line = line.TrimEnd('\r', '\n');
                if (line.Length == 0)
                    continue;

                Log?.Invoke(this, $"RX {line}");
                BoardProtocolEvent evt = Parse(line);
                ProtocolEventReceived?.Invoke(this, evt);

                if (evt.Family == "MEASURE" &&
                    _pendingMaxExt is int maxExt &&
                    Interlocked.CompareExchange(ref _maxExtSent, 1, 0) == 0)
                {
                    await SendLineAsync($":MAXEXT,{maxExt}", ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (TimeoutException)
            {
                // ReadTimeout is short by design; this is not a disconnect.
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    _scanning = false;
                    Log?.Invoke(this, $"UART ERROR {ex.GetType().Name}: {ex.Message}");
                }
                return;
            }
        }
    }

    readonly Dictionary<string, Queue<BoardProtocolEvent>> _waitQueues = new(StringComparer.OrdinalIgnoreCase);
    readonly object _waitGate = new();

    async Task<string> QueryLineAsync(string command, string family, TimeSpan timeout, CancellationToken ct)
    {
        Drain(family);
        await SendLineAsync(command, ct);
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            lock (_waitGate)
            {
                if (_waitQueues.TryGetValue(family, out Queue<BoardProtocolEvent>? q) && q.Count > 0)
                    return q.Dequeue().Raw;
            }
            await Task.Delay(20, ct);
        }
        throw new TimeoutException($"Timeout chờ {family} sau {command}");
    }

    void Drain(string family)
    {
        lock (_waitGate)
        {
            if (_waitQueues.TryGetValue(family, out Queue<BoardProtocolEvent>? q))
                q.Clear();
        }
    }

    async Task SendLineAsync(string command, CancellationToken ct)
    {
        EnsureOpen();
        byte[] payload = Encoding.ASCII.GetBytes(command.TrimEnd('\r', '\n') + "\r\n");
        await _writeLock.WaitAsync(ct);
        try
        {
            SerialPort serial = _serial!;
            await serial.BaseStream.WriteAsync(payload, ct);
            await serial.BaseStream.FlushAsync(ct);
            Log?.Invoke(this, $"TX {command}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    async Task<string> ResolvePortAsync(CancellationToken ct)
    {
        string preferred = (_production.UartPort ?? string.Empty).Trim();
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferred))
            candidates.Add(preferred);
        candidates.AddRange(SerialPort.GetPortNames()
            .OrderBy(ParseComNumber)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(x => !candidates.Contains(x, StringComparer.OrdinalIgnoreCase)));

        if (candidates.Count == 0)
            throw new InvalidOperationException("Windows không phát hiện cổng COM nào cho JBZ UART TTL.");

        foreach (string port in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (await ProbePortAsync(port, ct))
                return port;
        }

        throw new InvalidOperationException(
            "Không tìm thấy firmware JBZ UART TTL. Kiểm tra USB-UART, TX/RX/GND, mức TTL 3.3V và 115200 8N1.");
    }

    static int ParseComNumber(string port) =>
        int.TryParse(new string(port.Where(char.IsDigit).ToArray()), out int n) ? n : int.MaxValue;

    static SerialPort CreatePort(string port) => new(port, 115200, Parity.None, 8, StopBits.One)
    {
        Handshake = Handshake.None,
        ReadTimeout = 120,
        WriteTimeout = 2000,
        NewLine = "\r\n",
        DtrEnable = false,
        RtsEnable = false
    };

    static async Task<bool> ProbePortAsync(string port, CancellationToken ct)
    {
        try
        {
            using SerialPort serial = CreatePort(port);
            serial.Open();
            serial.DiscardInBuffer();
            serial.DiscardOutBuffer();
            byte[] query = Encoding.ASCII.GetBytes("*IDN?\r\n");
            await serial.BaseStream.WriteAsync(query, ct);
            await serial.BaseStream.FlushAsync(ct);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(650);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    string line = serial.ReadLine().Trim();
                    if (line.StartsWith("Universal Tester", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("UniversalTester", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (TimeoutException) { }
            }
        }
        catch
        {
            // Probe failures are expected while iterating candidate COM ports.
        }
        return false;
    }

    BoardProtocolEvent Parse(string raw)
    {
        string text = raw.Trim();
        string family = "RAW";
        string[] values = [];

        if (text.StartsWith("Universal Tester", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("UniversalTester", StringComparison.OrdinalIgnoreCase))
            family = "IDN";
        else if (text.StartsWith(":MODELNAME,", StringComparison.OrdinalIgnoreCase))
            Split("MODELNAME");
        else if (text.StartsWith(":TESTPIN,", StringComparison.OrdinalIgnoreCase))
            Split("TESTPIN");
        else if (text.StartsWith(":PIN,", StringComparison.OrdinalIgnoreCase))
            Split("PIN");
        else if (text.StartsWith(":OPEN,", StringComparison.OrdinalIgnoreCase))
            Split("OPEN");
        else if (text.StartsWith(":OTHER,", StringComparison.OrdinalIgnoreCase))
            Split("OTHER");
        else if (text.StartsWith(":CIRCUIT,", StringComparison.OrdinalIgnoreCase))
            Split("CIRCUIT");
        else if (text.StartsWith(":INPUT,", StringComparison.OrdinalIgnoreCase))
            Split("INPUT");
        else if (text.StartsWith(":OUTPUT,", StringComparison.OrdinalIgnoreCase))
            Split("OUTPUT");
        else if (text.StartsWith(":RESISTOR,", StringComparison.OrdinalIgnoreCase))
            Split("RESISTOR");
        else if (text.StartsWith(":AMPARE,", StringComparison.OrdinalIgnoreCase))
            Split("AMPARE");
        else if (text.StartsWith(":VOLTAGE,", StringComparison.OrdinalIgnoreCase))
            Split("VOLTAGE");
        else if (text.Equals(":MEASURE", StringComparison.OrdinalIgnoreCase)) family = "MEASURE";
        else if (text.Equals(":CLEAR", StringComparison.OrdinalIgnoreCase)) family = "CLEAR";
        else if (text.Equals(":PEN", StringComparison.OrdinalIgnoreCase)) family = "PEN";
        else if (text.Equals(":REMOVAL", StringComparison.OrdinalIgnoreCase)) family = "REMOVAL";
        else if (text.Equals(":UNCONNECT", StringComparison.OrdinalIgnoreCase)) family = "UNCONNECT";
        else if (text.StartsWith(":START", StringComparison.OrdinalIgnoreCase)) Split("START");
        else if (text.StartsWith(":ERROR", StringComparison.OrdinalIgnoreCase) || text.StartsWith(":NAK", StringComparison.OrdinalIgnoreCase)) family = "ERROR";

        var evt = new BoardProtocolEvent(DateTime.Now, family, raw, values);
        bool isAck = text.StartsWith(":OK,", StringComparison.OrdinalIgnoreCase) || text.StartsWith(":ERROR", StringComparison.OrdinalIgnoreCase) || text.StartsWith(":NAK", StringComparison.OrdinalIgnoreCase);
        // Only transactional handshake families need queues. Runtime OPEN/OTHER/
        // TESTPIN can run for hours and must not accumulate in memory.
        if (family is "IDN" or "MODELNAME" || isAck)
        {
            lock (_waitGate)
            {
                string queueKey = isAck ? "ACK" : family;
                if (!_waitQueues.TryGetValue(queueKey, out Queue<BoardProtocolEvent>? q))
                    _waitQueues[queueKey] = q = new Queue<BoardProtocolEvent>();
                q.Enqueue(evt);
            }
        }
        return evt;

        void Split(string name)
        {
            family = name;
            values = text.Split(',').Skip(1).Select(x => x.Trim()).ToArray();
        }
    }

    void EnsureOpen()
    {
        if (!IsConnected)
            throw new InvalidOperationException("JBZ UART TTL chưa kết nối.");
    }

    async Task CloseSerialAsync()
    {
        CancellationTokenSource? cts = _readerCts;
        _readerCts = null;
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
        }
        Task? reader = _readerTask;
        _readerTask = null;
        if (reader is not null)
        {
            try { await Task.WhenAny(reader, Task.Delay(500)); } catch { }
        }
        SerialPort? serial = _serial;
        _serial = null;
        if (serial is not null)
        {
            try { if (serial.IsOpen) serial.Close(); } catch { }
            serial.Dispose();
        }
        cts?.Dispose();
    }

    void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(UartTtlBoardTransport));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await DisconnectAsync();
        _writeLock.Dispose();
        _connectLock.Dispose();
    }
}
