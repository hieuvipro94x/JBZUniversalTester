using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using JBZUniversalTester.Services;
using JBZUniversalTester.Versioning;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _ownsSingleInstanceMutex = SingleInstanceGate.TryAcquire(
            @"Local\JBZUniversalTester.Production", out _singleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            // No modal dialog: it would keep a second process alive on each
            // extra click while the first process is still starting slowly.
            Shutdown(2);
            return;
        }

        StartupPerformanceTrace.Begin();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        var splash = new StartupWindow();
        MainWindow = splash;
        splash.Show();
        // Paint the lightweight splash before any configuration or database I/O.
        await Dispatcher.Yield(DispatcherPriority.Background);
        string startupStage = "Đọc cài đặt";
        MainViewModel? viewModel = null;
        try
        {
            splash.Report(10, "Đang đọc cài đặt và ngôn ngữ…");
            long configurationStarted = Stopwatch.GetTimestamp();
            (AppSettings settings, Models.ProductionSettings productionSettings) = await Task.Run(() =>
            {
                StartupBootstrapService.EnsureFastConfiguration();
                return (AppSettings.Load(), ProductionConfigService.Load());
            });
            AsyncFileLogService.Current.Configure(productionSettings.EnableSystemLogs);
            AsyncFileLogService.Current.Application($"STARTUP {AppVersion.DisplayVersion}");
            StartupPerformanceTrace.Stage("CONFIG_READY", configurationStarted);

            startupStage = "Chuẩn bị database";
            splash.Report(30, "Đang kiểm tra cấu trúc database và chuẩn bị dữ liệu…");
            Task databasePreparation = Task.Run(async () =>
            {
                using var scope = StartupPerformanceTrace.Measure("DATABASE_PREPARE");
                await StartupBootstrapService.EnsureDeferredProductionFiles(failOnError: true);
            });
            Task resourcePreparation = Task.Run(() =>
            {
                using var scope = StartupPerformanceTrace.Measure("RESOURCE_PREPARE");
                AppSoundService.Current.PreloadResources();
            });
            Task dataPreparation = Task.WhenAll(databasePreparation, resourcePreparation);
            StartupPerformanceTrace.Mark("T0 App.OnStartup PREPARING");

            startupStage = "Nạp mã hàng và chuẩn bị giao diện";
            viewModel = new MainViewModel(settings, productionSettings, productionDataPreparation: dataPreparation);
            viewModel.StartupProgress += splash.Report;
            // Driver handshake waits overlap with MainWindow BAML/layout work.
            Task applicationInitialization = viewModel.InitializeApplicationAsync();
            var mainWindow = new MainWindow(viewModel);
            try
            {
                await mainWindow.PrepareStartupAsync(applicationInitialization);
                // A hardware timeout may unlock Main, but data failures must
                // still reach the startup error path before showing the app.
                await dataPreparation;
            }
            finally
            {
                viewModel.StartupProgress -= splash.Report;
            }
            splash.Report(98, "Đang mở màn hình chính…");
            await Dispatcher.Yield(DispatcherPriority.Background);

            // Keep the splash until the populated MainWindow has painted once.
            void FinishStartup(object? sender, EventArgs args)
            {
                mainWindow.ContentRendered -= FinishStartup;
                StartupPerformanceTrace.Mark("SPLASH_HANDOFF_COMPLETE");
                splash.Report(100, "Đã hoàn tất khởi động");
                splash.Complete();
            }
            mainWindow.ContentRendered += FinishStartup;
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            WriteCrashDiagnostics(ex, $"Startup.{startupStage}");
            MessageBox.Show(splash, $"Không thể khởi động ở bước: {startupStage}.\n{ex.Message}\n\nVui lòng kiểm tra dữ liệu và mở lại phần mềm.",
                "KHÔNG THỂ KHỞI ĐỘNG", MessageBoxButton.OK, MessageBoxImage.Error);
            splash.Complete();
            try
            {
                if (viewModel is not null)
                    await viewModel.ShutdownAsync();
            }
            catch (Exception cleanupException)
            {
                WriteCrashDiagnostics(cleanupException, "Startup.Cleanup");
            }
            finally { Shutdown(1); }
            return;
        }

        // Defer audio I/O until MainWindow has rendered.
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
                    "Phần mềm cần khởi động lại để tiếp tục. Báo cáo lỗi đã được lưu.",
                    "VUI LÒNG KHỞI ĐỘNG LẠI",
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
                "Thao tác chưa hoàn thành. Vui lòng khởi động lại phần mềm.",
                "THÔNG BÁO",
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
        if (!_ownsSingleInstanceMutex)
        {
            // A rejected launch never initialized runtime services.
            base.OnExit(e);
            return;
        }

        try
        {
            AsyncFileLogService.Current.Application($"SHUTDOWN exitCode={e.ApplicationExitCode}");
            AppSoundService.Current.Dispose();

            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

            AsyncFileLogService.Current.Dispose();
        }
        finally
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            _ownsSingleInstanceMutex = false;
            base.OnExit(e);
        }
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
