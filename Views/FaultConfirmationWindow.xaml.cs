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
        Loaded += PositionBelowFaultRows;
    }

    private void PositionBelowFaultRows(object sender, RoutedEventArgs e)
    {
        if (Owner is null)
            return;

        Owner.UpdateLayout();
        var source = PresentationSource.FromVisual(Owner);
        if (source?.CompositionTarget is null)
            return;

        // PointToScreen returns physical pixels; Window positions use WPF units.
        Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
        Point ownerTop = fromDevice.Transform(Owner.PointToScreen(new Point(0, 0)));
        double bottom = ownerTop.Y + Owner.ActualHeight;
        double desiredTop = ownerTop.Y + (Owner.ActualHeight - ActualHeight) / 2;
        DataGrid? grid = FindFaultGrid(Owner);
        if (grid is not null && grid.IsVisible)
        {
            Point gridTop = fromDevice.Transform(grid.PointToScreen(new Point(0, 0)));
            desiredTop = gridTop.Y + grid.ColumnHeaderHeight;
            foreach (object item in grid.Items)
            {
                if (item is not FaultRow row || row.ProductFaultType is not
                    (ProductFaultType.WrongWiring or ProductFaultType.ShortCircuit))
                    continue;
                if (grid.ItemContainerGenerator.ContainerFromItem(item) is not DataGridRow container ||
                    !container.IsVisible)
                    continue;
                Point rowTop = container.TranslatePoint(new Point(0, 0), grid);
                if (rowTop.Y >= grid.ActualHeight || rowTop.Y + container.ActualHeight <= grid.ColumnHeaderHeight)
                    continue;
                desiredTop = Math.Max(desiredTop,
                    gridTop.Y + Math.Min(grid.ActualHeight, rowTop.Y + container.ActualHeight));
            }
            desiredTop += 12;
            double availableHeight = bottom - desiredTop - 12;
            if (availableHeight >= MinHeight)
                MaxHeight = Math.Min(MaxHeight, availableHeight);
            UpdateLayout();
        }

        Left = ownerTop.X + Math.Max(0, (Owner.ActualWidth - ActualWidth) / 2);
        Top = Math.Max(ownerTop.Y, Math.Min(desiredTop, bottom - ActualHeight - 12));
    }

    private static DataGrid? FindFaultGrid(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is DataGrid { Name: "FaultGrid" } grid)
                return grid;
            DataGrid? found = FindFaultGrid(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    private void FaultSummary_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock text || text.DataContext is not FaultItem item)
            return;

        text.Inlines.Clear();
        FaultDetail fault = item.Fault;
        if (fault.Type != ProductFaultType.WrongWiring)
        {
            text.Inlines.Add(new Run(item.Summary));
            return;
        }

        int[] expected = [fault.ExpectedSourceIo ?? 0, fault.ExpectedTargetIo ?? 0];
        int unexpectedIo = new[] { fault.ActualSourceIo ?? 0, fault.ActualTargetIo ?? 0 }
            .FirstOrDefault(io => io > 0 && !expected.Contains(io));
        PinRecord? sourceRecord = _model?.Pins.FirstOrDefault(pin =>
            pin.IoNumber == fault.ExpectedSourceIo &&
            (string.IsNullOrWhiteSpace(fault.ConnectorFrom) ||
             string.Equals(pin.Connector, fault.ConnectorFrom, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(fault.PinFrom) || pin.PinNumber == fault.PinFrom));
        sourceRecord ??= _model?.Pins.FirstOrDefault(pin => pin.IoNumber == fault.ExpectedSourceIo);

        bool unexpectedIsSource = unexpectedIo > 0 && fault.ActualSourceIo == unexpectedIo;
        string actualConnector = unexpectedIsSource ? fault.ActualConnectorFrom : fault.ActualConnectorTo;
        string actualPin = unexpectedIo == 0
            ? string.Empty
            : unexpectedIsSource ? fault.ActualPinFrom : fault.ActualPinTo;
        PinRecord? actualRecord = _model?.Pins.FirstOrDefault(pin =>
            unexpectedIo > 0 && pin.IoNumber == unexpectedIo &&
            (string.IsNullOrWhiteSpace(actualConnector) ||
             string.Equals(pin.Connector, actualConnector, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(actualPin) || pin.PinNumber == actualPin));
        actualRecord ??= _model?.Pins.FirstOrDefault(pin => unexpectedIo > 0 && pin.IoNumber == unexpectedIo);
        actualPin = string.IsNullOrWhiteSpace(actualPin)
            ? actualRecord?.PinNumber ?? string.Empty
            : actualPin.Trim();

        string sourceWire = sourceRecord?.WireName?.Trim() is { Length: > 0 } wireName
            ? wireName
            : fault.WireName.Trim();
        string sourceColor = sourceRecord?.Color?.Trim() is { Length: > 0 } wireColor
            ? wireColor
            : fault.WireColor.Trim();
        string sourcePin = sourceRecord?.PinNumber?.Trim() is { Length: > 0 } pinNumber
            ? pinNumber
            : fault.PinFrom.Trim();
        string actualWire = actualRecord?.WireName?.Trim() ?? string.Empty;
        string actualColor = actualRecord?.Color?.Trim() ?? string.Empty;
        AddWire(text, sourceWire, sourceColor, sourcePin);

        if (!string.IsNullOrWhiteSpace(actualWire) &&
            !string.Equals(sourceWire, actualWire, StringComparison.OrdinalIgnoreCase))
        {
            text.Inlines.Add(new Run(" cắm nhầm với "));
            AddWire(text, actualWire, actualColor, actualPin);
            return;
        }

        if (!string.IsNullOrWhiteSpace(actualPin))
            text.Inlines.Add(new Run($" cắm nhầm lỗ chân {actualPin}"));
        else
            text.Inlines.Add(new Run(" cắm nhầm vị trí chưa xác định"));
    }

    private static void AddWire(TextBlock text, string wireName, string color, string housingPosition)
    {
        text.Inlines.Add(new Run(string.IsNullOrWhiteSpace(wireName) ? "Mã dây chưa xác định" : wireName)
        {
            Foreground = Brushes.Red
        });
        if (!string.IsNullOrWhiteSpace(color))
        {
            text.Inlines.Add(new Run(" màu "));
            AddColorCode(text, color);
        }
        if (!string.IsNullOrWhiteSpace(housingPosition))
            text.Inlines.Add(new Run($" vị trí Housing: {housingPosition}"));
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
