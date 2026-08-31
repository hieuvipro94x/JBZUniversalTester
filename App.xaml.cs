using System;
using System.Windows;
using System.Windows.Threading;
using JBZUniversalTester.Services;
using JBZUniversalTester.Versioning;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\JBZUniversalTester.Production",
            createdNew: out _ownsSingleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            MessageBox.Show(
                "JBZ Universal Tester đang chạy. Không thể mở thêm phiên thứ hai vì bo và dữ liệu sản xuất chỉ được phép có một chủ sở hữu.",
                "JBZ Universal Tester",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(2);
            return;
        }

        // Khởi tạo/migrate History và config trước để khóa log lấy đúng giá trị
        // đã lưu của trạm. History không phụ thuộc và không bị khóa bởi log.
        StartupBootstrapService.EnsureFastConfiguration();
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

    private static async void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashDiagnostics(e.Exception, "DispatcherUnhandledException");

        if (e.Exception is OutOfMemoryException)
        {
            // Không tiếp tục một visual tree nửa khởi tạo sau OOM. Ghi crash report
            // ở trên, đưa hardware về safe state best-effort rồi shutdown có kiểm soát.
            e.Handled = true;
            try
            {
                if (Current?.MainWindow?.DataContext is MainViewModel viewModel)
                    await viewModel.ShutdownAsync();
            }
            catch (Exception shutdownException)
            {
                WriteCrashDiagnostics(shutdownException, "OutOfMemory.SafeShutdown");
            }

            try
            {
                MessageBox.Show(
                    "Ứng dụng đã hết bộ nhớ và phải đóng để bảo đảm trạng thái phần cứng. " +
                    "Vui lòng mở lại ứng dụng; báo cáo lỗi đã được ghi trong thư mục Crash.",
                    "JBZ - LỖI BỘ NHỚ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
            }

            Current?.Shutdown(-1);
            return;
        }

        // V15.1 SAFE OFFLINE MODE - lớp bảo vệ cuối cùng cho lỗi UI/async event.
        // Các thao tác phần cứng dự kiến phải được catch ở ViewModel/Command trước;
        // guard này tránh app văng ra nếu vẫn còn exception phục hồi được lọt tới Dispatcher.
        e.Handled = true;
        try
        {
            MessageBox.Show(
                "Ứng dụng gặp lỗi hệ thống và đã dừng thao tác hiện tại.\n\n" +
                "Thao tác hiện tại đã dừng. Vui lòng xem log để xác định lỗi cấu hình, giao diện, thiết bị hoặc dữ liệu.\n\n" +
                $"Chi tiết: {e.Exception.Message}",
                "JBZ - LỖI HỆ THỐNG",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
        }
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        Exception exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Unknown fatal exception");
        WriteCrashDiagnostics(exception, "AppDomain.UnhandledException");
    }

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
        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }

    private static string BuildRuntimeContext()
    {
        try
        {
            if (Current?.MainWindow?.DataContext is not MainViewModel viewModel)
                return "MainViewModel unavailable";

            return $"Model={viewModel.Model?.ModelName ?? "(none)"}; " +
                   $"Part={viewModel.Model?.PartNumber ?? "(none)"}; " +
                   $"AppState={viewModel.Status}; TestState={viewModel.Test.State}";
        }
        catch
        {
            return "Runtime context unavailable";
        }
    }

    private static void WriteCrashDiagnostics(Exception exception, string source)
    {
        // Crash diagnostics must stay best-effort, especially for OOM where even
        // formatting a second diagnostic can fail before hardware reaches safe state.
        try
        {
            CrashReportService.Write(exception, source, BuildRuntimeContext());
        }
        catch
        {
            // The original fatal exception remains authoritative.
        }

        try
        {
            AsyncFileLogService.Current.Error($"{source}: {exception}");
        }
        catch
        {
            // Logging cannot be allowed to recursively fail the fatal-error handler.
        }
    }
}
