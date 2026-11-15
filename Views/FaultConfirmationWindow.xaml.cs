using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public partial class FaultConfirmationWindow : Window
{
    private readonly string? _requiredDiscardPassword;
    private bool _authorized;
    private readonly string _language;
    private string T(string text) => TestViewText.Translate(text, _language);

    public FaultConfirmationWindow(
        IReadOnlyList<FaultDetail> faults,
        string footer,
        string? requiredDiscardPassword = null,
        string? windowHeader = null,
        ProductModel? model = null,
        string language = "vi")
    {
        _language = TestViewText.NormalizeLanguage(language);
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

        // Product details belong to the live TestView table and persisted history.
        // One notice per fault type keeps this acknowledgement short even for
        // hundreds of wrong-wire pairs or very long product metadata.
        FaultItemsControl.ItemsSource = faults
            .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
            .GroupBy(fault => fault.Type)
            .Select(group => T(group.Key switch
            {
                ProductFaultType.WrongWiring => "Đã có lỗi sai dây.\nVui lòng kiểm tra và xử lý.",
                ProductFaultType.ShortCircuit => "Đã có lỗi chập mạch.\nVui lòng kiểm tra và xử lý.",
                ProductFaultType.OpenCircuit => "Đã có lỗi đứt dây / tuột tuýt.\nVui lòng kiểm tra và xử lý.",
                ProductFaultType.ResistanceOutOfRange => "Đã có lỗi điện trở.\nVui lòng kiểm tra và xử lý.",
                ProductFaultType.WaterProofLeak => "Đã có lỗi kín nước.\nVui lòng kiểm tra và xử lý.",
                ProductFaultType.SystemDeviceError => group.Select(fault => fault.Message)
                    .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message))
                    ?? "Đã có lỗi thiết bị.\nVui lòng kiểm tra và xử lý.",
                _ => "Sản phẩm không đạt.\nVui lòng kiểm tra và xử lý."
            }))
            .ToArray();
        FooterText.Text = footer ?? string.Empty;
        Loaded += PositionBelowFaultRows;
        Loaded += (_, _) => TestViewLocalization.Apply(this, _language);
    }

    private void PositionBelowFaultRows(object sender, RoutedEventArgs e)
    {
        Window anchor = Owner ?? this;
        anchor.UpdateLayout();
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return;

        // PointToScreen returns physical pixels; Window positions use WPF units.
        Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
        var screen = System.Windows.Forms.Screen.FromHandle(
            new System.Windows.Interop.WindowInteropHelper(anchor).Handle);
        var workArea = new Rect(
            fromDevice.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top)),
            fromDevice.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom)));
        // Keep the footer outside the scrollable fault list, within this monitor's usable area.
        MinHeight = Math.Min(260, workArea.Height);
        MaxHeight = Math.Min(680, workArea.Height);
        Width = Math.Min(560, workArea.Width);
        UpdateLayout();
        Point ownerTop = fromDevice.Transform(anchor.PointToScreen(new Point(0, 0)));
        double bottom = Math.Min(workArea.Bottom, ownerTop.Y + anchor.ActualHeight);
        double desiredTop = ownerTop.Y + (anchor.ActualHeight - ActualHeight) / 2;
        DataGrid? grid = FindFaultGrid(anchor);
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

        Left = Math.Clamp(ownerTop.X + (anchor.ActualWidth - ActualWidth) / 2,
            workArea.Left, Math.Max(workArea.Left, workArea.Right - ActualWidth));
        Top = Math.Clamp(Math.Min(desiredTop, bottom - ActualHeight - 12),
            workArea.Top, Math.Max(workArea.Top, workArea.Bottom - ActualHeight));
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

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_requiredDiscardPassword is not null)
        {
            if (string.IsNullOrEmpty(_requiredDiscardPassword))
            {
                DiscardPasswordErrorText.Text =
                    T("Chưa cài Mật khẩu thùng lỗi trong Cài đặt Production.");
                DiscardPasswordErrorText.Visibility = Visibility.Visible;
                return;
            }

            if (!JBZUniversalTester.Services.AdminAuthenticationService.Verify(
                    _requiredDiscardPassword,
                    DiscardPasswordBox.Password))
            {
                DiscardPasswordErrorText.Text = T("Mật khẩu xử lý hàng lỗi không đúng.");
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
