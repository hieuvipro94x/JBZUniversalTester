using System.Diagnostics;

namespace JBZUniversalTester.Services;

internal enum ProductPresenceStabilityTransition { None, Started, Cancelled, Confirmed }

/// <summary>Completion-only debounce; realtime presentation remains independent.</summary>
internal sealed class ProductPresenceStabilityGate
{
    internal const int ConfirmationFrames = 4;
    private readonly object _gate = new();
    private long _runtimeGeneration = -1;
    private long _cycleEpoch = -1;
    private long _scanGeneration = -1;
    private long _lastSequence;
    private long _startedAt;
    private bool _candidate;
    private bool _confirmed;
    private int _frames;

    public bool IsConfirmed(long runtimeGeneration, long cycleEpoch)
    {
        lock (_gate)
            return _runtimeGeneration == runtimeGeneration && _cycleEpoch == cycleEpoch && _confirmed;
    }

    public void PrepareFrameContext(long runtimeGeneration, long cycleEpoch, long scanGeneration)
    {
        lock (_gate)
        {
            if (_runtimeGeneration == runtimeGeneration && _cycleEpoch == cycleEpoch &&
                scanGeneration > 0 && _scanGeneration > scanGeneration)
                return;
            if (_runtimeGeneration != runtimeGeneration || _cycleEpoch != cycleEpoch ||
                _scanGeneration != scanGeneration)
            {
                ClearCandidate();
                _runtimeGeneration = runtimeGeneration;
                _cycleEpoch = cycleEpoch;
                _scanGeneration = scanGeneration;
                _lastSequence = 0;
            }
        }
    }

    public ProductPresenceStabilityTransition Observe(
        bool present, long timestamp, long runtimeGeneration, long cycleEpoch,
        long scanGeneration, long sequence, out double elapsedMilliseconds, out int frames)
    {
        lock (_gate)
        {
            elapsedMilliseconds = 0;
            frames = 0;
            if (_runtimeGeneration == runtimeGeneration && _cycleEpoch == cycleEpoch &&
                scanGeneration > 0 && _scanGeneration > scanGeneration)
                return ProductPresenceStabilityTransition.None;

            if (_runtimeGeneration != runtimeGeneration || _cycleEpoch != cycleEpoch ||
                _scanGeneration != scanGeneration)
            {
                ClearCandidate();
                _runtimeGeneration = runtimeGeneration;
                _cycleEpoch = cycleEpoch;
                _scanGeneration = scanGeneration;
                _lastSequence = 0;
            }
            if (sequence > 0 && sequence <= _lastSequence)
                return ProductPresenceStabilityTransition.None;
            _lastSequence = sequence;

            if (!present)
            {
                bool cancelled = _candidate && !_confirmed;
                elapsedMilliseconds = _candidate
                    ? Stopwatch.GetElapsedTime(_startedAt, timestamp).TotalMilliseconds : 0;
                frames = _frames;
                ClearCandidate();
                return cancelled ? ProductPresenceStabilityTransition.Cancelled : ProductPresenceStabilityTransition.None;
            }
            if (!_candidate)
            {
                _candidate = true;
                _startedAt = timestamp;
                _frames = 1;
                frames = 1;
                return ProductPresenceStabilityTransition.Started;
            }
            if (_confirmed)
                return ProductPresenceStabilityTransition.None;

            frames = ++_frames;
            elapsedMilliseconds = Stopwatch.GetElapsedTime(_startedAt, timestamp).TotalMilliseconds;
            if (frames >= ConfirmationFrames)
            {
                _confirmed = true;
                return ProductPresenceStabilityTransition.Confirmed;
            }
            return ProductPresenceStabilityTransition.None;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            ClearCandidate();
            _runtimeGeneration = _cycleEpoch = _scanGeneration = -1;
            _lastSequence = 0;
        }
    }

    private void ClearCandidate()
    {
        _candidate = _confirmed = false;
        _startedAt = 0;
        _frames = 0;
    }
}
