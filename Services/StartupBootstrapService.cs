using JBZUniversalTester.Models;
using System.IO;

namespace JBZUniversalTester.Services;

public static class StartupBootstrapService
{
    public static void EnsureFastConfiguration()
    {
        AsyncFileLogService log = AsyncFileLogService.Current;
        try
        {
            IReadOnlyList<string> migrated = ProductionDataUpgradeService.MigrateFastConfigurationForCurrentVersion();
            if (migrated.Count > 0)
                log.Application($"Fast configuration inherited: {string.Join(", ", migrated)}");
        }
        catch (Exception ex)
        {
            log.Error($"Fast configuration inheritance failed: {ex}");
        }
    }

    public static void EnsureDeferredProductionFiles()
    {
        AsyncFileLogService log = AsyncFileLogService.Current;
        try
        {
            IReadOnlyList<string> migrated = ProductionDataUpgradeService.MigrateDeferredProductionDataForCurrentVersion();
            if (migrated.Count > 0)
                log.Application($"Deferred production data inherited: {string.Join(", ", migrated)}");

            _ = AppSettings.Load();
            log.Application($"appsettings.json ready: {AppSettings.SettingsPath}");

            ProductionSettings production = ProductionConfigService.Load();
            ProductionConfigService.EnsureSavedOnStartup(production);
            log.Application($"production.settings.json ready: {ProductionConfigService.JsonPath}");
            if (File.Exists(ProductionConfigService.LegacyCfgPath))
                log.Application($"compatibility cfg loaded: {ProductionConfigService.LegacyCfgPath}");

            string historyDirectory = string.IsNullOrWhiteSpace(production.HistoryDirectory)
                ? "Data/History"
                : production.HistoryDirectory.Trim();
            if (!Path.IsPathRooted(historyDirectory))
                historyDirectory = Path.Combine(AppContext.BaseDirectory, historyDirectory);

            string historyPath = Path.Combine(historyDirectory, "test-history.db");
            _ = new TestHistoryStore(historyPath);
            log.Application($"history database ready: {historyPath}");
            log.Application("Deferred filesystem bootstrap completed.");
        }
        catch (Exception ex)
        {
            log.Error($"Deferred startup bootstrap error: {ex}");
        }
    }
}
