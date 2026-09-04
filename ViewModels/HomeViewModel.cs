using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using JBZUniversalTester.Core;
using JBZUniversalTester.Services;
using JBZUniversalTester.Views;
using WinForms = System.Windows.Forms;

namespace JBZUniversalTester.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private const string ProductFileFilter =
        "The Files (*.tht)|*.tht|All Files (*.*)|*.*";
    private const string OriginalItemDirectory = @"C:\Item";
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
        using var dialog = new WinForms.OpenFileDialog
        {
            DefaultExt = ".tht",
            Filter = ProductFileFilter,
            Multiselect = false,
            AutoUpgradeEnabled = false,
            RestoreDirectory = true
        };

        if (Directory.Exists(OriginalItemDirectory))
        {
            dialog.InitialDirectory = OriginalItemDirectory;
        }
        else
        {
            string? currentModelDirectory = Path.GetDirectoryName(_main.Model?.SourcePath);
            dialog.InitialDirectory = !string.IsNullOrWhiteSpace(currentModelDirectory) &&
                                      Directory.Exists(currentModelDirectory)
                ? currentModelDirectory
                : AppContext.BaseDirectory;
        }

        Window? owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;
        WinForms.DialogResult accepted;
        if (owner is not null)
        {
            using var positionGuard = new FixedPositionOpenFileDialogGuard(owner);
            accepted = dialog.ShowDialog(new NativeDialogOwner(owner));
        }
        else
        {
            accepted = dialog.ShowDialog();
        }

        if (accepted != WinForms.DialogResult.OK)
            return;

        string selectedFilePath = dialog.FileName;
        if (!IsSupportedProductFile(selectedFilePath))
        {
            MessageBox.Show(
                owner ?? Application.Current?.MainWindow,
                "Chỉ có thể chọn file mã hàng .tht hoặc .model legacy.",
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

    private sealed class NativeDialogOwner : WinForms.IWin32Window
    {
        public NativeDialogOwner(Window owner)
        {
            Handle = new WindowInteropHelper(owner).Handle;
        }

        public IntPtr Handle { get; }
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
