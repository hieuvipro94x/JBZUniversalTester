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

    public event EventHandler? SettingsSaved;
    public event EventHandler? RequestClose;

    public ProductionSettingsPage()
        : this(null)
    {
    }

    public ProductionSettingsPage(MainViewModel? main)
    {
        _main = main;
        _vm = new ProductionSettingsViewModel(main?.Test);
        _labelSettingsPasswordAtOpen = _vm.Settings.Password ?? string.Empty;
        InitializeComponent();
        DataContext = _vm;
        InitializeComboBoxItems();
        RefreshPrinterPorts();
        RefreshWaterProofPorts();
        RefreshPrinterConnectionStatus();
        SetLabelSettingsUnlocked(string.IsNullOrEmpty(_labelSettingsPasswordAtOpen));
    }

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
        // tỷ lệ 28/28/44. Khi hẹp hơn 1240px, hai panel nhỏ nằm trên và panel
        // TEM/ĐIỆN TRỞ xuống hàng để chữ và control không bị ép mất.
        double available = Math.Max(760, e.NewSize.Width - 34);
        if (available >= 1240)
        {
            double content = available - 30;
            IoSettingsPanel.Width = Math.Floor(content * 0.28);
            RelaySettingsPanel.Width = Math.Floor(content * 0.28);
            LabelSettingsPanel.Width = Math.Max(
                500,
                content - IoSettingsPanel.Width - RelaySettingsPanel.Width);
            return;
        }

        double halfPanel = Math.Max(360, Math.Floor((available - 20) / 2));
        IoSettingsPanel.Width = halfPanel;
        RelaySettingsPanel.Width = halfPanel;
        LabelSettingsPanel.Width = Math.Max(540, available - 10);
    }

    private void InitializeComboBoxItems()
    {
        CardIoComboBox.ItemsSource = Enumerable
            .Range(1, BoardCapacity.MaxExpansionModuleCount)
            .Select(n => new CardIoOption(
                n,
                $"{n} card / {n * BoardCapacity.IoPerExpansionModule} IO"))
            .ToArray();

        if (_vm.Settings.ExpansionCardCount <= 0)
        {
            _vm.Settings.ExpansionCardCount =
                BoardIoDecoder.ExpansionCardCountFromScanCards(_vm.Settings.CardCount);
        }

        _vm.Settings.ExpansionCardCount = Math.Clamp(
            _vm.Settings.ExpansionCardCount,
            1,
            BoardCapacity.MaxExpansionModuleCount);

        BoardCapacity capacity = BoardCapacity.FromSettings(_vm.Settings);
        _vm.Settings.CardCount = capacity.ScanCardCount;

        IoConfirm1ComboBox.ItemsSource = Enumerable.Range(0, 128).ToArray();
        IoConfirmNComboBox.ItemsSource = Enumerable.Range(0, 32).ToArray();
    }

    private void RefreshPrinterPorts_Click(object sender, RoutedEventArgs e) => RefreshPrinterPorts();

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
            PrinterConnectionStatusText.Text = result.Connected ? $"ĐÃ NỐI\n{portName}" : "KẾT NỐI LỖI";

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
            string path = ResolveConfiguredLabelTemplatePath();
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, "CHỈNH TEM", MessageBoxImage.Warning);
        }
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
            string directory = Path.Combine(AppContext.BaseDirectory, "Data", "Labels");
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
        string path = string.IsNullOrWhiteSpace(configured)
            ? LabelProfileResolver.ResolveBuiltInTemplatePath(_vm.Settings.Label.TemplateType)
            : Path.GetFullPath(Path.IsPathRooted(configured)
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

    private void RefreshPrinterPorts()
    {
        try
        {
            string savedPort = _vm.Settings.Label.PrinterCom?.Trim() ?? string.Empty;
            List<ComPortOption> options = SerialPort.GetPortNames()
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetComPortNumber)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
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
        }
        catch (Exception ex)
        {
            PrinterComComboBox.ItemsSource = new[] { new ComPortOption(string.Empty, "Không dùng COM") };
            PrinterComComboBox.SelectedIndex = 0;
            ShowMessage(
                $"Không thể quét cổng COM.\n\n{ex.Message}",
                "Cổng COM in tem",
                MessageBoxImage.Warning);
        }
    }

    private void RefreshPrinterConnectionStatus()
    {
        if (_main?.Test.IsLabelPrinterConnected == true)
        {
            PrinterConnectionStatusText.Text = $"ĐÃ NỐI\n{_main.Test.LabelPrinterConnectedPort}";
            return;
        }

        PrinterConnectionStatusText.Text = string.IsNullOrWhiteSpace(_vm.Settings.Label.PrinterCom)
            ? "CHƯA CHỌN COM"
            : "CHƯA KẾT NỐI";
    }

    private void RefreshWaterProofPorts_Click(object sender, RoutedEventArgs e) => RefreshWaterProofPorts();

    private void RefreshWaterProofPorts()
    {
        try
        {
            string savedPort = _vm.Settings.WaterProofMachine.PortName?.Trim() ?? string.Empty;
            string[] ports = SerialPort.GetPortNames()
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetComPortNumber)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            WaterProofComComboBox.ItemsSource = ports;
            WaterProofComComboBox.Text = savedPort;
        }
        catch (Exception ex)
        {
            WaterProofComComboBox.ItemsSource = Array.Empty<string>();
            ShowMessage(
                $"Không thể quét cổng COM máy kín nước.\n\n{ex.Message}",
                "UART/RS232 kín nước",
                MessageBoxImage.Warning);
        }
    }

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

            ShowMessage(
                "Đã lưu cấu hình.",
                "JBZ",
                MessageBoxImage.Information);

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

        if (_vm.Settings.ExpansionCardCount is < 1 or > BoardCapacity.MaxExpansionModuleCount)
        {
            error = $"Card mở rộng phải từ 1 đến {BoardCapacity.MaxExpansionModuleCount}.";
            return false;
        }

        if (_vm.Settings.StartCardNumber is < 1 or > BoardCapacity.MaxPhysicalCardCount)
        {
            error = $"Số card bắt đầu phải từ 1 đến {BoardCapacity.MaxPhysicalCardCount}.";
            return false;
        }

        BoardCapacity capacity = BoardCapacity.FromSettings(_vm.Settings);
        if (!capacity.IsRangeWithinSystem)
        {
            error =
                "Cấu hình card vượt phạm vi phần cứng hiện tại.\n\n" +
                $"Start card: {capacity.StartCardNumber}\n" +
                $"Card vật lý active: {capacity.PhysicalCardCount}\n" +
                $"Giới hạn card vật lý: {BoardCapacity.MaxPhysicalCardCount}.";
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
        Window? owner = HostWindow;
        if (owner is not null)
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
    }

    private sealed record CardIoOption(int ExpansionCardCount, string Display);
    private sealed record ComPortOption(string PortName, string Display);
}
