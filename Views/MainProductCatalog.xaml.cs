using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public partial class MainProductCatalog : UserControl, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _searchTimer;
    private ICollectionView? _files;
    private string _root = string.Empty, _selectedPath = string.Empty, _search = string.Empty;
    private bool _loading, _disposed;
    private string _notice = string.Empty;
    public event Action<string>? ProductRequested;
    public event Action? BrowseRequested;

    public MainProductCatalog()
    {
        InitializeComponent();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchTimer.Tick += ApplySearch;
    }

    public async Task LoadAsync(string root, string selectedPath, string? databasePath = null)
    {
        if (_loading || _disposed) return;
        _root = root;
        _selectedPath = selectedPath;
        _loading = true;
        RefreshButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        StatusText.Text = "Đang tìm mã hàng...";
        _searchTimer.Stop();
        try
        {
            var ct = _lifetime.Token;
            var result = await Task.Run(() => ProductFileCatalog.Read(root, ct), ct);
            var ordered = result.Files;
            _notice = result.Errors.Count > 0 ? $"Có {result.Errors.Count} thư mục chưa đọc được. " : string.Empty;
            foreach (string error in result.Errors) AsyncFileLogService.Current.Error($"MAIN_CATALOG: {error}");
            try
            {
                var recent = await Task.Run(() => RecentTestedProductService.Read(databasePath ?? RuntimePaths.DatabaseFile, result.Files, ct), ct);
                var ranks = recent.Select((item, index) => (item.File.FullPath, index))
                    .ToDictionary(item => item.FullPath, item => item.index, StringComparer.OrdinalIgnoreCase);
                ordered = result.Files.OrderBy(file => ranks.GetValueOrDefault(file.FullPath, int.MaxValue)).ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AsyncFileLogService.Current.Error($"MAIN_CATALOG_RECENT: {ex}");
                _notice += "Chưa đọc được mã vừa test. ";
            }
            if (_disposed) return;
            _files = new ListCollectionView(ordered.ToList());
            _files.Filter = item => item is ProductFileEntry file &&
                (ProductFileCatalog.Matches(file, _search) || file.Folder.Contains(_search, StringComparison.OrdinalIgnoreCase));
            ProductList.ItemsSource = _files;
            _loading = false;
            ApplySearch(null, EventArgs.Empty);
            ProductList.SelectedItem = ordered.FirstOrDefault(file => string.Equals(file.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
            if (ProductList.SelectedItem is not null) ProductList.ScrollIntoView(ProductList.SelectedItem);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"MAIN_CATALOG_LOAD root={root}: {ex}");
            if (!_disposed)
            {
                _files = null;
                ProductList.ItemsSource = null;
                StatusText.Text = "Chưa đọc được danh mục. Bấm Làm mới hoặc Chọn file khác… để duyệt file.";
            }
        }
        finally
        {
            _loading = false;
            if (!_disposed) { RefreshButton.IsEnabled = true; UpdateSelection(); }
        }
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchTimer is null || _disposed) return;
        OpenButton.IsEnabled = false;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void ApplySearch(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        if (_disposed || _files is null) return;
        _search = SearchBox.Text.Trim();
        _files.Refresh();
        StatusText.Text = _notice + (ProductList.Items.Count == 0
            ? "Không có mã hàng phù hợp. Bấm Chọn file khác… để duyệt file."
            : $"{ProductList.Items.Count:N0} mã hàng • {_root}");
        UpdateSelection();
    }

    private void UpdateSelection() => OpenButton.IsEnabled = !_loading && !(_searchTimer?.IsEnabled ?? false) && ProductList.SelectedItem is ProductFileEntry;
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (!_disposed) BrowseRequested?.Invoke();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync(_root, _selectedPath);
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _disposed) return;
        ApplySearch(null, EventArgs.Empty);
        if (ProductList.SelectedItem is ProductFileEntry file) ProductRequested?.Invoke(file.FullPath);
    }
    private void Product_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? element = e.OriginalSource as DependencyObject;
        while (element is Visual && element is not ListBoxItem) element = VisualTreeHelper.GetParent(element);
        if (element is ListBoxItem) Open_Click(sender, e);
    }
    private void Product_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Open_Click(sender, e); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _searchTimer.Stop();
        _searchTimer.Tick -= ApplySearch;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
