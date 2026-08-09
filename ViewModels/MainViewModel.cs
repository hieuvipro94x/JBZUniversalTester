using System.IO;
using System.Windows;
using JBZUniversalTester.Core;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private object _page;
    private string _status = "CHƯA KẾT NỐI";
    private ProductModel? _model;

    private readonly AppSettings _settings;
    private readonly ProductionSettings _productionSettings;
    private readonly IBoardTransport _board;
    private readonly KeysightVisaService _visa = new();
    private readonly TestEngine _engine;
    private readonly SemaphoreSlim _shutdownGate = new(1, 1);
    private bool _shutdownCompleted;
    private readonly object _startupGate = new();
    private Task? _startupTask;

    // Chỉ phát khi NGƯỜI VẬN HÀNH chủ động chọn một file mã hàng mới.
    // MainWindow dùng event này để tự mở TestView ngay sau khi model đã parse
    // xong, không cần bấm BẮT ĐẦU KIỂM TRA lần thứ hai.
    public event Action<ProductModel>? ExplicitModelLoaded;

    public object CurrentPage
    {
        get => _page;
        set => Set(ref _page, value);
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public ProductModel? Model
    {
        get => _model;
        set => Set(ref _model, value);
    }

    // V12.9: MainWindow/Transport/Decoder/TestView cùng đọc một BoardCapacity.
    public BoardMode ActiveBoardMode => _board is UnifiedBoardTransport unified ? unified.ActiveMode : _productionSettings.BoardMode;
    public bool UsesD2xxCardCapacity => ActiveBoardMode != BoardMode.UartTtl && _productionSettings.BoardMode != BoardMode.UartTtl;

    public BoardCapacity CurrentBoardCapacity => BoardCapacity.FromSettings(_productionSettings);

    // Các property UI dưới đây vẫn hiển thị "card mở rộng" (1..10), nhưng
    // không tự tính công thức riêng nữa.
    public int RequiredCardCount =>
        Model is null
            ? 1
            : BoardCapacity.RequiredExpansionModulesForIo(
                Model.MaxIo,
                _productionSettings.StartCardNumber);

    public int ConfiguredCardCount => CurrentBoardCapacity.ExpansionModuleCount;
    public int ConfiguredIoCapacity => CurrentBoardCapacity.TotalIoCapacity;
    public int ConfiguredIoStart => CurrentBoardCapacity.FirstGlobalIo;
    public int ConfiguredIoEnd => CurrentBoardCapacity.LastGlobalIo;

    public ProductionSettings ProductionSettings => _productionSettings;

    public bool HasEnoughCardsForModel =>
        Model is null || !UsesD2xxCardCapacity ||
        (CurrentBoardCapacity.IsRangeWithinSystem &&
         Model.MaxIo <= BoardCapacity.MaxGlobalIo &&
         Model.Pins.All(pin => CurrentBoardCapacity.ContainsGlobalIo(pin.IoNumber)));

    public HomeViewModel Home { get; }
    public TestViewModel Test { get; }

    public RelayCommand ShowHomeCommand { get; }
    public RelayCommand ShowTestCommand { get; }
    public RelayCommand ExitCommand { get; }

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _productionSettings = ProductionConfigService.Load();
        // Yêu cầu 3: ngay khi app khởi động, ghi lại đầy đủ config tiếng Anh.
        ProductionConfigService.EnsureSavedOnStartup(_productionSettings);

        _board = new UnifiedBoardTransport(
            _settings.Board.FtdiSerial,
            _productionSettings
        );

        _engine = new TestEngine(
            _board,
            _visa,
            _settings,
            _productionSettings
        );

        Home = new HomeViewModel(this);

        Test = new TestViewModel(
            this,
            _engine,
            _board,
            _visa,
            _settings,
            _productionSettings
        );

        // Việc tự nạp mã + tự kết nối bo được bắt đầu tại MainWindow.Loaded.
        // Không dùng fire-and-forget trong constructor để tránh race giữa WPF
        // StartupUri/ShowDialog và quá trình recovery D2XX.
        _page = Home;

        ShowHomeCommand = new RelayCommand(
            () => CurrentPage = Home
        );

        ShowTestCommand = new RelayCommand(
            () => CurrentPage = Test
        );

        ExitCommand = new RelayCommand(() =>
        {
            // Không Shutdown trực tiếp ở đây. MainWindow.Closing sẽ chờ dừng
            // worker FTDI/Keysight/relay xong rồi mới cho process thoát.
            Application.Current.MainWindow?.Close();
        });
    }


    public Task InitializeApplicationAsync()
    {
        lock (_startupGate)
        {
            return _startupTask ??= InitializeApplicationCoreAsync();
        }
    }

    private async Task InitializeApplicationCoreAsync()
    {
        Status = "ĐANG NẠP MÃ HÀNG VÀ KẾT NỐI BO...";

        // TestViewModel thực hiện hai việc tự động: recovery/kết nối FTDI và
        // tải model gần nhất. SetModel sẽ đồng bộ model trở lại MainWindow.
        await Test.InitializeAsync();

        Home.Refresh();
        Raise(nameof(CurrentBoardCapacity));
        Raise(nameof(RequiredCardCount));
        Raise(nameof(ConfiguredCardCount));
        Raise(nameof(ConfiguredIoCapacity));
        Raise(nameof(ConfiguredIoStart));
        Raise(nameof(ConfiguredIoEnd));
        Raise(nameof(HasEnoughCardsForModel));

        if (Test.IsBoardConnected && Model is not null)
            Status = $"SẴN SÀNG - {Model.ModelName} - BO ĐÃ KẾT NỐI";
        else if (Test.IsBoardConnected)
            Status = "BO ĐÃ KẾT NỐI - CHƯA CÓ MÃ HÀNG";
        else if (Model is not null)
            Status = $"ĐÃ NẠP {Model.ModelName} - BO CHƯA KẾT NỐI";
        else
            Status = "CHƯA CÓ MÃ HÀNG - BO CHƯA KẾT NỐI";
    }

    public async Task ShutdownAsync()
    {
        await _shutdownGate.WaitAsync();
        try
        {
            if (_shutdownCompleted)
                return;

            _shutdownCompleted = true;

            // Dừng mọi âm báo ngay để không còn TESTPOINT.wav sau khi UI đóng.
            AppSoundService.Current.StopAll();

            try
            {
                await Test.ShutdownAsync();
            }
            catch
            {
                // Tiếp tục giải phóng phần cứng còn lại.
            }

            try
            {
                _engine.Dispose();
            }
            catch
            {
            }

            try
            {
                _visa.Dispose();
            }
            catch
            {
            }

            try
            {
                await _board.DisposeAsync();
            }
            catch
            {
                // Khi process đang thoát, ưu tiên trả handle về OS hơn popup lỗi.
            }
        }
        finally
        {
            _shutdownGate.Release();
        }
    }

    public async Task<ProductModel?> LoadModelAsync(string path)
    {
        Status = "ĐANG NẠP MÃ HÀNG...";
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Không tìm thấy file mã hàng.", full);

        ProductModel? model;
        string extension = Path.GetExtension(full).ToLowerInvariant();
        ProductBundle? bundle = null;
        PiLegacyModel? piModel = null;
        PiSetupProfile? piSetup = null;

        if (full.EndsWith(".jbzproduct.json", StringComparison.OrdinalIgnoreCase))
        {
            bundle = ProductBundle.Load(full);
            BoardMode effective = EffectiveBoardModeForModelSelection();
            if (effective == BoardMode.UartTtl)
            {
                if (string.IsNullOrWhiteSpace(bundle.UartModelPath) || !File.Exists(bundle.UartModelPath))
                    throw new InvalidDataException($"Bundle {bundle.PartNumber} chưa có file .model cho JBZ UART TTL.");
                piModel = await Task.Run(() => PiLegacyModelParser.Load(bundle.UartModelPath));
                model = await Test.LoadPreparedModelAsync(piModel.ToProductModel());
                if (!string.IsNullOrWhiteSpace(bundle.UartSetupPath) && File.Exists(bundle.UartSetupPath))
                    piSetup = await Task.Run(() => PiSetupProfile.Load(bundle.UartSetupPath));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(bundle.D2xxThtPath) || !File.Exists(bundle.D2xxThtPath))
                    throw new InvalidDataException($"Bundle {bundle.PartNumber} chưa có file .tht cho JBZ D2XX.");
                model = await Test.LoadSelectedModelFromPathAsync(bundle.D2xxThtPath);
            }
        }
        else if (extension == ".model")
        {
            piModel = await Task.Run(() => PiLegacyModelParser.Load(full));
            model = await Test.LoadPreparedModelAsync(piModel.ToProductModel());
            string setupPath = ResolvePiSetupPath(full);
            if (File.Exists(setupPath))
                piSetup = await Task.Run(() => PiSetupProfile.Load(setupPath));
        }
        else
        {
            model = await Test.LoadSelectedModelFromPathAsync(full);
        }

        if (model is null) return null;

        Model = model;
        Home.Refresh();
        Raise(nameof(CurrentBoardCapacity));
        Raise(nameof(RequiredCardCount));
        Raise(nameof(ConfiguredCardCount));
        Raise(nameof(ConfiguredIoCapacity));
        Raise(nameof(ConfiguredIoStart));
        Raise(nameof(ConfiguredIoEnd));
        Raise(nameof(HasEnoughCardsForModel));
        Status = $"MODEL ĐÃ TẢI: {model.ModelName}";

        if (!HasEnoughCardsForModel) ShowCardCapacityWarning();

        await EnsureBoardSpecificModelAsync(model, piModel, piSetup, bundle);
        ExplicitModelLoaded?.Invoke(model);
        return model;
    }

    private BoardMode EffectiveBoardModeForModelSelection()
    {
        if (_board is UnifiedBoardTransport unified && unified.ActiveMode != BoardMode.Auto)
            return unified.ActiveMode;
        return _productionSettings.BoardMode == BoardMode.UartTtl ? BoardMode.UartTtl : BoardMode.D2xx;
    }

    private static string ResolvePiSetupPath(string modelPath)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(modelPath)) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(modelPath);
        return Path.Combine(dir, stem + ".setup");
    }

    private async Task EnsureBoardSpecificModelAsync(
        ProductModel model,
        PiLegacyModel? piModel = null,
        PiSetupProfile? piSetup = null,
        ProductBundle? bundle = null)
    {
        if (_board is not IFirmwareProtocolBoard firmware || !firmware.UsesFirmwareCycleResult)
            return;

        UartModelProfile profile;
        if (piModel is not null)
        {
            profile = await Task.Run(() => PiLegacyModelCompiler.Compile(piModel));
        }
        else
        {
            string profilePath = ResolveUartProfilePath(model);
            if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
                throw new InvalidDataException(
                    $"Mã hàng {model.PartNumber ?? model.ModelName} chưa có cấu hình JBZ UART TTL. " +
                    "V15 ưu tiên file .model gốc của Pi; profile V14 chỉ còn là fallback tương thích.");
            profile = Path.GetExtension(profilePath).Equals(".model", StringComparison.OrdinalIgnoreCase)
                ? await Task.Run(() => PiLegacyModelCompiler.Compile(PiLegacyModelParser.Load(profilePath)))
                : await Task.Run(() => UartModelProfile.Load(profilePath));
        }

        if (piSetup is not null)
        {
            string linked = Path.GetFileNameWithoutExtension(piSetup.LinkedModelPath.Replace('\\','/'));
            string selected = Path.GetFileNameWithoutExtension(profile.SourcePath);
            if (!string.IsNullOrWhiteSpace(linked) && !string.Equals(NormalizeModelKey(linked), NormalizeModelKey(selected), StringComparison.OrdinalIgnoreCase))
                Test.AddExternalLog($"UART SETUP WARNING: setup link='{piSetup.LinkedModelPath}', selected model='{profile.SourcePath}'.");
            Test.AddExternalLog($"UART SETUP: {Path.GetFileName(piSetup.SourcePath)}; Barcode={(piSetup.BarcodeEnabled ? "ON" : "OFF")}; PreTest={(piSetup.PreTestEnabled ? "ON" : "OFF")}; PinChange={piSetup.PinChangeCurrent}/{piSetup.PinChangeCount}");
        }

        string boardModel = string.Empty;
        try { boardModel = await firmware.QueryModelNameAsync(); }
        catch (Exception ex) { Test.AddExternalLog($"UART MODELNAME warning: {ex.Message}"); }

        if (!string.Equals(NormalizeModelKey(boardModel), NormalizeModelKey(profile.ModelName), StringComparison.OrdinalIgnoreCase))
        {
            Status = $"ĐANG ĐỒNG BỘ MODEL UART: {profile.ModelName}";
            await firmware.UploadModelProfileAsync(profile);
            string verify = string.Empty;
            try { verify = await firmware.QueryModelNameAsync(); } catch { }
            if (!string.IsNullOrWhiteSpace(verify) && !string.Equals(NormalizeModelKey(verify), NormalizeModelKey(profile.ModelName), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Upload UART xong nhưng MODELNAME='{verify}', mong '{profile.ModelName}'.");
            _productionSettings.LastUartModelPath = profile.SourcePath;
            ProductionConfigService.Save(_productionSettings);
            Test.AddExternalLog($"UART MODEL SYNC: board='{boardModel}' -> pc='{profile.ModelName}' ({Path.GetFileName(profile.SourcePath)})");
        }
        else
        {
            Test.AddExternalLog($"UART MODEL OK: {boardModel}; không upload lại.");
        }
    }

    private string ResolveUartProfilePath(ProductModel model)
    {
        var candidates = new List<string>();
        // V14 safety: profile cùng stem với mã hàng đang chọn luôn ưu tiên trước.
        // LastUartModelPath chỉ là fallback và chỉ dùng nếu tên model/profile phù hợp.
        string d2xxPath = model.SourcePath;
        string sourceStem = string.Empty;
        if (!string.IsNullOrWhiteSpace(d2xxPath))
        {
            string full = Path.GetFullPath(d2xxPath);
            string? dir = Path.GetDirectoryName(full);
            sourceStem = Path.GetFileNameWithoutExtension(full);
            if (dir is not null)
            {
                candidates.Add(Path.Combine(dir, sourceStem + ".model"));
                candidates.Add(Path.Combine(dir, sourceStem + ".profile.json"));
                candidates.Add(Path.Combine(dir, sourceStem + ".uart.txt"));
                candidates.Add(Path.Combine(dir, sourceStem + ".protocol.txt"));
            }
        }

        string lastPath = _productionSettings.LastUartModelPath;
        if (!string.IsNullOrWhiteSpace(lastPath) && File.Exists(lastPath))
        {
            string lastStem = UartProfileStem(lastPath);
            string normalizedLast = NormalizeModelKey(lastStem);
            string[] validKeys = [sourceStem, model.PartNumber, model.ModelName];

            if (validKeys.Any(key =>
                    !string.IsNullOrWhiteSpace(key) &&
                    string.Equals(normalizedLast, NormalizeModelKey(key), StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(lastPath);
            }
            else
            {
                Test.AddExternalLog(
                    $"UART PROFILE SAFETY: bỏ qua LastUartModelPath '{Path.GetFileName(lastPath)}' " +
                    $"vì không khớp mã hàng hiện tại '{model.PartNumber ?? model.ModelName}'.");
            }
        }

        return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static string UartProfileStem(string path)
    {
        string file = Path.GetFileName(path);
        foreach (string suffix in new[] { ".profile.json", ".uart.txt", ".protocol.txt", ".model" })
        {
            if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return file[..^suffix.Length];
        }

        return Path.GetFileNameWithoutExtension(file);
    }

    private static string NormalizeModelKey(string? value) =>
        new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public void ReloadProductionSettings()
    {
        ProductionConfigService.ReloadInto(_productionSettings);
        Test.RefreshProductionConfiguration();
        RefreshSettingsBindings();
    }

    /// <summary>
    /// V12.9: dùng sau khi Save trang Cài đặt. Ngoài reload file còn stop/restart
    /// scan để capacity mới thực sự đi xuống transport/decoder/TestView.
    /// </summary>
    public async Task ReloadProductionSettingsAsync()
    {
        BoardMode oldBoardMode = _productionSettings.BoardMode;
        string oldUartPort = _productionSettings.UartPort ?? string.Empty;

        ProductionConfigService.ReloadInto(_productionSettings);

        bool boardSelectionChanged = oldBoardMode != _productionSettings.BoardMode ||
            !string.Equals(oldUartPort, _productionSettings.UartPort ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (boardSelectionChanged)
            await Test.ReconnectBoardForSettingsAsync();
        else
            await Test.RefreshProductionConfigurationAsync();

        RefreshSettingsBindings();
    }

    private void RefreshSettingsBindings()
    {
        Home.Refresh();

        Raise(nameof(CurrentBoardCapacity));
        Raise(nameof(RequiredCardCount));
        Raise(nameof(ConfiguredCardCount));
        Raise(nameof(ConfiguredIoCapacity));
        Raise(nameof(ConfiguredIoStart));
        Raise(nameof(ConfiguredIoEnd));
        Raise(nameof(HasEnoughCardsForModel));
    }

    public bool EnsureModelCardCapacity(bool showWarning = true)
    {
        if (Model is null)
            return false;

        // Đọc setting mới nhất ngay trước khi test/probe.
        ReloadProductionSettings();

        if (HasEnoughCardsForModel)
            return true;

        if (showWarning)
            ShowCardCapacityWarning();

        return false;
    }

    private void ShowCardCapacityWarning()
    {
        if (Model is null)
            return;

        string extra = Model.MaxIo > BoardCapacity.MaxGlobalIo
            ? $"Model vượt giới hạn {BoardCapacity.MaxGlobalIo} I/O của hệ thống hiện tại."
            : $"Hãy vào CÀI ĐẶT -> Card mở rộng và chọn tối thiểu {RequiredCardCount} card trước khi test.";

        MessageBox.Show(
            $"THÔNG TIN PIN / CARD\n\n" +
            $"Số I/O của mã hàng : {Model.MaxIo}\n" +
            $"Card mở rộng cần    : {RequiredCardCount}\n" +
            $"Card mở rộng hiện có: {ConfiguredCardCount}\n" +
            $"Vùng I/O hiện tại   : {ConfiguredIoStart} - {ConfiguredIoEnd} ({ConfiguredIoCapacity} I/O)\n\n" +
            extra,
            "Thiếu card I/O",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
