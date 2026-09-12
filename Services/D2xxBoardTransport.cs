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
    // Htdrv sends this model/scan context separator after RESET when the
    // active scan range is rebuilt during a product change. Keep it distinct
    // from RESET so the protocol trace remains faithful without changing the
    // decoder semantics.
    static readonly byte[] CmdModelContext = [0x9A, 0x01, 0x00, 0x00];

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
    int _activeRelay = -1;
    BoardScanMode _scanMode = BoardScanMode.Production;
    long _scanGeneration;
    ScanFrame? _stableFrameSnapshot;
    int _stableFrameCount;
    bool _firstStableFrameConfirmed;
    int _controlWaiters;
    long _lastPerfAggregateTick;
    long _pollCount;
    long _queueCallCount;
    long _zeroQueueCount;
    long _readCallCount;
    long _bytesReceived;
    long _framesPublished;
    long _completeFramesPublished;
    long _partialFramesReceived;
    long _parserErrorBytes;
    long _invalidFramesReceived;
    long _framesDropped;
    long _d2xxErrorCount;
    long _probePreviewsPublished;
    long _framesReceivedTotal;
    long _completeFramesReceivedTotal;
    long _lastFrameSequence;
    long _lastCompleteFrameSequence;
    long _lastFrameTimestampUtcTicks;
    int _lastFrameSourceCount;
    int _lastFrameEndMarkerCode = -1;
    int _lastFrameUnknownBytes;
    long _decodeTicks;
    const int FrameIntervalSampleCapacity = 256;
    readonly long[] _frameIntervalTicks = new long[FrameIntervalSampleCapacity];
    int _frameIntervalSampleCount;
    int _frameIntervalSampleWriteIndex;
    long _lastCompleteFrameStopwatchTimestamp;
    long _lastFrameIntervalGeneration = -1;
    long _lastProcessCpuTicks;
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
    // Subscriber code belongs to UI/TestEngine, not the FTDI transport. A slow or
    // throwing subscriber must never be mistaken for a USB/D2XX failure.
    const int SlowFrameSubscriberMs = 50;
    const int SlowLogSubscriberMs = 100;

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
    public event EventHandler<ProductionProbePreview>? ProductionProbePreviewReceived;
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

    void EnsureIo(uint status, string api)
    {
        if (status == FT_OK)
            return;

        Interlocked.Increment(ref _d2xxErrorCount);
        SafeDiagnostic(
            $"D2XX_IO_ERROR api={api} status={status} name={GetStatusName(status)} " +
            $"state={ConnectionState} scanning={IsScanning} handle_open={_handle != IntPtr.Zero} " +
            $"generation={Volatile.Read(ref _scanGeneration)}");
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
                // phiên FTDI hiện tại. Không mang state giữa hai lần chạy ứng dụng.
                _appliedScanCapacity = null;
                _activeScanConfiguration = string.Empty;
                FtdiCandidate candidate = await Task.Run(FindTargetBoard, ct);

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

                SafeLog(
                    $"Đã mở đúng FTDI {candidate.Description} [{candidate.Serial}] " +
                    $"ID 0x{candidate.Id:X8}; sẵn sàng scan.");

                return new BoardConnectionInfo(candidate.Description, candidate.Serial);
            }
            catch (Exception ex)
            {
                SafeDiagnostic(
                    $"D2XX_CONNECT_FAULT type={ex.GetType().Name} message={SanitizeDiagnostic(ex.Message)} " +
                    $"handle_open={_handle != IntPtr.Zero}");
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
                    SafeLog($"Relay OFF khi thoát: {ex.Message}");
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
                    SafeLog($"Cleanup board trước FT_Close chưa hoàn chỉnh: {ex.Message}");
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
                                SafeLog($"FT_Close trả {closeStatus} ({GetStatusName(closeStatus)}).");
                        }
                        catch (Exception ex)
                        {
                            SafeLog($"FT_Close lỗi: {ex.Message}");
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
            SafeLog(
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

        SafeLog(
            $"BOARD_CAPACITY installed={_scanCapacity.InstalledScanUnits} " +
            $"required={_scanCapacity.RequiredScanUnits} active={_scanCapacity.ActiveScanUnits} " +
            $"io={_scanCapacity.ActiveIoCapacity} " +
            $"fit={_scanCapacity.IsModelWithinInstalledCapacity} " +
            $"probe_all_io={_production.UseTestPointer}.");

        if (!_scanCapacity.IsModelWithinInstalledCapacity)
            SafeLog(_scanCapacity.CapacityErrorMessage);
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
                SafeLog($"START_SCAN REUSED: mode={mode}, configuration={requestedConfiguration}.");
                return;
            }

            bool capacityPreparationChanged =
                !HasSameScanRange(_preparedScanCapacity, _capacity);

            // Chỉ restart khi mode/capacity thật sự đổi hoặc stream không chạy.
            await StopScanCoreAsync(ct);
            // Trace Htdrv với 4 card khởi tạo BO trước khi gửi 8C 00 04 00.
            // Khi operator đổi product từ dải 1 card sang 4/10 card, INIT của
            // dải cũ không còn hợp lệ: reset sạch và chuẩn bị lại trước scan.
            // Nếu không, BO có thể stream chỉ 64/256 source và UI trông như lag.
            if (capacityPreparationChanged)
            {
                SafeLog(
                    $"SCAN_CAPACITY_REPREPARE old={FormatScanRange(_preparedScanCapacity)} " +
                    $"new={FormatScanRange(_capacity)}; STOP->RESET->INIT trước START_SCAN.");
                await ResetClearAsync(ct);
                await WriteAsync(CmdModelContext, ct);
                _scanPrepared = false;
                _preparedScanCapacity = null;
            }

            if (!_scanPrepared || !HasSameScanRange(_preparedScanCapacity, _capacity))
                await PrepareScanAsync(ct);

            _lastScanSignature = string.Empty;
            _stableFrameSnapshot = null;
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
            SafeLog($"START_SCAN parameter={_capacity.StartScanParameter}");

            // QUAN TRỌNG: START_SCAN không làm mất INIT. Giữ prepared=true để
            // STOP -> RESET -> START tiếp theo diễn ra ngay, không chờ INIT 700 ms.
            _scanPrepared = true;
            _preparedScanCapacity = _capacity;

            long generation = Interlocked.Increment(ref _scanGeneration);
            _scanMode = mode;
            Volatile.Write(ref _firmwareScanning, 1);
            Volatile.Write(ref _connectionState, (int)BoardConnectionState.Scanning);
            _activeScanConfiguration = requestedConfiguration;

            SafeLog($"SCAN MODE = {mode}; generation={generation}.");
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
                SafeLog($"STOP_SCAN báo lỗi: {ex.Message}");
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

            SafeLog(
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

        // Sau khi FT_Write đã thành công, không cho cancellation cắt ngang khoảng
        // settle bắt buộc. Nếu cập nhật cache trước rồi bị cancel, lần gọi sau có
        // thể bỏ qua lệnh dù firmware chưa kịp chốt relay.
        await Task.Delay(RelayCommandSettleMs, CancellationToken.None);
        Volatile.Write(ref _activeRelay, relayState);

        // 0x8E chỉ điều khiển relay ngoài (JIG/MARKING), không thay đổi
        // routing 0x90/0x91 đã được INIT cho continuity scan. Trace production
        // cho thấy sau relay OFF, Htdrv có thể gửi START_SCAN trực tiếp mà
        // không chạy lại INIT_1/INIT_2. Vì vậy không invalid _scanPrepared
        // tại đây; nếu invalid sẽ làm startup và mỗi chu kỳ relay bị cộng thêm
        // một vòng INIT không cần thiết.
        SafeLog($"D2XX RELAY {reason}; scan prepare preserved.");
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
            catch (Exception ex)
            {
                SafeDiagnostic(
                    $"D2XX_READER_STOP_ERROR type={ex.GetType().Name} message={SanitizeDiagnostic(ex.Message)}");
            }
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
        bool waitForRxNotification = true;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (waitForRxNotification)
                {
                    PublishPerfAggregateIfDue(_scanMode);

                    // Event-first receive: do not spend one empty queue call after
                    // every successful read. A timeout is only a watchdog fallback;
                    // it still checks the queue once in case a driver notification
                    // was missed, without creating a millisecond polling loop.
                    int waitResult = WaitHandle.WaitAny(receiveWaitHandles, 1000);
                    if (waitResult == 1 || ct.IsCancellationRequested)
                        break;
                }

                Interlocked.Increment(ref _pollCount);
                // Chụp generation trước khi kiểm tra control waiter. Nếu một
                // STOP/START bắt đầu ngay sau đây, buffer đang đọc vẫn mang
                // generation cũ và bị loại trước khi publish.
                long readGeneration = Volatile.Read(ref _scanGeneration);

                if (Volatile.Read(ref _controlWaiters) > 0)
                {
                    do
                    {
                        ct.WaitHandle.WaitOne(ProductionTimingPolicy.D2xxControlWaitSleepMs);
                    }
                    while (Volatile.Read(ref _controlWaiters) > 0 && !ct.IsCancellationRequested);

                    // No bytes were read before yielding to the command. Purge and
                    // START may now have established a new generation, so attribute
                    // the next fresh chunk to that generation instead of dropping it.
                    readGeneration = Volatile.Read(ref _scanGeneration);
                }
                ct.ThrowIfCancellationRequested();

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

                        EnsureIo(queueStatus, "FT_GetQueueStatus");
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

                            EnsureIo(readStatus, "FT_Read");
                        }
                    }
                }
                finally
                {
                    _ioLock.Release();
                }

                if (queued == 0 || read == 0)
                {
                    if (queued == 0)
                        Interlocked.Increment(ref _zeroQueueCount);

                    PublishPerfAggregateIfDue(_scanMode);
                    waitForRxNotification = true;
                    continue;
                }

                // Usually queued == read because the reusable buffer is 64 KiB.
                // If the driver returned less, drain the known remainder directly
                // instead of waiting for another notification.
                waitForRxNotification = read >= queued;

                Interlocked.Add(ref _bytesReceived, (long)read);
                PublishProtocolTrace("RX", buffer.AsSpan(0, checked((int)read)));
                long decodeStarted = Stopwatch.GetTimestamp();
                IReadOnlyList<ScanFrame> decodedFrames;
                IReadOnlyList<ProductionProbePreview> probePreviews;
                try
                {
                    lock (_decoderGate)
                    {
                        decodedFrames = _decoder.Feed(
                            buffer.AsSpan(0, checked((int)read)));
                        probePreviews = _decoder.DrainProductionProbePreviews();
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Interlocked.Increment(ref _invalidFramesReceived);
                    SafeDiagnostic(
                        $"D2XX_DECODER_ERROR type={ex.GetType().Name} " +
                        $"message={SanitizeDiagnostic(ex.Message)} bytes={read} " +
                        $"generation={readGeneration}; transport_preserved=true");
                    try
                    {
                        lock (_decoderGate)
                            _decoder.Reset();
                    }
                    catch (Exception resetEx)
                    {
                        SafeDiagnostic(
                            $"D2XX_DECODER_RESET_ERROR type={resetEx.GetType().Name} " +
                            $"message={SanitizeDiagnostic(resetEx.Message)}");
                    }

                    PublishPerfAggregateIfDue(_scanMode);
                    waitForRxNotification = true;
                    continue;
                }
                finally
                {
                    Interlocked.Add(ref _decodeTicks, Stopwatch.GetTimestamp() - decodeStarted);
                }

                foreach (ProductionProbePreview preview in probePreviews)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    if (!IsScanning ||
                        _scanMode != BoardScanMode.Production ||
                        readGeneration != Volatile.Read(ref _scanGeneration))
                    {
                        continue;
                    }

                    Interlocked.Increment(ref _probePreviewsPublished);
                    SafePublishProbePreview(
                        preview with { ScanGeneration = readGeneration });
                }

                foreach (ScanFrame decoded in decodedFrames)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    try
                    {
                        if (!IsScanning ||
                            decoded.Mode != _scanMode ||
                            readGeneration != Volatile.Read(ref _scanGeneration))
                        {
                            Interlocked.Increment(ref _framesDropped);
                            continue;
                        }

                        ScanFrame sessionFrame = decoded with { ScanGeneration = readGeneration };
                        if (IsContinuityPreviewFrame(sessionFrame))
                        {
                            // Presentation-only preview must not touch watchdog/frame metrics,
                            // LastFrameSequence, stable-frame confirmation or protocol logs.
                            SafePublishFrame(sessionFrame, "continuity-preview");
                            continue;
                        }

                        if (!ShouldPublishConfirmedFrame(sessionFrame))
                            continue;
                        PublishFrame(sessionFrame);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Interlocked.Increment(ref _invalidFramesReceived);
                        SafeDiagnostic(
                            $"D2XX_FRAME_PROCESSING_ERROR type={ex.GetType().Name} " +
                            $"message={SanitizeDiagnostic(ex.Message)} seq={decoded.Sequence} " +
                            $"generation={readGeneration}; transport_preserved=true");
                    }
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
            SafeDiagnostic(
                $"D2XX_READER_FAULT type={ex.GetType().Name} " +
                $"message={SanitizeDiagnostic(ex.Message)} generation={Volatile.Read(ref _scanGeneration)} " +
                $"mode={_scanMode} handle_open={_handle != IntPtr.Zero} " +
                $"last_frame={Interlocked.Read(ref _lastFrameSequence)} " +
                $"last_complete={Interlocked.Read(ref _lastCompleteFrameSequence)}");
            SafeLog($"Luồng quét FTDI dừng do lỗi: {ex.Message}");

            // Nếu driver/USB rơi giữa lúc quét, không giữ một handle giả
            // IsConnected=true. Đóng handle dưới cùng D2XX lock; ViewModel sẽ
            // khóa phiên và yêu cầu operator khởi động lại ứng dụng.
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
        try
        {
            PublishPerfAggregateCore(mode);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SafeDiagnostic(
                $"D2XX_PERF_DIAGNOSTIC_ERROR type={ex.GetType().Name} message={SanitizeDiagnostic(ex.Message)}");
        }
    }

    void PublishPerfAggregateCore(BoardScanMode mode)
    {
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref _lastPerfAggregateTick);
        if (previous != 0 && now - previous < 5000)
            return;
        if (Interlocked.CompareExchange(ref _lastPerfAggregateTick, now, previous) != previous)
            return;

        long polls = Interlocked.Exchange(ref _pollCount, 0);
        long queueCalls = Interlocked.Exchange(ref _queueCallCount, 0);
        long zeroQueueCalls = Interlocked.Exchange(ref _zeroQueueCount, 0);
        long reads = Interlocked.Exchange(ref _readCallCount, 0);
        long bytes = Interlocked.Exchange(ref _bytesReceived, 0);
        long frames = Interlocked.Exchange(ref _framesPublished, 0);
        long completeFrames = Interlocked.Exchange(ref _completeFramesPublished, 0);
        long partialFrames = Interlocked.Exchange(ref _partialFramesReceived, 0);
        long parserErrorBytes = Interlocked.Exchange(ref _parserErrorBytes, 0);
        long invalidFrames = Interlocked.Exchange(ref _invalidFramesReceived, 0);
        long droppedFrames = Interlocked.Exchange(ref _framesDropped, 0);
        long probePreviews = Interlocked.Exchange(ref _probePreviewsPublished, 0);
        long decodeTicks = Interlocked.Exchange(ref _decodeTicks, 0);
        double intervalSeconds = previous == 0 ? 5.0 : Math.Max(0.001, (now - previous) / 1000.0);
        double decodeMs = decodeTicks <= 0
            ? 0
            : decodeTicks * 1000.0 / Stopwatch.Frequency;
        (double intervalAvgMs, double intervalMedianMs, double intervalP95Ms, double intervalP99Ms) =
            TakeFrameIntervalStatistics();

        using Process process = Process.GetCurrentProcess();
        long processCpuTicks = process.TotalProcessorTime.Ticks;
        long previousCpuTicks = Interlocked.Exchange(ref _lastProcessCpuTicks, processCpuTicks);
        double processCpuPercent = previousCpuTicks <= 0
            ? 0
            : Math.Max(0, processCpuTicks - previousCpuTicks) /
              (intervalSeconds * TimeSpan.TicksPerSecond * Environment.ProcessorCount) * 100.0;
        SafeDiagnostic(
            "BOARD_METRICS " +
            $"mode={mode} polls_per_sec={polls / intervalSeconds:0.###} " +
            $"queue_calls_per_sec={queueCalls / intervalSeconds:0.###} " +
            $"zero_queue={zeroQueueCalls} reads_per_sec={reads / intervalSeconds:0.###} " +
            $"queue_calls_per_frame={(frames > 0 ? queueCalls / (double)frames : 0):0.###} " +
            $"reads_per_frame={(frames > 0 ? reads / (double)frames : 0):0.###} " +
            $"avg_bytes_per_read={(reads > 0 ? bytes / (double)reads : 0):0.###} " +
            $"frames_per_sec={frames / intervalSeconds:0.###} complete_frames={completeFrames} " +
            $"frame_interval_avg_ms={intervalAvgMs:0.###} frame_interval_median_ms={intervalMedianMs:0.###} " +
            $"frame_interval_p95_ms={intervalP95Ms:0.###} frame_interval_p99_ms={intervalP99Ms:0.###} " +
            $"partial_frames={partialFrames} parser_error_bytes={parserErrorBytes} " +
            $"invalid_frames={invalidFrames} dropped_frames={droppedFrames} " +
            $"probe_previews={probePreviews} bytes={bytes} " +
            $"decode_avg_ms={(frames > 0 ? decodeMs / frames : 0):0.###} " +
            $"opens={Interlocked.Read(ref _openCount)} closes={Interlocked.Read(ref _closeCount)} " +
            $"reconnects={Math.Max(0, Interlocked.Read(ref _openCount) - 1)} " +
            $"d2xx_errors={Interlocked.Read(ref _d2xxErrorCount)} process_cpu_pct={processCpuPercent:0.###} " +
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
        bool validCompleteProductionFrame = decoded.Mode == BoardScanMode.Production &&
                                            decoded.Complete &&
                                            decoded.UnknownBytes == 0 &&
                                            decoded.TerminatorKnown;
        if (validCompleteProductionFrame)
        {
            Interlocked.Increment(ref _completeFramesPublished);
            Interlocked.Exchange(ref _lastCompleteFrameSequence, decoded.Sequence);
            Interlocked.Increment(ref _completeFramesReceivedTotal);
            RecordCompleteFrameInterval(decoded.ScanGeneration);
        }
        else if (decoded.Mode == BoardScanMode.Production)
        {
            Interlocked.Increment(ref _partialFramesReceived);
            if (decoded.UnknownBytes > 0)
                Interlocked.Add(ref _parserErrorBytes, decoded.UnknownBytes);
            if (decoded.UnknownBytes > 0 || !decoded.TerminatorKnown)
                Interlocked.Increment(ref _invalidFramesReceived);
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

                SafeLog(
                    $"RX frame #{decoded.Sequence}: {ioText} [{quality}{sync}] " +
                    $"end=C0 {(decoded.EndMarkerCode ?? 0):X2} known={decoded.TerminatorKnown}");

                if (!decoded.TerminatorKnown)
                    SafeLog(
                        $"BOARD_PROTOCOL_UNKNOWN_TERMINATOR code={decoded.EndMarkerCode:X2} sourceCount={decoded.SourceCount}");
            }
        }

        SafePublishFrame(decoded, "confirmed-frame");
    }

    private void RecordCompleteFrameInterval(long generation)
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastFrameIntervalGeneration != generation)
        {
            _lastFrameIntervalGeneration = generation;
            _lastCompleteFrameStopwatchTimestamp = now;
            _frameIntervalSampleCount = 0;
            _frameIntervalSampleWriteIndex = 0;
            return;
        }

        long previous = _lastCompleteFrameStopwatchTimestamp;
        _lastCompleteFrameStopwatchTimestamp = now;
        if (previous <= 0 || now <= previous)
            return;

        _frameIntervalTicks[_frameIntervalSampleWriteIndex] = now - previous;
        _frameIntervalSampleWriteIndex =
            (_frameIntervalSampleWriteIndex + 1) % FrameIntervalSampleCapacity;
        if (_frameIntervalSampleCount < FrameIntervalSampleCapacity)
            _frameIntervalSampleCount++;
    }

    private (double AverageMs, double MedianMs, double P95Ms, double P99Ms)
        TakeFrameIntervalStatistics()
    {
        int count = _frameIntervalSampleCount;
        if (count == 0)
            return (0, 0, 0, 0);

        var samples = new long[count];
        if (count < FrameIntervalSampleCapacity)
        {
            Array.Copy(_frameIntervalTicks, samples, count);
        }
        else
        {
            int tail = FrameIntervalSampleCapacity - _frameIntervalSampleWriteIndex;
            Array.Copy(_frameIntervalTicks, _frameIntervalSampleWriteIndex, samples, 0, tail);
            Array.Copy(_frameIntervalTicks, 0, samples, tail, _frameIntervalSampleWriteIndex);
        }

        _frameIntervalSampleCount = 0;
        _frameIntervalSampleWriteIndex = 0;
        Array.Sort(samples);

        double averageTicks = 0;
        foreach (long sample in samples)
            averageTicks += sample;
        averageTicks /= count;

        return (
            TicksToMilliseconds(averageTicks),
            TicksToMilliseconds(Percentile(samples, 0.50)),
            TicksToMilliseconds(Percentile(samples, 0.95)),
            TicksToMilliseconds(Percentile(samples, 0.99)));
    }

    private static long Percentile(long[] sortedSamples, double percentile)
    {
        int index = Math.Clamp(
            (int)Math.Ceiling(sortedSamples.Length * percentile) - 1,
            0,
            sortedSamples.Length - 1);
        return sortedSamples[index];
    }

    private static double TicksToMilliseconds(double ticks) =>
        ticks * 1000.0 / Stopwatch.Frequency;

    private static bool IsContinuityPreviewFrame(ScanFrame frame) =>
        frame.Mode == BoardScanMode.Production &&
        !frame.Complete &&
        frame.UnknownBytes == 0 &&
        frame.SourceCount == 1 &&
        frame.Connections.Count == 1 &&
        frame.EndMarkerCode is null &&
        !frame.TerminatorKnown;

    private bool ShouldPublishConfirmedFrame(ScanFrame frame)
    {
        if (frame.Mode != BoardScanMode.Production || !frame.Complete || frame.UnknownBytes != 0)
            return true;

        int configured = _firstStableFrameConfirmed
            ? _production.IoConfirmN
            : _production.IoConfirm1;
        int required = Math.Max(1, configured);

        // The production default is one frame. Preserve the latest exact
        // snapshot in case settings change, but do not compare hundreds of IO
        // entries when no repeated-frame confirmation was requested.
        if (required == 1)
        {
            _stableFrameSnapshot = frame;
            _stableFrameCount = 1;
            ConfirmFirstStableFrameIfNeeded(frame, required);
            return true;
        }

        if (_stableFrameSnapshot is null ||
            !HasSameStableFrameState(_stableFrameSnapshot, frame))
        {
            // ScanFrame owns detached collections created by BoardIoDecoder,
            // so retaining the last snapshot is safe. Compare the exact state
            // instead of sorting and serializing hundreds of IO entries into a
            // temporary string for every complete frame.
            _stableFrameSnapshot = frame;
            _stableFrameCount = 1;
        }
        else
        {
            _stableFrameCount++;
        }

        if (_stableFrameCount < required)
            return false;

        ConfirmFirstStableFrameIfNeeded(frame, required);
        return true;
    }

    private void ConfirmFirstStableFrameIfNeeded(ScanFrame frame, int required)
    {
        if (_firstStableFrameConfirmed)
            return;

        _firstStableFrameConfirmed = true;
        SafeDiagnostic(
            $"IO_CONFIRM_READY generation={frame.ScanGeneration} required={required} " +
            $"start_card={_capacity.StartCardNumber} scan_through={_capacity.StartScanParameter}");
    }

    private static bool HasSameStableFrameState(ScanFrame previous, ScanFrame current)
    {
        if (previous.ExpectedIoCount != current.ExpectedIoCount ||
            previous.SourceCount != current.SourceCount ||
            previous.EndMarkerCode != current.EndMarkerCode ||
            !previous.ActiveIo.SetEquals(current.ActiveIo) ||
            previous.Connections.Count != current.Connections.Count ||
            previous.TargetHits.Count != current.TargetHits.Count)
        {
            return false;
        }

        foreach ((int source, IReadOnlySet<int> targets) in previous.Connections)
        {
            if (!current.Connections.TryGetValue(source, out IReadOnlySet<int>? currentTargets) ||
                !targets.SetEquals(currentTargets))
            {
                return false;
            }
        }

        foreach ((int target, int hits) in previous.TargetHits)
        {
            if (!current.TargetHits.TryGetValue(target, out int currentHits) || currentHits != hits)
                return false;
        }

        return true;
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
                    EnsureIo(FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX), "FT_Purge");

                EnsureIo(
                    FT_Write(handle, data, (uint)data.Length, out uint written),
                    "FT_Write");

                if (written != data.Length)
                {
                    Interlocked.Increment(ref _d2xxErrorCount);
                    SafeDiagnostic(
                        $"D2XX_IO_ERROR api=FT_WriteShort written={written} expected={data.Length} " +
                        $"state={ConnectionState} scanning={IsScanning} handle_open={_handle != IntPtr.Zero}");
                    throw new IOException(
                        $"FT_Write thiếu byte: {written}/{data.Length}");
                }
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

        SafeLog(
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
                    EnsureIo(FT_Purge(handle, FT_PURGE_RX | FT_PURGE_TX), "FT_Purge");
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

                EnsureIo(
                    FT_GetQueueStatus(handle, out uint queued),
                    "FT_GetQueueStatus");

                if (queued == 0)
                    return [];

                var buffer = new byte[Math.Min(queued, 4096)];
                EnsureIo(
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
        // Sau khi tạo phiên scan mới, một số firmware còn đẩy phần cuối frame dù STOP
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

    private void SafePublishFrame(ScanFrame frame, string source)
    {
        EventHandler<ScanFrame>? handlers = FrameReceived;
        if (handlers is null)
            return;

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            var handler = (EventHandler<ScanFrame>)subscriber;
            long started = Stopwatch.GetTimestamp();
            try
            {
                handler(this, frame);
            }
            catch (Exception ex)
            {
                SafeDiagnostic(
                    $"D2XX_FRAME_SUBSCRIBER_ERROR source={source} seq={frame.Sequence} " +
                    $"handler={SanitizeDiagnostic(handler.Method.DeclaringType?.FullName ?? "unknown")}.{handler.Method.Name} " +
                    $"type={ex.GetType().Name} message={SanitizeDiagnostic(ex.Message)}");
            }
            finally
            {
                double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (elapsedMs >= SlowFrameSubscriberMs)
                {
                    SafeDiagnostic(
                        $"D2XX_FRAME_SUBSCRIBER_SLOW source={source} seq={frame.Sequence} " +
                        $"handler={SanitizeDiagnostic(handler.Method.DeclaringType?.FullName ?? "unknown")}.{handler.Method.Name} " +
                        $"elapsed_ms={elapsedMs:F3}");
                }
            }
        }
    }

    private void SafePublishProbePreview(ProductionProbePreview preview)
    {
        EventHandler<ProductionProbePreview>? handlers = ProductionProbePreviewReceived;
        if (handlers is null)
            return;

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            var handler = (EventHandler<ProductionProbePreview>)subscriber;
            long started = Stopwatch.GetTimestamp();
            try
            {
                handler(this, preview);
            }
            catch (Exception ex)
            {
                SafeDiagnostic(
                    $"D2XX_PROBE_SUBSCRIBER_ERROR handler={SanitizeDiagnostic(handler.Method.DeclaringType?.FullName ?? "unknown")}.{handler.Method.Name} " +
                    $"type={ex.GetType().Name} message={SanitizeDiagnostic(ex.Message)}");
            }
            finally
            {
                double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (elapsedMs >= SlowFrameSubscriberMs)
                {
                    SafeDiagnostic(
                        $"D2XX_PROBE_SUBSCRIBER_SLOW handler={SanitizeDiagnostic(handler.Method.DeclaringType?.FullName ?? "unknown")}.{handler.Method.Name} " +
                        $"elapsed_ms={elapsedMs:F3}");
                }
            }
        }
    }

    private void SafeLog(string message)
    {
        EventHandler<string>? handlers = Log;
        if (handlers is null)
            return;

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            var handler = (EventHandler<string>)subscriber;
            long started = Stopwatch.GetTimestamp();
            try
            {
                handler(this, message);
            }
            catch (Exception ex)
            {
                // Never call Log again from here; that could recurse forever if the
                // faulty subscriber is itself the logging UI.
                SafeDiagnostic(
                    $"D2XX_LOG_SUBSCRIBER_ERROR handler={SanitizeDiagnostic(handler.Method.DeclaringType?.FullName ?? "unknown")}.{handler.Method.Name} " +
                    $"type={ex.GetType().Name} message={SanitizeDiagnostic(ex.Message)}");
            }
            finally
            {
                double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (elapsedMs >= SlowLogSubscriberMs)
                {
                    SafeDiagnostic(
                        $"D2XX_LOG_SUBSCRIBER_SLOW handler={SanitizeDiagnostic(handler.Method.DeclaringType?.FullName ?? "unknown")}.{handler.Method.Name} " +
                        $"elapsed_ms={elapsedMs:F3}");
                }
            }
        }
    }

    private static string SanitizeDiagnostic(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();

    private static void SafeDiagnostic(string message)
    {
        try
        {
            AsyncFileLogService.Current.Performance(message);
        }
        catch
        {
            // Diagnostics must never be able to take the hardware transport down.
        }
    }

    void PublishProtocolTrace(string direction, ReadOnlySpan<byte> data)
    {
        EventHandler<D2xxProtocolTrace>? handlers = ProtocolTrace;
        if (handlers is null || data.IsEmpty)
            return;

        var trace = new D2xxProtocolTrace(
            DateTime.UtcNow,
            Stopwatch.GetTimestamp(),
            direction,
            data.ToArray());

        foreach (Delegate subscriber in handlers.GetInvocationList())
        {
            var handler = (EventHandler<D2xxProtocolTrace>)subscriber;
            try
            {
                handler(this, trace);
            }
            catch (Exception ex)
            {
                SafeDiagnostic(
                    $"D2XX_PROTOCOL_TRACE_SUBSCRIBER_ERROR handler={SanitizeDiagnostic(handler.Method.DeclaringType?.FullName ?? "unknown")}.{handler.Method.Name} " +
                    $"type={ex.GetType().Name} message={SanitizeDiagnostic(ex.Message)}");
            }
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
