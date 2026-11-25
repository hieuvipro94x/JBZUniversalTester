using JBZUniversalTester.Models;
using System.IO;

namespace JBZUniversalTester.Services;

public static class StartupBootstrapService
{
    private static bool _allowNewDatabase;
    public static void EnsureFastConfiguration()
    {
        AsyncFileLogService log = AsyncFileLogService.Current;
        try
        {
            bool existed = File.Exists(RuntimePaths.ConfigFile);
            IReadOnlyList<string> migrated = ProductionDataUpgradeService.MigrateFastConfigurationForCurrentVersion();
            _allowNewDatabase = !existed && migrated.Count == 0;
            if (migrated.Count > 0)
                log.Application($"Fast configuration inherited: {string.Join(", ", migrated)}");

            bool canonicalExisted = File.Exists(RuntimePaths.ConfigFile);
            ProductionSettings production = ProductionConfigService.Load();
            if (!canonicalExisted)
                ProductionConfigService.Save(production);
            else
                ProductionConfigService.EnsureSavedOnStartup(production);

            if (!canonicalExisted)
                log.Application($"CONFIG_MIGRATION old -> {RuntimePaths.ConfigFile}");
        }
        catch (Exception ex)
        {
            log.Error($"Fast configuration inheritance failed: {ex}");
            throw; // Never replace an unresolved station configuration with defaults.
        }
    }

    public static Task EnsureDeferredProductionFiles(bool failOnError = false)
    {
        AsyncFileLogService log = AsyncFileLogService.Current;
        try
        {
            StationDatabaseMigrationService.EnsureForCurrentInstallation(_allowNewDatabase);

            ProductionSettings production = ProductionConfigService.Load();
            ProductionConfigService.EnsureSavedOnStartup(production);
            log.Application($"configuration ready: {RuntimePaths.ConfigFile}");

            var repository = new TestHistoryStore(RuntimePaths.DatabaseFile);
            BcwDatabaseMigrationService.EnsureForCurrentInstallation();
            log.Application($"database ready: {RuntimePaths.DatabaseFile}");

            // Once the SQLite import is acknowledged, old release mirrors are
            // obsolete. Rebuild from SQLite instead of choosing an old counter
            // file by its size/count (which could undo a legitimate PIN reset).
            IReadOnlyList<string> migrated = ProductionDataUpgradeService.MigrateDeferredProductionDataForCurrentVersion(
                includePreviousCounters: !repository.HasImportedPartCounters());
            if (migrated.Count > 0)
                log.Application($"Part counter file inherited: {string.Join(", ", migrated)}");
            log.Application($"part counter ready: {RuntimePaths.PartCounterFile}");

            var partCounter = new PartCounterStore(RuntimePaths.PartCounterFile);
            IReadOnlyList<PartCounterEntry> legacyCounters = partCounter.ReadAll();
            int importedCounters = repository.ImportPartCountersOnce(
                legacyCounters,
                RuntimePaths.PartCounterFile);
            if (importedCounters > 0)
                log.Application($"PARTCNT_IMPORT rows={importedCounters}");
            partCounter.MirrorAll(repository.GetAllProbeCounters());

            // C:\Pass_ và C:\Error_ trên máy cũ có thể chứa hàng trăm file.
            // Import chúng không phải điều kiện để kết nối bo/chọn mã; chờ tại
            // đây từng làm startup dừng trước T3 và khóa toàn bộ nút vận hành.
            log.Application(
                "Critical filesystem bootstrap completed. Legacy history import is available from the History page.");
        }
        catch (Exception ex)
        {
            log.Error($"Deferred startup bootstrap error: {ex}");
            if (failOnError) throw;
        }

        return Task.CompletedTask;
    }

}
