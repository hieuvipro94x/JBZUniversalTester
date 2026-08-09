using System.Windows;

namespace JBZUniversalTester.Views;

public partial class FaultConfirmationWindow : Window
{
    public FaultConfirmationWindow(string title, string summary, string details)
    {
        InitializeComponent();
        FaultTitleText.Text = string.IsNullOrWhiteSpace(title) ? "KIỂM TRA MẠCH KHÔNG ĐẠT" : title;
        SummaryText.Text = summary ?? string.Empty;
        DetailsText.Text = details ?? string.Empty;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
