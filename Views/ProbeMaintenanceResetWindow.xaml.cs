using System.Windows;
using System.Windows.Input;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public partial class ProbeMaintenanceResetWindow : Window
{
    private readonly Func<string, Task<(bool Reset, string Message)>> _confirm;
    private bool _confirming;
    public ProbeMaintenanceResetWindow(
        string partNumber,
        string modelName,
        long currentCycles,
        long replacementThreshold,
        Func<string, Task<(bool Reset, string Message)>> confirm)
    {
        _confirm = confirm;
        InitializeComponent();
        ModelText.Text = $"Mã hàng: {partNumber}\nModel: {modelName}";
        CurrentCyclesText.Text = currentCycles.ToString("N0");
        ThresholdText.Text = replacementThreshold.ToString("N0");
        Loaded += (_, _) => AdminPasswordBox.Focus();
        Loaded += (_, _) =>
        {
            string language = Owner is null ? "vi" : TestViewLocalization.GetLanguage(Owner);
            TestViewLocalization.Apply(this, language);
            ModelText.Text = $"{JBZUniversalTester.Services.TestViewText.Translate("Mã hàng: " + partNumber, language)}\n{JBZUniversalTester.Services.TestViewText.Translate("Model: " + modelName, language)}";
        };
        Closing += (_, e) => { if (_confirming) e.Cancel = true; };
        Closed += (_, _) => AdminPasswordBox.Clear();
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e) => await ConfirmAsync();

    private async Task ConfirmAsync()
    {
        if (_confirming) return;
        _confirming = true;
        ConfirmResetButton.IsEnabled = CancelResetButton.IsEnabled = false;
        string language = Owner is null ? "vi" : TestViewLocalization.GetLanguage(Owner);
        ValidationMessageText.Text = TestViewText.Translate("Đang kiểm tra mật khẩu Cài đặt...", language);
        try
        {
            (bool reset, string message) = await _confirm(AdminPasswordBox.Password);
            if (reset)
            {
                _confirming = false;
                DialogResult = true;
            }
            else
            {
                ValidationMessageText.Text = TestViewText.Translate(message, language);
                AdminPasswordBox.SelectAll();
                AdminPasswordBox.Focus();
            }
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Probe maintenance confirmation failed: {ex}");
            ValidationMessageText.Text = TestViewText.Translate("Chưa lưu được thay đổi Probe Pin. Vui lòng thử lại.", language);
        }
        finally
        {
            _confirming = false;
            ConfirmResetButton.IsEnabled = CancelResetButton.IsEnabled = true;
        }
    }

    private async void AdminPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ConfirmAsync();
        }
    }
}
