using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;
using JBZUniversalTester.ModelEditor.Services;

namespace JBZUniversalTester.ModelEditor;

public partial class ThtEditorPage : UserControl
{
    private ModelDocument _document = new() { SourceFormat = "tht", Base = new() { Model = "NewModel" } };
    private readonly DocumentChangeTracker _changes = new();
    private readonly BasePage _base = new();
    private readonly ConnectorPage _connectors = new();
    private readonly PinPage _pins = new();
    private readonly LinePage _lines = new();
    private readonly ColorPage _colors = new();
    private int _page;
    private bool _loading, _busy, _rebuild;
    private string? _hash;
    private Task _diskWork = Task.CompletedTask;
    public HashSet<string> SavedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    public event Func<Task>? RequestClose;
    public bool IsBusy => _busy;
    public string? ProtectedModelPath { get; set; }
    public bool HasUnsavedChanges
    {
        get { Commit(); return _changes.HasChanges(_document); }
    }

    public ThtEditorPage()
    {
        InitializeComponent();
        void Changed(object? _, EventArgs e) { if (!_loading) { SaveButton.Tag = null; StatusText.Text = "Đã thay đổi — bấm LƯU THT để lưu."; } }
        _base.Changed += Changed; _connectors.Changed += Changed; _pins.Changed += Changed; _lines.Changed += Changed;
        _lines.ConnectionsChanged += (_, _) => _rebuild = true;
        _pins.ConnectionsChanged += (_, _) => _rebuild = false;
        _pins.IoChanged += (_, _) => _rebuild = true;
        _colors.ColorsChanged += (_, _) => _lines.SetColors(_colors.CurrentColors);
        _changes.Reset(_document); ShowPage(0);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, async (_, _) => await SaveAsync()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));
    }

    private void Commit()
    {
        switch (_page)
        {
            case 0: _document.Base = _base.ReadData(); break;
            case 1:
                var connectors = _connectors.ReadData(); ModelLogic.ValidateConnectors(connectors);
                bool changed = System.Text.Json.JsonSerializer.Serialize(connectors) != System.Text.Json.JsonSerializer.Serialize(_document.Connectors);
                _document.Connectors = connectors;
                if (changed) _document.Pins = ModelLogic.RegeneratePins(connectors, _document.Pins);
                break;
            case 2: _document.Pins = _pins.ReadData(); break;
            case 3:
                _document.Pins = _lines.ReadData();
                if (_lines.HasStructuralChanges) ModelLogic.UpdateConnectorLayout(_document.Connectors, _document.Pins);
                break;
        }
    }

    private void ShowPage(int page)
    {
        _loading = true;
        try
        {
            _page = page;
            foreach (var nav in new[] { BaseNav, ConnectorNav, PinNav, LineNav, ColorNav })
                nav.IsChecked = int.Parse((string)nav.Tag) == page;
            switch (page)
            {
                case 0: _base.LoadData(_document.Base); ContentHost.Content = _base; break;
                case 1: _connectors.LoadData(_document); ContentHost.Content = _connectors; break;
                case 2: _pins.LoadData(_document.Pins); ContentHost.Content = _pins; break;
                case 3: _lines.SetColors(_colors.CurrentColors); _lines.LoadData(_document.Pins); ContentHost.Content = _lines; break;
                case 4: _colors.Reload(); ContentHost.Content = _colors; break;
            }
        }
        finally { _loading = false; }
    }

    public async Task OpenPathAsync(string path)
    {
        if (_busy) return;
        _busy = true; IsEnabled = false; StatusText.Text = "Đang đọc THT...";
        try
        {
            var loading = Task.Run(() =>
            {
                var doc = ThtFileService.Load(path);
                ModelLogic.ValidateConnectors(doc.Connectors);
                ModelLogic.NormalizeHarnProgClip(doc.Pins);
                ModelLogic.ValidatePinsForEditing(doc.Connectors, doc.Pins);
                return (Doc: doc, Hash: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(doc.ThtState!.OriginalFileBytes)));
            });
            _diskWork = loading;
            var loaded = await loading;
            _document = loaded.Doc; _hash = loaded.Hash; _rebuild = false;
            SaveButton.Tag = null;
            _changes.MarkSaved(_document); PathText.Text = Path.GetFullPath(path); ShowPage(0);
            StatusText.Text = $"Đã mở {_document.Connectors.Count} connector, {_document.Pins.Count} chân. THT gốc được giữ nguyên đến khi Lưu.";
        }
        catch (Exception ex) { ShowError("Chưa mở được THT", ex); }
        finally { _busy = false; IsEnabled = true; }
    }

    public async Task<bool> SaveAsync()
    {
        if (_busy) return false;
        try
        {
            Commit();
            ModelLogic.ValidatePinsForEditing(_document.Connectors, _document.Pins);
            if (_rebuild) { ModelLogic.RebuildConnections(_document.Pins); _rebuild = false; }
            ModelLogic.NormalizeHarnProgClip(_document.Pins);
            ModelLogic.ValidatePins(_document.Connectors, _document.Pins);
            string? target = _document.SourcePath;
            if (target is null)
            {
                var dialog = new SaveFileDialog { Filter = "HarnProg THT (*.tht)|*.tht", DefaultExt = ".tht", FileName = _document.Base.Model, InitialDirectory = Directory.Exists(@"C:\ITEM") ? @"C:\ITEM" : null };
                if (dialog.ShowDialog(Window.GetWindow(this)) != true) return false;
                target = dialog.FileName;
            }
            if (_changes.HasUnsavedChanges(_document) || _document.SourcePath is null)
            {
                _busy = true; IsEnabled = false; StatusText.Text = "Đang kiểm tra và lưu THT...";
                var snapshot = _document.Clone(); string? expected = _hash;
                var saving = Task.Run(() =>
                {
                    var doc = IntegratedThtStore.Save(snapshot, target, expected);
                    return (Doc: doc, Hash: Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(doc.ThtState!.OriginalFileBytes)));
                });
                _diskWork = saving;
                var saved = await saving;
                _document = saved.Doc; _hash = saved.Hash; _changes.MarkSaved(_document);
                SavedPaths.Add(Path.GetFullPath(target)); PathText.Text = target; ShowPage(_page);
            }
            StatusText.Text = "ĐÃ LƯU THT — bản gốc nằm trong ThtBackups.";
            SaveButton.Tag = "Saved";
            return true;
        }
        catch (Exception ex) { ShowError("Chưa lưu được THT", ex); return false; }
        finally { _busy = false; IsEnabled = true; }
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (_busy) return false;
        try { if (!HasUnsavedChanges) return true; }
        catch (Exception ex) { ShowError("Dữ liệu chưa hợp lệ", ex); return false; }
        var choice = MessageBox.Show(Window.GetWindow(this), "Lưu các thay đổi THT trước khi rời trang?", "Cấu hình THT chưa lưu", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || choice == MessageBoxResult.Yes && await SaveAsync();
    }

    public async Task DrainDiskWorkAsync()
    {
        try { await _diskWork; }
        catch (Exception ex) { JBZUniversalTester.Services.AsyncFileLogService.Current.Error($"THT EDIT disk operation finished with error: {ex}"); }
    }

    private void ShowError(string title, Exception ex)
    {
        JBZUniversalTester.Services.AsyncFileLogService.Current.Error($"THT EDIT: {ex}");
        string message = ex is ValidationException or ThtFormatException or ThtExportException
            ? ex.Message : "Không thể đọc/ghi THT. Kiểm tra quyền truy cập, file đang bị khóa hoặc đã thay đổi, rồi mở lại đúng cặp THT và cấu hình Leak.";
        StatusText.Text = message;
        MessageBox.Show(Window.GetWindow(this), message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try { Commit(); ShowPage(int.Parse((string)((ToggleButton)sender).Tag)); }
        catch (Exception ex) { ShowPage(_page); ShowError("Dữ liệu chưa hợp lệ", ex); }
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string? currentDirectory = Path.GetDirectoryName(_document.SourcePath);
        string root = Directory.Exists(JBZUniversalTester.Services.RuntimePaths.ItemDirectory)
            ? JBZUniversalTester.Services.RuntimePaths.ItemDirectory
            : currentDirectory is not null && Directory.Exists(currentDirectory) ? currentDirectory : AppContext.BaseDirectory;
        var owner = Window.GetWindow(this);
        var picker = new JBZUniversalTester.Views.ProductPickerWindow(root) { Title = "Chọn mã hàng để sửa cấu hình THT" };
        if (owner is not null) picker.Owner = owner;
        string? selectedPath = null;
        if (picker.ShowDialog() == true) selectedPath = picker.SelectedFilePath;
        else if (picker.BrowseFileRequested)
        {
            var dialog = new OpenFileDialog { Filter = "HarnProg THT (*.tht)|*.tht", InitialDirectory = root };
            if (dialog.ShowDialog(owner) == true) selectedPath = dialog.FileName;
        }
        if (selectedPath is not null && await ConfirmCloseAsync()) await OpenPathAsync(selectedPath);
    }
    private async void New_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmCloseAsync()) return;
        _document = new() { SourceFormat = "tht", Base = new() { Model = "NewModel" } };
        _hash = null; _rebuild = false; _changes.Reset(_document); ShowPage(0); PathText.Text = "THT mới";
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _document.SourcePath is not string path) return;
        if (ProtectedModelPath is string active && string.Equals(Path.GetFullPath(active), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(Window.GetWindow(this), "Mã hàng này đang được nạp trên máy test. Hãy chọn mã hàng khác trước khi xóa.", "MÃ HÀNG ĐANG SỬ DỤNG", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(Window.GetWindow(this), $"Xóa mã hàng {Path.GetFileName(path)} khỏi thư mục hiện tại?\nBản gốc được giữ trong ThtBackups.", "XÁC NHẬN XÓA THT", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true; IsEnabled = false;
        try
        {
            string? hash = _hash;
            var deleting = Task.Run(() => IntegratedThtStore.Archive(path, hash));
            _diskWork = deleting; await deleting;
            SavedPaths.Remove(path);
            _document = new() { SourceFormat = "tht", Base = new() { Model = "NewModel" } };
            _hash = null; _rebuild = false; _changes.Reset(_document); ShowPage(0); PathText.Text = "THT mới";
            StatusText.Text = "Đã chuyển file vào ThtBackups.";
        }
        catch (Exception ex) { ShowError("Chưa xóa được THT", ex); }
        finally { _busy = false; IsEnabled = true; }
    }
    private async void Back_Click(object sender, RoutedEventArgs e) { if (RequestClose is { } close) await close(); }
}
