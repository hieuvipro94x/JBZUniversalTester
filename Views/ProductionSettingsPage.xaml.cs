using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Text;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly string _labelSettingsPasswordAtOpen;
    private int _released;
    private int _portRefreshGeneration;

    public event EventHandler? SettingsSaved;
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
        _labelSettingsPasswordAtOpen = _vm.Settings.Password ?? string.Empty;
        InitializeComponent();
        DataContext = _vm;
        InitializeComboBoxItems();
        SetLabelSettingsUnlocked(string.IsNullOrEmpty(_labelSettingsPasswordAtOpen));
        Loaded += ProductionSettingsPage_Loaded;
    }

    private async void ProductionSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ProductionSettingsPage_Loaded;
        await System.Windows.Threading.Dispatcher.Yield(
            System.Windows.Threading.DispatcherPriority.ContextIdle);
        if (IsReleased)
            return;

        await RefreshPortsAsync();
        if (IsReleased)
            return;
        RefreshPrinterConnectionStatus();
    }

    public void ReleasePageResources()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;

        Interlocked.Increment(ref _portRefreshGeneration);
        Loaded -= ProductionSettingsPage_Loaded;
        DataContext = null;
        SettingsSaved = null;
        RequestClose = null;
    }

    private bool IsReleased => Volatile.Read(ref _released) != 0;

    private Window? HostWindow => Window.GetWindow(this) ?? Application.Current?.MainWindow;

    private void UnlockLabelSettings_Click(object sender, RoutedEventArgs e) =>
        TryUnlockLabelSettings();

    private void LabelUnlockPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        TryUnlockLabelSettings();
        e.Handled = true;
    }

    private void TryUnlockLabelSettings()
    {
        if (!AdminAuthenticationService.Verify(
                _labelSettingsPasswordAtOpen,
                LabelUnlockPasswordBox.Password))
        {
            LabelUnlockErrorText.Visibility = Visibility.Visible;
            LabelUnlockPasswordBox.SelectAll();
            LabelUnlockPasswordBox.Focus();
            return;
        }

        LabelUnlockPasswordBox.Password = string.Empty;
        LabelUnlockErrorText.Visibility = Visibility.Collapsed;
        SetLabelSettingsUnlocked(true);
        LabelSettingsPasswordTextBox.Focus();
    }

    private void SetLabelSettingsUnlocked(bool unlocked)
    {
        bool locked = !unlocked && !string.IsNullOrEmpty(_labelSettingsPasswordAtOpen);
        LabelSettingsLockPanel.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        LabelPrintSettingsForm.IsEnabled = !locked;
        LabelPrintActionsPanel.IsEnabled = !locked;
        if (locked)
            LabelUnlockPasswordBox.Focus();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IoSettingsPanel is null || RelaySettingsPanel is null || LabelSettingsPanel is null)
        {
            return;
        }

        // 1920x1080 là bố cục chính: ba panel dùng toàn bộ chiều rộng theo
        // tỷ lệ 28/28/44. Không cưỡng chiều rộng 760px vì trên màn hình nhỏ
        // hoặc DPI cao nó làm nội dung vượt viewport trong khi cuộn ngang tắt.
        double available = Math.Max(320, e.NewSize.Width - 28);
        if (available >= 1240)
        {
            double content = available - 24;
            IoSettingsPanel.Width = Math.Floor(content * 0.28);
            RelaySettingsPanel.Width = Math.Floor(content * 0.28);
            LabelSettingsPanel.Width = Math.Max(
                500,
                content - IoSettingsPanel.Width - RelaySettingsPanel.Width);
            return;
        }

        if (available >= 760)
        {
            double halfPanel = Math.Floor((available - 16) / 2);
            IoSettingsPanel.Width = halfPanel;
            RelaySettingsPanel.Width = halfPanel;
            LabelSettingsPanel.Width = available - 8;
            return;
        }

        // Màn hình rất hẹp: mỗi panel một hàng. Giữ 500px tối thiểu để form
        // TEM không ép mất chữ; ScrollViewer chỉ hiện cuộn ngang ở trường hợp
        // ngoại lệ này thay vì cắt hẳn mép phải.
        double singlePanel = Math.Max(500, available - 8);
        IoSettingsPanel.Width = singlePanel;
        RelaySettingsPanel.Width = singlePanel;
        LabelSettingsPanel.Width = singlePanel;
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

    private async void ConnectPrinter_Click(object sender, RoutedEventArgs e)
    {
        ConnectPrinterButton.IsEnabled = false;
        try
        {
            PrinterComComboBox
                .GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)
                ?.UpdateSource();

            string portName = _vm.Settings.Label.PrinterCom?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(portName))
            {
                PrinterConnectionStatusText.Text = "CHƯA CHỌN COM";
                ShowMessage("Hãy chọn cổng COM của máy in trước khi kết nối.", "MÁY IN", MessageBoxImage.Warning);
                return;
            }

            if (_main is null)
                throw new InvalidOperationException("Trang Cài đặt chưa được nối với chương trình chính.");

            PrinterConnectionStatusText.Text = "ĐANG KẾT NỐI...";
            LabelPrinterConnectionResult result = await _main.Test.ConnectLabelPrinterAsync(_vm.Settings.Label);
            PrinterConnectionStatusText.Text = result.Connected ? $"ĐÃ NỐI {portName}" : "KẾT NỐI LỖI";

            if (result.Connected)
            {
                // Lưu ngay cổng vừa kết nối để lần mở chương trình sau tự kết nối.
                ProductionConfigService.Save(_vm.Settings);
            }

            ShowMessage(
                result.Message,
                "KẾT NỐI MÁY IN",
                result.Connected ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            PrinterConnectionStatusText.Text = "KẾT NỐI LỖI";
            ShowMessage(ex.Message, "KẾT NỐI MÁY IN", MessageBoxImage.Warning);
        }
        finally
        {
            ConnectPrinterButton.IsEnabled = true;
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
            ShowMessage(ex.Message, "CHỈNH TEM", MessageBoxImage.Warning);
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
            Title = $"CHỈNH TEM {templateType} - lưu trong JBZUniversalTester.cfg",
            Owner = HostWindow,
            Width = 900,
            Height = 680,
            MinWidth = 640,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var layout = new Grid { Margin = new Thickness(12) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var note = new TextBlock
        {
            Text = "Nội dung sửa sẽ được lưu trong CFG, không tạo file Labels. Dùng KHÔI PHỤC MẶC ĐỊNH để bỏ bản tùy chỉnh.",
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
                MessageBox.Show(editor, "Template không được để trống.", "CHỈNH TEM", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            $"Đã áp dụng template {templateType}. Bấm LƯU CÀI ĐẶT để ghi vào JBZUniversalTester.cfg.",
            "CHỈNH TEM",
            MessageBoxImage.Information);
    }

    private void PreviewLabel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LabelPrintRequest request = BuildSettingsLabelRequest("PREVIEW");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding encoding = Encoding.GetEncoding(
                request.Profile.EncodingName,
                EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            string extension = request.Profile.Mode == LabelPrintMode.RawZpl ? ".zpl" : ".txt";
            string directory = Path.Combine(Path.GetTempPath(), "JBZUniversalTester", "LabelPreview");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"PREVIEW_{SafeFileName(request.Profile.Id)}_LOT{request.Data.LotNo}{extension}");
            File.WriteAllBytes(path, encoding.GetBytes(request.Payload));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, "XEM TRƯỚC TEM", MessageBoxImage.Warning);
        }
    }

    private async void TestPrintLabel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LabelPrintRequest request = BuildSettingsLabelRequest("TEST-PRINT");
            if (_main is null)
                throw new InvalidOperationException("Trang Cài đặt chưa được nối với chương trình chính.");
            LabelPrintTransportResult result = await _main.Test.PrintSettingsLabelAsync(request);
            ShowMessage(
                "TEST PRINT - không tăng LOT/production.\n\n" + result.Message,
                "IN THỬ TEM",
                result.Printed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, "IN THỬ TEM", MessageBoxImage.Warning);
        }
    }

    private LabelPrintRequest BuildSettingsLabelRequest(string purpose)
    {
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

        string path = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured));
        if (!File.Exists(path))
            throw new FileNotFoundException("Không tìm thấy file template label.", path);
        return path;
    }

    private static string SafeFileName(string value)
    {
        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
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
                options.Insert(0, new ComPortOption(savedPort, $"{savedPort} - chưa kết nối"));
            }

            options.Insert(0, new ComPortOption(string.Empty, "Không dùng COM / dùng Windows printer"));
            PrinterComComboBox.ItemsSource = options;
            PrinterComComboBox.SelectedValue = savedPort;

            string savedWaterProofPort = _vm.Settings.WaterProofMachine.PortName?.Trim() ?? string.Empty;
            WaterProofComComboBox.ItemsSource = ports;
            WaterProofComComboBox.Text = savedWaterProofPort;
        }
        catch (Exception ex)
        {
            if (IsReleased || generation != Volatile.Read(ref _portRefreshGeneration))
                return;

            PrinterComComboBox.ItemsSource = new[] { new ComPortOption(string.Empty, "Không dùng COM") };
            PrinterComComboBox.SelectedIndex = 0;
            WaterProofComComboBox.ItemsSource = Array.Empty<string>();
            ShowMessage(
                $"Không thể quét cổng COM.\n\n{ex.Message}",
                "Cổng COM",
                MessageBoxImage.Warning);
        }
    }

    private void RefreshPrinterConnectionStatus()
    {
        if (_main?.Test.IsLabelPrinterConnected == true)
        {
            PrinterConnectionStatusText.Text = $"ĐÃ NỐI {_main.Test.LabelPrinterConnectedPort}";
            return;
        }

        PrinterConnectionStatusText.Text = string.IsNullOrWhiteSpace(_vm.Settings.Label.PrinterCom)
            ? "CHƯA CHỌN COM"
            : "CHƯA KẾT NỐI";
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PrinterComComboBox
                .GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)
                ?.UpdateSource();
            WaterProofComComboBox
                .GetBindingExpression(ComboBox.TextProperty)
                ?.UpdateSource();

            if (!ValidateSettings(out string error))
            {
                ShowMessage(error, "Cấu hình chưa hợp lệ", MessageBoxImage.Warning);
                return;
            }

            // Đồng bộ CardCount compatibility từ BoardCapacity ngay trước save.
            BoardCapacity capacity = BoardCapacity.FromSettings(_vm.Settings);
            _vm.Settings.CardCount = capacity.ScanCardCount;
            _vm.Save();

            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.ToString(), "Không thể lưu cài đặt", MessageBoxImage.Error);
        }
    }

    public void SetManualRuntimeActive(bool active) =>
        _vm.SetManualRuntimeActive(active);

    public async Task ReleaseManualOutputsAsync()
    {
        if (_main?.Test.IsManualModeActive == true)
            await _main.Test.ExitManualModeAsync();
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

        _vm.Settings.StampDelay = $"{_vm.Settings.Relay1JigPulseMs},{_vm.Settings.Relay2MarkingPulseMs}"; // compatibility

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

            if (_vm.Settings.WaterProofMachine.BaudRate <= 0)
            {
                error = "Baudrate máy kín nước phải lớn hơn 0.";
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
