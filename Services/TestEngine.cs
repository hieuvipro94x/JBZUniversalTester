using JBZUniversalTester.Models;

using System.Globalization;
using System.Text.RegularExpressions;

namespace JBZUniversalTester.Services;

public sealed record ExpectedNetworkDiagnostic(
    string Key,
    string Name,
    string Category,
    int SourceIo,
    IReadOnlyList<int> ExpectedIo,
    bool Passed)
{
    public string Display => ExpectedIo.Count == 0
        ? $"{Name}: IO{SourceIo}"
        : $"{Name}: IO{SourceIo}<->{string.Join(",", ExpectedIo.Select(io => $"IO{io}"))}";
}

public sealed record PassGateDiagnostics(
    int ExpectedNetCount,
    int PassedNetCount,
    IReadOnlyList<ExpectedNetworkDiagnostic> ExpectedNetworks,
    IReadOnlyList<ExpectedNetworkDiagnostic> RemainingNetworks,
    int WrongCandidateCount,
    int WrongConfirmedCount,
    int ShortCandidateCount,
    int ShortConfirmedCount,
    bool HasProductActivity,
    bool HasExpectedSourceCoverage,
    bool ProductStable,
    bool ContactUnstable,
    bool ContactLossTimedOut,
    bool ContinuityPassed,
    bool HasWiringFault,
    bool LastFrameValid,
    long LastFrameSequence,
    int LastFrameUnknownBytes);

/// <summary>
/// V10.3 continuity engine reconstructed from the 2026-08-07 production and
/// TestPin traces. RX is a SOURCE -> TARGET relation table (80/81 -> A0/A1),
/// terminated by C0 00. The first I/O of each THT wire-name network is the
/// production source/stimulus; remaining I/Os are the expected targets.
/// </summary>
public sealed class TestEngine : IDisposable
{
    public sealed class PreparedModelState
    {
        internal ProductModel Model { get; init; } = null!;
        internal Dictionary<int, int> ComponentByIo { get; init; } = null!;
        internal Dictionary<PinRecord, WireNet[]> NetsByPin { get; init; } = null!;
        internal Dictionary<PinRecord, int> DisplayOrderByPin { get; init; } = null!;
        internal Dictionary<WireNet, int> DisplayOrderByNet { get; init; } = null!;
        internal Dictionary<WireNet, string> ConfirmationKeyByNet { get; init; } = null!;
        internal Dictionary<ClipBranch, string> ConfirmationKeyByClip { get; init; } = null!;
    }
    const int ClipDisplayOrderBase = -1_000_000;
    const string PendingConnectionStatus = "CHƯA KẾT NỐI";
    const string RemovalConnectionStatus = "CHỜ THÁO";
    public const int JigEjectRelay = 1;
    public const int MarkingRelay = 2;

    readonly IBoardTransport _board;
    readonly KeysightVisaService _visa;
    readonly AppSettings _settings;
    readonly ProductionSettings _production;
    readonly ProductionFaultConfirmationGate _faultConfirmation;
    readonly object _gate = new();
    readonly SemaphoreSlim _relayPulseGate = new(1, 1);

    ProductModel? _model;
    readonly HashSet<string> _passedNets = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> _stableCounters = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<int> _currentActive = [];
    readonly Dictionary<int, HashSet<int>> _currentConnections = [];
    readonly HashSet<int> _unexpectedIo = [];
    readonly HashSet<WiringFaultPair> _wiringFaults = [];
    readonly HashSet<WiringFaultPair> _candidateWiringFaults = [];
    readonly HashSet<string> _confirmedOpenKeys = new(StringComparer.Ordinal);
    readonly HashSet<string> _latchedClipKeys = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<(int SourceIo, int TargetIo)> _unexpectedPairScratch = [];
    Dictionary<int, int> _componentByIo = [];
    Dictionary<int, int> _actualComponentByIo = [];
    Dictionary<PinRecord, WireNet[]> _netsByPin = new(ReferenceEqualityComparer.Instance);
    Dictionary<WireNet, string> _confirmationKeyByNet = new(ReferenceEqualityComparer.Instance);
    Dictionary<ClipBranch, string> _confirmationKeyByClip = new(ReferenceEqualityComparer.Instance);
    Dictionary<PinRecord, int> _displayOrderByPin = new(ReferenceEqualityComparer.Instance);
    Dictionary<WireNet, int> _displayOrderByNet = new(ReferenceEqualityComparer.Instance);
    Dictionary<WireNet, FaultRow[]> _displayRowsByNet = new(ReferenceEqualityComparer.Instance);
    Dictionary<WireNet, FaultRow[]> _removalDisplayRowsByNet = new(ReferenceEqualityComparer.Instance);
    Dictionary<ClipBranch, FaultRow> _displayRowByClip = new(ReferenceEqualityComparer.Instance);
    Dictionary<ClipBranch, FaultRow> _removalDisplayRowByClip = new(ReferenceEqualityComparer.Instance);
    FaultRow? _clipCommonDisplayRow;
    readonly Dictionary<string, bool> _expectedConnectionScratch = new(StringComparer.Ordinal);
    volatile bool _frameProcessingEnabled = true;
    bool _forceNextFrameChanged = true;
    bool _contactUnstable;
    bool _contactLossTimedOut;
    bool _productStable;
    bool _readyToEvaluateProductFaults;
    bool _hasExpectedSourceCoverage;
    bool _lastFrameValid;
    long _lastFrameSequence;
    long _framesProcessed;
    int _lastFrameUnknownBytes;
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
    public int ExpectedNetCount
    {
        get
        {
            lock (_gate)
                return _model is null ? 0 : ProductionExpectedNetCount(_model);
        }
    }
    public bool HasResistanceSteps => _model is not null && ResistanceStepCount > 0;
    public int ResistanceStepCount => _model is null
        ? 0
        : ResistanceMeasurementPlan.BuildEnabledSteps(_production).Count;

    public bool HasWiringFault
    {
        get
        {
            lock (_gate)
                return _wiringFaults.Count > 0;
        }
    }

    public bool HasConfirmedOpenCircuit
    {
        get { lock (_gate) return _confirmedOpenKeys.Count > 0; }
    }

    public bool HasContactInstability
    {
        get { lock (_gate) return _contactUnstable; }
    }

    public bool ContactLossTimedOut
    {
        get { lock (_gate) return _contactLossTimedOut; }
    }

    public bool ReadyToEvaluateProductFaults
    {
        get { lock (_gate) return _readyToEvaluateProductFaults; }
    }

    public bool HasExpectedSourceCoverage
    {
        get { lock (_gate) return _hasExpectedSourceCoverage; }
    }

    public long FramesProcessed => Interlocked.Read(ref _framesProcessed);

    public bool LastFrameValid
    {
        get { lock (_gate) return _lastFrameValid; }
    }

    public long LastFrameSequence
    {
        get { lock (_gate) return _lastFrameSequence; }
    }

    public int LastFrameUnknownBytes
    {
        get { lock (_gate) return _lastFrameUnknownBytes; }
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
                    if (!IsEligibleProductionNet(net))
                        continue;

                    if (!IsWireNetConnected(net, _currentConnections))
                        return true;
                }

                if (_model.Clip is not null)
                {
                    foreach (ClipBranch branch in _model.Clip.Branches)
                    {
                        if (!IsEligibleClipBranch(_model.Clip, branch))
                            continue;

                        if (!IsClipBranchConnected(_model.Clip, branch, _currentConnections))
                            return true;
                    }
                }

                return ProductionExpectedNetCount(_model) == 0;
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
                    if (!IsEligibleProductionNet(net))
                        return 0;

                    return CountDisconnectedEndpoints(net, _currentConnections);
                });

                int clipMissing = _model.Clip?.Branches.Count(branch =>
                    !_passedNets.Contains(branch.NetName) &&
                    !_latchedClipKeys.Contains(ClipConfirmationKey(branch.NetName))) ?? 0;

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
                int expected = _model is null ? 0 : ProductionExpectedNetCount(_model);
                int passed = _model is null ? 0 : CountPassedExpectedUnsafe(_model);

                return _model is not null &&
                       expected > 0 &&
                       passed == expected &&
                       _candidateWiringFaults.Count == 0 &&
                       _wiringFaults.Count == 0;
            }
        }
    }

    public PassGateDiagnostics GetPassGateDiagnostics()
    {
        lock (_gate)
        {
            ProductModel? model = _model;
            ExpectedNetworkDiagnostic[] expectedNetworks = model is null
                ? []
                : BuildExpectedNetworkDiagnosticsUnsafe(model);
            ExpectedNetworkDiagnostic[] remainingNetworks = expectedNetworks
                .Where(item => !item.Passed)
                .ToArray();

            return new PassGateDiagnostics(
                expectedNetworks.Length,
                expectedNetworks.Count(item => item.Passed),
                expectedNetworks,
                remainingNetworks,
                _candidateWiringFaults.Count(item => item.FaultType == ProductFaultType.WrongWiring),
                _wiringFaults.Count(item => item.FaultType == ProductFaultType.WrongWiring),
                _candidateWiringFaults.Count(item => item.FaultType == ProductFaultType.ShortCircuit),
                _wiringFaults.Count(item => item.FaultType == ProductFaultType.ShortCircuit),
                model is not null && HasProductActivityUnsafe(model),
                _hasExpectedSourceCoverage,
                _productStable,
                _contactUnstable,
                _contactLossTimedOut,
                ContinuityPassed,
                _wiringFaults.Count > 0,
                _lastFrameValid,
                _lastFrameSequence,
                _lastFrameUnknownBytes);
        }
    }

    public TestEngine(
        IBoardTransport board,
        KeysightVisaService visa,
        AppSettings settings,
        ProductionSettings? production = null,
        TimeProvider? timeProvider = null)
    {
        _board = board;
        _visa = visa;
        _settings = settings;
        _production = production ?? new ProductionSettings();
        _faultConfirmation = new ProductionFaultConfirmationGate(_production, timeProvider);
        // V11.8: TestEngine KHÔNG subscribe trực tiếp board nữa.
        // TestViewModel là router duy nhất quyết định Production/Probe, tránh
        // tuyệt đối snapshot đầu dò lọt vào logic đấu sai/chập.
    }

    public PreparedModelState PrepareModel(ProductModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Build immutable topology outside the frame lock. Only the short
        // reference swap/reset is serialized with ProcessFrame.
        Dictionary<int, int> componentByIo = BuildExpectedComponents(model);
        Dictionary<PinRecord, WireNet[]> netsByPin = BuildNetsByPin(model);
        Dictionary<PinRecord, int> displayOrderByPin = BuildDisplayOrderByPin(model);
        Dictionary<WireNet, int> displayOrderByNet = BuildNetworkDisplayOrder(model);
        var confirmationKeyByNet = new Dictionary<WireNet, string>(ReferenceEqualityComparer.Instance);
        foreach (WireNet net in model.Nets)
            confirmationKeyByNet[net] = NetConfirmationKey(net.Name);

        var confirmationKeyByClip = new Dictionary<ClipBranch, string>(ReferenceEqualityComparer.Instance);
        foreach (ClipBranch branch in model.Clip?.Branches ?? [])
            confirmationKeyByClip[branch] = ClipConfirmationKey(branch.NetName);

        return new PreparedModelState
        {
            Model = model,
            ComponentByIo = componentByIo,
            NetsByPin = netsByPin,
            DisplayOrderByPin = displayOrderByPin,
            DisplayOrderByNet = displayOrderByNet,
            ConfirmationKeyByNet = confirmationKeyByNet,
            ConfirmationKeyByClip = confirmationKeyByClip
        };
    }

    public void CommitPreparedModel(PreparedModelState prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        lock (_gate)
        {
            _model = prepared.Model;
            _componentByIo = prepared.ComponentByIo;
            _netsByPin = prepared.NetsByPin;
            _displayOrderByPin = prepared.DisplayOrderByPin;
            _displayOrderByNet = prepared.DisplayOrderByNet;
            _confirmationKeyByNet = prepared.ConfirmationKeyByNet;
            _confirmationKeyByClip = prepared.ConfirmationKeyByClip;
            // Cache toàn bộ text/topology tĩnh
            // một lần khi load THT; frame chỉ chọn row cần hiện, không tạo lại
            // Connector/Pin/WireName/Color/IO-CN-PN cho hàng trăm endpoint.
            _displayRowsByNet = new Dictionary<WireNet, FaultRow[]>(
                ReferenceEqualityComparer.Instance);
            _removalDisplayRowsByNet = new Dictionary<WireNet, FaultRow[]>(
                ReferenceEqualityComparer.Instance);
            foreach (WireNet net in prepared.Model.Nets)
            {
                _displayRowsByNet[net] = CreateNetworkMappingRowsCore(net, PendingConnectionStatus);
                _removalDisplayRowsByNet[net] = CreateNetworkMappingRowsCore(net, RemovalConnectionStatus);
            }

            _displayRowByClip = new Dictionary<ClipBranch, FaultRow>(
                ReferenceEqualityComparer.Instance);
            _removalDisplayRowByClip = new Dictionary<ClipBranch, FaultRow>(
                ReferenceEqualityComparer.Instance);
            foreach (ClipBranch branch in prepared.Model.Clip?.Branches ?? [])
            {
                _displayRowByClip[branch] = CreateMissingClipConnectionRow(
                    prepared.Model.Clip!, branch, PendingConnectionStatus);
                _removalDisplayRowByClip[branch] = CreateMissingClipConnectionRow(
                    prepared.Model.Clip!, branch, RemovalConnectionStatus);
            }
            _clipCommonDisplayRow = prepared.Model.Clip is null
                ? null
                : CreateClipCommonDisplayRow(prepared.Model.Clip, PendingConnectionStatus);
            _latchedClipKeys.Clear();
            ResetUnsafe();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetModel(ProductModel model) => CommitPreparedModel(PrepareModel(model));

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

    static Dictionary<PinRecord, int> BuildDisplayOrderByPin(ProductModel model)
    {
        var result = new Dictionary<PinRecord, int>(ReferenceEqualityComparer.Instance);
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string NormalizeConnector(string connector) => (connector ?? string.Empty).Trim();

        string Find(string connector)
        {
            connector = NormalizeConnector(connector);
            if (!parent.TryGetValue(connector, out string? value))
            {
                parent[connector] = connector;
                return connector;
            }

            if (value.Equals(connector, StringComparison.OrdinalIgnoreCase))
                return connector;

            string root = Find(value);
            parent[connector] = root;
            return root;
        }

        void Union(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return;

            string rootA = Find(a);
            string rootB = Find(b);
            if (!rootA.Equals(rootB, StringComparison.OrdinalIgnoreCase))
                parent[rootB] = rootA;
        }

        foreach (PinRecord pin in model.Pins)
        {
            if (!string.IsNullOrWhiteSpace(pin.Connector))
                Find(pin.Connector);
        }

        foreach (WireNet net in model.Nets)
        {
            string[] connectors = net.Pins
                .Select(pin => NormalizeConnector(pin.Connector))
                .Where(connector => !string.IsNullOrWhiteSpace(connector))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (int i = 1; i < connectors.Length; i++)
                Union(connectors[0], connectors[i]);
        }

        if (model.Clip is not null)
        {
            foreach (ClipBranch branch in model.Clip.Branches)
            {
                Union(model.Clip.CommonPin.Connector, branch.ClipPin.Connector);
                if (branch.TargetPin is not null)
                    Union(model.Clip.CommonPin.Connector, branch.TargetPin.Connector);
            }
        }

        var pinsByConnector = model.Pins
            .Where(pin => !string.IsNullOrWhiteSpace(pin.Connector))
            .GroupBy(pin => NormalizeConnector(pin.Connector), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var connectorsByComponent = pinsByConnector.Keys
            .GroupBy(Find, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                string[] connectors = group.ToArray();
                int minOriginal = connectors
                    .SelectMany(connector => pinsByConnector[connector])
                    .Select(pin => pin.OriginalOrder > 0 ? pin.OriginalOrder : int.MaxValue)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();

                return new
                {
                    Connectors = connectors,
                    MinOriginal = minOriginal,
                    Natural = connectors.Select(NaturalSortKey).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).First()
                };
            })
            .OrderBy(group => group.MinOriginal)
            .ThenBy(group => group.Natural, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        int order = 0;
        foreach (var component in connectorsByComponent)
        {
            foreach (string connector in component.Connectors
                         .OrderBy(connector => ConnectorOriginalOrder(connector, pinsByConnector))
                         .ThenBy(NaturalSortKey, StringComparer.OrdinalIgnoreCase))
            {
                foreach (PinRecord pin in pinsByConnector[connector]
                             .OrderBy(pin => pin.OriginalOrder > 0 ? pin.OriginalOrder : int.MaxValue)
                             .ThenBy(pin => NaturalSortKey(pin.PinNumber), StringComparer.OrdinalIgnoreCase)
                             .ThenBy(pin => pin.IoNumber))
                {
                    result[pin] = order++;
                }
            }
        }

        foreach (PinRecord pin in model.Pins)
        {
            if (!result.ContainsKey(pin))
                result[pin] = order++;
        }

        return result;
    }

    static int ConnectorOriginalOrder(
        string connector,
        IReadOnlyDictionary<string, PinRecord[]> pinsByConnector)
    {
        return pinsByConnector.TryGetValue(connector, out PinRecord[]? pins)
            ? pins.Select(pin => pin.OriginalOrder > 0 ? pin.OriginalOrder : int.MaxValue)
                .DefaultIfEmpty(int.MaxValue)
                .Min()
            : int.MaxValue;
    }

    static Dictionary<WireNet, int> BuildNetworkDisplayOrder(ProductModel model)
    {
        var result = new Dictionary<WireNet, int>(ReferenceEqualityComparer.Instance);
        var entries = model.Nets
            .Select((net, modelIndex) => new
            {
                Net = net,
                ModelIndex = modelIndex,
                RelationKey = ConnectorRelationKey(net),
                FirstOriginal = FirstOriginalOrder(net),
                RelationNatural = ConnectorRelationNaturalKey(net)
            })
            .Where(entry => IsEligibleProductionNet(entry.Net))
            .ToArray();

        var groupOrder = entries
            .GroupBy(entry => entry.RelationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Key = group.Key,
                FirstOriginal = group.Min(entry => entry.FirstOriginal),
                FirstModelIndex = group.Min(entry => entry.ModelIndex),
                Natural = group
                    .Select(entry => entry.RelationNatural)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .First()
            })
            .OrderBy(group => group.FirstOriginal)
            .ThenBy(group => group.FirstModelIndex)
            .ThenBy(group => group.Natural, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => new { group.Key, Index = index })
            .ToDictionary(group => group.Key, group => group.Index, StringComparer.OrdinalIgnoreCase);

        int order = 0;
        foreach (var group in entries
                     .GroupBy(entry => entry.RelationKey, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => groupOrder[group.Key]))
        {
            foreach (var entry in group
                         .OrderBy(item => item.FirstOriginal)
                         .ThenBy(item => item.ModelIndex))
            {
                result[entry.Net] = order++ * 1000;
            }
        }

        return result;
    }

    static string ConnectorRelationKey(WireNet net)
    {
        string[] connectors = NetworkConnectors(net)
            .Select(NaturalSortKey)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return connectors.Length == 0
            ? $"<no-connector>{net.Name}"
            : string.Join("\u001f", connectors);
    }

    static string ConnectorRelationNaturalKey(WireNet net) =>
        string.Join("\u001f", NetworkConnectors(net).Select(NaturalSortKey));

    static string[] NetworkConnectors(WireNet net)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connectors = new List<string>();

        foreach (PinRecord pin in net.Pins
                     .OrderBy(pin => pin.OriginalOrder > 0 ? pin.OriginalOrder : int.MaxValue)
                     .ThenBy(pin => pin.IoNumber))
        {
            string connector = (pin.Connector ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(connector) || !seen.Add(connector))
                continue;

            connectors.Add(connector);
        }

        return connectors.ToArray();
    }

    static int FirstOriginalOrder(WireNet net) =>
        net.Pins
            .Select(pin => pin.OriginalOrder > 0 ? pin.OriginalOrder : int.MaxValue)
            .DefaultIfEmpty(int.MaxValue)
            .Min();

    static string NaturalSortKey(string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        Match match = Regex.Match(
            normalized,
            @"^(?<prefix>\D*?)(?<number>\d+)(?<suffix>.*)$",
            RegexOptions.CultureInvariant);

        if (!match.Success)
            return normalized;

        return match.Groups["prefix"].Value +
               int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture)
                   .ToString("D10", CultureInfo.InvariantCulture) +
               match.Groups["suffix"].Value;
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
            ResetUnsafe();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ResetUnsafe()
    {
        _passedNets.Clear();
        _stableCounters.Clear();
        _currentActive.Clear();
        _currentConnections.Clear();
        _actualComponentByIo.Clear();
        _unexpectedIo.Clear();
        _wiringFaults.Clear();
        _candidateWiringFaults.Clear();
        _confirmedOpenKeys.Clear();
        _faultConfirmation.Reset();
        _contactUnstable = false;
        _contactLossTimedOut = false;
        _productStable = false;
        _readyToEvaluateProductFaults = false;
        _hasExpectedSourceCoverage = false;
        _lastFrameValid = false;
        _lastFrameSequence = 0;
        _lastFrameUnknownBytes = 0;
        _forceNextFrameChanged = true;
    }

    public void ResetProductCycle()
    {
        lock (_gate)
            _latchedClipKeys.Clear();

        Reset();
    }

    public bool ProcessFrame(ScanFrame frame, bool preserveConfirmedWiringFaults = false)
    {
        if (_disposed)
            return false;

        // Tuyệt đối không cho snapshot TestPin đi vào logic production.
        // Điều này ngăn que GND tạo cảnh báo đấu sai/chập giả.
        if (!_frameProcessingEnabled ||
            frame.Mode != BoardScanMode.Production ||
            !frame.Complete ||
            frame.UnknownBytes > 0)
            return false;

        Interlocked.Increment(ref _framesProcessed);
        bool changed;

        lock (_gate)
        {
            // Có thể SetFrameProcessingEnabled(false) xảy ra sau check phía
            // ngoài nhưng trước khi callback lấy được lock. Kiểm tra lại để
            // frame production cũ không tạo lỗi đúng lúc chuyển sang TestPin.
            ProductModel? model = _model;
            if (!_frameProcessingEnabled || frame.Mode != BoardScanMode.Production || model is null)
                return false;

            bool sameActive = _currentActive.SetEquals(frame.ActiveIo);
            bool sameConnections = ConnectionsEqual(_currentConnections, frame.Connections);
            bool passedChanged = false;
            bool previousContactUnstable = _contactUnstable;
            bool previousContactLossTimedOut = _contactLossTimedOut;
            bool previousProductStable = _productStable;
            bool previousReadyToEvaluate = _readyToEvaluateProductFaults;
            WiringFaultPair[] previousConfirmedWiringFaults = preserveConfirmedWiringFaults
                ? _wiringFaults.ToArray()
                : [];

            if (!sameActive)
            {
                _currentActive.Clear();
                foreach (int io in frame.ActiveIo)
                    _currentActive.Add(io);
            }

            if (!sameConnections)
            {
                _currentConnections.Clear();
                foreach (var pair in frame.Connections)
                    _currentConnections[pair.Key] = pair.Value.ToHashSet();

                // Một điểm dập chung có thể được firmware phát dưới dạng một
                // source với nhiều target. Các target không nhất thiết có cạnh
                // trực tiếp với nhau dù chúng thuộc cùng một cụm điện thật.
                // Dựng component thực tế một lần khi frame đổi để mọi WireNet
                // dùng chung I/O (ví dụ nhiều dây về IO518) được đánh giá đúng.
                _actualComponentByIo = BuildActualComponents(_currentConnections);
            }

            // Frame của bo thường lặp lại nguyên trạng nhiều lần. Chỉ dựng lại
            // topology continuity khi các cạnh thực sự đổi; fault debounce theo
            // thời gian bên dưới vẫn được Observe trên mọi complete frame.
            bool evaluateConnections =
                _forceNextFrameChanged ||
                !sameConnections ||
                _expectedConnectionScratch.Count == 0;
            if (evaluateConnections)
            {
                // Continuity vật lý là cạnh điện hai chiều. Htdrv có thể thấy
                // IO1->IO2 hoặc IO2->IO1 cho cùng một dây hai chân; cả hai đều
                // phải được tính là đúng network, không phải OPEN/missing.
                _expectedConnectionScratch.Clear();
                foreach (WireNet net in model.Nets)
                {
                    if (!IsEligibleProductionNet(net))
                        continue;

                    bool connected = IsWireNetConnected(net, _currentConnections);
                    _expectedConnectionScratch[_confirmationKeyByNet[net]] = connected;

                    if (!connected)
                    {
                        _stableCounters[net.Name] = 0;
                        passedChanged |= _passedNets.Remove(net.Name);
                        continue;
                    }

                    int stable = _stableCounters.GetValueOrDefault(net.Name) + 1;
                    _stableCounters[net.Name] = stable;

                    // Kết nối đúng được chấp nhận ngay trên một complete board snapshot.
                    // IoConfirm*/RequiredStableFrames chỉ còn thuộc đường xác nhận lỗi
                    // đấu sai/chập, không được làm chậm PASS sạch.
                    passedChanged |= _passedNets.Add(net.Name);
                }

                // CLIP không phải WireNet thường. A0/AO là common, còn aN là
                // tên nhánh; đầu còn lại phải tới đúng I/O ghi trên row aN. Kiểm
                // tra hai chiều để không phụ thuộc source/target mà firmware
                // chọn khi phát frame.
                if (model.Clip is not null)
                {
                    foreach (ClipBranch branch in model.Clip.Branches)
                    {
                        if (!IsEligibleClipBranch(model.Clip, branch))
                            continue;

                        string clipKey = ClipConfirmationKey(branch.NetName);
                        if (_latchedClipKeys.Contains(clipKey))
                        {
                            _expectedConnectionScratch[_confirmationKeyByClip[branch]] = true;
                            passedChanged |= _passedNets.Add(branch.NetName);
                            continue;
                        }

                        bool connected = IsClipBranchConnected(
                            model.Clip,
                            branch,
                            _currentConnections);
                        _expectedConnectionScratch[_confirmationKeyByClip[branch]] = connected;

                        if (!connected)
                        {
                            _stableCounters[branch.NetName] = 0;
                            passedChanged |= _passedNets.Remove(branch.NetName);
                            continue;
                        }

                        int stable = _stableCounters.GetValueOrDefault(branch.NetName) + 1;
                        _stableCounters[branch.NetName] = stable;

                        passedChanged |= _latchedClipKeys.Add(clipKey);
                        passedChanged |= _passedNets.Add(branch.NetName);
                    }
                }
            }

            bool hasProductActivity = HasProductActivityUnsafe(model);
            bool hasExpectedSourceCoverage = HasExpectedSourceCoverageUnsafe(model);
            bool allExpectedConnectionsPresent =
                _expectedConnectionScratch.Count > 0 &&
                _expectedConnectionScratch.Values.All(static connected => connected);
            _hasExpectedSourceCoverage = hasExpectedSourceCoverage;
            // Một cạnh continuity là hai chiều. Một số bo Htdrv phát đầu THT
            // canonical ở phía source, trong khi bo khác phát chính cạnh đó theo
            // chiều ngược lại. Khi toàn bộ mạng kỳ vọng đã hiện diện trong cùng
            // complete frame, chính các cạnh đó đã chứng minh đủ coverage để PASS;
            // không được khóa chu kỳ chỉ vì hướng source của firmware khác nhau.
            // Với topology chưa đủ, vẫn giữ source-coverage gate cũ để không xác
            // nhận WRONG/SHORT trong lúc người vận hành đang lắp sản phẩm.
            _readyToEvaluateProductFaults =
                hasProductActivity &&
                (hasExpectedSourceCoverage || allExpectedConnectionsPresent);
            _lastFrameValid = true;
            _lastFrameSequence = frame.Sequence;
            _lastFrameUnknownBytes = frame.UnknownBytes;

            // Snapshot đã được classifier xác định là đầu dò chỉ dùng để hiển
            // thị Pin đang chạm. Không cho snapshot này tạo candidate/confirmed
            // WRONG hoặc SHORT mới; các lỗi thật đã có trước đó vẫn được giữ.
            // PASS cần coverage đầy đủ của model; một cạnh SAI thật thì không.
            // Nếu operator chạm đủ hai đầu của một dây sai, BO đã trả về cạnh
            // vật lý đó và phải báo sau debounce riêng, không đợi 99 dây của
            // WH322244 được lắp xong.
            bool wiringChanged = !preserveConfirmedWiringFaults && UpdateWiringFaults(
                model,
                _expectedConnectionScratch,
                hasProductActivity,
                hasProductActivity);

            if (preserveConfirmedWiringFaults &&
                previousConfirmedWiringFaults.Length > 0 &&
                _wiringFaults.Count == 0)
            {
                foreach (WiringFaultPair fault in previousConfirmedWiringFaults)
                    _wiringFaults.Add(fault);

                _unexpectedIo.Clear();
                foreach (WiringFaultPair fault in _wiringFaults)
                {
                    _unexpectedIo.Add(fault.SourceIo);
                    _unexpectedIo.Add(fault.TargetIo);
                }

                wiringChanged = true;
            }

            changed =
                _forceNextFrameChanged ||
                !sameActive ||
                !sameConnections ||
                passedChanged ||
                wiringChanged ||
                previousContactUnstable != _contactUnstable ||
                previousContactLossTimedOut != _contactLossTimedOut ||
                previousProductStable != _productStable ||
                previousReadyToEvaluate != _readyToEvaluateProductFaults;

            _forceNextFrameChanged = false;
        }

        // Không block worker D2XX. TestViewModel sẽ marshal async sang UI.
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);

        return changed;
    }

    bool UpdateWiringFaults(
        ProductModel model,
        IReadOnlyDictionary<string, bool> expectedConnections,
        bool hasProductActivity,
        bool readyToEvaluateFaults)
    {
        // Đấu sai phải xét theo COMPONENT điện thật, không chỉ theo một chiều
        // source->target. Trace cho thấy một component nhiều nhánh có thể sinh
        // quan hệ ngược/transitive (ví dụ 14->26) nhưng vẫn là dây đúng.
        // Component THT được tính một lần khi SetModel; không union-find lại
        // mỗi frame. Đây là một trong các tối ưu quan trọng để scan gần tốc độ
        // Htdrv gốc khi model có hàng trăm pin.
        IReadOnlyDictionary<int, int> componentByIo = _componentByIo;
        HashSet<(int SourceIo, int TargetIo)> unexpectedNow = _unexpectedPairScratch;
        unexpectedNow.Clear();

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

                // Một word target thay source có thể được decoder biểu diễn
                // tạm thời là IOx -> IOx. Đây là một đầu đang chạm, không hề
                // chứng minh có dây nối sai; chỉ cạnh giữa HAI IO khác nhau mới
                // được phép tạo WRONG/SHORT candidate.
                if (source == target)
                    continue;

                bool targetMapped = componentByIo.TryGetValue(target, out int targetComponent);

                if (!sourceMapped || !targetMapped || sourceComponent != targetComponent)
                    unexpectedNow.Add((source, target));
            }
        }

        WiringFaultPair[] classified = unexpectedNow.Count == 0
            ? Array.Empty<WiringFaultPair>()
            : unexpectedNow
                .Select(pair => ClassifyUnexpectedPair(model, pair.SourceIo, pair.TargetIo))
                .ToArray();

        bool changed = false;
        if (!_candidateWiringFaults.SetEquals(classified))
        {
            changed = true;
            _candidateWiringFaults.Clear();
            foreach (WiringFaultPair fault in classified)
                _candidateWiringFaults.Add(fault);
        }

        ProductionFaultConfirmationSnapshot snapshot = _faultConfirmation.Observe(
            expectedConnections,
            classified.Length == 0
                ? Array.Empty<UnexpectedFaultObservation>()
                : classified.Select(fault => new UnexpectedFaultObservation(
                    fault.SourceIo,
                    fault.TargetIo,
                    fault.FaultType))
                .ToArray(),
            hasProductActivity,
            readyToEvaluateFaults);

        if (!_confirmedOpenKeys.SetEquals(snapshot.ConfirmedOpenKeys))
        {
            changed = true;
            _confirmedOpenKeys.Clear();
            foreach (string key in snapshot.ConfirmedOpenKeys)
                _confirmedOpenKeys.Add(key);
        }

        _contactUnstable = snapshot.ContactUnstable;
        _contactLossTimedOut = snapshot.ContactLossTimedOut;
        _productStable = snapshot.ProductStable;

        if (classified.Length == 0)
        {
            if (_wiringFaults.Count > 0 || _unexpectedIo.Count > 0)
            {
                changed = true;
                _wiringFaults.Clear();
                _unexpectedIo.Clear();
            }

            return changed;
        }

        WiringFaultPair[] confirmedFaults = classified
            .Where(fault => snapshot.ConfirmedUnexpectedPairs.Contains((fault.SourceIo, fault.TargetIo)))
            .ToArray();

        if (!_wiringFaults.SetEquals(confirmedFaults))
        {
            changed = true;
            _wiringFaults.Clear();
            foreach (WiringFaultPair fault in confirmedFaults)
                _wiringFaults.Add(fault);

            _unexpectedIo.Clear();
            foreach (WiringFaultPair fault in _wiringFaults)
            {
                _unexpectedIo.Add(fault.SourceIo);
                _unexpectedIo.Add(fault.TargetIo);
            }
        }

        return changed;
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
                // ExpectedSourceIo/ExpectedTargetIo vẫn giữ riêng bên dưới.
                // Reason chỉ mô tả kết nối sai THỰC TẾ để UI/popup/history gọn.
                $"IO{actualSource} đang nối nhầm IO{actualTarget}",
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
            changed = _wiringFaults.Count > 0 || _unexpectedIo.Count > 0 || _candidateWiringFaults.Count > 0;
            _wiringFaults.Clear();
            _candidateWiringFaults.Clear();
            _unexpectedIo.Clear();
            _faultConfirmation.ClearUnexpected();
            _productStable = false;
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<FaultDetail> BuildConfirmedOpenFaults()
    {
        // OPEN/missing expected connections are legacy/history data only.
        // Current Production flow must not generate product OPEN faults.
        return [];
    }

    private bool HasProductActivityUnsafe(ProductModel model)
    {
        foreach (KeyValuePair<int, HashSet<int>> pair in _currentConnections)
        {
            foreach (int target in pair.Value)
            {
                if (IsProductActivityEdge(model, pair.Key, target))
                    return true;
            }
        }

        return false;
    }

    private bool HasExpectedSourceCoverageUnsafe(ProductModel model)
    {
        int expectedSources = 0;

        foreach (WireNet net in model.Nets)
        {
            if (net.ExpectedActiveIo.Count == 0)
                continue;

            expectedSources++;
            if (!_currentConnections.ContainsKey(net.SourceIo))
                return false;
        }

        if (model.Clip is not null && model.Clip.Branches.Count > 0)
        {
            expectedSources++;
            if (!_currentConnections.ContainsKey(model.Clip.CommonIo))
                return false;
        }

        return expectedSources > 0;
    }

    private static string NetConfirmationKey(string name) => $"NET:{name}";
    private static string ClipConfirmationKey(string name) => $"CLIP:{name}";

    private static int ProductionExpectedNetCount(ProductModel model) =>
        model.Nets.Count(IsEligibleProductionNet) +
        (model.Clip?.Branches.Count(branch => IsEligibleClipBranch(model.Clip, branch)) ?? 0);

    private static bool IsEligibleProductionNet(WireNet net) =>
        net.SourceIo > 0 && net.ExpectedActiveIo.Count > 0;

    private static bool IsEligibleClipBranch(ClipTopology clip, ClipBranch branch) =>
        clip.CommonIo > 0 && branch.TargetIo > 0;

    private ExpectedNetworkDiagnostic[] BuildExpectedNetworkDiagnosticsUnsafe(ProductModel model)
    {
        var result = new List<ExpectedNetworkDiagnostic>();

        foreach (WireNet net in model.Nets)
        {
            if (!IsEligibleProductionNet(net))
                continue;

            result.Add(new ExpectedNetworkDiagnostic(
                NetConfirmationKey(net.Name),
                net.Name,
                net.IsSplice ? "normal-splice" : "normal",
                net.SourceIo,
                net.ExpectedActiveIo.ToArray(),
                _passedNets.Contains(net.Name)));
        }

        if (model.Clip is not null)
        {
            foreach (ClipBranch branch in model.Clip.Branches)
            {
                if (!IsEligibleClipBranch(model.Clip, branch))
                    continue;

                result.Add(new ExpectedNetworkDiagnostic(
                    ClipConfirmationKey(branch.NetName),
                    branch.NetName,
                    "CLIP",
                    model.Clip.CommonIo,
                    [branch.TargetIo],
                    _passedNets.Contains(branch.NetName) ||
                    _latchedClipKeys.Contains(ClipConfirmationKey(branch.NetName))));
            }
        }

        return result.ToArray();
    }

    private int CountPassedExpectedUnsafe(ProductModel model)
    {
        int count = model.Nets.Count(net =>
            IsEligibleProductionNet(net) &&
            _passedNets.Contains(net.Name));

        if (model.Clip is not null)
        {
            count += model.Clip.Branches.Count(branch =>
                IsEligibleClipBranch(model.Clip, branch) &&
                (_passedNets.Contains(branch.NetName) ||
                 _latchedClipKeys.Contains(ClipConfirmationKey(branch.NetName))));
        }

        return count;
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

    private bool IsWireNetConnected(
        WireNet net,
        IReadOnlyDictionary<int, HashSet<int>> connections)
    {
        if (!IsEligibleProductionNet(net))
            return false;

        HashSet<int> reachable = BuildReachableNetEndpoints(net, connections);
        return net.IoNumbers
            .Where(io => io > 0)
            .Distinct()
            .All(reachable.Contains);
    }

    private bool IsEndpointConnectedWithinNet(
        WireNet net,
        int endpoint,
        IReadOnlyDictionary<int, HashSet<int>> connections)
    {
        if (endpoint == net.SourceIo)
            return IsWireNetConnected(net, connections);

        return BuildReachableNetEndpoints(net, connections).Contains(endpoint);
    }

    private int CountDisconnectedEndpoints(
        WireNet net,
        IReadOnlyDictionary<int, HashSet<int>> connections)
    {
        if (!IsEligibleProductionNet(net))
            return 0;

        HashSet<int> reachable = BuildReachableNetEndpoints(net, connections);
        return net.ExpectedActiveIo.Count(io => !reachable.Contains(io));
    }

    private HashSet<int> BuildReachableNetEndpoints(
        WireNet net,
        IReadOnlyDictionary<int, HashSet<int>> connections)
    {
        HashSet<int> endpoints = net.IoNumbers
            .Where(io => io > 0)
            .Distinct()
            .ToHashSet();
        var reachable = new HashSet<int>();
        if (net.SourceIo <= 0 || !endpoints.Contains(net.SourceIo))
            return reachable;

        if (!_actualComponentByIo.TryGetValue(net.SourceIo, out int sourceComponent))
            return reachable;

        foreach (int endpoint in endpoints)
        {
            if (_actualComponentByIo.TryGetValue(endpoint, out int component) &&
                component == sourceComponent)
                reachable.Add(endpoint);
        }

        return reachable;
    }

    private static Dictionary<int, int> BuildActualComponents(
        IReadOnlyDictionary<int, HashSet<int>> connections)
    {
        var parent = new Dictionary<int, int>();

        int Find(int value)
        {
            if (!parent.TryGetValue(value, out int current))
            {
                parent[value] = value;
                return value;
            }

            if (current == value)
                return value;

            int root = Find(current);
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

        foreach ((int source, HashSet<int> targets) in connections)
        {
            Find(source);
            foreach (int target in targets)
                Union(source, target);
        }

        return parent.Keys.ToDictionary(io => io, Find);
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

    static bool ConnectionsEqual(
        IReadOnlyDictionary<int, HashSet<int>> current,
        IReadOnlyDictionary<int, IReadOnlySet<int>> next)
    {
        if (current.Count != next.Count)
            return false;

        foreach (KeyValuePair<int, HashSet<int>> pair in current)
        {
            if (!next.TryGetValue(pair.Key, out IReadOnlySet<int>? nextTargets) ||
                !pair.Value.SetEquals(nextTargets))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Bảng động theo đúng cách vận hành Htdrv:
    /// - Ban đầu hiển thị endpoint rows của các network chưa hoàn thành.
    /// - Network PASS sạch: mọi endpoint row của network biến mất khỏi bảng.
    /// - Network mở lại: endpoint rows tự hiện lại theo thứ tự model.
    /// - Splice nhiều nhánh chỉ biến mất khi toàn bộ logical network đã đạt.
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
            // HTDRV_WIRING_FAIL_DISPLAY_2026-09-05: chỉ confirmed fault trong
            // _wiringFaults được phép thay presentation. Candidate tuyệt đối
            // không đi vào bảng operator.
            HashSet<int> diagnosticIos = [];
            rows.AddRange(BuildConfirmedWiringDisplayRows(model, diagnosticIos));

            // CLIP được kiểm tra riêng: mọi nhánh dùng chung A0 nhưng mỗi aN
            // phải đi tới đúng I/O được cấu hình trên row aN. Chỉ nhánh chưa
            // đạt mới còn trên bảng.
            if (model.Clip is not null)
            {
                if (!AnyClipBranchLatched(model.Clip) &&
                    _clipCommonDisplayRow is not null &&
                    !diagnosticIos.Contains(model.Clip.CommonIo))
                    rows.Add(_clipCommonDisplayRow);

                foreach (ClipBranch branch in OrderedClipBranches(model.Clip))
                {
                    if (!IsEligibleClipBranch(model.Clip, branch))
                        continue;

                    if (_latchedClipKeys.Contains(ClipConfirmationKey(branch.NetName)))
                        continue;

                    if (!diagnosticIos.Contains(branch.TargetIo) &&
                        _displayRowByClip.TryGetValue(branch, out FaultRow? row))
                        rows.Add(row);
                }
            }

            // NETWORK MAPPING là danh sách endpoint chưa hoàn thành.
            // Một WireName vẫn là một logical network; khi network PASS sạch,
            // endpoint rows chỉ biến mất khỏi presentation, không bị xóa khỏi
            // model hay expected network state.
            foreach (WireNet net in model.Nets)
            {
                if (!IsEligibleProductionNet(net))
                    continue;

                if (_passedNets.Contains(net.Name) ||
                    net.IoNumbers.Any(diagnosticIos.Contains))
                    continue;

                if (_displayRowsByNet.TryGetValue(net, out FaultRow[]? cachedRows))
                    rows.AddRange(cachedRows);
            }
        }

        // Fault rows được thêm trước, các row tĩnh đã mang DisplayOrder từ lúc
        // load model. Không OrderBy toàn bộ bảng theo từng frame.
        return rows;
    }

    /// <summary>
    /// Presentation riêng cho giai đoạn chờ tháo: quan hệ nào còn thật trên
    /// jig thì còn row; tháo quan hệ nào thì row của network đó mất ngay.
    /// Không thay đổi passed-net latch hay bất kỳ điều kiện PASS/FAIL nào.
    /// </summary>
    public IReadOnlyList<FaultRow> BuildRemovalRows()
    {
        ProductModel? model = _model;
        if (model is null)
            return [];

        var rows = new List<FaultRow>();
        lock (_gate)
        {
            HashSet<int> diagnosticIos = [];
            rows.AddRange(BuildConfirmedWiringDisplayRows(model, diagnosticIos));

            foreach (WireNet net in model.Nets)
            {
                if (IsEligibleProductionNet(net) &&
                    !net.IoNumbers.Any(diagnosticIos.Contains) &&
                    IsWireNetConnected(net, _currentConnections) &&
                    _removalDisplayRowsByNet.TryGetValue(net, out FaultRow[]? cachedRows))
                    rows.AddRange(cachedRows);
            }

            if (model.Clip is not null)
            {
                foreach (ClipBranch branch in OrderedClipBranches(model.Clip))
                {
                    if (IsEligibleClipBranch(model.Clip, branch) &&
                        IsClipBranchConnected(model.Clip, branch, _currentConnections) &&
                        _removalDisplayRowByClip.TryGetValue(branch, out FaultRow? row))
                        rows.Add(row);
                }
            }
        }
        return rows;
    }

    private IReadOnlyList<FaultRow> BuildConfirmedWiringDisplayRows(
        ProductModel model,
        HashSet<int> diagnosticIos)
    {
        var rows = new List<FaultRow>();
        HashSet<string> keys = new(StringComparer.Ordinal);

        foreach (WiringFaultPair fault in _wiringFaults
                     .OrderBy(item => item.SourceIo)
                     .ThenBy(item => item.TargetIo))
        {
            int[] relation = [fault.SourceIo, fault.TargetIo];
            if (TryResolveExpectedDisplayRelation(
                    model,
                    fault,
                    out int expectedSource,
                    out int expectedTarget))
            {
                int wrongPeer = expectedSource == fault.SourceIo
                    ? fault.TargetIo
                    : fault.SourceIo;
                AddDiagnosticRow(rows, keys, diagnosticIos, model, fault,
                    expectedSource, "SAI DÂY", FaultKind.WrongWiring,
                    ProductFaultType.WrongWiring, relation);
                AddDiagnosticRow(rows, keys, diagnosticIos, model, fault,
                    wrongPeer, "CHẬP MẠCH", FaultKind.Short,
                    ProductFaultType.ShortCircuit, relation);
                AddDiagnosticRow(rows, keys, diagnosticIos, model, fault,
                    expectedTarget, "HỞ MẠCH", FaultKind.Open,
                    ProductFaultType.OpenCircuit, relation);
                continue;
            }

            AddDiagnosticRow(rows, keys, diagnosticIos, model, fault,
                fault.SourceIo, "CHẬP MẠCH", FaultKind.Short,
                ProductFaultType.ShortCircuit, relation);
            AddDiagnosticRow(rows, keys, diagnosticIos, model, fault,
                fault.TargetIo, "CHẬP MẠCH", FaultKind.Short,
                ProductFaultType.ShortCircuit, relation);
        }

        return rows;
    }

    private static bool TryResolveExpectedDisplayRelation(
        ProductModel model,
        WiringFaultPair fault,
        out int expectedSource,
        out int expectedTarget)
    {
        if (fault.ExpectedSourceIo is int suppliedSource &&
            fault.ExpectedTargetIo is int suppliedTarget)
        {
            expectedSource = suppliedSource;
            expectedTarget = suppliedTarget;
            return true;
        }

        // Firmware có thể báo cùng cạnh điện theo chiều ngược. Chỉ phục hồi
        // metadata presentation từ source topology đã biết; không đổi internal
        // ProductFaultType/classifier.
        WireNet? sourceNet = model.Nets.FirstOrDefault(net =>
            net.SourceIo == fault.SourceIo || net.SourceIo == fault.TargetIo);
        if (sourceNet is not null)
        {
            expectedSource = sourceNet.SourceIo;
            int actualPeer = expectedSource == fault.SourceIo
                ? fault.TargetIo
                : fault.SourceIo;
            expectedTarget = sourceNet.ExpectedActiveIo.FirstOrDefault(io => io != actualPeer);
            if (expectedTarget <= 0)
                expectedTarget = sourceNet.ExpectedActiveIo.FirstOrDefault();
            return expectedTarget > 0;
        }

        if (model.Clip is not null &&
            (model.Clip.CommonIo == fault.SourceIo || model.Clip.CommonIo == fault.TargetIo))
        {
            expectedSource = model.Clip.CommonIo;
            int actualPeer = expectedSource == fault.SourceIo
                ? fault.TargetIo
                : fault.SourceIo;
            expectedTarget = model.Clip.Branches
                .Select(branch => branch.TargetIo)
                .FirstOrDefault(io => io > 0 && io != actualPeer);
            return expectedTarget > 0;
        }

        expectedSource = 0;
        expectedTarget = 0;
        return false;
    }

    private void AddDiagnosticRow(
        List<FaultRow> rows,
        HashSet<string> keys,
        HashSet<int> diagnosticIos,
        ProductModel model,
        WiringFaultPair fault,
        int io,
        string status,
        FaultKind kind,
        ProductFaultType productFaultType,
        int[] relation)
    {
        if (io <= 0)
            return;

        PinRecord? pin = ResolveProductionDisplayPin(model, io);
        string network = ResolveTopologyName(model, io);
        string key = $"{network}|{io}|{status}|{Math.Min(relation[0], relation[1])}:{Math.Max(relation[0], relation[1])}";
        if (!keys.Add(key))
            return;

        diagnosticIos.Add(io);
        bool unused = pin is null;
        rows.Add(new FaultRow
        {
            Kind = kind,
            ProductFaultType = productFaultType,
            FaultType = unused ? string.Empty : ResolveTopologyType(model, io),
            Io = io,
            DisplayOrder = pin is null ? int.MaxValue : ResolveDisplayOrder(pin),
            ExpectedSourceIo = fault.ExpectedSourceIo,
            ExpectedTargetIo = fault.ExpectedTargetIo,
            ActualSourceIo = fault.SourceIo,
            ActualTargetIo = fault.TargetIo,
            RelatedIos = relation,
            // HTDRV_UNUSED_IO_DISPLAY_2026-09-05
            Connector = unused ? $"IO({io})" : pin!.Connector,
            Pin = unused ? string.Empty : pin!.PinNumber,
            WireName = unused ? string.Empty : pin!.WireName,
            Splice = unused ? string.Empty : pin!.SpliceName,
            Section = unused ? string.Empty : pin!.Section,
            Color = unused ? string.Empty : pin!.Color,
            IoCnPnOverride = unused ? $"IO{io}" : string.Empty,
            Status = status
        });
    }

    private PinRecord? ResolveProductionDisplayPin(ProductModel model, int io) =>
        model.Pins
            .Where(pin => pin.IoNumber == io && !string.IsNullOrWhiteSpace(pin.WireName))
            .Where(pin => (_netsByPin.GetValueOrDefault(pin)?.Length ?? 0) > 0 ||
                          model.Clip?.IsSpecialPin(pin) == true)
            .OrderBy(ResolveDisplayOrder)
            .FirstOrDefault();

    private static string ResolveTopologyName(ProductModel model, int io) =>
        model.Nets.FirstOrDefault(net => net.IoNumbers.Contains(io))?.Name ??
        model.Clip?.Branches.FirstOrDefault(branch => branch.TargetIo == io)?.NetName ??
        (model.Clip?.CommonIo == io ? "CLIP" : string.Empty);

    private static string ResolveTopologyType(ProductModel model, int io)
    {
        WireNet? net = model.Nets.FirstOrDefault(item => item.IoNumbers.Contains(io));
        if (net is not null)
            return !net.IsSplice && net.IoNumbers.Where(value => value > 0).Distinct().Count() == 2
                ? "Đơn"
                : "Nối chung";
        return model.Clip is not null &&
               (model.Clip.CommonIo == io || model.Clip.Branches.Any(branch => branch.TargetIo == io))
            ? "Nối chung"
            : string.Empty;
    }

    FaultRow CreateWiringFaultRow(PinRecord pin)
    {
        WiringFaultPair? fault = _wiringFaults.FirstOrDefault(item =>
            item.SourceIo == pin.IoNumber || item.TargetIo == pin.IoNumber);

        ProductFaultType type = fault?.FaultType ?? ProductFaultType.WrongWiring;

        // FAIL trên TestWindow chỉ hiển thị đầu I/O đang nối sai với dòng hiện tại.
        // ExpectedSourceIo/ExpectedTargetIo vẫn giữ nguyên cho logic/history.
        string status = fault is null
            ? $"IO {pin.IoNumber}"
            : BuildWiringFaultStatus(_model!, fault, pin.IoNumber);

        return new FaultRow
        {
            Kind = type == ProductFaultType.ShortCircuit
                ? FaultKind.Short
                : FaultKind.WrongWiring,
            ProductFaultType = type,
            FaultType = FaultTypeCatalog.DisplayName(type),
            Io = pin.IoNumber,
            DisplayOrder = ResolveDisplayOrder(pin),
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

    /// <summary>
    /// Chuẩn hiển thị FAIL ở cột Trạng thái.
    /// Ví dụ: dòng IO14 nối sai với IO38, IO38 thuộc connector 2 / pin 7:
    ///     IO 38 (CN 2 - PIN 7)
    /// Dòng IO38 sẽ hiển thị ngược lại:
    ///     IO 14 (CN 1 - PIN 14)
    /// Nếu I/O đối diện không có map trong THT thì chỉ hiện "IO 38".
    /// </summary>
    private static string BuildWiringFaultStatus(
        ProductModel model,
        WiringFaultPair fault,
        int currentIo)
    {
        int peerIo;

        if (fault.SourceIo == currentIo)
            peerIo = fault.TargetIo;
        else if (fault.TargetIo == currentIo)
            peerIo = fault.SourceIo;
        else
            peerIo = fault.TargetIo > 0 ? fault.TargetIo : fault.SourceIo;

        return FormatFaultPeerIo(model, peerIo);
    }

    private static string FormatFaultPeerIo(ProductModel model, int io)
    {
        if (io <= 0)
            return string.Empty;

        // Một Global IO có thể xuất hiện nhiều row trong THT. Ưu tiên row có
        // WireName thật và thứ tự gốc nhỏ nhất để kết quả hiển thị ổn định.
        PinRecord? peerPin = model.Pins
            .Where(pin => pin.IoNumber == io)
            .OrderBy(pin => string.IsNullOrWhiteSpace(pin.WireName) ? 1 : 0)
            .ThenBy(pin => pin.OriginalOrder > 0 ? pin.OriginalOrder : int.MaxValue)
            .FirstOrDefault();

        if (peerPin is null)
            return $"IO {io}";

        string connector = (peerPin.Connector ?? string.Empty).Trim();
        string pinNumber = (peerPin.PinNumber ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(connector) &&
            !string.IsNullOrWhiteSpace(pinNumber))
        {
            return $"IO {io} (CN {connector} - PIN {pinNumber})";
        }

        if (!string.IsNullOrWhiteSpace(connector))
            return $"IO {io} (CN {connector})";

        if (!string.IsNullOrWhiteSpace(pinNumber))
            return $"IO {io} (PIN {pinNumber})";

        return $"IO {io}";
    }

    bool HasWiringFaultForNet(WireNet net)
    {
        HashSet<int> endpoints = net.IoNumbers
            .Where(io => io > 0)
            .Distinct()
            .ToHashSet();

        if (endpoints.Count == 0)
            return false;

        return _candidateWiringFaults.Any(fault =>
                   endpoints.Contains(fault.SourceIo) || endpoints.Contains(fault.TargetIo)) ||
               _wiringFaults.Any(fault =>
                   endpoints.Contains(fault.SourceIo) || endpoints.Contains(fault.TargetIo));
    }

    IReadOnlyList<FaultRow> CreateNetworkMappingRows(WireNet net, bool connected)
    {
        if (connected)
            return [];

        return _displayRowsByNet.TryGetValue(net, out FaultRow[]? cachedRows)
            ? cachedRows
            : CreateNetworkMappingRowsCore(net, PendingConnectionStatus);
    }

    private FaultRow[] CreateNetworkMappingRowsCore(WireNet net, string status)
    {

        PinRecord[] pins = net.Pins
            .Where(pin => pin.IoNumber > 0)
            .GroupBy(pin => pin.IoNumber)
            .Select(group => group.First())
            .OrderBy(ResolveDisplayOrder)
            .ToArray();

        if (pins.Length == 0)
            return [];

        HashSet<int> endpointIos = net.IoNumbers
            .Where(io => io > 0)
            .Distinct()
            .ToHashSet();
        int[] relatedIos = endpointIos
            .OrderBy(io => ResolvePeerOrder(net, io))
            .ToArray();
        string topologyType = !net.IsSplice && endpointIos.Count == 2
            ? "Đơn"
            : "Nối chung";

        return pins
            .Select((pin, endpointIndex) =>
            {
                int? firstPeer = endpointIos
                    .Where(io => io != pin.IoNumber)
                    .OrderBy(io => ResolvePeerOrder(net, io))
                    .Cast<int?>()
                    .FirstOrDefault();

                return new FaultRow
                {
                    Kind = FaultKind.MissingConnection,
                    ProductFaultType = ProductFaultType.None,
                    FaultType = topologyType,
                    Io = pin.IoNumber,
                    DisplayOrder = ResolveNetworkEndpointDisplayOrder(net, pin, endpointIndex),
                    ExpectedSourceIo = net.SourceIo,
                    ExpectedTargetIo = firstPeer,
                    RelatedIos = relatedIos,
                    Connector = pin.Connector,
                    Pin = pin.PinNumber,
                    WireName = pin.WireName,
                    Splice = pin.SpliceName,
                    Section = pin.Section,
                    Color = pin.Color,
                    Status = status
                };
            })
            .ToArray();
    }

    public bool SuppressProbeRelatedWiringFaults(IReadOnlyCollection<int> probeIos)
    {
        ArgumentNullException.ThrowIfNull(probeIos);
        HashSet<int> suppressed = probeIos.Where(io => io > 0).ToHashSet();
        if (suppressed.Count == 0)
            return false;

        bool changed;
        lock (_gate)
        {
            changed = _candidateWiringFaults.RemoveWhere(fault =>
                          suppressed.Contains(fault.SourceIo) || suppressed.Contains(fault.TargetIo)) > 0;

            // Không xóa _wiringFaults đã confirmed: đó có thể là lỗi SHORT/WRONG
            // thật tồn tại trước khi người vận hành chạm đầu dò vào cùng I/O.
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
        return changed;
    }

    /// <summary>
    /// Xác nhận connector được chọn cho máy Leak đang thực sự có continuity
    /// đúng theo topology THT. Không suy diễn kênh Leak từ số I/O/connector.
    /// </summary>
    public bool IsConnectorConnected(string? connectorId)
    {
        if (string.IsNullOrWhiteSpace(connectorId))
            return false;

        lock (_gate)
        {
            if (_model is null)
                return false;

            ConnectorDefinition? connector = _model.Connectors.FirstOrDefault(item =>
                string.Equals(item.ConnectorId, connectorId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (connector is null)
                return false;

            WireNet[] requiredNets = _model.Nets
                .Where(IsEligibleProductionNet)
                .Where(net => net.Pins.Any(pin =>
                    string.Equals(pin.Connector, connector.ConnectorId, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            ClipBranch[] requiredClipBranches = _model.Clip?.Branches
                .Where(branch => IsEligibleClipBranch(_model.Clip, branch))
                .Where(branch =>
                    string.Equals(branch.ClipPin.Connector, connector.ConnectorId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(branch.TargetPin?.Connector, connector.ConnectorId, StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? [];

            int requiredRelationCount = requiredNets.Length + requiredClipBranches.Length;
            if (requiredRelationCount == 0)
                return false;

            return requiredNets.All(net => IsWireNetConnected(net, _currentConnections)) &&
                   requiredClipBranches.All(branch =>
                       IsClipBranchConnected(_model.Clip!, branch, _currentConnections));
        }
    }

    int ResolveNetworkEndpointDisplayOrder(WireNet net, PinRecord pin, int endpointIndex) =>
        _displayOrderByNet.TryGetValue(net, out int baseOrder)
            ? baseOrder + Math.Clamp(endpointIndex, 0, 999)
            : ResolveDisplayOrder(pin);

    // Giữ method compatibility cho code/test cũ nếu còn gọi trực tiếp.
    FaultRow CreateMissingConnectionRow(WireNet net) =>
        CreateNetworkMappingRows(net, connected: false).First();

    FaultRow CreateMissingClipConnectionRow(ClipTopology clip, ClipBranch branch, string status)
    {
        PinRecord displayPin = branch.TargetPin ?? branch.ClipPin;
        return new FaultRow
        {
            Kind = FaultKind.MissingConnection,
            ProductFaultType = ProductFaultType.None,
            FaultType = "Nối chung",
            Io = branch.TargetIo,
            DisplayOrder = ClipDisplayOrder(branch),
            ExpectedSourceIo = clip.CommonIo,
            ExpectedTargetIo = branch.TargetIo,
            Connector = displayPin.Connector,
            Pin = displayPin.PinNumber,
            WireName = ResolveClipDisplayName(displayPin.WireName, branch.Name),
            Splice = displayPin.SpliceName,
            Section = displayPin.Section,
            Color = displayPin.Color,
            Status = status
        };
    }

    FaultRow CreateClipCommonDisplayRow(ClipTopology clip, string status)
    {
        PinRecord common = clip.CommonPin;
        return new FaultRow
        {
            Kind = FaultKind.MissingConnection,
            ProductFaultType = ProductFaultType.None,
            FaultType = "Nối chung",
            Io = common.IoNumber,
            DisplayOrder = ClipDisplayOrderBase,
            Connector = common.Connector,
            Pin = common.PinNumber,
            WireName = ResolveClipCommonDisplayName(common),
            Splice = common.SpliceName,
            Section = common.Section,
            Color = common.Color,
            Status = status
        };
    }

    bool AnyClipBranchLatched(ClipTopology clip) =>
        clip.Branches.Any(branch => _latchedClipKeys.Contains(ClipConfirmationKey(branch.NetName)));

    static IEnumerable<ClipBranch> OrderedClipBranches(ClipTopology clip) =>
        clip.Branches
            .OrderBy(ClipOriginalOrder)
            .ThenBy(branch => branch.BranchNumber)
            .ThenBy(branch => branch.TargetIo);

    static int ClipOriginalOrder(ClipBranch branch)
    {
        int clipOrder = branch.ClipPin.OriginalOrder > 0
            ? branch.ClipPin.OriginalOrder
            : int.MaxValue;
        int targetOrder = branch.TargetPin?.OriginalOrder > 0
            ? branch.TargetPin.OriginalOrder
            : int.MaxValue;

        return Math.Min(clipOrder, targetOrder);
    }

    static int ClipDisplayOrder(ClipBranch branch)
    {
        int original = ClipOriginalOrder(branch);
        int sequence = original == int.MaxValue
            ? Math.Clamp(branch.BranchNumber, 1, 999_999)
            : Math.Clamp(original, 1, 999_999);

        return ClipDisplayOrderBase + sequence;
    }

    static string FirstNotEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    static string ResolveClipDisplayName(string? pinWireName, string branchName)
    {
        string candidate = FirstNotEmpty(pinWireName);
        if (candidate.StartsWith("CLIP-", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("CLIP ", StringComparison.OrdinalIgnoreCase))
        {
            return branchName;
        }

        return FirstNotEmpty(candidate, branchName);
    }

    static string ResolveClipCommonDisplayName(PinRecord common)
    {
        string candidate = FirstNotEmpty(common.WireName);
        if (candidate.StartsWith("CLIP-", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("CLIP ", StringComparison.OrdinalIgnoreCase))
        {
            return FirstNotEmpty(common.PinType, common.PinNumber, "A0");
        }

        return FirstNotEmpty(candidate, common.PinType, common.PinNumber);
    }

    int ResolveDisplayOrder(PinRecord pin) =>
        _displayOrderByPin.TryGetValue(pin, out int order)
            ? order
            : pin.OriginalOrder > 0
                ? pin.OriginalOrder
                : int.MaxValue;

    int ResolvePeerOrder(WireNet net, int io)
    {
        PinRecord? pin = net.Pins.FirstOrDefault(item => item.IoNumber == io);
        return pin is null ? int.MaxValue : ResolveDisplayOrder(pin);
    }


    /// <summary>
    /// Sau khi người vận hành XÁC NHẬN hàng lỗi, chỉ relay đã được cài là
    /// relay MỞ JIG LỖI được pulse. Không chạy chuỗi MARKING của PASS.
    /// Một số máy đấu ngược R1/R2 nên số relay được chọn sau khi thử tay.
    /// </summary>
    public Task EjectFaultProductAsync(CancellationToken ct = default)
        => PulseJigRelayAsync(ct);

    /// <summary>
    /// Manual/production helper V15.2: Relay 1 JIG chỉ được pulse đúng một lần.
    /// Dù delay bị hủy hoặc board phát sinh exception, finally vẫn cố đưa toàn bộ relay về OFF.
    /// </summary>
    public Task PulseJigRelayAsync(CancellationToken ct = default)
    {
        int relay = ConfiguredJigRelay;
        string relayName = $"R{relay} JIG";
        return _production.JigEjectRelayEnabled
            ? PulseRelaySafeAsync(relay, PulseDurationForRelay(relay), relayName, ct)
            : SkipDisabledRelayAsync(relayName, ct);
    }

    /// <summary>Relay MARKING theo kiểu đấu máy chỉ được pulse đúng một lần và luôn trả về OFF.</summary>
    public Task PulseMarkingRelayAsync(CancellationToken ct = default)
    {
        int relay = ConfiguredMarkingRelay;
        string relayName = $"R{relay} MARKING";
        return _production.PassMarkingRelayEnabled
            ? PulseRelaySafeAsync(relay, PulseDurationForRelay(relay), relayName, ct)
            : SkipDisabledRelayAsync(relayName, ct);
    }

    /// <summary>Thử đúng relay vật lý theo số, không áp dụng ánh xạ vai trò production.</summary>
    public Task PulsePhysicalRelayAsync(int relay, CancellationToken ct = default)
    {
        if (relay is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(relay), relay, "Relay vật lý phải là 1 hoặc 2.");

        return PulseRelaySafeAsync(relay, PulseDurationForRelay(relay), $"R{relay} MANUAL", ct);
    }

    private int ConfiguredJigRelay => _production.RelayWiringMode == 1 ? 2 : 1;
    private int ConfiguredMarkingRelay => _production.RelayWiringMode == 1 ? 1 : 2;
    private int PulseDurationForRelay(int relay) => relay == 2
        ? _production.Relay2MarkingPulseMs
        : _production.Relay1JigPulseMs;

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
            string relayMarker = relay == MarkingRelay
                ? "T_RELAY2"
                : relay == JigEjectRelay
                    ? "T_RELAY1"
                    : $"T_RELAY{relay}";
            AsyncFileLogService.Current.Performance(
                $"PASS_LATENCY {relayMarker}_START relay={relay} name=\"{relayName}\" pulse_ms={pulseMs}");
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
                string relayMarker = relay == MarkingRelay
                    ? "T_RELAY2"
                    : relay == JigEjectRelay
                        ? "T_RELAY1"
                        : $"T_RELAY{relay}";
                AsyncFileLogService.Current.Performance(
                    $"PASS_LATENCY {relayMarker}_END relay={relay} name=\"{relayName}\"");
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

    private async Task SkipDisabledRelayAsync(string relayName, CancellationToken ct)
    {
        await ForceAllRelaysOffAsync(relayName + " DISABLED", ct);
        AsyncFileLogService.Current.Test($"RELAY {relayName} SKIPPED - disabled by Production Settings");
    }

    public Task<List<ResistanceResult>> MeasureResistanceAsync(CancellationToken ct = default) =>
        MeasureResistanceAsync(null, ct);

    public async Task<List<ResistanceResult>> MeasureResistanceAsync(
        Action<ResistanceResult>? onChannelUpdated,
        CancellationToken ct = default)
    {
        ProductModel? model = _model;
        if (model is null)
            return [];

        foreach (ResistanceChannelSetting configured in _production.ResistanceChannels)
        {
            if (!configured.Enabled)
            {
                AsyncFileLogService.Current.Test(
                    $"[AUTO-R] {configured.Name} disabled -> skipped");
            }
            else if (configured.Channel == 0)
            {
                AsyncFileLogService.Current.Test(
                    $"[AUTO-R] {configured.Name} channel=0 -> skipped");
            }
        }

        List<ResistanceStep> enabledSteps = ResistanceMeasurementPlan.BuildEnabledSteps(_production);
        return await MeasureResistanceStepsAsync(enabledSteps, onChannelUpdated, ct);
    }

    public async Task<List<ResistanceResult>> MeasureResistanceStepsAsync(
        IReadOnlyList<ResistanceStep> steps,
        Action<ResistanceResult>? onChannelUpdated = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(steps);

        ResistanceStep[] enabledSteps = steps
            .Where(step => step.Channel is >= D2xxResistanceRouting.MinChannel and
                <= D2xxResistanceRouting.MaxChannel)
            .ToArray();
        if (enabledSteps.Length == 0)
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
            foreach (ResistanceStep step in enabledSteps)
            {
                ct.ThrowIfCancellationRequested();

                // Production Settings là nguồn duy nhất cho danh sách bước,
                // kênh vật lý và giới hạn Min/Max. Route phần cứng lấy từ
                // Selector D2XX được dựng canonical trực tiếp từ Channel,
                // không phụ thuộc block resistance THT hay RouteA/RouteB cũ.
                AsyncFileLogService.Current.Test(
                    $"[AUTO-R] {step.Name} enabled=true channel={step.Channel} " +
                    $"selector=0x{D2xxResistanceRouting.ToResistanceSelector(step.Channel):X2}");
                await _board.SelectResistanceRouteAsync(step, ct);

                var measuring = new ResistanceResult
                {
                    Name = step.Name,
                    Channel = step.Channel,
                    MinOhm = step.MinOhm,
                    MaxOhm = step.MaxOhm,
                    MeasurementStatus = "ĐANG ĐO"
                };
                onChannelUpdated?.Invoke(measuring);

                ResistanceResult result = await MeasureChannelOnceAsync(step, ct);
                results.Add(result);
                onChannelUpdated?.Invoke(result);
            }
        }
        finally
        {
            await _board.ReleaseResistanceRouteAsync(CancellationToken.None);
        }

        return results;
    }

    private async Task<ResistanceResult> MeasureChannelOnceAsync(
        ResistanceStep step,
        CancellationToken ct)
    {
        int minimumSettleMs = Math.Max(
            Math.Max(0, _settings.Keysight.SettleDelayMs),
            Math.Max(
                Math.Max(0, _production.ResistanceDelayMs),
                Math.Max(0, _settings.Test.ResistanceMinimumSettleMs)));
        AsyncFileLogService.Current.Test(
            $"[AUTO-R] CH{step.Channel} minimum settle start ms={minimumSettleMs}");
        if (minimumSettleMs > 0)
            await Task.Delay(minimumSettleMs, ct);
        AsyncFileLogService.Current.Test($"[AUTO-R] CH{step.Channel} minimum settle complete");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        double sample = await Task.Run(
            () => _visa.MeasureResistance(_settings.Keysight.Command),
            ct);
        bool open = !double.IsFinite(sample) ||
                    Math.Abs(sample) >= _settings.Test.ResistanceOpenThreshold;
        bool passed = !open && sample >= step.MinOhm && sample <= step.MaxOhm;

        AsyncFileLogService.Current.Test(open
            ? $"[AUTO-R] CH{step.Channel} sample#1=OPEN"
            : $"[AUTO-R] CH{step.Channel} sample#1={sample:0.###}");
        AsyncFileLogService.Current.Test(
            $"[AUTO-R] CH{step.Channel} single measurement time={stopwatch.ElapsedMilliseconds}ms " +
            $"result={(passed ? "PASS" : "FAIL")}");

        ResistanceResult result = BuildResistanceResult(
            step,
            valueOhm: open ? null : sample,
            open,
            stable: true,
            status: passed ? "PASS" : "FAIL",
            sampleCount: 1,
            stopwatch.ElapsedMilliseconds);
        LogResistanceDiagnostic(result);
        return result;
    }

    private void LogResistanceDiagnostic(ResistanceResult result)
    {
        string raw = string.IsNullOrWhiteSpace(_visa.LastRawResistanceResponse)
            ? "(not captured)"
            : _visa.LastRawResistanceResponse;
        string valueText = result.IsOpen
            ? "OPEN"
            : result.ValueOhm is double value
                ? FormattableString.Invariant($"{value:0.##########}")
                : "NO VALUE";

        AsyncFileLogService.Current.Test($"[RES] {result.ChannelText} raw instrument response = {raw}");
        AsyncFileLogService.Current.Test($"[RES] {result.ChannelText} ValueOhm = {valueText}");
        AsyncFileLogService.Current.Test(FormattableString.Invariant($"[RES] {result.ChannelText} MinOhm = {result.MinOhm:0.##########}"));
        AsyncFileLogService.Current.Test(FormattableString.Invariant($"[RES] {result.ChannelText} MaxOhm = {result.MaxOhm:0.##########}"));
        AsyncFileLogService.Current.Test($"[RES] {result.ChannelText} DisplayUnit = {result.DisplayUnitText}");
        AsyncFileLogService.Current.Test($"[RES] {result.ChannelText} DisplayValue = {result.Display}");
        AsyncFileLogService.Current.Test($"[RES] {result.ChannelText} Result = {result.ResultText}");
    }

    private static ResistanceResult BuildResistanceResult(
        ResistanceStep step,
        double? valueOhm,
        bool open,
        bool stable,
        string status,
        int sampleCount,
        long stabilizationTimeMs)
    {
        bool passed = stable &&
                      !open &&
                      valueOhm is double value &&
                      value >= step.MinOhm &&
                      value <= step.MaxOhm;

        return new ResistanceResult
        {
            Name = step.Name,
            Channel = step.Channel,
            ValueOhm = open ? null : valueOhm,
            MinOhm = step.MinOhm,
            MaxOhm = step.MaxOhm,
            IsOpen = open,
            IsStable = stable,
            Passed = passed,
            MeasurementStatus = status,
            SampleCount = sampleCount,
            StabilizationTimeMs = stabilizationTimeMs
        };
    }

    public async Task<bool> CompletePassAsync(
        IReadOnlyList<ResistanceResult> resistance,
        Action? onPassStarted = null,
        bool markingEnabled = true,
        bool continuityAlreadyValidated = false,
        CancellationToken ct = default)
    {
        ProductModel? model = _model;
        if (model is null)
            return false;

        int expectedResistanceCount = ResistanceMeasurementPlan.BuildEnabledSteps(_production).Count;
        bool resistanceOk = expectedResistanceCount == 0 ||
                            (resistance.Count == expectedResistanceCount &&
                             resistance.All(x => x.Passed));

        if ((!ContinuityPassed && !continuityAlreadyValidated) || !resistanceOk)
            return false;

        if (expectedResistanceCount == 0)
        {
            // Trace production thật:
            // continuity PASS -> STOP_SCAN -> RESET_CLEAR -> MARKING (Relay 2)
            // -> JIG EJECT (Relay 1).
            // STOP/RESET không làm mất trạng thái INIT, vì sau relay Htdrv
            // START_SCAN lại trực tiếp.
            await _board.StopScanAsync(ct);
            await _board.ResetClearAsync(ct);
        }

        int relayStartDelayMs = expectedResistanceCount > 0
            ? Math.Max(0, _settings.Test.PostResistanceRelayDelayMs)
            : 0;

        if (relayStartDelayMs > 0)
            await Task.Delay(relayStartDelayMs, ct);

        // Vai trò relay lấy từ kiểu đấu của từng máy. Dù R1/R2 bị đảo vật lý,
        // production PASS luôn MARKING trước rồi mới mở JIG. Master/FAIL chỉ
        // gọi relay mở JIG và không đi vào chuỗi MARKING.
        await _board.AllRelaysOffAsync(ct);

        bool runMarking = markingEnabled && _production.PassMarkingRelayEnabled;
        if (runMarking)
        {
            onPassStarted?.Invoke();
            await PulseMarkingRelayAsync(ct);

            int interlockMs = Math.Clamp(_production.PassMarkingToJigDelayMs, 0, 5_000);
            if (interlockMs > 0)
                await Task.Delay(interlockMs, ct);
        }
        else
        {
            // Master sample, cấu hình tắt MARKING, hoặc cấu hình JIG chạy trước.
            onPassStarted?.Invoke();
        }

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
