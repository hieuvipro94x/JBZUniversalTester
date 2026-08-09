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

        if (displays.Count == 0)
        {
            displays =
            [
                new OperatorFaultDisplay(
                    "LỖI CHƯA PHÂN LOẠI",
                    [new FaultDisplayLine("Chi tiết", "KHÔNG CÓ DỮ LIỆU CHI TIẾT")])
            ];
        }

        SummaryText.Text = displays.Count == 1
            ? "PHÁT HIỆN 1 LỖI"
            : $"PHÁT HIỆN {displays.Count} LỖI";
        FaultItemsControl.ItemsSource = displays;
        FooterText.Text = footer ?? string.Empty;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
