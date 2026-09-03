using System.Collections.ObjectModel;
using System.IO;
using JBZUniversalTester.Core;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.ViewModels;

public sealed class ProductionSettingsViewModel : ObservableObject
{
    private int _masterFaultRequiredCount;
    private readonly TestViewModel? _test;
    private readonly string _modelPath;
    private readonly string _lotProductKey;
    private bool _manualRuntimeActive;
    private string _manualRelay1Status = "OFF";
    private string _manualRelay2Status = "OFF";
    private string _manualStatus = "Manual OFF";
    private int _selectedManualResistanceChannel;
    private bool _manualResistanceRunning;
    private string _manualResistanceStatus = "Chọn TẤT CẢ hoặc một CH để đo";

    public ProductionSettings Settings { get; }
    public ObservableCollection<ResistanceChannelEditor> ResistanceChannels { get; }
    public ObservableCollection<ResistanceResult> ManualResistanceResults { get; } = new();
    public WaterProofModelSettings WaterProof { get; }
    public IReadOnlyList<string> WaterProofConnectorOptions { get; }
    public string WaterProofModelKey =>
        ProductionConfigService.GetMasterModelKeyFromPath(_modelPath);
    public IReadOnlyList<ChannelOption> ChannelOptions { get; } =
    [
        new(0, "Không dùng"),
        new(1, "CH1"),
        new(2, "CH2"),
        new(3, "CH3"),
        new(4, "CH4"),
        new(5, "CH5"),
        new(6, "CH6"),
        new(7, "CH7"),
        new(8, "CH8"),
        new(9, "CH9"),
        new(10, "CH10")
    ];
    public IReadOnlyList<ChannelOption> ManualResistanceOptions { get; } =
    [
        new(0, "TẤT CẢ CH ĐÃ BẬT"),
        new(1, "CH1"),
        new(2, "CH2"),
        new(3, "CH3"),
        new(4, "CH4"),
        new(5, "CH5"),
        new(6, "CH6"),
        new(7, "CH7"),
        new(8, "CH8"),
        new(9, "CH9"),
        new(10, "CH10")
    ];

    public string MasterModelKey =>
        ProductionConfigService.GetMasterModelKeyFromPath(_modelPath);

    public int MasterFaultRequiredCount
    {
        get => _masterFaultRequiredCount;
        set => Set(ref _masterFaultRequiredCount, Math.Clamp(value, 0, 99));
    }

    public bool IsManualPanelVisible => true;

    public bool ManualRuntimeActive
    {
        get => _manualRuntimeActive;
        private set
        {
            if (Set(ref _manualRuntimeActive, value))
                RefreshManualCommands();
        }
    }

    public string ManualRelay1Status
    {
        get => _manualRelay1Status;
        private set => Set(ref _manualRelay1Status, value);
    }

    public string ManualRelay2Status
    {
        get => _manualRelay2Status;
        private set => Set(ref _manualRelay2Status, value);
    }

    public string ManualStatus
    {
        get => _manualStatus;
        private set => Set(ref _manualStatus, value);
    }

    public int SelectedManualResistanceChannel
    {
        get => _selectedManualResistanceChannel;
        set => Set(ref _selectedManualResistanceChannel, Math.Clamp(
            value,
            ResistanceMeasurementPlan.DisabledChannel,
            D2xxResistanceRouting.MaxChannel));
    }

    public string ManualResistanceStatus
    {
        get => _manualResistanceStatus;
        private set => Set(ref _manualResistanceStatus, value);
    }

    public AsyncRelayCommand ManualRelay1OnCommand { get; }
    public AsyncRelayCommand ManualRelay1OffCommand { get; }
    public AsyncRelayCommand ManualRelay2OnCommand { get; }
    public AsyncRelayCommand ManualRelay2OffCommand { get; }
    public AsyncRelayCommand ManualResetCommand { get; }
    public AsyncRelayCommand ManualMeasureResistanceCommand { get; }

    public ProductionSettingsViewModel(TestViewModel? test = null)
    {
        _test = test;
        Settings = ProductionConfigService.Load();
        _modelPath = test?.CurrentModelPath ?? Settings.LastThtPath;
        _lotProductKey = ProductionConfigService.GetLotProductKey(
            test?.PartNumber,
            _modelPath,
            test?.ModelName);
        ProductLotSettings productLot = ProductionConfigService.GetOrCreateProductLot(
            Settings,
            _lotProductKey,
            migrateCurrentLot: true);
        // Trường trên màn Cài đặt là LOTNO bắt đầu, không phải LOT kế tiếp đã
        // tăng trong quá trình sản xuất.
        Settings.LotNo = productLot.StartLotNo;
        Settings.LotNoDate = productLot.LotNoDate;
        if (!string.IsNullOrWhiteSpace(_modelPath))
            Settings.LastThtPath = _modelPath;
        Settings.ManualModeEnabled = false;
        _manualRuntimeActive = test?.IsManualModeActive == true;
        _manualStatus = "Sẵn sàng thao tác tay - không cần lưu cài đặt";
        ResistanceChannels = new ObservableCollection<ResistanceChannelEditor>(
            Settings.ResistanceChannels.Select((setting, index) =>
                new ResistanceChannelEditor(setting, index + 1)));
        WaterProof = ProductionConfigService.GetWaterProofProfileForPath(
            Settings, _modelPath);
        WaterProofConnectorOptions = LoadWaterProofConnectorOptions(test, _modelPath);
        _masterFaultRequiredCount = ProductionConfigService.GetMasterFaultRequiredCountForPath(
            Settings, _modelPath);

        ManualRelay1OnCommand = new AsyncRelayCommand(
            async () => await RunManualRelayCommandAsync(1, true),
            CanUseManualControls);
        ManualRelay1OffCommand = new AsyncRelayCommand(
            async () => await RunManualRelayCommandAsync(1, false),
            CanUseManualControls);
        ManualRelay2OnCommand = new AsyncRelayCommand(
            async () => await RunManualRelayCommandAsync(2, true),
            CanUseManualControls);
        ManualRelay2OffCommand = new AsyncRelayCommand(
            async () => await RunManualRelayCommandAsync(2, false),
            CanUseManualControls);
        ManualResetCommand = new AsyncRelayCommand(
            RunManualResetAsync,
            CanUseManualControls);
        ManualMeasureResistanceCommand = new AsyncRelayCommand(
            RunManualResistanceAsync,
            CanUseManualResistance);
    }

    private static IReadOnlyList<string> LoadWaterProofConnectorOptions(
        TestViewModel? test,
        string? thtPath)
    {
        IReadOnlyList<string> current = test?.CurrentConnectorIds ?? [];
        if (current.Count > 0)
            return current;

        if (string.IsNullOrWhiteSpace(thtPath) || !File.Exists(thtPath))
            return [];

        try
        {
            return new ThtModelParser().Load(thtPath.Trim()).Connectors
                .Select(connector => connector.ConnectorId)
                .Where(connector => !string.IsNullOrWhiteSpace(connector))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AsyncFileLogService.Current.Error(
                $"Không đọc được danh sách connector THT cho cấu hình Leak: {ex.Message}");
            return [];
        }
    }

    public void SetManualRuntimeActive(bool active)
    {
        ManualRuntimeActive = active;
        ManualStatus = active
            ? "MANUAL - production locked"
            : "Manual OFF";
        if (!active)
        {
            ManualRelay1Status = "OFF";
            ManualRelay2Status = "OFF";
        }
    }

    private bool CanUseManualControls() =>
        _test is not null &&
        !_test.IsDeviceFault &&
        !_manualResistanceRunning &&
        (_test.IsManualModeActive || _test.CanEnterManualMode);

    private bool CanUseManualResistance() => CanUseManualControls();

    private async Task RunManualRelayCommandAsync(int relay, bool turnOn)
    {
        if (_test is null)
            return;

        ManualStatus = turnOn
            ? $"Đang thử Relay {relay}..."
            : $"Đang tắt Relay {relay}...";

        try
        {
            int activeRelay = await _test.SetManualRelayAsync(relay, turnOn);
            ManualRuntimeActive = _test.IsManualModeActive;
            ManualRelay1Status = activeRelay == 1 ? "ON" : "OFF";
            ManualRelay2Status = activeRelay == 2 ? "ON" : "OFF";
            ManualStatus = activeRelay == 0
                ? "MANUAL - tất cả relay OFF"
                : $"MANUAL - Relay {activeRelay} ON";
            RefreshManualCommands();
        }
        catch
        {
            ManualRelay1Status = "OFF";
            ManualRelay2Status = "OFF";
            ManualStatus = "MANUAL FAULT - kiểm tra DeviceFault";
            RefreshManualCommands();
            throw;
        }
    }

    private async Task RunManualResetAsync()
    {
        if (_test is null)
            return;

        ManualStatus = "Đang reset manual...";
        try
        {
            await _test.ResetManualOutputsAsync();
            ManualRuntimeActive = _test.IsManualModeActive;
            ManualRelay1Status = "OFF";
            ManualRelay2Status = "OFF";
            ManualStatus = "MANUAL - reset complete, relay OFF";
            RefreshManualCommands();
        }
        catch
        {
            ManualRelay1Status = "OFF";
            ManualRelay2Status = "OFF";
            ManualStatus = "MANUAL FAULT - kiểm tra DeviceFault";
            RefreshManualCommands();
            throw;
        }
    }

    private async Task RunManualResistanceAsync()
    {
        if (_test is null)
            return;

        var snapshot = new ProductionSettings
        {
            ResistanceChannels = ResistanceChannels
                .Select(editor => editor.ToSetting())
                .ToArray()
        };
        List<ResistanceStep> steps = ResistanceMeasurementPlan.BuildManualSteps(
            snapshot,
            SelectedManualResistanceChannel);
        if (steps.Count == 0)
        {
            throw new InvalidOperationException(SelectedManualResistanceChannel == 0
                ? "Chưa bật kênh điện trở nào. Hãy tích BẬT cho ít nhất một dòng R."
                : $"CH{SelectedManualResistanceChannel} chưa được gán cho dòng R nào.");
        }

        _manualResistanceRunning = true;
        ManualResistanceResults.Clear();
        for (int index = 0; index < steps.Count; index++)
        {
            ResistanceStep step = steps[index];
            ManualResistanceResults.Add(new ResistanceResult
            {
                Name = step.Name,
                Channel = step.Channel,
                MinOhm = step.MinOhm,
                MaxOhm = step.MaxOhm,
                MeasurementStatus = index == 0 ? "ĐANG ĐO" : "CHỜ ĐO"
            });
        }
        ManualResistanceStatus =
            $"ĐANG ĐO {string.Join(", ", steps.Select(step => $"{step.Name}/CH{step.Channel}"))}...";
        RefreshManualCommands();

        try
        {
            IReadOnlyList<ResistanceResult> results =
                await _test.MeasureManualResistanceAsync(
                    steps,
                    UpdateManualResistanceResult);
            foreach (ResistanceResult result in results)
                UpdateManualResistanceResult(result);

            int passed = results.Count(result => result.Passed);
            ManualResistanceStatus =
                $"HOÀN THÀNH {results.Count} KÊNH • PASS {passed} • FAIL {results.Count - passed}";
        }
        catch (Exception ex)
        {
            for (int index = 0; index < ManualResistanceResults.Count; index++)
            {
                ResistanceResult current = ManualResistanceResults[index];
                if (current.ResultText != "ĐANG ĐO")
                    continue;

                ManualResistanceResults[index] = new ResistanceResult
                {
                    Name = current.Name,
                    Channel = current.Channel,
                    MinOhm = current.MinOhm,
                    MaxOhm = current.MaxOhm,
                    MeasurementStatus = "LỖI"
                };
            }
            ManualResistanceStatus = $"LỖI ĐO: {ex.Message}";
            throw;
        }
        finally
        {
            _manualResistanceRunning = false;
            ManualRuntimeActive = _test.IsManualModeActive;
            RefreshManualCommands();
        }
    }

    private void UpdateManualResistanceResult(ResistanceResult update)
    {
        int index = -1;
        for (int candidate = 0; candidate < ManualResistanceResults.Count; candidate++)
        {
            ResistanceResult current = ManualResistanceResults[candidate];
            if (current.Channel == update.Channel &&
                string.Equals(current.Name, update.Name, StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
                break;
            }
        }

        if (index >= 0)
            ManualResistanceResults[index] = update;
        else
            ManualResistanceResults.Add(update);

        if (update.ResultText == "ĐANG ĐO")
        {
            ManualResistanceStatus =
                $"ĐANG ĐO {update.Name}/CH{update.Channel} • chờ giá trị ổn định...";
        }
    }

    public void Save()
    {
        Settings.ExpansionCardCount = Math.Clamp(
            Settings.ExpansionCardCount,
            1,
            BoardIoDecoder.MaxExpansionCardCount);
        Settings.StartCardNumber = Math.Clamp(
            Settings.StartCardNumber,
            1,
            BoardCapacity.MaxExpansionCardCount);
        Settings.ExpansionCardCount = Math.Min(
            Settings.ExpansionCardCount,
            BoardCapacity.MaxExpansionCardCount - Settings.StartCardNumber + 1);
        Settings.CardCount = BoardCapacity.FromSettings(Settings).ScanCardCount;
        Settings.ResistanceChannels = ResistanceChannels
            .Select(editor => editor.ToSetting())
            .ToArray();
        Settings.AutoMasterSequence = true;
        // Manual là thao tác runtime tức thời, không phải cấu hình cần lưu.
        Settings.ManualModeEnabled = false;
        ProductionConfigService.SetMasterFaultRequiredCountForPath(
            Settings, _modelPath, MasterFaultRequiredCount);
        ProductionConfigService.SetWaterProofProfileForPath(
            Settings, _modelPath, WaterProof);
        ProductionConfigService.SetProductLot(
            Settings,
            _lotProductKey,
            Settings.LotNo,
            DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        ProductionConfigService.Save(Settings);
    }

    private void RefreshManualCommands()
    {
        ManualRelay1OnCommand?.RaiseCanExecuteChanged();
        ManualRelay1OffCommand?.RaiseCanExecuteChanged();
        ManualRelay2OnCommand?.RaiseCanExecuteChanged();
        ManualRelay2OffCommand?.RaiseCanExecuteChanged();
        ManualResetCommand?.RaiseCanExecuteChanged();
        ManualMeasureResistanceCommand?.RaiseCanExecuteChanged();
    }
}

public sealed record ChannelOption(int Value, string Display);

public sealed class ResistanceChannelEditor : ObservableObject
{
    private bool _enabled;
    private int _channelSelection;
    private double _minOhm;
    private double _maxOhm;

    public string Name { get; }
    public string Label { get; }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public int ChannelSelection
    {
        get => _channelSelection;
        set => Set(ref _channelSelection, Math.Clamp(
            value,
            0,
            D2xxResistanceRouting.MaxChannel));
    }

    public double MinOhm
    {
        get => _minOhm;
        set => Set(ref _minOhm, value);
    }

    public double MaxOhm
    {
        get => _maxOhm;
        set => Set(ref _maxOhm, value);
    }

    public ResistanceChannelEditor(ResistanceChannelSetting setting, int ordinal)
    {
        Name = $"R{ordinal}";
        Label = Name;
        _enabled = setting.Enabled;
        _channelSelection = Math.Clamp(
            setting.Channel,
            0,
            D2xxResistanceRouting.MaxChannel);
        _minOhm = setting.MinOhm;
        _maxOhm = setting.MaxOhm;
    }

    public ResistanceChannelSetting ToSetting() => new()
    {
        Enabled = Enabled,
        Name = Name,
        Channel = ChannelSelection,
        MinOhm = MinOhm,
        MaxOhm = MaxOhm
    };
}
