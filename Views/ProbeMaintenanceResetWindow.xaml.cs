using System.Windows;
using System.Windows.Input;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public partial class ProbeMaintenanceResetWindow : Window
{
    private readonly Func<string, Task<(bool Reset, string Message)>> _confirm;
    private bool _confirming;
    private readonly bool _replacementRequired;
    private bool _resetSucceeded;
    public ProbeMaintenanceResetWindow(
        string partNumber,
        string modelName,
        long currentCycles,
        long replacementThreshold,
        Func<string, Task<(bool Reset, string Message)>> confirm)
    {
        _confirm = confirm;
        _replacementRequired = currentCycles >= Math.Max(1, replacementThreshold);
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;
        if (_replacementRequired)
        {
            WindowStyle = WindowStyle.None;
            CancelResetButton.IsCancel = false;
            CancelResetButton.Visibility = Visibility.Collapsed;
        }
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
        Closing += (_, e) => { if (_confirming || (_replacementRequired && !_resetSucceeded)) e.Cancel = true; };
        PreviewKeyDown += (_, e) =>
        {
            if (_replacementRequired && !_resetSucceeded &&
                (e.Key == Key.Escape || (e.SystemKey == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)))
                e.Handled = true;
        };
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
                _resetSucceeded = true;
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
