using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Win32;

namespace JBZUniversalTester.Views;

/// <summary>
/// V12.9: lịch sử là một page trong MainWindow, không phải Window/ShowDialog.
/// </summary>
public partial class HistoryPage : UserControl
{
    private const int UiRowLimit = 2_000;
    private readonly string _historyPath;
    private readonly object _storeGate = new();
    private TestHistoryStore? _store;
    private IReadOnlyList<TestHistoryRecord> _records = [];
    private int _reloadGeneration;

    public event EventHandler? RequestClose;

    public HistoryPage(ProductionSettings settings)
    {
        InitializeComponent();
        DataContext = this;

        _historyPath = RuntimePaths.DatabaseFile;

        SetDefaultFilters();
        Loaded += HistoryPage_Loaded;
    }

    private void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HistoryPage_Loaded;
        Reload();
    }

    public void ReleasePageResources()
    {
        Loaded -= HistoryPage_Loaded;
        Interlocked.Increment(ref _reloadGeneration);
        HistoryGrid.ItemsSource = null;
        _records = [];
        DataContext = null;
    }

    private Window? HostWindow => Window.GetWindow(this) ?? Application.Current?.MainWindow;

    private void SetDefaultFilters()
    {
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
        ToDatePicker.SelectedDate = DateTime.Today;
        LotTextBox.Text = string.Empty;
        PartTextBox.Text = string.Empty;
        ResultComboBox.SelectedIndex = 0;
    }

    private void Search_Click(object sender, RoutedEventArgs e) => Reload();

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        SetDefaultFilters();
        Reload();
    }

    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        Reload();
        e.Handled = true;
    }

    private TestHistoryStore GetStore()
    {
        lock (_storeGate)
            return _store ??= new TestHistoryStore(_historyPath);
    }

    private async void Reload()
    {
        int generation = Interlocked.Increment(ref _reloadGeneration);
        try
        {
            HistorySearchCriteria criteria = CreateSearchCriteria(UiRowLimit);
            IReadOnlyList<TestHistoryRecord> rows = await Task.Run(() =>
                GetStore().Search(criteria));
            if (generation != Volatile.Read(ref _reloadGeneration))
                return;

            _records = rows;
            HistoryGrid.ItemsSource = rows;

            int productCount = rows.Count(row => !row.IsMasterRecord);
            int pass = rows.Count(row => !row.IsMasterRecord && row.Passed);
            int fail = productCount - pass;
            int master = rows.Count(row => row.IsMasterRecord);

            TotalCountText.Text = productCount.ToString("N0");
            PassCountText.Text = pass.ToString("N0");
            FailCountText.Text = fail.ToString("N0");

            SummaryText.Text =
                $"{rows.Count:N0} bản ghi hiển thị (tối đa {UiRowLimit:N0}) | SẢN PHẨM {productCount:N0} " +
                $"(PASS {pass:N0} / FAIL {fail:N0}) | MASTER {master:N0} | DB: {_historyPath}";
        }
        catch (Exception ex)
        {
            TotalCountText.Text = "0";
            PassCountText.Text = "0";
            FailCountText.Text = "0";
            SummaryText.Text = "Không thể đọc dữ liệu lịch sử.";
            ShowMessage(ex.ToString(), "Không thể đọc lịch sử", MessageBoxImage.Error);
        }
    }

    private HistorySearchCriteria CreateSearchCriteria(int maxRows)
    {
        DateTime? from = FromDatePicker.SelectedDate?.Date;
        DateTime? to = ToDatePicker.SelectedDate?.Date.AddDays(1).AddTicks(-1);
        long? lot = long.TryParse(LotTextBox.Text?.Trim(), out long n) ? n : null;
        string result = (ResultComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ALL";
        return new HistorySearchCriteria(
            from,
            to,
            lot,
            PartTextBox.Text?.Trim() ?? string.Empty,
            result,
            maxRows);
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_records.Count == 0)
        {
            ShowMessage("Không có dữ liệu để xuất.", "Lịch sử", MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Xuất lịch sử CSV",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"JBZ_TestHistory_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        bool? result = HostWindow is Window owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        if (result != true)
            return;

        HistorySearchCriteria criteria = CreateSearchCriteria(20_000);
        try
        {
            int exportedCount = await Task.Run(() =>
            {
                IReadOnlyList<TestHistoryRecord> rows = GetStore().SearchForExport(criteria);
                HistoryExportService.ExportCsv(dialog.FileName, rows);
                return rows.Count;
            });
            ShowMessage(
                $"Đã xuất toàn bộ {exportedCount:N0} bản ghi theo mã hàng và ngày/giờ.\n\n{dialog.FileName}",
                "JBZ",
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowMessage($"Không thể xuất CSV.\n\n{ex.Message}", "Lỗi xuất lịch sử", MessageBoxImage.Error);
        }
    }

    private async void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        if (_records.Count == 0)
        {
            ShowMessage("Không có dữ liệu để xuất.", "Lịch sử", MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Xuất lịch sử Excel theo mẫu chuẩn",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"JBZ_TestHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        bool? result = HostWindow is Window owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        if (result != true)
            return;

        HistorySearchCriteria criteria = CreateSearchCriteria(20_000);
        try
        {
            int exportedCount = await Task.Run(() =>
            {
                IReadOnlyList<TestHistoryRecord> rows = GetStore().SearchForExport(criteria);
                HistoryExportService.ExportXlsx(dialog.FileName, rows);
                return rows.Count;
            });
            ShowMessage(
                $"Đã xuất toàn bộ {exportedCount:N0} bản ghi theo mã hàng và ngày/giờ.\n\n{dialog.FileName}",
                "JBZ",
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowMessage($"Không thể xuất Excel.\n\n{ex.Message}", "Lỗi xuất lịch sử", MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        RequestClose?.Invoke(this, EventArgs.Empty);

    private void ShowMessage(string message, string title, MessageBoxImage image)
    {
        Window? owner = HostWindow;
        if (owner is not null)
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
    }
}
