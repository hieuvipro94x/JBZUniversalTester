using JBZUniversalTester.Models;
using System.IO;
namespace JBZUniversalTester.Services;

public static class StartupBootstrapService
{
    public static void EnsureStartupFiles()
    {
        AsyncFileLogService log = AsyncFileLogService.Current;
        try
        {
            AppSettings appSettings = AppSettings.Load();
            log.Application($"appsettings.json ready: {AppSettings.SettingsPath}");

            ProductionSettings production = ProductionConfigService.Load();
            ProductionConfigService.EnsureSavedOnStartup(production);
            log.Application($"production.settings.json ready: {ProductionConfigService.JsonPath}");
            log.Application($"compatibility cfg ready: {ProductionConfigService.LegacyCfgPath}");

            string historyDirectory = string.IsNullOrWhiteSpace(production.HistoryDirectory)
                ? "Data/History"
                : production.HistoryDirectory.Trim();
            if (!Path.IsPathRooted(historyDirectory))
                historyDirectory = Path.Combine(AppContext.BaseDirectory, historyDirectory);

            string historyPath = Path.Combine(historyDirectory, "test-history.db");
            _ = new TestHistoryStore(historyPath);
            log.Application($"history database ready: {historyPath}");

            // Các thư mục vận hành được tạo ngay từ startup, không chờ tới
            // lần đầu có lỗi/kết quả.
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Data", "ErrorLogs"));
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "Data", "Models"));
            log.Application("Startup filesystem bootstrap completed.");
        }
        catch (Exception ex)
        {
            log.Error($"Startup bootstrap error: {ex}");
        }
    }
}
