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

        SummaryText.Text = displays.Count == 1
            ? "PHAT HIEN 1 LOI"
            : $"PHAT HIEN {displays.Count} LOI";
        FaultItemsControl.ItemsSource = displays;
        FooterText.Text = footer ?? string.Empty;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
