using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>Optional production-only timeout for continuously missing expected pairs.</summary>
public sealed class MissingConnectionConfirmationGate(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<(int, int), long> _missingSince = [];
    public void Reset() => _missingSince.Clear();

    public IReadOnlyList<FaultDetail> Observe(IReadOnlyList<FaultDetail> missing,
        bool enabled, int seconds, bool eligible)
    {
        if (!enabled || !eligible)
        {
            Reset();
            return [];
        }
        var current = missing.Where(fault => fault.Type == ProductFaultType.OpenCircuit &&
                fault.ExpectedSourceIo > 0 && fault.ExpectedTargetIo > 0)
            .GroupBy(fault => (fault.ExpectedSourceIo!.Value, fault.ExpectedTargetIo!.Value))
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var key in _missingSince.Keys.Where(key => !current.ContainsKey(key)).ToArray())
            _missingSince.Remove(key);
        long now = _clock.GetTimestamp();
        var confirmed = new List<FaultDetail>();
        foreach (var (key, fault) in current)
        {
            if (!_missingSince.TryGetValue(key, out long since))
                _missingSince[key] = since = now;
            if (_clock.GetElapsedTime(since, now).TotalSeconds >= Math.Clamp(seconds, 1, 120))
                confirmed.Add(fault);
        }
        return confirmed;
    }
}
