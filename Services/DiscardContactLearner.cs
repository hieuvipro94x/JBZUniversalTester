using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed record DiscardLearningSnapshot(string Message, int FirstIo = 0, int SecondIo = 0, bool Completed = false);

/// <summary>Read-only observer of the existing D2XX scan/classifier; never owns a device.</summary>
public sealed class DiscardContactLearner(int stableFrames = 2)
{
    private readonly int _required = Math.Max(2, stableFrames); // Same minimum as topology learning.
    private int _phase, _stable;
    private int[] _candidate = [];
    private long _generation, _sequence;
    public DiscardLearningSnapshot Current { get; private set; } = new("Bỏ tay/vật ra khỏi cảm biến và tháo hết sản phẩm khỏi gá để lấy trạng thái ban đầu.");

    public DiscardLearningSnapshot Observe(ScanFrame frame, BoardCapacity capacity)
    {
        if (Current.Completed) return Current;
        if (frame.Sequence <= 0 || frame.Mode != BoardScanMode.Production || !frame.Complete || frame.UnknownBytes != 0 || !frame.TerminatorKnown ||
            (frame.ExpectedIoCount != 0 && frame.ExpectedIoCount != capacity.TotalIoCapacity) ||
            (frame.ScanUnitCount != 0 && frame.ScanUnitCount != capacity.ScanCardCount))
        { _stable = 0; return Current; }
        if (_generation != 0 && frame.ScanGeneration != 0 && frame.ScanGeneration < _generation) return Current;
        if (frame.ScanGeneration != 0 && _generation != frame.ScanGeneration)
        {
            _generation = frame.ScanGeneration; _sequence = 0; _phase = 0; _stable = 0; _candidate = [];
            Current = new("Dải quét vừa thay đổi. Bỏ tay/vật ra để lấy lại trạng thái ban đầu.");
        }
        if (frame.Sequence > 0 && frame.Sequence <= _sequence) return Current;
        if (frame.Sequence > 0) _sequence = frame.Sequence;
        bool clear = !frame.ActiveIo.Any(capacity.ContainsGlobalIo) &&
            !frame.Connections.Any(pair => capacity.ContainsGlobalIo(pair.Key) && pair.Value.Any(target => target != pair.Key && capacity.ContainsGlobalIo(target)));
        if (_phase == 0)
        {
            if (!clear) { _stable = 0; return Current; }
            if (++_stable >= _required) { _phase = 1; _stable = 0; Current = new("Đưa tay hoặc một vật qua cảm biến thùng lỗi, giữ một lát rồi lấy ra."); }
            return Current;
        }
        if (clear)
        {
            if (_phase == 2 && ++_stable >= _required)
                Current = new($"ĐÃ NHẬN IO {_candidate[0]} / {_candidate[1]} — cảm biến đã tác động và nhả.", _candidate[0], _candidate[1], true);
            else if (_phase == 1) _stable = 0;
            return Current;
        }
        var snapshot = TopologyLearningService.BuildSnapshot(frame, capacity);
        int[] pair = snapshot.Networks.Count == 1 && snapshot.Networks[0].Ios.Count == 2
            ? snapshot.Networks[0].Ios.Order().ToArray() : [];
        bool ambiguous = pair.Length != 2 || TopologyLearningService.FindProbeContactIo(frame, capacity).Count != 0 ||
            frame.ActiveIo.Where(capacity.ContainsGlobalIo).Any(io => !pair.Contains(io));
        if (ambiguous)
        {
            _phase = 0; _stable = 0; _candidate = [];
            return Current = new("Chưa xác định được duy nhất hai IO. Chỉ để cảm biến thùng lỗi hoạt động; tháo dây/sản phẩm khác rồi thử lại.");
        }
        if (_phase == 2)
        {
            _stable = 0;
            if (!_candidate.SequenceEqual(pair))
            { _phase = 0; _candidate = []; Current = new("Có IO khác thay đổi. Bỏ tay/vật ra rồi học lại cảm biến."); }
            return Current;
        }
        if (!_candidate.SequenceEqual(pair)) { _candidate = pair; _stable = 0; }
        if (++_stable >= _required)
        {
            _phase = 2; _stable = 0;
            Current = new($"Đã phát hiện IO {pair[0]} / {pair[1]}. Lấy tay/vật ra khỏi cảm biến để xác nhận.", pair[0], pair[1]);
        }
        return Current;
    }
}
