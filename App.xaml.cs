using System;
using System.Windows;
using System.Windows.Threading;
using JBZUniversalTester.Services;
using JBZUniversalTester.Versioning;

namespace JBZUniversalTester;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Khởi tạo/migrate History và config trước để khóa log lấy đúng giá trị
        // đã lưu của trạm. History không phụ thuộc và không bị khóa bởi log.
        StartupBootstrapService.EnsureStartupFiles();
        var productionSettings = ProductionConfigService.Load();
        AsyncFileLogService.Current.Configure(productionSettings.EnableSystemLogs);
        AsyncFileLogService.Current.Application($"STARTUP {AppVersion.DisplayVersion}");
        StartupPerformanceTrace.Mark("T0 App.OnStartup");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        // Không load/khởi tạo audio đồng bộ trong OnStartup. Với StartupUri,
        // MainWindow đã được tạo trong base.OnStartup nhưng Dispatcher chưa có
        // cơ hội render frame đầu. Đẩy sound xuống ApplicationIdle để audio I/O
        // không làm cửa sổ có cảm giác treo ngay khi vừa mở.
        _ = Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    AppSoundService.Current.Initialize();
                    AppSoundService.Current.PlayStartup();
                }
                catch (Exception ex)
                {
                    AsyncFileLogService.Current.Error($"Startup sound init failed: {ex}");
                }
            }),
            DispatcherPriority.ApplicationIdle);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AsyncFileLogService.Current.Error($"DispatcherUnhandledException: {e.Exception}");

        // V15.1 SAFE OFFLINE MODE - lớp bảo vệ cuối cùng cho lỗi UI/async event.
        // Các thao tác phần cứng dự kiến phải được catch ở ViewModel/Command trước;
        // guard này tránh app văng ra nếu vẫn còn exception phục hồi được lọt tới Dispatcher.
        e.Handled = true;
        try
        {
            MessageBox.Show(
                "Ứng dụng gặp lỗi hệ thống và đã dừng thao tác hiện tại.\n\n" +
                "Hãy kiểm tra nguồn điện, bo mạch và cáp USB trước khi thử lại.\n\n" +
                $"Chi tiết: {e.Exception.Message}",
                "JBZ - LỖI HỆ THỐNG",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
        }
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        AsyncFileLogService.Current.Error($"AppDomain UnhandledException: {e.ExceptionObject}");

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AsyncFileLogService.Current.Error($"UnobservedTaskException: {e.Exception}");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AsyncFileLogService.Current.Application($"SHUTDOWN exitCode={e.ApplicationExitCode}");
        AppSoundService.Current.Dispose();

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        AsyncFileLogService.Current.Dispose();
        base.OnExit(e);
    }
}