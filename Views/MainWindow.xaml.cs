using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.Versioning;

namespace JBZUniversalTester.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan StartupControlUnlockTimeout = TimeSpan.FromSeconds(8);
    private readonly MainViewModel _viewModel;
    private TestWindow? _testWindow;
    private ProductionSettingsPage? _settingsPage;
    private HistoryPage? _historyPage;
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private bool _startupStarted;
    private long _internalPageGeneration;
    private UiStallWatchdog? _uiStallWatchdog;

    public MainWindow()
    {
        InitializeComponent();
        StartupPerformanceTrace.Mark("T1 MainWindow constructed");
        Title = $"UniversalTester {AppVersion.DisplayVersion} - JBZ Production";
        AppVersionText.Text = $"JBZ Universal : {AppVersion.DisplayVersion}";

        _viewModel = new MainViewModel();
        _viewModel.ExplicitModelLoaded += ViewModel_ExplicitModelLoaded;
        _viewModel.Test.PropertyChanged += TestViewModel_PropertyChanged;
        DataContext = _viewModel;
        ContentRendered += MainWindow_ContentRendered;
        UpdateProductRemovalGate();
    }

    private async void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;
        StartupPerformanceTrace.Mark("T2 MainWindow first ContentRendered");
        LogMemory("MEM STARTUP");
        _uiStallWatchdog = new UiStallWatchdog(Dispatcher);

        // Quan trọng: không khởi tạo FTDI/model/printer trong Loaded.
        // ContentRendered đảm bảo WPF đã vẽ frame đầu tiên; Task.Yield trả
        // quyền cho Dispatcher xử lý input/paint trước khi bắt đầu startup I/O.
        await Task.Yield();
        await InitializeApplicationAfterFirstRenderAsync();
    }

    private void ViewModel_ExplicitModelLoaded(ProductModel model)
    {
        // File dialog vừa đóng và model đã được parse/SetModel hoàn chỉnh.
        // Tự vào TestView ngay, không yêu cầu click BẮT ĐẦU lần thứ hai.
        Dispatcher.BeginInvoke(new Action(() => OpenTestWindowCore(allowViewWhenInsufficient: true)));
    }


    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Loaded có thể xảy ra trước ContentRendered. Chỉ khóa các nút phụ thuộc
        // hardware ở đây; tuyệt đối không chạy FTDI/model/printer trước frame đầu.
        StartTestButton.IsEnabled = false;
        SelectModelButton.IsEnabled = false;
    }

    private async Task InitializeApplicationAfterFirstRenderAsync()
    {
        if (_startupStarted)
            return;

        _startupStarted = true;

        try
        {
            // Tự nạp mã gần nhất và tự kết nối/recovery bo sau khi cửa sổ đã
            // render. Các API bên dưới vẫn await bình thường nên Dispatcher
            // không bị giữ trong thời gian handshake/delay phần cứng.
            Task initialization = _viewModel.InitializeApplicationAsync();
            Task completed = await Task.WhenAny(
                initialization,
                Task.Delay(StartupControlUnlockTimeout));

            if (completed != initialization)
            {
                // Một số driver/D2XX trên máy production có thể giữ lời gọi mở
                // thiết bị lâu bất thường. Không để lỗi phần cứng khóa luôn việc
                // chọn THT/cài đặt. Tác vụ kết nối vẫn tiếp tục và được theo dõi.
                _viewModel.Status =
                    "KẾT NỐI BO ĐANG CHẬM - PHẦN MỀM ĐANG TỰ THỬ LẠI";
                AsyncFileLogService.Current.Error(
                    $"STARTUP HARDWARE TIMEOUT after {StartupControlUnlockTimeout.TotalSeconds:0}s; " +
                    "operator controls unlocked while initialization continues.");
                _ = ObserveDeferredStartupAsync(initialization);
                return;
            }

            await initialization;
        }
        catch (Exception ex)
        {
            // Startup phải luôn để MainWindow sử dụng được. Lỗi board/model đã
            // được ViewModel ghi vào Status/HardwareStatus; popup này chỉ cho lỗi
            // ngoài dự kiến.
            MessageBox.Show(
                this,
                $"Khởi tạo ứng dụng chưa hoàn chỉnh.\n\n{ex.Message}",
                "Cảnh báo khởi động",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            UpdateProductRemovalGate();
        }
    }

    private async Task ObserveDeferredStartupAsync(Task initialization)
    {
        try
        {
            await initialization;
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"DEFERRED STARTUP FAILED: {ex}");
        }
        finally
        {
            if (!_shutdownStarted)
                await Dispatcher.InvokeAsync(UpdateProductRemovalGate);
        }
    }

    private void OpenTestWindow_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenTestWindowCore(allowViewWhenInsufficient: false);
    }

    private void OpenTestWindowCore(bool allowViewWhenInsufficient = false)
    {
        if (_viewModel.Model is null)
        {
            MessageBox.Show(
                "Hãy chọn mã hàng trước khi bắt đầu kiểm tra.",
                "Chưa chọn mã hàng",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_viewModel.Test.IsManualModeActive)
        {
            MessageBox.Show(
                "Relay tay đang bật. Hãy bấm TẮT hoặc RESET trước khi bắt đầu Production Test.",
                "Production bị khóa",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Khi người vận hành vừa chọn model, vẫn mở TestView để nhìn toàn bộ
        // cấu hình dù số card chưa đủ; chỉ KHÔNG ARM test. MainViewModel đã
        // cảnh báo thiếu card ngay sau khi load. Với nút BẮT ĐẦU thủ công thì
        // vẫn chặn như máy gốc.
        bool boardConnected = _viewModel.Test.IsBoardConnected;
        bool hasCapacity = _viewModel.EnsureModelCardCapacity(
            showWarning: boardConnected && !allowViewWhenInsufficient);
        if (boardConnected && !hasCapacity && !allowViewWhenInsufficient)
            return;

        if (_testWindow is { IsLoaded: true })
        {
            if (_testWindow.WindowState == WindowState.Minimized)
                _testWindow.WindowState = WindowState.Maximized;

            _testWindow.Activate();
            return;
        }

        try
        {
            bool offlinePreview = !boardConnected;
            _testWindow = new TestWindow(
                _viewModel.Test,
                autoStartProduction: boardConnected && hasCapacity,
                offlinePreview: offlinePreview);
            _testWindow.Closed += TestWindow_Closed;

            // Faults đã được SetModel/BuildRows trước khi Show(), vì vậy DataGrid
            // có cấu hình THT ngay frame render đầu tiên của TestWindow.
            _testWindow.Show();
            LogMemory("MEM TESTWINDOW_OPEN");
            Hide();
        }
        catch (Exception ex)
        {
            if (_testWindow is not null)
            {
                _testWindow.Closed -= TestWindow_Closed;
                _testWindow = null;
            }

            MessageBox.Show(
                ex.ToString(),
                "Không thể mở màn hình kiểm tra",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void TestWindow_Closed(
        object? sender,
        EventArgs e)
    {
        if (_testWindow is not null)
        {
            _testWindow.Closed -= TestWindow_Closed;
            _testWindow = null;
        }

        Show();
        LogMemory("MEM TESTWINDOW_CLOSE");
        WindowState = WindowState.Maximized;
        UpdateProductRemovalGate();
        Activate();
        Focus();
    }

    private void TestViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TestViewModel.IsProductRemovalPending) or
            nameof(TestViewModel.IsBoardConnected))
            Dispatcher.BeginInvoke(UpdateProductRemovalGate);
    }

    private void UpdateProductRemovalGate()
    {
        bool blocked = _viewModel.Test.IsProductRemovalPending;
        ProductRemovalNotice.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        // Cho phép chuẩn bị/xem mã hàng ở máy phát triển không có bo. TestWindow
        // tự vào Offline Preview và không ARM scan/PASS/relay. Removal gate vẫn
        // khóa đổi mã hàng để bảo toàn chu kỳ production đang dở.
        StartTestButton.IsEnabled = _viewModel.Model is not null;
        SelectModelButton.IsEnabled = !blocked;
    }

    private async void OpenSettings_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await ShowSettingsPageAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.ToString(),
                "Không thể mở cài đặt",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ShowSettingsPageAsync()
    {
        CloseInternalPage();
        long navigationGeneration = Volatile.Read(ref _internalPageGeneration);
        LogMemory("MEM BEFORE_SETTINGS");

        // Config/THT preparation có disk/parse work; tạo ViewModel ngoài
        // Dispatcher để BAML của page được cấp phát sau khi UI đã rảnh.
        ProductionSettingsViewModel settingsViewModel = await Task.Run(
            () => new ProductionSettingsViewModel(_viewModel.Test));

        // Người dùng có thể chuyển sang History/đóng app trong lúc config/THT
        // đang được chuẩn bị. Không dựng BAML cho một navigation đã stale.
        if (_shutdownStarted ||
            navigationGeneration != Volatile.Read(ref _internalPageGeneration))
        {
            return;
        }

        _settingsPage = new ProductionSettingsPage(_viewModel, settingsViewModel);
        _settingsPage.RequestClose += InternalPage_RequestClose;
        _settingsPage.SettingsSaved += SettingsPage_SettingsSaved;

        InternalPageHost.Content = _settingsPage;
        InternalPageHost.Visibility = Visibility.Visible;
        LogMemory("MEM AFTER_SETTINGS_OPEN");
    }

    private async void SettingsPage_SettingsSaved(object? sender, EventArgs e)
    {
        ProductionSettingsPage? savedPage = sender as ProductionSettingsPage;
        try
        {
            if (savedPage is not null)
                await savedPage.ReleaseManualOutputsAsync();
            await _viewModel.ReloadProductionSettingsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Đã lưu file cài đặt nhưng chưa reconfigure runtime hoàn chỉnh.\n\n{ex.Message}",
                "Cảnh báo áp dụng cài đặt",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (ReferenceEquals(_settingsPage, savedPage))
            CloseInternalPage();
    }

    private void OpenHistory_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            CloseInternalPage();

            _historyPage = new HistoryPage(_viewModel.ProductionSettings);
            _historyPage.RequestClose += InternalPage_RequestClose;

            InternalPageHost.Content = _historyPage;
            InternalPageHost.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.ToString(),
                "Không thể mở lịch sử",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void InternalPage_RequestClose(object? sender, EventArgs e) =>
        CloseInternalPage();

    private void CloseInternalPage()
    {
        Interlocked.Increment(ref _internalPageGeneration);

        if (_settingsPage is not null)
        {
            _settingsPage.RequestClose -= InternalPage_RequestClose;
            _settingsPage.SettingsSaved -= SettingsPage_SettingsSaved;
            _settingsPage.ReleasePageResources();
            _settingsPage = null;
            LogMemory("MEM SETTINGS_CLOSE");
        }

        if (_historyPage is not null)
        {
            _historyPage.RequestClose -= InternalPage_RequestClose;
            _historyPage.ReleasePageResources();
            _historyPage = null;
        }

        InternalPageHost.Content = null;
        InternalPageHost.Visibility = Visibility.Collapsed;
    }

    private static void LogMemory(string marker)
    {
        using Process process = Process.GetCurrentProcess();
        AsyncFileLogService.Current.Performance(
            $"{marker} private_mb={process.PrivateMemorySize64 / 1048576d:0.###} " +
            $"working_set_mb={process.WorkingSet64 / 1048576d:0.###} " +
            $"gc_heap_mb={GC.GetTotalMemory(false) / 1048576d:0.###} " +
            $"handles={process.HandleCount} threads={process.Threads.Count}");
    }

    private async void MainWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_shutdownComplete)
            return;

        // Không cho WPF hủy cửa sổ trước khi worker D2XX/VISA được dừng.
        e.Cancel = true;

        if (_shutdownStarted)
            return;

        _shutdownStarted = true;

        try
        {
            CloseInternalPage();
            _uiStallWatchdog?.Dispose();
            _uiStallWatchdog = null;
            _viewModel.ExplicitModelLoaded -= ViewModel_ExplicitModelLoaded;
            _viewModel.Test.PropertyChanged -= TestViewModel_PropertyChanged;
            await _viewModel.ShutdownAsync();
        }
        finally
        {
            _shutdownComplete = true;

            // Đóng lần hai sau khi mọi handle đã được giải phóng.
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
    }

}
