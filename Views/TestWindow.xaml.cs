using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _yellowPulseTimer;
    private readonly DispatcherTimer _whitePulseTimer;
    private readonly DispatcherTimer _greenPulseTimer;
    private NotifyCollectionChangedEventHandler? _faultsChangedHandler;
    private readonly CancellationTokenSource _viewLifetimeCts = new();
    private int _scrollDispatchQueued;
    private readonly object _statusFrameGate = new();
    private readonly StatusLedFrameTracker _statusFrameTracker = new();

    public event EventHandler? ReturningToMain;
    private int _statusLedHandlersAttached;
    private int _statusPulseDispatchQueued;
    private int _statusStateDispatchQueued;
    private int _yellowPulsePending;
    private int _whitePulsePending;
    private int _greenPulsePending;
    private bool _passLedsHeld;
    private bool _wiringFaultLedsHeld;
    private WaterProofTestWindow? _waterProofWindow;

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
        bool autoStartProduction = true)
    {
        InitializeComponent();
        Title = $"UniversalTester {AppVersion.DisplayVersion} - Màn hình kiểm tra";
        TestAppVersionText.Text = AppVersion.DisplayVersion;
        DataContext = viewModel;
        _autoStartProduction = autoStartProduction;

        if (!_autoStartProduction && viewModel.IsBoardConnected)
            viewModel.State = "CẤU HÌNH CARD KHÔNG ĐỦ";

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += ClockTimer_Tick;

        _yellowPulseTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _yellowPulseTimer.Tick += YellowPulseTimer_Tick;

        _whitePulseTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _whitePulseTimer.Tick += WhitePulseTimer_Tick;

        _greenPulseTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _greenPulseTimer.Tick += GreenPulseTimer_Tick;

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

    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        UpdateClock();
        if (DataContext is TestViewModel viewModel)
            viewModel.RefreshDailyMasterRequirement();
    }

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

        // Keep the test grid virtualized even when the model exposes hundreds
        // of rows. Recycling avoids a full visual-tree rebuild when the VM
        // applies a Reset/delta after one Production frame.
        FaultGrid.EnableRowVirtualization = true;
        FaultGrid.EnableColumnVirtualization = true;
        System.Windows.Controls.VirtualizingPanel.SetIsVirtualizing(FaultGrid, true);
        System.Windows.Controls.VirtualizingPanel.SetVirtualizationMode(
            FaultGrid,
            System.Windows.Controls.VirtualizationMode.Recycling);
        System.Windows.Controls.ScrollViewer.SetCanContentScroll(FaultGrid, true);

        _faultsChangedHandler = (_, args) =>
        {
            if (ShouldAutoScrollToFirstFault(args, viewModel))
                ScheduleScrollToFirstFault(viewModel);
        };
        viewModel.Faults.CollectionChanged += _faultsChangedHandler;

        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (_autoStartProduction)
                await viewModel.StartProductionTestAsync(_viewLifetimeCts.Token);
        }
        catch (Exception ex)
        {
            if (viewModel.IsDeviceFault)
                return;

            AsyncFileLogService.Current.Error($"TestWindow initialization failed: {ex}");
            MessageBox.Show(this,
                "Chưa thể bắt đầu kiểm tra. Vui lòng quay về trang chính và thử lại.",
                "CHƯA BẮT ĐẦU KIỂM TRA", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AttachStatusLedHandlers(TestViewModel viewModel)
    {
        if (Interlocked.Exchange(ref _statusLedHandlersAttached, 1) != 0)
            return;

        viewModel.BoardFrameActivity += ViewModel_BoardFrameActivity;
        viewModel.PropertyChanged += ViewModel_StatusPropertyChanged;
        viewModel.WaterProofWindowOpenRequested += ViewModel_WaterProofWindowOpenRequested;
        viewModel.WaterProofWindowCloseRequested += ViewModel_WaterProofWindowCloseRequested;
        ResetActivityLeds();
        ApplyStatusLedState(viewModel);
    }

    private void ViewModel_WaterProofWindowOpenRequested(object? sender, WaterProofTestViewModel viewModel)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => ViewModel_WaterProofWindowOpenRequested(sender, viewModel));
            return;
        }

        if (!IsLoaded || !viewModel.IsRunning ||
            Volatile.Read(ref _statusLedHandlersAttached) == 0)
            return;
        if (ReferenceEquals(_waterProofWindow?.DataContext, viewModel))
            return;
        CloseWaterProofWindow();
        _waterProofWindow = new WaterProofTestWindow(viewModel) { Owner = this };
        PositionWaterProofWindow();
        LocationChanged += TestWindow_LocationChanged;
        SizeChanged += TestWindow_LocationChanged;
        _waterProofWindow.Show();
    }

    private void ViewModel_WaterProofWindowCloseRequested(object? sender, string reason)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(CloseWaterProofWindow);
            return;
        }
        CloseWaterProofWindow();
    }

    private void TestWindow_LocationChanged(object? sender, EventArgs e) => PositionWaterProofWindow();

    private void PositionWaterProofWindow()
    {
        if (_waterProofWindow is null || ResultStatusHost is null || !IsLoaded)
            return;

        // Leak chỉ là ba ô số nhỏ. Neo trực tiếp dưới ô trạng thái chính
        // LẮP SẢN PHẨM / ĐANG KIỂM TRA / PASS.
        Point screenAnchor = ResultStatusHost.PointToScreen(
            new Point(0, ResultStatusHost.ActualHeight));
        Point screenRight = ResultStatusHost.PointToScreen(
            new Point(ResultStatusHost.ActualWidth, ResultStatusHost.ActualHeight));
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? System.Windows.Media.Matrix.Identity;
        Point anchor = transform.Transform(screenAnchor);
        Point right = transform.Transform(screenRight);

        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)screenAnchor.X, (int)screenAnchor.Y));
        Point workTopLeft = transform.Transform(
            new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        Point workBottomRight = transform.Transform(
            new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));

        const double gap = 4;
        double requestedLeft =
            (anchor.X + right.X - _waterProofWindow.Width) / 2;
        double requestedTop = anchor.Y + gap;
        double maxLeft = Math.Max(workTopLeft.X, workBottomRight.X - _waterProofWindow.Width);
        double maxTop = Math.Max(workTopLeft.Y, workBottomRight.Y - _waterProofWindow.Height);

        _waterProofWindow.Left = Math.Clamp(requestedLeft, workTopLeft.X, maxLeft);
        _waterProofWindow.Top = Math.Clamp(requestedTop, workTopLeft.Y, maxTop);
    }

    private void CloseWaterProofWindow()
    {
        LocationChanged -= TestWindow_LocationChanged;
        SizeChanged -= TestWindow_LocationChanged;
        WaterProofTestWindow? window = _waterProofWindow;
        _waterProofWindow = null;
        window?.CloseOnce();
    }

    private void ViewModel_BoardFrameActivity(object? sender, ScanFrame frame)
    {
        if (Volatile.Read(ref _statusLedHandlersAttached) == 0 ||
            sender is not TestViewModel viewModel ||
            !viewModel.IsBoardConnected ||
            viewModel.IsDeviceFault)
            return;

        if (!StatusLedFrameTracker.IsCompleteScan(frame))
            return;

        lock (_statusFrameGate)
        {
            bool contactChanged = _statusFrameTracker.Observe(frame);
            if (contactChanged)
                Interlocked.Exchange(ref _whitePulsePending, 1);
            Interlocked.Exchange(ref _greenPulsePending, 1);
            if (contactChanged && frame.Mode == BoardScanMode.Production)
                Interlocked.Exchange(ref _yellowPulsePending, 1);
        }

        if (Interlocked.Exchange(ref _statusPulseDispatchQueued, 1) != 0)
            return;

        _ = Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _statusPulseDispatchQueued, 0);
            if (Volatile.Read(ref _statusLedHandlersAttached) == 0 ||
                DataContext is not TestViewModel currentViewModel ||
                !ReferenceEquals(currentViewModel, viewModel) ||
                !currentViewModel.IsBoardConnected ||
                currentViewModel.IsDeviceFault)
                return;

            ApplyStatusLedState(currentViewModel);
            if (Interlocked.Exchange(ref _whitePulsePending, 0) != 0)
                PulseWhiteLed();
            if (Interlocked.Exchange(ref _yellowPulsePending, 0) != 0)
                PulseYellowLed();
            if (Interlocked.Exchange(ref _greenPulsePending, 0) != 0)
                PulseGreenLed();
        }, DispatcherPriority.Background);
    }

    private void ViewModel_StatusPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TestViewModel.IsBoardConnected) or
                                   nameof(TestViewModel.IsDeviceFault) or
                                   nameof(TestViewModel.State) or
                                   nameof(TestViewModel.ResultStatusText) or
                                   nameof(TestViewModel.WiringFaultCount) or
                                   nameof(TestViewModel.IsPassStatusLedHeld)) ||
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

        if (!viewModel.IsBoardConnected || viewModel.IsDeviceFault)
        {
            ResetActivityLeds();
            SetRedLed(false);
            return;
        }

        bool passWasHeld = _passLedsHeld;
        bool previouslyHeld = _passLedsHeld || _wiringFaultLedsHeld;
        _passLedsHeld = viewModel.IsPassStatusLedHeld;
        // Missing connections are installation instructions, not wrong/short
        // wiring. Never turn red on merely because installation is incomplete.
        _wiringFaultLedsHeld = !_passLedsHeld && viewModel.WiringFaultCount > 0;
        SetRedLed(_wiringFaultLedsHeld);

        if (_passLedsHeld || _wiringFaultLedsHeld || previouslyHeld)
        {
            _greenPulseTimer.Stop();
            _yellowPulseTimer.Stop();
            SetGreenLed(_passLedsHeld || _wiringFaultLedsHeld);
            YellowStatusLed.Fill = _passLedsHeld ? YellowLedOnBrush : YellowLedOffBrush;
        }
        if (_passLedsHeld || passWasHeld)
        {
            _whitePulseTimer.Stop();
            WhiteStatusLed.Fill = _passLedsHeld ? WhiteLedOnBrush : WhiteLedOffBrush;
            // Discard contact pulses queued during PASS. Normal activity resumes
            // from the next scan; a sticky PASS label does not hold these lamps.
            Interlocked.Exchange(ref _whitePulsePending, 0);
            Interlocked.Exchange(ref _yellowPulsePending, 0);
        }
    }

    private void PulseYellowLed()
    {
        if (_passLedsHeld || _wiringFaultLedsHeld)
            return;
        // Original CLed::Pulse restarts the timeout on every new activity.
        _yellowPulseTimer.Stop();
        YellowStatusLed.Fill = YellowLedOnBrush;
        _yellowPulseTimer.Start();
    }

    private void PulseWhiteLed()
    {
        if (_passLedsHeld)
            return;
        _whitePulseTimer.Stop();
        WhiteStatusLed.Fill = WhiteLedOnBrush;
        _whitePulseTimer.Start();
    }

    private void PulseGreenLed()
    {
        if (_passLedsHeld || _wiringFaultLedsHeld)
            return;
        _greenPulseTimer.Stop();
        SetGreenLed(true);
        _greenPulseTimer.Start();
    }

    private void YellowPulseTimer_Tick(object? sender, EventArgs e)
    {
        _yellowPulseTimer.Stop();
        YellowStatusLed.Fill = _passLedsHeld ? YellowLedOnBrush : YellowLedOffBrush;
    }

    private void WhitePulseTimer_Tick(object? sender, EventArgs e)
    {
        _whitePulseTimer.Stop();
        WhiteStatusLed.Fill = _passLedsHeld ? WhiteLedOnBrush : WhiteLedOffBrush;
    }

    private void GreenPulseTimer_Tick(object? sender, EventArgs e)
    {
        _greenPulseTimer.Stop();
        SetGreenLed(_passLedsHeld || _wiringFaultLedsHeld);
    }

    private void ResetActivityLeds()
    {
        _passLedsHeld = false;
        _wiringFaultLedsHeld = false;
        lock (_statusFrameGate)
        {
            _statusFrameTracker.Reset();
            Interlocked.Exchange(ref _yellowPulsePending, 0);
            Interlocked.Exchange(ref _whitePulsePending, 0);
            Interlocked.Exchange(ref _greenPulsePending, 0);
        }
        _greenPulseTimer.Stop();
        SetGreenLed(false);
        _yellowPulseTimer.Stop();
        _whitePulseTimer.Stop();
        YellowStatusLed.Fill = YellowLedOffBrush;
        WhiteStatusLed.Fill = WhiteLedOffBrush;
    }

    private void SetGreenLed(bool isOn) =>
        GreenStatusLed.Fill = isOn ? GreenLedOnBrush : GreenLedOffBrush;

    private void SetRedLed(bool isOn) =>
        RedStatusLed.Fill = isOn ? RedLedOnBrush : RedLedOffBrush;

    private void ScheduleScrollToFirstFault(TestViewModel viewModel)
    {
        // Một delta có thể phát vài CollectionChanged liên tiếp. Chỉ giữ một
        // callback scroll pending để không tạo/hủy CTS và Dispatcher operation
        // cho từng row trên model 10 card.
        if (Interlocked.Exchange(ref _scrollDispatchQueued, 1) != 0)
            return;

        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (_closeInProgress || viewModel.Faults.Count == 0)
                    return;

                FaultRow firstFault = viewModel.Faults[0];
                if (IsFaultRowVisible(firstFault))
                    return;

                int delay = viewModel.ScrollDelay;
                if (delay > 0)
                    await Task.Delay(delay);
                if (_closeInProgress || viewModel.Faults.Count == 0)
                    return;

                firstFault = viewModel.Faults[0];
                if (!IsFaultRowVisible(firstFault))
                    FaultGrid.ScrollIntoView(firstFault);
            }
            finally
            {
                Interlocked.Exchange(ref _scrollDispatchQueued, 0);
            }
        }, DispatcherPriority.Background);
    }

    private static bool ShouldAutoScrollToFirstFault(
        NotifyCollectionChangedEventArgs args,
        TestViewModel viewModel)
    {
        // Normal continuity rows can be added/removed rapidly while the operator
        // is installing the product. Scrolling on those deltas forces DataGrid
        // measure/layout and steals time from the real test presentation. Only an
        // actual product fault is allowed to take scroll ownership.
        if (viewModel.WiringFaultCount <= 0)
            return false;

        if (args.Action == NotifyCollectionChangedAction.Reset)
            return true;

        if (args.Action is not (NotifyCollectionChangedAction.Add or
            NotifyCollectionChangedAction.Replace))
        {
            return false;
        }

        return args.NewItems?.OfType<FaultRow>().Any(row =>
            row.ProductFaultType is ProductFaultType.WrongWiring or
                ProductFaultType.ShortCircuit) == true;
    }

    private bool IsFaultRowVisible(FaultRow row)
    {
        if (FaultGrid.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement container ||
            !container.IsVisible ||
            container.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            Point topLeft = container.TranslatePoint(new Point(0, 0), FaultGrid);
            double bottom = topLeft.Y + container.ActualHeight;
            return bottom > FaultGrid.ColumnHeaderHeight && topLeft.Y < FaultGrid.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            // Container có thể vừa bị recycle giữa hai lần cập nhật collection.
            return false;
        }
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
            if (DataContext is TestViewModel viewModel &&
                !await viewModel.StopViewAsync())
            {
                ShowProductRemovalRequired();
                _closeInProgress = false;
                return;
            }

            CancelPendingAutoStart();
            await RevealMainBeforeCloseAsync();
            _allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Return to Main failed: {ex}");
            _closeInProgress = false;
        }
    }

    private async void TestWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            CleanupUiHandlers();
            return;
        }

        if (DataContext is TestViewModel deviceFaultViewModel && deviceFaultViewModel.IsDeviceFault)
        {
            _allowClose = true;
            CleanupUiHandlers();
            return;
        }

        e.Cancel = true;
        if (_closeInProgress)
            return;

        if (DataContext is TestViewModel productViewModel &&
            productViewModel.HasProductOnTestTable)
        {
            ShowProductRemovalRequired();
            return;
        }

        MessageBoxResult result = MessageBox.Show(this,
            "Bạn có muốn dừng kiểm tra và quay về màn hình chọn mã?",
            "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _closeInProgress = true;
        try
        {
            if (DataContext is TestViewModel viewModel &&
                !await viewModel.StopViewAsync())
            {
                ShowProductRemovalRequired();
                _closeInProgress = false;
                return;
            }

            CancelPendingAutoStart();
            await RevealMainBeforeCloseAsync();
            _allowClose = true;
            CleanupUiHandlers();
            Close();
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Close TestWindow failed: {ex}");
            _closeInProgress = false;
        }
    }

    private void ShowProductRemovalRequired()
    {
        MessageBox.Show(
            this,
            "Vui lòng tháo sản phẩm ra khỏi bàn test !!",
            "CHƯA THÁO SẢN PHẨM",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private Task RevealMainBeforeCloseAsync()
    {
        EventHandler? returningHandler = ReturningToMain;
        if (returningHandler is null ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return Task.CompletedTask;
        }

        // MainWindow luôn còn hiển thị và đã được DWM compose phía sau cửa sổ
        // test. Chỉ cần cập nhật/activate trước Close(); không chờ các nhịp
        // CompositionTarget.Rendering toàn cục vì chúng không chứng minh frame
        // của MainWindow và còn tạo cảm giác chuyển trang chậm.
        returningHandler(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void CancelPendingAutoStart()
    {
        if (_viewLifetimeCts.IsCancellationRequested)
            return;

        try { _viewLifetimeCts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void CleanupUiHandlers()
    {
        ReturningToMain = null;
        CancelPendingAutoStart();
        ContentRendered -= TestWindow_ContentRendered;
        _clockTimer.Stop();
        _clockTimer.Tick -= ClockTimer_Tick;
        _yellowPulseTimer.Stop();
        _yellowPulseTimer.Tick -= YellowPulseTimer_Tick;
        _whitePulseTimer.Stop();
        _whitePulseTimer.Tick -= WhitePulseTimer_Tick;
        _greenPulseTimer.Stop();
        _greenPulseTimer.Tick -= GreenPulseTimer_Tick;
        Interlocked.Exchange(ref _scrollDispatchQueued, 0);
        Interlocked.Exchange(ref _statusLedHandlersAttached, 0);
        ResetActivityLeds();
        if (DataContext is TestViewModel vm)
        {
            vm.BoardFrameActivity -= ViewModel_BoardFrameActivity;
            vm.PropertyChanged -= ViewModel_StatusPropertyChanged;
            vm.WaterProofWindowOpenRequested -= ViewModel_WaterProofWindowOpenRequested;
            vm.WaterProofWindowCloseRequested -= ViewModel_WaterProofWindowCloseRequested;
            if (_faultsChangedHandler is not null)
                vm.Faults.CollectionChanged -= _faultsChangedHandler;
        }
        _faultsChangedHandler = null;
        CloseWaterProofWindow();
        DataContext = null;
    }
}
