using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public partial class ProductPickerWindow : Window
{
    private readonly string _root;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _searchTimer;
    private ICollectionView? _files;
    private string _appliedSearch = string.Empty;
    private bool _searchPending;
    private int _fileCount;
    private int _errorCount;
    private bool _closed;
    public string? SelectedFilePath { get; private set; }
    public bool BrowseFileRequested { get; private set; }

    public ProductPickerWindow(string root)
    {
        InitializeComponent();
        _searchTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _searchTimer.Tick += SearchTimer_Tick;
        _root = root;
        RootText.Text = $"Thư mục: {root} (bao gồm tất cả thư mục con)";
        Loaded += async (_, _) => { SearchBox.Focus(); await LoadFilesAsync(); };
        Closed += (_, _) =>
        {
            _closed = true;
            _searchTimer.Stop();
            _searchTimer.Tick -= SearchTimer_Tick;
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
    }

    private async Task LoadFilesAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Đang tìm mã hàng...";
        try
        {
            CancellationToken ct = _lifetime.Token;
            ProductFileCatalogResult result = await Task.Run(() => ProductFileCatalog.Read(_root, ct), ct);
            if (_closed) return;
            _fileCount = result.Files.Count;
            _errorCount = result.Errors.Count;
            foreach (string error in result.Errors)
                AsyncFileLogService.Current.Error($"PRODUCT_PICKER: {error}");
            _files = CollectionViewSource.GetDefaultView(result.Files);
            _searchTimer.Stop();
            _searchPending = false;
            _appliedSearch = SearchBox.Text.Trim();
            _files.Filter = item => item is ProductFileEntry file && ProductFileCatalog.Matches(file, _appliedSearch);
            ProductGrid.ItemsSource = _files;
            UpdateSearchResults();
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"PRODUCT_PICKER_LOAD root={_root}: {ex}");
            if (!_closed) StatusText.Text = "Không đọc được danh sách mã hàng. Hãy làm mới hoặc chọn file khác.";
        }
        finally
        {
            if (!_closed) RefreshButton.IsEnabled = true;
        }
    }

    private void FilterFiles()
    {
        _searchTimer.Stop();
        if (_files is null || _closed) return;
        var watch = Stopwatch.StartNew();
        string search = SearchBox.Text.Trim();
        if (!search.Equals(_appliedSearch, StringComparison.OrdinalIgnoreCase))
        {
            _appliedSearch = search;
            _files.Refresh();
        }
        _searchPending = false;
        UpdateSearchResults();
        AsyncFileLogService.Current.Performance(
            $"PRODUCT_PICKER_FILTER total={_fileCount} matches={ProductGrid.Items.Count} ms={watch.Elapsed.TotalMilliseconds:0.###}");
    }

    private void UpdateSearchResults()
    {
        ProductGrid.SelectedItem = null;
        int matches = ProductGrid.Items.Count;
        StatusText.Text = matches == 0 ? "Không có mã hàng phù hợp." : $"Tìm thấy {matches:N0} / {_fileCount:N0} mã hàng.";
        if (_errorCount > 0)
            StatusText.Text += $" Có {_errorCount} thư mục chưa quét được; xem nhật ký để biết chi tiết.";
        if (matches > 0) ProductGrid.SelectedIndex = 0;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_files is null || _closed) return;
        _searchPending = true;
        SelectButton.IsEnabled = false;
        _searchTimer.Stop();
        _searchTimer.Start();
    }
    private void SearchTimer_Tick(object? sender, EventArgs e) => FilterFiles();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadFilesAsync();
    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        BrowseFileRequested = true;
        DialogResult = false;
    }
    private void ProductGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectButton.IsEnabled = !_searchPending && ProductGrid.SelectedItem is ProductFileEntry;
    private void Select_Click(object sender, RoutedEventArgs e) => SelectProduct();
    private void SelectProduct()
    {
        if (_searchPending) FilterFiles();
        if (ProductGrid.SelectedItem is not ProductFileEntry file) return;
        SelectedFilePath = file.FullPath;
        DialogResult = true;
    }
    private void ProductGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_searchPending) { FilterFiles(); return; }
        DependencyObject? element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not DataGridRow)
            element = element is Visual ? VisualTreeHelper.GetParent(element) : null;
        if (element is DataGridRow) SelectProduct();
    }
    private void ProductGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; SelectProduct(); }
    }
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; SelectProduct(); }
        else if (e.Key == Key.Down)
        {
            if (_searchPending) FilterFiles();
            if (ProductGrid.SelectedItem is not null) { e.Handled = true; ProductGrid.Focus(); }
        }
    }
}
