using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed record D2xxDeviceInfo(
    string Serial,
    string Description,
    uint Id,
    uint LocationId,
    uint Type,
    bool IsOpen);

public sealed record D2xxProtocolTrace(
    DateTime TimestampUtc,
    long StopwatchTimestamp,
    string Direction,
    byte[] Data);

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
    const uint FT_EVENT_RXCHAR = 1;

    static readonly byte[] CmdHandshake = [0x8A, 0x01, 0x01, 0x01];
    static readonly byte[] CmdInit1 = D2xxResistanceRouting.BuildReleaseRouteB();
    static readonly byte[] CmdInit2 = D2xxResistanceRouting.BuildReleaseRouteA();
    static readonly byte[] CmdStopScan = [0x8D, 0x00, 0x00, 0x00];
    static readonly byte[] CmdResetClear = [0x80, 0x00, 0x00, 0x00];

    readonly string _serial;
    string _connectedSerial = string.Empty;
    readonly ProductionSettings _production;
    readonly SemaphoreSlim _ioLock = new(1, 1);
    readonly SemaphoreSlim _connectLock = new(1, 1);
    readonly SemaphoreSlim _scanSwitchLock = new(1, 1);
    readonly AutoResetEvent _rxEvent = new(false);

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
    long _lastPerfAggregateTick;
    long _pollCount;
    long _bytesReceived;
    long _framesPublished;
    long _framesReceivedTotal;
    long _lastFrameSequence;
    long _lastFrameTimestampUtcTicks;
    long _decodeTicks;
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
    public BoardScanMode CurrentScanMode => _scanMode;
    public BoardCapacity Capacity => _capacity;
    public DateTime LastFrameTimestampUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastFrameTimestampUtcTicks);
            return ticks <= 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc);
        }
    }
    public long LastFrameSequence => Interlocked.Read(ref _lastFrameSequence);
    public long FramesReceived => Interlocked.Read(ref _framesReceivedTotal);

    public event EventHandler<ScanFrame>? FrameReceived;
    public event EventHandler<string>? Log;
    public event EventHandler<D2xxProtocolTrace>? ProtocolTrace;

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

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_GetLibraryVersion(out uint libraryVersion);

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

    [DllImport("ftd2xx.dll", CallingConvention = CallingConvention.StdCall)]
    static extern uint FT_SetEventNotification(IntPtr handle, uint eventMask, IntPtr eventHandle);

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

    private sealed record FtdiCandidate(
        string Serial,
        string Description,
        uint Id,
        uint LocationId,
        bool IsOpen);

    public static uint GetD2xxLibraryVersion()
    {
        Ensure(FT_GetLibraryVersion(out uint version), "FT_GetLibraryVersion");
        return version;
    }

    public static IReadOnlyList<D2xxDeviceInfo> EnumerateDevices()
    {
        Ensure(FT_CreateDeviceInfoList(out uint count), "FT_CreateDeviceInfoList");

        var devices = new List<D2xxDeviceInfo>(checked((int)count));
        for (uint index = 0; index < count; index++)
        {
            var serial = new StringBuilder(64);
            var description = new StringBuilder(128);
            uint status = FT_GetDeviceInfoDetail(
                index,
                out uint flags,
                out uint type,
                out uint id,
                out uint locationId,
                serial,
                description,
                out _);

            if (status != FT_OK)
                continue;

            devices.Add(new D2xxDeviceInfo(
                serial.ToString().TrimEnd('\0', ' '),
                description.ToString().TrimEnd('\0', ' '),
                id,
                locationId,
                type,
                (flags & 0x01) != 0));
        }

        return devices;
    }

    private FtdiCandidate FindTargetBoard()
    {
        List<FtdiCandidate> matches = EnumerateDevices()
            .Where(device =>
                device.Id == TargetFtdiId &&
                device.Description.Contains("FT245R", StringComparison.OrdinalIgnoreCase) &&
                device.Description.Contains("USB FIFO", StringComparison.OrdinalIgnoreCase))
            .Select(device => new FtdiCandidate(
                device.Serial,
                device.Description,
                device.Id,
                device.LocationId,
                device.IsOpen))
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                "Không tìm thấy bo FT245R USB FIFO ID 0x04036001. " +
                "Kiểm tra nguồn bo, cáp USB và driver FTDI D2XX.");
        }

        if (!string.IsNullOrWhiteSpace(_serial))
        {
            FtdiCandidate? preferred = matches.FirstOrDefault(x =>
                x.Serial.Equals(_serial, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return RequireAvailable(preferred);
        }

        if (matches.Count > 1)
        {
            string candidates = string.Join(
                ", ",
                matches.Select(item => $"{item.Description} [{item.Serial}] ID=0x{item.Id:X8}"));
            throw new InvalidOperationException(
                "Có nhiều bo FT245R phù hợp nhưng FtdiSerial không xác định đúng một bo. " +
                $"Dừng để tránh mở nhầm thiết bị: {candidates}");
        }

        return RequireAvailable(matches[0]);
    }

    private static FtdiCandidate RequireAvailable(FtdiCandidate candidate)
    {
        if (candidate.IsOpen)
        {
            throw new InvalidOperationException(
                $"Bo FTDI {candidate.Description} [{candidate.Serial}] đang bị phần mềm khác chiếm dụng.");
        }

        return candidate;
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
                    _rxEvent.Reset();
                    Ensure(
                        FT_SetEventNotification(
                            _handle,
                            FT_EVENT_RXCHAR,
                            _rxEvent.SafeWaitHandle.DangerousGetHandle()),
                        "FT_SetEventNotification");
                }, ct);

                _scanPrepared = false;

                // Startup theo trace Htdrv: STOP_SCAN -> ~500 ms -> HANDSHAKE
                // 8A/0F -> INIT_1 -> INIT_2. Nếu handshake fail thì không scan.
                await WriteAsync(CmdStopScan, ct, purgeBeforeWrite: true);
                await Task.Delay(ProductionTimingPolicy.StartupStopToHandshakeMs, ct);
                await HandshakeAsync(ct);
                await Task.Delay(ProductionTimingPolicy.StartupHandshakeToInit1Ms, ct);
                await PrepareScanAsync(ct);
                // V12.4: khi vừa kết nối, relay phải ở trạng thái chờ/không kích.
                // R1 chỉ mở JIG, R2 chỉ MARKING khi workflow yêu cầu.
                await AllRelaysOffAsync(ct);

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
        await Task.Delay(ProductionTimingPolicy.StartupInit1ToInit2Ms, ct);
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

            // START_SCAN bắt đầu vòng stream mới nên purge đúng thời điểm,
            // không purge tùy tiện giữa các frame đang được reader tách.
            await PurgeAsync(ct);
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

        // Canonical production sequence for every physical resistance channel:
        // 90 00 00 01 -> ~350 ms -> 91 00 00 <channel>.
        // RouteA/RouteB remain on ResistanceStep for legacy import only. The
        // production runtime must never derive the selector from those fields.
        byte[] routeA = D2xxResistanceRouting.BuildRouteA();
        byte[] routeB = D2xxResistanceRouting.BuildRouteB(step.Channel);

        await WriteAsync(routeA, ct);
        await Task.Delay(350, ct);
        await WriteAsync(routeB, ct);
        _scanPrepared = false;
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
        1 => WriteRelayAsync([0x8E, 0x00, 0x00, 0x01], "RELAY1", ct),
        2 => WriteRelayAsync([0x8E, 0x00, 0x00, 0x02], "RELAY2", ct),
        _ => throw new ArgumentOutOfRangeException(nameof(relay))
    };

    public Task AllRelaysOffAsync(CancellationToken ct = default) =>
        IsConnected
            ? WriteRelayAsync([0x8E, 0x00, 0x00, 0x00], "ALL_RELAYS_OFF", ct)
            : Task.CompletedTask;

    async Task WriteRelayAsync(byte[] command, string reason, CancellationToken ct)
    {
        await WriteAsync(command, ct);
        _scanPrepared = false;
        Log?.Invoke(this, $"D2XX PREPARE INVALIDATED after {reason}; next START_SCAN will run INIT recovery.");
    }

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
        var buffer = new byte[65536];
        WaitHandle[] receiveWaitHandles = [_rxEvent, ct.WaitHandle];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref _pollCount);

                if (Volatile.Read(ref _controlWaiters) > 0)
                {
                    ct.WaitHandle.WaitOne(ProductionTimingPolicy.D2xxControlWaitSleepMs);
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
                    PublishPerfAggregateIfDue(mode);

                    // Htdrv gốc đăng ký FT_EVENT_RXCHAR và chỉ thức khi driver
                    // báo có dữ liệu. Timeout giữ watchdog/perf metrics hoạt động
                    // ngay cả khi bo im lặng; cancellation luôn đánh thức worker.
                    WaitHandle.WaitAny(receiveWaitHandles, 1000);
                    continue;
                }

                Interlocked.Add(ref _bytesReceived, (long)read);
                PublishProtocolTrace("RX", buffer.AsSpan(0, checked((int)read)));
                long decodeStarted = Stopwatch.GetTimestamp();
                IReadOnlyList<ScanFrame> decodedFrames = decoder.Feed(
                    buffer.AsSpan(0, checked((int)read)));
                Interlocked.Add(ref _decodeTicks, Stopwatch.GetTimestamp() - decodeStarted);

                foreach (ScanFrame decoded in decodedFrames)
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

                PublishPerfAggregateIfDue(mode);
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

    void PublishPerfAggregateIfDue(BoardScanMode mode)
    {
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref _lastPerfAggregateTick);
        if (previous != 0 && now - previous < 5000)
            return;
        if (Interlocked.CompareExchange(ref _lastPerfAggregateTick, now, previous) != previous)
            return;

        long polls = Interlocked.Exchange(ref _pollCount, 0);
        long bytes = Interlocked.Exchange(ref _bytesReceived, 0);
        long frames = Interlocked.Exchange(ref _framesPublished, 0);
        long decodeTicks = Interlocked.Exchange(ref _decodeTicks, 0);
        double intervalSeconds = previous == 0 ? 5.0 : Math.Max(0.001, (now - previous) / 1000.0);
        double decodeMs = decodeTicks <= 0
            ? 0
            : decodeTicks * 1000.0 / Stopwatch.Frequency;

        AsyncFileLogService.Current.Performance(
            "BOARD_METRICS " +
            $"mode={mode} polls_per_sec={polls / intervalSeconds:0.###} " +
            $"frames_per_sec={frames / intervalSeconds:0.###} bytes={bytes} " +
            $"decode_avg_ms={(frames > 0 ? decodeMs / frames : 0):0.###} " +
            $"threads={Process.GetCurrentProcess().Threads.Count} " +
            $"handles={Process.GetCurrentProcess().HandleCount} " +
            $"memory_mb={GC.GetTotalMemory(false) / 1024.0 / 1024.0:0.###}");
    }

    void PublishFrame(ScanFrame decoded)
    {
        Interlocked.Increment(ref _framesPublished);
        Interlocked.Increment(ref _framesReceivedTotal);
        Interlocked.Exchange(ref _lastFrameSequence, decoded.Sequence);
        Interlocked.Exchange(ref _lastFrameTimestampUtcTicks, DateTime.UtcNow.Ticks);
        long now = Environment.TickCount64;
        bool forceLog = decoded.UnknownBytes > 0 ||
                        (decoded.Mode == BoardScanMode.Production && !decoded.Complete);
        bool canLogTransition = now - _lastScanLogTick >= 50;

        // Log DataGrid/ObservableCollection không được phép kéo chậm worker.
        // Chỉ log trạng thái RX tối đa khoảng 20 lần/giây; FrameReceived vẫn
        // phát TẤT CẢ frame cho TestEngine nên logic test không bị giảm tốc.
        if (forceLog || canLogTransition)
        {
            string signature = $"{decoded.Mode}:" + string.Join(",", decoded.ActiveIo.Order());
            bool signatureChanged =
                !string.Equals(signature, _lastScanSignature, StringComparison.Ordinal);

            if (forceLog || signatureChanged)
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
        }

        FrameReceived?.Invoke(this, decoded);
    }

    async Task WriteAsync(
        byte[] data,
        CancellationToken ct,
        bool purgeBeforeWrite = false)
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

                if (purgeBeforeWrite)
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
        PublishProtocolTrace("TX", data);
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

                byte[] received = buffer[..(int)read];
                PublishProtocolTrace("RX", received);
                return received;
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

    void PublishProtocolTrace(string direction, ReadOnlySpan<byte> data)
    {
        EventHandler<D2xxProtocolTrace>? handler = ProtocolTrace;
        if (handler is null || data.IsEmpty)
            return;

        try
        {
            handler.Invoke(
                this,
                new D2xxProtocolTrace(
                    DateTime.UtcNow,
                    Stopwatch.GetTimestamp(),
                    direction,
                    data.ToArray()));
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, $"Protocol trace subscriber error: {ex.Message}");
        }
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
            _rxEvent.Dispose();
        }
    }
}
