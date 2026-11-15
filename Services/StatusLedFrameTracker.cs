using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>Presentation only: compare scan contents without changing the test engine.</summary>
internal sealed class StatusLedFrameTracker
{
    private ScanFrame? _snapshot;

    public bool Observe(ScanFrame frame)
    {
        if (!IsCompleteScan(frame))
            return false;

        bool changed = _snapshot is null ||
                       _snapshot.ScanGeneration != frame.ScanGeneration ||
                       _snapshot.Mode != frame.Mode ||
                       _snapshot.CardNumber != frame.CardNumber ||
                       _snapshot.ExpectedIoCount != frame.ExpectedIoCount ||
                       !_snapshot.ActiveIo.SetEquals(frame.ActiveIo) ||
                       !ConnectionsEqual(_snapshot.Connections, frame.Connections);
        if (changed)
        {
            // Own the sets: transport callbacks must not mutate the previous sample.
            _snapshot = frame with
            {
                Raw = Array.Empty<byte>(),
                ActiveIo = new HashSet<int>(frame.ActiveIo),
                ConnectionsBySource = frame.Connections
                    .Where(pair => pair.Value.Count != 0)
                    .ToDictionary(pair => pair.Key,
                        pair => (IReadOnlySet<int>)new HashSet<int>(pair.Value))
            };
        }

        // Empty/bootstrap scans are only the green heartbeat. White activity
        // requires an actual contact, not a first sample or removal to empty.
        return changed && HasContact(frame);
    }

    public void Reset() => _snapshot = null;

    public static bool IsCompleteScan(ScanFrame frame) =>
        frame.Complete && frame.TerminatorKnown && frame.UnknownBytes == 0;

    public static bool HasContact(ScanFrame frame) =>
        frame.ActiveIo.Count != 0 ||
        frame.Connections.Any(pair => pair.Value.Any(target => target != pair.Key));

    private static bool ConnectionsEqual(
        IReadOnlyDictionary<int, IReadOnlySet<int>> left,
        IReadOnlyDictionary<int, IReadOnlySet<int>> right)
    {
        // Empty rows and absent rows describe the same electrical matrix.
        foreach (var pair in left)
        {
            if (pair.Value.Count != 0 &&
                (!right.TryGetValue(pair.Key, out var targets) || !pair.Value.SetEquals(targets)))
                return false;
        }
        foreach (var pair in right)
        {
            if (pair.Value.Count != 0 &&
                (!left.TryGetValue(pair.Key, out var targets) || !pair.Value.SetEquals(targets)))
                return false;
        }
        return true;
    }
}
