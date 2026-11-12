using System.ComponentModel;
using System.Globalization;
using System.Windows;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public sealed record ProductInitialPrintSetup(long StartLot, string TemplateType, bool PrintLabel = true);

public partial class ProductPrintSetupWindow : Window
{
    private readonly Func<ProductInitialPrintSetup, Task> _save;
    private bool _saving;

    public ProductPrintSetupWindow(ProductModel model, Func<ProductInitialPrintSetup, Task> save)
    {
        InitializeComponent();
        _save = save;
        MaxHeight = SystemParameters.WorkArea.Height;
        ProductText.Text = ProductionConfigService.GetLotProductKey(model);
        ModelText.Text = model.ModelName;
        TemplateComboBox.ItemsSource = new[] { LabelSettings.LargeTemplate, LabelSettings.LargeSqdzTemplate,
            LabelSettings.SmallTemplate, LabelSettings.SmallQrTemplate, LabelSettings.BcwTemplate };
        Loaded += (_, _) => StartLotTextBox.Focus();
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e) => await ConfirmAsync();

    private void PrintChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (TemplateOptionsPanel is not null)
            TemplateOptionsPanel.Visibility = PrintYesRadioButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ConfirmAsync()
    {
        if (_saving) return;
        ValidationText.Visibility = Visibility.Visible;
        if (!long.TryParse(StartLotTextBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out long start) ||
            start < 0 || start == long.MaxValue)
        {
            ValidationText.Text = "Nhập LOTNO bắt đầu hợp lệ, từ 0 trở lên.";
            StartLotTextBox.Focus();
            return;
        }
        if (PrintYesRadioButton.IsChecked != true && PrintNoRadioButton.IsChecked != true)
        {
            ValidationText.Text = "Chọn Có in tem hoặc Không in tem cho mã hàng này.";
            return;
        }
        if (PrintYesRadioButton.IsChecked == true && TemplateComboBox.SelectedItem is not string)
        {
            ValidationText.Text = "Vui lòng chọn loại tem cần in.";
            TemplateComboBox.Focus();
            return;
        }
        _saving = true;
        PrintYesRadioButton.IsEnabled = PrintNoRadioButton.IsEnabled = false;
        ConfirmButton.IsEnabled = CancelButton.IsEnabled = false;
        StartLotTextBox.IsEnabled = TemplateComboBox.IsEnabled = false;
        ValidationText.Visibility = Visibility.Collapsed;
        try
        {
            await _save(new(start, TemplateComboBox.SelectedItem as string ?? string.Empty, PrintYesRadioButton.IsChecked == true));
            _saving = false;
            PrintYesRadioButton.IsEnabled = PrintNoRadioButton.IsEnabled = true;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"First product print setup failed: {ex}");
            ValidationText.Text = "Chưa lưu được cấu hình. Vui lòng thử lại; mã hàng chưa được nạp.";
            ValidationText.Visibility = Visibility.Visible;
        }
        finally
        {
            _saving = false;
            PrintYesRadioButton.IsEnabled = PrintNoRadioButton.IsEnabled = true;
            ConfirmButton.IsEnabled = CancelButton.IsEnabled = true;
            StartLotTextBox.IsEnabled = TemplateComboBox.IsEnabled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_saving) e.Cancel = true;
    }
}
