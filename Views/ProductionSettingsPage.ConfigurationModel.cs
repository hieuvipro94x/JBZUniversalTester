using System.IO;
using System.Windows;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public partial class ProductionSettingsPage
{
    public event Func<ProductionSettingsPage, string, Task>? ConfigurationModelRequested;
    public SettingsAccessSession AccessSession => _accessSession;
    private int _configurationSwitchInProgress;
    public Task<ViewModels.ProductionSettingsViewModel> PrepareConfigurationModelAsync(string path) =>
        Task.Run(() => _vm.CreateForConfigurationModel(path));

    private async void SelectConfigurationModel_Click(object sender, RoutedEventArgs e)
    {
        if (IsReleased || _modelEditor is not null || _saveInProgress != 0 || _batchPrintInProgress != 0 ||
            _vm.IsManualOperationRunning || Volatile.Read(ref _configurationSwitchInProgress) != 0) return;
        string root = _main?.Home.CatalogDirectory ??
            (Directory.Exists(Path.GetDirectoryName(_vm.DiscardModelPath)) ? Path.GetDirectoryName(_vm.DiscardModelPath)! : RuntimePaths.ItemDirectory);
        var picker = new ProductPickerWindow(root) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true && picker.SelectedFilePath is string path)
            await SelectConfigurationModelAsync(path);
    }

    public async Task<bool> SelectConfigurationModelAsync(string path, Func<bool?>? confirmUnsaved = null)
    {
        if (IsReleased || _modelEditor is not null || _saveInProgress != 0 || _batchPrintInProgress != 0 ||
            _vm.IsManualOperationRunning || Interlocked.CompareExchange(ref _configurationSwitchInProgress, 1, 0) != 0) return false;
        try
        {
            if (!string.IsNullOrWhiteSpace(_vm.DiscardModelPath) &&
                string.Equals(Path.GetFullPath(path), Path.GetFullPath(_vm.DiscardModelPath), StringComparison.OrdinalIgnoreCase)) return true;
            if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy file THT đã chọn.", path);
            CommitPendingEditorValues(); SyncCompatibilityFields();
            if (!string.Equals(_savedSettingsSnapshot, CaptureEditableSettingsSnapshot(), StringComparison.Ordinal))
            {
                bool? save = confirmUnsaved?.Invoke();
                if (confirmUnsaved is null) save = ConfirmSaveBeforeLeaving();
                if (save is null || save == true && !await PersistSettingsAsync()) return false;
            }
            var handlers = ConfigurationModelRequested;
            if (handlers is null) return false;
            SelectConfigurationModelButton.IsEnabled = false;
            IsEnabled = false;
            foreach (Func<ProductionSettingsPage, string, Task> handler in handlers.GetInvocationList())
                await handler(this, Path.GetFullPath(path));
            return true;
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Settings model selection failed: {ex}");
            if (!IsReleased) ShowMessage("Chưa mở được cấu hình mã hàng đã chọn. Kiểm tra file THT và thử lại.", "CHỌN MÃ CÀI ĐẶT", MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _configurationSwitchInProgress, 0);
            if (!IsReleased) { IsEnabled = true; SelectConfigurationModelButton.IsEnabled = true; }
        }
    }
}
