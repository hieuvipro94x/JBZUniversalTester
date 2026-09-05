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
    CancellationTokenSource? _readerCts;
    Task? _readerTask;
    readonly object _decoderGate = new();
    readonly BoardIoDecoder _decoder = new();
    int _firmwareScanning;
    string _lastScanSignature = string.Empty;
    string _activeScanConfiguration = string.Empty;
    long _lastScanLogTick;
    bool _scanPrepared;
    // INIT_1/INIT_2 chuẩn bị đường quét theo số scan-unit đang hoạt động.
    // Không được dùng preparation của 1 card để START_SCAN cho 4/10 card.
    BoardCapacity? _preparedScanCapacity;
    BoardCapacity _installedCapacity;
    BoardCapacity _capacity;
    BoardCapacity? _appliedScanCapacity;
    BoardScanCapacity _scanCapacity;
    int _expectedIoCount;
    string _lastCapacityLogSignature = string.Empty;
    byte _appliedLatencyMs;
    int _activeRelay = -1;
    BoardScanMode _scanMode = BoardScanMode.Production;
    long _scanGeneration;
    string _stableFrameSignature = string.Empty;
    int _stableFrameCount;
    bool _firstStableFrameConfirmed;
    int _controlWaiters;
    long _lastPerfAggregateTick;
    long _pollCount;
    long _queueCallCount;
    long _readCallCount;
    long _bytesReceived;
    long _framesPublished;
    long _framesReceivedTotal;
    long _completeFramesReceivedTotal;
    long _lastFrameSequence;
    long _lastCompleteFrameSequence;
    long _lastFrameTimestampUtcTicks;
    int _lastFrameSourceCount;
    int _lastFrameEndMarkerCode = -1;
    int _lastFrameUnknownBytes;
    long _decodeTicks;
    long _openCount;
    long _closeCount;
    long _readerStartCount;
    int _disposeStarted;
    int _disposed;
    int _connectionState = (int)BoardConnectionState.Disconnected;

    // Timing measured from the original Htdrv shutdown trace 2026-08-07 16:25.
    // These delays are only used for FINAL application shutdown, not normal
    // TestView/TestPin switching.
    const int FinalStopToResetMs = 280;
    const int FinalResetToInit1Ms = 170;
    const int FinalInitDelayMs = 350;
    const int FinalInit2ToStopMs = 330;
    const int FinalStopToCloseMs = 130;
    // Htdrv gốc giữ khoảng 100 ms sau lệnh điều khiển từ 0x8D trở lên.
    // FT_Write hoàn tất chỉ xác nhận dữ liệu đã vào driver, không xác nhận firmware
    // đã áp dụng trạng thái relay. Không được gửi RESET/START_SCAN đè ngay sau 0x8E.
    const int RelayCommandSettleMs = 100;

    public bool IsConnected => _handle != IntPtr.Zero;
    public BoardConnectionState ConnectionState => (BoardConnectionState)Volatile.Read(ref _connectionState);
    public bool IsScanning => Volatile.Read(ref _firmwareScanning) != 0;
    public BoardScanMode CurrentScanMode => _scanMode;
    public BoardCapacity InstalledCapacity => _installedCapacity;
    public BoardCapacity Capacity => _capacity;
    public BoardCapacity? AppliedScanCapacity => _appliedScanCapacity;
    public BoardScanCapacity ScanCapacity => _scanCapacity;
    public DateTime LastFrameTimestampUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastFrameTimestampUtcTicks);
            return ticks <= 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc);
        }
    }
    public long LastFrameSequence => Interlocked.Read(ref _lastFrameSequence);
    public long LastCompleteFrameSequence => Interlocked.Read(ref _lastCompleteFrameSequence);
    public long FramesReceived => Interlocked.Read(ref _framesReceivedTotal);
    public long CompleteFramesReceived => Interlocked.Read(ref _completeFramesReceivedTotal);
    public int LastFrameSourceCount => Volatile.Read(ref _lastFrameSourceCount);
    public byte? LastFrameEndMarkerCode
    {
        get
        {
            int code = Volatile.Read(ref _lastFrameEndMarkerCode);
            return code < 0 ? null : checked((byte)code);
        }
    }
    public int LastFrameUnknownBytes => Volatile.Read(ref _lastFrameUnknownBytes);

    public event EventHandler<ScanFrame>? FrameReceived;
    public event EventHandler<string>? Log;
    public event EventHandler<D2xxProtocolTrace>? ProtocolTrace;

    public D2xxBoardTransport(
        string serial,
        ProductionSettings? production = null)
    {
        _serial = serial;
        _production = production ?? new ProductionSettings();
        _scanCapacity = BoardScanCapacity.Create(_production, 0);
        _installedCapacity = _scanCapacity.Installed;
        _capacity = _scanCapacity.Active;
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
                "Có nhiều bo xác định. " +
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
                Volatile.Write(ref _connectionState, (int)BoardConnectionState.Connecting);
                // AppliedScanCapacity chỉ mô tả START_SCAN đã thực sự gửi cho
                // phiên FTDI hiện tại. Không mang state của handle cũ sang reconnect.
                _appliedScanCapacity = null;
                _activeScanConfiguration = string.Empty;
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
                    Interlocked.Increment(ref _openCount);
                    _connectedSerial = candidate.Serial;

                    Ensure(FT_SetBaudRate(_handle, 115200), "FT_SetBaudRate");
                    Ensure(FT_SetDataCharacteristics(_handle, 8, 0, 0), "FT_SetDataCharacteristics");
                    Ensure(FT_SetFlowControl(_handle, 0, 0, 0), "FT_SetFlowControl");
                    Ensure(FT_SetTimeouts(_handle, 50, 150), "FT_SetTimeouts");
                    Ensure(FT_SetUSBParameters(_handle, 65536, 65536), "FT_SetUSBParameters");

                    byte latencyMs = checked((byte)Math.Clamp(_production.UsbDelay, 1, 16));
                    Ensure(FT_SetLatencyTimer(_handle, latencyMs), "FT_SetLatencyTimer");
                    _appliedLatencyMs = latencyMs;
                    Ensure(FT_Purge(_handle, FT_PURGE_RX | FT_PURGE_TX), "FT_Purge");
                    _rxEvent.Reset();
                    Ensure(
                        FT_SetEventNotification(
                            _handle,
                            FT_EVENT_RXCHAR,
                            _rxEvent.SafeWaitHandle.DangerousGetHandle()),
                        "FT_SetEventNotification");
                }, ct);

                Volatile.Write(ref _connectionState, (int)BoardConnectionState.Initializing);

                _scanPrepared = false;
                _preparedScanCapacity = null;
                _activeRelay = -1;

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
                StartPermanentReader();
                Volatile.Write(ref _connectionState, (int)BoardConnectionState.Ready);

                Log?.Invoke(
                    this,
                    $"Đã mở đúng FTDI {candidate.Description} [{candidate.Serial}] " +
                    $"ID 0x{candidate.Id:X8}; sẵn sàng scan.");

                return new BoardConnectionInfo(candidate.Description, candidate.Serial);
            }
            catch
            {
                Volatile.Write(ref _connectionState, (int)BoardConnectionState.Faulted);
                IntPtr handle = _handle;
                _handle = IntPtr.Zero;
                _connectedSerial = string.Empty;
                _scanPrepared = false;
                _preparedScanCapacity = null;
                _activeRelay = -1;
                _appliedScanCapacity = null;
                _activeScanConfiguration = string.Empty;
                await StopPermanentReaderAsync();

                if (handle != IntPtr.Zero)
                {
                    try { FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX); } catch { }
                    try
                    {
                        FT_Close(handle);
                        Interlocked.Increment(ref _closeCount);
                    }
                    catch { }
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
            Volatile.Write(ref _connectionState, (int)BoardConnectionState.ShuttingDown);
            await _scanSwitchLock.WaitAsync();
            try
            {
                bool hadReader = _readerTask is not null;
                await StopScanCoreAsync(CancellationToken.None);

                IntPtr handle = _handle;
                if (handle == IntPtr.Zero)
                {
                    await StopPermanentReaderAsync();
                    return;
                }

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

                await StopPermanentReaderAsync();

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
                            Interlocked.Increment(ref _closeCount);
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
                _preparedScanCapacity = null;
                _appliedScanCapacity = null;
                _activeScanConfiguration = string.Empty;
                Volatile.Write(ref _connectionState, (int)BoardConnectionState.Disconnected);
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
        byte[] rx = await ReadUntilHandshakeAsync(500, ct);

        if (!ContainsHandshake(rx))
            throw new InvalidOperationException(
                $"Handshake không hợp lệ: {Convert.ToHexString(rx)}");

        int handshakeOffset = FindHandshakeOffset(rx);
        if (handshakeOffset > 0)
        {
            Log?.Invoke(
                this,
                $"Handshake đã đồng bộ lại sau {handshakeOffset} byte scan còn lại.");
        }
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
        if (_scanPrepared && HasSameScanRange(_preparedScanCapacity, _capacity))
            return;

        await WriteAsync(CmdInit1, ct);
        await Task.Delay(ProductionTimingPolicy.StartupInit1ToInit2Ms, ct);
        await WriteAsync(CmdInit2, ct);
        _scanPrepared = true;
        _preparedScanCapacity = _capacity;
    }

    public void ConfigureActiveScanRange(int maxIo)
    {
        // Test pointer là chế độ quan sát I/O vật lý. Khi bật, BO phải quét
        // toàn bộ card đã cấu hình, không được co theo MaxIo của THT; nhờ đó
        // có thể dò IO128 trên máy 2 card dù THT chỉ khai báo ví dụ IO1..64.
        // TestEngine vẫn chỉ đánh giá topology trong model nên I/O ngoài THT không thể PASS/FAIL.
        _scanCapacity = BoardScanCapacity.Create(
            _production,
            maxIo,
            scanAllInstalledIo: true);
        _installedCapacity = _scanCapacity.Installed;
        _capacity = _scanCapacity.Active;
        _production.ExpansionCardCount = _installedCapacity.ExpansionCardCount;
        _production.CardCount = _installedCapacity.ScanCardCount;
        _production.StartCardNumber = _installedCapacity.StartCardNumber;
        _expectedIoCount = _capacity.TotalIoCapacity;

        string signature = $"{_scanCapacity}:{_scanCapacity.IsModelWithinInstalledCapacity}";
        if (string.Equals(signature, _lastCapacityLogSignature, StringComparison.Ordinal))
            return;
        _lastCapacityLogSignature = signature;

        Log?.Invoke(
            this,
            $"BOARD_CAPACITY installed={_scanCapacity.InstalledScanUnits} " +
            $"required={_scanCapacity.RequiredScanUnits} active={_scanCapacity.ActiveScanUnits} " +
            $"io={_scanCapacity.ActiveIoCapacity} " +
            $"fit={_scanCapacity.IsModelWithinInstalledCapacity} " +
            $"probe_all_io={_production.UseTestPointer}.");

        if (!_scanCapacity.IsModelWithinInstalledCapacity)
            Log?.Invoke(this, _scanCapacity.CapacityErrorMessage);
    }

    public async Task StartScanAsync(
        BoardScanMode mode = BoardScanMode.Production,
        CancellationToken ct = default)
    {
        await _scanSwitchLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            if (mode == BoardScanMode.Production &&
                !_scanCapacity.IsModelWithinInstalledCapacity)
            {
                throw new InvalidOperationException(_scanCapacity.CapacityErrorMessage);
            }
            string requestedConfiguration = BuildScanConfiguration(mode);
            if (IsScanning &&
                mode == _scanMode &&
                string.Equals(requestedConfiguration, _activeScanConfiguration, StringComparison.Ordinal))
            {
                Log?.Invoke(this, $"START_SCAN REUSED: mode={mode}, configuration={requestedConfiguration}.");
                return;
            }

            bool capacityPreparationChanged =
                !HasSameScanRange(_preparedScanCapacity, _capacity);

            // Chỉ restart khi mode/capacity thật sự đổi hoặc stream không chạy.
            await StopScanCoreAsync(ct);
            await ApplyPendingNativeConfigurationAsync(ct);

            // Trace Htdrv với 4 card khởi tạo BO trước khi gửi 8C 00 04 00.
            // Khi operator đổi product từ dải 1 card sang 4/10 card, INIT của
            // dải cũ không còn hợp lệ: reset sạch và chuẩn bị lại trước scan.
            // Nếu không, BO có thể stream chỉ 64/256 source và UI trông như lag.
            if (capacityPreparationChanged)
            {
                Log?.Invoke(
                    this,
                    $"SCAN_CAPACITY_REPREPARE old={FormatScanRange(_preparedScanCapacity)} " +
                    $"new={FormatScanRange(_capacity)}; STOP->RESET->INIT trước START_SCAN.");
                await ResetClearAsync(ct);
                _scanPrepared = false;
                _preparedScanCapacity = null;
            }

            if (!_scanPrepared || !HasSameScanRange(_preparedScanCapacity, _capacity))
                await PrepareScanAsync(ct);

            _lastScanSignature = string.Empty;
            _stableFrameSignature = string.Empty;
            _stableFrameCount = 0;
            _firstStableFrameConfirmed = false;
            _scanMode = mode;

            lock (_decoderGate)
            {
                _decoder.ConfigureCapacity(_capacity);
                _decoder.ConfigureMode(mode);
                _decoder.Reset();
            }

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
            _appliedScanCapacity = _capacity;
            Log?.Invoke(this, $"START_SCAN parameter={_capacity.StartScanParameter}");

            // QUAN TRỌNG: START_SCAN không làm mất INIT. Giữ prepared=true để
            // STOP -> RESET -> START tiếp theo diễn ra ngay, không chờ INIT 700 ms.
            _scanPrepared = true;
            _preparedScanCapacity = _capacity;

            long generation = Interlocked.Increment(ref _scanGeneration);
            _scanMode = mode;
            Volatile.Write(ref _firmwareScanning, 1);
            Volatile.Write(ref _connectionState, (int)BoardConnectionState.Scanning);
            _activeScanConfiguration = requestedConfiguration;

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
        if (!IsScanning)
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

        Volatile.Write(ref _firmwareScanning, 0);
        if (IsConnected)
            Volatile.Write(ref _connectionState, (int)BoardConnectionState.PausedForHardwareOperation);
        _activeScanConfiguration = string.Empty;
        // Reader remains alive and waits for RX/cancel. STOP only pauses the
        // firmware stream; the next logical mode reuses the same worker.
    }

    public async Task EnterIdleAsync(CancellationToken ct = default)
    {
        // CARD_SYNC_2026-09-05:
        // IDLE không được giả lập rằng capacity hiện tại đã được INIT.
        // Nếu CARD vừa thay đổi, StartScanAsync phải nhìn thấy
        // _preparedScanCapacity cũ và tự RESET->INIT theo capacity mới.
        await _scanSwitchLock.WaitAsync(ct);
        try
        {
            bool hadReader = _readerTask is not null;
            await StopScanCoreAsync(ct);

            if (!IsConnected)
                return;

            if (!hadReader)
                await WriteAsync(CmdStopScan, ct);

            await AllRelaysOffAsync(ct);
            await ResetClearAsync(ct);
            await PurgeAsync(ct);

            // KHÔNG:
            // _scanPrepared = true;
            // _preparedScanCapacity = _capacity;

            Volatile.Write(
                ref _connectionState,
                (int)BoardConnectionState.Ready);

            Log?.Invoke(
                this,
                "Board đã về IDLE sạch, giữ FTDI mở và sẵn sàng START_SCAN lại.");
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
        _preparedScanCapacity = null;
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
        _preparedScanCapacity = _capacity;
    }

    public Task SetRelayAsync(int relay, CancellationToken ct = default) => relay switch
    {
        1 => WriteRelayAsync([0x8E, 0x00, 0x00, 0x01], "RELAY1", 1, ct),
        2 => WriteRelayAsync([0x8E, 0x00, 0x00, 0x02], "RELAY2", 2, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(relay))
    };

    public Task AllRelaysOffAsync(CancellationToken ct = default) =>
        IsConnected
            ? WriteRelayAsync([0x8E, 0x00, 0x00, 0x00], "ALL_RELAYS_OFF", 0, ct)
            : Task.CompletedTask;

    async Task WriteRelayAsync(byte[] command, string reason, int relayState, CancellationToken ct)
    {
        // Lệnh OFF là lệnh an toàn cưỡng bức: luôn ghi lại 00, kể cả khi cache
        // phần mềm đang nghĩ relay đã OFF. Nhờ đó Manual/PASS không phụ thuộc
        // vào trạng thái cache nếu một relay cơ khí vừa nhả chậm hoặc bị nhiễu.
        if (relayState != 0 && Volatile.Read(ref _activeRelay) == relayState)
            return;
        // JBZ I/O Monitor V1.9 purge RX/TX ngay trước mọi frame relay.
        // Manual đã dừng scan nên purge không làm mất frame Production;
        // thao tác này ngăn BO bỏ qua frame OFF 8E 00 00 00 trên một số máy.
        await WriteAsync(command, ct, purgeBeforeWrite: true);
        Volatile.Write(ref _activeRelay, relayState);

        // Đặc biệt quan trọng với ALL OFF: Manual OFF/RESET và relay production
        // phải cho firmware đủ thời gian chốt 00 trước lệnh điều khiển kế tiếp.
        await Task.Delay(RelayCommandSettleMs, ct);

        // 0x8E chỉ điều khiển relay ngoài (JIG/MARKING), không thay đổi
        // routing 0x90/0x91 đã được INIT cho continuity scan. Trace production
        // cho thấy sau relay OFF, Htdrv có thể gửi START_SCAN trực tiếp mà
        // không chạy lại INIT_1/INIT_2. Vì vậy không invalid _scanPrepared
        // tại đây; nếu invalid sẽ làm startup và mỗi chu kỳ relay bị cộng thêm
        // một vòng INIT không cần thiết.
        Log?.Invoke(this, $"D2XX RELAY {reason}; scan prepare preserved.");
    }

    private string BuildScanConfiguration(BoardScanMode mode) =>
        $"{mode}:{_capacity.StartCardNumber}:{_capacity.ExpansionCardCount}:{_capacity.StartScanParameter}";

    private static bool HasSameScanRange(BoardCapacity? left, BoardCapacity right) =>
        left is not null &&
        left.StartScanParameter == right.StartScanParameter &&
        left.StartCardNumber == right.StartCardNumber &&
        left.TotalIoCapacity == right.TotalIoCapacity;

    private static string FormatScanRange(BoardCapacity? capacity) => capacity is null
        ? "none"
        : $"{capacity.StartScanParameter}/{capacity.TotalIoCapacity}";

    private async Task ApplyPendingNativeConfigurationAsync(CancellationToken ct)
    {
        byte requestedLatency = checked((byte)Math.Clamp(_production.UsbDelay, 1, 16));
        if (_appliedLatencyMs == requestedLatency)
            return;

        await _ioLock.WaitAsync(ct);
        try
        {
            IntPtr handle = _handle;
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("Bo JBZ đã đóng kết nối.");
            Ensure(FT_SetLatencyTimer(handle, requestedLatency), "FT_SetLatencyTimer");
            _appliedLatencyMs = requestedLatency;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private void StartPermanentReader()
    {
        if (_readerTask is { IsCompleted: false })
            return;
        _readerCts?.Dispose();
        _readerCts = new CancellationTokenSource();
        Interlocked.Increment(ref _readerStartCount);
        _readerTask = ScanLoopAsync(_readerCts.Token);
    }

    private async Task StopPermanentReaderAsync()
    {
        CancellationTokenSource? cts = _readerCts;
        Task? task = _readerTask;
        if (cts is null && task is null)
            return;
        cts?.Cancel();
        _rxEvent.Set();
        if (task is not null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
        _readerTask = null;
        _readerCts = null;
        cts?.Dispose();
        Volatile.Write(ref _firmwareScanning, 0);
    }

    Task ScanLoopAsync(CancellationToken ct)
    {
        return Task.Factory.StartNew(
            () => ScanLoopWorker(ct),
            ct,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    void ScanLoopWorker(CancellationToken ct)
    {
        var buffer = new byte[65536];
        WaitHandle[] receiveWaitHandles = [_rxEvent, ct.WaitHandle];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                Interlocked.Increment(ref _pollCount);
                // Chụp generation trước khi kiểm tra control waiter. Nếu một
                // STOP/START bắt đầu ngay sau đây, buffer đang đọc vẫn mang
                // generation cũ và bị loại trước khi publish.
                long readGeneration = Volatile.Read(ref _scanGeneration);

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
                    Interlocked.Increment(ref _queueCallCount);
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
                        Interlocked.Increment(ref _readCallCount);

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
                    PublishPerfAggregateIfDue(_scanMode);

                    // Htdrv gốc đăng ký FT_EVENT_RXCHAR và chỉ thức khi driver
                    // báo có dữ liệu. Timeout giữ watchdog/perf metrics hoạt động
                    // ngay cả khi bo im lặng; cancellation luôn đánh thức worker.
                    WaitHandle.WaitAny(receiveWaitHandles, 1000);
                    continue;
                }

                Interlocked.Add(ref _bytesReceived, (long)read);
                PublishProtocolTrace("RX", buffer.AsSpan(0, checked((int)read)));
                long decodeStarted = Stopwatch.GetTimestamp();
                IReadOnlyList<ScanFrame> decodedFrames;
                lock (_decoderGate)
                {
                    decodedFrames = _decoder.Feed(
                        buffer.AsSpan(0, checked((int)read)));
                }
                Interlocked.Add(ref _decodeTicks, Stopwatch.GetTimestamp() - decodeStarted);

                foreach (ScanFrame decoded in decodedFrames)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    if (!IsScanning ||
                        decoded.Mode != _scanMode ||
                        readGeneration != Volatile.Read(ref _scanGeneration))
                    {
                        continue;
                    }

                    ScanFrame sessionFrame = decoded with { ScanGeneration = readGeneration };
                    if (!ShouldPublishConfirmedFrame(sessionFrame))
                        continue;
                    PublishFrame(sessionFrame);
                }

                PublishPerfAggregateIfDue(_scanMode);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Volatile.Write(ref _firmwareScanning, 0);
            Volatile.Write(ref _connectionState, (int)BoardConnectionState.Faulted);
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
                _preparedScanCapacity = null;
                if (handle != IntPtr.Zero)
                {
                    try { FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX); } catch { }
                    try
                    {
                        FT_Close(handle);
                        Interlocked.Increment(ref _closeCount);
                    }
                    catch { }
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
        long queueCalls = Interlocked.Exchange(ref _queueCallCount, 0);
        long reads = Interlocked.Exchange(ref _readCallCount, 0);
        long bytes = Interlocked.Exchange(ref _bytesReceived, 0);
        long frames = Interlocked.Exchange(ref _framesPublished, 0);
        long decodeTicks = Interlocked.Exchange(ref _decodeTicks, 0);
        double intervalSeconds = previous == 0 ? 5.0 : Math.Max(0.001, (now - previous) / 1000.0);
        double decodeMs = decodeTicks <= 0
            ? 0
            : decodeTicks * 1000.0 / Stopwatch.Frequency;

        using Process process = Process.GetCurrentProcess();
        AsyncFileLogService.Current.Performance(
            "BOARD_METRICS " +
            $"mode={mode} polls_per_sec={polls / intervalSeconds:0.###} " +
            $"queue_calls_per_sec={queueCalls / intervalSeconds:0.###} " +
            $"reads_per_sec={reads / intervalSeconds:0.###} " +
            $"frames_per_sec={frames / intervalSeconds:0.###} bytes={bytes} " +
            $"decode_avg_ms={(frames > 0 ? decodeMs / frames : 0):0.###} " +
            $"opens={Interlocked.Read(ref _openCount)} closes={Interlocked.Read(ref _closeCount)} " +
            $"reader_starts={Interlocked.Read(ref _readerStartCount)} reader_active={(_readerTask is { IsCompleted: false } ? 1 : 0)} " +
            $"threads={process.Threads.Count} handles={process.HandleCount} " +
            $"private_mb={process.PrivateMemorySize64 / 1048576d:0.###} " +
            $"memory_mb={GC.GetTotalMemory(false) / 1024.0 / 1024.0:0.###}");
    }

    void PublishFrame(ScanFrame decoded)
    {
        Interlocked.Increment(ref _framesPublished);
        Interlocked.Increment(ref _framesReceivedTotal);
        Interlocked.Exchange(ref _lastFrameSequence, decoded.Sequence);
        if (decoded.Mode == BoardScanMode.Production &&
            decoded.Complete &&
            decoded.UnknownBytes == 0 &&
            decoded.TerminatorKnown)
        {
            Interlocked.Exchange(ref _lastCompleteFrameSequence, decoded.Sequence);
            Interlocked.Increment(ref _completeFramesReceivedTotal);
        }
        Interlocked.Exchange(ref _lastFrameTimestampUtcTicks, DateTime.UtcNow.Ticks);
        Volatile.Write(ref _lastFrameSourceCount, decoded.SourceCount);
        Volatile.Write(
            ref _lastFrameEndMarkerCode,
            decoded.EndMarkerCode is byte endMarkerCode ? endMarkerCode : -1);
        Volatile.Write(ref _lastFrameUnknownBytes, decoded.UnknownBytes);
        long now = Environment.TickCount64;
        bool unhealthyFrame = decoded.UnknownBytes > 0 ||
                              !decoded.TerminatorKnown ||
                              (decoded.Mode == BoardScanMode.Production && !decoded.Complete);
        // Khung thiếu có thể đến 10+ lần/giây. Ghi từng khung làm nghẽn UI log
        // đúng lúc BO chưa đồng bộ; giữ diagnostic nhưng chỉ một lần/giây.
        long logIntervalMs = unhealthyFrame ? 1_000 : 50;
        bool canLogTransition = now - _lastScanLogTick >= logIntervalMs;

        // Log DataGrid/ObservableCollection không được phép kéo chậm worker.
        // Chỉ log trạng thái RX tối đa khoảng 20 lần/giây; FrameReceived vẫn
        // phát TẤT CẢ frame cho TestEngine nên logic test không bị giảm tốc.
        if (canLogTransition)
        {
            string signature = $"{decoded.Mode}:" + string.Join(",", decoded.ActiveIo.Order());
            bool signatureChanged =
                !string.Equals(signature, _lastScanSignature, StringComparison.Ordinal);

            if (unhealthyFrame || signatureChanged)
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
                        ? $"frame production hoàn chỉnh, {decoded.SourceCount} source"
                        : $"BOARD_FRAME_INCOMPLETE sources={decoded.SourceCount}/{decoded.ExpectedIoCount}";

                string sync = decoded.UnknownBytes > 0
                    ? $", bỏ {decoded.UnknownBytes} byte mất đồng bộ"
                    : string.Empty;

                Log?.Invoke(
                    this,
                    $"RX frame #{decoded.Sequence}: {ioText} [{quality}{sync}] " +
                    $"end=C0 {(decoded.EndMarkerCode ?? 0):X2} known={decoded.TerminatorKnown}");

                if (!decoded.TerminatorKnown)
                    Log?.Invoke(this,
                        $"BOARD_PROTOCOL_UNKNOWN_TERMINATOR code={decoded.EndMarkerCode:X2} sourceCount={decoded.SourceCount}");
            }
        }

        FrameReceived?.Invoke(this, decoded);
    }

    private bool ShouldPublishConfirmedFrame(ScanFrame frame)
    {
        if (frame.Mode != BoardScanMode.Production || !frame.Complete || frame.UnknownBytes != 0)
            return true;

        string signature = BuildStableFrameSignature(frame);
        if (!string.Equals(signature, _stableFrameSignature, StringComparison.Ordinal))
        {
            _stableFrameSignature = signature;
            _stableFrameCount = 1;
        }
        else
        {
            _stableFrameCount++;
        }

        int configured = _firstStableFrameConfirmed
            ? _production.IoConfirmN
            : _production.IoConfirm1;
        int required = Math.Max(1, configured);
        if (_stableFrameCount < required)
            return false;

        if (!_firstStableFrameConfirmed)
        {
            _firstStableFrameConfirmed = true;
            AsyncFileLogService.Current.Performance(
                $"IO_CONFIRM_READY generation={frame.ScanGeneration} required={required} " +
                $"start_card={_capacity.StartCardNumber} scan_through={_capacity.StartScanParameter}");
        }
        return true;
    }

    private static string BuildStableFrameSignature(ScanFrame frame)
    {
        string activeIo = string.Join(',', frame.ActiveIo.Order());
        string connections = string.Join(
            ";",
            frame.Connections
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}:{string.Join(',', pair.Value.Order())}"));
        string targetHits = string.Join(
            ",",
            frame.TargetHits
                .OrderBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}:{pair.Value}"));
        return $"{frame.ExpectedIoCount}|{frame.SourceCount}|{frame.EndMarkerCode}|" +
               $"{activeIo}|{connections}|{targetHits}";
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

    async Task<byte[]> ReadUntilHandshakeAsync(int timeoutMs, CancellationToken ct)
    {
        // Sau reconnect, một số firmware còn đẩy phần cuối frame scan dù STOP
        // đã được gửi. Không kết luận handshake sai ngay ở hai byte đầu; tiếp
        // tục đọc trong chính timeout hiện có cho tới phản hồi 0F 00.
        var result = new List<byte>(64);
        long until = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < until)
        {
            byte[] available = await ReadAvailableAsync(ct);
            if (available.Length == 0)
            {
                await Task.Delay(1, ct);
                continue;
            }

            result.AddRange(available);
            if (ContainsHandshake(result))
                break;
        }

        return result.ToArray();
    }

    private static bool ContainsHandshake(IReadOnlyList<byte> bytes) =>
        FindHandshakeOffset(bytes) >= 0;

    private static int FindHandshakeOffset(IReadOnlyList<byte> bytes)
    {
        for (int index = 0; index + 1 < bytes.Count; index++)
        {
            if (bytes[index] == 0x0F && bytes[index + 1] == 0x00)
                return index;
        }

        return -1;
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
