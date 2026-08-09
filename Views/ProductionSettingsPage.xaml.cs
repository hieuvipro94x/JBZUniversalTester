using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
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
    private readonly ProductionSettingsViewModel _vm = new();

    public event EventHandler? SettingsSaved;
    public event EventHandler? RequestClose;

    public ProductionSettingsPage()
    {
        InitializeComponent();
        DataContext = _vm;
        InitializeComboBoxItems();
        RefreshPrinterPorts();
    }

    private Window? HostWindow => Window.GetWindow(this) ?? Application.Current?.MainWindow;

    private void InitializeComboBoxItems()
    {
        CardIoComboBox.ItemsSource = Enumerable
            .Range(1, BoardCapacity.MaxExpansionModuleCount)
            .Select(n => new CardIoOption(
                n,
                $"{n} card mở rộng - {n * BoardCapacity.IoPerExpansionModule} IO " +
                $"({n * BoardCapacity.PhysicalCardsPerExpansionModule} card vật lý)"))
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
                "Đã lưu toàn bộ cấu hình.\n\n" +
                $"{Path.Combine(AppContext.BaseDirectory, "production.settings.json")}\n" +
                $"{Path.Combine(AppContext.BaseDirectory, "UniversalTester.cfg")}\n\n" +
                $"Board capacity: {capacity}",
                "JBZ",
                MessageBoxImage.Information);

            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.ToString(), "Không thể lưu cài đặt", MessageBoxImage.Error);
        }
    }

    private bool ValidateSettings(out string error)
    {
        if (_vm.Settings.LotNo < 0)
        {
            error = "LOTNO phải là số nguyên từ 0 trở lên.";
            return false;
        }

        if (_vm.MasterFaultRequiredCount is < 1 or > 99)
        {
            error = "Số lỗi Master phải từ 1 đến 99 điểm lỗi duy nhất.";
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

        foreach (ResistanceChannelSetting channel in _vm.ResistanceChannels)
        {
            if (!channel.Enabled)
                continue;

            if (channel.Channel is < 1 or > 5)
            {
                error = $"Kênh của {channel.Name} phải nằm trong khoảng 1 đến 5.";
                return false;
            }

            if (channel.MinOhm < 0 || channel.MaxOhm < 0 || channel.MinOhm > channel.MaxOhm)
            {
                error = $"{channel.Name}: Min Ω phải <= Max Ω và không được âm.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        RequestClose?.Invoke(this, EventArgs.Empty);

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
