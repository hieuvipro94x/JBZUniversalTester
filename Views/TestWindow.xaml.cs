using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Versioning;

namespace JBZUniversalTester.Views;

public partial class TestWindow : Window
{
    private bool _allowClose;
    private bool _initializationStarted;
    private bool _closeInProgress;
    private readonly bool _autoStartProduction;
    private readonly DispatcherTimer _clockTimer;
    private NotifyCollectionChangedEventHandler? _faultsChangedHandler;
    private CancellationTokenSource? _scrollCts;
    private readonly DispatcherTimer _bottomToolbarHideTimer;
    private bool _isMouseOverBottomHotZone;
    private bool _isMouseOverBottomToolbar;
    private bool _bottomToolbarVisible;

    public TestWindow(TestViewModel viewModel, bool autoStartProduction = true)
    {
        InitializeComponent();
        Title = $"UniversalTester {AppVersion.DisplayVersion} - Màn hình kiểm tra";
        TestAppVersionText.Text = AppVersion.DisplayVersion;
        DataContext = viewModel;
        _autoStartProduction = autoStartProduction;

        if (!_autoStartProduction)
            viewModel.State = "CẤU HÌNH CARD KHÔNG ĐỦ";

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateClock();

        // V12.9.2: delay 200 ms chỉ dành cho UX toolbar, tuyệt đối không dùng cho Probe.
        _bottomToolbarHideTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _bottomToolbarHideTimer.Tick += BottomToolbarHideTimer_Tick;

        UpdateClock();
    }

    private void UpdateClock() => CurrentTimeText.Text = DateTime.Now.ToString("yyyy/MM/dd  HH:mm:ss");

    private async void TestWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted)
            return;

        _initializationStarted = true;
        if (DataContext is not TestViewModel viewModel)
            return;

        _clockTimer.Start();
        InitializeBottomToolbar();
        ModelTitleText.Visibility = viewModel.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        ConnectorColumn.Visibility = viewModel.ShowConnector ? Visibility.Visible : Visibility.Collapsed;

        _faultsChangedHandler = (_, _) => ScheduleScrollToFirstFault(viewModel);
        viewModel.Faults.CollectionChanged += _faultsChangedHandler;

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            await viewModel.InitializeHardwareAsync();
            if (_autoStartProduction)
                await viewModel.StartProductionTestAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Màn hình Test đã mở nhưng có lỗi khi khởi tạo.\n\n{ex.Message}",
                "Cảnh báo khởi tạo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ScheduleScrollToFirstFault(TestViewModel viewModel)
    {
        _scrollCts?.Cancel();
        _scrollCts?.Dispose();
        _scrollCts = new CancellationTokenSource();
        CancellationToken token = _scrollCts.Token;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                int delay = viewModel.ScrollDelay;
                if (delay > 0)
                    await Task.Delay(delay, token);
                if (token.IsCancellationRequested || viewModel.Faults.Count == 0)
                    return;
                FaultGrid.ScrollIntoView(viewModel.Faults[0]);
            }
            catch (OperationCanceledException) { }
        }, DispatcherPriority.Background);
    }

    private void InitializeBottomToolbar()
    {
        _bottomToolbarHideTimer.Stop();
        _isMouseOverBottomHotZone = false;
        _isMouseOverBottomToolbar = false;
        _bottomToolbarVisible = false;

        BottomButtonPanel.Visibility = Visibility.Collapsed;
        BottomToolbarHotZone.Visibility = Visibility.Visible;
    }

    private void BottomToolbarHotZone_MouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseOverBottomHotZone = true;
        _bottomToolbarHideTimer.Stop();
        ShowBottomToolbar();
    }

    private void BottomToolbarHotZone_MouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOverBottomHotZone = false;
        ScheduleBottomToolbarHide();
    }

    private void BottomButtonPanel_MouseEnter(object sender, MouseEventArgs e)
    {
        _isMouseOverBottomToolbar = true;
        _bottomToolbarHideTimer.Stop();
        ShowBottomToolbar();
    }

    private void BottomButtonPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        _isMouseOverBottomToolbar = false;
        ScheduleBottomToolbarHide();
    }

    private void ScheduleBottomToolbarHide()
    {
        if (_isMouseOverBottomHotZone || _isMouseOverBottomToolbar)
            return;

        _bottomToolbarHideTimer.Stop();
        _bottomToolbarHideTimer.Start();
    }

    private void BottomToolbarHideTimer_Tick(object? sender, EventArgs e)
    {
        _bottomToolbarHideTimer.Stop();
        if (!_isMouseOverBottomHotZone && !_isMouseOverBottomToolbar)
            HideBottomToolbar();
    }

    private void ShowBottomToolbar()
    {
        _bottomToolbarHideTimer.Stop();
        if (_bottomToolbarVisible)
            return;

        _bottomToolbarVisible = true;
        BottomToolbarHotZone.Visibility = Visibility.Collapsed;
        BottomButtonPanel.Visibility = Visibility.Visible;
    }

    private void HideBottomToolbar()
    {
        if (!_bottomToolbarVisible)
            return;

        _bottomToolbarVisible = false;
        BottomButtonPanel.Visibility = Visibility.Collapsed;
        BottomToolbarHotZone.Visibility = Visibility.Visible;
    }

    private async void BackToMain_Click(object sender, RoutedEventArgs e)
    {
        if (_closeInProgress)
            return;
        _closeInProgress = true;

        try
        {
            if (DataContext is TestViewModel viewModel)
                await viewModel.StopViewAsync();
        }
        catch { }
        finally
        {
            _allowClose = true;
            Close();
        }
    }

    private async void TestWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            CleanupUiHandlers();
            return;
        }

        e.Cancel = true;
        if (_closeInProgress)
            return;

        MessageBoxResult result = MessageBox.Show(this,
            "Bạn có muốn dừng kiểm tra và quay về màn hình chọn mã?",
            "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _closeInProgress = true;
        try
        {
            if (DataContext is TestViewModel viewModel)
                await viewModel.StopViewAsync();
        }
        catch { }
        finally
        {
            _allowClose = true;
            CleanupUiHandlers();
            Close();
        }
    }

    private void CleanupUiHandlers()
    {
        _clockTimer.Stop();
        _bottomToolbarHideTimer.Stop();
        _scrollCts?.Cancel();
        _scrollCts?.Dispose();
        _scrollCts = null;
        if (DataContext is TestViewModel vm && _faultsChangedHandler is not null)
            vm.Faults.CollectionChanged -= _faultsChangedHandler;
        _faultsChangedHandler = null;
    }
}
