namespace JBZUniversalTester.Services;

public sealed class ProbeStateTracker
{
    private sealed class Contact
    {
        public int SeenFrames;
        public int MissingFrames;
        public bool Confirmed;
    }

    private readonly Dictionary<int, Contact> _contacts = [];
    private readonly int _confirmFrames;
    private readonly int _releaseFrames;
    private int[] _activeIos = [];

    public ProbeStateTracker(int confirmFrames = 2, int releaseFrames = 2, int maxContacts = 2)
    {
        _confirmFrames = Math.Clamp(confirmFrames, 1, 10);
        _releaseFrames = Math.Clamp(releaseFrames, 1, 10);
        MaxContacts = Math.Clamp(maxContacts, 1, 8);
    }

    public int MaxContacts { get; }

    public IReadOnlyList<int> ActiveIos => _activeIos;

    public bool IsActive => _activeIos.Length > 0;

    /// <summary>
    /// Có candidate đang debounce hoặc contact đã xác nhận. Caller dùng tín hiệu
    /// này để cách ly các frame chuyển tiếp khỏi TestEngine cho tới khi RELEASE đủ frame.
    /// </summary>
    public bool HasTrackedContacts => _contacts.Count > 0;

    public bool Update(IReadOnlyCollection<int> observedIos)
    {
        HashSet<int> observed = observedIos
            .Where(io => io > 0)
            .Distinct()
            .Take(MaxContacts)
            .ToHashSet();

        foreach (int io in observed)
        {
            if (!_contacts.TryGetValue(io, out Contact? contact))
            {
                contact = new Contact();
                _contacts[io] = contact;
            }

            contact.SeenFrames++;
            contact.MissingFrames = 0;
            if (contact.SeenFrames >= _confirmFrames)
                contact.Confirmed = true;
        }

        foreach (int io in _contacts.Keys.ToArray())
        {
            if (observed.Contains(io))
                continue;

            Contact contact = _contacts[io];
            contact.SeenFrames = 0;
            contact.MissingFrames++;
            if (contact.MissingFrames >= _releaseFrames)
                _contacts.Remove(io);
        }

        int[] nextActive = _contacts
            .Where(pair => pair.Value.Confirmed)
            .Select(pair => pair.Key)
            .OrderBy(io => io)
            .Take(MaxContacts)
            .ToArray();

        if (_activeIos.SequenceEqual(nextActive))
            return false;

        _activeIos = nextActive;
        return true;
    }

    public bool Clear()
    {
        bool changed = _activeIos.Length > 0 || _contacts.Count > 0;
        _contacts.Clear();
        _activeIos = [];
        return changed;
    }
}
