using System.Windows;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Views;

public partial class FaultConfirmationWindow : Window
{
    public FaultConfirmationWindow(IReadOnlyList<FaultDetail> faults, string footer)
    {
        InitializeComponent();

        IReadOnlyList<OperatorFaultDisplay> displays = faults
            .Select(FaultDisplayFormatter.FormatOperator)
            .ToArray();

        SummaryText.Text = displays.Count == 0
            ? "KIỂM TRA SẢN PHẨM"
            : displays[0].Title;
        FaultItemsControl.ItemsSource = displays;
        FooterText.Text = footer ?? string.Empty;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
