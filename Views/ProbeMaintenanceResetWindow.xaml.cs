using System.Windows;
using System.Windows.Input;

namespace JBZUniversalTester.Views;

public partial class ProbeMaintenanceResetWindow : Window
{
    public ProbeMaintenanceResetWindow(
        string partNumber,
        string modelName,
        long currentCycles,
        long replacementThreshold)
    {
        InitializeComponent();
        ModelText.Text = $"Mã hàng: {partNumber}  •  Model: {modelName}";
        CounterText.Text =
            $"Chu kỳ hiện tại: {currentCycles:N0}\n" +
            $"Chu kỳ thay thế: {replacementThreshold:N0}";
        Loaded += (_, _) => AdminPasswordBox.Focus();
    }

    public string AdminPassword => AdminPasswordBox.Password;

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void AdminPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            DialogResult = true;
    }
}
