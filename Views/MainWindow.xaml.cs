using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.Versioning;

namespace JBZUniversalTester.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private TestWindow? _testWindow;
    private ProductionSettingsPage? _settingsPage;
    private HistoryPage? _historyPage;
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private bool _startupStarted;

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

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;
        StartupPerformanceTrace.Mark("T2 MainWindow first ContentRendered");
    }

    private void ViewModel_ExplicitModelLoaded(ProductModel model)
    {
        // File dialog vừa đóng và model đã được parse/SetModel hoàn chỉnh.
        // Tự vào TestView ngay, không yêu cầu click BẮT ĐẦU lần thứ hai.
        Dispatcher.BeginInvoke(new Action(() => OpenTestWindowCore(allowViewWhenInsufficient: true)));
    }


    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupStarted)
            return;

        _startupStarted = true;
        StartTestButton.IsEnabled = false;
        SelectModelButton.IsEnabled = false;

        try
        {
            // V10.9: ngay khi MainWindow xuất hiện, tự nạp mã gần nhất và
            // tự kết nối/recovery bo. Bấm BẮT ĐẦU KIỂM TRA chỉ chuyển sang
            // production scan, không còn là thời điểm mới đi kết nối phần cứng.
            await _viewModel.InitializeApplicationAsync();
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

    private void OpenTestWindow_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenTestWindowCore(allowViewWhenInsufficient: false);
    }

    private void OpenTestWindowCore(bool allowViewWhenInsufficient = false)
    {
        if (_viewModel.Test.IsProductRemovalPending)
        {
            UpdateProductRemovalGate();
            return;
        }

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
        bool hasCapacity = _viewModel.EnsureModelCardCapacity(showWarning: !allowViewWhenInsufficient);
        if (!hasCapacity && !allowViewWhenInsufficient)
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
            _testWindow = new TestWindow(_viewModel.Test, autoStartProduction: hasCapacity);
            _testWindow.Closed += TestWindow_Closed;

            // Faults đã được SetModel/BuildRows trước khi Show(), vì vậy DataGrid
            // có cấu hình THT ngay frame render đầu tiên của TestWindow.
            _testWindow.Show();
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
        WindowState = WindowState.Maximized;
        UpdateProductRemovalGate();
        Activate();
        Focus();
    }

    private void TestViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TestViewModel.IsProductRemovalPending))
            Dispatcher.BeginInvoke(UpdateProductRemovalGate);
    }

    private void UpdateProductRemovalGate()
    {
        bool blocked = _viewModel.Test.IsProductRemovalPending;
        ProductRemovalNotice.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        StartTestButton.IsEnabled = !blocked;
        SelectModelButton.IsEnabled = !blocked;
    }

    private void OpenSettings_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            string password = _viewModel.ProductionSettings.Password ?? string.Empty;
            if (!string.IsNullOrEmpty(password))
            {
                SettingsPasswordBox.Password = string.Empty;
                SettingsPasswordErrorText.Visibility = Visibility.Collapsed;
                SettingsPasswordGate.Visibility = Visibility.Visible;
                SettingsPasswordBox.Focus();
                return;
            }

            ShowSettingsPage();
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

    private void ConfirmSettingsPassword_Click(object sender, RoutedEventArgs e) =>
        ConfirmSettingsPassword();

    private void SettingsPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ConfirmSettingsPassword();
        e.Handled = true;
    }

    private void ConfirmSettingsPassword()
    {
        string expected = _viewModel.ProductionSettings.Password ?? string.Empty;
        if (!string.Equals(SettingsPasswordBox.Password, expected, StringComparison.Ordinal))
        {
            SettingsPasswordErrorText.Visibility = Visibility.Visible;
            SettingsPasswordBox.SelectAll();
            SettingsPasswordBox.Focus();
            return;
        }

        SettingsPasswordGate.Visibility = Visibility.Collapsed;
        SettingsPasswordBox.Password = string.Empty;
        SettingsPasswordErrorText.Visibility = Visibility.Collapsed;
        ShowSettingsPage();
    }

    private void CancelSettingsPassword_Click(object sender, RoutedEventArgs e)
    {
        SettingsPasswordGate.Visibility = Visibility.Collapsed;
        SettingsPasswordBox.Password = string.Empty;
        SettingsPasswordErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowSettingsPage()
    {
        CloseInternalPage();

        _settingsPage = new ProductionSettingsPage(_viewModel);
        _settingsPage.RequestClose += InternalPage_RequestClose;
        _settingsPage.SettingsSaved += SettingsPage_SettingsSaved;

        InternalPageHost.Content = _settingsPage;
        InternalPageHost.Visibility = Visibility.Visible;
    }

    private async void SettingsPage_SettingsSaved(object? sender, EventArgs e)
    {
        try
        {
            if (_settingsPage is not null)
                await _settingsPage.ReleaseManualOutputsAsync();
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
        if (_settingsPage is not null)
        {
            _settingsPage.RequestClose -= InternalPage_RequestClose;
            _settingsPage.SettingsSaved -= SettingsPage_SettingsSaved;
            _settingsPage = null;
        }

        if (_historyPage is not null)
        {
            _historyPage.RequestClose -= InternalPage_RequestClose;
            _historyPage = null;
        }

        InternalPageHost.Content = null;
        InternalPageHost.Visibility = Visibility.Collapsed;
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
