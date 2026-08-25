using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed class LotSequenceService
{
    private readonly object _gate = new();
    private readonly ProductionSettings _settings;
    private readonly Action<ProductionSettings> _persist;
    private readonly Dictionary<string, long> _reservations = new(StringComparer.Ordinal);

    public LotSequenceService(
        ProductionSettings settings,
        Action<ProductionSettings>? persist = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _persist = persist ?? ProductionConfigService.Save;
    }

    public long NextLot
    {
        get
        {
            lock (_gate)
                return Math.Max(0, _settings.LotNo);
        }
    }

    public long ReserveForCycle(string cycleId)
    {
        if (string.IsNullOrWhiteSpace(cycleId))
            throw new ArgumentException("CycleId is required for LOT reservation.", nameof(cycleId));

        lock (_gate)
        {
            if (_reservations.TryGetValue(cycleId, out long existing))
                return existing;

            long reserved = checked(Math.Max(0, _settings.LotNo) + _reservations.Values.Distinct().Count());
            _reservations[cycleId] = reserved;
            return reserved;
        }
    }

    public bool TryCommitSuccessfulPrint(string cycleId, long printedLot, out string error)
    {
        lock (_gate)
        {
            if (!_reservations.TryGetValue(cycleId, out long reserved) || reserved != printedLot)
            {
                error = $"LOT reservation mismatch for cycle {cycleId}.";
                return false;
            }

            long current = Math.Max(0, _settings.LotNo);
            if (printedLot != current)
            {
                error = $"Cannot commit LOT {printedLot}; next persisted LOT is {current}.";
                return false;
            }

            try
            {
                _settings.LotNo = checked(printedLot + 1);
                _persist(_settings);
                _reservations.Remove(cycleId);
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                _settings.LotNo = current;
                error = $"Cannot persist next LOT: {ex.Message}";
                return false;
            }
        }
    }

    public bool IsCommitCandidate(string cycleId, long lotNo)
    {
        lock (_gate)
        {
            return _reservations.TryGetValue(cycleId, out long reserved) &&
                   reserved == lotNo &&
                   Math.Max(0, _settings.LotNo) == lotNo;
        }
    }

    public bool TryRestoreReservation(string cycleId, long lotNo)
    {
        if (string.IsNullOrWhiteSpace(cycleId))
            return false;

        lock (_gate)
        {
            if (_reservations.TryGetValue(cycleId, out long existing))
                return existing == lotNo;

            if (lotNo < Math.Max(0, _settings.LotNo) ||
                _reservations.Values.Contains(lotNo))
                return false;

            _reservations[cycleId] = lotNo;
            return true;
        }
    }

    public long GetReservedOrNext(string cycleId)
    {
        lock (_gate)
            return _reservations.TryGetValue(cycleId, out long lot) ? lot : Math.Max(0, _settings.LotNo);
    }
}
