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
        AsyncFileLogService.Current.Initialize();
        AsyncFileLogService.Current.Application($"STARTUP {AppVersion.DisplayVersion}");
        StartupBootstrapService.EnsureStartupFiles();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        AppSoundService.Current.Initialize();
        Dispatcher.BeginInvoke(
            new Action(AppSoundService.Current.PlayStartup),
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
                "Phần mềm vừa chặn một lỗi để tránh bị thoát.\n\n" +
                "Trạng thái phần cứng có thể chưa sẵn sàng. Hãy kiểm tra kết nối bo và thử lại.\n\n" +
                $"Chi tiết: {e.Exception.Message}",
                "JBZ - Đã chặn lỗi",
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
