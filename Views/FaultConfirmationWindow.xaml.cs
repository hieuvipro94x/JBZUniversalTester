using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using JBZUniversalTester.Converters;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Views;

public partial class FaultConfirmationWindow : Window
{
    private readonly string? _requiredDiscardPassword;
    private readonly ProductModel? _model;
    private bool _authorized;

    private sealed record FaultItem(FaultDetail Fault, string Title, string Summary);

    public FaultConfirmationWindow(
        IReadOnlyList<FaultDetail> faults,
        string footer,
        string? requiredDiscardPassword = null,
        string? windowHeader = null,
        ProductModel? model = null)
    {
        InitializeComponent();

        _requiredDiscardPassword = requiredDiscardPassword;
        _model = model;
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

        FaultDetail[] orderedFaults = faults
            .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
            .ThenBy(fault => fault.ActualSourceIo ?? int.MaxValue)
            .ThenBy(fault => fault.ActualTargetIo ?? int.MaxValue)
            .ToArray();
        CompactOperatorFaultDisplay[] displays = orderedFaults
            .Select(FaultDisplayFormatter.FormatCompactOperator)
            .ToArray();

        FaultItemsControl.ItemsSource = orderedFaults
            .Select((fault, index) => new FaultItem(fault, displays[index].Title, displays[index].Summary))
            .ToArray();
        FooterText.Text = footer ?? string.Empty;
    }

    private void FaultSummary_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock text || text.DataContext is not FaultItem item)
            return;

        text.Inlines.Clear();
        FaultDetail fault = item.Fault;
        int[] expected = [fault.ExpectedSourceIo ?? 0, fault.ExpectedTargetIo ?? 0];
        int unexpectedIo = new[] { fault.ActualSourceIo ?? 0, fault.ActualTargetIo ?? 0 }
            .FirstOrDefault(io => io > 0 && !expected.Contains(io));
        if (fault.Type != ProductFaultType.WrongWiring || unexpectedIo == 0)
        {
            text.Inlines.Add(new Run(item.Summary));
            return;
        }

        bool unexpectedIsSource = fault.ActualSourceIo == unexpectedIo;
        string actualConnector = unexpectedIsSource ? fault.ActualConnectorFrom : fault.ActualConnectorTo;
        string actualPin = unexpectedIsSource ? fault.ActualPinFrom : fault.ActualPinTo;
        PinRecord? actualRecord = _model?.Pins.FirstOrDefault(pin =>
            pin.IoNumber == unexpectedIo &&
            (string.IsNullOrWhiteSpace(actualConnector) ||
             string.Equals(pin.Connector, actualConnector, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(actualPin) || pin.PinNumber == actualPin));
        actualRecord ??= _model?.Pins.FirstOrDefault(pin => pin.IoNumber == unexpectedIo);
        actualConnector = string.IsNullOrWhiteSpace(actualConnector)
            ? actualRecord?.Connector ?? string.Empty
            : actualConnector.Trim();
        actualPin = string.IsNullOrWhiteSpace(actualPin)
            ? actualRecord?.PinNumber ?? string.Empty
            : actualPin.Trim();

        string sourceConnector = fault.ConnectorFrom.Trim();
        string sourceColor = fault.WireColor.Trim();
        string actualColor = actualRecord?.Color?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceConnector) ||
            string.IsNullOrWhiteSpace(sourceColor) ||
            string.IsNullOrWhiteSpace(actualConnector))
        {
            text.Inlines.Add(new Run(item.Summary));
            return;
        }

        text.Inlines.Add(new Run(sourceConnector) { Foreground = Brushes.Red });
        text.Inlines.Add(new Run(" màu "));
        AddColorCode(text, sourceColor);

        bool wrongHole = !string.IsNullOrWhiteSpace(actualPin) &&
            (string.Equals(actualConnector, sourceConnector, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(actualConnector, fault.ConnectorTo, StringComparison.OrdinalIgnoreCase));
        if (wrongHole)
        {
            text.Inlines.Add(new Run($" cắm nhầm lỗ chân {actualPin}"));
            return;
        }

        text.Inlines.Add(new Run(" cắm nhầm với "));
        text.Inlines.Add(new Run(actualConnector) { Foreground = Brushes.Red });
        if (!string.IsNullOrWhiteSpace(actualColor))
        {
            text.Inlines.Add(new Run(" màu "));
            AddColorCode(text, actualColor);
        }
        if (!string.IsNullOrWhiteSpace(actualPin))
            text.Inlines.Add(new Run($" (chân {actualPin})"));
    }

    private static void AddColorCode(TextBlock text, string code)
    {
        Brush brush = WireColorToBrushConverter.ToBrush(code);
        var run = new Run(code) { Foreground = brush };
        if (code.Equals("W", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("WH", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("WHITE", StringComparison.OrdinalIgnoreCase))
            run.Background = Brushes.DimGray;
        text.Inlines.Add(run);
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
