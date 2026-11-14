using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Win32;

namespace JBZUniversalTester.Views;

/// <summary>
/// V12.9: lịch sử là một page trong MainWindow, không phải Window/ShowDialog.
/// </summary>
public partial class HistoryPage : UserControl
{
    private readonly string _historyPath;
    private readonly object _storeGate = new();
    private TestHistoryStore? _store;
    private int _reloadGeneration;
    private const int PageSize = 200;
    private readonly ObservableCollection<TestHistoryRecord> _records = [];
    private readonly Dictionary<string, long> _passOrdinalsByProduct = new(StringComparer.Ordinal);
    private HistorySearchCriteria? _activeCriteria;
    private HistorySummary _summary = new(0, 0, 0, 0, 0, 0);
    private bool _loadingPage;
    private bool _released;
    private bool _searching;
    private bool _exporting;

    public event EventHandler? RequestClose;

    public HistoryPage()
    {
        InitializeComponent();
        DataContext = this;
        _historyPath = RuntimePaths.DatabaseFile;

        SetDefaultFilters();
        HistoryGrid.ItemsSource = _records;
        Loaded += HistoryPage_Loaded;
    }

    private async void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HistoryPage_Loaded;
        try
        {
            IReadOnlyList<HistoryPartOption> parts = await Task.Run(() => GetStore().GetHistoryPartOptions());
            if (_released) return;
            PartComboBox.ItemsSource = parts;
            PartComboBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"History part list failed: {ex}");
        }
        if (!_released) Reload();
    }

    public void ReleasePageResources()
    {
        _released = true;
        Loaded -= HistoryPage_Loaded;
        Interlocked.Increment(ref _reloadGeneration);
        _records.Clear();
        HistoryGrid.ItemsSource = null;
        DataContext = null;
    }

    private Window? HostWindow => Window.GetWindow(this) ?? Application.Current?.MainWindow;

    private void SetDefaultFilters()
    {
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
        ToDatePicker.SelectedDate = DateTime.Today;
        LotTextBox.Text = string.Empty;
        if (PartComboBox is not null)
            PartComboBox.SelectedIndex = 0;
        ResultComboBox.SelectedIndex = 0;
        InspectionTypeComboBox.SelectedIndex = 0;
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
        if (_released || _searching) return;
        HistorySearchCriteria criteria;
        try { criteria = CreateSearchCriteria(); }
        catch (ArgumentException ex) { ShowMessage(ex.Message, "KIỂM TRA BỘ LỌC", MessageBoxImage.Warning); return; }
        _searching = true;
        SetExportButtons();
        LoadMoreButton.IsEnabled = false;
        int generation = Interlocked.Increment(ref _reloadGeneration);
        try
        {
            _activeCriteria = null;
            _records.Clear();
            _passOrdinalsByProduct.Clear();
            SummaryText.Text = "Đang đọc lịch sử...";
            Task<HistorySummary> summaryTask = Task.Run(() => GetStore().GetHistorySummary(criteria));
            Task<IReadOnlyList<TestHistoryRecord>> pageTask = Task.Run(() =>
                GetStore().Search(criteria with { MaxRows = PageSize, Offset = 0 }));
            await Task.WhenAll(summaryTask, pageTask);
            if (generation != Volatile.Read(ref _reloadGeneration))
                return;

            _summary = summaryTask.Result;
            IReadOnlyList<TestHistoryRecord> rows = pageTask.Result;
            _activeCriteria = criteria;
            HistoryPresentation.AssignProductOrdinals(rows, _passOrdinalsByProduct);
            foreach (TestHistoryRecord row in rows)
                _records.Add(row);

            long productCount = _summary.ProductTotal;
            long pass = _summary.ProductPass;
            long fail = _summary.ProductFail;
            long master = _summary.MasterTotal;
            long leakRetest = _summary.LeakRetestTotal;

            TotalCountText.Text = productCount.ToString("N0");
            PassCountText.Text = pass.ToString("N0");
            FailCountText.Text = fail.ToString("N0");
            LoadMoreButton.IsEnabled = _records.Count < _summary.Total;

            SummaryText.Text =
                $"Đang hiển thị {_records.Count:N0} / {_summary.Total:N0} bản ghi | " +
                $"SẢN PHẨM {productCount:N0} (PASS {pass:N0} / FAIL {fail:N0}, {_summary.ProductPassRate:0.00}%) | " +
                $"기밀 검사 시작 {leakRetest:N0} | MASTER {master:N0}";
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"History search failed: {ex}");
            if (_released || generation != Volatile.Read(ref _reloadGeneration)) return;
            TotalCountText.Text = "0";
            PassCountText.Text = "0";
            FailCountText.Text = "0";
            SummaryText.Text = "Không thể đọc dữ liệu lịch sử.";
            ShowMessage("Chưa đọc được lịch sử kiểm tra. Vui lòng thử lại.", "CHƯA ĐỌC ĐƯỢC LỊCH SỬ", MessageBoxImage.Error);
        }
        finally
        {
            _searching = false;
            if (!_released) SetExportButtons();
        }
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (_released || _searching || _loadingPage || _activeCriteria is null || _records.Count == 0 || _records.Count >= _summary.Total)
            return;

        _loadingPage = true;
        int generation = Volatile.Read(ref _reloadGeneration);
        LoadMoreButton.IsEnabled = false;
        try
        {
            TestHistoryRecord cursor = _records[^1];
            HistorySearchCriteria pageCriteria = _activeCriteria with
            {
                MaxRows = PageSize,
                Offset = 0,
                BeforeHistoryAt = cursor.EffectiveTestStartedAt,
                BeforeId = cursor.Id
            };
            IReadOnlyList<TestHistoryRecord> page = await Task.Run(() => GetStore().Search(pageCriteria));
            if (generation != Volatile.Read(ref _reloadGeneration))
                return;

            HistoryPresentation.AssignProductOrdinals(page, _passOrdinalsByProduct);
            foreach (TestHistoryRecord row in page)
                _records.Add(row);
            SummaryText.Text = $"Đang hiển thị {_records.Count:N0} / {_summary.Total:N0} bản ghi";
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"History next page failed: {ex}");
            if (!_released && generation == Volatile.Read(ref _reloadGeneration))
                ShowMessage("Chưa đọc được trang lịch sử tiếp theo. Vui lòng thử lại.",
                    "CHƯA ĐỌC ĐƯỢC LỊCH SỬ", MessageBoxImage.Warning);
        }
        finally
        {
            _loadingPage = false;
            if (!_released && generation == Volatile.Read(ref _reloadGeneration))
                LoadMoreButton.IsEnabled = _records.Count < _summary.Total;
        }
    }

    private void HistoryGrid_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        // Keep the full-value bindings; only shape the hovered cell's text when
        // WPF is about to open its tooltip. No scroll/layout observers are needed.
        e.Handled = e.OriginalSource is not TextBlock text || !IsHistoryTextClipped(text);
    }

    internal static bool IsHistoryTextClipped(TextBlock text)
    {
        if (string.IsNullOrEmpty(text.Text) || text.ActualWidth <= 0 || text.ActualHeight <= 0)
            return false;

        var formatted = new FormattedText(text.Text, text.Language.GetSpecificCulture(), text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
            Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
        double width = formatted.WidthIncludingTrailingWhitespace;
        double available = Math.Max(0, text.ActualWidth - text.Padding.Left - text.Padding.Right);
        double offset = text.TextAlignment switch
        {
            TextAlignment.Center => Math.Max(0, (available - width) / 2),
            TextAlignment.Right => Math.Max(0, available - width),
            _ => 0
        };
        var content = new Rect(text.Padding.Left + offset, text.Padding.Top, width, formatted.Height);
        var visible = new Rect(new Point(), text.RenderSize);
        // A cell can fit its text yet be partly hidden by the horizontal viewport
        // or frozen columns. Intersect ancestor clips in this TextBlock's space.
        for (Visual? ancestor = text; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor) as Visual)
        {
            GeneralTransform? transform = ancestor == text ? null : ancestor.TransformToDescendant(text);
            if (ancestor is FrameworkElement element)
            {
                if (element.ClipToBounds || element is ScrollContentPresenter || element is DataGridCell)
                {
                    var bounds = new Rect(new Point(), element.RenderSize);
                    visible.Intersect(transform?.TransformBounds(bounds) ?? bounds);
                }
                Geometry? layoutClip = LayoutInformation.GetLayoutClip(element);
                if (layoutClip is not null)
                    visible.Intersect(transform?.TransformBounds(layoutClip.Bounds) ?? layoutClip.Bounds);
            }
            Geometry? clip = (ancestor as UIElement)?.Clip ?? VisualTreeHelper.GetClip(ancestor);
            if (clip is not null)
                visible.Intersect(transform?.TransformBounds(clip.Bounds) ?? clip.Bounds);
        }
        const double tolerance = 0.5; // Device-independent layout rounding.
        return visible.IsEmpty || content.Left < visible.Left - tolerance || content.Top < visible.Top - tolerance ||
               content.Right > visible.Right + tolerance || content.Bottom > visible.Bottom + tolerance;
    }

    private HistorySearchCriteria CreateSearchCriteria()
    {
        DateTime? from = FromDatePicker.SelectedDate?.Date;
        DateTime? end = ToDatePicker.SelectedDate?.Date;
        if (from > end) throw new ArgumentException("Từ ngày phải nhỏ hơn hoặc bằng Đến ngày.");
        if (end == DateTime.MaxValue.Date) throw new ArgumentException("Ngày kết thúc vượt phạm vi cho phép.");
        DateTime? to = end?.AddDays(1);
        string lotText = LotTextBox.Text.Trim();
        long? lot = null;
        if (lotText.Length > 0)
        {
            if (!long.TryParse(lotText, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out long n))
                throw new ArgumentException("Số đếm PASS phải là số nguyên từ 0 trở lên.");
            lot = n;
        }
        string result = (ResultComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ALL";
        string inspectionType =
            (InspectionTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        return new HistorySearchCriteria(
            from,
            to,
            lot,
            PartComboBox.SelectedValue?.ToString()?.Trim() ?? string.Empty,
            result,
            InspectionType: inspectionType);
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e) => await ExportAsync(false);
    private async void ExportXlsx_Click(object sender, RoutedEventArgs e) => await ExportAsync(true);

    private void SetExportButtons()
    {
        CsvExportButton.IsEnabled = ExcelExportButton.IsEnabled =
            !_released && !_searching && !_exporting && _activeCriteria is not null;
    }

    internal HistorySearchCriteria CaptureExportCriteria()
    {
        if (_activeCriteria is null || CreateSearchCriteria() != _activeCriteria)
            throw new ArgumentException("Bộ lọc đã thay đổi. Bấm TÌM KIẾM trước khi xuất để file khớp với bảng lịch sử.");
        return _activeCriteria;
    }

    private async Task ExportAsync(bool excel)
    {
        if (_released || _searching || _exporting || _activeCriteria is null) return;
        HistorySearchCriteria criteria;
        try { criteria = CaptureExportCriteria(); }
        catch (ArgumentException ex) { ShowMessage(ex.Message, "KIỂM TRA BỘ LỌC", MessageBoxImage.Warning); return; }
        _exporting = true;
        SetExportButtons();
        int generation = Volatile.Read(ref _reloadGeneration);
        string previousSummary = SummaryText.Text;
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = excel ? "Xuất lịch sử Excel" : "Xuất lịch sử CSV",
                Filter = excel ? "Excel Workbook (*.xlsx)|*.xlsx" : "CSV (*.csv)|*.csv",
                FileName = $"JBZ_TestHistory_{DateTime.Now:yyyyMMdd_HHmmss}.{(excel ? "xlsx" : "csv")}",
                OverwritePrompt = true
            };
            bool? result = HostWindow is Window owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (result != true || _released) return;
            SummaryText.Text = "Đang xuất toàn bộ lịch sử theo bộ lọc...";
            int exportedCount = await Task.Run(() =>
            {
                IEnumerable<TestHistoryRecord> rows = GetStore().EnumerateForExport(criteria);
                return excel ? HistoryExportService.ExportXlsx(dialog.FileName, rows)
                    : HistoryExportService.ExportCsv(dialog.FileName, rows);
            });
            if (!_released && generation == Volatile.Read(ref _reloadGeneration))
                ShowMessage($"Đã xuất toàn bộ {exportedCount:N0} bản ghi theo bộ lọc.\n\n{dialog.FileName}",
                    "JBZ", MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"History export failed: {ex}");
            if (!_released && generation == Volatile.Read(ref _reloadGeneration))
                ShowMessage("Chưa xuất được lịch sử. File đích cũ được giữ nguyên. Vui lòng thử lại.",
                    "CHƯA XUẤT ĐƯỢC LỊCH SỬ", MessageBoxImage.Error);
        }
        finally
        {
            _exporting = false;
            if (!_released)
            {
                if (generation == Volatile.Read(ref _reloadGeneration)) SummaryText.Text = previousSummary;
                SetExportButtons();
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        RequestClose?.Invoke(this, EventArgs.Empty);

    private void ShowMessage(string message, string title, MessageBoxImage image)
    {
        if (_released) return;
        Window? owner = HostWindow;
        if (owner is not null)
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, image);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, image);
    }
}
