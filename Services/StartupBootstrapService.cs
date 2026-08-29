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

            bool canonicalExisted = File.Exists(RuntimePaths.ConfigFile);
            AppSettings appSettings = canonicalExisted
                ? AppSettings.Load()
                : AppSettings.LoadLegacyJson();
            ProductionSettings production = ProductionConfigService.Load();
            bool migratedLastModel = string.IsNullOrWhiteSpace(production.LastThtPath) &&
                                     !string.IsNullOrWhiteSpace(appSettings.Storage.LastTestedModelFile);
            if (migratedLastModel)
                production.LastThtPath = appSettings.Storage.LastTestedModelFile.Trim();

            if (!canonicalExisted || migratedLastModel)
                ProductionConfigService.Save(production);
            else
                ProductionConfigService.EnsureSavedOnStartup(production);
            if (!canonicalExisted || !File.ReadLines(RuntimePaths.ConfigFile)
                    .Any(line => line.StartsWith("[App.", StringComparison.OrdinalIgnoreCase)))
            {
                appSettings.Save();
            }

            if (!canonicalExisted || migratedLastModel)
                log.Application($"CONFIG_MIGRATION old -> {RuntimePaths.ConfigFile}");
        }
        catch (Exception ex)
        {
            log.Error($"Fast configuration inheritance failed: {ex}");
        }
    }

    public static async Task EnsureDeferredProductionFiles()
    {
        AsyncFileLogService log = AsyncFileLogService.Current;
        try
        {
            IReadOnlyList<string> migrated = ProductionDataUpgradeService.MigrateDeferredProductionDataForCurrentVersion();
            if (migrated.Count > 0)
                log.Application($"Deferred production data inherited: {string.Join(", ", migrated)}");

            ProductionSettings production = ProductionConfigService.Load();
            ProductionConfigService.EnsureSavedOnStartup(production);
            log.Application($"configuration ready: {RuntimePaths.ConfigFile}");

            MigrateLocalLegacyDatabase(log);
            var repository = new TestHistoryStore(RuntimePaths.DatabaseFile);
            log.Application($"database ready: {RuntimePaths.DatabaseFile}");

            var partCounter = new PartCounterStore(RuntimePaths.PartCounterFile);
            IReadOnlyList<PartCounterEntry> legacyCounters = partCounter.ReadAll();
            int importedCounters = repository.ImportPartCountersOnce(
                legacyCounters,
                RuntimePaths.PartCounterFile);
            if (importedCounters > 0)
                log.Application($"PARTCNT_IMPORT rows={importedCounters}");
            partCounter.MirrorAll(repository.GetAllProbeCounters());

            await ImportLegacyHistoryOnceAsync(repository, production, log).ConfigureAwait(false);
            log.Application("Deferred filesystem bootstrap completed.");
        }
        catch (Exception ex)
        {
            log.Error($"Deferred startup bootstrap error: {ex}");
        }
    }

    private static void MigrateLocalLegacyDatabase(AsyncFileLogService log)
    {
        if (File.Exists(RuntimePaths.DatabaseFile) || !File.Exists(RuntimePaths.LegacyDatabaseFile))
            return;

        Directory.CreateDirectory(RuntimePaths.DataDirectory);
        string temporaryPath = RuntimePaths.DatabaseFile + ".migration.tmp";
        try
        {
            File.Copy(RuntimePaths.LegacyDatabaseFile, temporaryPath, overwrite: false);
            File.Move(temporaryPath, RuntimePaths.DatabaseFile, overwrite: false);
            log.Application(
                $"DATABASE_MIGRATION {RuntimePaths.LegacyDatabaseFile} -> {RuntimePaths.DatabaseFile}");
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static async Task ImportLegacyHistoryOnceAsync(
        TestHistoryStore repository,
        ProductionSettings production,
        AsyncFileLogService log)
    {
        const string migrationKey = "LEGACY_PHT_UNDERSCORE_IMPORT_V1";
        if (repository.IsRuntimeMigrationCompleted(migrationKey))
            return;

        var reader = new LegacyPhtHistoryReader(
            RuntimePaths.LegacyPassRoot,
            RuntimePaths.LegacyErrorRoot);
        int imported = 0;
        int existing = 0;
        await using (var persistence = new ProductionPersistenceService(
                         repository,
                         production,
                         ProgramIdentityService.VersionText))
        {
            await persistence.Initialization.ConfigureAwait(false);
            var importer = new LegacyPhtImportService(persistence, reader);
            IReadOnlyList<LegacyImportResult> results =
                await importer.ImportChangedFilesAsync().ConfigureAwait(false);
            imported = results.Sum(result => result.ImportedRecords);
            existing = results.Sum(result => result.ExistingRecords);
        }

        repository.CompleteRuntimeMigration(
            migrationKey,
            $"imported={imported}; existing={existing}");
        log.Application($"LEGACY_HISTORY_IMPORT imported={imported} existing={existing}");
    }
}
