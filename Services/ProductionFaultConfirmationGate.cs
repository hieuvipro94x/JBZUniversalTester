using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed record UnexpectedFaultObservation(
    int SourceIo,
    int TargetIo,
    ProductFaultType Type);

public sealed record ProductionFaultConfirmationSnapshot(
    IReadOnlySet<string> ConfirmedOpenKeys,
    IReadOnlySet<(int SourceIo, int TargetIo)> ConfirmedUnexpectedPairs,
    bool ContactUnstable,
    bool ContactLossTimedOut,
    bool ProductStable);

/// <summary>
/// Monotonic confirmation state above raw transport observations. Timers are
/// continuous: a recovered signal removes its candidate instead of accumulating
/// several short outages into one product fault.
/// </summary>
public sealed class ProductionFaultConfirmationGate
{
    private readonly ProductionSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, long> _openCandidateSince = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BounceState> _openBounce = new(StringComparer.Ordinal);
    private readonly HashSet<string> _confirmedOpen = new(StringComparer.Ordinal);
    private readonly Dictionary<(int SourceIo, int TargetIo), UnexpectedCandidate> _unexpectedCandidates = [];
    private readonly HashSet<(int SourceIo, int TargetIo)> _confirmedUnexpected = [];

    private bool _productActivitySeen;
    private bool _lastHasProductActivity;
    private bool _contactUnstable;
    private bool _contactLossTimedOut;
    private bool _productStable;
    private long? _firstActivityAt;
    private long? _contactLossSince;
    private long? _allCorrectSince;

    public ProductionFaultConfirmationGate(
        ProductionSettings settings,
        TimeProvider? timeProvider = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ProductionFaultConfirmationSnapshot Observe(
        IReadOnlyDictionary<string, bool> expectedConnections,
        IReadOnlyCollection<UnexpectedFaultObservation> unexpectedConnections,
        bool hasProductActivity)
    {
        ArgumentNullException.ThrowIfNull(expectedConnections);
        ArgumentNullException.ThrowIfNull(unexpectedConnections);

        long now = _timeProvider.GetTimestamp();
        UpdateProductPresence(hasProductActivity, now);
        UpdateOpenCandidates(expectedConnections, hasProductActivity, now);
        UpdateUnexpectedCandidates(unexpectedConnections, now);
        UpdateCleanStability(expectedConnections, unexpectedConnections, hasProductActivity, now);
        _lastHasProductActivity = hasProductActivity;

        return Snapshot();
    }

    public void Reset()
    {
        _openCandidateSince.Clear();
        _openBounce.Clear();
        _confirmedOpen.Clear();
        _unexpectedCandidates.Clear();
        _confirmedUnexpected.Clear();
        _productActivitySeen = false;
        _lastHasProductActivity = false;
        _contactUnstable = false;
        _contactLossTimedOut = false;
        _productStable = false;
        _firstActivityAt = null;
        _contactLossSince = null;
        _allCorrectSince = null;
    }

    public void ClearUnexpected()
    {
        _unexpectedCandidates.Clear();
        _confirmedUnexpected.Clear();
        _allCorrectSince = null;
        _productStable = false;
    }

    private void UpdateProductPresence(bool hasActivity, long now)
    {
        if (hasActivity)
        {
            if (!_lastHasProductActivity)
                _firstActivityAt = now;

            _productActivitySeen = true;
            _contactLossSince = null;
            _contactLossTimedOut = false;
            return;
        }

        _firstActivityAt = null;
        _allCorrectSince = null;
        _productStable = false;

        if (!_productActivitySeen)
            return;

        // Mất toàn bộ contact trong một cycle chưa chốt kết quả không
        // đủ bằng chứng để kết luận OPEN của sản phẩm.
        _contactUnstable = true;
        _confirmedOpen.Clear();
        _openCandidateSince.Clear();
        _contactLossSince ??= now;
        _contactLossTimedOut = HasElapsed(
            _contactLossSince.Value,
            Math.Max(0, _settings.JigContactUnstableWindowMs),
            now);
    }

    private void UpdateOpenCandidates(
        IReadOnlyDictionary<string, bool> expectedConnections,
        bool hasProductActivity,
        long now)
    {
        foreach (string removedKey in _openCandidateSince.Keys
                     .Where(key => !expectedConnections.ContainsKey(key))
                     .ToArray())
        {
            _openCandidateSince.Remove(removedKey);
            _confirmedOpen.Remove(removedKey);
            _openBounce.Remove(removedKey);
        }

        bool settleElapsed = hasProductActivity &&
                             _firstActivityAt is long firstActivity &&
                             HasElapsed(firstActivity, Math.Max(0, _settings.ProductSettleTimeMs), now);

        foreach ((string key, bool connected) in expectedConnections)
        {
            if (connected)
            {
                if (_openCandidateSince.Remove(key))
                    RegisterOpenRecovery(key, now);
                _confirmedOpen.Remove(key);
                continue;
            }

            if (!hasProductActivity || !settleElapsed)
            {
                _openCandidateSince.Remove(key);
                _confirmedOpen.Remove(key);
                continue;
            }

            if (!_openCandidateSince.TryGetValue(key, out long started))
            {
                started = now;
                _openCandidateSince[key] = started;
            }

            if (HasElapsed(started, Math.Max(0, _settings.OpenCircuitConfirmMs), now))
                _confirmedOpen.Add(key);
        }
    }

    private void RegisterOpenRecovery(string key, long now)
    {
        int windowMs = Math.Max(0, _settings.JigContactUnstableWindowMs);
        if (!_openBounce.TryGetValue(key, out BounceState? state) ||
            HasElapsed(state.WindowStartedAt, windowMs, now))
        {
            _openBounce[key] = new BounceState(now, 1);
            return;
        }

        state = state with { RecoveryCount = state.RecoveryCount + 1 };
        _openBounce[key] = state;
        if (state.RecoveryCount >= 2)
            _contactUnstable = true;
    }

    private void UpdateUnexpectedCandidates(
        IReadOnlyCollection<UnexpectedFaultObservation> observations,
        long now)
    {
        if (observations.Count == 0)
        {
            _unexpectedCandidates.Clear();
            _confirmedUnexpected.Clear();
            return;
        }

        var current = observations
            .GroupBy(item => (item.SourceIo, item.TargetIo))
            .ToDictionary(group => group.Key, group => group.First().Type);

        foreach ((int SourceIo, int TargetIo) key in _unexpectedCandidates.Keys
                     .Where(key => !current.ContainsKey(key))
                     .ToArray())
        {
            _unexpectedCandidates.Remove(key);
            _confirmedUnexpected.Remove(key);
        }

        foreach (((int SourceIo, int TargetIo) key, ProductFaultType type) in current)
        {
            if (!_unexpectedCandidates.TryGetValue(key, out UnexpectedCandidate? candidate) ||
                candidate.Type != type)
            {
                candidate = new UnexpectedCandidate(type, now);
                _unexpectedCandidates[key] = candidate;
                _confirmedUnexpected.Remove(key);
            }

            int confirmMs = type == ProductFaultType.ShortCircuit
                ? Math.Max(0, _settings.ShortCircuitConfirmMs)
                : Math.Max(0, _settings.WrongConnectionConfirmMs);

            if (HasElapsed(candidate.StartedAt, confirmMs, now))
                _confirmedUnexpected.Add(key);
        }
    }

    private void UpdateCleanStability(
        IReadOnlyDictionary<string, bool> expectedConnections,
        IReadOnlyCollection<UnexpectedFaultObservation> unexpectedConnections,
        bool hasProductActivity,
        long now)
    {
        bool allCorrect = hasProductActivity &&
                          expectedConnections.Count > 0 &&
                          expectedConnections.Values.All(connected => connected) &&
                          unexpectedConnections.Count == 0;

        if (!allCorrect)
        {
            _allCorrectSince = null;
            _productStable = false;
            return;
        }

        _allCorrectSince ??= now;
        _productStable = HasElapsed(
            _allCorrectSince.Value,
            Math.Max(0, _settings.ProductSettleTimeMs),
            now);

        if (_productStable && _contactUnstable)
        {
            // Một chu kỳ continuity sạch, liên tục là re-evaluation PASS
            // cho contact warning; không dịch ngược warning thành product fault.
            _contactUnstable = false;
            _contactLossTimedOut = false;
            _openBounce.Clear();
        }
    }

    private bool HasElapsed(long started, int milliseconds, long now) =>
        milliseconds <= 0 ||
        _timeProvider.GetElapsedTime(started, now) >= TimeSpan.FromMilliseconds(milliseconds);

    private ProductionFaultConfirmationSnapshot Snapshot() => new(
        _confirmedOpen.ToHashSet(StringComparer.Ordinal),
        _confirmedUnexpected.ToHashSet(),
        _contactUnstable,
        _contactLossTimedOut,
        _productStable);

    private sealed record BounceState(long WindowStartedAt, int RecoveryCount);
    private sealed record UnexpectedCandidate(ProductFaultType Type, long StartedAt);
}
