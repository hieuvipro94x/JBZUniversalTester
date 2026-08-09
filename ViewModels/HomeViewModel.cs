using System.IO;
using System.Threading.Tasks;
using System.Windows;
using JBZUniversalTester.Core;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    // Cho HomeView mở màn hình kiểm tra chân pin và cài đặt.
    public MainViewModel Main => _main;

    public AsyncRelayCommand LoadModelCommand { get; }
    public RelayCommand StartCommand { get; }

    public string ModelName =>
        _main.Model?.ModelName ?? "CHƯA CÓ MODEL";

    public string PartNumber =>
        _main.Model?.PartNumber ?? "—";

    public string ProductName =>
        _main.Model?.ProductName ?? "—";

    public string VehicleType =>
        _main.Model?.VehicleType ?? "—";

    public string SourcePath =>
        _main.Model?.SourcePath ?? string.Empty;

    public HomeViewModel(MainViewModel main)
    {
        _main = main;

        LoadModelCommand = new AsyncRelayCommand(LoadModelAsync);

        StartCommand = new RelayCommand(
            () => _main.CurrentPage = _main.Test,
            () => _main.Model is not null && _main.HasEnoughCardsForModel
        );
    }

    private async Task LoadModelAsync()
    {
        string? initialDirectory = Path.GetDirectoryName(_main.Model?.SourcePath);
        var dialog = new ProductFilePickerWindow(initialDirectory);
        Window? owner = Application.Current?.MainWindow;
        if (owner is not null)
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        if (dialog.ShowDialog() == true && dialog.SelectedFilePath is string selectedFilePath)
        {
            try
            {
                await _main.LoadModelAsync(selectedFilePath);
            }
            catch (InvalidDataException ex)
            {
                MessageBox.Show(
                    $"Không thể đọc file mã hàng JBZ (.model/.tht).\n\n" +
                    $"File: {selectedFilePath}\n\n" +
                    $"{ex.Message}\n\n" +
                    "Hãy kiểm tra đúng file mã hàng gốc. Nếu file đang được phần mềm khác ghi/copy, " +
                    "đợi hoàn tất rồi chọn lại.",
                    "File model không hợp lệ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                MessageBox.Show(
                    $"Không thể mở file mã hàng vì file đang bận hoặc lỗi I/O.\n\n" +
                    $"File: {selectedFilePath}\n\n{ex.Message}",
                    "Không mở được file model",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể nạp mã hàng.\n\n{ex.Message}",
                    "Lỗi nạp model",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    public void Refresh()
    {
        Raise(nameof(ModelName));
        Raise(nameof(PartNumber));
        Raise(nameof(ProductName));
        Raise(nameof(VehicleType));
        Raise(nameof(SourcePath));

        StartCommand.RaiseCanExecuteChanged();
    }
}
