using System.IO;
using Microsoft.VisualBasic.FileIO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public partial class MainProductCatalog : UserControl, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<string, CancellationToken, ProductFileCatalogResult> _readCatalog;
    private readonly DispatcherTimer _searchTimer;
    private IReadOnlyList<ProductFileEntry> _files = Array.Empty<ProductFileEntry>();
    private string _root = string.Empty, _selectedPath = string.Empty;
    private int _searchGeneration;
    private bool _searchPending;
    private bool _loading, _disposed, _deleting;
    private string? _databasePath;
    private readonly Action<string> _recycleFile;
    private string _notice = string.Empty;
    public event Action<string>? ProductRequested;
    public event Action? BrowseRequested;

    public MainProductCatalog() : this(ProductFileCatalog.Read) { }

    public MainProductCatalog(Func<string, CancellationToken, ProductFileCatalogResult> readCatalog, Action<string>? recycleFile = null)
    {
        _readCatalog = readCatalog ?? throw new ArgumentNullException(nameof(readCatalog));
        _recycleFile = recycleFile ?? (path => FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException));
        InitializeComponent();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchTimer.Tick += ApplySearch;
    }

    private bool _refreshLoopActive;
    private Task _refreshTask = Task.CompletedTask;
    private (string Root, string SelectedPath, string? DatabasePath)? _pendingRefresh;

    public Task LoadAsync(string root, string selectedPath, string? databasePath = null)
    {
        if (_disposed) return Task.CompletedTask;
        _pendingRefresh = (root, selectedPath, databasePath);
        if (!_refreshLoopActive) _refreshTask = ProcessRefreshesAsync();
        return _refreshTask;
    }

    private async Task ProcessRefreshesAsync()
    {
        _refreshLoopActive = true;
        try
        {
            // A save during an existing scan must schedule another read rather
            // than silently dropping the refresh and leaving a stale list.
            while (!_disposed && _pendingRefresh is { } request)
            {
                _pendingRefresh = null;
                await LoadCoreAsync(request.Root, request.SelectedPath, request.DatabasePath);
            }
        }
        finally { _refreshLoopActive = false; }
    }

    private async Task LoadCoreAsync(string root, string selectedPath, string? databasePath)
    {
        if (_disposed) return;
        _root = root;
        _databasePath = databasePath;
        _selectedPath = selectedPath;
        _loading = true;
        ++_searchGeneration;
        _searchPending = true;
        RefreshButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        StatusText.Text = "Đang tìm mã hàng...";
        _searchTimer.Stop();
        try
        {
            var ct = _lifetime.Token;
            var result = await Task.Run(() => _readCatalog(root, ct), ct);
            var ordered = result.Files;
            _notice = result.Errors.Count > 0 ? $"Có {result.Errors.Count} thư mục chưa đọc được. " : string.Empty;
            foreach (string error in result.Errors) AsyncFileLogService.Current.Error($"MAIN_CATALOG: {error}");
            try
            {
                ordered = await Task.Run(() =>
                {
                    var recent = RecentTestedProductService.Read(databasePath ?? RuntimePaths.DatabaseFile, result.Files, ct);
                    var ranks = recent.Select((item, index) => (item.File.FullPath, index))
                        .ToDictionary(item => item.FullPath, item => item.index, StringComparer.OrdinalIgnoreCase);
                    return result.Files.OrderBy(file => ranks.GetValueOrDefault(file.FullPath, int.MaxValue)).ToArray();
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AsyncFileLogService.Current.Error($"MAIN_CATALOG_RECENT: {ex}");
                _notice += "Chưa đọc được mã vừa test. ";
            }
            if (_disposed) return;
            _files = ordered;
            // Typing while the initial filter is running invalidates that result.
            // Finish the newest query before releasing the catalog loading gate.
            do { await ApplySearchAsync(selectedPath); }
            while (!_disposed && _searchPending);
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"MAIN_CATALOG_LOAD root={root}: {ex}");
            if (!_disposed)
            {
                _files = Array.Empty<ProductFileEntry>();
                _searchPending = false;
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
        ++_searchGeneration;
        _searchPending = true;
        OpenButton.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void ApplySearch(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        if (_disposed || _loading) return;
        await ApplySearchAsync((ProductList.SelectedItem as ProductFileEntry)?.FullPath);
    }

    private async Task ApplySearchAsync(string? selectedPath = null)
    {
        _searchTimer.Stop();
        if (_disposed) return;
        int generation = ++_searchGeneration;
        _searchPending = true;
        UpdateSelection();
        string search = SearchBox.Text.Trim();
        var files = _files;
        var ct = _lifetime.Token;
        try
        {
            // Only immutable file entries cross threads. WPF receives one completed
            // result instead of filtering the entire catalog on its Dispatcher.
            var matches = await Task.Run(() =>
            {
                var rows = new List<ProductFileEntry>();
                ProductFileEntry? selected = null;
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!ProductFileCatalog.Matches(file, search) &&
                        !file.Folder.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                    rows.Add(file);
                    if (string.Equals(file.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase)) selected = file;
                }
                return (Rows: rows, Selected: selected);
            }, ct);
            if (_disposed || generation != _searchGeneration) return;
            ProductList.ItemsSource = matches.Rows;
            ProductList.SelectedItem = matches.Selected;
            if (matches.Selected is not null) ProductList.ScrollIntoView(matches.Selected);
            StatusText.Text = _notice + (matches.Rows.Count == 0
                ? "Không có mã hàng phù hợp. Bấm Chọn file khác… để duyệt file."
                : $"{matches.Rows.Count:N0} mã hàng");
            StatusText.ToolTip = _root;
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"MAIN_CATALOG_FILTER: {ex}");
            if (!_disposed && generation == _searchGeneration)
            {
                ProductList.ItemsSource = null;
                StatusText.Text = "Chưa tìm được mã hàng. Bấm Làm mới để thử lại.";
            }
        }
        finally
        {
            if (!_disposed && generation == _searchGeneration)
            {
                _searchPending = false;
                UpdateSelection();
            }
        }
    }

    private void UpdateSelection()
    {
        bool ready = !_loading && !_deleting && !_disposed && !_searchPending && !(_searchTimer?.IsEnabled ?? false) && ProductList.SelectedItem is ProductFileEntry;
        BrowseButton.IsEnabled = !_deleting && !_disposed;
        RefreshButton.IsEnabled = !_loading && !_deleting && !_disposed;
        ProductList.IsEnabled = SearchBox.IsEnabled = !_deleting;
        var selected = ProductList.SelectedItem as ProductFileEntry;
        SelectionNameText.Text = selected is null ? "Chưa chọn mã hàng" : "ĐANG CHỌN: " + selected.Name;
        SelectionNameText.ToolTip = selected?.FullPath;
        SelectionFolderText.Text = selected is null ? "Chọn một dòng trong danh sách" : "Thư mục: " + Path.GetDirectoryName(selected.FullPath);
        SelectionFolderText.ToolTip = selected?.FullPath;
        SelectionStateText.Text = selected is null ? string.Empty :
            string.Equals(selected.FullPath, _selectedPath, StringComparison.OrdinalIgnoreCase)
                ? "Mã này đang được mở trên máy test"
                : "Chưa áp dụng • Bấm MỞ MÃ HÀNG để đổi mã";
        OpenButton.IsEnabled = ready;
        DeleteButton.IsEnabled = ready && ProductList.SelectedItem is ProductFileEntry file &&
            !string.Equals(file.FullPath, _selectedPath, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> DeleteSelectedAsync(Func<ProductFileEntry, bool> confirm)
    {
        if (_disposed || _loading || _deleting || _searchPending || (_searchTimer?.IsEnabled ?? false) ||
            ProductList.SelectedItem is not ProductFileEntry file ||
            string.Equals(file.FullPath, _selectedPath, StringComparison.OrdinalIgnoreCase)) return false;
        _deleting = true;
        UpdateSelection();
        try
        {
            if (!confirm(file)) return false;
            if (!string.Equals(Path.GetExtension(file.FullPath), ".tht", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Chỉ được xóa file THT.");
            await Task.Run(() => _recycleFile(file.FullPath));
            if (!_disposed) await LoadAsync(_root, _selectedPath, _databasePath);
            return true;
        }
        finally { _deleting = false; if (!_disposed) UpdateSelection(); }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await DeleteSelectedAsync(file => MessageBox.Show(Window.GetWindow(this),
                $"Xóa mã hàng {file.Name}?\n\n{file.FullPath}\n\nFile THT sẽ được đưa vào Thùng rác. Lịch sử kiểm tra được giữ nguyên.",
                "XÁC NHẬN XÓA MÃ HÀNG", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes);
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"MAIN_CATALOG_DELETE: {ex}");
            if (!_disposed) MessageBox.Show(Window.GetWindow(this), "Chưa xóa được file THT. Kiểm tra quyền truy cập hoặc file đang được sử dụng.",
                "XÓA MÃ HÀNG", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (!_disposed && !_deleting) BrowseRequested?.Invoke();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    { if (!_deleting && !_disposed) await LoadAsync(_root, _selectedPath, _databasePath); }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _deleting || _disposed || _searchPending || _searchTimer.IsEnabled) return;
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
        ++_searchGeneration;
        _pendingRefresh = null;
        _searchTimer.Stop();
        _searchTimer.Tick -= ApplySearch;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
