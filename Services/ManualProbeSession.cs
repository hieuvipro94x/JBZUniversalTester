using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public enum ManualProbePhase
{
    Inactive = 0,
    DiscoveringPointer = 1,
    ConnectorCheck = 2
}

public enum ManualProbeTransition
{
    None = 0,
    Started = 1,
    PointerCandidateChanged = 2,
    PointerConfirmed = 3,
    ConnectorCandidateChanged = 4,
    ConnectorChanged = 5,
    ConnectorReleased = 6,
    Reset = 7
}

public sealed record ManualProbeUpdate(
    ManualProbeTransition Transition,
    ManualProbePhase Phase,
    int ProbeIo,
    int ContactIo,
    int CandidateIo,
    int StableFrames);

/// <summary>
/// State machine thuần logic cho TEST POINTER/MANUAL PROBE. TP được học
/// từ IO rỗng và khóa suốt phiên; sau đó chỉ có contact connector thay đổi.
/// Không sở hữu transport, không gọi relay và không thay đổi TestEngine.
/// </summary>
public sealed class ManualProbeSession
{
    private readonly object _gate = new();
    private readonly int _confirmFrames;
    private readonly int _releaseFrames;
    private long _lastFrameSequence;
    private int _candidateIo;
    private int _candidateFrames;
    private int _missingFrames;

    public ManualProbeSession(int confirmFrames = 2, int releaseFrames = 2)
    {
        _confirmFrames = Math.Clamp(confirmFrames, 2, 10);
        _releaseFrames = Math.Clamp(releaseFrames, 2, 10);
    }

    private ManualProbePhase _phase;
    private int _probeIo;
    private int _contactIo;

    public ManualProbePhase Phase { get { lock (_gate) return _phase; } }
    public int ProbeIo { get { lock (_gate) return _probeIo; } }
    public int ContactIo { get { lock (_gate) return _contactIo; } }
    public bool IsActive { get { lock (_gate) return _phase != ManualProbePhase.Inactive; } }

    public ManualProbeUpdate Start()
    {
        lock (_gate)
        {
            ResetCore();
            _phase = ManualProbePhase.DiscoveringPointer;
            return Snapshot(ManualProbeTransition.Started);
        }
    }

    public ManualProbeUpdate Reset()
    {
        lock (_gate)
        {
            bool changed = _phase != ManualProbePhase.Inactive ||
                           _probeIo != 0 ||
                           _contactIo != 0 ||
                           _candidateIo != 0;
            ResetCore();
            return Snapshot(changed ? ManualProbeTransition.Reset : ManualProbeTransition.None);
        }
    }

    public ManualProbeUpdate Update(
        long frameSequence,
        IReadOnlyList<ProbeContactClassifier.Detection> detections,
        ProductModel? model,
        BoardCapacity capacity)
    {
        lock (_gate)
        {
            if (_phase == ManualProbePhase.Inactive ||
                frameSequence <= 0 ||
                frameSequence == _lastFrameSequence)
            {
                return Snapshot(ManualProbeTransition.None);
            }

            _lastFrameSequence = frameSequence;
            if (_phase == ManualProbePhase.DiscoveringPointer)
                return UpdatePointer(detections, model, capacity);

            return UpdateConnector(detections, model, capacity);
        }
    }

    private ManualProbeUpdate UpdatePointer(
        IReadOnlyList<ProbeContactClassifier.Detection> detections,
        ProductModel? model,
        BoardCapacity capacity)
    {
        int candidate = detections
            .Where(item => IsFreeIo(item.Io, model, capacity))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Io)
            .Select(item => item.Io)
            .FirstOrDefault();

        if (candidate == 0)
        {
            ClearCandidate();
            return Snapshot(ManualProbeTransition.None);
        }

        ManualProbeTransition transition = AdvanceCandidate(
            candidate,
            ManualProbeTransition.PointerCandidateChanged);
        if (_candidateFrames < _confirmFrames)
            return Snapshot(transition);

        _probeIo = candidate;
        _phase = ManualProbePhase.ConnectorCheck;
        ClearCandidate();
        return Snapshot(ManualProbeTransition.PointerConfirmed);
    }

    private ManualProbeUpdate UpdateConnector(
        IReadOnlyList<ProbeContactClassifier.Detection> detections,
        ProductModel? model,
        BoardCapacity capacity)
    {
        int[] mapped = detections
            .Where(item => item.Io != _probeIo && IsMappedIo(item.Io, model, capacity))
            .OrderByDescending(item => item.Io == _contactIo)
            .ThenByDescending(item => item.Score)
            .ThenBy(item => item.Io)
            .Select(item => item.Io)
            .Distinct()
            .ToArray();

        // Trong frame chuyển tiếp có nhiều target, giữ contact đang ổn định.
        if (_contactIo != 0 && mapped.Contains(_contactIo))
        {
            _missingFrames = 0;
            ClearCandidate();
            return Snapshot(ManualProbeTransition.None);
        }

        int candidate = mapped.FirstOrDefault();
        if (candidate != 0)
        {
            _missingFrames = 0;
            ManualProbeTransition transition = AdvanceCandidate(
                candidate,
                ManualProbeTransition.ConnectorCandidateChanged);
            if (_candidateFrames < _confirmFrames)
                return Snapshot(transition);

            _contactIo = candidate;
            ClearCandidate();
            return Snapshot(ManualProbeTransition.ConnectorChanged);
        }

        ClearCandidate();
        if (_contactIo == 0)
            return Snapshot(ManualProbeTransition.None);

        _missingFrames++;
        if (_missingFrames < _releaseFrames)
            return Snapshot(ManualProbeTransition.None);

        _contactIo = 0;
        _missingFrames = 0;
        return Snapshot(ManualProbeTransition.ConnectorReleased);
    }

    private ManualProbeTransition AdvanceCandidate(int io, ManualProbeTransition changed)
    {
        if (_candidateIo != io)
        {
            _candidateIo = io;
            _candidateFrames = 1;
            return changed;
        }

        _candidateFrames++;
        return ManualProbeTransition.None;
    }

    private static bool IsFreeIo(int io, ProductModel? model, BoardCapacity capacity)
    {
        if (!capacity.ContainsGlobalIo(io) || model is null)
            return false;

        return !IsMappedIo(io, model, capacity) &&
               !model.IgnoredIo.Contains(io) &&
               !model.DiscardContactIo.Contains(io);
    }

    private static bool IsMappedIo(int io, ProductModel? model, BoardCapacity capacity)
    {
        if (!capacity.ContainsGlobalIo(io) || model is null)
            return false;

        if (model.Pins.Any(pin => pin.IoNumber == io))
            return true;

        return model.Clip is not null &&
               (model.Clip.CommonIo == io ||
                model.Clip.Branches.Any(branch => branch.TargetIo == io));
    }

    private ManualProbeUpdate Snapshot(ManualProbeTransition transition) =>
        new(transition, _phase, _probeIo, _contactIo, _candidateIo, _candidateFrames);

    private void ClearCandidate()
    {
        _candidateIo = 0;
        _candidateFrames = 0;
    }

    private void ResetCore()
    {
        _phase = ManualProbePhase.Inactive;
        _probeIo = 0;
        _contactIo = 0;
        _lastFrameSequence = 0;
        _missingFrames = 0;
        ClearCandidate();
    }
}
