using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Win32;

namespace JBZUniversalTester.Views;

public partial class TopologyLearningWindow : Window
{
    private const int MinimumStableFrames = 2;
    private const int MaximumStableFrames = 500;
    private readonly TestViewModel _test;
    private ScanFrame? _latestFrame;
    private int _frameDispatchQueued;
    private bool _diagnosing;
    private int _requiredStableFrames;
    private int _stableFrames;
    private string _candidateSignature = string.Empty;
    private string _displayedSignature = string.Empty;
    private LearnedTopologySnapshot? _capturedSnapshot;

    public ObservableCollection<LearnedTopologyRow> Rows { get; } = [];

    public TopologyLearningWindow(TestViewModel test)
    {
        InitializeComponent();
        _test = test ?? throw new ArgumentNullException(nameof(test));
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _test.StartTopologyLearningAsync();
            _test.BoardFrameActivity += Test_BoardFrameActivity;
            _test.PropertyChanged += Test_PropertyChanged;
            UpdateBoardStatus();
            LearningStatusText.Text = "Đang quan sát frame thật từ toàn bộ card đã cấu hình.";
        }
        catch (Exception ex)
        {
            DiagnoseButton.IsEnabled = false;
            LearningStatusText.Text = ex.Message;
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _test.BoardFrameActivity -= Test_BoardFrameActivity;
        _test.PropertyChanged -= Test_PropertyChanged;
        Interlocked.Exchange(ref _frameDispatchQueued, 0);
        _latestFrame = null;
        try
        {
            await _test.StopTopologyLearningAsync();
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"TOPOLOGY LEARNING restore scan failed: {ex}");
        }
    }

    private void Test_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TestViewModel.IsBoardConnected) or nameof(TestViewModel.IsDeviceFault))
            _ = Dispatcher.BeginInvoke(UpdateBoardStatus, DispatcherPriority.DataBind);
    }

    private void UpdateBoardStatus()
    {
        bool ready = _test.IsBoardConnected && !_test.IsDeviceFault;
        BoardStatusText.Text = ready ? "BO ĐÃ KẾT NỐI" : "BO CHƯA SẴN SÀNG";
        DiagnoseButton.IsEnabled = ready;
        if (!ready)
        {
            _diagnosing = false;
            SaveButton.IsEnabled = false;
            LearningStatusText.Text = "Không thể học topology khi bo mất kết nối hoặc lỗi.";
        }
    }

    private void Test_BoardFrameActivity(object? sender, ScanFrame frame)
    {
        if (!_test.IsBoardConnected || _test.IsDeviceFault ||
            frame.Mode != BoardScanMode.Production || !frame.Complete || frame.UnknownBytes != 0)
        {
            return;
        }

        Volatile.Write(ref _latestFrame, frame);
        if (Interlocked.Exchange(ref _frameDispatchQueued, 1) != 0)
            return;

        _ = Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _frameDispatchQueued, 0);
            ScanFrame? latest = Volatile.Read(ref _latestFrame);
            if (latest is not null && IsLoaded)
                ProcessFrame(latest);
        }, DispatcherPriority.Background);
    }

    private void ProcessFrame(ScanFrame frame)
    {
        LearnedTopologySnapshot snapshot = TopologyLearningService.BuildSnapshot(frame, _test.BoardCapacity);
        if (!string.Equals(snapshot.Signature, _displayedSignature, StringComparison.Ordinal))
        {
            _displayedSignature = snapshot.Signature;
            Rows.Clear();
            foreach (LearnedTopologyRow row in snapshot.Rows)
                Rows.Add(row);
        }

        if (!_diagnosing)
        {
            LearningStatusText.Text = snapshot.Rows.Count == 0
                ? "Chưa phát hiện quan hệ continuity."
                : $"Đang quan sát {snapshot.Rows.Count} mạng continuity.";
            return;
        }

        if (snapshot.Rows.Count == 0)
        {
            _stableFrames = 0;
            _candidateSignature = string.Empty;
            UpdateLearningProgress("Chưa có kết nối để chẩn đoán.");
            return;
        }

        if (string.Equals(snapshot.Signature, _candidateSignature, StringComparison.Ordinal))
            _stableFrames++;
        else
        {
            _candidateSignature = snapshot.Signature;
            _stableFrames = 1;
        }

        UpdateLearningProgress($"Đang xác nhận ổn định: {_stableFrames}/{_requiredStableFrames}");
        if (_stableFrames < _requiredStableFrames)
            return;

        _diagnosing = false;
        _capturedSnapshot = snapshot;
        DiagnoseButton.Content = "CHẨN ĐOÁN LẠI";
        SaveButton.IsEnabled = true;
        LearningStatusText.Text = $"ĐÃ QUÉT XONG • {snapshot.Networks.Count} MẠNG • {_stableFrames}/{_requiredStableFrames} FRAME ỔN ĐỊNH";
    }

    private void UpdateLearningProgress(string text)
    {
        LearningStatusText.Text = text;
        LearningProgress.Maximum = Math.Max(1, _requiredStableFrames);
        LearningProgress.Value = Math.Min(_stableFrames, _requiredStableFrames);
    }

    private void Diagnose_Click(object sender, RoutedEventArgs e)
    {
        if (!_test.IsBoardConnected || _test.IsDeviceFault)
            return;

        if (!int.TryParse(StableFrameCountTextBox.Text.Trim(), out int required) ||
            required is < MinimumStableFrames or > MaximumStableFrames)
        {
            MessageBox.Show(this,
                $"Số frame ổn định phải từ {MinimumStableFrames} đến {MaximumStableFrames}.",
                "Giá trị không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _requiredStableFrames = required;
        _stableFrames = 0;
        _candidateSignature = string.Empty;
        _capturedSnapshot = null;
        _diagnosing = true;
        DiagnoseButton.Content = "ĐANG CHẨN ĐOÁN...";
        SaveButton.IsEnabled = false;
        UpdateLearningProgress($"Đang xác nhận ổn định: 0/{required}");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        LearnedTopologySnapshot? snapshot = _capturedSnapshot;
        string productCode = ProductCodeTextBox.Text.Trim();
        if (snapshot is null || snapshot.Networks.Count == 0)
            return;
        if (string.IsNullOrWhiteSpace(productCode))
        {
            MessageBox.Show(this, "Hãy nhập mã cấu hình trước khi lưu.",
                "Thiếu mã cấu hình", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string safeName = string.Concat(productCode.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".jbzscan.json",
            Filter = "JBZ diagnostic topology (*.jbzscan.json)|*.jbzscan.json",
            FileName = safeName + ".jbzscan.json",
            InitialDirectory = Directory.Exists(@"C:\ITEM") ? @"C:\ITEM" : AppContext.BaseDirectory
        };
        if (dialog.ShowDialog(this) != true)
            return;

        BoardCapacity capacity = _test.BoardCapacity;
        var profile = new LearnedTopologyProfile
        {
            ProductCode = productCode,
            CreatedAt = DateTime.Now,
            ExpansionCardCount = capacity.ExpansionCardCount,
            FirstIo = capacity.FirstGlobalIo,
            LastIo = capacity.LastGlobalIo,
            RequiredStableFrames = _requiredStableFrames,
            ObservedStableFrames = _stableFrames,
            Networks = snapshot.Networks.Select(network => new LearnedTopologyNetwork
            {
                Name = network.Name,
                Ios = network.Ios.ToList()
            }).ToList()
        };

        try
        {
            SaveButton.IsEnabled = false;
            await TopologyLearningService.SaveAsync(dialog.FileName, profile);
            LearningStatusText.Text = $"ĐÃ LƯU: {Path.GetFileName(dialog.FileName)}";
            MessageBox.Show(this, "Đã lưu cấu hình continuity chẩn đoán.",
                "Hoàn thành", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể lưu cấu hình.\n\n{ex.Message}",
                "Lỗi lưu file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = _capturedSnapshot is not null;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
