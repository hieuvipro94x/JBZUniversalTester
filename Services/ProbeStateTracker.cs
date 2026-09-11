namespace JBZUniversalTester.Services;

public sealed class ProbeStateTracker
{
    private readonly int _confirmFrames;
    private readonly int _releaseFrames;
    private int[] _candidateIos = [];
    private int _candidateFrames;
    private int _missingFrames;
    private int[] _activeIos = [];

    public ProbeStateTracker(int confirmFrames = 2, int releaseFrames = 2, int maxContacts = 2)
    {
        _confirmFrames = Math.Clamp(confirmFrames, 1, 10);
        _releaseFrames = Math.Clamp(releaseFrames, 1, 10);
        MaxContacts = Math.Clamp(maxContacts, 1, 64);
    }

    public int MaxContacts { get; }

    public IReadOnlyList<int> ActiveIos => _activeIos;

    public bool IsActive => _activeIos.Length > 0;

    /// <summary>
    /// Có candidate đang debounce hoặc contact đã xác nhận. Caller dùng tín hiệu
    /// này để cách ly các frame chuyển tiếp khỏi TestEngine cho tới khi RELEASE đủ frame.
    /// </summary>
    public bool HasTrackedContacts => _candidateIos.Length > 0 || _activeIos.Length > 0;

    public bool Update(IReadOnlyCollection<int> observedIos)
    {
        int[] observed = observedIos
            .Where(io => io > 0)
            .Distinct()
            .OrderBy(io => io)
            .Take(MaxContacts)
            .ToArray();

        if (observed.Length == 0)
        {
            _candidateIos = [];
            _candidateFrames = 0;
            if (_activeIos.Length == 0)
                return false;

            _missingFrames++;
            if (_missingFrames < _releaseFrames)
                return false;

            _activeIos = [];
            _missingFrames = 0;
            return true;
        }

        _missingFrames = 0;
        if (_activeIos.SequenceEqual(observed))
        {
            _candidateIos = [];
            _candidateFrames = 0;
            return false;
        }

        if (_candidateIos.SequenceEqual(observed))
            _candidateFrames++;
        else if (_activeIos.Length == 0 &&
                 _candidateIos.Length > 0 &&
                 _candidateIos.Intersect(observed).Any())
        {
            // A real touch can expose its contacts progressively across two
            // complete scans (IO10, then IO10+IO12). Preserve the shared
            // candidate and confirm the union without using a time delay.
            _candidateIos = _candidateIos
                .Concat(observed)
                .Distinct()
                .OrderBy(io => io)
                .Take(MaxContacts)
                .ToArray();
            _candidateFrames++;
        }
        else
        {
            _candidateIos = observed;
            _candidateFrames = 1;
        }

        if (_candidateFrames < _confirmFrames)
            return false;

        _activeIos = _candidateIos;
        _candidateIos = [];
        _candidateFrames = 0;
        return true;
    }

    public bool Clear()
    {
        bool changed = _activeIos.Length > 0 || _candidateIos.Length > 0;
        _candidateIos = [];
        _candidateFrames = 0;
        _missingFrames = 0;
        _activeIos = [];
        return changed;
    }
}
