using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Versioning;

namespace JBZUniversalTester.Views;

public partial class TestWindow : Window
{
    private const double TestGridReferenceWidth = 1366;
    private const double TestGridReferenceHeight = 768;
    private const double TestGridMaximumScale = 1.25;
    private bool _allowClose;
    private bool _initializationStarted;
    private bool _closeInProgress;
    private readonly bool _autoStartProduction;
    private readonly bool _offlinePreview;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _yellowPulseTimer;
    private readonly DispatcherTimer _whitePulseTimer;
    private NotifyCollectionChangedEventHandler? _faultsChangedHandler;
    private CancellationTokenSource? _scrollCts;
    private CancellationTokenSource? _greenBlinkCts;
    private Task _greenBlinkTask = Task.CompletedTask;
    private int _greenBlinkRequestGeneration;
    private int _statusLedHandlersAttached;
    private int _statusPulseDispatchQueued;
    private int _statusStateDispatchQueued;
    private int _yellowPulsePending;
    private int _whitePulsePending;
    private bool _lastLedBoardConnected;
    private string _lastLedState = string.Empty;
    private string _lastLedResultStatus = string.Empty;

    private static readonly Brush YellowLedOffBrush = CreateFrozenBrush(0x6B, 0x62, 0x40);
    private static readonly Brush YellowLedOnBrush = CreateFrozenBrush(0xFF, 0xD4, 0x00);
    private static readonly Brush WhiteLedOffBrush = CreateFrozenBrush(0x9C, 0xA3, 0xAF);
    private static readonly Brush WhiteLedOnBrush = CreateFrozenBrush(0xFF, 0xFF, 0xFF);
    private static readonly Brush GreenLedOffBrush = CreateFrozenBrush(0x31, 0x54, 0x3B);
    private static readonly Brush GreenLedOnBrush = CreateFrozenBrush(0x22, 0xC5, 0x5E);
    private static readonly Brush RedLedOffBrush = CreateFrozenBrush(0x5A, 0x30, 0x30);
    private static readonly Brush RedLedOnBrush = CreateFrozenBrush(0xEF, 0x44, 0x44);

    public TestWindow(
        TestViewModel viewModel,
        bool autoStartProduction = true,
        bool offlinePreview = false)
    {
        InitializeComponent();
        Title = $"UniversalTester {AppVersion.DisplayVersion} - Màn hình kiểm tra";
        TestAppVersionText.Text = AppVersion.DisplayVersion;
        DataContext = viewModel;
        _autoStartProduction = autoStartProduction;
        _offlinePreview = offlinePreview;

        if (_offlinePreview)
        {
            OperationTablesHost.Visibility = Visibility.Collapsed;
            viewModel.State = "XEM MÃ HÀNG OFFLINE - BO CHƯA KẾT NỐI";
        }
        else if (!_autoStartProduction)
            viewModel.State = "CẤU HÌNH CARD KHÔNG ĐỦ";

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += ClockTimer_Tick;

        _yellowPulseTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _yellowPulseTimer.Tick += YellowPulseTimer_Tick;

        _whitePulseTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _whitePulseTimer.Tick += WhitePulseTimer_Tick;

        UpdateClock();
        ContentRendered += TestWindow_ContentRendered;
    }

    private static Brush CreateFrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void TestWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= TestWindow_ContentRendered;
        StartupPerformanceTrace.Mark("T9 TestWindow first rendered");
    }

    private void UpdateClock() => CurrentTimeText.Text = DateTime.Now.ToString("HH:mm:ss");

    private void ClockTimer_Tick(object? sender, EventArgs e) => UpdateClock();

    private void TestWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (TestHeaderSurface is null)
        {
            return;
        }

        // Ở 1920x1080 header giãn ra dùng toàn bộ màn hình. Chỉ khi viewport
        // nhỏ hơn thiết kế 1344px (ví dụ 1024x768), Viewbox mới scale xuống.
        TestHeaderSurface.Width = Math.Max(1344, e.NewSize.Width - 16);
        ApplyResponsiveTestGridLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void ApplyResponsiveTestGridLayout(double viewportWidth, double viewportHeight)
    {
        // 1280x768 vẫn giữ cỡ chữ lớn, dễ đọc. Từ mốc 1366x768 trở lên,
        // chữ, chiều cao dòng và header tăng cùng một tỷ lệ; cột DataGrid dùng
        // Star Width nên tự nhận phần chiều rộng tăng thêm một cách đồng đều.
        double widthScale = Math.Max(1, viewportWidth) / TestGridReferenceWidth;
        double heightScale = Math.Max(1, viewportHeight) / TestGridReferenceHeight;
        double scale = Math.Clamp(
            Math.Min(widthScale, heightScale),
            1.0,
            TestGridMaximumScale);

        Resources["TestGridBaseFontSize"] = ResponsiveValue(18, scale);
        Resources["TestFaultGridFontSize"] = ResponsiveValue(20, scale);
        Resources["TestGridHeaderFontSize"] = ResponsiveValue(18, scale);
        Resources["TestFaultGridHeaderFontSize"] = ResponsiveValue(20, scale);
        Resources["TestGridRowHeight"] = ResponsiveValue(34, scale);
        Resources["TestGridColumnHeaderHeight"] = ResponsiveValue(40, scale);
    }

    private static double ResponsiveValue(double baseline, double scale) =>
        Math.Round(baseline * scale * 2, MidpointRounding.AwayFromZero) / 2;

    private async void TestWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted)
            return;

        _initializationStarted = true;
        if (DataContext is not TestViewModel viewModel)
            return;

        _clockTimer.Start();
        AttachStatusLedHandlers(viewModel);
        ModelTitleText.Visibility = viewModel.ShowTitle ? Visibility.Visible : Visibility.Collapsed;
        ConnectorColumn.Visibility = Visibility.Visible;

        if (!_offlinePreview)
        {
            _faultsChangedHandler = (_, _) => ScheduleScrollToFirstFault(viewModel);
            viewModel.Faults.CollectionChanged += _faultsChangedHandler;
        }

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (_autoStartProduction && !_offlinePreview)
                await viewModel.StartProductionTestAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Màn hình Test đã mở nhưng có lỗi khi khởi tạo.\n\n{ex.Message}",
                "Cảnh báo khởi tạo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AttachStatusLedHandlers(TestViewModel viewModel)
    {
        if (Interlocked.Exchange(ref _statusLedHandlersAttached, 1) != 0)
            return;

        viewModel.BoardFrameActivity += ViewModel_BoardFrameActivity;
        viewModel.PropertyChanged += ViewModel_StatusPropertyChanged;
        _lastLedBoardConnected = viewModel.IsBoardConnected;
        _lastLedState = viewModel.State ?? string.Empty;
        _lastLedResultStatus = viewModel.ResultStatusText;
        ResetActivityLeds();
        bool hardwareReady = viewModel.IsBoardConnected && !viewModel.IsDeviceFault;
        SetGreenLed(hardwareReady);
        SetRedLed(hardwareReady && IsConfirmedFailLedState(viewModel, viewModel.ResultStatusText));
    }

    private void ViewModel_BoardFrameActivity(object? sender, ScanFrame frame)
    {
        if (Volatile.Read(ref _statusLedHandlersAttached) == 0 ||
            sender is not TestViewModel viewModel ||
            !viewModel.IsBoardConnected ||
            viewModel.IsDeviceFault)
            return;

        Interlocked.Exchange(ref _whitePulsePending, 1);
        if (frame.Mode == BoardScanMode.Production && frame.Complete && frame.UnknownBytes == 0)
            Interlocked.Exchange(ref _yellowPulsePending, 1);

        if (Interlocked.Exchange(ref _statusPulseDispatchQueued, 1) != 0)
            return;

        _ = Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _statusPulseDispatchQueued, 0);
            if (Volatile.Read(ref _statusLedHandlersAttached) == 0 ||
                DataContext is not TestViewModel currentViewModel ||
                !currentViewModel.IsBoardConnected ||
                currentViewModel.IsDeviceFault)
                return;

            if (Interlocked.Exchange(ref _whitePulsePending, 0) != 0)
                PulseWhiteLed();
            if (Interlocked.Exchange(ref _yellowPulsePending, 0) != 0)
                PulseYellowLed();
        }, DispatcherPriority.Background);
    }

    private void ViewModel_StatusPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TestViewModel.IsBoardConnected) or
                                   nameof(TestViewModel.IsDeviceFault) or
                                   nameof(TestViewModel.State) or
                                   nameof(TestViewModel.ResultStatusText)) ||
            sender is not TestViewModel viewModel)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            if (Interlocked.Exchange(ref _statusStateDispatchQueued, 1) != 0)
                return;

            _ = Dispatcher.BeginInvoke(
                () =>
                {
                    Interlocked.Exchange(ref _statusStateDispatchQueued, 0);
                    ApplyStatusLedState(viewModel);
                },
                DispatcherPriority.DataBind);
            return;
        }

        ApplyStatusLedState(viewModel);
    }

    private void ApplyStatusLedState(TestViewModel viewModel)
    {
        if (Volatile.Read(ref _statusLedHandlersAttached) == 0 || DataContext != viewModel)
            return;

        string state = viewModel.State ?? string.Empty;
        string resultStatus = viewModel.ResultStatusText;
        bool boardConnected = viewModel.IsBoardConnected;
        if (!boardConnected || viewModel.IsDeviceFault)
        {
            CancelGreenPassBlink(false);
            SetRedLed(false);
            ResetActivityLeds();
            _lastLedBoardConnected = boardConnected;
            _lastLedState = state;
            _lastLedResultStatus = resultStatus;
            return;
        }

        if (boardConnected == _lastLedBoardConnected &&
            state.Equals(_lastLedState, StringComparison.Ordinal) &&
            resultStatus.Equals(_lastLedResultStatus, StringComparison.Ordinal))
        {
            return;
        }

        bool boardConnectionChanged = boardConnected != _lastLedBoardConnected;
        bool isNewCycle = state.Equals("CHỜ LẮP SẢN PHẨM", StringComparison.OrdinalIgnoreCase) ||
                          state.Equals("SẴN SÀNG SẢN XUẤT", StringComparison.OrdinalIgnoreCase) ||
                          state.Equals("SẴN SÀNG", StringComparison.OrdinalIgnoreCase);

        if (isNewCycle)
        {
            CancelGreenPassBlink(boardConnected);
            SetRedLed(false);
            ResetActivityLeds();
        }
        else if (!boardConnected)
        {
            CancelGreenPassBlink(false);
        }
        else if (boardConnectionChanged || _greenBlinkTask.IsCompleted)
        {
            SetGreenLed(true);
        }

        if (IsConfirmedFailLedState(viewModel, resultStatus))
            SetRedLed(true);

        if (resultStatus == "PASS" && _lastLedResultStatus != "PASS")
            _ = RestartGreenPassBlinkAsync(viewModel);

        _lastLedBoardConnected = boardConnected;
        _lastLedState = state;
        _lastLedResultStatus = resultStatus;
    }

    private static bool IsConfirmedFailLedState(TestViewModel viewModel, string resultStatus)
    {
        // LED đỏ chỉ phản ánh NG sản phẩm đã đi vào state lỗi hiện hữu.
        // Không dùng nó cho lỗi thiết bị hoặc cho chuỗi MASTER.
        if (viewModel.IsDeviceFault || viewModel.IsMasterSequenceActive)
            return false;

        if (resultStatus.Equals("FAIL", StringComparison.OrdinalIgnoreCase))
            return true;

        string state = viewModel.State ?? string.Empty;
        return state.Contains("CHẬP", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("SAI KẾT NỐI", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("ĐẤU SAI", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("ĐIỆN TRỞ KHÔNG ĐẠT", StringComparison.OrdinalIgnoreCase) ||
               state.Contains("KÍN NƯỚC KHÔNG ĐẠT", StringComparison.OrdinalIgnoreCase);
    }

    private void PulseYellowLed()
    {
        // Không restart timer theo từng frame liên tục, nếu không LED sẽ bị giữ
        // sáng đặc khi scan nhanh. Frame mới chỉ tạo pulse sau khi pulse trước đã tắt.
        if (_yellowPulseTimer.IsEnabled)
            return;

        YellowStatusLed.Fill = YellowLedOnBrush;
        _yellowPulseTimer.Start();
    }

    private void PulseWhiteLed()
    {
        // Giống Yellow: coalesce luồng frame dày thành các xung nhìn thấy được,
        // không tạo timer/task mới và không tác động timing giao tiếp.
        if (_whitePulseTimer.IsEnabled)
            return;

        WhiteStatusLed.Fill = WhiteLedOnBrush;
        _whitePulseTimer.Start();
    }

    private void YellowPulseTimer_Tick(object? sender, EventArgs e)
    {
        _yellowPulseTimer.Stop();
        YellowStatusLed.Fill = YellowLedOffBrush;
    }

    private void WhitePulseTimer_Tick(object? sender, EventArgs e)
    {
        _whitePulseTimer.Stop();
        WhiteStatusLed.Fill = WhiteLedOffBrush;
    }

    private void ResetActivityLeds()
    {
        Interlocked.Exchange(ref _yellowPulsePending, 0);
        Interlocked.Exchange(ref _whitePulsePending, 0);
        _yellowPulseTimer.Stop();
        _whitePulseTimer.Stop();
        YellowStatusLed.Fill = YellowLedOffBrush;
        WhiteStatusLed.Fill = WhiteLedOffBrush;
    }

    private void SetGreenLed(bool isOn) =>
        GreenStatusLed.Fill = isOn ? GreenLedOnBrush : GreenLedOffBrush;

    private void SetRedLed(bool isOn) =>
        RedStatusLed.Fill = isOn ? RedLedOnBrush : RedLedOffBrush;

    private async Task RestartGreenPassBlinkAsync(TestViewModel viewModel)
    {
        int request = Interlocked.Increment(ref _greenBlinkRequestGeneration);

        // Tách CTS cũ khỏi field trước khi Cancel để field không giữ tham chiếu
        // tới CancellationTokenSource đã Dispose khi blink cũ kết thúc.
        CancellationTokenSource? previousCts = Interlocked.Exchange(ref _greenBlinkCts, null);
        if (previousCts is not null)
        {
            try { previousCts.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        Task previousBlink = _greenBlinkTask;
        try
        {
            await previousBlink;
        }
        catch (OperationCanceledException)
        {
        }

        if (request != Volatile.Read(ref _greenBlinkRequestGeneration) ||
            Volatile.Read(ref _statusLedHandlersAttached) == 0 ||
            DataContext != viewModel ||
            !viewModel.IsBoardConnected ||
            viewModel.IsDeviceFault)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _greenBlinkCts, cts);
        Task blinkTask = RunGreenPassBlinkAsync(viewModel, cts.Token);
        _greenBlinkTask = blinkTask;

        try
        {
            await blinkTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Interlocked.CompareExchange(ref _greenBlinkCts, null, cts);

            if (request == Volatile.Read(ref _greenBlinkRequestGeneration) &&
                Volatile.Read(ref _statusLedHandlersAttached) != 0 &&
                DataContext == viewModel &&
                !viewModel.IsDeviceFault)
            {
                SetGreenLed(viewModel.IsBoardConnected);
            }

            cts.Dispose();
        }
    }

    private async Task RunGreenPassBlinkAsync(TestViewModel viewModel, CancellationToken token)
    {
        for (int blink = 0; blink < 3; blink++)
        {
            token.ThrowIfCancellationRequested();
            if (!viewModel.IsBoardConnected || viewModel.IsDeviceFault)
                return;

            SetGreenLed(false);
            await Task.Delay(TimeSpan.FromMilliseconds(120), token);
            if (!viewModel.IsBoardConnected || viewModel.IsDeviceFault)
                return;

            SetGreenLed(true);
            await Task.Delay(TimeSpan.FromMilliseconds(120), token);
        }
    }

    private void CancelGreenPassBlink(bool restoreConnectedState)
    {
        Interlocked.Increment(ref _greenBlinkRequestGeneration);
        CancellationTokenSource? cts = Interlocked.Exchange(ref _greenBlinkCts, null);
        if (cts is not null)
        {
            try { cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        SetGreenLed(restoreConnectedState);
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

    private async void ResetProbeCounter_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TestViewModel viewModel)
            return;

        var dialog = new ProbeMaintenanceResetWindow(
            viewModel.PartNumber,
            viewModel.ModelName,
            viewModel.ProbeCycleCount,
            viewModel.ProbeReplacementThreshold)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        (bool reset, string message) = await viewModel.TryResetProbeCycleAsync(dialog.AdminPassword);
        MessageBox.Show(
            this,
            message,
            reset ? "Đã reset counter Pin" : "Không thể reset counter Pin",
            MessageBoxButton.OK,
            reset ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
        ContentRendered -= TestWindow_ContentRendered;
        _clockTimer.Stop();
        _clockTimer.Tick -= ClockTimer_Tick;
        _yellowPulseTimer.Stop();
        _yellowPulseTimer.Tick -= YellowPulseTimer_Tick;
        _whitePulseTimer.Stop();
        _whitePulseTimer.Tick -= WhitePulseTimer_Tick;
        _scrollCts?.Cancel();
        _scrollCts?.Dispose();
        _scrollCts = null;
        Interlocked.Exchange(ref _statusLedHandlersAttached, 0);
        CancelGreenPassBlink(false);
        if (DataContext is TestViewModel vm)
        {
            vm.BoardFrameActivity -= ViewModel_BoardFrameActivity;
            vm.PropertyChanged -= ViewModel_StatusPropertyChanged;
            if (_faultsChangedHandler is not null)
                vm.Faults.CollectionChanged -= _faultsChangedHandler;
        }
        _faultsChangedHandler = null;
        DataContext = null;
    }
}
