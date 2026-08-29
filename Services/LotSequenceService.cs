using System.Globalization;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed class LotSequenceService
{
    private readonly object _gate = new();
    private readonly ProductionSettings _settings;
    private readonly Action<ProductionSettings> _persist;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, long> _reservations = new(StringComparer.Ordinal);

    public LotSequenceService(
        ProductionSettings settings,
        Action<ProductionSettings>? persist = null,
        Func<DateTime>? now = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _persist = persist ?? ProductionConfigService.Save;
        _now = now ?? (() => DateTime.Now);

        lock (_gate)
            EnsureCurrentProductionDateLocked();
    }

    public long NextLot
    {
        get
        {
            lock (_gate)
            {
                EnsureCurrentProductionDateLocked();
                return Math.Max(0, _settings.LotNo);
            }
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

            EnsureCurrentProductionDateLocked();

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
                EnsureCurrentProductionDateLocked();
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
        {
            if (_reservations.TryGetValue(cycleId, out long lot))
                return lot;

            EnsureCurrentProductionDateLocked();
            return Math.Max(0, _settings.LotNo);
        }
    }

    private void EnsureCurrentProductionDateLocked()
    {
        // Không đổi ngày khi còn LOT đã cấp cho một chu kỳ chưa in/commit.
        // Sau khi reservation cuối cùng hoàn tất, ngày mới được áp dụng ngay.
        if (_reservations.Count > 0)
            return;

        string today = _now().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string storedDate = (_settings.LotNoDate ?? string.Empty).Trim();
        if (string.Equals(storedDate, today, StringComparison.Ordinal))
            return;

        long previousLot = _settings.LotNo;
        string previousDate = _settings.LotNoDate ?? string.Empty;

        // Migration an toàn: lần đầu nâng cấp chỉ đóng dấu ngày hiện tại, không
        // làm mất số LOT đang sản xuất. Chỉ ngày đã lưu khác hôm nay mới reset.
        if (DateOnly.TryParseExact(
                storedDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            _settings.LotNo = 0;
        }
        _settings.LotNoDate = today;

        try
        {
            _persist(_settings);
            if (previousLot != _settings.LotNo)
            {
                AsyncFileLogService.Current.Test(
                    $"LOTNO DAILY RESET date={today} previous={previousLot} next=0");
            }
        }
        catch (Exception ex)
        {
            _settings.LotNo = previousLot;
            _settings.LotNoDate = previousDate;
            AsyncFileLogService.Current.Error(
                $"Không thể lưu ngày/reset LOTNO tự động: {ex.Message}");
        }
    }
}
