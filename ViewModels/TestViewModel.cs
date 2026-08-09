using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using JBZUniversalTester.Core;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.ViewModels;

public sealed class TestViewModel : ObservableObject
{
    private enum RuntimeMode
    {
        Background = 0,
        Production = 1,
        Probe = 2,
        ShuttingDown = 3
    }

    private readonly MainViewModel _main;
    private readonly TestEngine _engine;
    private readonly IBoardTransport _board;
    private readonly KeysightVisaService _visa;
    private readonly AppSettings _settings;
    private readonly ProductionSettings _productionSettings;
    private readonly ProductionStatisticsStore _statisticsStore = new();
    private TestHistoryStore _historyStore;
    private readonly LabelPrintService _labelPrintService = new();
    private readonly AppSoundService _sound = AppSoundService.Current;
    private readonly ThtModelParser _modelParser = new();
    private readonly object _initializationGate = new();
    private readonly object _cycleTokenGate = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _cycleCts;

    private Task? _initializationTask;

    private ProductModel? _model;
    private ILookup<int, PinRecord> _pinsByIoLookup = Array.Empty<PinRecord>().ToLookup(pin => pin.IoNumber);
    private string _state = "CHỜ CHỌN MÃ HÀNG";
    private string _lot = "0";
    private string _keysightResource;
    private string _hardwareStatus = "Bo: đang khởi tạo...";
    private string _boardConnectionMessage = "Chưa kết nối bo JBZ.";
    private string? _currentModelPath;

    private int _total;
    private int _pass;
    private int _fail;
    private bool _cycleActive;
    private bool _waitForProductRelease;
    private bool _waitForFaultProductRemoval;
    private int _postContinuityStarted;
    private int _wiringFaultHandlingStarted;
    private Task? _hardwareInitializationTask;
    private Task? _hardwareMonitorTask;
    private int _selectedOperationTabIndex;
    private int _shutdownStarted;
    private bool _productDetectedThisCycle;
    private int _probeSessionActive;
    private int _runtimeMode = (int)RuntimeMode.Background;
    private long _runtimeGeneration;
    // V12.10.3: TestEngine.Reset() phát Changed đồng bộ. Trong Master state machine,
    // reset nội bộ không được phép tái nhập OnEngineChanged trước khi state hoàn tất.
    private int _suppressEngineChanged;
    // Gate liên luồng: callback D2XX chạy qua Dispatcher, còn UART protocol
    // chạy từ reader task. Mỗi chu kỳ chỉ một caller được chốt side effects.
    private int _resultRecordedThisCycle;
    private int _probeCycleRecordedThisCycle;
    private DateTime _cycleStartedAt = DateTime.Now;
    private long _dailyTestCount;
    private long _monthlyTestCount;
    private long _lifetimeTestCount;
    private long _probeCycleCount;

    // V13.0 DUAL BOARD: UART firmware emits high-level TESTPIN/OPEN/OTHER/CIRCUIT
    // events directly. D2XX continues to use ScanFrame + TestEngine unchanged.
    private readonly Dictionary<int, int[]> _uartOpenSnapshots = [];
    private readonly HashSet<(int A, int B)> _uartWrongPairs = [];
    private int _uartResultHandlingStarted;
    private string _uartWaitingRemovalReason = string.Empty;

    // V11.9: nhận dạng đầu dò GND ngay cả khi TestView đang mở. Firmware có
    // chữ ký fan-out dày (một source kéo theo hàng chục target liên tiếp).
    // Frame dạng này là thao tác dò pin, không phải chập mạch sản phẩm.
    // _inlineProbeContactIo chỉ là sentinel/primary để các interlock cũ đọc lock-free.
    // Danh sách thật được giữ riêng để V12.8 có thể hiển thị đồng thời 2 I/O.
    private int _inlineProbeContactIo;
    private readonly object _inlineProbeGate = new();
    private int[] _inlineProbeContactIos = Array.Empty<int>();
    private long _inlineProbeLastSeenUtcTicks;
    // V12.9.2: Probe UI tuyệt đối không dùng TTL/quarantine dài.
    // Timestamp chỉ còn phục vụ interlock relay chống rung cực ngắn sau RELEASE,
    // không được phép giữ ProbeContacts trên giao diện.
    private const int ProbeRelayReleaseDebounceMs = 40;

    // V12.9.5 - MASTER SAMPLE hoàn toàn tự động. Không còn manual command/checkbox.
    // Master NG chỉ mở khóa khi đủ N fault dây DUY NHẤT; cùng fault lặp nhiều frame
    // không được tăng bộ đếm. Master không bao giờ cộng LOT/Pass/Fail production.
    private MasterSequenceState _masterSequenceState = MasterSequenceState.WaitingGoodMaster;
    private bool _masterApproved;
    private bool _masterGoodVerified;
    private bool _masterBadVerified;
    private int _masterRequiredFaultCount = 2;
    private readonly HashSet<MasterFaultKey> _masterDetectedFaultKeys = [];
    // V12.10.1: cùng key dùng để dựng snapshot FaultGrid MasterBad. DataGrid chỉ
    // hiển thị một dòng cho mỗi fault unique, không lặp theo số frame scan.
    private readonly Dictionary<MasterFaultKey, FaultDetail> _masterDetectedFaultDetails = [];
    private bool _masterFaultCollectionLocked;
    private int _masterPostStarted;
    private int _masterEjectStarted;
    private long _masterBadCollectNotBeforeUtcTicks;
    private const int MasterBadSettleMs = 120;
    private string _masterStatus = "ĐANG CHỜ LẮP MẪU MASTER ĐẠT";

    // Mỗi lần người vận hành chọn model mới sẽ tăng generation. Tác vụ
    // auto-load model gần nhất lúc startup chỉ được áp dụng nếu generation
    // vẫn không đổi. Nhờ vậy model cũ không thể hoàn thành muộn rồi ghi đè
    // model mới, vốn là nguyên nhân bảng TestView xuất hiện chậm/đổi model.
    private int _modelLoadGeneration;

    public ObservableCollection<FaultRow> Faults { get; } = new();

    /// <summary>Danh sách fault duy nhất đã xác nhận trên MASTER NG.</summary>
    public ObservableCollection<MasterFaultDisplayRow> MasterFaults { get; } = new();

    // V12.8: đầu dò chạy SONG SONG với bảng cấu hình production. ProbeContacts
    // chỉ cấp dữ liệu cho thanh trạng thái đầu dò, không bao giờ thay thế Faults.
    public ObservableCollection<FaultRow> ProbeContacts { get; } = new();

    // V12.9: card vật lý được sinh động từ BoardCapacity. Probe chỉ đổi
    // HasProbeActivity; card vẫn ACTIVE khi nhấc que.
    public ObservableCollection<BoardCardState> Cards { get; } = new();
    // Alias tương thích mã cũ; collection này chứa cả card bật và card tắt.
    public ObservableCollection<BoardCardState> ActiveCards => Cards;
    public BoardCapacity BoardCapacity => _board.Capacity;
    public string BoardCapacityText =>
        $"{BoardCapacity.ExpansionModuleCount} module / {BoardCapacity.PhysicalCardCount} card / " +
        $"{BoardCapacity.TotalIoCapacity} I/O";

    public bool HasInlineProbeContacts => ProbeContacts.Count > 0;
    public string ProbeModeText => HasInlineProbeContacts
        ? $"ĐANG DÒ ({ProbeContacts.Count})"
        : "SẴN SÀNG";

    public ObservableCollection<ResistanceResult> Resistance { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    /// <summary>
    /// Phát trực tiếp frame scan đã được transport map về I/O toàn cục.
    /// PinProbe dùng event này thay vì cố phân tích chuỗi log.
    /// </summary>
    public event EventHandler<ScanFrame>? ScanFrameReceived;

    public string State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(StateBackground));
                Raise(nameof(StateForeground));
                RaiseActiveFault();
            }
        }
    }

    /// <summary>
    /// Màu trạng thái lớn giống máy production: PASS phải xanh lá; lỗi đỏ;
    /// chờ/đang kiểm tra dùng nền vàng dễ quan sát từ xa.
    /// </summary>
    public string StateBackground
    {
        get
        {
            string value = State ?? string.Empty;

            if (IsMasterSequenceActive)
            {
                if (value.Contains("LỖI THIẾT BỊ", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
                    return "#F07B7B";

                return MasterState is MasterSequenceState.EjectingGoodMaster or MasterSequenceState.EjectingBadMaster
                    ? "#8BE39A"
                    : "#FFE46B";
            }

            if (MasterApproved && value.Contains("SẴN SÀNG SẢN XUẤT", StringComparison.OrdinalIgnoreCase))
                return "#8BE39A";

            if (value.Equals("PASS", StringComparison.OrdinalIgnoreCase))
                return "#58D36B";

            if (value.Contains("LỖI", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("CHƯA ĐẠT", StringComparison.OrdinalIgnoreCase))
                return "#F07B7B";

            if (value.Contains("ĐANG KIỂM TRA", StringComparison.OrdinalIgnoreCase))
                return "#FFE46B";

            return "#FFF0A0";
        }
    }

    public string StateForeground => "#202020";

    public string Lot
    {
        get => _lot;
        private set => Set(ref _lot, value);
    }

    public string KeysightResource
    {
        get => _keysightResource;
        set => Set(ref _keysightResource, value);
    }

    public string HardwareStatus
    {
        get => _hardwareStatus;
        set => Set(ref _hardwareStatus, value);
    }

    public bool IsBoardConnected => _board.IsConnected;

    public string BoardConnectionMessage
    {
        get => _boardConnectionMessage;
        private set
        {
            if (Set(ref _boardConnectionMessage, value))
            {
                Raise(nameof(HasBoardConnectionError));
            }
        }
    }

    /// <summary>
    /// Dùng để tự động ẩn/hiện thanh cảnh báo lỗi kết nối trong TestWindow.
    /// </summary>
    public bool HasBoardConnectionError =>
        !string.IsNullOrWhiteSpace(BoardConnectionMessage);

    public string? CurrentModelPath
    {
        get => _currentModelPath;
        private set => Set(ref _currentModelPath, value);
    }

    public int Total
    {
        get => _total;
        set
        {
            if (Set(ref _total, value))
            {
                Raise(nameof(Rate));
            }
        }
    }

    public int Pass
    {
        get => _pass;
        set
        {
            if (Set(ref _pass, value))
            {
                Raise(nameof(Rate));
            }
        }
    }

    public int Fail
    {
        get => _fail;
        set
        {
            if (Set(ref _fail, value))
            {
                Raise(nameof(Rate));
            }
        }
    }

    // Htdrv gốc hiển thị/đếm theo từng dòng pin map đang còn trên bảng.
    // Vì vậy OpenCount phải là số row Dây chưa kết nối, không phải số network.
    public int OpenCount =>
        Faults.Count(x => x.Kind == FaultKind.Open);

    public int WrongCount =>
        Faults.Count(x => x.Kind == FaultKind.WrongWiring);

    public int ShortCount =>
        Faults.Count(x => x.Kind == FaultKind.Short);

    public int WiringFaultCount => WrongCount + ShortCount;

    public int PassedNetworkCount =>
        _engine.PassedNets.Count;

    public int ExpectedNetworkCount =>
        _engine.ExpectedNetCount;

    public string NetworkProgress => IsMasterBadPhase
        ? $"{MasterDetectedFaultCount}/{MasterRequiredFaultCount}"
        : $"{PassedNetworkCount}/{ExpectedNetworkCount}";

    public string ActiveFaultTitle
    {
        get
        {
            if (IsMasterSequenceActive)
                return (State ?? string.Empty)
                    .Replace("\r", " ", StringComparison.Ordinal)
                    .Replace("\n", " • ", StringComparison.Ordinal);

            if (State.Equals("PASS", StringComparison.OrdinalIgnoreCase))
                return "PASS";

            FaultDetail? fault = GetVisiblePrimaryFault();
            return fault?.Name ?? State;
        }
    }

    public string ActiveFaultMessage
    {
        get
        {
            if (IsMasterSequenceActive)
                return IsMasterBadPhase
                    ? $"{MasterStatus}  •  {MasterDetectedFaultCount}/{MasterRequiredFaultCount}"
                    : MasterStatus;

            FaultDetail? fault = GetVisiblePrimaryFault();
            if (fault is null)
                return string.Empty;

            if (fault.Type == ProductFaultType.OpenCircuit)
                return string.IsNullOrWhiteSpace(fault.WireName)
                    ? fault.Message
                    : $"Dây {fault.WireName} chưa kết nối";

            if (fault.Type == ProductFaultType.ResistanceOutOfRange && fault.MeasuredResistance is double measured)
                return $"Đo {measured:0.###} Ω | Giới hạn {fault.ResistanceMin:0.###}–{fault.ResistanceMax:0.###} Ω";

            return fault.Message;
        }
    }

    public string ActiveFaultExpectedText
    {
        get
        {
            FaultDetail? fault = GetVisiblePrimaryFault();
            if (fault?.ExpectedSourceIo is not int source || fault.ExpectedTargetIo is not int target)
                return string.Empty;
            return $"Tiêu chuẩn: {DescribeIoCompact(source)}  →  {DescribeIoCompact(target)}";
        }
    }

    public string ActiveFaultActualText
    {
        get
        {
            FaultDetail? fault = GetVisiblePrimaryFault();
            if (fault is null)
                return string.Empty;

            if (fault.ActualSourceIo is int source && fault.ActualTargetIo is int target)
                return $"Thực tế: {DescribeIoCompact(source)}  →  {DescribeIoCompact(target)}";

            if (fault.Type == ProductFaultType.ShortCircuit && fault.RelatedIos.Length > 1)
                return "Thực tế: " + string.Join("  ↔  ", fault.RelatedIos.Select(DescribeIoCompact));

            return string.Empty;
        }
    }

    public string ActiveFaultBackground
    {
        get
        {
            if (IsMasterSequenceActive)
                return StateBackground;

            FaultDetail? fault = GetVisiblePrimaryFault();
            if (State.Equals("PASS", StringComparison.OrdinalIgnoreCase))
                return "#58D36B";
            return fault?.Type switch
            {
                ProductFaultType.ShortCircuit => "#D32F2F",
                ProductFaultType.WrongWiring => "#E53935",
                ProductFaultType.OpenCircuit => "#F28C28",
                ProductFaultType.ResistanceOutOfRange => "#E53935",
                _ => StateBackground
            };
        }
    }

    public string ActiveFaultForeground =>
        IsMasterSequenceActive || GetVisiblePrimaryFault() is null || State.Equals("PASS", StringComparison.OrdinalIgnoreCase)
            ? StateForeground
            : "#FFFFFF";

    public string ModelName =>
        _model?.ModelName ?? string.Empty;

    public string PartNumber =>
        _model?.PartNumber ?? string.Empty;

    public string ProductName =>
        _model?.ProductName ?? string.Empty;

    public string VehicleType =>
        _model?.VehicleType ?? string.Empty;

    public string CustomerCode =>
        _model?.CustomerCode ?? string.Empty;

    public string Eco => _model?.Eco ?? string.Empty;
    public string Nco => _model?.Nco ?? string.Empty;
    public string Alc => _model?.Alc ?? string.Empty;

    public int ItemHeight => Math.Clamp(_productionSettings.ItemHeight, 30, 44);
    public int ScrollDelay => Math.Clamp(_productionSettings.ScrollDelay, 0, 5000);
    public int PageDelay => Math.Clamp(_productionSettings.PageDelay, 0, 5000);
    public bool ShowTitle => _productionSettings.ShowTitle;
    public bool ShowConnector => _productionSettings.ShowConnector;

    public PinRecord? FindPinByIo(int io) =>
        _pinsByIoLookup[io].FirstOrDefault();

    /// <summary>
    /// V12.9: lookup 1 -> N, không làm mất duplicate mapping khi một Global IO
    /// xuất hiện ở nhiều connector/pin trong THT.
    /// </summary>
    public IReadOnlyList<PinRecord> FindPinsByIo(int io) =>
        _pinsByIoLookup[io].ToArray();

    /// <summary>
    /// Trả các I/O cùng một tên dây/network với chân đang dò. TestPin dùng
    /// thông tin này để hiển thị đúng kiểu máy gốc: IO hiện tại + tên dây +
    /// màu + chân đó đang ghép với I/O nào trong file THT.
    /// </summary>
    public IReadOnlyList<int> FindRelatedIo(int io, string? wireName)
    {
        ProductModel? model = _model;
        if (model is null)
            return Array.Empty<int>();

        // V12.4: CLIP phải tra theo topology, không phụ thuộc WireName vì các
        // row AO/aN trong THT thực tế có thể để trống tên dây.
        if (model.Clip is not null)
        {
            IEnumerable<ClipBranch> clipBranches = model.Clip.Branches.Where(branch =>
                io == model.Clip.CommonIo ||
                io == branch.TargetIo);

            int[] clipRelated = clipBranches
                .SelectMany(branch => new[]
                {
                    model.Clip.CommonIo,
                    branch.TargetIo
                })
                .Where(candidate => candidate != io)
                .Distinct()
                .OrderBy(candidate => candidate)
                .ToArray();

            if (clipRelated.Length > 0)
                return clipRelated;
        }

        if (string.IsNullOrWhiteSpace(wireName))
            return Array.Empty<int>();

        string name = wireName.Trim();

        WireNet? net = model.Nets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) ||
            candidate.Pins.Any(pin =>
                string.Equals(pin.WireName?.Trim(), name, StringComparison.OrdinalIgnoreCase)));

        IEnumerable<int> related = net is not null
            ? net.IoNumbers
            : model.Pins
                .Where(pin =>
                    string.Equals(pin.WireName?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                .Select(pin => pin.IoNumber);

        return related
            .Where(candidate => candidate != io)
            .Distinct()
            .OrderBy(candidate => candidate)
            .ToArray();
    }

    public double Rate =>
        Total == 0 ? 0 : 100.0 * Pass / Total;

    public long DailyTestCount
    {
        get => _dailyTestCount;
        private set => Set(ref _dailyTestCount, value);
    }

    public long MonthlyTestCount
    {
        get => _monthlyTestCount;
        private set => Set(ref _monthlyTestCount, value);
    }

    public long LifetimeTestCount
    {
        get => _lifetimeTestCount;
        private set => Set(ref _lifetimeTestCount, value);
    }

    public long ProbeCycleCount
    {
        get => _probeCycleCount;
        private set
        {
            if (Set(ref _probeCycleCount, value))
            {
                Raise(nameof(ProbeCycleText));
                Raise(nameof(ProbeMaintenanceDue));
                Raise(nameof(ProbeMaintenanceStatus));
                Raise(nameof(ProbeMaintenanceBackground));
            }
        }
    }

    public long ProbeReplacementThreshold =>
        Math.Max(1, _productionSettings.ProbeReplacementThreshold);

    public string ProbeCycleText => $"{ProbeCycleCount:N0} / {ProbeReplacementThreshold:N0}";
    public bool ProbeMaintenanceDue => ProbeCycleCount >= ProbeReplacementThreshold;
    public string ProbeMaintenanceStatus => ProbeMaintenanceDue
        ? "ĐẾN CHU KỲ THAY PROBE PIN"
        : "PROBE PIN ĐANG TRONG CHU KỲ SỬ DỤNG";
    public string ProbeMaintenanceBackground => ProbeMaintenanceDue ? "#D32F2F" : "#E8F5E9";

    public MasterSequenceState MasterState
    {
        get => _masterSequenceState;
        private set
        {
            if (Set(ref _masterSequenceState, value))
            {
                Raise(nameof(IsMasterSequenceActive));
                Raise(nameof(IsMasterBadPhase));
                Raise(nameof(ProductionEnabled));
                Raise(nameof(MasterProgressText));
                Raise(nameof(NetworkProgress));
                RaiseActiveFault();
            }
        }
    }

    public bool MasterApproved
    {
        get => _masterApproved;
        private set
        {
            if (Set(ref _masterApproved, value))
            {
                Raise(nameof(IsMasterSequenceActive));
                Raise(nameof(ProductionEnabled));
                Raise(nameof(MasterProgressText));
                Raise(nameof(NetworkProgress));
                RaiseActiveFault();
            }
        }
    }

    public bool ProductionEnabled => MasterApproved;
    public bool IsMasterSequenceActive => _model is not null && !MasterApproved;
    public bool IsMasterBadPhase => MasterState is
        MasterSequenceState.WaitingBadMaster or
        MasterSequenceState.TestingBadMaster or
        MasterSequenceState.EjectingBadMaster;

    public int MasterRequiredFaultCount => _masterRequiredFaultCount;
    public int MasterDetectedFaultCount => _masterDetectedFaultKeys.Count;
    public string MasterProgressText => IsMasterBadPhase
        ? $"LỖI MASTER: {MasterDetectedFaultCount}/{MasterRequiredFaultCount}"
        : MasterApproved
            ? "MASTER HOÀN TẤT • SẴN SÀNG SẢN XUẤT"
            : string.Empty;

    public string MasterStatus
    {
        get => _masterStatus;
        private set
        {
            if (Set(ref _masterStatus, value))
                RaiseActiveFault();
        }
    }

    public int SelectedOperationTabIndex
    {
        get => _selectedOperationTabIndex;
        set => Set(ref _selectedOperationTabIndex, value);
    }

    public AsyncRelayCommand ConnectBoardCommand { get; }
    public AsyncRelayCommand ConnectKeysightCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand MeasureCommand { get; }
    public AsyncRelayCommand CompleteCommand { get; }
    public AsyncRelayCommand Relay1Command { get; }
    public AsyncRelayCommand Relay2Command { get; }
    public AsyncRelayCommand RelaysOffCommand { get; }

    public TestViewModel(
        MainViewModel main,
        TestEngine engine,
        IBoardTransport board,
        KeysightVisaService visa,
        AppSettings settings,
        ProductionSettings productionSettings)
    {
        _main = main;
        _engine = engine;
        _board = board;
        _visa = visa;
        _settings = settings;
        _productionSettings = productionSettings;
        _historyStore = new TestHistoryStore(ResolveHistoryDatabasePath(_productionSettings));
        Lot = _productionSettings.LotNo.ToString();
        _sound.Initialize();
        if (!string.IsNullOrWhiteSpace(_statisticsStore.RecoveryNotice))
            AddLog($"COUNTER RECOVERY: {_statisticsStore.RecoveryNotice}");

        _keysightResource =
            settings.Keysight.Resource ?? string.Empty;

        _engine.Changed += OnEngineChanged;
        _board.Log += OnBoardLog;
        _board.FrameReceived += OnBoardFrameReceived;
        if (_board is IFirmwareProtocolBoard firmwareBoard)
            firmwareBoard.ProtocolEventReceived += OnFirmwareProtocolEventReceived;

        RebuildActiveCards();

        ConnectBoardCommand =
            new AsyncRelayCommand(ConnectBoardAsync);

        ConnectKeysightCommand =
            new AsyncRelayCommand(ConnectKeysightAsync);

        StopCommand =
            new AsyncRelayCommand(StopTestAsync);

        MeasureCommand =
            new AsyncRelayCommand(MeasureResistanceAsync);

        CompleteCommand =
            new AsyncRelayCommand(CompleteTestAsync);

        Relay1Command =
            new AsyncRelayCommand(async () =>
            {
                if (!EnsureManualBoardReady("bật Relay 1 - MỞ/THÁO JIG", requireD2xxRelay: true))
                    return;

                int relay1Ms = _productionSettings.Relay1JigPulseMs;
                AddLog($"TEST Relay 1 - MỞ/THÁO JIG: pulse 1 lần ({relay1Ms} ms)");
                await _engine.PulseJigRelayAsync();
                AddLog("Relay 1 OFF - đã cưỡng bức về trạng thái chờ.");
            });

        Relay2Command =
            new AsyncRelayCommand(async () =>
            {
                if (!EnsureManualBoardReady("bật Relay 2 - MARKING", requireD2xxRelay: true))
                    return;

                int relay2Ms = _productionSettings.Relay2MarkingPulseMs;
                AddLog($"TEST Relay 2 - MARKING: pulse 1 lần ({relay2Ms} ms)");
                await _engine.PulseMarkingRelayAsync();
                AddLog("Relay 2 OFF - đã cưỡng bức về trạng thái chờ.");
            });

        RelaysOffCommand =
            new AsyncRelayCommand(async () =>
            {
                if (!EnsureManualBoardReady("tắt relay", requireD2xxRelay: true))
                    return;

                await _board.AllRelaysOffAsync();
                AddLog("Tất cả relay OFF");
            });

    }

    private bool EnsureManualBoardReady(string action, bool requireD2xxRelay = false)
    {
        if (!_board.IsConnected)
        {
            string message =
                $"Không thể {action} vì CHƯA KẾT NỐI VỚI BO MẠCH TEST.\n\n" +
                "Phần mềm vẫn tiếp tục hoạt động. Hãy kiểm tra:\n" +
                "• LOẠI BO MẠCH trong Cài đặt\n" +
                "• D2XX: cáp USB/driver FTDI\n" +
                "• UART TTL: COM, TX/RX/GND và 115200 8N1\n\n" +
                "Sau khi bo được kết nối, hãy thử lại thao tác.";

            BoardConnectionMessage = "CHƯA KẾT NỐI VỚI BO MẠCH TEST";
            HardwareStatus = "Bo: CHƯA KẾT NỐI";
            State = "CHƯA KẾT NỐI BO - CHỨC NĂNG PHẦN CỨNG BỊ KHÓA";
            AddLog($"MANUAL BLOCKED: {action} - bo chưa kết nối.");
            MessageBox.Show(
                message,
                "Chưa kết nối bo mạch test",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (requireD2xxRelay &&
            _board is UnifiedBoardTransport unified &&
            unified.ActiveMode == BoardMode.UartTtl)
        {
            string message =
                $"Không thể {action} bằng nút Relay D2XX.\n\n" +
                "Bo hiện tại là JBZ UART TTL. Firmware UART điều khiển chu trình tháo hàng " +
                "bằng PASSPEN/UNCONNECT, không dùng Relay 1/Relay 2 D2XX.";

            AddLog($"MANUAL BLOCKED: {action} - backend UART TTL không hỗ trợ relay D2XX.");
            MessageBox.Show(
                message,
                "Chức năng không áp dụng cho bo UART TTL",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private string ReadyStateForCurrentModel()
    {
        if (_model is null)
            return "CHỜ CHỌN MÃ HÀNG";
        return MasterApproved
            ? "CHỜ LẮP SẢN PHẨM"
            : "ĐANG CHỜ LẮP MẪU MASTER ĐẠT";
    }

    private static string ResolveHistoryDatabasePath(ProductionSettings settings)
    {
        string directory = string.IsNullOrWhiteSpace(settings.HistoryDirectory)
            ? "Data/History"
            : settings.HistoryDirectory.Trim();
        if (!Path.IsPathRooted(directory))
            directory = Path.Combine(AppContext.BaseDirectory, directory);
        return Path.Combine(directory, "test-history.db");
    }

    private CancellationToken BeginCycleOperations()
    {
        CancellationTokenSource? old;

        lock (_cycleTokenGate)
        {
            old = _cycleCts;
            _cycleCts = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCts.Token);
        }

        if (old is not null)
        {
            try { old.Cancel(); } catch { }
            old.Dispose();
        }

        return _cycleCts.Token;
    }

    private CancellationToken CurrentCycleToken()
    {
        lock (_cycleTokenGate)
            return _cycleCts?.Token ?? CancellationToken.None;
    }

    private void CancelCycleOperations()
    {
        CancellationTokenSource? current;

        lock (_cycleTokenGate)
        {
            current = _cycleCts;
            _cycleCts = null;
        }

        if (current is null)
            return;

        try { current.Cancel(); } catch { }
        current.Dispose();
    }

    /// <summary>
    /// Chạy đúng một lần khi TestWindow được mở: kết nối bo trước,
    /// sau đó tự tải file model đã kiểm tra gần nhất nếu chưa có model hiện tại.
    /// </summary>
    /// <summary>
    /// Khởi động kết nối bo đúng một lần ngay khi ứng dụng được tạo.
    /// Không yêu cầu người vận hành bấm nút KẾT NỐI BO.
    /// </summary>
    public Task InitializeHardwareAsync()
    {
        lock (_initializationGate)
        {
            if (_board.IsConnected)
                return EnsureContinuousProductionScanAsync();

            // Không cache vĩnh viễn một lần kết nối thất bại. Nếu task cũ đã
            // hoàn tất mà bo vẫn chưa Connected, lần gọi kế tiếp tự thử lại.
            if (_hardwareInitializationTask is null ||
                _hardwareInitializationTask.IsCompleted)
            {
                _hardwareInitializationTask = ConnectBoardWithRetryAsync();
            }

            return _hardwareInitializationTask;
        }
    }

    private async Task ConnectBoardWithRetryAsync()
    {
        const int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await ConnectBoardAsync();

            if (_board.IsConnected || _lifetimeCts.IsCancellationRequested)
                return;

            if (attempt < maxAttempts)
            {
                AddLog($"Tự kết nối bo lần {attempt} chưa thành công - thử lại nhanh...");
                try
                {
                    await Task.Delay(120, _lifetimeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task EnsureContinuousProductionScanAsync()
    {
        if (_lifetimeCts.IsCancellationRequested ||
            Volatile.Read(ref _probeSessionActive) != 0 ||
            Volatile.Read(ref _postContinuityStarted) != 0 ||
            Volatile.Read(ref _wiringFaultHandlingStarted) != 0 ||
            !_board.IsConnected)
        {
            return;
        }

        if (_board.IsScanning)
            return;

        try
        {
            // Đây chỉ là scan nền liên tục. Chưa bấm BẮT ĐẦU KIỂM TRA thì
            // TestEngine bị khóa, nên scan không thể tự PASS/relay.
            _engine.SetFrameProcessingEnabled(false);
            _board.ConfigureScanRange(_model?.MaxIo ?? 0);
            await _board.StartScanAsync(BoardScanMode.Production, _lifetimeCts.Token);
            State = ReadyStateForCurrentModel();
            AddLog("Bo đã kết nối và START SCAN I/O liên tục ở chế độ nền.");
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AddLog($"Không khởi động được scan nền: {ex.Message}");
        }
    }

    private async Task HardwareMonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (Volatile.Read(ref _probeSessionActive) == 0 &&
                    Volatile.Read(ref _postContinuityStarted) == 0 &&
                    Volatile.Read(ref _wiringFaultHandlingStarted) == 0)
                {
                    if (!_board.IsConnected)
                    {
                        await InitializeHardwareAsync();
                    }
                    else if (!_board.IsScanning)
                    {
                        await EnsureContinuousProductionScanAsync();
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AddLog($"Auto-reconnect/scan: {ex.Message}");
            }

            try
            {
                // SAFE OFFLINE MODE: không spam FTDI/COM khi máy chạy offline.
                // Vẫn tự reconnect nhưng với nhịp đủ nhẹ cho production PC.
                await Task.Delay(2000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public Task InitializeAsync()
    {
        lock (_initializationGate)
        {
            return _initializationTask ??= InitializeCoreAsync();
        }
    }

    private async Task InitializeCoreAsync()
    {
        AddLog("Khởi tạo ứng dụng: tự kết nối bo và tự nạp mã gần nhất.");

        // Chạy song song để MainWindow sẵn sàng nhanh hơn. Việc đọc THT không
        // phụ thuộc FTDI; ConfigureScanRange chỉ cập nhật cấu hình transport.
        Task boardTask = InitializeHardwareAsync();
        Task modelTask;

        if (_model is null)
        {
            modelTask = LoadLastTestedModelAsync();
        }
        else
        {
            CurrentModelPath = ResolveOptionalModelPath(_model.SourcePath);
            AddLog($"Giữ model đang chọn: {ModelName}");
            modelTask = Task.CompletedTask;
        }

        await Task.WhenAll(boardTask, modelTask);

        if (_board.IsConnected)
            await EnsureContinuousProductionScanAsync();

        // Theo dõi nhẹ 2 Hz: nếu USB/D2XX rơi, tự mở lại và khởi động scan nền.
        _hardwareMonitorTask ??= HardwareMonitorLoopAsync(_lifetimeCts.Token);

        State = _board.IsConnected
            ? ReadyStateForCurrentModel()
            : (_model is null ? "BO CHƯA KẾT NỐI" : "MODEL ĐÃ TẢI - BO CHƯA KẾT NỐI");
    }

    private static void ValidateModelPath(string path, out string fullPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Đường dẫn model không hợp lệ.", nameof(path));

        fullPath = ResolveModelPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Không tìm thấy file model .tht.", fullPath);
    }

    /// <summary>
    /// Nạp model do NGƯỜI VẬN HÀNH chọn. Lựa chọn này luôn ưu tiên hơn
    /// auto-load model startup. Parse chạy background, SetModel trở lại UI
    /// continuation nên bảng TestView được dựng ngay khi task hoàn tất.
    /// </summary>
    public async Task<ProductModel?> LoadSelectedModelFromPathAsync(string path)
    {
        ValidateModelPath(path, out string fullPath);

        int generation = Interlocked.Increment(ref _modelLoadGeneration);
        State = "ĐANG NẠP MÃ HÀNG...";

        ProductModel model = await Task.Run(() => _modelParser.Load(fullPath));

        // Nếu người vận hành chọn tiếp file khác trong lúc file này đang parse,
        // bỏ kết quả cũ thay vì ghi đè model mới hơn.
        if (generation != Volatile.Read(ref _modelLoadGeneration))
            return null;

        SetModel(model);
        State = _board.IsConnected ? ReadyStateForCurrentModel() : "MODEL ĐÃ TẢI - BO CHƯA KẾT NỐI";
        return model;
    }

    /// <summary>V15: nhận model đã parse bởi backend-specific parser (.model của Pi).</summary>
    public Task<ProductModel?> LoadPreparedModelAsync(ProductModel model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        Interlocked.Increment(ref _modelLoadGeneration);
        SetModel(model);
        State = _board.IsConnected ? ReadyStateForCurrentModel() : "MODEL ĐÃ TẢI - BO CHƯA KẾT NỐI";
        return Task.FromResult<ProductModel?>(model);
    }

    /// <summary>Compatibility helper for internal callers.</summary>
    public async Task LoadModelFromPathAsync(string path)
    {
        await LoadSelectedModelFromPathAsync(path);
    }

    private async Task LoadLastTestedModelAsync()
    {
        var savedPath = _settings.Storage.LastTestedModelFile;

        if (string.IsNullOrWhiteSpace(savedPath))
        {
            AddLog("Chưa có file model được kiểm tra gần nhất.");
            return;
        }

        var fullPath = ResolveModelPath(savedPath);
        if (!File.Exists(fullPath))
        {
            AddLog($"File model gần nhất không còn tồn tại: {fullPath}");
            return;
        }

        try
        {
            State = "ĐANG TẢI MODEL GẦN NHẤT";

            // Startup model chỉ là lựa chọn dự phòng. Nếu người vận hành chọn
            // file mới trong lúc parse đang chạy, generation đổi và kết quả
            // startup này bị bỏ ngay, tuyệt đối không ghi đè model mới.
            int generation = Volatile.Read(ref _modelLoadGeneration);
            ProductModel startupModel = await Task.Run(() => _modelParser.Load(fullPath));

            if (generation == Volatile.Read(ref _modelLoadGeneration) && _model is null)
            {
                SetModel(startupModel);
                AddLog($"Đã tự tải model gần nhất: {Path.GetFileName(fullPath)}");
            }
            else
            {
                AddLog($"Bỏ model startup {Path.GetFileName(fullPath)} vì người vận hành đã chọn model mới.");
            }

            State = _board.IsConnected
                ? ReadyStateForCurrentModel()
                : (_model is null ? "BO CHƯA KẾT NỐI" : "MODEL ĐÃ TẢI - BO CHƯA KẾT NỐI");
        }
        catch (Exception ex)
        {
            State = _board.IsConnected
                ? "SẴN SÀNG"
                : "LỖI KẾT NỐI BO";
            AddLog($"Không thể tải model gần nhất: {ex.Message}");
        }
    }

    private void SaveLastTestedModel()
    {
        var sourcePath = CurrentModelPath;

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            sourcePath = _model?.SourcePath;
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            AddLog("Không lưu được model gần nhất vì model không có SourcePath.");
            return;
        }

        try
        {
            var fullPath = ResolveModelPath(sourcePath);
            _settings.Storage.LastTestedModelFile = fullPath;
            _settings.Save();
            CurrentModelPath = fullPath;
            AddLog($"Đã lưu model kiểm tra gần nhất: {Path.GetFileName(fullPath)}");
        }
        catch (Exception ex)
        {
            // Không làm gián đoạn chu kỳ kiểm tra chỉ vì không ghi được appsettings.json.
            AddLog($"Không thể lưu model gần nhất: {ex.Message}");
        }
    }

    private static string ResolveModelPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Đường dẫn model không hợp lệ.", nameof(path));
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    private RuntimeMode CurrentRuntimeMode =>
        (RuntimeMode)Volatile.Read(ref _runtimeMode);

    private long SwitchRuntimeMode(RuntimeMode mode)
    {
        Volatile.Write(ref _runtimeMode, (int)mode);
        return Interlocked.Increment(ref _runtimeGeneration);
    }

    private bool IsRuntimeMode(RuntimeMode mode) =>
        CurrentRuntimeMode == mode;

    private bool IsRuntimeContext(RuntimeMode mode, long generation) =>
        CurrentRuntimeMode == mode &&
        Volatile.Read(ref _runtimeGeneration) == generation;

    /// <summary>
    /// Chỉ Production thật mới được phép tạo lỗi dây/popup.
    /// Probe phải bị loại kể cả khi callback Production cũ đã được xếp hàng
    /// trước lúc chuyển cửa sổ.
    /// </summary>
    private bool IsProductionFaultContext(long generation) =>
        IsRuntimeContext(RuntimeMode.Production, generation) &&
        Volatile.Read(ref _probeSessionActive) == 0 &&
        Volatile.Read(ref _inlineProbeContactIo) == 0 &&
        MasterApproved;

    /// <summary>
    /// API chẩn đoán explicit Probe: khóa Production trước khi chuyển transport.
    /// V12.9 không còn cửa sổ PinProbe riêng; UI vận hành dùng Probe song song
    /// trong TestWindow. StartProbeScanAsync vẫn được giữ cho diagnostic/service
    /// sau đó, nhưng từ thời điểm này không callback Production nào được phép đổi UI,
    /// phát âm lỗi hoặc mở popup.
    /// </summary>
    public void PrepareProbeUiMode()
    {
        CancelCycleOperations();
        SwitchRuntimeMode(RuntimeMode.Probe);
        // Khóa context Production trước khi diagnostic explicit Probe bắt đầu.
        Interlocked.Exchange(ref _probeSessionActive, 1);
        ClearInlineProbeContactsState(clearLastSeen: true);
        InvokeUi(ClearInlineProbeDisplay);

        _cycleActive = false;
        _waitForProductRelease = false;
        _waitForFaultProductRemoval = false;
        _productDetectedThisCycle = false;
        Interlocked.Exchange(ref _postContinuityStarted, 0);
        Interlocked.Exchange(ref _wiringFaultHandlingStarted, 0);

        _engine.SetFrameProcessingEnabled(false);
        _sound.SetWiringFaultAlarm(false);
        _sound.StopAll();

        // Probe là lớp hiển thị song song. Không được xóa bảng cấu hình/
        // trạng thái Production đang thấy trên TestView; chỉ ProbeContacts thay đổi.
        InvokeUi(RaiseTestStatistics);
    }

    private static string? ResolveOptionalModelPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : ResolveModelPath(path);
    }

    private void ResetEngineWithoutChangedReentry()
    {
        Interlocked.Increment(ref _suppressEngineChanged);
        try
        {
            _engine.Reset();
        }
        finally
        {
            Interlocked.Decrement(ref _suppressEngineChanged);
        }
    }

    private void OnEngineChanged(object? sender, EventArgs e)
    {
        // TestEngine.Reset() phát Changed đồng bộ. Bỏ qua callback lồng nhau
        // khi chính ViewModel đang reset engine trong một transition Master.
        if (Volatile.Read(ref _suppressEngineChanged) != 0)
            return;

        // V11.6: chỉ RuntimeMode.Production mới được phép cập nhật bảng lỗi,
        // phát TESTPOINT hoặc mở popup. Probe/Background bị chặn tuyệt đối.
        if (!IsRuntimeMode(RuntimeMode.Production) ||
            Volatile.Read(ref _probeSessionActive) != 0 ||
            Volatile.Read(ref _inlineProbeContactIo) != 0)
        {
            return;
        }

        long generation = Volatile.Read(ref _runtimeGeneration);

        if (!MasterApproved)
        {
            HandleMasterEngineChanged(generation);
            return;
        }

        InvokeUi(() =>
        {
            if (!IsProductionFaultContext(generation))
                return;

            RefreshFaults();

            // Sau lỗi: chỉ chờ tháo sản phẩm, không phát lại lỗi.
            if (_waitForFaultProductRemoval)
            {
                if (_engine.IsProductReleased)
                {
                    _waitForFaultProductRemoval = false;
                    _engine.Reset();
                    Interlocked.Exchange(ref _wiringFaultHandlingStarted, 0);
                    Interlocked.Exchange(ref _postContinuityStarted, 0);
                    _cycleActive = true;
                    _productDetectedThisCycle = false;
                    Interlocked.Exchange(ref _resultRecordedThisCycle, 0);
                    Interlocked.Exchange(ref _probeCycleRecordedThisCycle, 0);
                    Lot = _productionSettings.LotNo.ToString();
                    State = "CHỜ LẮP SẢN PHẨM";
                    AddLog("Đã tháo sản phẩm lỗi - chờ lắp sản phẩm lại.");
                }

                return;
            }

            // Sau PASS bắt buộc phải THÁO TOÀN BỘ sản phẩm khỏi JIG trước khi
            // ARM lượt test mới. Mất/chạm lại chỉ một I/O KHÔNG được xem là
            // sản phẩm đã tháo và tuyệt đối không được làm relay chạy lại.
            // Chỉ khi không còn bất kỳ quan hệ continuity sản phẩm nào thì mới
            // reset engine và chuyển về CHỜ LẮP SẢN PHẨM cho lượt tiếp theo.
            if (_waitForProductRelease)
            {
                if (_engine.IsProductReleased)
                {
                    _waitForProductRelease = false;
                    _engine.Reset();
                    Interlocked.Exchange(ref _postContinuityStarted, 0);
                    _cycleActive = true;
                    _productDetectedThisCycle = false;
                    Interlocked.Exchange(ref _resultRecordedThisCycle, 0);
                    Interlocked.Exchange(ref _probeCycleRecordedThisCycle, 0);
                    Lot = _productionSettings.LotNo.ToString();
                    State = "CHỜ LẮP SẢN PHẨM";
                    AddLog("PASS đã tháo hoàn toàn: toàn bộ continuity sản phẩm đã mất -> ARM lượt test mới.");
                }

                return;
            }

            // Chỉ product fault đã qua monotonic confirmation gate mới được
            // dừng scan/popup/ghi FAIL. Candidate raw không đi vào lifecycle FAIL.
            if (_cycleActive &&
                (_engine.HasWiringFault || _engine.HasConfirmedOpenCircuit) &&
                Interlocked.CompareExchange(ref _wiringFaultHandlingStarted, 1, 0) == 0)
            {
                _ = HandleWiringFaultAsync(generation);
                return;
            }

            if (_cycleActive && _engine.HasContactInstability)
            {
                _sound.SetWiringFaultAlarm(false);
                Interlocked.Exchange(ref _postContinuityStarted, 0);
                State = "TIẾP XÚC JIG/PROBE KHÔNG ỔN ĐỊNH — KIỂM TRA PROBE PIN/JIG";

                if (_engine.ContactLossTimedOut && _productDetectedThisCycle)
                {
                    // Mất contact kéo dài được xem là ranh giới cơ khí bị hủy,
                    // không phải FAIL. Lần lắp/contact tiếp theo là một Probe cycle mới.
                    _productDetectedThisCycle = false;
                    Interlocked.Exchange(ref _probeCycleRecordedThisCycle, 0);
                    AddLog("JIG CONTACT WARNING: mất toàn bộ contact quá cửa sổ; không ghi product FAIL.");
                }
                else if (_engine.HasProductActivity && !_productDetectedThisCycle)
                {
                    _cycleStartedAt = DateTime.Now;
                    _productDetectedThisCycle = true;
                    RecordProbeCycleStarted();
                }

                return;
            }

            // Chỉ khi không có lỗi mới cập nhật trạng thái lắp sản phẩm.
            if (_cycleActive)
            {
                bool hasActivity = _engine.HasProductActivity;

                if (hasActivity)
                {
                    if (!_productDetectedThisCycle)
                    {
                        _cycleStartedAt = DateTime.Now;
                        RecordProbeCycleStarted();
                    }
                    _productDetectedThisCycle = true;
                    if (!State.Equals("PASS", StringComparison.OrdinalIgnoreCase))
                        State = "ĐANG KIỂM TRA...";
                }
                else if (_productDetectedThisCycle)
                {
                    // Không suy diễn "mất hết activity" thành OPEN product.
                    // Confirmation gate sẽ phân nhánh contact warning/re-evaluation.
                    State = "TIẾP XÚC JIG/PROBE KHÔNG ỔN ĐỊNH — KIỂM TRA PROBE PIN/JIG";
                }
            }

            if (_cycleActive &&
                _engine.ContinuityPassed &&
                Interlocked.CompareExchange(ref _postContinuityStarted, 1, 0) == 0)
            {
                _ = RunAutomaticPostContinuityAsync();
            }
        });
    }

    private void OnFirmwareProtocolEventReceived(object? sender, BoardProtocolEvent evt)
    {
        if (_board is not IFirmwareProtocolBoard firmware || !firmware.UsesFirmwareCycleResult)
            return;

        switch (evt.Family.ToUpperInvariant())
        {
            case "START":
                InvokeUi(() => State = "ĐANG KIỂM TRA...");
                break;

            case "MEASURE":
                InvokeUi(() => State = "ĐANG ĐO / KIỂM TRA...");
                break;

            case "CLEAR":
                _uartOpenSnapshots.Clear();
                _uartWrongPairs.Clear();
                InvokeUi(RefreshUartFaultRows);
                break;

            case "TESTPIN":
                HandleUartProbe(evt);
                break;

            case "PIN":
                // Firmware variants may emit :PIN,<io>,0/1 instead of TESTPIN.
                if (evt.Values.Count >= 2 && (evt.Values[1] == "0" || evt.Values[1] == "1"))
                {
                    var translated = new BoardProtocolEvent(
                        evt.Timestamp, "TESTPIN", evt.Raw,
                        [evt.Values[0], evt.Values[1] == "1" ? "ON" : "OFF"]);
                    HandleUartProbe(translated);
                }
                break;

            case "OPEN":
                HandleUartOpen(evt);
                break;

            case "OTHER":
                HandleUartOther(evt);
                break;

            case "CIRCUIT":
                if (evt.Values.Count > 0 && int.TryParse(evt.Values[0], out int circuit))
                {
                    if (Interlocked.CompareExchange(ref _uartResultHandlingStarted, 1, 0) == 0)
                    {
                        // UART firmware chỉ cung cấp kết quả cycle cấp cao; CIRCUIT là
                        // bằng chứng fixture đã thực sự thực hiện một test, không đếm theo :START/frame.
                        RecordProbeCycleStarted();
                        _ = HandleUartCircuitResultAsync(circuit);
                    }
                }
                break;

            case "PEN":
                if (string.Equals(_uartWaitingRemovalReason, "PASS", StringComparison.OrdinalIgnoreCase))
                    _ = RequestUartRemovalAsync("PASS");
                break;

            case "REMOVAL":
                InvokeUi(() => State = "HÃY THÁO TOÀN BỘ SẢN PHẨM");
                break;

            case "UNCONNECT":
                _ = CompleteUartRemovalAsync();
                break;

            case "ERROR":
                InvokeUi(() => State = "LỖI BO UART TTL");
                AddLog($"UART firmware error: {evt.Raw}");
                break;
        }
    }

    private void HandleUartProbe(BoardProtocolEvent evt)
    {
        if (!_productionSettings.UseTestPointer || evt.Values.Count < 2 ||
            !int.TryParse(evt.Values[0], out int io) || io <= 0)
            return;

        bool active = evt.Values[1].Equals("ON", StringComparison.OrdinalIgnoreCase) || evt.Values[1] == "1";
        int[] current = SnapshotInlineProbeContacts();
        int[] next = active
            ? current.Append(io).Distinct().OrderBy(x => x).Take(2).ToArray()
            : current.Where(x => x != io).ToArray();

        bool changed = UpdateInlineProbeContacts(next);
        if (!changed)
            return;

        InvokeUi(() =>
        {
            if (next.Length > 0)
            {
                ShowInlineProbeContacts(next);
                _sound.PlayTestPoint();
            }
            else
            {
                ClearInlineProbeDisplay();
            }
        });
    }

    private void HandleUartOpen(BoardProtocolEvent evt)
    {
        int[] values = evt.Values.Select(v => int.TryParse(v, out int n) ? n : 0)
            .Where(n => n > 0).ToArray();
        if (values.Length == 0)
            return;

        int network = values[0];
        int[] pins = values.Skip(1).Distinct().ToArray();
        if (pins.Length == 0)
            _uartOpenSnapshots.Remove(network);
        else
            _uartOpenSnapshots[network] = pins;

        InvokeUi(RefreshUartFaultRows);
    }

    private void HandleUartOther(BoardProtocolEvent evt)
    {
        int[] values = evt.Values.Select(v => int.TryParse(v, out int n) ? n : 0)
            .Where(n => n > 0).Take(2).ToArray();
        if (values.Length < 2)
            return;
        int a = Math.Min(values[0], values[1]);
        int b = Math.Max(values[0], values[1]);
        _uartWrongPairs.Add((a, b));
        InvokeUi(RefreshUartFaultRows);
    }

    private void RefreshUartFaultRows()
    {
        if (_board is not IFirmwareProtocolBoard firmware || !firmware.UsesFirmwareCycleResult)
            return;

        Faults.Clear();
        foreach ((int network, int[] pins) in _uartOpenSnapshots.OrderBy(x => x.Key))
        {
            int io = pins.FirstOrDefault(network);
            PinRecord? pin = FindPinByIo(io);
            Faults.Add(new FaultRow
            {
                Kind = FaultKind.Open,
                ProductFaultType = ProductFaultType.OpenCircuit,
                FaultType = FaultTypeCatalog.DisplayName(ProductFaultType.OpenCircuit),
                Io = io,
                RelatedIos = pins,
                Connector = pin?.Connector ?? string.Empty,
                Pin = pin?.PinNumber ?? string.Empty,
                WireName = pin?.WireName ?? string.Empty,
                Splice = pin?.SpliceName ?? string.Empty,
                Section = pin?.Section ?? string.Empty,
                Color = pin?.Color ?? string.Empty,
                Status = "Không có kết nối: " + string.Join(" ↔ ", pins.Select(DescribeIoCompact))
            });
        }

        foreach ((int a, int b) in _uartWrongPairs.OrderBy(x => x.A).ThenBy(x => x.B))
        {
            PinRecord? pin = FindPinByIo(a);
            Faults.Add(new FaultRow
            {
                Kind = FaultKind.WrongWiring,
                ProductFaultType = ProductFaultType.WrongWiring,
                FaultType = FaultTypeCatalog.DisplayName(ProductFaultType.WrongWiring),
                Io = a,
                ActualSourceIo = a,
                ActualTargetIo = b,
                RelatedIos = [a, b],
                Connector = pin?.Connector ?? string.Empty,
                Pin = pin?.PinNumber ?? string.Empty,
                WireName = pin?.WireName ?? string.Empty,
                Splice = pin?.SpliceName ?? string.Empty,
                Section = pin?.Section ?? string.Empty,
                Color = pin?.Color ?? string.Empty,
                Status = $"SAI KẾT NỐI: {DescribeIoCompact(a)} ↔ {DescribeIoCompact(b)}"
            });
        }
    }

    private async Task HandleUartCircuitResultAsync(int circuit)
    {
        if (_board is not IFirmwareProtocolBoard firmware || !firmware.UsesFirmwareCycleResult || _model is null)
            return;

        try
        {
            if (circuit == 0)
            {
                _uartOpenSnapshots.Clear();
                _uartWrongPairs.Clear();
                InvokeUi(() =>
                {
                    RefreshUartFaultRows();
                    State = "PASS";
                });
                RecordCompletedProduct(true, "PASS");
                _uartWaitingRemovalReason = "PASS";
                AddLog("UART :CIRCUIT,0 => PASS. Chờ 300 ms rồi gửi PASSPEN giống Pi/V11.");
                await Task.Delay(300, CurrentCycleToken());
                await firmware.SendPassPenAsync(500, UartPinCount(), CurrentCycleToken());
            }
            else
            {
                InvokeUi(() => State = "KHÔNG ĐẠT");
                _sound.SetWiringFaultAlarm(true);
                IReadOnlyList<FaultDetail> faults = CaptureFaultDetails();
                await InvokeUiAsync(() =>
                {
                    var dialog = new JBZUniversalTester.Views.FaultConfirmationWindow(
                        faults,
                        "Sau XÁC NHẬN, hệ thống sẽ đưa JIG sang bước tháo hàng an toàn.");
                    dialog.Owner = Application.Current?.MainWindow;
                    dialog.ShowDialog();
                });
                _sound.SetWiringFaultAlarm(false);
                RecordCompletedProduct(false, "FAIL");
                await RequestUartRemovalAsync("FAIL");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AddLog($"UART result workflow error: {ex.Message}");
            InvokeUi(() => State = "LỖI CHU TRÌNH UART");
        }
    }

    private Task RequestUartRemovalAsync(string reason)
    {
        if (_board is not IFirmwareProtocolBoard firmware || !firmware.UsesFirmwareCycleResult)
            return Task.CompletedTask;
        _uartWaitingRemovalReason = reason;
        InvokeUi(() => State = reason == "PASS" ? "ĐÃ ĐÓNG DẤU - HÃY THÁO SẢN PHẨM" : "LỖI - HÃY THÁO SẢN PHẨM");
        AddLog($"UART {reason}: TX :UNCONNECT,500,{UartPinCount()}");
        return firmware.RequestUnconnectAsync(500, UartPinCount(), CurrentCycleToken());
    }

    private async Task CompleteUartRemovalAsync()
    {
        if (_board is not IFirmwareProtocolBoard firmware || !firmware.UsesFirmwareCycleResult)
            return;
        if (string.IsNullOrWhiteSpace(_uartWaitingRemovalReason))
            return;

        _uartWaitingRemovalReason = string.Empty;
        _uartOpenSnapshots.Clear();
        _uartWrongPairs.Clear();
        ClearInlineProbeContactsState(clearLastSeen: true);
        Interlocked.Exchange(ref _uartResultHandlingStarted, 0);
        InvokeUi(() =>
        {
            ClearInlineProbeDisplay();
            RefreshUartFaultRows();
            State = "SẴN SÀNG";
        });

        if (!_cycleActive || _lifetimeCts.IsCancellationRequested)
            return;

        try
        {
            await Task.Delay(50, CurrentCycleToken());
            _cycleStartedAt = DateTime.Now;
            Interlocked.Exchange(ref _resultRecordedThisCycle, 0);
            Interlocked.Exchange(ref _probeCycleRecordedThisCycle, 0);
            _productDetectedThisCycle = false;
            InvokeUi(() => Lot = _productionSettings.LotNo.ToString());
            AddLog("UART :UNCONNECT => ranh giới sản phẩm sạch; bắt đầu chu kỳ mới bằng :START.");
            await firmware.StartFirmwareCycleAsync(0, CurrentCycleToken());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AddLog($"UART không START được chu kỳ kế tiếp: {ex.Message}");
            InvokeUi(() => State = "BO UART MẤT KẾT NỐI");
        }
    }

    private IReadOnlyList<FaultDetail> BuildUartSnapshotFaultDetails()
    {
        var details = new List<FaultDetail>();
        foreach ((int _, int[] pins) in _uartOpenSnapshots.OrderBy(x => x.Key))
        {
            details.Add(EnrichFaultDetail(new FaultDetail
            {
                Type = ProductFaultType.OpenCircuit,
                RelatedIos = pins
            }));
        }

        foreach ((int a, int b) in _uartWrongPairs.OrderBy(x => x.A).ThenBy(x => x.B))
        {
            details.Add(EnrichFaultDetail(new FaultDetail
            {
                Type = ProductFaultType.WrongWiring,
                ActualSourceIo = a,
                ActualTargetIo = b,
                RelatedIos = [a, b]
            }));
        }

        if (details.Count == 0)
        {
            details.Add(new FaultDetail
            {
                Type = ProductFaultType.SystemDeviceError,
                Message = "Bo kết luận mạch không đạt nhưng chưa cung cấp chi tiết vị trí."
            });
        }

        return details;
    }

    private int UartPinCount() => Math.Max(1, _model?.Pins.Select(p => p.IoNumber).Where(io => io > 0).Distinct().Count() ?? 1);

    private Task InvokeUiAsync(Action action)
    {
        if (Application.Current?.Dispatcher is null || Application.Current.Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return Application.Current.Dispatcher.InvokeAsync(action).Task;
    }

    private void OnBoardLog(object? sender, string text)
    {
        AppLogLevel level = text.StartsWith("RX frame", StringComparison.OrdinalIgnoreCase) ||
                            text.StartsWith("TX ", StringComparison.OrdinalIgnoreCase)
            ? AppLogLevel.Diagnostic
            : AppLogLevel.Normal;
        AsyncFileLogService.Current.Board(text, level);

        InvokeUi(() =>
        {
            Logs.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {text}");
            while (Logs.Count > 300)
                Logs.RemoveAt(Logs.Count - 1);
        });
    }

    private void OnBoardFrameReceived(object? sender, ScanFrame frame)
    {
        RuntimeMode mode = CurrentRuntimeMode;
        long generation = Volatile.Read(ref _runtimeGeneration);

        // V12.9.2: router duy nhất + cập nhật Probe theo snapshot/event hiện tại.
        // Không Task.Delay, không DispatcherTimer và không TTL để giữ contact cũ.
        if (mode == RuntimeMode.Probe)
        {
            if (Volatile.Read(ref _probeSessionActive) != 0 &&
                frame.Mode == BoardScanMode.Probe)
            {
                int[] probeIos = frame.ActiveIo
                    .Where(_board.Capacity.ContainsGlobalIo)
                    .Distinct()
                    .Take(2)
                    .OrderBy(value => value)
                    .ToArray();

                bool changed = probeIos.Length > 0
                    ? UpdateInlineProbeContacts(probeIos)
                    : ClearInlineProbeContactsState();

                if (changed)
                {
                    DateTime requestedAt = DateTime.Now;
                    InvokeUi(() =>
                    {
                        if (!IsRuntimeContext(RuntimeMode.Probe, generation) ||
                            Volatile.Read(ref _probeSessionActive) == 0)
                        {
                            return;
                        }

                        if (probeIos.Length > 0)
                            ShowInlineProbeContacts(probeIos);
                        else
                            ClearInlineProbeDisplay();

                        LogProbeLatency(frame, requestedAt, probeIos);
                    });
                }

                ScanFrameReceived?.Invoke(this, frame);
            }

            return;
        }

        if (mode == RuntimeMode.Production &&
            Volatile.Read(ref _probeSessionActive) == 0 &&
            frame.Mode == BoardScanMode.Production)
        {
            // Probe là lớp quan sát SONG SONG. Nếu frame hiện tại có chữ ký Probe,
            // cập nhật ngay contact hiện tại và chặn frame đó khỏi TestEngine.
            if (TryDetectInlineProbeContacts(frame, out int[] touchedIos))
            {
                bool changed = UpdateInlineProbeContacts(touchedIos);
                Interlocked.Exchange(ref _wiringFaultHandlingStarted, 0);
                _sound.SetWiringFaultAlarm(false);
                _engine.ClearTransientWiringFaults();

                if (changed)
                {
                    DateTime requestedAt = DateTime.Now;
                    InvokeUi(() =>
                    {
                        if (!IsRuntimeContext(RuntimeMode.Production, generation) ||
                            Volatile.Read(ref _probeSessionActive) != 0)
                        {
                            return;
                        }

                        ShowInlineProbeContacts(touchedIos);
                        LogProbeLatency(frame, requestedAt, touchedIos);
                    });
                }

                return;
            }

            // RELEASE: frame mới không còn Probe => xóa ngay. Timestamp lastSeen
            // chỉ còn giữ interlock relay 40 ms, tuyệt đối không giữ UI.
            if (ClearInlineProbeContactsState())
            {
                _sound.SetWiringFaultAlarm(false);
                DateTime requestedAt = DateTime.Now;
                InvokeUi(() =>
                {
                    if (!IsRuntimeContext(RuntimeMode.Production, generation) ||
                        Volatile.Read(ref _probeSessionActive) != 0)
                    {
                        return;
                    }

                    ClearInlineProbeDisplay();
                    LogProbeLatency(frame, requestedAt, Array.Empty<int>());
                });
            }

            _engine.ProcessFrame(frame);
        }

        // Background/ShuttingDown: chỉ quét nền, không test và không tạo lỗi.
    }

    private static void LogProbeLatency(ScanFrame frame, DateTime uiRequestedAt, IReadOnlyList<int> ios)
    {
        DateTime renderedAt = DateTime.Now;
        double rxToVmMs = Math.Max(0, (uiRequestedAt - frame.Timestamp).TotalMilliseconds);
        double vmToUiMs = Math.Max(0, (renderedAt - uiRequestedAt).TotalMilliseconds);
        string state = ios.Count == 0
            ? "RELEASE"
            : $"TOUCH {string.Join(", ", ios.Select(io => $"IO{io}"))}";

        AsyncFileLogService.Current.Board(
            $"PROBE_LATENCY {state}; RX->VM={rxToVmMs:0.0} ms; VM->UI={vmToUiMs:0.0} ms; seq={frame.Sequence}",
            AppLogLevel.Diagnostic);
    }

    private bool TryDetectInlineProbeContacts(ScanFrame frame, out int[] ios)
    {
        ios = Array.Empty<int>();
        if (!_productionSettings.UseTestPointer ||
            frame.Mode != BoardScanMode.Production)
        {
            return false;
        }

        IReadOnlyList<ProbeContactClassifier.Detection> detections =
            ProbeContactClassifier.DetectMany(
                frame,
                _model,
                maxContacts: 2,
                boardCapacity: _board.Capacity);

        if (detections.Count > 0)
        {
            ios = detections
                .Select(item => item.Io)
                .Where(value => value > 0)
                .Distinct()
                .Take(2)
                .OrderBy(value => value)
                .ToArray();

            if (ios.Length > 0)
            {
                Interlocked.Exchange(ref _inlineProbeLastSeenUtcTicks, DateTime.UtcNow.Ticks);
                return true;
            }
        }

        // V12.9.2: không giữ contact cũ bằng TTL/quarantine. Frame hiện tại
        // không còn chữ ký Probe thì RELEASE được áp dụng ngay ở OnBoardFrameReceived.
        // Production có thể giữ stable-frame riêng trong TestEngine, nhưng Probe UI
        // không được chờ RequiredStableFrames hoặc timer 500-2000 ms.
        return false;
    }

    private int[] SnapshotInlineProbeContacts()
    {
        lock (_inlineProbeGate)
            return _inlineProbeContactIos.ToArray();
    }

    private bool UpdateInlineProbeContacts(IReadOnlyList<int> ios)
    {
        BoardCapacity capacity = _board.Capacity;
        int[] normalized = ios
            .Where(value => capacity.ContainsGlobalIo(value))
            .Distinct()
            .Take(2)
            .OrderBy(value => value)
            .ToArray();

        bool changed;
        lock (_inlineProbeGate)
        {
            changed = !_inlineProbeContactIos.SequenceEqual(normalized);
            _inlineProbeContactIos = normalized;
        }

        Volatile.Write(ref _inlineProbeContactIo, normalized.FirstOrDefault());
        return changed;
    }

    private bool ClearInlineProbeContactsState(bool clearLastSeen = false)
    {
        bool changed;
        lock (_inlineProbeGate)
        {
            changed = _inlineProbeContactIos.Length > 0;
            _inlineProbeContactIos = Array.Empty<int>();
        }

        Volatile.Write(ref _inlineProbeContactIo, 0);

        // RELEASE xóa UI ngay; lastSeen chỉ được giữ cho interlock relay 40 ms
        // chống rung rất ngắn. Reset timestamp hoàn toàn khi đổi phiên/chu kỳ.
        if (clearLastSeen)
            Interlocked.Exchange(ref _inlineProbeLastSeenUtcTicks, 0);

        return changed;
    }


    private bool IsProbeRelayInterlockActive()
    {
        if (!_productionSettings.UseTestPointer)
            return false;

        if (Volatile.Read(ref _probeSessionActive) != 0 ||
            Volatile.Read(ref _inlineProbeContactIo) != 0)
        {
            return true;
        }

        long lastTicks = Interlocked.Read(ref _inlineProbeLastSeenUtcTicks);
        if (lastTicks <= 0)
            return false;

        long elapsedTicks = Math.Max(0, DateTime.UtcNow.Ticks - lastTicks);
        return elapsedTicks < TimeSpan.FromMilliseconds(ProbeRelayReleaseDebounceMs).Ticks;
    }

    private async Task WaitForProbeRelayInterlockAsync(CancellationToken ct)
    {
        bool logged = false;
        while (IsProbeRelayInterlockActive())
        {
            ct.ThrowIfCancellationRequested();
            if (!logged)
            {
                logged = true;
                AddLog("Khóa relay an toàn: chờ debounce RELEASE đầu dò rất ngắn trước khi cho phép chuỗi PASS.");
            }

            await Task.Delay(50, ct);
        }
    }

    private IReadOnlyList<FaultRow> BuildProbeDisplayRows(IEnumerable<int> ios)
    {
        return ios
            .Where(io => io > 0)
            .Distinct()
            .Take(2)
            .SelectMany(BuildProbeDisplayRows)
            .ToArray();
    }

    private IReadOnlyList<FaultRow> BuildProbeDisplayRows(int io)
    {
        // V12.8: mỗi I/O đầu dò tạo đúng MỘT dòng; tối đa 2 dòng cùng lúc. Nếu chính I/O đang
        // chạm không có WireName, lấy tên dây từ đầu còn lại của cùng network
        // trong THT để người vận hành vẫn biết đúng dây cần kiểm tra.
        IReadOnlyList<PinRecord> pins = FindPinsByIo(io);
        PinRecord? pin = pins.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.WireName))
                         ?? pins.FirstOrDefault();

        string? clipStatus = BuildClipProbeStatus(io);
        string wireName = ResolveProbeWireName(io, pin);
        string color = ResolveProbeColor(io, pin, wireName);
        IReadOnlyList<int> related = FindRelatedIo(io, wireName);

        string status;
        if (!string.IsNullOrWhiteSpace(clipStatus))
        {
            status = clipStatus;
        }
        else if (!string.IsNullOrWhiteSpace(wireName) && related.Count > 0)
        {
            status = $"ĐẦU DÒ IO({io}) • Dây {wireName} • IO đối diện: " +
                     string.Join(", ", related.Select(value => $"IO({value})"));
        }
        else if (!string.IsNullOrWhiteSpace(wireName))
        {
            status = $"ĐẦU DÒ IO({io}) • Dây {wireName}";
        }
        else
        {
            status = $"ĐẦU DÒ IO({io})";
        }

        return
        [
            new FaultRow
            {
                Kind = FaultKind.Probe,
                FaultType = "ĐẦU DÒ",
                Io = io,
                Connector = pin?.Connector ?? string.Empty,
                Pin = pin?.PinNumber ?? string.Empty,
                WireName = wireName,
                Splice = pin?.SpliceName ?? string.Empty,
                Section = pin?.Section ?? string.Empty,
                Color = color,
                Status = status
            }
        ];
    }

    private string ResolveProbeWireName(int io, PinRecord? touchedPin)
    {
        if (!string.IsNullOrWhiteSpace(touchedPin?.WireName))
            return touchedPin.WireName.Trim();

        ProductModel? model = _model;
        if (model is null)
            return string.Empty;

        WireNet? net = model.Nets.FirstOrDefault(candidate => candidate.IoNumbers.Contains(io));
        if (net is not null)
        {
            string? mappedWire = net.Pins
                .Select(candidate => candidate.WireName)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (!string.IsNullOrWhiteSpace(mappedWire))
                return mappedWire.Trim();

            if (!string.IsNullOrWhiteSpace(net.Name))
                return net.Name.Trim();
        }

        if (model.Clip is not null)
        {
            if (io == model.Clip.CommonIo)
            {
                string? branchWire = model.Clip.Branches
                    .Select(branch => branch.TargetPin?.WireName)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrWhiteSpace(branchWire))
                    return branchWire.Trim();
            }
            else
            {
                ClipBranch? branch = model.Clip.Branches.FirstOrDefault(item => item.TargetIo == io);
                if (!string.IsNullOrWhiteSpace(branch?.TargetPin?.WireName))
                    return branch.TargetPin.WireName.Trim();
            }
        }

        return string.Empty;
    }

    private string ResolveProbeColor(int io, PinRecord? touchedPin, string wireName)
    {
        if (!string.IsNullOrWhiteSpace(touchedPin?.Color))
            return touchedPin.Color.Trim();

        ProductModel? model = _model;
        if (model is null)
            return string.Empty;

        WireNet? net = model.Nets.FirstOrDefault(candidate => candidate.IoNumbers.Contains(io));
        if (net is not null)
        {
            PinRecord? colored = net.Pins.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(candidate.Color) &&
                (string.IsNullOrWhiteSpace(wireName) ||
                 string.Equals(candidate.WireName?.Trim(), wireName.Trim(), StringComparison.OrdinalIgnoreCase)));

            colored ??= net.Pins.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.Color));
            if (!string.IsNullOrWhiteSpace(colored?.Color))
                return colored.Color.Trim();
        }

        if (model.Clip is not null)
        {
            if (io == model.Clip.CommonIo)
            {
                string? branchColor = model.Clip.Branches
                    .Select(branch => branch.TargetPin?.Color)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (!string.IsNullOrWhiteSpace(branchColor))
                    return branchColor.Trim();
            }
            else
            {
                ClipBranch? branch = model.Clip.Branches.FirstOrDefault(item => item.TargetIo == io);
                if (!string.IsNullOrWhiteSpace(branch?.TargetPin?.Color))
                    return branch.TargetPin.Color.Trim();
            }
        }

        return string.Empty;
    }

    private string? BuildClipProbeStatus(int io)
    {
        ClipTopology? clip = _model?.Clip;
        if (clip is null)
            return null;

        if (io == clip.CommonIo)
        {
            string branches = string.Join(
                ", ",
                clip.Branches.Select(branch => $"{branch.Name}->IO({branch.TargetIo})"));
            return $"CLIP A0/AO CHUNG IO({clip.CommonIo}): {branches}";
        }

        ClipBranch[] matches = clip.Branches
            .Where(branch => branch.TargetIo == io)
            .ToArray();

        if (matches.Length == 0)
            return null;

        string names = string.Join("/", matches.Select(branch => branch.Name));
        return $"CLIP {names}: A0/AO IO({clip.CommonIo}) -> IO({io})";
    }

    private void ShowInlineProbeContacts(IReadOnlyList<int> ios)
    {
        IReadOnlyList<FaultRow> rows = BuildProbeDisplayRows(ios);

        ProbeContacts.Clear();
        foreach (FaultRow row in rows)
            ProbeContacts.Add(row);

        UpdateProbeCardActivity(ios);
        Raise(nameof(HasInlineProbeContacts));
        Raise(nameof(ProbeModeText));

        string display = string.Join(", ", rows.Select(row => $"IO({row.Io})"));
        AddLog($"Đầu dò phát hiện {display}; hiển thị song song và bỏ qua logic chập của frame probe.");
    }

    private void ClearInlineProbeDisplay()
    {
        if (ProbeContacts.Count > 0)
            ProbeContacts.Clear();

        UpdateProbeCardActivity(Array.Empty<int>());
        Raise(nameof(HasInlineProbeContacts));
        Raise(nameof(ProbeModeText));
    }

    private void RebuildActiveCards()
    {
        BoardCapacity capacity = _board.Capacity;
        int[] currentProbe = SnapshotInlineProbeContacts();

        Cards.Clear();
        for (int cardNumber = 1; cardNumber <= BoardCapacity.MaxPhysicalCardCount; cardNumber++)
        {
            int firstIo = ((cardNumber - 1) * BoardCapacity.IoPerPhysicalCard) + 1;
            int lastIo = firstIo + BoardCapacity.IoPerPhysicalCard - 1;
            bool enabled =
                cardNumber >= capacity.StartCardNumber &&
                cardNumber < capacity.StartCardNumber + capacity.PhysicalCardCount;

            Cards.Add(new BoardCardState
            {
                CardNumber = cardNumber,
                ExpansionModuleNumber =
                    ((cardNumber - 1) / BoardCapacity.PhysicalCardsPerExpansionModule) + 1,
                FirstGlobalIo = firstIo,
                LastGlobalIo = lastIo,
                IsEnabled = enabled,
                IsScanning = enabled && _board.IsScanning,
                HasProbeActivity = enabled &&
                    currentProbe.Any(io => io >= firstIo && io <= lastIo)
            });
        }

        Raise(nameof(BoardCapacity));
        Raise(nameof(BoardCapacityText));
    }

    private void UpdateProbeCardActivity(IReadOnlyList<int> ios)
    {
        foreach (BoardCardState card in Cards)
        {
            card.HasProbeActivity = card.IsEnabled && ios.Any(io =>
                io >= card.FirstGlobalIo && io <= card.LastGlobalIo);
        }
    }

    private void UpdateCardScanningState()
    {
        bool scanning = _board.IsScanning;
        foreach (BoardCardState card in Cards)
            card.IsScanning = card.IsEnabled && scanning;
    }

    public IReadOnlyList<FaultRow> GetProbeRows(int io) => BuildProbeDisplayRows(io);

    private async Task ConnectBoardAsync()
    {
        try
        {
            if (_board.IsConnected)
            {
                BoardConnectionMessage = string.Empty;
                HardwareStatus = "Bo: đã kết nối";
                State = ReadyStateForCurrentModel();
                Raise(nameof(IsBoardConnected));
                await EnsureContinuousProductionScanAsync();
                return;
            }

            State = "ĐANG KẾT NỐI BO";

            var info = await _board.ConnectAsync(_lifetimeCts.Token);

            if (_lifetimeCts.IsCancellationRequested)
                return;

            BoardConnectionMessage = string.Empty;
            HardwareStatus =
                $"Bo: {info.Description} [{info.SerialNumber}] - ĐÃ KẾT NỐI";

            AddLog(
                $"Đã kết nối bo: {info.Description} [{info.SerialNumber}]");

            State = ReadyStateForCurrentModel();
            Raise(nameof(IsBoardConnected));

            // Kết nối thành công là START SCAN ngay. Không chờ mở TestView.
            await EnsureContinuousProductionScanAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            // Ứng dụng đang thoát; không cập nhật UI và không báo lỗi kết nối.
            return;
        }
        catch (Exception ex)
        {
            // Đây là lỗi phần cứng có thể phục hồi. Không ném lại exception,
            // để người dùng vẫn vào được màn hình Test và bấm kết nối lại sau.
            BoardConnectionMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? "Không thể kết nối với bo JBZ."
                : ex.Message.Trim();
            HardwareStatus = "Bo: CHƯA KẾT NỐI";

            State = _model is null
                ? "BO CHƯA KẾT NỐI"
                : "MODEL ĐÃ TẢI - BO CHƯA KẾT NỐI";

            AddLog($"Chưa kết nối được board: {ex.Message}");
            Raise(nameof(IsBoardConnected));

        }
    }

    private async Task ConnectKeysightAsync()
    {
        // Giữ command để tương thích binding cũ, nhưng việc kết nối thực tế là tự động
        // và chỉ xảy ra khi model có bước đo điện trở sau continuity PASS.
        await EnsureKeysightConnectedAsync();
    }

    private async Task EnsureKeysightConnectedAsync()
    {
        if (_visa.IsConnected) return;

        State = "ĐANG CHUẨN BỊ ĐO ĐIỆN TRỞ";
        var idn = await Task.Run(() =>
            _visa.ConnectAutomatic(_settings.Keysight.Resource));
        AddLog($"Đã tự kết nối Keysight: {idn}");
    }

    /// <summary>
    /// Dừng mọi thao tác của TestView/PinProbe nhưng giữ kết nối board để có
    /// thể quay về MainWindow rồi test lại ngay. Cửa sổ phải await hàm này
    /// trước khi đóng để không để worker scan chạy ngầm.
    /// </summary>
    public Task StopViewAsync() => StopTestAsync();

    /// <summary>
    /// Shutdown cuối cùng của ứng dụng. Idempotent: chỉ chạy một lần.
    /// Dừng scan -> relay OFF -> reset -> disconnect FTDI -> đóng VISA ->
    /// bỏ toàn bộ event subscription.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            return;

        SwitchRuntimeMode(RuntimeMode.ShuttingDown);
        _lifetimeCts.Cancel();
        CancelCycleOperations();

        if (_hardwareMonitorTask is not null)
        {
            try { await _hardwareMonitorTask; } catch { }
        }

        _cycleActive = false;
        _waitForProductRelease = false;
        _waitForFaultProductRemoval = false;
        _productDetectedThisCycle = false;
        Interlocked.Exchange(ref _postContinuityStarted, 0);
        Interlocked.Exchange(ref _wiringFaultHandlingStarted, 0);
        Interlocked.Exchange(ref _probeSessionActive, 0);

        _sound.SetWiringFaultAlarm(false);
        _sound.StopAll();
        _engine.SetFrameProcessingEnabled(false);

        try
        {
            // DisconnectAsync V10.8 tự chạy đúng sequence cuối của Htdrv:
            // STOP -> RESET -> INIT1 -> INIT2 -> STOP -> FT_Close.
            // Không gửi cleanup trùng lặp từ ViewModel nữa.
            await _board.DisconnectAsync();
        }
        catch (Exception ex)
        {
            AddLog($"Cleanup board khi thoát chưa hoàn chỉnh: {ex.Message}");
        }

        try
        {
            _visa.Dispose();
        }
        catch
        {
        }

        _engine.Changed -= OnEngineChanged;
        _board.Log -= OnBoardLog;
        _board.FrameReceived -= OnBoardFrameReceived;
        if (_board is IFirmwareProtocolBoard firmwareBoard)
            firmwareBoard.ProtocolEventReceived -= OnFirmwareProtocolEventReceived;

        _lifetimeCts.Dispose();
    }

    public async Task StartProbeScanAsync()
    {
        // PrepareProbeUiMode() đã bật guard từ constructor. Nếu hàm được gọi
        // trực tiếp ở nơi khác thì bật guard tại đây. Không return sớm vì vẫn
        // phải chuyển transport sang decoder Probe.
        Interlocked.Exchange(ref _probeSessionActive, 1);

        // V12.9 không còn PinProbeWindow riêng. Giữ fallback để API diagnostic
        // explicit Probe vẫn an toàn nếu được gọi từ service/tool nội bộ.
        if (!IsRuntimeMode(RuntimeMode.Probe))
            PrepareProbeUiMode();

        try
        {
            if (!_board.IsConnected)
                await InitializeHardwareAsync();

            if (!_board.IsConnected)
                throw new InvalidOperationException("Bo JBZ chưa kết nối.");

            // Hủy mọi relay/đo/PASS còn chạy của production trước khi đổi mode.
            CancelCycleOperations();

            // Khóa production TRƯỚC khi dừng worker cũ. Nếu còn callback cũ
            // đang xếp hàng trên Dispatcher, OnEngineChanged cũng sẽ bỏ qua.
            _cycleActive = false;
            _waitForProductRelease = false;
            _waitForFaultProductRemoval = false;
            _productDetectedThisCycle = false;
            Interlocked.Exchange(ref _postContinuityStarted, 0);
            Interlocked.Exchange(ref _wiringFaultHandlingStarted, 0);
            _sound.SetWiringFaultAlarm(false);
            _sound.StopAll();
            _engine.SetFrameProcessingEnabled(false);

            // Giữ nguyên bảng cấu hình/kết quả Production trên TestView.
            // Diagnostic Probe chỉ cập nhật ProbeContacts ở vùng riêng.
            InvokeUi(RaiseTestStatistics);

            // Dừng sạch mode cũ trước, sau đó mới cấu hình/switch sang Probe.
            // Transport V10.7 còn dùng decoder riêng theo từng generation để
            // đảm bảo byte TestPin không bao giờ lọt sang parser Production.
            _board.ConfigureScanRange(_model?.MaxIo ?? 0);
            await _board.StartScanAsync(BoardScanMode.Probe);
            InvokeUi(UpdateCardScanningState);

            State = "ĐANG DÒ CHÂN";
            AddLog("TESTPIN: quét liên tục độc quyền; chỉ hiển thị I/O que GND đang chạm.");
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _probeSessionActive, 0);
            SwitchRuntimeMode(RuntimeMode.Background);
            _engine.SetFrameProcessingEnabled(false);
            _engine.Reset();

            if (!_board.IsConnected)
            {
                BoardConnectionMessage = "CHƯA KẾT NỐI VỚI BO MẠCH TEST";
                HardwareStatus = "Bo: CHƯA KẾT NỐI";
                State = "CHƯA KẾT NỐI BO - KHÔNG THỂ TEST PROBE PIN";
                AddLog($"TESTPIN không bắt đầu: {ex.Message}");
                MessageBox.Show(
                    "Không thể TEST PROBE PIN vì chưa kết nối với bo mạch test.\n\n" +
                    "Phần mềm vẫn hoạt động bình thường. Hãy kết nối bo rồi thử lại.",
                    "Chưa kết nối bo mạch test",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            AddLog($"TESTPIN lỗi: {ex.Message}");
            throw;
        }
    }

    public async Task StopProbeScanAsync()
    {
        if (Interlocked.Exchange(ref _probeSessionActive, 0) == 0)
            return;

        SwitchRuntimeMode(RuntimeMode.Background);

        try
        {
            if (_board.IsConnected)
            {
                // Thoát Probe phải quay thẳng về production scan nền.
                await _board.StartScanAsync(BoardScanMode.Production);
                InvokeUi(UpdateCardScanningState);
            }
        }
        finally
        {
            _sound.SetWiringFaultAlarm(false);
            _sound.StopAll();
            _engine.SetFrameProcessingEnabled(false);
            _engine.Reset();
            State = ReadyStateForCurrentModel();
            AddLog("Đã đóng TestPin; tự quay lại quét I/O production liên tục.");
        }
    }

    /// <summary>
    /// Được TestWindow gọi tự động ngay sau khi người vận hành bấm
    /// "BẮT ĐẦU KIỂM TRA" tại MainWindow. Không còn nút Start I/O thứ hai.
    /// </summary>
    public Task StartProductionTestAsync() => StartTestAsync();

    private async Task StartTestAsync()
    {
        if (_model is null)
        {
            throw new InvalidOperationException(
                "Chưa tải model .tht.");
        }

        if (!_board.IsConnected)
            await InitializeHardwareAsync();

        if (!_board.IsConnected)
        {
            if (string.IsNullOrWhiteSpace(BoardConnectionMessage))
            {
                BoardConnectionMessage =
                    "Chưa kết nối bo JBZ. Hãy kiểm tra LOẠI BO MẠCH trong Cài đặt; " +
                    "D2XX: cáp/driver FTDI; UART TTL: COM, TX/RX/GND, mức 3.3V và 115200 8N1.";
            }

            State = "BO CHƯA KẾT NỐI";
            HardwareStatus = "Bo: CHƯA KẾT NỐI";
            AddLog("Không thể bắt đầu kiểm tra vì bo JBZ chưa kết nối sau recovery tự động.");
            return;
        }

        // Production và TestPin loại trừ lẫn nhau. Đổi generation trước khi
        // ARM engine để callback Probe/Background cũ không thể lọt sang test.
        Interlocked.Exchange(ref _probeSessionActive, 0);
        SwitchRuntimeMode(RuntimeMode.Production);

        // Chu kỳ mới có CancellationToken riêng. Khi đóng TestView/thoát app,
        // mọi delay/relay/đo còn chạy của chu kỳ cũ sẽ bị hủy trước cleanup board.
        CancellationToken cycleToken = BeginCycleOperations();

        // D2XX luôn bắt đầu từ relay OFF. UART TTL không có relay D2XX;
        // backend UART map AllRelaysOff thành no-op và dùng PASSPEN/UNCONNECT.
        await _board.AllRelaysOffAsync(cycleToken);

        // Chỉ ghi lại khi model thực sự được dùng để bắt đầu một chu kỳ kiểm tra.
        SaveLastTestedModel();

        // Chu kỳ mới bắt đầu với trạng thái cảnh báo sạch.
        _cycleActive = MasterApproved;
        _waitForProductRelease = false;
        _waitForFaultProductRemoval = false;
        Interlocked.Exchange(ref _postContinuityStarted, 0);
        Interlocked.Exchange(ref _wiringFaultHandlingStarted, 0);
        Interlocked.Exchange(ref _masterPostStarted, 0);
        ClearInlineProbeContactsState(clearLastSeen: true);
        InvokeUi(ClearInlineProbeDisplay);
        _sound.SetWiringFaultAlarm(false);
        // V12.9.5: engine phải chạy cả khi Master Gate đang khóa để state machine
        // tự xác nhận Good/Bad Master. Context Master không được ghi production result.
        _engine.SetFrameProcessingEnabled(true);
        _engine.Reset();
        _productDetectedThisCycle = false;
        Interlocked.Exchange(ref _resultRecordedThisCycle, 0);
        Interlocked.Exchange(ref _probeCycleRecordedThisCycle, 0);
        _cycleStartedAt = DateTime.Now;
        Lot = _productionSettings.LotNo.ToString();

        Resistance.Clear();
        RefreshFaults();

        RaiseTestStatistics();
        SelectedOperationTabIndex = 0;

        // V12.9: mỗi lần ARM TestView phải chốt lại BoardCapacity từ Settings
        // và tạo generation scan mới. Nhờ vậy thay đổi số card/start card không
        // thể để decoder nền cũ tiếp tục chạy với capacity cũ. START_SCAN không
        // INIT lại nên chuyển rất nhanh nhưng vẫn purge/invalidate sạch frame cũ.
        _board.ConfigureScanRange(_model.MaxIo);
        InvokeUi(RebuildActiveCards);
        await _board.StartScanAsync(BoardScanMode.Production, cycleToken);
        InvokeUi(UpdateCardScanningState);

        if (_board is IFirmwareProtocolBoard firmware && firmware.UsesFirmwareCycleResult)
        {
            // Firmware Pi tự kết luận CIRCUIT và tự phát TESTPIN. Không chạy
            // Master state-machine D2XX vì nó phụ thuộc raw ScanFrame.
            MasterApproved = true;
            MasterStatus = "UART TTL • KẾT QUẢ THEO FIRMWARE (:CIRCUIT)";
            RaiseMasterState();
            _cycleActive = true;
            _uartOpenSnapshots.Clear();
            _uartWrongPairs.Clear();
            _uartWaitingRemovalReason = string.Empty;
            Interlocked.Exchange(ref _uartResultHandlingStarted, 0);
            State = "ĐANG KIỂM TRA...";
            AddLog($"UART TTL: ARM chu kỳ bằng :START trên {firmware.ActivePort}; kết quả theo :CIRCUIT.");
            await firmware.StartFirmwareCycleAsync(0, cycleToken);
            return;
        }

        if (!MasterApproved)
        {
            await StartAutomaticMasterSequenceAsync();
            return;
        }

        State = "CHỜ LẮP SẢN PHẨM";
        AddLog("Đã ARM chu kỳ production trên luồng scan I/O đang chạy liên tục.");
    }

    private async Task StopTestAsync()
    {
        // Khóa/cancel workflow TRƯỚC khi gửi lệnh board. Nếu PASS task đang chờ
        // relay/interlock, nó phải dừng trước để không gửi command sau khi view đóng.
        _cycleActive = false;
        SwitchRuntimeMode(RuntimeMode.Background);
        CancelCycleOperations();

        try
        {
            // Đóng TestView không dừng phần cứng. Bo tiếp tục quét I/O nền để
            // lần mở TestView sau không có độ trễ START/INIT.
            _engine.SetFrameProcessingEnabled(false);
            _engine.Reset();
            if (_board.IsConnected && !_board.IsScanning)
                await EnsureContinuousProductionScanAsync();
        }
        finally
        {
            _waitForProductRelease = false;
            _waitForFaultProductRemoval = false;
            _productDetectedThisCycle = false;
            Interlocked.Exchange(ref _postContinuityStarted, 0);
            Interlocked.Exchange(ref _wiringFaultHandlingStarted, 0);
            _sound.SetWiringFaultAlarm(false);
            State = ReadyStateForCurrentModel();
            AddLog("Đã rời TestView; scan I/O nền vẫn chạy liên tục.");
        }
    }

    private bool IsProbeSessionActive =>
        IsRuntimeMode(RuntimeMode.Probe) &&
        Volatile.Read(ref _probeSessionActive) != 0;

    private void AbortProductionFaultForProbe()
    {
        Interlocked.Exchange(ref _wiringFaultHandlingStarted, 0);
        _sound.SetWiringFaultAlarm(false);
    }

    private async Task HandleWiringFaultAsync(long generation)
    {
        // Callback production cũ có thể đã được schedule ngay trước khi mở
        // TestPin. Probe mode tuyệt đối không được hiện popup chập/đấu sai.
        if (!IsProductionFaultContext(generation))
        {
            AbortProductionFaultForProbe();
            return;
        }

        if (_model is null)
            return;

        _cycleActive = false;
        State = "ĐANG XỬ LÝ LỖI DÂY";
        SelectedOperationTabIndex = 0;

        // TESTPOINT.wav phải kêu liên tục cho tới khi người vận hành xác nhận.
        _sound.SetWiringFaultAlarm(true);

        try
        {
            if (_board.IsConnected)
            {
                await _board.StopScanAsync();
                await _board.AllRelaysOffAsync();
            }
        }
        catch (Exception ex)
        {
            AddLog($"Không thể dừng bo sau lỗi đấu sai: {ex.Message}");
        }

        // TestPin có thể được mở trong lúc handler production đang await
        // StopScan. Phải kiểm tra LẠI sau await; nếu không callback cũ vẫn
        // có thể bật popup chập dù transport đã chuyển sang Probe.
        if (!IsProductionFaultContext(generation))
        {
            AbortProductionFaultForProbe();
            return;
        }

        WiringFaultPair[] wiringPairs = _engine.WiringFaults
            .OrderBy(x => x.SourceIo)
            .ThenBy(x => x.TargetIo)
            .ToArray();

        FaultDetail[] dialogFaults = _engine.BuildConfirmedOpenFaults()
            .Select(EnrichFaultDetail)
            .Concat(wiringPairs.Select(pair => EnrichFaultDetail(new FaultDetail
            {
                Type = pair.FaultType,
                ExpectedSourceIo = pair.ExpectedSourceIo,
                ExpectedTargetIo = pair.ExpectedTargetIo,
                ActualSourceIo = pair.SourceIo,
                ActualTargetIo = pair.TargetIo,
                RelatedIos = [pair.SourceIo, pair.TargetIo],
                Message = pair.Reason
            })))
            .GroupBy(fault => new
            {
                fault.Type,
                fault.ExpectedSourceIo,
                fault.ExpectedTargetIo,
                fault.ActualSourceIo,
                fault.ActualTargetIo,
                fault.WireName
            })
            .Select(group => group.First())
            .ToArray();

        if (dialogFaults.Length == 0)
        {
            AbortProductionFaultForProbe();
            State = "ĐANG KIỂM TRA...";
            return;
        }

        ProductFaultType primaryType = dialogFaults
            .Select(fault => fault.Type)
            .OrderBy(FaultTypeCatalog.Priority)
            .First();
        string primaryName = FaultTypeCatalog.DisplayName(primaryType);
        State = primaryName;

        AddLog(
            $"DỪNG TEST do {primaryName}: " +
            string.Join(", ", dialogFaults.Select(fault =>
                $"{fault.Code} {fault.ExpectedText} {fault.ActualText}".Trim())));

        // Chốt cuối ngay trước UI modal. Từ thời điểm Probe bật, tuyệt đối
        // không được phép hiện popup production.
        if (!IsProductionFaultContext(generation))
        {
            AbortProductionFaultForProbe();
            return;
        }

        var faultDialog = new JBZUniversalTester.Views.FaultConfirmationWindow(
            dialogFaults,
            "Sau khi XÁC NHẬN: JIG sẽ được đưa về trạng thái tháo hàng an toàn; MARKING luôn OFF khi FAIL.");
        faultDialog.Owner = Application.Current?.MainWindow;
        faultDialog.ShowDialog();

        // Người vận hành đã xác nhận popup: sản phẩm được tính là FAIL một lần.
        if (!_productDetectedThisCycle)
            _cycleStartedAt = DateTime.Now;
        RecordCompletedProduct(false, primaryName);

        // V13.0: sau xác nhận FAIL chỉ Relay 1 JIG được pulse. Relay 2 MARKING
        // luôn OFF; Probe không bao giờ được phép đi vào handler này.
        _sound.SetWiringFaultAlarm(false);

        try
        {
            State = "ĐANG MỞ JIG HÀNG LỖI";
            await _engine.EjectFaultProductAsync();
            AddLog($"Lỗi đã xác nhận: R1 JIG pulse đúng 1 lần ({_productionSettings.Relay1JigPulseMs} ms) rồi OFF; R2 MARKING luôn OFF.");

            _waitForFaultProductRemoval = true;
            await _board.StartScanAsync(BoardScanMode.Production);
            State = "LỖI - CHỜ THÁO TOÀN BỘ SẢN PHẨM";
        }
        catch (Exception ex)
        {
            _waitForFaultProductRemoval = false;
            State = "LỖI THIẾT BỊ - JIG KHÔNG MỞ";
            AddLog($"Không thể eject/restart scan sau lỗi: {ex.Message}");
            MessageBox.Show(
                $"Không thể mở JIG hoặc khởi động lại scan sau lỗi.\nRelay 2 MARKING vẫn OFF.\n\n{ex.Message}",
                "Lỗi thiết bị",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string DescribeIoForPopup(int io)
    {
        IReadOnlyList<PinRecord> pins = FindPinsByIo(io);
        if (pins.Count == 0)
            return $"I/O {io} (không có map trong THT)";

        string mapped = string.Join(
            " / ",
            pins.Take(3).Select(pin =>
                $"I/O {io} - {EmptyAsDash(pin.Connector)} - chân {EmptyAsDash(pin.PinNumber)} - dây {EmptyAsDash(pin.WireName)}"));

        return mapped;
    }

    private string DescribeIoCompact(int io)
    {
        PinRecord? pin = FindPinByIo(io);
        if (pin is null)
            return $"IO {io}";

        string connector = EmptyAsDash(pin.Connector);
        string pinNumber = EmptyAsDash(pin.PinNumber);
        return $"IO {io} / {connector}-PIN{pinNumber}";
    }

    private IReadOnlyList<FaultDetail> CaptureFaultDetails()
    {
        if (_board is IFirmwareProtocolBoard firmware && firmware.UsesFirmwareCycleResult)
        {
            FaultDetail[] firmwareDetails = Faults
                .Where(row => row.Kind is FaultKind.Open or FaultKind.WrongWiring or FaultKind.Short)
                .Select(row => EnrichFaultDetail(row.ToFaultDetail()))
                .ToArray();

            return firmwareDetails.Length > 0
                ? firmwareDetails
                : BuildUartSnapshotFaultDetails();
        }

        var details = _engine.WiringFaults
            .Select(pair =>
            {
                var detail = new FaultDetail
                {
                    Type = pair.FaultType,
                    ExpectedSourceIo = pair.ExpectedSourceIo,
                    ExpectedTargetIo = pair.ExpectedTargetIo,
                    ActualSourceIo = pair.SourceIo,
                    ActualTargetIo = pair.TargetIo,
                    RelatedIos = new[] { pair.SourceIo, pair.TargetIo },
                    Message = pair.Reason
                };

                PinRecord? fromPin = FindPinByIo(pair.ExpectedSourceIo ?? pair.SourceIo);
                PinRecord? toPin = FindPinByIo(pair.ExpectedTargetIo ?? pair.TargetIo);
                PinRecord? actualFromPin = FindPinByIo(pair.SourceIo);
                PinRecord? actualToPin = FindPinByIo(pair.TargetIo);
                if (fromPin is not null)
                {
                    detail.ConnectorFrom = fromPin.Connector;
                    detail.PinFrom = fromPin.PinNumber;
                    detail.WireName = fromPin.WireName;
                    detail.WireColor = fromPin.Color;
                }
                if (toPin is not null)
                {
                    detail.ConnectorTo = toPin.Connector;
                    detail.PinTo = toPin.PinNumber;
                }
                if (actualFromPin is not null)
                {
                    detail.ActualConnectorFrom = actualFromPin.Connector;
                    detail.ActualPinFrom = actualFromPin.PinNumber;
                }
                if (actualToPin is not null)
                {
                    detail.ActualConnectorTo = actualToPin.Connector;
                    detail.ActualPinTo = actualToPin.PinNumber;
                }
                return detail;
            })
            .ToList();

        if (!MasterApproved && IsMasterBadPhase)
        {
            // Master BAD giữ semantics evidence riêng, không phải product FAIL.
            details.AddRange(_engine.BuildRows()
                .Where(row => row.ProductFaultType != ProductFaultType.None &&
                              row.ProductFaultType != ProductFaultType.SystemDeviceError)
                .Select(row => EnrichFaultDetail(row.ToFaultDetail())));
        }
        else
        {
            details.AddRange(_engine.BuildConfirmedOpenFaults().Select(EnrichFaultDetail));
        }

        foreach (ResistanceResult resistance in Resistance.Where(item => !item.Passed))
            details.Add(CreateResistanceFaultDetail(resistance));

        return details
            .GroupBy(fault => new
            {
                fault.Type,
                fault.ExpectedSourceIo,
                fault.ExpectedTargetIo,
                fault.ActualSourceIo,
                fault.ActualTargetIo,
                fault.WireName,
                fault.MeasuredResistance
            })
            .Select(group => group.First())
            .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
            .ToArray();
    }

    private FaultDetail EnrichFaultDetail(FaultDetail detail)
    {
        int? fromIo = detail.ExpectedSourceIo
            ?? detail.ActualSourceIo
            ?? detail.RelatedIos.ElementAtOrDefault(0);
        int? toIo = detail.ExpectedTargetIo
            ?? detail.ActualTargetIo
            ?? detail.RelatedIos.ElementAtOrDefault(1);

        if (fromIo is int source && source > 0)
        {
            PinRecord? fromPin = FindPinByIo(source);
            if (fromPin is not null)
            {
                detail.ConnectorFrom = fromPin.Connector;
                detail.PinFrom = fromPin.PinNumber;
                if (string.IsNullOrWhiteSpace(detail.WireName))
                    detail.WireName = fromPin.WireName;
                if (string.IsNullOrWhiteSpace(detail.WireColor))
                    detail.WireColor = fromPin.Color;
            }
        }

        if (toIo is int target && target > 0)
        {
            PinRecord? toPin = FindPinByIo(target);
            if (toPin is not null)
            {
                detail.ConnectorTo = toPin.Connector;
                detail.PinTo = toPin.PinNumber;
            }
        }

        if (detail.ActualSourceIo is int actualSource)
        {
            PinRecord? actualFromPin = FindPinByIo(actualSource);
            if (actualFromPin is not null)
            {
                detail.ActualConnectorFrom = actualFromPin.Connector;
                detail.ActualPinFrom = actualFromPin.PinNumber;
            }
        }

        if (detail.ActualTargetIo is int actualTarget)
        {
            PinRecord? actualToPin = FindPinByIo(actualTarget);
            if (actualToPin is not null)
            {
                detail.ActualConnectorTo = actualToPin.Connector;
                detail.ActualPinTo = actualToPin.PinNumber;
            }
        }

        return detail;
    }

    private static FaultDetail CreateResistanceFaultDetail(ResistanceResult resistance) => new()
    {
        Type = ProductFaultType.ResistanceOutOfRange,
        MeasuredResistance = resistance.ValueOhm,
        ResistanceMin = resistance.MinOhm,
        ResistanceMax = resistance.MaxOhm,
        WireName = resistance.Name,
        Message = $"{resistance.Name}: {resistance.Display}; giới hạn {resistance.MinOhm:0.###}–{resistance.MaxOhm:0.###} Ω"
    };

    private FaultDetail? GetVisiblePrimaryFault()
    {
        bool stateIsFault =
            State.Contains("LỖI", StringComparison.OrdinalIgnoreCase) ||
            State.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
            State.Contains("CHẬP", StringComparison.OrdinalIgnoreCase) ||
            State.Contains("SAI KẾT NỐI", StringComparison.OrdinalIgnoreCase) ||
            State.Contains("HỞ MẠCH", StringComparison.OrdinalIgnoreCase) ||
            State.Contains("ĐẤU SAI", StringComparison.OrdinalIgnoreCase) ||
            State.Contains("DÂY CHƯA", StringComparison.OrdinalIgnoreCase) ||
            State.Contains("ĐIỆN TRỞ KHÔNG ĐẠT", StringComparison.OrdinalIgnoreCase);

        if (!_productDetectedThisCycle && !_waitForFaultProductRemoval && !stateIsFault)
            return null;

        return CaptureFaultDetails().FirstOrDefault();
    }

    private static string EmptyAsDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private void ResetMasterGateForModel()
    {
        MasterApproved = false;
        _masterGoodVerified = false;
        _masterBadVerified = false;
        _masterFaultCollectionLocked = false;
        _masterDetectedFaultKeys.Clear();
        _masterDetectedFaultDetails.Clear();
        MasterFaults.Clear();
        _masterRequiredFaultCount = _model is null
            ? Math.Clamp(_productionSettings.MasterFaultRequiredCount, 1, 99)
            : ProductionConfigService.GetMasterFaultRequiredCount(_productionSettings, _model);
        Interlocked.Exchange(ref _masterPostStarted, 0);
        Interlocked.Exchange(ref _masterEjectStarted, 0);
        Interlocked.Exchange(ref _masterBadCollectNotBeforeUtcTicks, 0);
        MasterState = MasterSequenceState.WaitingGoodMaster;
        MasterStatus = $"CẦN MASTER ĐẠT → MASTER SAI DÂY ({_masterRequiredFaultCount} LỖI)";
        State = "ĐANG CHỜ LẮP MẪU MASTER ĐẠT";
        RaiseMasterState();
    }

    private void RaiseMasterState()
    {
        Raise(nameof(MasterRequiredFaultCount));
        Raise(nameof(MasterDetectedFaultCount));
        Raise(nameof(MasterProgressText));
        Raise(nameof(NetworkProgress));
        Raise(nameof(IsMasterBadPhase));
        Raise(nameof(IsMasterSequenceActive));
        Raise(nameof(ProductionEnabled));
        RaiseActiveFault();
    }

    private async Task StartAutomaticMasterSequenceAsync()
    {
        if (_model is null || MasterApproved)
            return;

        if (!_board.IsConnected)
            await InitializeHardwareAsync();

        if (!_board.IsConnected)
        {
            MasterStatus = "BO CHƯA KẾT NỐI - KHÔNG THỂ KIỂM TRA MASTER";
            State = "LỖI THIẾT BỊ - MASTER BỊ KHÓA";
            return;
        }

        _productionSettings.AutoMasterSequence = true;
        _cycleActive = false;
        _productDetectedThisCycle = false;
        Interlocked.Exchange(ref _resultRecordedThisCycle, 0);
        _waitForProductRelease = false;
        _waitForFaultProductRemoval = false;
        _masterGoodVerified = false;
        _masterBadVerified = false;
        _masterFaultCollectionLocked = false;
        _masterDetectedFaultKeys.Clear();
        _masterDetectedFaultDetails.Clear();
        MasterFaults.Clear();
        Interlocked.Exchange(ref _masterPostStarted, 0);
        Interlocked.Exchange(ref _masterEjectStarted, 0);
        Interlocked.Exchange(ref _masterBadCollectNotBeforeUtcTicks, 0);

        _engine.SetFrameProcessingEnabled(true);
        ResetEngineWithoutChangedReentry();
        RefreshFaults();

        MasterState = MasterSequenceState.WaitingGoodMaster;
        State = "ĐANG CHỜ LẮP MẪU MASTER ĐẠT";
        MasterStatus = "MASTER GOOD START • TỰ ĐỘNG KIỂM TRA KHI LẮP MẪU";
        AddLog("MASTER GOOD START - production gate LOCKED; không cộng LOT/Pass/Fail.");

        if (!_board.IsScanning)
            await _board.StartScanAsync(BoardScanMode.Production, CurrentCycleToken());

        RaiseMasterState();
    }

    private void HandleMasterEngineChanged(long generation)
    {
        if (!IsRuntimeContext(RuntimeMode.Production, generation) || MasterApproved)
            return;

        InvokeUi(() =>
        {
            if (!IsRuntimeContext(RuntimeMode.Production, generation) || MasterApproved)
                return;

            RefreshFaults();

            switch (MasterState)
            {
                case MasterSequenceState.WaitingGoodMaster:
                    if (_engine.HasProductActivity)
                    {
                        MasterState = MasterSequenceState.TestingGoodMaster;
                        State = "ĐANG KIỂM TRA MẪU MASTER ĐẠT";
                        MasterStatus = "ĐANG KIỂM TRA TOÀN BỘ CONTINUITY / ĐIỆN TRỞ";
                        AddLog("MASTER GOOD: phát hiện mẫu, bắt đầu kiểm tra tự động.");
                    }
                    break;

                case MasterSequenceState.TestingGoodMaster:
                    if (_engine.IsProductReleased)
                    {
                        Interlocked.Exchange(ref _masterPostStarted, 0);
                        MasterState = MasterSequenceState.WaitingGoodMaster;
                        ResetEngineWithoutChangedReentry();
                        State = "ĐANG CHỜ LẮP MẪU MASTER ĐẠT";
                        MasterStatus = "MASTER ĐẠT CHƯA PASS - LẮP LẠI MẪU ĐẠT";
                        AddLog("MASTER GOOD chưa PASS và đã tháo; giữ gate LOCKED, chờ kiểm tra lại.");
                        break;
                    }

                    if (_engine.HasWiringFault)
                    {
                        State = "MASTER ĐẠT - FAIL";
                        MasterStatus = "MẪU MASTER ĐẠT ĐANG CÓ LỖI DÂY - KIỂM TRA / THÁO MẪU";
                        // Không alarm/eject theo logic Product FAIL. Good master chỉ được eject sau PASS thật.
                        _sound.SetWiringFaultAlarm(false);
                        break;
                    }

                    if (_engine.ContinuityPassed &&
                        Interlocked.CompareExchange(ref _masterPostStarted, 1, 0) == 0)
                    {
                        _ = CompleteGoodMasterAsync(generation);
                    }
                    break;

                case MasterSequenceState.EjectingGoodMaster:
                    if (_engine.IsProductReleased)
                        TransitionToBadMaster();
                    break;

                case MasterSequenceState.WaitingBadMaster:
                    if (_engine.HasProductActivity)
                    {
                        MasterState = MasterSequenceState.TestingBadMaster;
                        State = "ĐANG KIỂM TRA MẪU SAI DÂY";
                        MasterStatus = $"ĐANG XÁC NHẬN LỖI MASTER: {MasterDetectedFaultCount}/{MasterRequiredFaultCount}";
                        Interlocked.Exchange(
                            ref _masterBadCollectNotBeforeUtcTicks,
                            DateTime.UtcNow.AddMilliseconds(MasterBadSettleMs).Ticks);
                        AddLog($"MASTER BAD START - cần {MasterRequiredFaultCount} fault dây duy nhất.");
                        _ = CollectMasterFaultsAfterSettleAsync(generation);
                    }
                    break;

                case MasterSequenceState.TestingBadMaster:
                    if (_engine.IsProductReleased)
                    {
                        // Không xóa HashSet 1/N: cùng mẫu có thể mất contact tạm thời. Gate chỉ mở ở N/N.
                        Interlocked.Exchange(ref _masterBadCollectNotBeforeUtcTicks, 0);
                        MasterState = MasterSequenceState.WaitingBadMaster;
                        ResetEngineWithoutChangedReentry();
                        State = "ĐANG CHỜ LẮP MẪU SAI DÂY";
                        MasterStatus = MasterDetectedFaultCount == 0
                            ? $"CHỜ MASTER LỖI • 0/{MasterRequiredFaultCount}"
                            : $"MASTER LỖI CHƯA ĐỦ • {MasterDetectedFaultCount}/{MasterRequiredFaultCount} • CHỜ LỖI CÒN THIẾU";
                        AddLog($"MASTER BAD released khi mới {MasterDetectedFaultCount}/{MasterRequiredFaultCount}; không mở Production.");
                        break;
                    }

                    CollectCurrentMasterFaults(generation);
                    break;

                case MasterSequenceState.EjectingBadMaster:
                    if (_engine.IsProductReleased)
                        CompleteMasterValidation();
                    break;
            }
        });
    }

    private async Task CollectMasterFaultsAfterSettleAsync(long generation)
    {
        try
        {
            await Task.Delay(MasterBadSettleMs, CurrentCycleToken());
        }
        catch (OperationCanceledException)
        {
            return;
        }

        InvokeUi(() => CollectCurrentMasterFaults(generation));
    }

    private void CollectCurrentMasterFaults(long generation)
    {
        if (!IsRuntimeContext(RuntimeMode.Production, generation) ||
            MasterApproved ||
            MasterState != MasterSequenceState.TestingBadMaster ||
            _masterFaultCollectionLocked)
        {
            return;
        }

        long notBeforeTicks = Interlocked.Read(ref _masterBadCollectNotBeforeUtcTicks);
        if (notBeforeTicks > 0 && DateTime.UtcNow.Ticks < notBeforeTicks)
            return;

        FaultDetail[] candidates = CaptureFaultDetails()
            .Where(fault => fault.Type is
                ProductFaultType.OpenCircuit or
                ProductFaultType.WrongWiring or
                ProductFaultType.ShortCircuit)
            .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
            .ThenBy(fault => fault.ExpectedSourceIo ?? fault.ActualSourceIo ?? 0)
            .ThenBy(fault => fault.ExpectedTargetIo ?? fault.ActualTargetIo ?? 0)
            .ToArray();

        foreach (FaultDetail fault in candidates)
        {
            if (_masterFaultCollectionLocked)
                break;

            MasterFaultKey key = MasterFaultKey.From(fault);
            if (!_masterDetectedFaultKeys.Add(key))
            {
                // V12.10.2: cùng cạnh điện có thể được engine mô tả trước là
                // Short rồi frame sau có đủ Expected* để mô tả WrongWiring rõ hơn.
                // Không tăng bộ đếm, chỉ nâng chất lượng dòng đang hiển thị.
                if (_masterDetectedFaultDetails.TryGetValue(key, out FaultDetail? existing) &&
                    existing is not null &&
                    ShouldReplaceMasterFaultDetail(existing, fault))
                {
                    _masterDetectedFaultDetails[key] = fault;
                    RebuildMasterFaultDisplayRows();
                    SynchronizeFaultRows(BuildMasterFaultGridRows());
                }

                continue;
            }

            int number = _masterDetectedFaultKeys.Count;
            _masterDetectedFaultDetails[key] = fault;
            RebuildMasterFaultDisplayRows();
            SynchronizeFaultRows(BuildMasterFaultGridRows());
            MasterStatus = $"ĐANG KIỂM TRA MẪU SAI DÂY • LỖI MASTER: {number}/{MasterRequiredFaultCount}";
            State = "ĐANG KIỂM TRA MẪU SAI DÂY";
            AddLog(
                $"MASTER BAD FAULT {number}/{MasterRequiredFaultCount} " +
                $"{FaultTypeCatalog.Code(fault.Type)} | {fault.Summary}");
            RaiseMasterState();

            if (number >= MasterRequiredFaultCount)
            {
                _masterFaultCollectionLocked = true;
                _masterBadVerified = true;
                _ = EjectValidatedBadMasterAsync(generation);
                break;
            }
        }
    }

    /// <summary>
    /// V12.10.1: FaultGrid là vùng hiển thị chính cho cả MasterBad và Production.
    /// MasterBad chỉ đưa các fault đã được HashSet xác nhận unique vào grid; vì vậy
    /// cùng IO/fault lặp 50-100 frame không sinh thêm dòng.
    /// </summary>
    private static bool ShouldReplaceMasterFaultDetail(FaultDetail current, FaultDetail candidate)
    {
        static int Quality(FaultDetail fault)
        {
            int score = 0;
            if (fault.ExpectedSourceIo is int expectedSource && expectedSource > 0) score += 4;
            if (fault.ExpectedTargetIo is int expectedTarget && expectedTarget > 0) score += 4;
            if (fault.ActualSourceIo is int actualSource && actualSource > 0) score += 2;
            if (fault.ActualTargetIo is int actualTarget && actualTarget > 0) score += 2;
            if (!string.IsNullOrWhiteSpace(fault.ConnectorFrom)) score += 1;
            if (!string.IsNullOrWhiteSpace(fault.PinFrom)) score += 1;
            if (!string.IsNullOrWhiteSpace(fault.WireName)) score += 1;

            // Khi cùng một cạnh bị phân loại hai cách, WrongWiring có Expected*
            // hữu ích hơn cho người vận hành so với nhãn Short chung chung.
            if (fault.Type == ProductFaultType.WrongWiring) score += 1;
            return score;
        }

        return Quality(candidate) > Quality(current);
    }

    private void RebuildMasterFaultDisplayRows()
    {
        MasterFaults.Clear();
        int number = 0;
        foreach (FaultDetail detail in _masterDetectedFaultDetails.Values)
        {
            number++;
            MasterFaults.Add(BuildMasterFaultDisplayRow(number, detail));
        }
    }

    private IReadOnlyList<FaultRow> BuildMasterFaultGridRows()
    {
        return _masterDetectedFaultDetails.Values
            .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
            .ThenBy(fault => fault.ActualSourceIo ?? fault.ExpectedSourceIo ?? fault.RelatedIos.FirstOrDefault())
            .ThenBy(fault => fault.ActualTargetIo ?? fault.ExpectedTargetIo ?? 0)
            .Select(BuildMasterFaultGridRow)
            .ToArray();
    }

    private FaultRow BuildMasterFaultGridRow(FaultDetail fault)
    {
        int primaryIo = fault.ExpectedSourceIo
            ?? fault.ActualSourceIo
            ?? fault.ExpectedTargetIo
            ?? fault.ActualTargetIo
            ?? fault.RelatedIos.FirstOrDefault();

        PinRecord? pin = primaryIo > 0 ? FindPinByIo(primaryIo) : null;

        string status = fault.Type switch
        {
            ProductFaultType.WrongWiring =>
                $"Tiêu chuẩn: {DescribePair(fault.ExpectedSourceIo, fault.ExpectedTargetIo, "→")} | " +
                $"Thực tế: {DescribePair(fault.ActualSourceIo, fault.ActualTargetIo, "→")}",
            ProductFaultType.ShortCircuit =>
                $"Chập mạch: {DescribeFaultIos(fault, "↔")}",
            ProductFaultType.OpenCircuit =>
                $"Chưa kết nối: {DescribeFaultIos(fault, "↔")}",
            _ => fault.Summary
        };

        FaultKind kind = fault.Type switch
        {
            ProductFaultType.OpenCircuit => FaultKind.Open,
            ProductFaultType.WrongWiring => FaultKind.WrongWiring,
            ProductFaultType.ShortCircuit => FaultKind.Short,
            ProductFaultType.ResistanceOutOfRange => FaultKind.Resistance,
            _ => FaultKind.Info
        };

        return new FaultRow
        {
            Kind = kind,
            ProductFaultType = fault.Type,
            FaultType = FaultTypeCatalog.DisplayName(fault.Type),
            Io = primaryIo,
            ExpectedSourceIo = fault.ExpectedSourceIo,
            ExpectedTargetIo = fault.ExpectedTargetIo,
            ActualSourceIo = fault.ActualSourceIo,
            ActualTargetIo = fault.ActualTargetIo,
            RelatedIos = fault.RelatedIos,
            Connector = pin?.Connector ?? fault.ConnectorFrom,
            Pin = pin?.PinNumber ?? fault.PinFrom,
            WireName = pin?.WireName ?? fault.WireName,
            Splice = pin?.SpliceName ?? string.Empty,
            Section = pin?.Section ?? string.Empty,
            Color = pin?.Color ?? fault.WireColor,
            Status = status
        };
    }

    private string DescribePair(int? source, int? target, string separator)
    {
        if (source is int s && target is int t)
            return $"{DescribeIoCompact(s)} {separator} {DescribeIoCompact(t)}";
        return "—";
    }

    private string DescribeFaultIos(FaultDetail fault, string separator)
    {
        int[] ios = fault.RelatedIos
            .Concat(new[]
            {
                fault.ExpectedSourceIo ?? 0,
                fault.ExpectedTargetIo ?? 0,
                fault.ActualSourceIo ?? 0,
                fault.ActualTargetIo ?? 0
            })
            .Where(io => io > 0)
            .Distinct()
            .Take(6)
            .ToArray();

        return ios.Length == 0
            ? fault.Summary
            : string.Join($" {separator} ", ios.Select(DescribeIoCompact));
    }

    private MasterFaultDisplayRow BuildMasterFaultDisplayRow(int number, FaultDetail fault)
    {
        string detail = string.Empty;
        string expected = string.Empty;
        string actual = string.Empty;

        if (fault.Type == ProductFaultType.WrongWiring)
        {
            if (fault.ExpectedSourceIo is int es && fault.ExpectedTargetIo is int et)
                expected = $"Tiêu chuẩn: {DescribeIoCompact(es)}  →  {DescribeIoCompact(et)}";
            if (fault.ActualSourceIo is int actualSource && fault.ActualTargetIo is int actualTarget)
                actual = $"Thực tế: {DescribeIoCompact(actualSource)}  →  {DescribeIoCompact(actualTarget)}";
        }
        else
        {
            int[] ios = fault.RelatedIos
                .Concat(new[] { fault.ExpectedSourceIo ?? 0, fault.ExpectedTargetIo ?? 0, fault.ActualSourceIo ?? 0, fault.ActualTargetIo ?? 0 })
                .Where(io => io > 0)
                .Distinct()
                .Take(6)
                .ToArray();

            if (ios.Length > 0)
                detail = string.Join("  ↔  ", ios.Select(DescribeIoCompact));
            else
                detail = fault.Summary;
        }

        return new MasterFaultDisplayRow
        {
            Number = number,
            RequiredCount = MasterRequiredFaultCount,
            Title = FaultTypeCatalog.DisplayName(fault.Type),
            Detail = detail,
            Expected = expected,
            Actual = actual
        };
    }

    private async Task CompleteGoodMasterAsync(long generation)
    {
        if (_model is null ||
            !IsRuntimeContext(RuntimeMode.Production, generation) ||
            MasterState != MasterSequenceState.TestingGoodMaster)
        {
            Interlocked.Exchange(ref _masterPostStarted, 0);
            return;
        }

        CancellationToken ct = CurrentCycleToken();
        try
        {
            Resistance.Clear();
            bool resistancePassed = true;

            if (_model.ResistanceSteps.Count > 0)
            {
                await EnsureKeysightConnectedAsync();
                List<ResistanceResult> results = await _engine.MeasureResistanceAsync(ct);
                foreach (ResistanceResult result in results)
                    Resistance.Add(result);
                resistancePassed = Resistance.Count == _model.ResistanceSteps.Count && Resistance.All(item => item.Passed);
            }

            if (!resistancePassed)
            {
                State = "MASTER ĐẠT - FAIL";
                MasterStatus = "MASTER ĐẠT: ĐIỆN TRỞ KHÔNG ĐẠT - KHÔNG MỞ PRODUCTION";
                AddLog("MASTER GOOD FAIL - resistance out of range; không eject tự động.");
                return;
            }

            await WaitForProbeRelayInterlockAsync(ct);
            if (!_engine.ContinuityPassed || _engine.HasWiringFault)
            {
                State = "MASTER ĐẠT - FAIL";
                MasterStatus = "MASTER ĐẠT MẤT ĐIỀU KIỆN PASS - KIỂM TRA LẠI";
                AddLog("MASTER GOOD FAIL - continuity không còn PASS trước relay.");
                return;
            }

            bool ok = await _engine.CompletePassAsync(
                Resistance,
                onPassStarted: () =>
                {
                    State = "MASTER ĐẠT - PASS\nĐANG ĐẨY MẪU RA";
                    MasterStatus = "MASTER GOOD PASS • RELAY JIG TỰ ĐỘNG";
                    _sound.PlayTestOk();
                },
                markingEnabled: false,
                ct: ct);

            if (!ok)
            {
                State = "MASTER ĐẠT - FAIL";
                MasterStatus = "MASTER ĐẠT KHÔNG HOÀN THÀNH PASS - KIỂM TRA LẠI";
                AddLog("MASTER GOOD FAIL - CompletePassAsync trả false.");
                return;
            }

            _masterGoodVerified = true;
            MasterState = MasterSequenceState.EjectingGoodMaster;
            State = "MASTER ĐẠT - PASS\nĐANG ĐẨY MẪU RA";
            MasterStatus = "MASTER GOOD PASS / EJECT - CHỜ MẪU RA KHỎI JIG";
            AddLog("MASTER GOOD PASS");
            AddLog("MASTER GOOD EJECT - Relay 1 JIG; không MARKING, không cộng sản lượng.");

            // CompletePass đã STOP/RESET transport. Reset nội bộ không được phát callback
            // EjectingGoodMaster giả; chỉ frame scan thật sau restart mới xác nhận RELEASE.
            ResetEngineWithoutChangedReentry();
            _engine.SetFrameProcessingEnabled(true);
            await _board.StartScanAsync(BoardScanMode.Production, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            State = "LỖI THIẾT BỊ - MASTER";
            MasterStatus = $"LỖI KIỂM TRA MASTER ĐẠT: {ex.Message}";
            AddLog(MasterStatus);
        }
        finally
        {
            // Giữ latch cho tới khi MASTER GOOD được tháo/nhả. Nếu resistance hoặc
            // continuity không đạt, không được tự đo/lặp PASS liên tục theo từng frame.
            // Nhánh IsProductReleased ở state TestingGoodMaster sẽ reset latch về 0.
            SelectedOperationTabIndex = 0;
            RaiseMasterState();
        }
    }

    private void TransitionToBadMaster()
    {
        if (!_masterGoodVerified || MasterApproved)
            return;

        // Đổi state trước khi reset để tuyệt đối không thể tái nhập
        // EjectingGoodMaster -> TransitionToBadMaster -> Reset -> Changed -> ...
        MasterState = MasterSequenceState.WaitingBadMaster;
        ResetEngineWithoutChangedReentry();
        _masterDetectedFaultKeys.Clear();
        _masterDetectedFaultDetails.Clear();
        MasterFaults.Clear();
        _masterFaultCollectionLocked = false;
        _masterBadVerified = false;
        Interlocked.Exchange(ref _masterEjectStarted, 0);
        Interlocked.Exchange(ref _masterBadCollectNotBeforeUtcTicks, 0);

        State = "ĐANG CHỜ LẮP MẪU SAI DÂY";
        MasterStatus = $"MASTER GOOD OK • CHỜ MASTER LỖI • 0/{MasterRequiredFaultCount}";
        AddLog("MASTER GOOD đã tháo khỏi JIG. Chuyển sang MASTER BAD tự động.");
        RaiseMasterState();
    }

    private async Task EjectValidatedBadMasterAsync(long generation)
    {
        if (_model is null ||
            !IsRuntimeContext(RuntimeMode.Production, generation) ||
            MasterDetectedFaultCount < MasterRequiredFaultCount ||
            Interlocked.CompareExchange(ref _masterEjectStarted, 1, 0) != 0)
        {
            return;
        }

        CancellationToken ct = CurrentCycleToken();
        try
        {
            _masterFaultCollectionLocked = true;
            MasterState = MasterSequenceState.EjectingBadMaster;
            State = $"MASTER LỖI OK\n{MasterDetectedFaultCount}/{MasterRequiredFaultCount}\nĐANG ĐẨY MẪU RA";
            MasterStatus = $"MASTER BAD PASS • ĐỦ {MasterDetectedFaultCount}/{MasterRequiredFaultCount} LỖI DUY NHẤT";
            _sound.SetWiringFaultAlarm(false);
            AddLog($"MASTER BAD PASS - đủ {MasterDetectedFaultCount}/{MasterRequiredFaultCount} fault duy nhất.");

            // MASTER BAD fault là EXPECTED evidence: chỉ eject JIG sau N/N, không dùng Product FAIL behavior.
            await _engine.EjectMasterSampleAsync(ct);
            AddLog("MASTER BAD EJECT - Relay 1 JIG tự động; không tăng FAIL/LOT.");

            // Chờ frame thật xác nhận MASTER BAD đã rời jig; Reset không được
            // tự phát Changed và hoàn tất Master Gate ngay trong cùng call stack.
            ResetEngineWithoutChangedReentry();
            _engine.SetFrameProcessingEnabled(true);
            await _board.StartScanAsync(BoardScanMode.Production, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            State = "LỖI THIẾT BỊ - MASTER";
            MasterStatus = $"LỖI EJECT MASTER: {ex.Message}";
            AddLog(MasterStatus);
            Interlocked.Exchange(ref _masterEjectStarted, 0);
        }
        finally
        {
            RaiseMasterState();
        }
    }

    private void CompleteMasterValidation()
    {
        if (!_masterGoodVerified || !_masterBadVerified || MasterDetectedFaultCount < MasterRequiredFaultCount)
            return;

        MasterApproved = true;
        MasterState = MasterSequenceState.Completed;
        _masterFaultCollectionLocked = true;
        _cycleActive = true;
        _productDetectedThisCycle = false;
        Interlocked.Exchange(ref _resultRecordedThisCycle, 0);
        _waitForProductRelease = false;
        _waitForFaultProductRemoval = false;
        Interlocked.Exchange(ref _postContinuityStarted, 0);
        Interlocked.Exchange(ref _wiringFaultHandlingStarted, 0);
        Interlocked.Exchange(ref _masterPostStarted, 0);
        Interlocked.Exchange(ref _masterEjectStarted, 0);
        _sound.SetWiringFaultAlarm(false);
        ResetEngineWithoutChangedReentry();
        _engine.SetFrameProcessingEnabled(true);
        RefreshFaults();

        State = "SẴN SÀNG SẢN XUẤT";
        MasterStatus = "MASTER HOÀN TẤT • PRODUCTION ENABLED";
        AddLog("MASTER VALIDATION COMPLETED - MASTER GATE PASS, ProductionEnabled=true.");
        RaiseMasterState();
    }

    private async Task MeasureResistanceAsync()
    {
        // Không cho đo thủ công trước khi continuity PASS.
        // Command được giữ lại chỉ để tương thích với XAML/phiên bản cũ.
        if (!_cycleActive || !_engine.ContinuityPassed)
        {
            AddLog("Điện trở chỉ được đo tự động sau khi toàn bộ mạng I/O PASS.");
            return;
        }

        if (Interlocked.CompareExchange(ref _postContinuityStarted, 1, 0) == 0)
            await RunAutomaticPostContinuityAsync();
    }

    private async Task CompleteTestAsync()
    {
        // PASS/relay cũng được tự động hóa để không thể xác nhận PASS trước điện trở.
        if (!_cycleActive || !_engine.ContinuityPassed)
        {
            AddLog("Chưa đủ điều kiện PASS toàn bộ mạng I/O.");
            return;
        }

        if (Interlocked.CompareExchange(ref _postContinuityStarted, 1, 0) == 0)
            await RunAutomaticPostContinuityAsync();
    }

    private async Task RunAutomaticPostContinuityAsync()
    {
        if (!_cycleActive || _model is null)
            return;

        CancellationToken ct = CurrentCycleToken();
        if (ct.IsCancellationRequested)
            return;

        try
        {
            ct.ThrowIfCancellationRequested();
            AddLog("Toàn bộ mạng I/O đã đạt theo model THT.");
            // Không tạo trạng thái trung gian trên bảng lớn. Người vận hành chỉ
            // thấy CHỜ LẮP -> ĐANG KIỂM TRA -> PASS.

            if (_model.ResistanceSteps.Count > 0)
            {
                // Vẫn giữ chữ ĐANG KIỂM TRA trong lúc đo điện trở.
                State = "ĐANG KIỂM TRA...";
                if (_productionSettings.PageDelay > 0)
                    await Task.Delay(Math.Clamp(_productionSettings.PageDelay, 0, 5000), ct);
                SelectedOperationTabIndex = 1;
                Resistance.Clear();

                await EnsureKeysightConnectedAsync();

                List<ResistanceResult> results =
                    await _engine.MeasureResistanceAsync(ct);

                foreach (ResistanceResult result in results)
                    Resistance.Add(result);

                AddLog(
                    $"Hoàn thành {Resistance.Count}/{_model.ResistanceSteps.Count} " +
                    "phép đo điện trở.");

                if (Resistance.Count != _model.ResistanceSteps.Count ||
                    Resistance.Any(x => !x.Passed))
                {
                    _cycleActive = false;
                    RecordCompletedProduct(false, FaultTypeCatalog.DisplayName(ProductFaultType.ResistanceOutOfRange));
                    State = FaultTypeCatalog.DisplayName(ProductFaultType.ResistanceOutOfRange);
                    RaiseTestStatistics();
                    AddLog("Điện trở không đạt. Không chạy relay PASS.");

                    FaultDetail[] resistanceFaults = Resistance
                        .Where(item => !item.Passed)
                        .Select(CreateResistanceFaultDetail)
                        .ToArray();
                    var faultDialog = new JBZUniversalTester.Views.FaultConfirmationWindow(
                        resistanceFaults,
                        "Không chạy relay PASS. Hãy xử lý sản phẩm không đạt theo quy trình vận hành.");
                    faultDialog.Owner = Application.Current?.MainWindow;
                    faultDialog.ShowDialog();
                    return;
                }

                AddLog("Tất cả phép đo điện trở PASS.");
            }
            else
            {
                AddLog("Model không yêu cầu đo điện trở - bỏ qua Keysight.");
            }

            // V12.4: PASS + DINGDONG bắt đầu cùng mốc Relay 2 MARKING.
            // Engine MARKING trước; chỉ sau khi MARKING hoàn tất mới pulse
            // Relay 1 để mở/tháo JIG.
            // Tuyệt đối không cho relay PASS chạy trong lúc/Ngay sau khi que
            // dò GND còn tạo tín hiệu. Sau lockout phải xác nhận continuity
            // vẫn PASS và không có wiring fault mới được MARKING/JIG.
            await WaitForProbeRelayInterlockAsync(ct);
            if (!_cycleActive || !_engine.ContinuityPassed || _engine.HasWiringFault)
            {
                Interlocked.Exchange(ref _postContinuityStarted, 0);
                State = "ĐANG KIỂM TRA...";
                AddLog("Đã hủy chuỗi relay PASS vì trạng thái I/O thay đổi sau đầu dò.");
                return;
            }

            bool passUiTriggered = false;
            bool ok = await _engine.CompletePassAsync(
                Resistance,
                onPassStarted: () =>
                {
                    if (passUiTriggered)
                        return;

                    passUiTriggered = true;
                    State = "PASS";
                    _sound.SetWiringFaultAlarm(false);
                    _sound.PlayTestOk();
                    AddLog("PASS - Relay 2 MARKING bắt đầu; sau MARKING mới Relay 1 mở JIG.");
                },
                ct: ct);

            if (!ok)
            {
                _cycleActive = false;
                RecordCompletedProduct(false, "CHƯA ĐẠT");
                State = "CHƯA ĐẠT";
                AddLog("Sản phẩm chưa đạt điều kiện PASS cuối cùng.");
                RaiseTestStatistics();
                return;
            }

            RecordCompletedProduct(true, "PASS");
            AddLog("Chuỗi PASS hoàn tất: Relay 2 MARKING -> Relay 1 mở JIG -> tất cả relay OFF.");
            RaiseTestStatistics();

            if (!_settings.Test.AutoRestartAfterPass)
            {
                _cycleActive = false;
                return;
            }

            // Production trace: ~200 ms after relay2 OFF, Htdrv sends START_SCAN
            // for the next product. Re-arm only after current harness is released.
            await Task.Delay(
                Math.Max(0, _settings.Test.PostRelayRestartDelayMs),
                ct);

            _engine.Reset();
            Interlocked.Exchange(ref _postContinuityStarted, 0);
            _waitForProductRelease = true;
            _cycleActive = true;
            SelectedOperationTabIndex = 0;

            await _board.StartScanAsync(BoardScanMode.Production, ct);
            State = "PASS";
            AddLog("Đã restart scan. Chờ nhả sản phẩm/jig trước chu kỳ tiếp theo.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            AddLog("Chu trình cũ đã được hủy sạch.");
            return;
        }
        catch (Exception ex)
        {
            _cycleActive = false;
            _waitForProductRelease = false;

            try
            {
                await _board.StopScanAsync();
                await _board.AllRelaysOffAsync();
            }
            catch
            {
                // Giữ lỗi gốc của chu trình để chẩn đoán.
            }

            State = "LỖI CHU TRÌNH TEST";
            AddLog($"Chu trình tự động bị dừng: {ex.Message}");
            // Lỗi thiết bị/communication không tự cộng FAIL sản phẩm.
        }
    }

    public async Task ReconnectBoardForSettingsAsync()
    {
        _cycleActive = false;
        CancelCycleOperations();
        ClearInlineProbeContactsState(clearLastSeen: true);
        InvokeUi(ClearInlineProbeDisplay);
        _sound.SetWiringFaultAlarm(false);
        _engine.SetFrameProcessingEnabled(false);

        try
        {
            if (_board.IsConnected)
                await _board.DisconnectAsync();
        }
        finally
        {
            lock (_initializationGate)
                _hardwareInitializationTask = null;
        }

        BoardConnectionMessage = string.Empty;
        HardwareStatus = "Bo: đang nhận dạng lại...";
        State = "ĐANG NHẬN DẠNG LOẠI BO";
        AddLog($"Áp dụng LOẠI BO MẠCH: {BoardModeCatalog.DisplayName(_productionSettings.BoardMode)}; UART={_productionSettings.UartPort}.");
        await InitializeHardwareAsync();
        if (_board is IFirmwareProtocolBoard firmware && firmware.UsesFirmwareCycleResult)
        {
            MasterApproved = true;
            MasterStatus = "UART TTL • KẾT QUẢ THEO FIRMWARE (:CIRCUIT)";
            RaiseMasterState();
        }
        else if (_model is not null)
        {
            ResetMasterGateForModel();
        }
        RefreshProductionUiSettings();
    }

    public void RefreshProductionConfiguration()
    {
        int maxIo = _model?.MaxIo ?? 0;
        if (_model is not null)
        {
            int configuredMasterFaults = ProductionConfigService.GetMasterFaultRequiredCount(_productionSettings, _model);
            if (configuredMasterFaults != _masterRequiredFaultCount)
                ResetMasterGateForModel();
        }
        _board.ConfigureScanRange(maxIo);
        InvokeUi(RebuildActiveCards);
        RefreshProductionUiSettings();
        AddLog(
            $"Đã nạp lại cấu hình production: model max IO {maxIo}, LOTNO {Lot}; " +
            $"{_board.Capacity}.");
    }

    /// <summary>
    /// V12.9: áp dụng thay đổi card xuống tận runtime. Scan cũ bị dừng,
    /// generation transport bị invalidate, RX được purge bởi command D2XX,
    /// decoder/card UI được dựng lại rồi scan nền được khởi động lại.
    /// Không đóng/mở FTDI.
    /// </summary>
    public async Task RefreshProductionConfigurationAsync()
    {
        int maxIo = _model?.MaxIo ?? 0;
        bool wasScanning = _board.IsScanning;
        RuntimeMode runtimeMode = CurrentRuntimeMode;
        BoardScanMode resumeMode = runtimeMode == RuntimeMode.Probe
            ? BoardScanMode.Probe
            : BoardScanMode.Production;

        ClearInlineProbeContactsState(clearLastSeen: true);
        InvokeUi(ClearInlineProbeDisplay);
        _sound.SetWiringFaultAlarm(false);
        _engine.ClearTransientWiringFaults();

        if (_board.IsConnected && wasScanning)
        {
            await _board.StopScanAsync();
            await _board.AllRelaysOffAsync();
        }

        _board.ConfigureScanRange(maxIo);
        InvokeUi(RebuildActiveCards);
        RefreshProductionUiSettings();

        if (_model is not null)
        {
            int configuredMasterFaults = ProductionConfigService.GetMasterFaultRequiredCount(_productionSettings, _model);
            if (configuredMasterFaults != _masterRequiredFaultCount)
            {
                ResetMasterGateForModel();
                AddLog($"Cấu hình Số lỗi Master thay đổi -> reset Master Gate về 0/{configuredMasterFaults}.");
            }
        }

        if (_board.IsConnected && wasScanning)
        {
            await _board.StartScanAsync(resumeMode);
        }

        // Chỉ Production đang ARM mới được nối lại engine. Background vẫn chỉ scan nền.
        _engine.SetFrameProcessingEnabled(
            runtimeMode == RuntimeMode.Production &&
            Volatile.Read(ref _probeSessionActive) == 0 &&
            (MasterApproved || IsMasterSequenceActive));

        AddLog(
            $"Đã reconfigure card runtime không đóng FTDI: {_board.Capacity}; " +
            $"resume={resumeMode}, wasScanning={wasScanning}.");
    }

    private void RefreshProductionUiSettings()
    {
        _historyStore = new TestHistoryStore(ResolveHistoryDatabasePath(_productionSettings));
        Lot = _productionSettings.LotNo.ToString();
        Raise(nameof(ItemHeight));
        Raise(nameof(ScrollDelay));
        Raise(nameof(PageDelay));
        Raise(nameof(ShowTitle));
        Raise(nameof(ShowConnector));
        Raise(nameof(BoardCapacity));
        Raise(nameof(BoardCapacityText));
    }

    public void SetModel(ProductModel model)
    {
        // Đổi mã hàng phải hủy sạch chu trình cũ trước khi thay _model; nếu
        // không một task PASS/FAIL cũ hoàn thành muộn có thể cộng sản lượng
        // nhầm sang mã hàng vừa chọn.
        CancelCycleOperations();
        _cycleActive = false;
        _waitForProductRelease = false;
        _waitForFaultProductRemoval = false;
        Interlocked.Exchange(ref _resultRecordedThisCycle, 0);
        Interlocked.Exchange(ref _probeCycleRecordedThisCycle, 0);

        _model = model ??
            throw new ArgumentNullException(nameof(model));
        _pinsByIoLookup = _model.Pins.ToLookup(pin => pin.IoNumber);

        _sound.SetWiringFaultAlarm(false);
        _engine.SetModel(model);
        ResetMasterGateForModel();

        // Chỉ áp dụng SỐ CARD ĐÃ CẤU HÌNH. Không tự nâng theo model.
        // MainWindow sẽ chặn test nếu max IO của THT vượt dung lượng card.
        _board.ConfigureScanRange(model.MaxIo);
        InvokeUi(RebuildActiveCards);

        CurrentModelPath = ResolveOptionalModelPath(model.SourcePath);
        if (!string.IsNullOrWhiteSpace(CurrentModelPath))
        {
            _productionSettings.LastThtPath = CurrentModelPath;
            try
            {
                ProductionConfigService.Save(_productionSettings);
            }
            catch (Exception ex)
            {
                AddLog($"Không lưu được LastThtPath production config: {ex.Message}");
            }
        }

        // Đồng bộ model lên MainWindow ngay cả khi model được tự nạp lúc startup.
        // Đồng thời lưu ngay lựa chọn model; không chờ tới lúc bắt đầu test.
        _main.Model = model;
        _main.Home.Refresh();
        SaveLastTestedModel();

        Raise(nameof(ModelName));
        Raise(nameof(PartNumber));
        Raise(nameof(ProductName));
        Raise(nameof(VehicleType));
        Raise(nameof(CustomerCode));
        Raise(nameof(Eco));
        Raise(nameof(Nco));
        Raise(nameof(Alc));

        LoadStatisticsForModel(model);
        Lot = _productionSettings.LotNo.ToString();

        // _engine.SetModel() -> Reset() đã phát Changed và dựng bảng một lần.
        // Không RefreshFaults() lần hai vì model lớn có hàng trăm pin sẽ làm
        // người vận hành cảm giác load THT chậm gấp đôi.
        AddLog($"Đã nạp model {model.ModelName}: {model.Nets.Count} mạng I/O thường, " +
               $"{model.Clip?.Branches.Count ?? 0} nhánh CLIP, {model.ResistanceSteps.Count} bước đo điện trở.");

        if (model.Clip is not null)
        {
            string clipMap = string.Join(
                ", ",
                model.Clip.Branches.Select(branch =>
                    $"{branch.Name}->IO{branch.TargetIo}"));

            AddLog($"CLIP THT: A0/AO common=IO{model.Clip.CommonIo}; {clipMap}");
        }
    }

    private void LoadStatisticsForModel(ProductModel model)
    {
        try
        {
            ModelProductionStatistics stats = _statisticsStore.Get(model);
            Total = stats.Total;
            Pass = stats.Pass;
            Fail = stats.Fail;
            ApplyExtendedStatistics(stats);
            RaiseTestStatistics();

            AddLog(
                $"Đã nạp sản lượng mã hàng: Tổng {Total}, PASS {Pass}, FAIL {Fail}, " +
                $"Tỷ lệ {Rate:0.00}%.");
        }
        catch (Exception ex)
        {
            Total = 0;
            Pass = 0;
            Fail = 0;
            DailyTestCount = 0;
            MonthlyTestCount = 0;
            LifetimeTestCount = 0;
            ProbeCycleCount = 0;
            RaiseTestStatistics();
            AddLog($"Không thể nạp lịch sử sản lượng: {ex.Message}");
        }
    }

    private void RecordCompletedProduct(bool passed, string resultText)
    {
        ProductModel? model = _model;
        if (model is null ||
            Interlocked.CompareExchange(ref _resultRecordedThisCycle, 1, 0) != 0)
            return;

        long completedLot = Math.Max(0, _productionSettings.LotNo);
        DateTime finished = DateTime.Now;
        DateTime started = _cycleStartedAt <= finished ? _cycleStartedAt : finished;

        IReadOnlyList<FaultDetail> faultDetails = passed
            ? Array.Empty<FaultDetail>()
            : CaptureFaultDetails();
        FaultDetail? primaryFault = faultDetails
            .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
            .FirstOrDefault();

        string resultStatus = passed ? "PASS" : "FAIL";
        string failureName = passed
            ? string.Empty
            : primaryFault?.Name ?? (string.IsNullOrWhiteSpace(resultText) ? "FAIL" : resultText.Trim());

        var completed = new CompletedTestResult
        {
            Started = started,
            Finished = finished,
            Passed = passed,
            ResultText = resultStatus,
            Faults = faultDetails,
            Resistance = Resistance.ToArray()
        };

        try
        {
            ModelProductionStatistics stats = _statisticsStore.Record(
                model, passed, completedLot, passed ? "PASS" : failureName);
            Total = stats.Total;
            Pass = stats.Pass;
            Fail = stats.Fail;
            ApplyExtendedStatistics(stats);
        }
        catch (Exception ex)
        {
            Total++;
            if (passed) Pass++; else Fail++;
            AddLog($"Không thể lưu production.statistics.json: {ex.Message}");
        }

        int openCount = faultDetails.Count(x => x.Type == ProductFaultType.OpenCircuit);
        int wrongOnly = faultDetails.Count(x => x.Type == ProductFaultType.WrongWiring);
        int shortOnly = faultDetails.Count(x => x.Type == ProductFaultType.ShortCircuit);
        string resistanceText = string.Join(
            "; ",
            Resistance.Select(x => $"{x.Name}={x.Display}({x.ResultText})"));

        ResistanceResult? failedResistance = Resistance.FirstOrDefault(item => !item.Passed);
        CustomerFaultDisplay? customerFault = primaryFault is null
            ? null
            : FaultDisplayFormatter.FormatCustomer(primaryFault);

        var history = new TestHistoryRecord
        {
            Started = completed.Started,
            Finished = completed.Finished,
            PartName = model.ProductName,
            PartNumber = model.PartNumber,
            Eco = model.Eco,
            Nco = model.Nco,
            Alc = model.Alc,
            LotNo = completedLot,
            Result = completed.ResultText,
            Passed = completed.Passed,
            ModelName = model.ModelName,
            ModelFile = model.SourcePath,
            HtdrvName = ProgramIdentityService.BuildHtdrvName(_productionSettings),
            OpenCount = openCount,
            WrongCount = wrongOnly,
            ShortCount = shortOnly,
            Resistance = resistanceText,
            DeviceName = _productionSettings.DeviceName,
            DeviceNumber = _productionSettings.DeviceNumber,
            OperatorCompany = _productionSettings.OperatorCompany,
            ProductionLine = _productionSettings.ProductionLine,
            FaultType = customerFault?.FaultType ?? string.Empty,
            FaultCode = primaryFault?.Code ?? string.Empty,
            ExpectedSourceIo = primaryFault?.ExpectedSourceIo,
            ExpectedTargetIo = primaryFault?.ExpectedTargetIo,
            ActualSourceIo = primaryFault?.ActualSourceIo,
            ActualTargetIo = primaryFault?.ActualTargetIo,
            FaultDetailsJson = completed.FaultDetailsJson,
            FaultSummary = customerFault is null
                ? string.Empty
                : FaultDisplayFormatter.CustomerSummary(customerFault),
            MeasuredResistance = failedResistance?.ValueOhm,
            ResistanceMin = failedResistance?.MinOhm,
            ResistanceMax = failedResistance?.MaxOhm
        };

        try
        {
            _historyStore.Add(history);
            string historyFault = passed ? string.Empty : $" - {failureName}";
            AddLog($"History: LOT {completedLot} {resultStatus}{historyFault} đã lưu vào {_historyStore.DatabasePath}.");
        }
        catch (Exception ex)
        {
            AddLog($"Không thể lưu test history: {ex.Message}");
        }

        if (!passed)
        {
            try
            {
                ErrorLogService.SaveIfEnabled(
                    _productionSettings, model, completedLot, completed);
            }
            catch (Exception ex)
            {
                AddLog($"Không thể auto-save error detail: {ex.Message}");
            }
        }

        if (passed && _productionSettings.AutoPrintLabelOnPass)
            _ = PrintPassLabelSafeAsync(history);

        try
        {
            checked { _productionSettings.LotNo = completedLot + 1; }
            ProductionConfigService.Save(_productionSettings);
            Lot = _productionSettings.LotNo.ToString();
        }
        catch (OverflowException)
        {
            AddLog("LOTNO đã đạt giới hạn số nguyên; không thể tự tăng thêm.");
        }
        catch (Exception ex)
        {
            AddLog($"Không thể lưu LOTNO tiếp theo: {ex.Message}");
        }

        RaiseTestStatistics();
        AddLog(
            $"Đã lưu kết quả mã hàng: LOT {completedLot}, {resultStatus}" +
            (passed ? ", " : $" - {failureName}, ") +
            $"Tổng {Total}, PASS {Pass}, FAIL {Fail}, tỷ lệ {Rate:0.00}%. " +
            $"LOTNO kế tiếp: {_productionSettings.LotNo}.");
    }

    private void ApplyExtendedStatistics(ModelProductionStatistics stats)
    {
        DailyTestCount = stats.DailyTestCount;
        MonthlyTestCount = stats.MonthlyTestCount;
        LifetimeTestCount = stats.LifetimeTestCount;
        ProbeCycleCount = stats.ProbeCycleCount;
        Raise(nameof(ProbeReplacementThreshold));
        Raise(nameof(ProbeCycleText));
        Raise(nameof(ProbeMaintenanceDue));
        Raise(nameof(ProbeMaintenanceStatus));
        Raise(nameof(ProbeMaintenanceBackground));
    }

    private void RecordProbeCycleStarted()
    {
        ProductModel? model = _model;
        if (model is null ||
            !MasterApproved ||
            IsProbeSessionActive ||
            Interlocked.CompareExchange(ref _probeCycleRecordedThisCycle, 1, 0) != 0)
        {
            return;
        }

        bool wasDue = ProbeCycleCount >= ProbeReplacementThreshold;
        try
        {
            ModelProductionStatistics stats = _statisticsStore.RecordProbeCycle(
                model,
                ProbeReplacementThreshold);
            AddLog($"Probe cycle: {stats.ProbeCycleCount:N0}/{ProbeReplacementThreshold:N0} cho {stats.ModelKey}.");

            bool reachedDue = stats.ProbeCycleCount >= ProbeReplacementThreshold;
            InvokeUi(() =>
            {
                ApplyExtendedStatistics(stats);
                if (!wasDue && reachedDue)
                {
                    MessageBox.Show(
                        Application.Current?.MainWindow,
                        $"ĐẾN CHU KỲ THAY PROBE PIN\n\n" +
                        $"Mã hàng: {PartNumber}\n" +
                        $"Chu kỳ hiện tại: {ProbeCycleCount:N0}\n" +
                        $"Chu kỳ thay thế: {ProbeReplacementThreshold:N0}\n\n" +
                        "Trạng thái: CẦN THAY PROBE PIN",
                        "Cảnh báo bảo trì Probe Pin",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _probeCycleRecordedThisCycle, 0);
            AddLog($"Không thể lưu ProbeCycleCount: {ex.Message}");
        }
    }

    public bool TryResetProbeCycle(string password, out string message)
    {
        ProductModel? model = _model;
        if (model is null)
        {
            message = "Chưa chọn mã hàng.";
            return false;
        }

        if (_productDetectedThisCycle || _waitForProductRelease || _waitForFaultProductRemoval)
        {
            message = "Không thể reset trong khi sản phẩm/JIG đang ở trong chu kỳ test.";
            return false;
        }

        if (!MasterApproved)
        {
            message = "Không thể reset counter trong chu trình xác nhận MASTER.";
            return false;
        }

        if (_board is IFirmwareProtocolBoard firmware &&
            firmware.UsesFirmwareCycleResult &&
            _cycleActive)
        {
            message = "Hãy DỪNG AN TOÀN chu kỳ UART trước khi xác nhận thay Probe Pin.";
            return false;
        }

        string expected = _productionSettings.Password ?? string.Empty;
        if (string.IsNullOrEmpty(expected))
        {
            message = "Chưa cấu hình mật khẩu quản trị. Reset Probe Pin bị từ chối.";
            return false;
        }

        if (!AdminAuthenticationService.Verify(expected, password))
        {
            message = "Xác thực quản trị không đúng.";
            return false;
        }

        try
        {
            ProbeMaintenanceRecord record = _statisticsStore.ResetProbeCycle(
                model,
                ProbeReplacementThreshold,
                "SETTINGS_ADMIN",
                _productionSettings.DeviceName);
            ApplyExtendedStatistics(_statisticsStore.Get(model));
            Interlocked.Exchange(ref _probeCycleRecordedThisCycle, 0);
            message = $"PROBE PIN REPLACED đã được lưu. Counter {record.PreviousProbeCycleCount:N0} → 0.";
            AddLog($"MAINTENANCE: {record.Action}; model={record.ModelKey}; previous={record.PreviousProbeCycleCount}; admin={record.AdminIdentity}.");
            return true;
        }
        catch (Exception ex)
        {
            message = $"Không thể lưu reset Probe Pin: {ex.Message}";
            return false;
        }
    }

    private async Task PrintPassLabelSafeAsync(TestHistoryRecord history)
    {
        try
        {
            var data = new LabelPrintData(
                history.PartName, history.PartNumber, history.Eco, history.Nco, history.Alc,
                history.LotNo, history.Finished);

            string message = await _labelPrintService.PrintPassLabelAsync(
                data, _productionSettings.Label, _lifetimeCts.Token);
            AddLog($"LABEL: {message}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AddLog($"LABEL PRINT ERROR: {ex.Message}");
            InvokeUi(() => MessageBox.Show(
                $"Sản phẩm đã PASS nhưng không in được tem.\n\n{ex.Message}",
                "Lỗi in tem",
                MessageBoxButton.OK,
                MessageBoxImage.Warning));
        }
    }

    private void RefreshFaults()
    {
        if (IsRuntimeMode(RuntimeMode.Probe) ||
            Volatile.Read(ref _probeSessionActive) != 0)
        {
            // Probe chạy song song: không đụng vào Faults (bảng cấu hình Production).
            // Chỉ tắt cảnh báo dây để Probe không bị hiểu nhầm là lỗi sản phẩm.
            _sound.SetWiringFaultAlarm(false);
            RaiseTestStatistics();
            return;
        }

        if (!MasterApproved && IsMasterBadPhase)
            SynchronizeFaultRows(BuildMasterFaultGridRows());
        else
            SynchronizeFaultRows(_engine.BuildRows());

        RaiseTestStatistics();

        // Master GOOD/BAD dùng fault làm dữ liệu xác nhận, không phải Product FAIL.
        // Không phát TESTPOINT, không popup/eject fault, không tăng thống kê.
        if (!MasterApproved)
        {
            _sound.SetWiringFaultAlarm(false);
            return;
        }

        // Chỉ phát TESTPOINT khi đang ở chu kỳ production và chưa bước vào
        // giai đoạn chờ tháo sản phẩm. HandleWiringFaultAsync giữ âm lặp cho
        // tới khi popup được xác nhận.
        if (!_waitForFaultProductRemoval)
            _sound.SetWiringFaultAlarm(WiringFaultCount > 0 && (_cycleActive || _sound.IsWiringFaultAlarmActive));
        else
            _sound.SetWiringFaultAlarm(false);


    }

    private void SynchronizeFaultRows(IReadOnlyList<FaultRow> desiredRows)
    {
        // DataGrid production có thể có 100-200+ pin. Clear()+Add() toàn bộ
        // collection ở mỗi thay đổi I/O gây layout lại toàn bảng và làm cảm
        // giác scan chậm. Đồng bộ vi sai để chỉ thêm/xóa/move các dòng đổi.
        var desiredKeys = desiredRows
            .Select(RowKey)
            .ToHashSet(StringComparer.Ordinal);

        for (int index = Faults.Count - 1; index >= 0; index--)
        {
            if (!desiredKeys.Contains(RowKey(Faults[index])))
                Faults.RemoveAt(index);
        }

        for (int desiredIndex = 0; desiredIndex < desiredRows.Count; desiredIndex++)
        {
            FaultRow desired = desiredRows[desiredIndex];
            string key = RowKey(desired);

            int currentIndex = -1;
            for (int index = desiredIndex; index < Faults.Count; index++)
            {
                if (string.Equals(RowKey(Faults[index]), key, StringComparison.Ordinal))
                {
                    currentIndex = index;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                Faults.Insert(desiredIndex, desired);
                continue;
            }

            FaultRow current = Faults[currentIndex];
            current.Status = desired.Status;

            if (currentIndex != desiredIndex)
                Faults.Move(currentIndex, desiredIndex);
        }
    }

    private static string RowKey(FaultRow row) =>
        $"{(int)row.Kind}|{row.Io}|{row.Connector}|{row.Pin}|{row.WireName}|{row.Splice}|" +
        $"{row.ExpectedSourceIo}|{row.ExpectedTargetIo}|{row.ActualSourceIo}|{row.ActualTargetIo}";

    private void RaiseTestStatistics()
    {
        Raise(nameof(OpenCount));
        Raise(nameof(WrongCount));
        Raise(nameof(ShortCount));
        Raise(nameof(WiringFaultCount));
        Raise(nameof(PassedNetworkCount));
        Raise(nameof(ExpectedNetworkCount));
        Raise(nameof(NetworkProgress));
        Raise(nameof(MasterDetectedFaultCount));
        Raise(nameof(MasterRequiredFaultCount));
        Raise(nameof(MasterProgressText));
        RaiseActiveFault();
    }

    private void RaiseActiveFault()
    {
        Raise(nameof(ActiveFaultTitle));
        Raise(nameof(ActiveFaultMessage));
        Raise(nameof(ActiveFaultExpectedText));
        Raise(nameof(ActiveFaultActualText));
        Raise(nameof(ActiveFaultBackground));
        Raise(nameof(ActiveFaultForeground));
    }

    public void AddExternalLog(string message) => AddLog(message);

    private void AddLog(string text)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => AddLog(text)));
            return;
        }

        AsyncFileLogService.Current.Test(text);
        Logs.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {text}");
        while (Logs.Count > 300)
            Logs.RemoveAt(Logs.Count - 1);
    }

    private static void InvokeUi(Action action)
    {
        var dispatcher =
            Application.Current?.Dispatcher;

        if (dispatcher is null ||
            dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
