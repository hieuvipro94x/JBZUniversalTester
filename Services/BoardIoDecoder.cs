using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// Decoder stream RX của bo JBZ.
///
/// QUAN TRỌNG: cùng byte 80/A0 có ý nghĩa khác nhau theo chế độ.
///
/// Production continuity:
///   80/81 nn = SOURCE đang được kích/quét
///   A0/A1 nn = TARGET đang thông với SOURCE gần nhất
///   C0 00/01 = kết thúc vòng quét (đã xác nhận bằng trace nhỏ/lớn)
///
/// TestPin / đầu dò GND (xác nhận bằng trace 2026-08-07 14:55):
///   80/81 nn = chính I/O nn đang NORMAL
///   A0/A1 nn = chính I/O nn đang bị que GND chạm
///   C0 00    = kết thúc snapshot theo BoardCapacity (trace gốc: 256 I/O)
/// Ví dụ: 80 00, A0 01, 80 02 ... => chỉ IO(2) đang chạm.
/// </summary>
public sealed class BoardIoDecoder
{
    // V12.9: các hằng số compatibility trỏ về BoardCapacity để toàn project
    // dùng cùng một nguồn sự thật. 1 physical card = 32 I/O; 1 expansion
    // module hiện cấu hình 2 physical card = 64 I/O.
    public const int IoPerCard = BoardCapacity.IoPerPhysicalCard;
    public const int ScanCardsPerExpansionCard = 1;
    public const int IoPerScanCard = BoardCapacity.IoPerExpansionModule;
    public const int IoPerExpansionCard = BoardCapacity.IoPerExpansionModule;
    public const int MaxExpansionCardCount = BoardCapacity.MaxExpansionModuleCount;
    public const int MaxScanCardCount = BoardCapacity.MaxExpansionModuleCount;
    public const int MaxCardCount = BoardCapacity.MaxPhysicalCardCount;
    public const int IoPerBank = BoardAddressMapper.IoPerProtocolBank;
    public const int MaxIoCount = BoardCapacity.MaxGlobalIo;

    public const byte SourceBase = 0x80;
    public const byte TargetBase = 0xA0;
    public const byte WordEnd1 = 0xC0;
    // Compatibility constant. Decoder không còn hard-code duy nhất C0 00.
    public const byte WordEnd2 = 0x00;

    readonly List<byte> _buffer = [];
    int _bufferOffset;

    readonly HashSet<int> _sourcesSeen = [];
    readonly Dictionary<int, HashSet<int>> _connections = [];
    readonly HashSet<int> _activeTargets = [];
    readonly Dictionary<int, int> _targetHitCounts = [];
    readonly HashSet<int> _probeActive = [];
    readonly List<byte> _frameRaw = [];

    int? _currentSource;
    long _sequence;
    int _unknownBytes;
    BoardCapacity _capacity = BoardCapacity.Create(1);
    readonly BoardAddressMapper _addressMapper;
    BoardScanMode _mode = BoardScanMode.Production;

    public BoardIoDecoder()
    {
        _addressMapper = new BoardAddressMapper(_capacity);
    }

    public BoardCapacity Capacity => _capacity;
    public int ExpectedIoCount => _capacity.TotalIoCapacity;
    public BoardScanMode Mode => _mode;
    public int CardCount => _capacity.ScanCardCount;

    public static int NormalizeScanCardCount(int cardCount) =>
        Math.Clamp(cardCount, 1, MaxScanCardCount);

    public static int ScanCardCountFromExpansionCards(int expansionCardCount) =>
        BoardCapacity.Create(expansionCardCount).ScanCardCount;

    public static int ExpansionCardCountFromScanCards(int scanCardCount) =>
        NormalizeScanCardCount(scanCardCount);

    public static int RequiredExpansionCardCountForIo(int maxIo) =>
        BoardCapacity.RequiredScanUnitsForIo(maxIo);

    public static int RequiredCardCountForIo(int maxIo) =>
        RequiredExpansionCardCountForIo(maxIo);

    public static int CapacityForCards(int cardCount) =>
        NormalizeScanCardCount(cardCount) * IoPerScanCard;

    public static int CapacityForExpansionCards(int expansionCardCount) =>
        BoardCapacity.Create(expansionCardCount).TotalIoCapacity;

    public void ConfigureCapacity(BoardCapacity capacity)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        _capacity = capacity;
        _addressMapper.Configure(capacity);
        Reset();
    }

    public void ConfigureStartCardNumber(int startCardNumber)
    {
        ConfigureCapacity(BoardCapacity.Create(
            _capacity.ExpansionModuleCount,
            startCardNumber));
    }

    public void ConfigureMode(BoardScanMode mode)
    {
        if (_mode == mode)
            return;

        _mode = mode;
        Reset();
    }

    public void ConfigureCardCount(int cardCount)
    {
        int scanCardCount = NormalizeScanCardCount(cardCount);
        ConfigureCapacity(BoardCapacity.Create(scanCardCount, _capacity.StartCardNumber));
    }

    public void ConfigureIoCount(int ioCount)
    {
        int expansion = Math.Clamp(
            (int)Math.Ceiling(Math.Max(1, ioCount) / (double)IoPerExpansionCard),
            1,
            MaxExpansionCardCount);
        ConfigureCapacity(BoardCapacity.Create(expansion, _capacity.StartCardNumber));
    }

    public void Reset()
    {
        _buffer.Clear();
        _bufferOffset = 0;
        ResetFrameState(resetSequence: true);
    }

    void ResetFrameState(bool resetSequence = false)
    {
        _sourcesSeen.Clear();
        _connections.Clear();
        _activeTargets.Clear();
        _targetHitCounts.Clear();
        _probeActive.Clear();
        _frameRaw.Clear();
        _currentSource = null;
        _unknownBytes = 0;

        if (resetSequence)
            _sequence = 0;
    }

    public IReadOnlyList<ScanFrame> Feed(ReadOnlySpan<byte> data) =>
        _mode == BoardScanMode.Probe
            ? FeedProbe(data)
            : FeedProduction(data);

    IReadOnlyList<ScanFrame> FeedProduction(ReadOnlySpan<byte> data)
    {
        AppendInput(data);
        var frames = new List<ScanFrame>();

        while (_buffer.Count - _bufferOffset >= 2)
        {
            byte first = _buffer[_bufferOffset];
            byte second = _buffer[_bufferOffset + 1];

            if (TryDecodeSource(first, second, out int sourceIo))
            {
                _currentSource = sourceIo;
                _sourcesSeen.Add(sourceIo);
                _connections.TryAdd(sourceIo, []);
                AppendRaw(first, second);
                _bufferOffset += 2;
                continue;
            }

            if (TryDecodeTarget(first, second, out int targetIo))
            {
                _activeTargets.Add(targetIo);
                _targetHitCounts[targetIo] =
                    _targetHitCounts.GetValueOrDefault(targetIo) + 1;

                if (_currentSource is int source)
                {
                    if (!_connections.TryGetValue(source, out HashSet<int>? targets))
                    {
                        targets = [];
                        _connections[source] = targets;
                    }

                    targets.Add(targetIo);
                }

                AppendRaw(first, second);
                _bufferOffset += 2;
                continue;
            }

            if (TryDecodeFrameTerminator(first, second, out FrameTerminator terminator))
            {
                bool hasActiveFrameData = _sourcesSeen.Count > 0 ||
                                          _activeTargets.Count > 0 ||
                                          _unknownBytes > 0;
                AppendRaw(first, second);
                _bufferOffset += 2;

                // Không biến C0 xx trôi nổi giữa garbage/biên purge thành một
                // frame. Production chỉ kết thúc khi decoder thực sự đã nhận data.
                if (!hasActiveFrameData)
                {
                    ResetFrameState();
                    continue;
                }

                _sequence++;

                // Production scan phải phủ đúng toàn bộ active capacity. Frame
                // thiếu source vẫn được phát để diagnostic nhưng không thể ARM.
                bool complete = terminator.IsKnown &&
                                _sourcesSeen.Count == ExpectedIoCount;

                frames.Add(new ScanFrame(
                    DateTime.Now,
                    CardCount,
                    _activeTargets.ToHashSet(),
                    _frameRaw.ToArray(),
                    complete,
                    _unknownBytes,
                    _sequence,
                    _connections.ToDictionary(
                        pair => pair.Key,
                        pair => (IReadOnlySet<int>)pair.Value.ToHashSet()),
                    _targetHitCounts.ToDictionary(pair => pair.Key, pair => pair.Value),
                    BoardScanMode.Production,
                    ExpectedIoCount,
                    _sourcesSeen.Count,
                    terminator.RawCode,
                    CardCount,
                    terminator.IsKnown));

                ResetFrameState();
                continue;
            }

            // Mất đồng bộ: bỏ đúng một byte rồi tìm lại word hợp lệ.
            _bufferOffset++;
            _unknownBytes++;
        }

        CompactBuffer();
        return frames;
    }

    IReadOnlyList<ScanFrame> FeedProbe(ReadOnlySpan<byte> data)
    {
        AppendInput(data);
        var frames = new List<ScanFrame>();

        while (_buffer.Count - _bufferOffset >= 2)
        {
            byte first = _buffer[_bufferOffset];
            byte second = _buffer[_bufferOffset + 1];

            if (TryDecodeProbeState(first, second, out int io, out bool active))
            {
                AppendRaw(first, second);
                _bufferOffset += 2;

                // V12.9.2: protocol Probe được xử lý như event-based state.
                // TARGET = TOUCH/ON, SOURCE = RELEASE/OFF. Cả hai đều cập nhật
                // tập hiện tại và phát frame ngay; không chờ C0/TTL/timer để UI đổi.
                if (active)
                    _probeActive.Add(io);
                else
                    _probeActive.Remove(io);

                HashSet<int> instantIo = _probeActive.ToHashSet();
                frames.Add(new ScanFrame(
                    DateTime.Now,
                    CardCount,
                    instantIo,
                    [first, second],
                    false,
                    _unknownBytes,
                    _sequence,
                    new Dictionary<int, IReadOnlySet<int>>(),
                    instantIo.ToDictionary(value => value, _ => 1),
                    BoardScanMode.Probe));

                continue;
            }

            if (TryDecodeFrameTerminator(first, second, out FrameTerminator terminator) &&
                terminator.IsKnown)
            {
                AppendRaw(first, second);
                _bufferOffset += 2;
                _sequence++;

                // C0 kết thúc snapshot: luôn phát trạng thái đầy đủ. Nếu một chân
                // đã nhả thì nó không còn trong tập này; nhả hết => tập rỗng.
                // Nhờ vậy release từng IO được xử lý độc lập và không reset UI
                // Production/configuration.
                HashSet<int> snapshot = _probeActive.ToHashSet();
                frames.Add(new ScanFrame(
                    DateTime.Now,
                    CardCount,
                    snapshot,
                    _frameRaw.ToArray(),
                    true,
                    _unknownBytes,
                    _sequence,
                    new Dictionary<int, IReadOnlySet<int>>(),
                    snapshot.ToDictionary(value => value, _ => 1),
                    BoardScanMode.Probe,
                    ExpectedIoCount,
                    _sourcesSeen.Count,
                    terminator.RawCode,
                    CardCount,
                    true));

                ResetFrameState();
                continue;
            }

            _bufferOffset++;
            _unknownBytes++;
        }

        CompactBuffer();
        return frames;
    }

    public IReadOnlyList<ScanFrame> Decode(byte[] raw)
    {
        Reset();
        return Feed(raw);
    }

    void AppendInput(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        foreach (byte value in data)
            _buffer.Add(value);
    }

    bool TryDecodeSource(byte first, byte second, out int io) =>
        _addressMapper.TryDecode(first, SourceBase, second, out io);

    bool TryDecodeTarget(byte first, byte second, out int io) =>
        _addressMapper.TryDecode(first, TargetBase, second, out io);

    /// <summary>
    /// Terminator protocol đã có bằng chứng cho code 00 và 01. C0 với code khác
    /// vẫn được nhận diện là terminator candidate để phát diagnostic incomplete,
    /// nhưng tuyệt đối không được đánh dấu complete.
    /// </summary>
    public static bool TryDecodeFrameTerminator(
        byte first,
        byte second,
        out FrameTerminator terminator)
    {
        if (first != WordEnd1)
        {
            terminator = default;
            return false;
        }

        terminator = new FrameTerminator(
            second,
            second is 0x00 or 0x01);
        return true;
    }

    bool TryDecodeProbeState(byte first, byte second, out int io, out bool active)
    {
        active = false;

        if (_addressMapper.TryDecode(first, SourceBase, second, out io))
        {
            active = false;
            return true;
        }

        if (_addressMapper.TryDecode(first, TargetBase, second, out io))
        {
            active = true;
            return true;
        }

        io = 0;
        return false;
    }

    void AppendRaw(byte first, byte second)
    {
        _frameRaw.Add(first);
        _frameRaw.Add(second);
    }

    void CompactBuffer()
    {
        if (_bufferOffset == 0)
            return;

        if (_bufferOffset >= _buffer.Count)
        {
            _buffer.Clear();
            _bufferOffset = 0;
            return;
        }

        if (_bufferOffset >= 4096 || _bufferOffset >= _buffer.Count / 2)
        {
            _buffer.RemoveRange(0, _bufferOffset);
            _bufferOffset = 0;
        }
    }
}

public readonly record struct FrameTerminator(byte RawCode, bool IsKnown);
