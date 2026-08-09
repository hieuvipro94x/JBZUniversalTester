using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// FTDI D2XX transport aligned with the production Htdrv trace captured on
/// 2026-08-07. Scan initialization is stateful: INIT_1/INIT_2 prepare the board,
/// then START_SCAN is sent separately.
/// </summary>
public sealed class D2xxBoardTransport : IBoardTransport
{
    const uint FT_OK = 0;
    const uint FT_OPEN_BY_SERIAL_NUMBER = 1;
    const uint FT_OPEN_BY_DESCRIPTION = 2;
    const uint TargetFtdiId = 0x04036001; // VID 0403 / PID 6001
    const string TargetDescription = "FT245R USB FIFO";
    const uint FT_PURGE_RX = 1;
    const uint FT_PURGE_TX = 2;

    static readonly byte[] CmdHandshake = [0x8A, 0x01, 0x01, 0x01];
    static readonly byte[] CmdInit1 = [0x91, 0x00, 0x00, 0x00];
    static readonly byte[] CmdInit2 = [0x90, 0x00, 0x00, 0x30];
    static readonly byte[] CmdStopScan = [0x8D, 0x00, 0x00, 0x00];
    static readonly byte[] CmdResetClear = [0x80, 0x00, 0x00, 0x00];

    readonly string _serial;
    string _connectedSerial = string.Empty;
    readonly ProductionSettings _production;
    readonly SemaphoreSlim _ioLock = new(1, 1);
    readonly SemaphoreSlim _connectLock = new(1, 1);
    readonly SemaphoreSlim _scanSwitchLock = new(1, 1);

    IntPtr _handle;
    CancellationTokenSource? _scanCts;
    Task? _scanTask;
    string _lastScanSignature = string.Empty;
    long _lastScanLogTick;
    bool _scanPrepared;
    BoardCapacity _capacity;
    int _configuredCardCount;
    int _requiredCardCount = 1;
    int _expectedIoCount;
    BoardScanMode _scanMode = BoardScanMode.Production;
    long _scanGeneration;
    int _controlWaiters;
    int _disposeStarted;
    int _disposed;

    // Timing measured from the original Htdrv shutdown trace 2026-08-07 16:25.
    // These delays are only used for FINAL application shutdown, not normal
    // TestView/TestPin switching.
    const int FinalStopToResetMs = 280;
    const int FinalResetToInit1Ms = 170;
    const int FinalInitDelayMs = 350;
    const int FinalInit2ToStopMs = 330;
    const int FinalStopToCloseMs = 130;

    public bool IsConnected => _handle != IntPtr.Zero;
    public bool IsScanning => _scanTask is { IsCompleted: false };
    public BoardCapacity Capacity => _capacity;

    public event EventHandler<ScanFrame>? FrameReceived;
    public event EventHandler<string>? Log;

    public D2xxBoardTransport(
        string serial,
        ProductionSettings? production = null)
    {
        _serial = serial;
        _production = production ?? new ProductionSettings();
        _capacity = BoardCapacity.FromSettings(_production);
        _configuredCardCount = ResolveConfiguredScanCardCount();
        _expectedIoCount = _capacity.TotalIoCapacity;
    }

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    static extern uint FT_OpenEx(string argument, uint flags, out IntPtr handle);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_CreateDeviceInfoList(out uint numberOfDevices);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    static extern uint FT_GetDeviceInfoDetail(
        uint index,
        out uint flags,
        out uint type,
        out uint id,
        out uint locationId,
        [Out] StringBuilder serialNumber,
        [Out] StringBuilder description,
        out IntPtr handle);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_Close(IntPtr handle);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_Read(IntPtr handle, byte[] buffer, uint bytesToRead, out uint bytesReturned);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_Write(IntPtr handle, byte[] buffer, uint bytesToWrite, out uint bytesWritten);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_Purge(IntPtr handle, uint mask);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_SetTimeouts(IntPtr handle, uint readTimeout, uint writeTimeout);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_SetLatencyTimer(IntPtr handle, byte latency);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_SetUSBParameters(IntPtr handle, uint inTransferSize, uint outTransferSize);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_SetBaudRate(IntPtr handle, uint baudRate);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_SetDataCharacteristics(IntPtr handle, byte wordLength, byte stopBits, byte parity);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_SetFlowControl(IntPtr handle, ushort flowControl, byte xon, byte xoff);

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_GetQueueStatus(IntPtr handle, out uint amountInRxQueue);

    static string GetStatusName(uint status) => status switch
    {
        0 => "FT_OK",
        1 => "FT_INVALID_HANDLE",
        2 => "FT_DEVICE_NOT_FOUND",
        3 => "FT_DEVICE_NOT_OPENED",
        4 => "FT_IO_ERROR",
        5 => "FT_INSUFFICIENT_RESOURCES",
        6 => "FT_INVALID_PARAMETER",
        7 => "FT_INVALID_BAUD_RATE",
        10 => "FT_FAILED_TO_WRITE_DEVICE",
        17 => "FT_NOT_SUPPORTED",
        18 => "FT_OTHER_ERROR",
        _ => $"FT_STATUS_{status}"
    };

    static void Ensure(uint status, string api)
    {
        if (status != FT_OK)
            throw new InvalidOperationException(
                $"{api} lỗi FTDI: {status} ({GetStatusName(status)})");
    }

    private sealed record FtdiCandidate(string Serial, string Description, uint Id, uint LocationId);

    private FtdiCandidate FindTargetBoard()
    {
        Ensure(FT_CreateDeviceInfoList(out uint count), "FT_CreateDeviceInfoList");

        var matches = new List<FtdiCandidate>();
        for (uint index = 0; index < count; index++)
        {
            var serial = new StringBuilder(64);
            var description = new StringBuilder(128);

            uint status = FT_GetDeviceInfoDetail(
                index,
                out _,
                out _,
                out uint id,
                out uint locationId,
                serial,
                description,
                out _);

            if (status != FT_OK)
                continue;

            string serialText = serial.ToString().TrimEnd('\0', ' ');
            string descriptionText = description.ToString().TrimEnd('\0', ' ');

            // Chỉ nhận đúng họ bo đã thấy trong trace/Device Info:
            // FT245R USB FIFO + VID/PID 0403:6001 (ID 0x04036001).
            if (id == TargetFtdiId &&
                descriptionText.Contains("FT245R", StringComparison.OrdinalIgnoreCase) &&
                descriptionText.Contains("USB FIFO", StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(new FtdiCandidate(
                    serialText,
                    descriptionText,
                    id,
                    locationId));
            }
        }

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                "Không tìm thấy bo FT245R USB FIFO ID 0x04036001. " +
                "Kiểm tra nguồn bo, cáp USB và driver FTDI D2XX.");
        }

        // Nếu appsettings còn serial cũ AI050MBB nhưng bo thực tế là A90764PH,
        // không được fail. Serial chỉ dùng để ưu tiên khi có nhiều bo cùng loại.
        if (!string.IsNullOrWhiteSpace(_serial))
        {
            FtdiCandidate? preferred = matches.FirstOrDefault(x =>
                x.Serial.Equals(_serial, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return preferred;
        }

        return matches[0];
    }

    public async Task<BoardConnectionInfo> ConnectAsync(
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _connectLock.WaitAsync(ct);
        try
        {
            if (IsConnected)
            {
                string serial = string.IsNullOrWhiteSpace(_connectedSerial)
                    ? _serial
                    : _connectedSerial;
                return new BoardConnectionInfo(TargetDescription, serial);
            }

            try
            {
                FtdiCandidate? candidate = null;

                // Windows/D2XX đôi khi cần vài chục ms sau khi app cũ vừa đóng.
                // Retry rất ngắn, không khóa UI và không yêu cầu người dùng bấm lại.
                for (int attempt = 1; attempt <= 6; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        candidate = await Task.Run(FindTargetBoard, ct);
                        break;
                    }
                    catch (InvalidOperationException) when (attempt < 6)
                    {
                        await Task.Delay(80, ct);
                    }
                }

                candidate ??= await Task.Run(FindTargetBoard, ct);

                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();

                    uint openStatus = FT_OpenEx(
                        candidate.Serial,
                        FT_OPEN_BY_SERIAL_NUMBER,
                        out IntPtr openedHandle);

                    Ensure(openStatus, "FT_OpenEx");
                    _handle = openedHandle;
                    _connectedSerial = candidate.Serial;

                    Ensure(FT_SetBaudRate(_handle, 115200), "FT_SetBaudRate");
                    Ensure(FT_SetDataCharacteristics(_handle, 8, 0, 0), "FT_SetDataCharacteristics");
                    Ensure(FT_SetFlowControl(_handle, 0, 0, 0), "FT_SetFlowControl");
                    Ensure(FT_SetTimeouts(_handle, 50, 150), "FT_SetTimeouts");
                    Ensure(FT_SetUSBParameters(_handle, 65536, 65536), "FT_SetUSBParameters");

                    byte latencyMs = checked((byte)Math.Clamp(_production.UsbDelay, 1, 16));
                    Ensure(FT_SetLatencyTimer(_handle, latencyMs), "FT_SetLatencyTimer");
                    Ensure(FT_Purge(_handle, FT_PURGE_RX | FT_PURGE_TX), "FT_Purge");
                }, ct);

                _scanPrepared = false;

                // Fast startup: KHÔNG dùng handshake 8A/0F để chặn kết nối.
                // Chỉ kéo firmware về IDLE rồi INIT đúng sequence đã thấy trong trace.
                await WriteAsync(CmdStopScan, ct);
                await Task.Delay(30, ct);
                // V12.4: khi vừa kết nối, relay phải ở trạng thái chờ/không kích.
                // R1 chỉ mở JIG, R2 chỉ MARKING khi workflow yêu cầu.
                await AllRelaysOffAsync(ct);
                await PurgeAsync(ct);
                await PrepareScanAsync(ct);

                Log?.Invoke(
                    this,
                    $"Đã mở đúng FTDI {candidate.Description} [{candidate.Serial}] " +
                    $"ID 0x{candidate.Id:X8}; sẵn sàng scan.");

                return new BoardConnectionInfo(candidate.Description, candidate.Serial);
            }
            catch
            {
                IntPtr handle = _handle;
                _handle = IntPtr.Zero;
                _connectedSerial = string.Empty;
                _scanPrepared = false;

                if (handle != IntPtr.Zero)
                {
                    try { FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX); } catch { }
                    try { FT_Close(handle); } catch { }
                }

                throw;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        // Toàn bộ vòng đời D2XX được serialize: không FT_Close khi worker vẫn còn
        // đọc handle. Sequence cuối bám trace Htdrv:
        // STOP -> RESET -> INIT1 -> INIT2 -> STOP -> FT_Close.
        await _connectLock.WaitAsync();
        try
        {
            await _scanSwitchLock.WaitAsync();
            try
            {
                bool hadReader = _scanCts is not null || _scanTask is not null || IsScanning;
                await StopScanCoreAsync(CancellationToken.None);

                IntPtr handle = _handle;
                if (handle == IntPtr.Zero)
                    return;

                if (!hadReader)
                {
                    try { await WriteAsync(CmdStopScan, CancellationToken.None); } catch { }
                }

                try
                {
                    await AllRelaysOffAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log?.Invoke(this, $"Relay OFF khi thoát: {ex.Message}");
                }

                try
                {
                    // Timing gần đúng trace gốc; chỉ chạy khi thoát hẳn app.
                    await Task.Delay(FinalStopToResetMs);
                    await ResetClearAsync(CancellationToken.None);

                    await Task.Delay(FinalResetToInit1Ms);
                    await WriteAsync(CmdInit1, CancellationToken.None);
                    await Task.Delay(FinalInitDelayMs);
                    await WriteAsync(CmdInit2, CancellationToken.None);
                    _scanPrepared = true;

                    await Task.Delay(FinalInit2ToStopMs);
                    await WriteAsync(CmdStopScan, CancellationToken.None);
                    await Task.Delay(FinalStopToCloseMs);
                }
                catch (Exception ex)
                {
                    // Dù firmware không trả lời, vẫn phải trả handle về driver/OS.
                    Log?.Invoke(this, $"Cleanup board trước FT_Close chưa hoàn chỉnh: {ex.Message}");
                }

                await _ioLock.WaitAsync();
                try
                {
                    handle = _handle;
                    if (handle != IntPtr.Zero)
                    {
                        try { FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX); } catch { }
                        try
                        {
                            uint closeStatus = FT_Close(handle);
                            if (closeStatus != FT_OK)
                                Log?.Invoke(this, $"FT_Close trả {closeStatus} ({GetStatusName(closeStatus)}).");
                        }
                        catch (Exception ex)
                        {
                            Log?.Invoke(this, $"FT_Close lỗi: {ex.Message}");
                        }
                    }

                    _handle = IntPtr.Zero;
                }
                finally
                {
                    _ioLock.Release();
                }
            }
            finally
            {
                _scanPrepared = false;
                _scanSwitchLock.Release();
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task HandshakeAsync(CancellationToken ct = default)
    {
        await WriteAsync(CmdHandshake, ct);
        byte[] rx = await ReadExactOrAvailableAsync(2, 500, ct);

        if (rx.Length < 2 || rx[0] != 0x0F || rx[1] != 0x00)
            throw new InvalidOperationException(
                $"Handshake không hợp lệ: {Convert.ToHexString(rx)}");
    }

    public async Task ResetClearAsync(CancellationToken ct = default)
    {
        // Trace production chứng minh sau STOP_SCAN -> RESET_CLEAR -> relay,
        // Htdrv gửi START_SCAN trực tiếp, không INIT lại. Vì vậy RESET_CLEAR
        // không được làm mất trạng thái prepared.
        await WriteAsync(CmdResetClear, ct);
    }

    async Task PrepareScanAsync(CancellationToken ct)
    {
        EnsureConnected();
        if (_scanPrepared)
            return;

        await WriteAsync(CmdInit1, ct);
        await Task.Delay(350, ct);
        await WriteAsync(CmdInit2, ct);
        _scanPrepared = true;
    }

    private int ResolveConfiguredScanCardCount()
    {
        // V12.9: không subsystem nào tự tính card. BoardCapacity là nguồn duy nhất.
        _capacity = BoardCapacity.FromSettings(_production);
        _production.ExpansionCardCount = _capacity.ExpansionModuleCount;
        _production.CardCount = _capacity.ScanCardCount;
        _production.StartCardNumber = _capacity.StartCardNumber;
        return _capacity.ScanCardCount;
    }

    public void ConfigureScanRange(int maxIo)
    {
        ProductionConfigService.ReloadInto(_production);

        int requiredExpansion = BoardCapacity.RequiredExpansionModulesForIo(
            maxIo,
            _production.StartCardNumber);
        _requiredCardCount = requiredExpansion;
        _configuredCardCount = ResolveConfiguredScanCardCount();
        _expectedIoCount = _capacity.TotalIoCapacity;

        if (IsConnected)
        {
            byte latencyMs = checked((byte)Math.Clamp(_production.UsbDelay, 1, 16));

            // Không đổi latency đồng thời với FT_Read.
            _ioLock.Wait();
            try
            {
                if (_handle != IntPtr.Zero)
                    Ensure(FT_SetLatencyTimer(_handle, latencyMs), "FT_SetLatencyTimer");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        string fit = _requiredCardCount <= _configuredCardCount
            ? "ĐỦ CARD"
            : $"THIẾU CARD - cần {_requiredCardCount}";

        Log?.Invoke(
            this,
            $"Cấu hình scan V12.9: {_capacity.ExpansionModuleCount} module mở rộng; " +
            $"{_capacity.PhysicalCardCount} card vật lý x {BoardCapacity.IoPerPhysicalCard} I/O; " +
            $"scan={_capacity.ScanCardCount}; capacity {_capacity.FirstGlobalIo}-{_capacity.LastGlobalIo}; " +
            $"START_SCAN xx={_capacity.StartScanParameter}; model max I/O {maxIo}; {fit}.");
    }

    public async Task StartScanAsync(
        BoardScanMode mode = BoardScanMode.Production,
        CancellationToken ct = default)
    {
        await _scanSwitchLock.WaitAsync(ct);
        try
        {
            // Nếu mode cũ đang chạy, STOP sạch trước. STOP/RESET không làm mất
            // INIT của board nên sau đó có thể START_SCAN lại ngay như Htdrv gốc.
            await StopScanCoreAsync(ct);
            EnsureConnected();

            // Transport luôn quét theo SỐ CARD ĐÃ CẤU HÌNH. Việc model có
            // vượt dung lượng card hay không được MainWindow/TestView kiểm tra
            // ở tầng nghiệp vụ. Nhờ vậy vừa kết nối bo là có thể scan liên tục
            // kể cả trước khi người vận hành bấm BẮT ĐẦU KIỂM TRA.

            if (!_scanPrepared)
                await PrepareScanAsync(ct);

            _lastScanSignature = string.Empty;
            _scanMode = mode;

            var decoder = new BoardIoDecoder();
            decoder.ConfigureCapacity(_capacity);
            decoder.ConfigureMode(mode);
            decoder.Reset();

            byte[] startScan =
            [
                0x8C,
                0x00,
                checked((byte)_capacity.StartScanParameter),
                0x00
            ];

            // WriteAsync đã PURGE + WRITE dưới cùng một D2XX lock.
            await WriteAsync(startScan, ct);

            // QUAN TRỌNG: START_SCAN không làm mất INIT. Giữ prepared=true để
            // STOP -> RESET -> START tiếp theo diễn ra ngay, không chờ INIT 700 ms.
            _scanPrepared = true;

            long generation = Interlocked.Increment(ref _scanGeneration);
            _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _scanTask = ScanLoopAsync(
                decoder,
                mode,
                generation,
                _scanCts.Token);

            Log?.Invoke(this, $"SCAN MODE = {mode}; generation={generation}.");
        }
        finally
        {
            _scanSwitchLock.Release();
        }
    }

    public async Task StopScanAsync(CancellationToken ct = default)
    {
        await _scanSwitchLock.WaitAsync(ct);
        try
        {
            await StopScanCoreAsync(ct);
        }
        finally
        {
            _scanSwitchLock.Release();
        }
    }

    async Task StopScanCoreAsync(CancellationToken ct)
    {
        bool hadActiveScan = _scanCts is not null || _scanTask is not null || IsScanning;
        CancellationTokenSource? cts = _scanCts;
        Task? scanTask = _scanTask;

        if (!hadActiveScan)
            return;

        // Vô hiệu callback cũ trước tiên.
        Interlocked.Increment(ref _scanGeneration);

        // Dừng firmware TRƯỚC khi hủy reader. Vì toàn bộ D2XX call dùng _ioLock,
        // STOP không thể chạy đồng thời với FT_Read.
        if (IsConnected)
        {
            try
            {
                await WriteAsync(CmdStopScan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, $"STOP_SCAN báo lỗi: {ex.Message}");
                // Vẫn tiếp tục hủy worker để không để thread/handle treo.
            }
        }

        cts?.Cancel();

        if (scanTask is not null)
        {
            try
            {
                // Sau khi D2XX được serialize, worker chỉ giữ native call trong
                // thời gian rất ngắn. Chờ worker thực sự chết thay vì bỏ reference
                // rồi để thread cũ dùng handle ngầm.
                await scanTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _scanCts = null;
        _scanTask = null;
        cts?.Dispose();

        // STOP_SCAN không làm mất trạng thái INIT/prepared.
        // Đây là khác biệt quan trọng so với V10.7 và giúp chuyển mode rất nhanh.
    }

    public async Task EnterIdleAsync(CancellationToken ct = default)
    {
        // Dùng khi đóng TestView/TestPin nhưng vẫn giữ app/FTDI mở:
        // STOP -> relay OFF -> RESET. Không INIT lại, không FT_Close.
        // Trace gốc cho thấy board có thể START_SCAN trực tiếp sau chuỗi này.
        await _scanSwitchLock.WaitAsync(ct);
        try
        {
            bool hadReader = _scanCts is not null || _scanTask is not null || IsScanning;
            await StopScanCoreAsync(ct);

            if (!IsConnected)
                return;

            if (!hadReader)
                await WriteAsync(CmdStopScan, ct);

            await AllRelaysOffAsync(ct);
            await ResetClearAsync(ct);
            await PurgeAsync(ct);

            _scanPrepared = true;
            Log?.Invoke(this, "Board đã về IDLE sạch, giữ FTDI mở và sẵn sàng START_SCAN lại.");
        }
        finally
        {
            _scanSwitchLock.Release();
        }
    }

    public async Task SelectResistanceRouteAsync(
        ResistanceStep step,
        CancellationToken ct = default)
    {
        EnsureConnected();

        // Captured production sequence for both R1/R2:
        // 90 00 00 01 -> ~350 ms -> 91 00 00 <channel>.
        byte[] routeA = ParseFrame(
            step.RouteA,
            [0x90, 0x00, 0x00, 0x01]);

        byte[] routeB = ParseFrame(
            step.RouteB,
            [0x91, 0x00, 0x00, checked((byte)step.Channel)]);

        await WriteAsync(routeA, ct);
        await Task.Delay(350, ct);
        await WriteAsync(routeB, ct);
    }

    public async Task ReleaseResistanceRouteAsync(
        CancellationToken ct = default)
    {
        if (!IsConnected)
            return;

        // Production trace sends three recovery/preparation cycles after R2.
        // The last INIT_2 leaves the board prepared, so after the pass relay
        // sequence the next product starts with START_SCAN directly.
        for (int cycle = 0; cycle < 3; cycle++)
        {
            await WriteAsync(CmdInit1, ct);
            await Task.Delay(350, ct);
            await WriteAsync(CmdInit2, ct);

            if (cycle < 2)
                await Task.Delay(350, ct);
        }

        _scanPrepared = true;
    }

    public Task SetRelayAsync(int relay, CancellationToken ct = default) => relay switch
    {
        1 => WriteAsync([0x8E, 0x00, 0x00, 0x01], ct),
        2 => WriteAsync([0x8E, 0x00, 0x00, 0x02], ct),
        _ => throw new ArgumentOutOfRangeException(nameof(relay))
    };

    public Task AllRelaysOffAsync(CancellationToken ct = default) =>
        IsConnected
            ? WriteAsync([0x8E, 0x00, 0x00, 0x00], ct)
            : Task.CompletedTask;

    Task ScanLoopAsync(
        BoardIoDecoder decoder,
        BoardScanMode mode,
        long generation,
        CancellationToken ct)
    {
        return Task.Factory.StartNew(
            () => ScanLoopWorker(decoder, mode, generation, ct),
            ct,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    void ScanLoopWorker(
        BoardIoDecoder decoder,
        BoardScanMode mode,
        long generation,
        CancellationToken ct)
    {
        try { Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; } catch { }

        int idleDelayMs = Math.Clamp(_production.UsbDelay, 0, 10);
        int emptyPolls = 0;
        var buffer = new byte[65536];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (Volatile.Read(ref _controlWaiters) > 0)
                {
                    Thread.Yield();
                    continue;
                }

                IntPtr handle = _handle;
                if (handle == IntPtr.Zero)
                    break;

                uint queued = 0;
                uint read = 0;

                _ioLock.Wait(ct);
                try
                {
                    handle = _handle;
                    if (handle == IntPtr.Zero)
                        break;

                    uint queueStatus = FT_GetQueueStatus(handle, out queued);
                    if (queueStatus != FT_OK)
                    {
                        if (ct.IsCancellationRequested || _handle == IntPtr.Zero)
                            break;

                        throw new InvalidOperationException(
                            $"FT_GetQueueStatus lỗi FTDI: {queueStatus} ({GetStatusName(queueStatus)})");
                    }

                    if (queued > 0)
                    {
                        int want = (int)Math.Min(queued, (uint)buffer.Length);
                        uint readStatus = FT_Read(
                            handle,
                            buffer,
                            (uint)want,
                            out read);

                        if (readStatus != FT_OK)
                        {
                            if (ct.IsCancellationRequested || _handle == IntPtr.Zero)
                                break;

                            throw new InvalidOperationException(
                                $"FT_Read lỗi FTDI: {readStatus} ({GetStatusName(readStatus)})");
                        }
                    }
                }
                finally
                {
                    _ioLock.Release();
                }

                if (queued == 0 || read == 0)
                {
                    // Giữ phản hồi đầu vào nhanh: vài chục poll đầu chỉ Yield,
                    // chỉ Sleep khi USB thực sự im lâu. Nhờ vậy bấm BẮT ĐẦU/
                    // chạm dây không phải chờ một chuỗi sleep 1 ms liên tiếp,
                    // nhưng worker cũng không chiếm 100% CPU khi jig đang rỗng.
                    emptyPolls++;
                    if (emptyPolls <= 32 || idleDelayMs == 0)
                        Thread.Yield();
                    else
                        Thread.Sleep(idleDelayMs);
                    continue;
                }

                emptyPolls = 0;

                foreach (ScanFrame decoded in decoder.Feed(
                             buffer.AsSpan(0, checked((int)read))))
                {
                    if (ct.IsCancellationRequested)
                        break;

                    if (generation != Volatile.Read(ref _scanGeneration) ||
                        mode != _scanMode ||
                        decoded.Mode != mode)
                    {
                        continue;
                    }

                    PublishFrame(decoded);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log?.Invoke(this, $"Luồng quét FTDI dừng do lỗi: {ex.Message}");

            // Nếu driver/USB rơi giữa lúc quét, không giữ một handle giả
            // IsConnected=true. Đóng handle dưới cùng D2XX lock để vòng
            // auto-reconnect của ViewModel có thể mở lại bo mà không rút nguồn.
            _ioLock.Wait();
            try
            {
                IntPtr handle = _handle;
                _handle = IntPtr.Zero;
                _connectedSerial = string.Empty;
                _scanPrepared = false;
                if (handle != IntPtr.Zero)
                {
                    try { FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX); } catch { }
                    try { FT_Close(handle); } catch { }
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }
    }

    void PublishFrame(ScanFrame decoded)
    {
        string signature = $"{decoded.Mode}:" + string.Join(",", decoded.ActiveIo.Order());
        bool signatureChanged =
            !string.Equals(signature, _lastScanSignature, StringComparison.Ordinal);

        long now = Environment.TickCount64;
        bool forceLog = decoded.UnknownBytes > 0 ||
                        (decoded.Mode == BoardScanMode.Production && !decoded.Complete);

        // Log DataGrid/ObservableCollection không được phép kéo chậm worker.
        // Chỉ log trạng thái RX tối đa khoảng 20 lần/giây; FrameReceived vẫn
        // phát TẤT CẢ frame cho TestEngine nên logic test không bị giảm tốc.
        if (forceLog || (signatureChanged && now - _lastScanLogTick >= 50))
        {
            _lastScanLogTick = now;
            _lastScanSignature = signature;

            string ioText = decoded.ActiveIo.Count == 0
                ? "không có I/O active"
                : $"I/O {string.Join(", ", decoded.ActiveIo.Order())}";

            string quality = decoded.Mode == BoardScanMode.Probe
                ? decoded.Complete
                    ? "snapshot TestPin hoàn chỉnh"
                    : "TestPin phát hiện tức thời"
                : decoded.Complete
                    ? $"frame production hoàn chỉnh, {decoded.Connections.Count} source"
                    : "frame production đầu đồng bộ/chưa hoàn chỉnh";

            string sync = decoded.UnknownBytes > 0
                ? $", bỏ {decoded.UnknownBytes} byte mất đồng bộ"
                : string.Empty;

            Log?.Invoke(
                this,
                $"RX frame #{decoded.Sequence}: {ioText} [{quality}{sync}]");
        }
        else if (signatureChanged)
        {
            // Giữ signature mới để không xếp hàng log lặp khi UI đang bận.
            _lastScanSignature = signature;
        }

        FrameReceived?.Invoke(this, decoded);
    }

    async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        EnsureConnected();

        Interlocked.Increment(ref _controlWaiters);
        try
        {
            await _ioLock.WaitAsync(ct);
            try
            {
                IntPtr handle = _handle;
                if (handle == IntPtr.Zero)
                    throw new InvalidOperationException("Bo JBZ đã đóng kết nối.");

                Ensure(FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX), "FT_Purge");
                Ensure(
                    FT_Write(handle, data, (uint)data.Length, out uint written),
                    "FT_Write");

                if (written != data.Length)
                    throw new IOException(
                        $"FT_Write thiếu byte: {written}/{data.Length}");
            }
            finally
            {
                _ioLock.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _controlWaiters);
        }

        Log?.Invoke(
            this,
            $"TX {BitConverter.ToString(data).Replace("-", " ")}");
    }

    async Task PurgeAsync(CancellationToken ct)
    {
        if (!IsConnected)
            return;

        Interlocked.Increment(ref _controlWaiters);
        try
        {
            await _ioLock.WaitAsync(ct);
            try
            {
                IntPtr handle = _handle;
                if (handle != IntPtr.Zero)
                    Ensure(FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX), "FT_Purge");
            }
            finally
            {
                _ioLock.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _controlWaiters);
        }
    }

    async Task<byte[]> ReadAvailableAsync(CancellationToken ct)
    {
        if (!IsConnected)
            return [];

        Interlocked.Increment(ref _controlWaiters);
        try
        {
            await _ioLock.WaitAsync(ct);
            try
            {
                IntPtr handle = _handle;
                if (handle == IntPtr.Zero)
                    return [];

                Ensure(
                    FT_GetQueueStatus(handle, out uint queued),
                    "FT_GetQueueStatus");

                if (queued == 0)
                    return [];

                var buffer = new byte[Math.Min(queued, 4096)];
                Ensure(
                    FT_Read(handle, buffer, (uint)buffer.Length, out uint read),
                    "FT_Read");

                return buffer[..(int)read];
            }
            finally
            {
                _ioLock.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _controlWaiters);
        }
    }

    async Task<byte[]> ReadExactOrAvailableAsync(
        int expected,
        int timeoutMs,
        CancellationToken ct)
    {
        var result = new List<byte>(expected);
        long until = Environment.TickCount64 + timeoutMs;

        while (result.Count < expected && Environment.TickCount64 < until)
        {
            result.AddRange(await ReadAvailableAsync(ct));
            if (result.Count < expected)
                await Task.Delay(1, ct);
        }

        return result.ToArray();
    }

    static byte[] ParseFrame(string text, byte[] fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        byte[] values = text
            .Split([' ', '-', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Convert.ToByte(x, 16))
            .ToArray();

        if (values.Length != 4)
            throw new InvalidDataException($"Frame phải có 4 byte: {text}");

        return values;
    }

    void EnsureConnected()
    {
        ThrowIfDisposed();

        if (!IsConnected)
            throw new InvalidOperationException("Chưa kết nối bo JBZ");
    }

    void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(D2xxBoardTransport));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        try
        {
            // Chỉ đánh dấu disposed SAU khi cleanup D2XX hoàn tất, nếu không
            // WriteAsync trong DisconnectAsync sẽ tự ném ObjectDisposedException.
            await DisconnectAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _disposed, 1);
            _ioLock.Dispose();
            _connectLock.Dispose();
            _scanSwitchLock.Dispose();
        }
    }
}
