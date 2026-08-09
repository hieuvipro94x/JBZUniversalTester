using System.IO;
using System.Threading.Tasks;
using System.Windows;
using JBZUniversalTester.Core;
using JBZUniversalTester.Views;
using Microsoft.Win32;

namespace JBZUniversalTester.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private const string ProductFileFilter = "Mã hàng JBZ (*.tht;*.model)|*.tht;*.model";
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
        var dialog = new OpenFileDialog
        {
            Title = "Chọn mã hàng",
            Filter = ProductFileFilter,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            ValidateNames = true
        };

        string? initialDirectory = Path.GetDirectoryName(_main.Model?.SourcePath);
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        Window? owner = Application.Current?.MainWindow;
        bool? accepted;
        if (owner is not null)
        {
            using var positionGuard = new StandardFileDialogPositionGuard(owner);
            accepted = dialog.ShowDialog(owner);
        }
        else
        {
            accepted = dialog.ShowDialog();
        }

        if (accepted == true)
        {
            string selectedFilePath = dialog.FileName;
            if (!IsSupportedProductFile(selectedFilePath))
            {
                MessageBox.Show(
                    owner ?? Application.Current?.MainWindow,
                    "Chỉ có thể chọn file mã hàng .tht hoặc .model.",
                    "File không được hỗ trợ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

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

    private static bool IsSupportedProductFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".tht", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".model", StringComparison.OrdinalIgnoreCase);
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
