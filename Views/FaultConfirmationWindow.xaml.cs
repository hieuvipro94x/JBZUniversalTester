using System.Windows;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Views;

public partial class FaultConfirmationWindow : Window
{
    private readonly string? _requiredDiscardPassword;
    private bool _authorized;

    public FaultConfirmationWindow(
        IReadOnlyList<FaultDetail> faults,
        string footer,
        string? requiredDiscardPassword = null,
        string? windowHeader = null)
    {
        InitializeComponent();

        _requiredDiscardPassword = requiredDiscardPassword;
        if (!string.IsNullOrWhiteSpace(windowHeader))
        {
            Title = windowHeader;
            WindowHeaderText.Text = windowHeader;
        }

        if (requiredDiscardPassword is not null)
        {
            DiscardPasswordPanel.Visibility = Visibility.Visible;
            Loaded += (_, _) => DiscardPasswordBox.Focus();
            Closing += PreventUnauthorizedClose;
        }

        IReadOnlyList<OperatorFaultDisplay> displays = faults
            .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
            .ThenBy(fault => fault.ActualSourceIo ?? int.MaxValue)
            .ThenBy(fault => fault.ActualTargetIo ?? int.MaxValue)
            .Select(FaultDisplayFormatter.FormatOperator)
            .ToArray();

        FaultItemsControl.ItemsSource = displays;
        FooterText.Text = footer ?? string.Empty;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_requiredDiscardPassword is not null)
        {
            if (string.IsNullOrEmpty(_requiredDiscardPassword))
            {
                DiscardPasswordErrorText.Text =
                    "Chưa cài Mật khẩu thùng lỗi trong Cài đặt Production.";
                DiscardPasswordErrorText.Visibility = Visibility.Visible;
                return;
            }

            if (!JBZUniversalTester.Services.AdminAuthenticationService.Verify(
                    _requiredDiscardPassword,
                    DiscardPasswordBox.Password))
            {
                DiscardPasswordErrorText.Text = "Mật khẩu xử lý hàng lỗi không đúng.";
                DiscardPasswordErrorText.Visibility = Visibility.Visible;
                DiscardPasswordBox.SelectAll();
                DiscardPasswordBox.Focus();
                return;
            }
        }

        _authorized = true;
        DialogResult = true;
        Close();
    }

    private void PreventUnauthorizedClose(
        object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (!_authorized && IsVisible)
            e.Cancel = true;
    }
}
