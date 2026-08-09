using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// V10.3 continuity engine reconstructed from the 2026-08-07 production and
/// TestPin traces. RX is a SOURCE -> TARGET relation table (80/81 -> A0/A1),
/// terminated by C0 00. The first I/O of each THT wire-name network is the
/// production source/stimulus; remaining I/Os are the expected targets.
/// </summary>
public sealed class TestEngine : IDisposable
{
    public const int JigEjectRelay = 1;
    public const int MarkingRelay = 2;

    readonly IBoardTransport _board;
    readonly KeysightVisaService _visa;
    readonly AppSettings _settings;
    readonly ProductionSettings _production;
    readonly object _gate = new();
    readonly SemaphoreSlim _relayPulseGate = new(1, 1);

    ProductModel? _model;
    readonly HashSet<string> _passedNets = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> _stableCounters = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<int> _currentActive = [];
    readonly Dictionary<int, HashSet<int>> _currentConnections = [];
    readonly HashSet<int> _unexpectedIo = [];
    readonly HashSet<WiringFaultPair> _wiringFaults = [];
    readonly Dictionary<(int Source, int Target), DateTime> _unexpectedPairSince = [];
    Dictionary<int, int> _componentByIo = [];
    Dictionary<PinRecord, WireNet[]> _netsByPin = new(ReferenceEqualityComparer.Instance);
    volatile bool _frameProcessingEnabled = true;
    bool _forceNextFrameChanged = true;
    bool _disposed;

    public event EventHandler? Changed;

    public IReadOnlyCollection<string> PassedNets => _passedNets;
    public IReadOnlyCollection<int> UnexpectedIo => _unexpectedIo;

    public IReadOnlyCollection<WiringFaultPair> WiringFaults
    {
        get
        {
            lock (_gate)
                return _wiringFaults.ToArray();
        }
    }
    public int ExpectedNetCount => _model is null
        ? 0
        : _model.Nets.Count + (_model.Clip?.Branches.Count ?? 0);
    public bool HasResistanceSteps => _model?.ResistanceSteps.Count > 0;
    public int ResistanceStepCount => _model?.ResistanceSteps.Count ?? 0;

    public bool HasWiringFault
    {
        get
        {
            lock (_gate)
                return _wiringFaults.Count > 0;
        }
    }

    /// <summary>
    /// Có hoạt động điện của sản phẩm trên các I/O production hiện tại.
    /// Với CLIP, A0/AO common -> I/O cấu hình trên row aN cũng được tính
    /// là hoạt động của sản phẩm.
    /// Dùng để chuyển UI từ CHỜ LẮP SẢN PHẨM sang ĐANG KIỂM TRA.
    /// </summary>
    public bool HasProductActivity
    {
        get
        {
            lock (_gate)
            {
                if (_model is null)
                    return false;

                return _currentConnections.Any(pair =>
                    pair.Value.Any(target =>
                        IsProductActivityEdge(_model, pair.Key, target)));
            }
        }
    }

    public bool IsProductReleased
    {
        get
        {
            lock (_gate)
            {
                if (_model is null)
                    return true;

                // CLIP A0/AO -> I/O cấu hình là một phần continuity sản phẩm,
                // nên chỉ released khi các quan hệ đó cũng đã mất.
                return !_currentConnections.Any(pair =>
                    pair.Value.Any(target =>
                        IsProductActivityEdge(_model, pair.Key, target)));
            }
        }
    }

    /// <summary>
    /// Chỉ dùng SAU KHI sản phẩm đã PASS và relay đã mở jig. Ngay khi bất kỳ
    /// quan hệ continuity bắt buộc nào của model bị mất, coi như thao tác tháo
    /// sản phẩm đã bắt đầu và UI phải rời PASS -> CHỜ LẮP SẢN PHẨM ngay.
    /// Không dùng property này trong lúc đang test vì contact có thể đang được
    /// lắp dần từng chân.
    /// </summary>
    public bool IsPassReleaseStarted
    {
        get
        {
            lock (_gate)
            {
                if (_model is null)
                    return true;

                foreach (WireNet net in _model.Nets)
                {
                    HashSet<int> actual =
                        _currentConnections.GetValueOrDefault(net.SourceIo) ?? [];

                    if (net.ExpectedActiveIo.Any(io => !actual.Contains(io)))
                        return true;
                }

                if (_model.Clip is not null)
                {
                    foreach (ClipBranch branch in _model.Clip.Branches)
                    {
                        if (!IsClipBranchConnected(_model.Clip, branch, _currentConnections))
                            return true;
                    }
                }

                return _model.Nets.Count == 0 && (_model.Clip?.Branches.Count ?? 0) == 0;
            }
        }
    }

    /// <summary>Number of receiver endpoints that are not currently A0 ACTIVE.</summary>
    public int MissingConnectionCount
    {
        get
        {
            lock (_gate)
            {
                if (_model is null)
                    return 0;

                int ordinaryMissing = _model.Nets.Sum(net =>
                {
                    HashSet<int> actual = _currentConnections.GetValueOrDefault(net.SourceIo) ?? [];
                    return net.ExpectedActiveIo.Count(io => !actual.Contains(io));
                });

                int clipMissing = _model.Clip?.Branches.Count(branch =>
                    !_passedNets.Contains(branch.NetName)) ?? 0;

                return ordinaryMissing + clipMissing;
            }
        }
    }

    public bool ContinuityPassed
    {
        get
        {
            lock (_gate)
            {
                int expected = _model is null
                    ? 0
                    : _model.Nets.Count + (_model.Clip?.Branches.Count ?? 0);

                return _model is not null &&
                       expected > 0 &&
                       _passedNets.Count == expected &&
                       _wiringFaults.Count == 0;
            }
        }
    }

    public TestEngine(
        IBoardTransport board,
        KeysightVisaService visa,
        AppSettings settings,
        ProductionSettings? production = null)
    {
        _board = board;
        _visa = visa;
        _settings = settings;
        _production = production ?? new ProductionSettings();
        // V11.8: TestEngine KHÔNG subscribe trực tiếp board nữa.
        // TestViewModel là router duy nhất quyết định Production/Probe, tránh
        // tuyệt đối snapshot đầu dò lọt vào logic đấu sai/chập.
    }

    public void SetModel(ProductModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _componentByIo = BuildExpectedComponents(model);
        _netsByPin = BuildNetsByPin(model);
        Reset();
    }

    static Dictionary<PinRecord, WireNet[]> BuildNetsByPin(ProductModel model)
    {
        // BuildRows được gọi rất thường xuyên. Không được mỗi lần lại quét
        // model.Nets cho từng pin (O(P*N)). Dựng lookup một lần khi load THT.
        var temp = new Dictionary<PinRecord, List<WireNet>>(ReferenceEqualityComparer.Instance);

        foreach (PinRecord pin in model.Pins)
            temp[pin] = [];

        foreach (WireNet net in model.Nets)
        {
            foreach (PinRecord pin in net.Pins)
            {
                if (!temp.TryGetValue(pin, out List<WireNet>? list))
                {
                    list = [];
                    temp[pin] = list;
                }

                list.Add(net);
            }
        }

        var result = new Dictionary<PinRecord, WireNet[]>(
            ReferenceEqualityComparer.Instance);

        foreach (KeyValuePair<PinRecord, List<WireNet>> pair in temp)
            result[pair.Key] = pair.Value.ToArray();

        return result;
    }

    /// <summary>
    /// PinProbe chỉ cần raw ScanFrame để nhận IO que dò; tắt engine production
    /// trong thời gian đó để không tốn CPU cho pass/fault/UI và không tạo trạng
    /// thái continuity giả. Khi quay lại production phải bật lại.
    /// </summary>
    public void SetFrameProcessingEnabled(bool enabled)
    {
        _frameProcessingEnabled = enabled;
        if (!enabled)
            Reset();
    }

    public void Reset()
    {
        lock (_gate)
        {
            _passedNets.Clear();
            _stableCounters.Clear();
            _currentActive.Clear();
            _currentConnections.Clear();
            _unexpectedIo.Clear();
            _wiringFaults.Clear();
            _unexpectedPairSince.Clear();
            // Frame production hoàn chỉnh đầu tiên sau Reset luôn phải phát
            // Changed, kể cả nó rỗng. Sau PASS relay có thể đã nhả toàn bộ
            // harness trước frame đầu tiên; nếu bỏ event rỗng thì UI sẽ mắc
            // ở PASS dù sản phẩm đã được tháo.
            _forceNextFrameChanged = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ProcessFrame(ScanFrame frame)
    {
        if (_disposed)
            return;

        ProductModel? model = _model;
        // Tuyệt đối không cho snapshot TestPin đi vào logic production.
        // Điều này ngăn que GND tạo cảnh báo đấu sai/chập giả.
        if (!_frameProcessingEnabled ||
            frame.Mode != BoardScanMode.Production ||
            model is null ||
            !frame.Complete)
            return;

        bool changed;

        lock (_gate)
        {
            // Có thể SetFrameProcessingEnabled(false) xảy ra sau check phía
            // ngoài nhưng trước khi callback lấy được lock. Kiểm tra lại để
            // frame production cũ không tạo lỗi đúng lúc chuyển sang TestPin.
            if (!_frameProcessingEnabled || frame.Mode != BoardScanMode.Production)
                return;

            string previousConnectionSignature = BuildConnectionSignature(_currentConnections);
            var previousPassed = _passedNets.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var previousFaults = _wiringFaults.ToHashSet();

            _currentActive.Clear();
            foreach (int io in frame.ActiveIo)
                _currentActive.Add(io);

            _currentConnections.Clear();
            foreach (var pair in frame.Connections)
                _currentConnections[pair.Key] = pair.Value.ToHashSet();

            // Đúng protocol Htdrv: kiểm tra từng network bằng quan hệ SOURCE -> TARGET.
            // Vì vậy một target đúng nhưng xuất hiện dưới source khác sẽ KHÔNG PASS.
            foreach (WireNet net in model.Nets)
            {
                HashSet<int> actualTargets =
                    _currentConnections.GetValueOrDefault(net.SourceIo) ?? [];

                bool connected = net.ExpectedActiveIo.Count > 0 &&
                                 net.ExpectedActiveIo.All(actualTargets.Contains);

                if (!connected)
                {
                    _stableCounters[net.Name] = 0;
                    _passedNets.Remove(net.Name);
                    continue;
                }

                int stable = _stableCounters.GetValueOrDefault(net.Name) + 1;
                _stableCounters[net.Name] = stable;

                int configuredConfirm = net.ExpectedActiveIo.Count <= 1
                    ? _production.IoConfirm1
                    : _production.IoConfirmN;

                int requiredFrames = configuredConfirm > 0
                    ? configuredConfirm
                    : Math.Max(1, _settings.Board.RequiredStableFrames);

                if (stable >= requiredFrames)
                    _passedNets.Add(net.Name);
            }

            // CLIP không phải WireNet thường. A0/AO là common, còn aN là
            // tên nhánh; đầu còn lại phải tới đúng I/O ghi trên row aN. Kiểm
            // tra hai chiều để không phụ thuộc source/target mà firmware
            // chọn khi phát frame.
            if (model.Clip is not null)
            {
                foreach (ClipBranch branch in model.Clip.Branches)
                {
                    bool connected = IsClipBranchConnected(
                        model.Clip,
                        branch,
                        _currentConnections);

                    if (!connected)
                    {
                        _stableCounters[branch.NetName] = 0;
                        _passedNets.Remove(branch.NetName);
                        continue;
                    }

                    int stable = _stableCounters.GetValueOrDefault(branch.NetName) + 1;
                    _stableCounters[branch.NetName] = stable;

                    int configuredConfirm = _production.IoConfirm1;
                    int requiredFrames = configuredConfirm > 0
                        ? configuredConfirm
                        : Math.Max(1, _settings.Board.RequiredStableFrames);

                    if (stable >= requiredFrames)
                        _passedNets.Add(branch.NetName);
                }
            }

            UpdateWiringFaults(model, frame.Timestamp);

            changed =
                _forceNextFrameChanged ||
                !string.Equals(
                    previousConnectionSignature,
                    BuildConnectionSignature(_currentConnections),
                    StringComparison.Ordinal) ||
                !previousPassed.SetEquals(_passedNets) ||
                !previousFaults.SetEquals(_wiringFaults);

            _forceNextFrameChanged = false;
        }

        // Không block worker D2XX. TestViewModel sẽ marshal async sang UI.
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    void UpdateWiringFaults(ProductModel model, DateTime timestamp)
    {
        // Đấu sai phải xét theo COMPONENT điện thật, không chỉ theo một chiều
        // source->target. Trace cho thấy một component nhiều nhánh có thể sinh
        // quan hệ ngược/transitive (ví dụ 14->26) nhưng vẫn là dây đúng.
        // Component THT được tính một lần khi SetModel; không union-find lại
        // mỗi frame. Đây là một trong các tối ưu quan trọng để scan gần tốc độ
        // Htdrv gốc khi model có hàng trăm pin.
        IReadOnlyDictionary<int, int> componentByIo = _componentByIo;
        var unexpectedNow = new HashSet<(int Source, int Target)>();

        foreach (var pair in _currentConnections)
        {
            int source = pair.Key;

            if (model.IgnoredIo.Contains(source))
                continue;

            bool sourceMapped = componentByIo.TryGetValue(source, out int sourceComponent);

            foreach (int target in pair.Value)
            {
                if (model.IgnoredIo.Contains(target))
                    continue;

                bool targetMapped = componentByIo.TryGetValue(target, out int targetComponent);

                if (!sourceMapped || !targetMapped || sourceComponent != targetComponent)
                    unexpectedNow.Add((source, target));
            }
        }

        foreach (var pair in _unexpectedPairSince.Keys.ToArray())
        {
            if (!unexpectedNow.Contains(pair))
                _unexpectedPairSince.Remove(pair);
        }

        // V11.6: quan hệ sai trong Production phải báo NGAY ở frame đầu tiên.
        // Không chờ trạng thái "ĐANG KIỂM TRA" và không chờ ShortConfirmMs.
        // Đây chỉ chạy trong TestEngine Production; Probe không bao giờ vào hàm này.
        var confirmed = new HashSet<WiringFaultPair>();

        foreach (var pair in unexpectedNow)
        {
            _unexpectedPairSince[pair] = timestamp;
            confirmed.Add(ClassifyUnexpectedPair(model, pair.Source, pair.Target));
        }

        _wiringFaults.Clear();
        foreach (WiringFaultPair fault in confirmed)
            _wiringFaults.Add(fault);

        _unexpectedIo.Clear();
        foreach (WiringFaultPair fault in _wiringFaults)
        {
            _unexpectedIo.Add(fault.SourceIo);
            _unexpectedIo.Add(fault.TargetIo);
        }
    }

    private static WiringFaultPair ClassifyUnexpectedPair(
        ProductModel model,
        int actualSource,
        int actualTarget)
    {
        // Nếu SOURCE chính là source được khai báo của một network nhưng trả về
        // một target ngoài network đó, đây là lỗi ĐẤU SAI: ta biết chính xác
        // "đáng lẽ source này phải đi tới đâu" và "thực tế đang đi tới đâu".
        WireNet? sourceNet = model.Nets.FirstOrDefault(net =>
            net.SourceIo == actualSource && net.ExpectedActiveIo.Count > 0);

        if (sourceNet is not null)
        {
            int expectedTarget = sourceNet.ExpectedActiveIo
                .FirstOrDefault(io => io != actualTarget);
            if (expectedTarget <= 0)
                expectedTarget = sourceNet.ExpectedActiveIo[0];

            return new WiringFaultPair(
                actualSource,
                actualTarget,
                $"Mong đợi IO{actualSource} -> IO{expectedTarget}; thực tế IO{actualSource} -> IO{actualTarget}",
                ProductFaultType.WrongWiring,
                actualSource,
                expectedTarget);
        }

        // Một cạnh điện nối hai component THT khác nhau nhưng SOURCE không phải
        // source định nghĩa của network nào thường biểu hiện một cầu nối/chập
        // giữa hai network. Tách riêng để UI/History không còn ghi chung chung.
        bool sourceMapped = model.Nets.Any(net => net.IoNumbers.Contains(actualSource)) ||
                            (model.Clip?.CommonIo == actualSource) ||
                            (model.Clip?.Branches.Any(branch => branch.TargetIo == actualSource) == true);
        bool targetMapped = model.Nets.Any(net => net.IoNumbers.Contains(actualTarget)) ||
                            (model.Clip?.CommonIo == actualTarget) ||
                            (model.Clip?.Branches.Any(branch => branch.TargetIo == actualTarget) == true);

        ProductFaultType type = sourceMapped && targetMapped
            ? ProductFaultType.ShortCircuit
            : ProductFaultType.WrongWiring;

        string reason = type == ProductFaultType.ShortCircuit
            ? $"Phát hiện chập IO{actualSource} <-> IO{actualTarget} giữa hai network khác nhau"
            : $"IO{actualSource} đang thông nhầm IO{actualTarget}";

        return new WiringFaultPair(
            actualSource,
            actualTarget,
            reason,
            type,
            null,
            null);
    }

    /// <summary>
    /// Xóa riêng lỗi đấu sai/chập đang treo mà không reset trạng thái PASS/open.
    /// Dùng khi phát hiện chữ ký đầu dò GND trong luồng scan production để
    /// một lần chạm que không thể tiếp tục kích popup từ frame trước.
    /// </summary>
    public void ClearTransientWiringFaults()
    {
        bool changed = false;
        lock (_gate)
        {
            changed = _wiringFaults.Count > 0 || _unexpectedIo.Count > 0 || _unexpectedPairSince.Count > 0;
            _wiringFaults.Clear();
            _unexpectedIo.Clear();
            _unexpectedPairSince.Clear();
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }


    private static bool IsProductActivityEdge(
        ProductModel model,
        int source,
        int target)
    {
        // CLIP A0 -> I/O cấu hình là continuity thật của sản phẩm. Chỉ loại
        // các special row malformed đã được parser đánh dấu ignored.
        return !model.IgnoredIo.Contains(source) &&
               !model.IgnoredIo.Contains(target);
    }

    private static bool IsClipBranchConnected(
        ClipTopology clip,
        ClipBranch branch,
        IReadOnlyDictionary<int, HashSet<int>> connections)
    {
        // aN chỉ là tên/thứ tự nhánh. I/O đích là giá trị cột I/O của row aN.
        // Chấp nhận hai chiều vì firmware có thể đảo SOURCE/TARGET.
        return HasElectricalEdge(connections, clip.CommonIo, branch.TargetIo);
    }

    private static bool HasElectricalEdge(
        IReadOnlyDictionary<int, HashSet<int>> connections,
        int a,
        int b)
    {
        return (connections.TryGetValue(a, out HashSet<int>? fromA) && fromA.Contains(b)) ||
               (connections.TryGetValue(b, out HashSet<int>? fromB) && fromB.Contains(a));
    }

    static Dictionary<int, int> BuildExpectedComponents(ProductModel model)
    {
        var parent = new Dictionary<int, int>();

        int Find(int value)
        {
            if (!parent.TryGetValue(value, out int p))
            {
                parent[value] = value;
                return value;
            }

            if (p == value)
                return value;

            int root = Find(p);
            parent[value] = root;
            return root;
        }

        void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA != rootB)
                parent[rootB] = rootA;
        }

        foreach (WireNet net in model.Nets)
        {
            if (net.IoNumbers.Count == 0)
                continue;

            int first = net.IoNumbers[0];
            Find(first);

            foreach (int io in net.IoNumbers.Skip(1))
                Union(first, io);
        }

        // Toàn bộ CLIP dùng chung A0. Vì vậy A0 và các I/O được cấu hình
        // trên row aN đều thuộc cùng component điện hợp lệ. Điều này ngăn
        // engine báo short giả giữa các nhánh CLIP vốn được thiết kế chung A0.
        if (model.Clip is not null)
        {
            int common = model.Clip.CommonIo;
            Find(common);

            foreach (ClipBranch branch in model.Clip.Branches)
                Union(common, branch.TargetIo);
        }

        return parent.Keys.ToDictionary(io => io, Find);
    }

    static string BuildConnectionSignature(
        IReadOnlyDictionary<int, HashSet<int>> connections)
    {
        return string.Join(
            ";",
            connections
                .Where(pair => pair.Value.Count > 0)
                .OrderBy(pair => pair.Key)
                .Select(pair =>
                    $"{pair.Key}>{string.Join(',', pair.Value.Order())}"));
    }

    /// <summary>
    /// Bảng động theo đúng cách vận hành Htdrv:
    /// - Ban đầu hiển thị TOÀN BỘ pin map có trong THT.
    /// - Network 2 chân: khi receiver được xác nhận A0 thì cả source + target ẩn.
    /// - Nhả dây: receiver về 80, hai dòng hiện lại ngay.
    /// - Splice nhiều nhánh: target nào đang A0 thì target đó ẩn; source chỉ ẩn
    ///   khi toàn bộ nhánh của network đã đạt.
    /// - I/O bất thường được giữ tại đúng vị trí pin và tô đỏ ở View.
    /// </summary>
    public IReadOnlyList<FaultRow> BuildRows()
    {
        ProductModel? model = _model;
        if (model is null)
            return [];

        var rows = new List<FaultRow>();

        lock (_gate)
        {
            foreach (PinRecord pin in model.Pins)
            {
                if (_unexpectedIo.Contains(pin.IoNumber))
                {
                    rows.Add(CreateWiringFaultRow(pin));
                    continue;
                }

                // Chỉ bỏ chính row AO/aN special. Nếu cùng một I/O còn có
                // row pin sản phẩm bình thường thì row đó vẫn phải được xử lý.
                if (model.Clip?.IsSpecialPin(pin) == true)
                    continue;

                // Htdrv chỉ đưa lên bảng các pin có map dây thực sự. Những dòng
                // trong THT không có Tên dây là dữ liệu pin/card nhưng không phải
                // một dòng continuity để người vận hành xử lý. TestPin vẫn có thể
                // hiển thị I/O vật lý của chúng khi chạm đầu dò.
                if (string.IsNullOrWhiteSpace(pin.WireName))
                    continue;

                // Pin đặc biệt có tên dây vẫn được giữ để người vận hành nhìn thấy
                // map THT, nhưng không tham gia điều kiện continuity/PASS.
                if (model.IgnoredIo.Contains(pin.IoNumber))
                {
                    rows.Add(new FaultRow
                    {
                        Kind = FaultKind.Info,
                        FaultType = "I/O đặc biệt",
                        Io = pin.IoNumber,
                        Connector = pin.Connector,
                        Pin = pin.PinNumber,
                        WireName = pin.WireName,
                        Splice = pin.SpliceName,
                        Section = pin.Section,
                        Color = pin.Color,
                        Status = "Theo cấu hình THT - không tham gia thông mạch"
                    });
                    continue;
                }

                WireNet[] memberships =
                    _netsByPin.GetValueOrDefault(pin) ?? Array.Empty<WireNet>();

                if (memberships.Length == 0)
                {
                    rows.Add(new FaultRow
                    {
                        Kind = FaultKind.Info,
                        FaultType = "Map pin",
                        Io = pin.IoNumber,
                        Connector = pin.Connector,
                        Pin = pin.PinNumber,
                        WireName = pin.WireName,
                        Splice = pin.SpliceName,
                        Section = pin.Section,
                        Color = pin.Color,
                        Status = "Không có cặp continuity trong THT"
                    });
                    continue;
                }

                bool visible = false;

                foreach (WireNet net in memberships)
                {
                    bool netPassed = _passedNets.Contains(net.Name);
                    bool isSource = pin.IoNumber == net.SourceIo;

                    if (net.IoNumbers.Count == 2)
                    {
                        // Cặp 2 chân: chỉ ẩn sau khi net đã qua bộ lọc ổn định.
                        if (!netPassed)
                            visible = true;
                    }
                    else if (isSource)
                    {
                        if (!netPassed)
                            visible = true;
                    }
                    else
                    {
                        // Splice: chỉ ẩn receiver nếu TARGET đó xuất hiện đúng
                        // dưới SOURCE của chính network, không dùng union A0 toàn frame.
                        HashSet<int> actualTargets =
                            _currentConnections.GetValueOrDefault(net.SourceIo) ?? [];
                        if (!actualTargets.Contains(pin.IoNumber))
                            visible = true;
                    }
                }

                if (visible)
                    rows.Add(CreateOpenRow(pin));
            }

            // CLIP được kiểm tra riêng: mọi nhánh dùng chung A0 nhưng mỗi aN
            // phải đi tới đúng I/O được cấu hình trên row aN. Chỉ nhánh chưa
            // đạt mới còn trên bảng.
            if (model.Clip is not null)
            {
                foreach (ClipBranch branch in model.Clip.Branches)
                {
                    if (_passedNets.Contains(branch.NetName))
                        continue;

                    PinRecord displayPin = branch.TargetPin ?? branch.ClipPin;
                    string targetDescription = branch.TargetPin is null
                        ? $"I/O {branch.TargetIo} (chưa có pin map thường trong THT)"
                        : $"I/O {branch.TargetIo} - {branch.TargetPin.Connector} - chân {branch.TargetPin.PinNumber}";

                    rows.Add(new FaultRow
                    {
                        Kind = FaultKind.Open,
                        ProductFaultType = ProductFaultType.OpenCircuit,
                        FaultType = FaultTypeCatalog.DisplayName(ProductFaultType.OpenCircuit),
                        Io = branch.TargetIo,
                        ExpectedSourceIo = model.Clip.CommonIo,
                        ExpectedTargetIo = branch.TargetIo,
                        RelatedIos = new[] { model.Clip.CommonIo, branch.TargetIo },
                        Connector = displayPin.Connector,
                        Pin = displayPin.PinNumber,
                        WireName = string.IsNullOrWhiteSpace(displayPin.WireName)
                            ? $"CLIP {branch.Name}"
                            : displayPin.WireName,
                        Splice = $"A0(IO{model.Clip.CommonIo}) -> {branch.Name} -> IO{branch.TargetIo}",
                        Section = displayPin.Section,
                        Color = displayPin.Color,
                        Status = $"Chưa thông CLIP {branch.Name}: A0(IO{model.Clip.CommonIo}) -> " +
                                 $"{branch.Name} -> {targetDescription}"
                    });
                }
            }

            // Trường hợp source/target lỗi không có pin map trong THT.
            foreach (WiringFaultPair fault in _wiringFaults
                         .OrderBy(x => x.SourceIo)
                         .ThenBy(x => x.TargetIo))
            {
                foreach (int io in new[] { fault.SourceIo, fault.TargetIo }.Distinct())
                {
                    if (model.Pins.Any(pin =>
                            pin.IoNumber == io &&
                            !string.IsNullOrWhiteSpace(pin.WireName)))
                        continue;

                    rows.Add(new FaultRow
                    {
                        Kind = fault.FaultType == ProductFaultType.ShortCircuit
                            ? FaultKind.Short
                            : FaultKind.WrongWiring,
                        ProductFaultType = fault.FaultType,
                        FaultType = FaultTypeCatalog.DisplayName(fault.FaultType),
                        Io = io,
                        ExpectedSourceIo = fault.ExpectedSourceIo,
                        ExpectedTargetIo = fault.ExpectedTargetIo,
                        ActualSourceIo = fault.SourceIo,
                        ActualTargetIo = fault.TargetIo,
                        RelatedIos = new[] { fault.SourceIo, fault.TargetIo },
                        Status = $"{fault.Reason}; I/O {io} không có map pin trong THT"
                    });
                }
            }
        }

        // V11.4: lỗi đấu sai/chập luôn phải nằm ở đầu bảng để người vận hành
        // nhìn thấy ngay. OrderBy của LINQ là stable nên thứ tự pin THT bên
        // trong nhóm lỗi và nhóm hở mạch vẫn được giữ nguyên.
        return rows
            .OrderBy(row => row.ProductFaultType == ProductFaultType.None
                ? 90
                : FaultTypeCatalog.Priority(row.ProductFaultType))
            .ToArray();
    }

    FaultRow CreateWiringFaultRow(PinRecord pin)
    {
        WiringFaultPair? fault = _wiringFaults.FirstOrDefault(item =>
            item.SourceIo == pin.IoNumber || item.TargetIo == pin.IoNumber);

        ProductFaultType type = fault?.FaultType ?? ProductFaultType.WrongWiring;
        string status = fault is null
            ? $"I/O {pin.IoNumber} đấu sai cấu hình"
            : fault.Reason;

        return new FaultRow
        {
            Kind = type == ProductFaultType.ShortCircuit
                ? FaultKind.Short
                : FaultKind.WrongWiring,
            ProductFaultType = type,
            FaultType = FaultTypeCatalog.DisplayName(type),
            Io = pin.IoNumber,
            ExpectedSourceIo = fault?.ExpectedSourceIo,
            ExpectedTargetIo = fault?.ExpectedTargetIo,
            ActualSourceIo = fault?.SourceIo,
            ActualTargetIo = fault?.TargetIo,
            RelatedIos = fault is null
                ? new[] { pin.IoNumber }
                : new[] { fault.SourceIo, fault.TargetIo },
            Connector = pin.Connector,
            Pin = pin.PinNumber,
            WireName = pin.WireName,
            Splice = pin.SpliceName,
            Section = pin.Section,
            Color = pin.Color,
            Status = status
        };
    }

    FaultRow CreateOpenRow(PinRecord pin)
    {
        WireNet? net = (_netsByPin.GetValueOrDefault(pin) ?? Array.Empty<WireNet>())
            .FirstOrDefault(candidate => !_passedNets.Contains(candidate.Name));

        int expectedSource = net?.SourceIo ?? pin.IoNumber;
        int expectedTarget = net?.ExpectedActiveIo.FirstOrDefault(io => io != expectedSource) ?? 0;
        if (expectedTarget <= 0 && net is not null)
            expectedTarget = net.IoNumbers.FirstOrDefault(io => io != expectedSource);

        string expectedText = expectedTarget > 0
            ? $"IO{expectedSource} <-> IO{expectedTarget}"
            : $"IO{pin.IoNumber}";

        return new FaultRow
        {
            Kind = FaultKind.Open,
            ProductFaultType = ProductFaultType.OpenCircuit,
            FaultType = FaultTypeCatalog.DisplayName(ProductFaultType.OpenCircuit),
            Io = pin.IoNumber,
            ExpectedSourceIo = expectedSource > 0 ? expectedSource : null,
            ExpectedTargetIo = expectedTarget > 0 ? expectedTarget : null,
            RelatedIos = net?.IoNumbers.Distinct().ToArray() ?? new[] { pin.IoNumber },
            Connector = pin.Connector,
            Pin = pin.PinNumber,
            WireName = pin.WireName,
            Splice = pin.SpliceName,
            Section = pin.Section,
            Color = pin.Color,
            Status = $"Chưa kết nối: {expectedText}"
        };
    }

    /// <summary>
    /// V13.0: sau khi người vận hành XÁC NHẬN hàng lỗi, chỉ Relay 1 (JIG)
    /// được pulse để đẩy/mở jig. Relay 2 MARKING bị cấm tuyệt đối trên FAIL.
    /// Việc gọi method này nằm sau hộp xác nhận, không bao giờ từ Probe.
    /// </summary>
    public Task EjectFaultProductAsync(CancellationToken ct = default)
        => PulseJigRelayAsync(ct);

    /// <summary>
    /// Manual/production helper V15.2: Relay 1 JIG chỉ được pulse đúng một lần.
    /// Dù delay bị hủy hoặc board phát sinh exception, finally vẫn cố đưa toàn bộ relay về OFF.
    /// </summary>
    public Task PulseJigRelayAsync(CancellationToken ct = default)
        => PulseRelaySafeAsync(JigEjectRelay, _production.Relay1JigPulseMs, "R1 JIG", ct);

    /// <summary>Relay 2 MARKING chỉ được pulse đúng một lần và luôn trả về OFF.</summary>
    public Task PulseMarkingRelayAsync(CancellationToken ct = default)
        => PulseRelaySafeAsync(MarkingRelay, _production.Relay2MarkingPulseMs, "R2 MARKING", ct);

    /// <summary>
    /// V12.9.5: eject riêng cho Master Sample. Chỉ Relay 1 JIG được pulse;
    /// tuyệt đối không MARKING và không dùng behavior Product FAIL.
    /// </summary>
    public Task EjectMasterSampleAsync(CancellationToken ct = default)
        => PulseJigRelayAsync(ct);

    private async Task PulseRelaySafeAsync(int relay, int durationMs, string relayName, CancellationToken ct)
    {
        int pulseMs = Math.Clamp(durationMs, 50, 5_000);
        await _relayPulseGate.WaitAsync(ct);
        Exception? offFailure = null;
        try
        {
            // Luôn bắt đầu từ trạng thái OFF để không kế thừa trạng thái relay trước đó.
            await ForceAllRelaysOffAsync(relayName + " PRE", ct);
            await _board.SetRelayAsync(relay, ct);
            AsyncFileLogService.Current.Test($"RELAY {relayName} ON - pulse {pulseMs} ms");
            await Task.Delay(pulseMs, ct);
        }
        finally
        {
            try
            {
                // CancellationToken của cycle có thể đã cancel. Safe-OFF vẫn phải cố chạy độc lập.
                await ForceAllRelaysOffAsync(relayName + " POST", CancellationToken.None);
                AsyncFileLogService.Current.Test($"RELAY {relayName} OFF - safe idle");
            }
            catch (Exception ex)
            {
                offFailure = ex;
                AsyncFileLogService.Current.Error($"RELAY SAFE-OFF FAILED after {relayName}: {ex.Message}");
            }
            _relayPulseGate.Release();
        }

        // Nếu OFF không xác nhận được, không cho workflow tiếp tục sang relay tiếp theo.
        if (offFailure is not null)
            throw new InvalidOperationException($"Không thể đưa relay về OFF sau {relayName}.", offFailure);
    }

    private async Task ForceAllRelaysOffAsync(string reason, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await _board.AllRelaysOffAsync(ct);
                AsyncFileLogService.Current.Test($"ALL RELAYS OFF [{reason}] attempt={attempt}");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                AsyncFileLogService.Current.Error($"ALL RELAYS OFF FAILED [{reason}] attempt={attempt}: {ex.Message}");
                if (attempt < 3)
                    await Task.Delay(80, CancellationToken.None);
            }
        }

        throw new InvalidOperationException($"Không thể cưỡng bức ALL RELAYS OFF ({reason}).", last);
    }

    public async Task<List<ResistanceResult>> MeasureResistanceAsync(
        CancellationToken ct = default)
    {
        ProductModel? model = _model;
        if (model is null || model.ResistanceSteps.Count == 0)
            return [];

        if (!_visa.IsConnected)
            throw new InvalidOperationException("Chưa kết nối Keysight 34461A");

        // Real production trace:
        // STOP_SCAN -> RESET_CLEAR -> R1 route -> measure -> R2 route -> measure
        // -> three INIT_1/INIT_2 recovery cycles. Do not release between R1/R2.
        await _board.StopScanAsync(ct);
        await _board.ResetClearAsync(ct);

        var results = new List<ResistanceResult>();

        try
        {
            foreach (ResistanceStep originalStep in model.ResistanceSteps.OrderBy(x => x.Channel))
            {
                ct.ThrowIfCancellationRequested();

                // Cấu hình R1-R5 trong Settings có tác dụng thật: nếu Enabled,
                // nó ánh xạ tên bước THT sang kênh phần cứng. Min/Max trong THT
                // vẫn là chuẩn; chỉ dùng Min/Max Settings làm fallback khi THT=0/0.
                ResistanceChannelSetting? productionChannel = _production.ResistanceChannels
                    .FirstOrDefault(x => x.Enabled &&
                        string.Equals(x.Name, originalStep.Name, StringComparison.OrdinalIgnoreCase));

                int channel = productionChannel?.Channel ?? originalStep.Channel;
                double minOhm = originalStep.MinOhm;
                double maxOhm = originalStep.MaxOhm;
                if (productionChannel is not null && minOhm == 0 && maxOhm == 0)
                {
                    minOhm = productionChannel.MinOhm;
                    maxOhm = productionChannel.MaxOhm;
                }

                ResistanceChannelSettings? route = _settings.Test.ResistanceChannels
                    .FirstOrDefault(x => x.Channel == channel);

                ResistanceStep step = originalStep with
                {
                    Channel = channel,
                    MinOhm = minOhm,
                    MaxOhm = maxOhm,
                    RouteA = string.IsNullOrWhiteSpace(originalStep.RouteA) ? (route?.RouteA ?? string.Empty) : originalStep.RouteA,
                    RouteB = string.IsNullOrWhiteSpace(originalStep.RouteB) ? (route?.RouteB ?? string.Empty) : originalStep.RouteB
                };

                await _board.SelectResistanceRouteAsync(step, ct);

                int resistanceDelay = Math.Max(
                    Math.Max(0, _settings.Keysight.SettleDelayMs),
                    Math.Max(0, _production.ResistanceDelayMs));
                if (resistanceDelay > 0)
                    await Task.Delay(resistanceDelay, ct);

                double value = _visa.MeasureResistance(_settings.Keysight.Command);
                bool open = !double.IsFinite(value) ||
                            Math.Abs(value) >= _settings.Test.ResistanceOpenThreshold;

                results.Add(new ResistanceResult
                {
                    Name = step.Name,
                    Channel = step.Channel,
                    ValueOhm = open ? null : value,
                    MinOhm = step.MinOhm,
                    MaxOhm = step.MaxOhm,
                    IsOpen = open,
                    Passed = !open && value >= step.MinOhm && value <= step.MaxOhm
                });
            }
        }
        finally
        {
            await _board.ReleaseResistanceRouteAsync(ct);
        }

        return results;
    }

    public async Task<bool> CompletePassAsync(
        IReadOnlyList<ResistanceResult> resistance,
        Action? onPassStarted = null,
        bool markingEnabled = true,
        CancellationToken ct = default)
    {
        ProductModel? model = _model;
        if (model is null)
            return false;

        bool resistanceOk = model.ResistanceSteps.Count == 0 ||
                            (resistance.Count == model.ResistanceSteps.Count &&
                             resistance.All(x => x.Passed));

        if (!ContinuityPassed || !resistanceOk)
            return false;

        if (model.ResistanceSteps.Count == 0)
        {
            // Trace production thật:
            // continuity PASS -> STOP_SCAN -> RESET_CLEAR -> MARKING (Relay 2)
            // -> JIG EJECT (Relay 1).
            // STOP/RESET không làm mất trạng thái INIT, vì sau relay Htdrv
            // START_SCAN lại trực tiếp.
            await _board.StopScanAsync(ct);
            await _board.ResetClearAsync(ct);
        }

        int relayStartDelayMs = model.ResistanceSteps.Count > 0
            ? Math.Max(0, _settings.Test.PostResistanceRelayDelayMs)
            : 0;

        if (relayStartDelayMs > 0)
            await Task.Delay(relayStartDelayMs, ct);

        // V12.4 - vai trò relay cố định:
        //   Relay 2 = MARKING
        //   Relay 1 = MỞ/THÁO JIG
        // Trạng thái chờ luôn OFF. Sản phẩm production PASS phải MARKING trước,
        // sau đó mới được mở JIG. Master OK dùng cùng xác nhận PASS nhưng tắt
        // markingEnabled để KHÔNG đóng dấu lên mẫu master; master chỉ mở JIG.
        await _board.AllRelaysOffAsync(ct);

        if (markingEnabled)
        {
            // Bước 1 production PASS: R2 MARKING pulse đúng một lần rồi OFF.
            onPassStarted?.Invoke();
            await PulseMarkingRelayAsync(ct);

            int interlockMs = Math.Clamp(_production.PassMarkingToJigDelayMs, 0, 5_000);
            if (interlockMs > 0)
                await Task.Delay(interlockMs, ct);
        }
        else
        {
            // Master sample: đã đạt nhưng tuyệt đối không kích MARKING.
            onPassStarted?.Invoke();
        }

        // Bước cuối PASS/MASTER: R1 JIG pulse đúng một lần rồi OFF.
        await PulseJigRelayAsync(ct);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _frameProcessingEnabled = false;
        Changed = null;
        _relayPulseGate.Dispose();
    }

}
