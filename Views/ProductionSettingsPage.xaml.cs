using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.Views;

/// <summary>
/// V12.9: trang Cài đặt nhúng trực tiếp trong MainWindow. Không tạo Window,
/// không xuất hiện thêm mục Alt+Tab và không mở một shell ứng dụng thứ hai.
/// </summary>
public partial class ProductionSettingsPage : UserControl
{
    private readonly MainViewModel? _main;
    private readonly ProductionSettingsViewModel _vm;
    private int _released;
    private int _portRefreshGeneration;
    private int _printerConnectionGeneration;
    private int _saveInProgress;
    private bool _printerPortSelectionInitialized;
    private bool _suppressPrinterPortSelection;

    public event Func<object?, EventArgs, Task>? SettingsSaved;
    public event EventHandler? RequestClose;

    public ProductionSettingsPage()
        : this(null)
    {
    }

    public ProductionSettingsPage(MainViewModel? main)
        : this(main, new ProductionSettingsViewModel(main?.Test))
    {
    }

    public ProductionSettingsPage(
        MainViewModel? main,
        ProductionSettingsViewModel viewModel)
    {
        _main = main;
        _vm = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _vm;
        InitializeComboBoxItems();
        ApplyLabelTemplatePhysicalSize(_vm.Settings.Label.TemplateType);
        Loaded += ProductionSettingsPage_Loaded;
    }

    private async void ProductionSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ProductionSettingsPage_Loaded;
        await System.Windows.Threading.Dispatcher.Yield(
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        if (IsReleased)
            return;

        UpdatePanelWidths(ActualWidth, ActualHeight);

        await RefreshPortsAsync();
        if (IsReleased)
            return;
        _printerPortSelectionInitialized = true;
    }

    public void ReleasePageResources()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;

        Interlocked.Increment(ref _portRefreshGeneration);
        Interlocked.Increment(ref _printerConnectionGeneration);
        Loaded -= ProductionSettingsPage_Loaded;
        DataContext = null;
        SettingsSaved = null;
        RequestClose = null;
    }

    private bool IsReleased => Volatile.Read(ref _released) != 0;

    private Window? HostWindow => Window.GetWindow(this) ?? Application.Current?.MainWindow;

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Dùng chính kích thước thật của UserControl. Không lấy ViewportWidth ở đây:
        // trong lúc WPF đang layout, ViewportWidth có thể vẫn là giá trị của frame trước
        // và làm toàn bộ 4 panel bị giữ hẹp ở bên trái.
        UpdatePanelWidths(e.NewSize.Width, e.NewSize.Height);
    }

    private void SettingsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.ViewportWidthChange) <= 0.1 && Math.Abs(e.ViewportHeightChange) <= 0.1)
            return;

        // Sau khi scrollbar xuất hiện/biến mất, tính lại theo kích thước thật của trang
        // để 4 vùng tiếp tục dùng hết chiều ngang khả dụng.
        double pageWidth = ActualWidth;
        double pageHeight = ActualHeight;
        if (!double.IsFinite(pageWidth) || pageWidth <= 0)
            pageWidth = SettingsScrollViewer?.ActualWidth ?? e.ViewportWidth;
        if (!double.IsFinite(pageHeight) || pageHeight <= 0)
            pageHeight = SettingsScrollViewer?.ActualHeight ?? e.ViewportHeight;

        UpdatePanelWidths(pageWidth, pageHeight);
    }

    private void UpdatePanelWidths(double pageWidth, double pageHeight)
    {
        // Bốn vùng chính luôn là 4 cột * bằng nhau và chiếm hết chiều rộng trang.
        // Chỉ khi cửa sổ nhỏ hơn mức tối thiểu để các nhãn/nút không bị cắt thì
        // host mới rộng hơn viewport và ScrollViewer mới cho phép cuộn ngang.
        if (UnifiedSettingsGrid is null ||
            SettingsPanelsHost is null ||
            SettingsScrollViewer is null)
        {
            return;
        }

        const double minimumUsableContentWidth = 1120d;
        const double hostHorizontalMargin = 16d; // SettingsPanelsHost Margin="8,6,8,6"

        if (!double.IsFinite(pageWidth) || pageWidth <= 0)
            return;

        double verticalScrollBarWidth =
            SettingsScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible
                ? SystemParameters.VerticalScrollBarWidth
                : 0d;

        double availableWidth = Math.Max(320d, pageWidth - hostHorizontalMargin - verticalScrollBarWidth);
        double targetWidth = Math.Max(minimumUsableContentWidth, availableWidth);

        SettingsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        SettingsScrollViewer.HorizontalScrollBarVisibility =
            availableWidth + 0.5 < minimumUsableContentWidth
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;

        // Width được cập nhật từ toàn bộ UserControl nên không còn trường hợp cụm 4 card
        // đứng bên trái và để trống một mảng lớn bên phải.
        if (!double.IsFinite(SettingsPanelsHost.Width) ||
            Math.Abs(SettingsPanelsHost.Width - targetWidth) > 0.5)
        {
            SettingsPanelsHost.Width = targetWidth;
        }
        SettingsPanelsHost.MinWidth = 0;
        SettingsPanelsHost.MaxWidth = double.PositiveInfinity;
        SettingsPanelsHost.HorizontalAlignment = HorizontalAlignment.Left;

        // Grid có 4 ColumnDefinition Width="*" => mỗi vùng chính luôn đúng 25%.
        UnifiedSettingsGrid.Width = double.NaN;
        UnifiedSettingsGrid.MinWidth = 0;
        UnifiedSettingsGrid.MaxWidth = double.PositiveInfinity;
        UnifiedSettingsGrid.HorizontalAlignment = HorizontalAlignment.Stretch;

        if (IoSettingsPanel is not null)
            IoSettingsPanel.Width = double.NaN;
        if (RelaySettingsPanel is not null)
            RelaySettingsPanel.Width = double.NaN;
        if (WaterProofSettingsPanel is not null)
            WaterProofSettingsPanel.Width = double.NaN;
        if (LabelSettingsPanel is not null)
            LabelSettingsPanel.Width = double.NaN;
        if (ResistanceSettingsPanel is not null)
            ResistanceSettingsPanel.Width = double.NaN;
    }

    private void LabelTemplateTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string templateType =
            (LabelTemplateTypeComboBox.SelectedValue as string) ??
            _vm.Settings.Label.TemplateType;
        ApplyLabelTemplatePhysicalSize(templateType);

        // Khi người vận hành đổi loại tem, làm mới ngay vùng LỆNH IN TEM nếu
        // trang đã load và có THT hợp lệ. Không hiện MessageBox ở thao tác đổi
        // ComboBox để tránh làm gián đoạn cấu hình.
        if (IsLoaded && !IsReleased && InlineLabelCommandTextBox is not null)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(TryRefreshInlineLabelCommandPreview));
        }
    }

    private void TryRefreshInlineLabelCommandPreview()
    {
        if (IsReleased || !IsLoaded || InlineLabelCommandTextBox is null)
            return;

        string thtPath = _vm.Settings.LastThtPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(thtPath) || !File.Exists(thtPath))
            return;

        try
        {
            LabelPrintRequest request = BuildSettingsLabelRequest("PREVIEW");
            RenderInlineLabelPreview(request, "XEM TRƯỚC");
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Automatic label command preview refresh failed: {ex}");
        }
    }

    private void ApplyLabelTemplatePhysicalSize(string? templateType)
    {
        string normalized = LabelProfileResolver.NormalizeTemplateType(templateType);
        (int widthMm, int heightMm) = normalized switch
        {
            LabelSettings.SmallTemplate => (60, 25),
            LabelSettings.SmallQrTemplate => (60, 15),
            _ => (90, 15)
        };

        _vm.Settings.Label.TemplateType = normalized;
        _vm.Settings.Label.WidthMm = widthMm;
        _vm.Settings.Label.HeightMm = heightMm;
    }

    private void InitializeComboBoxItems()
    {
        CardIoComboBox.ItemsSource = Enumerable
            .Range(1, BoardCapacity.MaxExpansionCardCount)
            .Select(n => new CardIoOption(
                n,
                n.ToString()))
            .ToArray();
        StartCardComboBox.ItemsSource = Enumerable
            .Range(1, BoardCapacity.MaxExpansionCardCount)
            .ToArray();

        if (_vm.Settings.ExpansionCardCount <= 0)
        {
            _vm.Settings.ExpansionCardCount =
                BoardIoDecoder.ExpansionCardCountFromScanCards(_vm.Settings.CardCount);
        }

        _vm.Settings.ExpansionCardCount = Math.Clamp(
            _vm.Settings.ExpansionCardCount,
            1,
            BoardCapacity.MaxExpansionCardCount);
        _vm.Settings.StartCardNumber = Math.Clamp(
            _vm.Settings.StartCardNumber,
            1,
            BoardCapacity.MaxExpansionCardCount);

        BoardCapacity capacity = BoardCapacity.FromSettings(_vm.Settings);
        _vm.Settings.CardCount = capacity.ScanCardCount;
        RefreshTotalIoCapacity();

        IoConfirm1ComboBox.ItemsSource = Enumerable.Range(0, 128).ToArray();
        IoConfirmNComboBox.ItemsSource = Enumerable.Range(0, 32).ToArray();
        RelayWiringModeComboBox.ItemsSource = new[]
        {
            new RelayWiringOption(0, "R2 MARK → R1 JIG • FAIL R1"),
            new RelayWiringOption(1, "R1 MARK → R2 JIG • FAIL R2")
        };
    }

    private void CardIoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshTotalIoCapacity();

    private void RefreshTotalIoCapacity()
    {
        if (TotalIoCapacityText is null)
            return;

        int count = CardIoComboBox?.SelectedValue is int selected
            ? selected
            : _vm.Settings.ExpansionCardCount;
        count = Math.Clamp(count, 1, BoardCapacity.MaxExpansionCardCount);
        int start = StartCardComboBox?.SelectedItem is int selectedStart
            ? selectedStart
            : _vm.Settings.StartCardNumber;
        start = Math.Clamp(start, 1, BoardCapacity.MaxExpansionCardCount);
        int end = start + count - 1;
        TotalIoCapacityText.Text = end <= BoardCapacity.MaxExpansionCardCount
            ? $"{count * BoardCapacity.IoPerExpansionCard} IO • card {start}-{end}"
            : $"VƯỢT GIỚI HẠN CARD {BoardCapacity.MaxExpansionCardCount}";
    }

    private async void RefreshPrinterPorts_Click(object sender, RoutedEventArgs e) =>
        await RefreshPortsAsync();

    private async void PrinterComComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_printerPortSelectionInitialized || _suppressPrinterPortSelection || IsReleased)
            return;

        int generation = Interlocked.Increment(ref _printerConnectionGeneration);
        try
        {
            PrinterComComboBox
                .GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)
                ?.UpdateSource();

            string portName = _vm.Settings.Label.PrinterCom?.Trim() ?? string.Empty;
            // Kết nối thử là side effect tức thời; file cấu hình chỉ được
            // ghi qua pipeline chung khi người dùng rời trang Cài đặt.

            if (_main is null)
                return;

            if (string.IsNullOrWhiteSpace(portName))
            {
                await _main.Test.DisconnectLabelPrinterAsync();
                return;
            }

            LabelPrinterConnectionResult result =
                await _main.Test.ConnectLabelPrinterAsync(_vm.Settings.Label);
            if (!result.Connected &&
                !IsReleased &&
                generation == Volatile.Read(ref _printerConnectionGeneration))
            {
                AsyncFileLogService.Current.Error(
                    $"Automatic label printer selection failed on {portName}: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Automatic label printer selection failed: {ex}");
        }
    }

    private void EditLabelTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string configured = _vm.Settings.Label.TemplatePath?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string path = ResolveConfiguredLabelTemplatePath();
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            EditBuiltInLabelTemplate();
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Open label template editor failed: {ex}");
            ShowMessage("Chưa mở được mẫu tem. Vui lòng thử lại.", "CHỈNH TEM", MessageBoxImage.Warning);
        }
    }

    private void EditBuiltInLabelTemplate()
    {
        string templateType = LabelProfileResolver.NormalizeTemplateType(_vm.Settings.Label.TemplateType);
        string reference = BuiltInLabelTemplateStore.ReferenceFor(templateType);
        string defaultTemplate = BuiltInLabelTemplateStore.Load(reference);
        string savedOverride = BuiltInLabelTemplateStore.LoadOverride(_vm.Settings.Label, templateType);

        var editor = new Window
        {
            Title = $"CHỈNH TEM {templateType}",
            Owner = HostWindow,
            Width = 900,
            Height = 680,
            MinWidth = 640,
            MinHeight = 420,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var layout = new Grid { Margin = new Thickness(12) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var note = new TextBlock
        {
            Text = "Nội dung sửa sẽ được lưu cùng Cài đặt. Dùng KHÔI PHỤC MẶC ĐỊNH để bỏ bản tùy chỉnh.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(note, 0);
        layout.Children.Add(note);

        var textBox = new TextBox
        {
            Text = string.IsNullOrEmpty(savedOverride) ? defaultTemplate : savedOverride,
            AcceptsReturn = true,
            AcceptsTab = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 14
        };
        Grid.SetRow(textBox, 1);
        layout.Children.Add(textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var resetButton = new Button { Content = "KHÔI PHỤC MẶC ĐỊNH", MinWidth = 170, Margin = new Thickness(4) };
        var cancelButton = new Button { Content = "HỦY", MinWidth = 90, Margin = new Thickness(4), IsCancel = true };
        var saveButton = new Button { Content = "ÁP DỤNG", MinWidth = 110, Margin = new Thickness(4), IsDefault = true };
        resetButton.Click += (_, _) => textBox.Text = defaultTemplate;
        cancelButton.Click += (_, _) => editor.DialogResult = false;
        saveButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                MessageBox.Show(editor, "Mẫu tem không được để trống.", "CHỈNH TEM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            editor.DialogResult = true;
        };
        buttons.Children.Add(resetButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        Grid.SetRow(buttons, 2);
        layout.Children.Add(buttons);
        editor.Content = layout;

        if (editor.ShowDialog() != true)
            return;

        if (string.Equals(textBox.Text, defaultTemplate, StringComparison.Ordinal))
            BuiltInLabelTemplateStore.ClearOverride(_vm.Settings.Label, templateType);
        else
            BuiltInLabelTemplateStore.SaveOverride(_vm.Settings.Label, templateType, textBox.Text);

        ShowMessage(
            "Đã áp dụng mẫu tem. Bấm LƯU CÀI ĐẶT để hoàn tất.",
            "CHỈNH TEM",
            MessageBoxImage.Information);
    }

    private void PreviewLabel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Preview và Print cùng đi qua LabelPrintRequest.Capture(). Vì vậy dữ liệu,
            // template và payload ở đây chính là payload mà pipeline in thật sử dụng.
            LabelPrintRequest request = BuildSettingsLabelRequest("PREVIEW");
            RenderInlineLabelPreview(request, "XEM TRƯỚC");
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Label preview failed: {ex}");
            SetInlineLabelPreviewStatus("KHÔNG TẠO ĐƯỢC PAYLOAD", isError: true);
            ShowMessage(
                "Chưa tạo được lệnh in tem. Vui lòng kiểm tra mẫu tem/THT.",
                "XEM TRƯỚC TEM",
                MessageBoxImage.Warning);
        }
    }

    private void RenderInlineLabelPreview(LabelPrintRequest request, string status)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Không dựng mô phỏng tem đồ họa. Hiển thị nguyên payload cuối cùng mà
        // pipeline in tạo ra, để người vận hành thấy chính xác lệnh + dữ liệu
        // sẽ được gửi xuống máy in khi bấm IN THỬ.
        string payload = FormatPrinterCommandForPreview(request.Payload);

        InlineLabelPreviewViewbox.Child = null;
        InlineLabelPreviewViewbox.Visibility = Visibility.Collapsed;

        // TextBox của trang có style chung VerticalContentAlignment=Center.
        // Với preview nhiều dòng điều đó làm command bị nằm giữa/dưới khung.
        // Ép local alignment + vị trí scroll để dòng đầu (8N/Q/R... tùy mẫu)
        // luôn xuất hiện ngay ở góc trên-trái.
        InlineLabelCommandTextBox.HorizontalContentAlignment = HorizontalAlignment.Left;
        InlineLabelCommandTextBox.VerticalContentAlignment = VerticalAlignment.Top;
        InlineLabelCommandTextBox.Text = payload;
        InlineLabelCommandTextBox.Visibility = Visibility.Visible;
        InlineLabelCommandTextBox.SelectionStart = 0;
        InlineLabelCommandTextBox.SelectionLength = 0;
        InlineLabelCommandTextBox.ScrollToHome();
        InlineLabelCommandTextBox.ScrollToHorizontalOffset(0);
        InlineLabelCommandTextBox.ScrollToVerticalOffset(0);
        InlineLabelPreviewPlaceholder.Visibility = Visibility.Collapsed;
        InlineLabelPreviewInfoText.Text =
            $"{request.Profile.Id} • {request.WidthMm} × {request.HeightMm} mm • " +
            $"LOT {request.Data.LotNo} • {LabelProfileResolver.DetectLanguage(request.Payload)}";
        SetInlineLabelPreviewStatus(status, isError: false);
    }

    private static string FormatPrinterCommandForPreview(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return string.Empty;

        // request.Payload là nguồn dữ liệu duy nhất: chính payload này được pipeline
        // in sử dụng. Chỉ loại bỏ các ký tự điều khiển không thể hiển thị trong
        // TextBox (NUL/ESC/BOM...), tuyệt đối không thay đổi nội dung lệnh EPL/ZPL.
        var builder = new StringBuilder(payload.Length);
        foreach (char character in payload)
        {
            if (character is '\r' or '\n' or '\t')
            {
                builder.Append(character);
                continue;
            }

            if (character == '\uFEFF' || char.IsControl(character))
                continue;

            builder.Append(character);
        }

        string visible = builder.ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        // Không để framing/control byte đã bị loại bỏ tạo ra các dòng trắng ở đầu.
        return visible.TrimStart('\n');
    }

    private void SetInlineLabelPreviewStatus(string status, bool isError)
    {
        if (InlineLabelPreviewStatusText is null)
            return;

        InlineLabelPreviewStatusText.Text = status ?? string.Empty;
        InlineLabelPreviewStatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(198, 40, 40))
            : new SolidColorBrush(Color.FromRgb(31, 67, 145));
    }

    private void ShowLabelPreviewWindow(LabelPrintRequest request)
    {
        var previewWindow = new Window
        {
            Title = $"XEM TRƯỚC TEM • {request.Profile.Id}",
            Owner = HostWindow,
            Width = 1120,
            Height = 720,
            MinWidth = 760,
            MinHeight = 500,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = $"{request.Profile.Id}  •  LOT {request.Data.LotNo}",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 67, 145))
        });
        title.Children.Add(new TextBlock
        {
            Text = $"Kích thước tem thật: {request.WidthMm} × {request.HeightMm} mm  •  " +
                   $"Payload: {LabelProfileResolver.DetectLanguage(request.Payload)}  •  " +
                   $"Máy in: {(string.IsNullOrWhiteSpace(request.Printer) ? "CHƯA CHỌN" : request.Printer)}",
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = Brushes.DimGray,
            FontSize = 12
        });
        header.Children.Add(title);

        var fidelity = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(238, 245, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(164, 188, 224)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Child = new TextBlock
            {
                Text = "CÙNG PAYLOAD VỚI ĐƯỜNG IN",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 67, 145))
            }
        };
        Grid.SetColumn(fidelity, 1);
        header.Children.Add(fidelity);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var tabs = new TabControl();
        var visualTab = new TabItem { Header = "TEM THỰC TẾ" };
        var commandTab = new TabItem { Header = "LỆNH IN GỐC" };

        var visualHost = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(239, 242, 247)),
            Margin = new Thickness(2)
        };
        visualHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        visualHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        FrameworkElement labelVisual = BuildLabelPreviewVisual(request);
        var viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = labelVisual
        };
        visualHost.Children.Add(viewbox);

        var note = new TextBlock
        {
            Margin = new Thickness(14, 0, 14, 12),
            Text = "Preview dựng từ chính payload EPL/ZPL đã render và đúng tỷ lệ kích thước tem vật lý. " +
                   "TEM_BE_QR được hiển thị dưới dạng QR Code (3 finder ở ba góc), không phải Data Matrix. " +
                   "Font raster/module cuối cùng có thể chênh nhẹ vì do firmware máy in tạo; dùng ‘IN THỬ ĐÚNG BẢN NÀY’ để kiểm chứng vật lý.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            FontSize = 11.5
        };
        Grid.SetRow(note, 1);
        visualHost.Children.Add(note);
        visualTab.Content = visualHost;

        commandTab.Content = new TextBox
        {
            Text = request.Payload,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Margin = new Thickness(6)
        };

        tabs.Items.Add(visualTab);
        tabs.Items.Add(commandTab);
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var snapshotText = new TextBlock
        {
            Text = $"Part: {request.Data.PartNumber}   •   Barcode: {request.Data.Barcode}",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.DimGray,
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        footer.Children.Add(snapshotText);

        var printButton = new Button
        {
            Content = "IN THỬ ĐÚNG BẢN NÀY",
            MinWidth = 190,
            Height = 34,
            Margin = new Thickness(8, 0, 8, 0),
            FontWeight = FontWeights.Bold
        };
        Grid.SetColumn(printButton, 1);
        printButton.Click += async (_, _) =>
        {
            if (_main is null)
            {
                MessageBox.Show(
                    previewWindow,
                    "Trang Cài đặt chưa được nối với chương trình chính.",
                    "IN THỬ TEM",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            printButton.IsEnabled = false;
            try
            {
                LabelPrintTransportResult result = await _main.Test.PrintSettingsLabelAsync(request);
                MessageBox.Show(
                    previewWindow,
                    result.Printed
                        ? "Đã in đúng snapshot đang xem. Không tăng LOT hoặc sản lượng."
                        : "Chưa in được tem. Hãy kiểm tra cổng COM/cáp máy in.",
                    "IN THỬ TEM",
                    MessageBoxButton.OK,
                    result.Printed ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                AsyncFileLogService.Current.Error($"Preview snapshot test print failed: {ex}");
                MessageBox.Show(
                    previewWindow,
                    "Chưa in được snapshot đang xem. Hãy kiểm tra kết nối máy in.",
                    "IN THỬ TEM",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                printButton.IsEnabled = true;
            }
        };
        footer.Children.Add(printButton);

        var closeButton = new Button
        {
            Content = "ĐÓNG",
            MinWidth = 100,
            Height = 34,
            IsCancel = true
        };
        Grid.SetColumn(closeButton, 2);
        closeButton.Click += (_, _) => previewWindow.Close();
        footer.Children.Add(closeButton);

        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        previewWindow.Content = root;
        previewWindow.ShowDialog();
    }

    private static FrameworkElement BuildLabelPreviewVisual(LabelPrintRequest request)
    {
        const double dotsPerMm = 8.0; // máy in tem 203 dpi ≈ 8 dots/mm
        double logicalWidth = Math.Max(160, request.WidthMm * dotsPerMm);
        double logicalHeight = Math.Max(80, request.HeightMm * dotsPerMm);

        var canvas = new Canvas
        {
            Width = logicalWidth,
            Height = logicalHeight,
            Background = Brushes.White,
            ClipToBounds = true
        };

        LabelPrintMode language = LabelProfileResolver.DetectLanguage(request.Payload);
        bool rendered = language switch
        {
            LabelPrintMode.RawZpl => RenderZplPreview(canvas, request.Payload),
            LabelPrintMode.RawEpl => RenderEplPreview(canvas, request),
            _ => RenderEplPreview(canvas, request) || RenderZplPreview(canvas, request.Payload)
        };

        if (!rendered)
        {
            canvas.Children.Add(new TextBlock
            {
                Text = "Không nhận diện được lệnh đồ họa của template này.\nXem tab LỆNH IN GỐC để đối chiếu payload.",
                Margin = new Thickness(18),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            });
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(2),
            Child = canvas,
            SnapsToDevicePixels = true
        };
    }

    private static bool RenderEplPreview(Canvas canvas, LabelPrintRequest request)
    {
        string payload = request.Payload;
        string[] lines = payload.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        // Built-in TEM_BE/TEM_BE_QR were authored against the full printer-head
        // coordinate space. Their physical 60 mm label starts about 180 dots
        // from the head origin (the verified QR form even carries the legacy
        // X180..620 layout reference). Preview must translate that printer-head
        // X coordinate back to the physical label's left edge. The print payload
        // itself is NOT modified.
        string templateType = LabelProfileResolver.NormalizeTemplateType(
            string.IsNullOrWhiteSpace(request.Profile.Id) ? request.FormatName : request.Profile.Id);
        double labelOriginX = templateType is LabelSettings.SmallTemplate or LabelSettings.SmallQrTemplate
            ? 180d
            : 0d;

        // EPL R command offsets the reference point. Respect it in the preview.
        double referenceX = 0d;
        double referenceY = 0d;
        foreach (string rawReference in lines)
        {
            Match reference = Regex.Match(
                rawReference.Trim(),
                @"^(?:N)?R(?<x>-?\d+),(?<y>-?\d+)$",
                RegexOptions.CultureInvariant);
            if (!reference.Success)
                continue;

            referenceX = ParseDouble(reference.Groups["x"].Value);
            referenceY = ParseDouble(reference.Groups["y"].Value);
            break;
        }

        double PhysicalX(double printerX) => printerX + referenceX - labelOriginX;
        double PhysicalY(double printerY) => printerY + referenceY;
        var variableOrder = new List<string>();
        foreach (string raw in lines)
        {
            Match variable = Regex.Match(raw.Trim(), @"^V(?<id>\d{2}),", RegexOptions.CultureInvariant);
            if (variable.Success)
            {
                string id = "V" + variable.Groups["id"].Value;
                if (!variableOrder.Contains(id, StringComparer.Ordinal))
                    variableOrder.Add(id);
            }
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        int dataMarker = Array.FindIndex(lines, line => line.Trim() == "?");
        if (dataMarker >= 0)
        {
            int valueIndex = 0;
            for (int index = dataMarker + 1; index < lines.Length && valueIndex < variableOrder.Count; index++)
            {
                string value = lines[index].TrimEnd('\r');
                if (value.StartsWith("P", StringComparison.OrdinalIgnoreCase) &&
                    value.Length <= 4)
                {
                    break;
                }

                values[variableOrder[valueIndex++]] = value;
            }
        }

        bool rendered = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("'", StringComparison.Ordinal))
                continue;

            Match text = Regex.Match(
                line,
                @"^A(?<x>-?\d+),(?<y>-?\d+),(?<rot>\d+),(?<font>\d+),(?<hm>\d+),(?<vm>\d+),(?<rev>[NR]),(?<data>.+)$",
                RegexOptions.CultureInvariant);
            if (text.Success)
            {
                double x = PhysicalX(ParseDouble(text.Groups["x"].Value));
                double y = PhysicalY(ParseDouble(text.Groups["y"].Value));
                int font = ParseInt(text.Groups["font"].Value, 1);
                int hm = Math.Max(1, ParseInt(text.Groups["hm"].Value, 1));
                int vm = Math.Max(1, ParseInt(text.Groups["vm"].Value, 1));
                int rotation = ParseInt(text.Groups["rot"].Value, 0) * 90;
                string value = ResolveEplExpression(text.Groups["data"].Value, values);

                var block = new TextBlock
                {
                    Text = value,
                    FontFamily = new FontFamily("Arial"),
                    FontSize = (font <= 1 ? 11 : 15) * vm,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = text.Groups["rev"].Value == "R" ? Brushes.White : Brushes.Black,
                    Background = text.Groups["rev"].Value == "R" ? Brushes.Black : Brushes.Transparent,
                    Padding = new Thickness(0),
                    RenderTransformOrigin = new Point(0, 0)
                };
                if (hm > 1)
                    block.LayoutTransform = new ScaleTransform(hm, 1);
                if (rotation != 0)
                    block.RenderTransform = new RotateTransform(rotation);
                Canvas.SetLeft(block, x);
                Canvas.SetTop(block, y);
                canvas.Children.Add(block);
                rendered = true;
                continue;
            }

            Match matrix = Regex.Match(
                line,
                @"^b(?<x>-?\d+),(?<y>-?\d+),(?<kind>[A-Za-z0-9]+),(?<size>[^,]+),(?<data>.+)$",
                RegexOptions.CultureInvariant);
            if (matrix.Success)
            {
                double x = PhysicalX(ParseDouble(matrix.Groups["x"].Value));
                double y = PhysicalY(ParseDouble(matrix.Groups["y"].Value));
                string kind = matrix.Groups["kind"].Value.ToUpperInvariant();
                string value = ResolveEplExpression(matrix.Groups["data"].Value, values);
                int module = ExtractFirstInteger(matrix.Groups["size"].Value, 3);
                double size = kind == "Q"
                    ? Math.Clamp(30 + module * 22, 78, 126)
                    : Math.Clamp(34 + module * 14, 68, 112);
                if (kind == "Q")
                    DrawQrPlaceholder(canvas, x, y, size, value);
                else
                    DrawDataMatrixPlaceholder(canvas, x, y, size, value);
                rendered = true;
                continue;
            }

            Match box = Regex.Match(
                line,
                @"^X(?<x1>\d+),(?<y1>\d+),(?<t>\d+),(?<x2>\d+),(?<y2>\d+)$",
                RegexOptions.CultureInvariant);
            if (box.Success)
            {
                double x1 = PhysicalX(ParseDouble(box.Groups["x1"].Value));
                double y1 = PhysicalY(ParseDouble(box.Groups["y1"].Value));
                double x2 = PhysicalX(ParseDouble(box.Groups["x2"].Value));
                double y2 = PhysicalY(ParseDouble(box.Groups["y2"].Value));
                var rect = new Rectangle
                {
                    Width = Math.Max(1, x2 - x1),
                    Height = Math.Max(1, y2 - y1),
                    Stroke = Brushes.Black,
                    StrokeThickness = Math.Max(1, ParseDouble(box.Groups["t"].Value))
                };
                Canvas.SetLeft(rect, x1);
                Canvas.SetTop(rect, y1);
                canvas.Children.Add(rect);
                rendered = true;
            }
        }

        return rendered;
    }

    private static bool RenderZplPreview(Canvas canvas, string payload)
    {
        if (!payload.Contains("^XA", StringComparison.OrdinalIgnoreCase))
            return false;

        bool rendered = false;
        MatchCollection fields = Regex.Matches(
            payload,
            @"\^(?:FO|FT)(?<x>\d+),(?<y>\d+)(?<body>.*?)(?=\^(?:FO|FT)|\^XZ)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match field in fields)
        {
            double x = ParseDouble(field.Groups["x"].Value);
            double y = ParseDouble(field.Groups["y"].Value);
            string body = field.Groups["body"].Value;
            Match data = Regex.Match(body, @"\^FD(?<data>.*?)\^FS", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            string value = data.Success ? data.Groups["data"].Value.Replace("\\&", "\n", StringComparison.Ordinal) : string.Empty;

            bool isQr = body.Contains("^BQ", StringComparison.OrdinalIgnoreCase);
            bool isDataMatrix = body.Contains("^BX", StringComparison.OrdinalIgnoreCase);
            bool isLinearBarcode = body.Contains("^BC", StringComparison.OrdinalIgnoreCase);
            if (isQr)
            {
                DrawQrPlaceholder(canvas, x, y, 104, value);
                rendered = true;
                continue;
            }
            if (isDataMatrix)
            {
                DrawDataMatrixPlaceholder(canvas, x, y, 100, value);
                rendered = true;
                continue;
            }
            if (isLinearBarcode)
            {
                DrawLinearBarcodePlaceholder(canvas, x, y, 120, 58, value);
                rendered = true;
                continue;
            }

            Match graphicBox = Regex.Match(body, @"\^GB(?<w>\d+),(?<h>\d+),(?<t>\d+)", RegexOptions.IgnoreCase);
            if (graphicBox.Success)
            {
                var rect = new Rectangle
                {
                    Width = ParseDouble(graphicBox.Groups["w"].Value),
                    Height = ParseDouble(graphicBox.Groups["h"].Value),
                    Stroke = Brushes.Black,
                    StrokeThickness = Math.Max(1, ParseDouble(graphicBox.Groups["t"].Value))
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                canvas.Children.Add(rect);
                rendered = true;
            }

            if (value.Length > 0)
            {
                Match font = Regex.Match(body, @"\^A[^,]*,(?<h>\d+),(?<w>\d+)", RegexOptions.IgnoreCase);
                double fontSize = font.Success ? Math.Clamp(ParseDouble(font.Groups["h"].Value) * 0.78, 9, 42) : 15;
                var block = new TextBlock
                {
                    Text = value,
                    FontFamily = new FontFamily("Arial"),
                    FontSize = fontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.Black
                };
                Canvas.SetLeft(block, x);
                Canvas.SetTop(block, y);
                canvas.Children.Add(block);
                rendered = true;
            }
        }

        return rendered;
    }

    private static string ResolveEplExpression(
        string expression,
        IReadOnlyDictionary<string, string> variables)
    {
        string resolved = Regex.Replace(
            expression,
            @"V\d{2}",
            match => variables.TryGetValue(match.Value, out string? value) ? value : match.Value,
            RegexOptions.CultureInvariant);
        return resolved.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
    }

    private static void DrawQrPlaceholder(
        Canvas canvas,
        double x,
        double y,
        double size,
        string value)
    {
        // Preview QR rõ ràng: quiet zone 4 module + 3 finder chuẩn ở ba góc.
        // Payload in thật vẫn là lệnh EPL kind=Q; đây chỉ là raster mô phỏng UI.
        const int dataModules = 29;
        const int quiet = 4;
        const int modules = dataModules + quiet * 2;
        double moduleSize = size / modules;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));

        var background = new Rectangle
        {
            Width = size,
            Height = size,
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 0.8
        };
        Canvas.SetLeft(background, x);
        Canvas.SetTop(background, y);
        canvas.Children.Add(background);

        bool InFinder(int row, int col, int r0, int c0)
        {
            int r = row - r0;
            int c = col - c0;
            if (r < 0 || r >= 7 || c < 0 || c >= 7)
                return false;
            return r == 0 || r == 6 || c == 0 || c == 6 ||
                   (r >= 2 && r <= 4 && c >= 2 && c <= 4);
        }

        bool IsFinderOrSeparator(int row, int col)
        {
            int[] starts = [quiet, quiet + dataModules - 7];
            if (InFinder(row, col, starts[0], starts[0]) ||
                InFinder(row, col, starts[0], starts[1]) ||
                InFinder(row, col, starts[1], starts[0]))
                return true;

            bool Around(int r0, int c0) =>
                row >= r0 - 1 && row <= r0 + 7 &&
                col >= c0 - 1 && col <= c0 + 7;
            return Around(starts[0], starts[0]) ||
                   Around(starts[0], starts[1]) ||
                   Around(starts[1], starts[0]);
        }

        bool IsBlack(int row, int col)
        {
            if (row < quiet || col < quiet || row >= modules - quiet || col >= modules - quiet)
                return false;

            int top = quiet;
            int right = quiet + dataModules - 7;
            if (InFinder(row, col, top, top) ||
                InFinder(row, col, top, right) ||
                InFinder(row, col, right, top))
                return true;

            if (IsFinderOrSeparator(row, col))
                return false;

            // Timing pattern làm hình nhận diện là QR thay vì Data Matrix.
            if (row == quiet + 6 && col >= quiet + 8 && col < right - 1)
                return col % 2 == 0;
            if (col == quiet + 6 && row >= quiet + 8 && row < right - 1)
                return row % 2 == 0;

            int bitIndex = (row - quiet) * dataModules + (col - quiet);
            return (hash[(bitIndex / 8) % hash.Length] & (1 << (bitIndex % 8))) != 0;
        }

        for (int row = 0; row < modules; row++)
        {
            for (int col = 0; col < modules; col++)
            {
                if (!IsBlack(row, col))
                    continue;

                var pixel = new Rectangle
                {
                    Width = moduleSize + 0.12,
                    Height = moduleSize + 0.12,
                    Fill = Brushes.Black,
                    StrokeThickness = 0
                };
                Canvas.SetLeft(pixel, x + col * moduleSize);
                Canvas.SetTop(pixel, y + row * moduleSize);
                canvas.Children.Add(pixel);
            }
        }
    }

    private static void DrawDataMatrixPlaceholder(
        Canvas canvas,
        double x,
        double y,
        double size,
        string value)
    {
        const int modules = 20;
        double moduleSize = size / modules;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));

        var background = new Rectangle
        {
            Width = size,
            Height = size,
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1
        };
        Canvas.SetLeft(background, x);
        Canvas.SetTop(background, y);
        canvas.Children.Add(background);

        for (int row = 0; row < modules; row++)
        {
            for (int col = 0; col < modules; col++)
            {
                bool border = col == 0 || row == modules - 1 ||
                              (row == 0 && col % 2 == 0) ||
                              (col == modules - 1 && row % 2 == 0);
                int bitIndex = row * modules + col;
                bool hashBit = (hash[(bitIndex / 8) % hash.Length] & (1 << (bitIndex % 8))) != 0;
                if (!border && !hashBit)
                    continue;

                var pixel = new Rectangle
                {
                    Width = moduleSize + 0.12,
                    Height = moduleSize + 0.12,
                    Fill = Brushes.Black,
                    StrokeThickness = 0
                };
                Canvas.SetLeft(pixel, x + col * moduleSize);
                Canvas.SetTop(pixel, y + row * moduleSize);
                canvas.Children.Add(pixel);
            }
        }
    }

    private static void DrawLinearBarcodePlaceholder(
        Canvas canvas,
        double x,
        double y,
        double width,
        double height,
        string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        double cursor = x;
        for (int index = 0; index < 80 && cursor < x + width; index++)
        {
            bool black = (hash[index % hash.Length] & (1 << (index % 8))) != 0;
            double barWidth = index % 3 == 0 ? 2.4 : 1.2;
            if (black)
            {
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = height,
                    Fill = Brushes.Black,
                    StrokeThickness = 0
                };
                Canvas.SetLeft(bar, cursor);
                Canvas.SetTop(bar, y);
                canvas.Children.Add(bar);
            }
            cursor += barWidth + 0.8;
        }
    }

    private static int ExtractFirstInteger(string value, int fallback)
    {
        Match match = Regex.Match(value ?? string.Empty, @"\d+", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;

    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0d;

    private async void TestPrintLabel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LabelPrintRequest request = BuildSettingsLabelRequest("TEST-PRINT");

            // Hiển thị chính snapshot/payload sắp được gửi tới máy in ngay trong
            // khung XEM TRƯỚC TEM. Không dựng request lần hai để tránh lệch LOT,
            // timestamp, barcode hoặc dữ liệu THT giữa preview và bản in thử.
            RenderInlineLabelPreview(request, "ĐANG IN THỬ...");

            if (_main is null)
                throw new InvalidOperationException("Trang Cài đặt chưa được nối với chương trình chính.");

            LabelPrintTransportResult result = await _main.Test.PrintSettingsLabelAsync(request);
            SetInlineLabelPreviewStatus(
                result.Printed ? "ĐÃ IN THỬ" : "IN THỬ KHÔNG THÀNH CÔNG",
                isError: !result.Printed);

            ShowMessage(
                result.Printed
                    ? "Đã in thử đúng snapshot đang hiển thị. Không tăng LOT hoặc sản lượng."
                    : "Chưa in thử được tem. Hãy rút/cắm lại cáp và chọn lại cổng COM.",
                "IN THỬ TEM",
                result.Printed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Test label print failed: {ex}");
            SetInlineLabelPreviewStatus("IN THỬ LỖI", isError: true);
            ShowMessage(
                "Chưa in thử được tem. Hãy rút/cắm lại cáp và chọn lại cổng COM.",
                "IN THỬ TEM",
                MessageBoxImage.Warning);
        }
    }

    private LabelPrintRequest BuildSettingsLabelRequest(string purpose)
    {
        ApplyLabelTemplatePhysicalSize(_vm.Settings.Label.TemplateType);
        string thtPath = _vm.Settings.LastThtPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(thtPath) || !File.Exists(thtPath))
            throw new FileNotFoundException("Chưa có file THT hiện tại để dựng dữ liệu tem.", thtPath);

        ProductModel model = new ThtModelParser().Load(thtPath);
        DateTime now = DateTime.Now;
        var history = new TestHistoryRecord
        {
            Finished = now,
            PartName = model.ProductName,
            PartNumber = model.PartNumber,
            Eco = model.Eco,
            Nco = model.Nco,
            Alc = model.Alc,
            LotNo = Math.Max(0, _vm.Settings.LotNo),
            ModelName = model.ModelName,
            ModelFile = model.SourcePath,
            CycleId = purpose + "-" + now.ToString("yyyyMMddHHmmssfff")
        };
        return LabelPrintRequest.Capture(history, model, _vm.Settings.Label);
    }

    private string ResolveConfiguredLabelTemplatePath()
    {
        string configured = _vm.Settings.Label.TemplatePath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Template tích hợp phải được chỉnh bằng trình soạn thảo trong ứng dụng.");

        string path = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(configured)
            ? configured
            : System.IO.Path.Combine(AppContext.BaseDirectory, configured));
        if (!File.Exists(path))
            throw new FileNotFoundException("Không tìm thấy file template label.", path);
        return path;
    }

    private static string SafeFileName(string value)
    {
        HashSet<char> invalid = System.IO.Path.GetInvalidFileNameChars().ToHashSet();
        string safe = new((value ?? string.Empty).Where(character => !invalid.Contains(character)).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "UNRESOLVED" : safe;
    }

    private async Task RefreshPortsAsync()
    {
        int generation = Interlocked.Increment(ref _portRefreshGeneration);
        try
        {
            string[] ports = await Task.Run(() => SerialPort.GetPortNames()
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetComPortNumber)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray());

            if (IsReleased || generation != Volatile.Read(ref _portRefreshGeneration))
                return;

            string savedPort = _vm.Settings.Label.PrinterCom?.Trim() ?? string.Empty;
            List<ComPortOption> options = ports
                .Select(x => new ComPortOption(x, x))
                .ToList();

            if (!string.IsNullOrWhiteSpace(savedPort) &&
                options.All(x => !string.Equals(x.PortName, savedPort, StringComparison.OrdinalIgnoreCase)))
            {
                options.Insert(0, new ComPortOption(savedPort, savedPort));
            }

            options.Insert(0, new ComPortOption(string.Empty, "Không dùng COM / dùng Windows printer"));
            _suppressPrinterPortSelection = true;
            try
            {
                PrinterComComboBox.ItemsSource = options;
                PrinterComComboBox.SelectedValue = savedPort;
            }
            finally
            {
                _suppressPrinterPortSelection = false;
            }

            string savedWaterProofPort = _vm.Settings.WaterProofMachine.PortName?.Trim() ?? string.Empty;
            WaterProofComComboBox.ItemsSource = ports;
            WaterProofComComboBox.Text = savedWaterProofPort;
        }
        catch (Exception ex)
        {
            if (IsReleased || generation != Volatile.Read(ref _portRefreshGeneration))
                return;

            _suppressPrinterPortSelection = true;
            try
            {
                PrinterComComboBox.ItemsSource = new[] { new ComPortOption(string.Empty, "Không dùng COM") };
                PrinterComComboBox.SelectedIndex = 0;
            }
            finally
            {
                _suppressPrinterPortSelection = false;
            }
            WaterProofComComboBox.ItemsSource = Array.Empty<string>();
            AsyncFileLogService.Current.Error($"COM port enumeration failed: {ex}");
            ShowMessage(
                "Chưa đọc được danh sách cổng kết nối. Vui lòng thử lại.",
                "Cổng COM",
                MessageBoxImage.Warning);
        }
    }

    private async void RefreshWaterProofPorts_Click(object sender, RoutedEventArgs e) =>
        await RefreshPortsAsync();

    private static int GetComPortNumber(string portName)
    {
        Match m = Regex.Match(portName, @"^COM(\d+)$", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out int number)
            ? number
            : int.MaxValue;
    }

    private void CommitPendingEditorValues()
    {
        CommitPendingEditorValues(this);
    }

    private static void CommitPendingEditorValues(DependencyObject parent)
    {
        switch (parent)
        {
            case TextBox textBox:
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                break;
            case ComboBox comboBox:
                comboBox.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateSource();
                comboBox.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateSource();
                comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                break;
            case ToggleButton toggleButton:
                toggleButton.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
                break;
        }

        int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < childCount; index++)
            CommitPendingEditorValues(System.Windows.Media.VisualTreeHelper.GetChild(parent, index));
    }

    private void SyncCompatibilityFields()
    {
        BoardCapacity capacity = BoardCapacity.FromSettings(_vm.Settings);
        _vm.Settings.CardCount = capacity.ScanCardCount;
        _vm.Settings.StampDelay =
            $"{_vm.Settings.Relay1JigPulseMs},{_vm.Settings.Relay2MarkingPulseMs}";
    }

    private async Task NotifySettingsSavedAsync()
    {
        Func<object?, EventArgs, Task>? handlers = SettingsSaved;
        if (handlers is null)
            return;

        foreach (Delegate subscriber in handlers.GetInvocationList())
            await ((Func<object?, EventArgs, Task>)subscriber)(this, EventArgs.Empty);
    }

    public void ShowSavedConfirmation()
    {
        SettingsSavedStatusText.Visibility = Visibility.Visible;
    }

    private async Task<bool> PersistSettingsAsync()
    {
        if (Interlocked.CompareExchange(ref _saveInProgress, 1, 0) != 0)
            return false;

        try
        {
            CommitPendingEditorValues();
            if (!ValidateSettings(out string error))
            {
                ShowMessage(error, "Cấu hình chưa hợp lệ", MessageBoxImage.Warning);
                return false;
            }

            SyncCompatibilityFields();
            _vm.Save();
            await NotifySettingsSavedAsync();
            return true;
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Save production settings failed: {ex}");
            ShowMessage(
                "Chưa lưu được Cài đặt. Dữ liệu đang nhập vẫn được giữ nguyên.",
                "CHƯA LƯU ĐƯỢC",
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _saveInProgress, 0);
        }
    }

    public void SetManualRuntimeActive(bool active) =>
        _vm.SetManualRuntimeActive(active);

    public async Task ReleaseManualOutputsAsync()
    {
        if (_main?.Test.IsManualModeActive == true)
            await _main.Test.ResetManualOutputsAsync();
        _vm.SetManualRuntimeActive(false);
    }

    private bool ValidateSettings(out string error)
    {
        if (_vm.Settings.LotNo < 0)
        {
            error = "LOTNO phải là số nguyên từ 0 trở lên.";
            return false;
        }

        if (_vm.MasterFaultRequiredCount is < 0 or > 99)
        {
            error = "Số lỗi Master phải từ 0 đến 99. 0 = bỏ kiểm tra Master.";
            return false;
        }

        if (_vm.Settings.ExpansionCardCount is < 1 or > BoardCapacity.MaxExpansionCardCount)
        {
            error = $"Card mở rộng phải từ 1 đến {BoardCapacity.MaxExpansionCardCount}.";
            return false;
        }

        if (_vm.Settings.StartCardNumber is < 1 or > BoardCapacity.MaxExpansionCardCount ||
            _vm.Settings.StartCardNumber + _vm.Settings.ExpansionCardCount - 1 > BoardCapacity.MaxExpansionCardCount)
        {
            error = $"Card bắt đầu + số card mở rộng không được vượt card {BoardCapacity.MaxExpansionCardCount}.";
            return false;
        }

        BoardCapacity capacity = BoardCapacity.FromSettings(_vm.Settings);
        if (!capacity.IsRangeWithinSystem)
        {
            error =
                "Cấu hình card vượt phạm vi phần cứng hiện tại.\n\n" +
                $"Card mở rộng: {capacity.ExpansionCardCount}\n" +
                $"Tổng IO: {capacity.TotalIoCapacity}\n" +
                $"Giới hạn card: {BoardCapacity.MaxExpansionCardCount}.";
            return false;
        }

        if (_vm.Settings.IoConfirm1 is < 0 or > 127 || _vm.Settings.IoConfirmN is < 0 or > 31)
        {
            error = "Xác nhận IO1 phải 0..127 và IOn phải 0..31.";
            return false;
        }

        if (_vm.Settings.UsbDelay is < 1 or > 16)
        {
            error = "USB Delay phải từ 1 đến 16 ms.";
            return false;
        }

        if (_vm.Settings.Relay1JigPulseMs is < 50 or > 5000)
        {
            error = "R1 JIG phải từ 50 đến 5000 ms.";
            return false;
        }

        if (_vm.Settings.Relay2MarkingPulseMs is < 50 or > 5000)
        {
            error = "R2 MARKING phải từ 50 đến 5000 ms.";
            return false;
        }

        if (_vm.Settings.PassMarkingToJigDelayMs is < 0 or > 5000)
        {
            error = "Delay PASS từ R2 sang R1 phải từ 0 đến 5000 ms.";
            return false;
        }

        if (_vm.Settings.RelayWiringMode is < 0 or > 1)
        {
            error = "Hãy chọn đúng kiểu đấu Relay MARKING và Relay mở JIG của máy.";
            return false;
        }

        if (_vm.Settings.Label.WidthMm <= 0 || _vm.Settings.Label.HeightMm <= 0)
        {
            error = "Kích thước tem phải lớn hơn 0 mm.";
            return false;
        }

        if (_vm.Settings.Label.BaudRate <= 0 ||
            _vm.Settings.Label.WriteTimeoutMs < 100 ||
            _vm.Settings.Label.Copies is < 1 or > 20)
        {
            error = "Cấu hình máy in không hợp lệ: BaudRate > 0, timeout >= 100 ms, số bản in 1..20.";
            return false;
        }

        if (_vm.WaterProof.Enabled)
        {
            if (string.IsNullOrWhiteSpace(_vm.Settings.WaterProofMachine.PortName))
            {
                error = "Model đang bật kiểm tra kín nước nhưng chưa cấu hình COM UART/RS232.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_vm.Settings.Label.PrinterCom) &&
                string.Equals(
                    _vm.Settings.WaterProofMachine.PortName.Trim(),
                    _vm.Settings.Label.PrinterCom.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "Máy Leak và máy in tem không được dùng chung một cổng COM.";
                return false;
            }

            if (_vm.WaterProof.EnabledChannelCount == 0)
            {
                error = "Kiểm tra kín nước phải chọn ít nhất một kênh CH1/CH2/CH3.";
                return false;
            }

            (string Channel, bool Enabled, string Connector)[] connectorMappings =
            [
                ("CH1", _vm.WaterProof.Channel1Enabled, _vm.WaterProof.Channel1Connector),
                ("CH2", _vm.WaterProof.Channel2Enabled, _vm.WaterProof.Channel2Connector),
                ("CH3", _vm.WaterProof.Channel3Enabled, _vm.WaterProof.Channel3Connector)
            ];
            foreach ((string channel, bool enabled, string connector) in connectorMappings)
            {
                if (!enabled)
                    continue;

                if (string.IsNullOrWhiteSpace(connector))
                {
                    error = $"{channel}: phải chọn connector THT trước khi bật kiểm tra kín nước.";
                    return false;
                }

                if (_vm.WaterProofConnectorOptions.Count == 0)
                {
                    error = "Không đọc được connector từ file THT hiện tại. Không thể bật kiểm tra kín nước.";
                    return false;
                }

                if (!_vm.WaterProofConnectorOptions.Contains(
                        connector.Trim(),
                        StringComparer.OrdinalIgnoreCase))
                {
                    error = $"{channel}: connector '{connector}' không tồn tại trong file THT hiện tại.";
                    return false;
                }
            }

            if (_vm.WaterProof.PressMin < 0 || _vm.WaterProof.LeakLimit < 0)
            {
                error = "Áp tối thiểu và độ sụt tối đa không được âm.";
                return false;
            }

            if (_vm.WaterProof.PressTimeMs is < 1 or > 300000 ||
                _vm.WaterProof.WaitTimeMs is < 1 or > 300000)
            {
                error = "Thời gian tạo áp/giữ áp phải từ 1 đến 300000 ms.";
                return false;
            }
        }

        foreach (ResistanceChannelEditor channel in _vm.ResistanceChannels)
        {
            if (channel.ChannelSelection is < ResistanceMeasurementPlan.DisabledChannel or
                > D2xxResistanceRouting.MaxChannel)
            {
                error = $"Kênh của {channel.Name} phải nằm trong khoảng 0 đến 10.";
                return false;
            }

            if (!double.IsFinite(channel.MinOhm) ||
                !double.IsFinite(channel.MaxOhm) ||
                channel.MinOhm < 0 ||
                channel.MaxOhm < channel.MinOhm)
            {
                error = $"{channel.Name}: Min Ω phải <= Max Ω và không được âm.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!await PersistSettingsAsync())
            return;

        await ReleaseManualOutputsAsync();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private void ShowMessage(string message, string title, MessageBoxImage image)
    {
        if (IsReleased)
            return;

        Window? owner = HostWindow;
        if (owner is not null)
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
    }

    private sealed record CardIoOption(int ExpansionCardCount, string Display);
    private sealed record ComPortOption(string PortName, string Display);
    private sealed record RelayWiringOption(int Mode, string Display);
}
