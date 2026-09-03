using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public enum DiscardContactTransition
{
    None = 0,
    Completed = 1
}

/// <summary>
/// Theo dõi tiếp điểm thường mở của thùng hàng lỗi. Một lần THÔNG mới sau ARM
/// xác nhận sản phẩm đã đi qua cảm biến. Nếu lúc ARM đang THÔNG, bắt buộc chờ
/// NGẮT làm baseline rồi THÔNG lại để tiếp điểm kẹt không thể tự xác nhận.
/// </summary>
public sealed class DiscardContactInterlock
{
    private enum State
    {
        Idle,
        AwaitingOpenBaseline,
        AwaitingClosure,
        Completed
    }

    private readonly object _gate = new();
    private State _state;

    public bool IsArmed
    {
        get { lock (_gate) return _state is not State.Idle and not State.Completed; }
    }

    public bool IsCompleted
    {
        get { lock (_gate) return _state == State.Completed; }
    }

    public void Arm(bool contactClosed)
    {
        lock (_gate)
            _state = contactClosed ? State.AwaitingOpenBaseline : State.AwaitingClosure;
    }

    public void Reset()
    {
        lock (_gate)
            _state = State.Idle;
    }

    public DiscardContactTransition Observe(bool contactClosed)
    {
        lock (_gate)
        {
            switch (_state)
            {
                case State.AwaitingOpenBaseline when !contactClosed:
                    _state = State.AwaitingClosure;
                    break;
                case State.AwaitingClosure when contactClosed:
                    _state = State.Completed;
                    return DiscardContactTransition.Completed;
            }

            return DiscardContactTransition.None;
        }
    }

    public static bool IsContactClosed(ScanFrame frame, IReadOnlyList<int> contactIo)
    {
        if (!frame.Complete || frame.UnknownBytes != 0 || contactIo.Count != 2)
            return false;

        int first = contactIo[0];
        int second = contactIo[1];
        return HasDirectedConnection(frame, first, second) ||
               HasDirectedConnection(frame, second, first);
    }

    public static ScanFrame RemoveDiscardIo(ScanFrame frame, IReadOnlyCollection<int> discardIo)
    {
        if (discardIo.Count == 0)
            return frame;

        HashSet<int> excluded = discardIo.ToHashSet();
        IReadOnlySet<int> active = frame.ActiveIo
            .Where(io => !excluded.Contains(io))
            .ToHashSet();

        var connections = new Dictionary<int, IReadOnlySet<int>>();
        foreach ((int source, IReadOnlySet<int> targets) in frame.Connections)
        {
            if (excluded.Contains(source))
                continue;

            HashSet<int> filteredTargets = targets
                .Where(target => !excluded.Contains(target))
                .ToHashSet();
            if (filteredTargets.Count > 0 || targets.Count == 0)
                connections[source] = filteredTargets;
        }

        IReadOnlyDictionary<int, int> targetHits = frame.TargetHits
            .Where(pair => !excluded.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return frame with
        {
            ActiveIo = active,
            ConnectionsBySource = connections,
            TargetHitCounts = targetHits,
            SourceCount = connections.Count
        };
    }

    private static bool HasDirectedConnection(ScanFrame frame, int source, int target) =>
        frame.Connections.TryGetValue(source, out IReadOnlySet<int>? targets) &&
        targets.Contains(target);
}
