using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public static class ProductionTimingPolicy
{
    public const int DefaultShortCircuitConfirmMs = 100;

    public static void Normalize(ProductionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.IoScanIntervalMs = Math.Clamp(settings.IoScanIntervalMs, 1, 50);
        settings.OpenCircuitConfirmMs = Math.Clamp(settings.OpenCircuitConfirmMs, 20, 10_000);

        int shortConfirm = settings.ShortCircuitConfirmMs > 0
            ? settings.ShortCircuitConfirmMs
            : settings.ShortConfirmMs > 0
                ? settings.ShortConfirmMs
                : DefaultShortCircuitConfirmMs;
        settings.ShortCircuitConfirmMs = Math.Clamp(shortConfirm, 20, 10_000);
        settings.ShortConfirmMs = settings.ShortCircuitConfirmMs;

        settings.WrongConnectionConfirmMs = Math.Clamp(settings.WrongConnectionConfirmMs, 20, 10_000);
        settings.ProductSettleTimeMs = Math.Clamp(settings.ProductSettleTimeMs, 20, 10_000);
        settings.JigContactUnstableWindowMs = Math.Clamp(settings.JigContactUnstableWindowMs, 100, 30_000);
        settings.ProbeReplacementThreshold = Math.Clamp(settings.ProbeReplacementThreshold, 1_000, 100_000_000);
    }
}
