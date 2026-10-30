using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using JBZUniversalTester.Core;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.ViewModels;

public sealed class ProductionSettingsViewModel : ObservableObject
{
    private string _discardContactIo1Text = string.Empty;
    private string _discardContactIo2Text = string.Empty;
    public string DiscardContactIo1Text { get => _discardContactIo1Text; set => Set(ref _discardContactIo1Text, value); }
    public string DiscardContactIo2Text { get => _discardContactIo2Text; set => Set(ref _discardContactIo2Text, value); }
    private int _masterFaultRequiredCount;
    private MasterSampleSelection _masterSelectedFaultSamples = MasterSampleSelection.All;
    public MasterSampleSelection MasterSelectedFaultSamples => _masterSelectedFaultSamples;
    public bool MasterWrongWiringRequired
    {
        get => (_masterSelectedFaultSamples & MasterSampleSelection.WrongWiring) != 0;
        set => SetMasterSampleRequired(MasterSampleSelection.WrongWiring, value);
    }
    public bool MasterShortCircuitRequired
    {
        get => (_masterSelectedFaultSamples & MasterSampleSelection.ShortCircuit) != 0;
        set => SetMasterSampleRequired(MasterSampleSelection.ShortCircuit, value);
    }
    public bool MasterOpenCircuitRequired
    {
        get => (_masterSelectedFaultSamples & MasterSampleSelection.OpenCircuit) != 0;
        set => SetMasterSampleRequired(MasterSampleSelection.OpenCircuit, value);
    }
    public bool CanEditMasterWrongCount => MasterEnabled && MasterWrongWiringRequired;
    public bool CanEditMasterOpenCount => MasterEnabled && MasterOpenCircuitRequired;
    private void SetMasterSampleRequired(MasterSampleSelection flag, bool required)
    {
        _masterSelectedFaultSamples = required ? _masterSelectedFaultSamples | flag : _masterSelectedFaultSamples & ~flag;
        Settings.MasterSelectedFaultSamples = _masterSelectedFaultSamples & MasterSampleSelection.All;
        Raise(nameof(MasterSelectedFaultSamples));
        Raise(nameof(MasterWrongWiringRequired));
        Raise(nameof(MasterShortCircuitRequired));
        Raise(nameof(MasterOpenCircuitRequired));
        Raise(nameof(CanEditMasterWrongCount));
        Raise(nameof(CanEditMasterOpenCount));
    }
    private int _masterOpenFaultRequiredCount = 1;
    public int MasterExpectedConnectionCount => _test?.MasterExpectedConnectionCount ?? 0;
    public int MasterOpenFaultRequiredCount
    {
        get => _masterOpenFaultRequiredCount;
        set => Set(ref _masterOpenFaultRequiredCount, Math.Clamp(value, 1, 99));
    }
    public bool MasterEnabled
    {
        get => MasterFaultRequiredCount > 0;
        set => MasterFaultRequiredCount = value ? Math.Max(1, MasterFaultRequiredCount) : 0;
    }
    private readonly TestViewModel? _test;
    private readonly Action<ProductionSettings> _saveSettings;
    private SettingsAccessSession _accessSession = SettingsAccessSession.Administrator;
    private string _deniedSettingsSnapshot = string.Empty;
    private string _deniedEditorsSnapshot = string.Empty;

    public void SetAccessSession(SettingsAccessSession access)
    {
        _accessSession = access;
        _deniedSettingsSnapshot = SettingsPermissionPolicy.DeniedSnapshot(Settings, access);
        _deniedEditorsSnapshot = CaptureDeniedEditors();
        RefreshManualCommands();
    }

    private string CaptureDeniedEditors() => JsonSerializer.Serialize(new
    {
        Master = _accessSession.Allows(SettingsPermission.Io) ? null :
            new { MasterFaultRequiredCount, MasterSelectedFaultSamples, MasterOpenFaultRequiredCount, DiscardContactIo1Text, DiscardContactIo2Text },
        Resistance = _accessSession.Allows(SettingsPermission.Resistance) ? null :
            ResistanceChannels.Select(editor => editor.ToSetting()).ToArray(),
        Leak = _accessSession.Allows(SettingsPermission.Leak) ? null : WaterProof
    });

    public void ValidateAccessForSave()
    {
        if (HasWaterProofConfigurationError && _unavailableLeakSnapshot != CaptureLeakSettings())
            throw new InvalidOperationException("Cấu hình Leak chưa hợp lệ. Hãy khôi phục đúng cặp file THT và cấu hình Leak rồi mở lại Cài đặt.");
        if (_accessSession.IsAdmin) return;
        if (_accessSession.Permissions == SettingsPermission.None)
            throw new UnauthorizedAccessException("Bạn chỉ có quyền xem Cài đặt.");
        if (_deniedSettingsSnapshot != SettingsPermissionPolicy.DeniedSnapshot(Settings, _accessSession) ||
            _deniedEditorsSnapshot != CaptureDeniedEditors())
            throw new UnauthorizedAccessException("Có thay đổi trong nhóm bạn chưa được cấp quyền. Hãy đóng và mở lại Cài đặt.");
    }
    private CancellationTokenSource? _manualOperationCts;
    private Task _manualOperationCompletion = Task.CompletedTask;
    private bool _manualOperationRunning;
    private bool _manualControlsClosing;
    public bool IsManualOperationRunning => _manualOperationRunning;
    private readonly string _modelPath;
    private readonly string _lotProductKey;
    private bool _printLabelForCurrentProduct;
    public bool PrintLabelForCurrentProduct
    {
        get => _printLabelForCurrentProduct;
        set => Set(ref _printLabelForCurrentProduct, value);
    }
    private bool _manualRuntimeActive;
    private string _manualRelay1Status = "OFF";
    private string _manualRelay2Status = "OFF";
    private string _manualStatus = "Manual OFF";
    private int _selectedManualResistanceChannel;
    private bool _manualResistanceRunning;
    private string _manualResistanceStatus = "Chọn TẤT CẢ hoặc một CH để đo";
    private bool _manualWaterProofRunning;
    private string _manualWaterProofStatus = "Chọn COM và bấm CHẠY TEST";
    private readonly double?[] _manualWaterProofPressBaseline = new double?[3];

    public ProductionSettings Settings { get; }
    private string? _probeReplacementThresholdText;
    public string ProbeReplacementThresholdText
    {
        get => _probeReplacementThresholdText ?? Settings.ProbeReplacementThreshold.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));
        set
        {
            _probeReplacementThresholdText = value;
            if (TryParseProbeThreshold(value, out long threshold)) Settings.ProbeReplacementThreshold = threshold;
            Raise();
        }
    }
    public bool IsProbeReplacementThresholdValid => TryParseProbeThreshold(ProbeReplacementThresholdText, out _);
    private static bool TryParseProbeThreshold(string text, out long threshold)
    {
        threshold = 0;
        string[] groups = text.Trim().Split('.');
        if (groups.Length > 1 && (groups[0].Length is < 1 or > 3 || groups.Skip(1).Any(group => group.Length != 3))) return false;
        return long.TryParse(string.Concat(groups), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out threshold) && threshold > 0;
    }
    public ObservableCollection<ResistanceChannelEditor> ResistanceChannels { get; }
    public ObservableCollection<ResistanceResult> ManualResistanceResults { get; } = new();
    public ObservableCollection<WaterProofChannelResult> ManualWaterProofResults { get; } = new();
    public WaterProofModelSettings WaterProof { get; }
    public string WaterProofConfigurationError { get; } = string.Empty;
    public bool HasWaterProofConfigurationError => WaterProofConfigurationError.Length > 0;
    private readonly string _unavailableLeakSnapshot = string.Empty;
    private string CaptureLeakSettings() => JsonSerializer.Serialize(new
        { WaterProof, Settings.WaterProofMachine, Settings.WaterProofProfilesByModel, Settings.WaterproofSerialPort });
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
        set
        {
            if (Set(ref _masterFaultRequiredCount, Math.Clamp(value, 0, 99)))
            {
                Raise(nameof(MasterEnabled));
                Raise(nameof(CanEditMasterWrongCount));
                Raise(nameof(CanEditMasterOpenCount));
            }
        }
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

    public string ManualWaterProofStatus
    {
        get => _manualWaterProofStatus;
        private set => Set(ref _manualWaterProofStatus, value);
    }

    public AsyncRelayCommand ManualRelay1OnCommand { get; }
    public AsyncRelayCommand ManualRelay1OffCommand { get; }
    public AsyncRelayCommand ManualRelay2OnCommand { get; }
    public AsyncRelayCommand ManualRelay2OffCommand { get; }
    public AsyncRelayCommand ManualResetCommand { get; }
    public AsyncRelayCommand ManualMeasureResistanceCommand { get; }
    public AsyncRelayCommand ManualWaterProofTestCommand { get; }

    public ProductionSettingsViewModel(TestViewModel? test = null, Action<ProductionSettings>? saveSettings = null,
        ProductionSettings? initialSettings = null)
    {
        _test = test;
        _saveSettings = saveSettings ?? ProductionConfigService.Save;
        Settings = initialSettings ?? ProductionConfigService.Load();
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
        ProductionConfigService.ApplyProductLabelTemplate(Settings, _lotProductKey);
        _printLabelForCurrentProduct = ProductionConfigService.IsProductLabelPrintingEnabled(Settings, _lotProductKey);
        if (!string.IsNullOrWhiteSpace(_modelPath))
            Settings.LastThtPath = _modelPath;
        Settings.ManualModeEnabled = false;
        _manualRuntimeActive = test?.IsManualModeActive == true;
        _manualStatus = "Sẵn sàng thao tác tay - không cần lưu cài đặt";
        ResistanceChannels = new ObservableCollection<ResistanceChannelEditor>(
            ProductionConfigService.GetResistanceProfileForPath(Settings, _modelPath).Select((setting, index) =>
                new ResistanceChannelEditor(setting, index + 1)));
        try
        {
            WaterProof = ProductionConfigService.GetWaterProofProfileForPath(Settings, _modelPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Editor recovery only: never import suspect assignments, alter the
            // stored profile, or weaken production model validation.
            string key = ProductionConfigService.GetMasterModelKeyFromPath(_modelPath);
            WaterProof = Settings.WaterProofProfilesByModel.TryGetValue(key, out var saved)
                ? saved.Clone() : new WaterProofModelSettings();
            WaterProofConfigurationError = "Cấu hình Leak không đọc được, không hợp lệ hoặc không khớp THT. " +
                "Hãy chép đúng cặp THT và .tht.leak.json rồi mở lại Cài đặt. Các nhóm khác vẫn sử dụng được.";
            _unavailableLeakSnapshot = CaptureLeakSettings();
            AsyncFileLogService.Current.Error($"Settings Leak profile unavailable; other groups remain editable: model={_modelPath}; {ex}");
        }
        ResetManualWaterProofResults(WaterProof);
        WaterProofConnectorOptions = LoadWaterProofConnectorOptions(test, _modelPath);
        _masterFaultRequiredCount = ProductionConfigService.GetMasterFaultRequiredCountForPath(
            Settings, _modelPath);
        _masterSelectedFaultSamples = ProductionConfigService.GetMasterSelectedFaultSamplesForPath(Settings, _modelPath);
        _masterOpenFaultRequiredCount = ProductionConfigService.GetMasterOpenFaultRequiredCountForPath(Settings, _modelPath);
        _discardContactIo1Text = Settings.DiscardContactIo1 == 0 ? string.Empty : Settings.DiscardContactIo1.ToString();
        _discardContactIo2Text = Settings.DiscardContactIo2 == 0 ? string.Empty : Settings.DiscardContactIo2.ToString();

        ManualRelay1OnCommand = new AsyncRelayCommand(
            () => RunManualOperationAsync(_ => RunManualRelayCommandAsync(1, true)),
            CanUseManualRelay);
        ManualRelay1OffCommand = new AsyncRelayCommand(
            () => RunManualOperationAsync(_ => RunManualRelayCommandAsync(1, false)),
            CanUseManualRelay);
        ManualRelay2OnCommand = new AsyncRelayCommand(
            () => RunManualOperationAsync(_ => RunManualRelayCommandAsync(2, true)),
            CanUseManualRelay);
        ManualRelay2OffCommand = new AsyncRelayCommand(
            () => RunManualOperationAsync(_ => RunManualRelayCommandAsync(2, false)),
            CanUseManualRelay);
        ManualResetCommand = new AsyncRelayCommand(
            () => RunManualOperationAsync(_ => RunManualResetAsync()),
            CanUseManualRelay);
        ManualMeasureResistanceCommand = new AsyncRelayCommand(
            () => RunManualOperationAsync(RunManualResistanceAsync),
            CanUseManualResistance);
        ManualWaterProofTestCommand = new AsyncRelayCommand(
            () => RunManualOperationAsync(RunManualWaterProofAsync),
            CanUseManualWaterProof);
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
        !_manualOperationRunning && !_manualControlsClosing &&
        _test is not null &&
        !_test.IsDeviceFault &&
        !_manualResistanceRunning &&
        !_manualWaterProofRunning &&
        (_test.IsManualModeActive || _test.CanEnterManualMode);

    private bool CanUseManualRelay() => _accessSession.Allows(SettingsPermission.Relay) && CanUseManualControls();
    private bool CanUseManualResistance() => _accessSession.Allows(SettingsPermission.Resistance) && CanUseManualControls();

    private bool CanUseManualWaterProof() => !HasWaterProofConfigurationError &&
        _accessSession.Allows(SettingsPermission.Leak) && CanUseManualControls();

    private async Task RunManualOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (_manualOperationRunning || _manualControlsClosing)
            throw new InvalidOperationException("Đang xử lý thao tác tay hoặc đang đóng Cài đặt.");
        _manualOperationRunning = true;
        using var cancellation = new CancellationTokenSource();
        _manualOperationCts = cancellation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _manualOperationCompletion = completion.Task;
        Raise(nameof(IsManualOperationRunning));
        RefreshManualCommands();
        try { await operation(cancellation.Token); }
        finally
        {
            _manualOperationCts = null;
            _manualOperationRunning = false;
            completion.TrySetResult();
            Raise(nameof(IsManualOperationRunning));
            RefreshManualCommands();
        }
    }

    public async Task StopManualOperationsAsync(bool closing = false)
    {
        RequestStopManualOperations(closing);
        await _manualOperationCompletion;
        RefreshManualCommands();
    }

    public void RequestStopManualOperations(bool closing = false)
    {
        if (closing) _manualControlsClosing = true;
        _manualOperationCts?.Cancel();
        RefreshManualCommands();
    }

    private async Task RunManualWaterProofAsync(CancellationToken cancellationToken)
    {
        _accessSession.Demand(SettingsPermission.Leak);
        if (HasWaterProofConfigurationError)
            throw new InvalidOperationException("Cấu hình Leak chưa hợp lệ; chưa thể chạy máy Leak.");
        if (_test is null)
            return;

        string portName = Settings.WaterProofMachine.PortName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(portName))
            throw new InvalidOperationException("Hãy chọn cổng COM UART/RS232 của máy Leak trước khi chạy test.");
        if (!string.IsNullOrWhiteSpace(Settings.Label.PrinterCom) &&
            string.Equals(portName, Settings.Label.PrinterCom.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{portName} đang được cấu hình cho máy in tem. Máy Leak phải dùng một cổng COM riêng.");
        }
        if (WaterProof.EnabledChannelCount == 0)
            throw new InvalidOperationException("Hãy bật ít nhất một kênh CH1/CH2/CH3 trước khi chạy test.");
        if (WaterProof.PressTimeMs is < 1 or > 300000 ||
            WaterProof.WaitTimeMs is < 1 or > 300000)
        {
            throw new InvalidOperationException("Thời gian tạo áp/giữ áp phải từ 1 đến 300000 ms.");
        }

        var machine = new WaterProofMachineSettings
        {
            PortName = portName,
            BaudRate = WaterProofMachineSettings.DefaultBaudRate,
            AutoConnect = Settings.WaterProofMachine.AutoConnect,
            ReadTimeoutMs = Settings.WaterProofMachine.ReadTimeoutMs,
            WriteTimeoutMs = Settings.WaterProofMachine.WriteTimeoutMs
        };
        WaterProofModelSettings profile = WaterProof.Clone();

        _manualWaterProofRunning = true;
        ResetManualWaterProofResults(profile);
        ManualWaterProofStatus = $"ĐANG KẾT NỐI {portName} • 115200 8N1...";
        RefreshManualCommands();

        try
        {
            WaterProofRunResult result = await _test.TestManualWaterProofAsync(
                machine,
                profile,
                progress =>
                {
                    if (!cancellationToken.IsCancellationRequested && !_manualControlsClosing)
                        UpdateManualWaterProofProgress(portName, progress);
                },
                cancellationToken);
            ApplyManualWaterProofResult(result);
            string channels = string.Join(" • ", result.Channels
                .Where(channel => channel.Enabled)
                .Select(channel => $"CH{channel.Channel} {(channel.Passed ? "PASS" : "FAIL")} Δ{channel.Leak:0.###}"));
            ManualWaterProofStatus =
                $"TEST {(result.Passed ? "PASS" : "FAIL")} • {channels}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ManualWaterProofStatus = "ĐÃ DỪNG TEST LEAK";
        }
        catch (UnauthorizedAccessException ex)
        {
            ManualWaterProofStatus = $"{portName} ĐANG BỊ CHIẾM DỤNG";
            throw new InvalidOperationException(
                $"Không mở được {portName}: cổng đang bị chương trình khác chiếm dụng.",
                ex);
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"Manual Leak test failed: {ex}");
            ManualWaterProofStatus = $"TEST LEAK LỖI • {ex.Message}";
            throw;
        }
        finally
        {
            _manualWaterProofRunning = false;
            ManualRuntimeActive = _test.IsManualModeActive;
            RefreshManualCommands();
        }
    }

    private void UpdateManualWaterProofProgress(string portName, WaterProofProgress progress)
    {
        string stage = progress.Stage switch
        {
            WaterProofStage.Pressurizing => "ĐANG TẠO ÁP",
            WaterProofStage.Waiting => "ĐANG ĐO ĐỘ RÒ",
            WaterProofStage.Evaluating => "ĐANG ĐÁNH GIÁ",
            _ => "ĐANG TEST"
        };
        string values = progress.Values.Count == 0
            ? string.Empty
            : " • " + string.Join(" / ", progress.Values.Select(value =>
                value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        ManualWaterProofStatus = $"{stage} {portName}{values}";

        if (progress.Stage is not (WaterProofStage.Pressurizing or WaterProofStage.Waiting))
            return;

        int count = Math.Min(3, progress.Values.Count);
        for (int index = 0; index < count; index++)
        {
            WaterProofChannelResult row = ManualWaterProofResults[index];
            if (!row.Enabled)
                continue;

            double current = progress.Values[index];
            row.LiveMachineValue = current;
            if (progress.Stage == WaterProofStage.Pressurizing)
            {
                _manualWaterProofPressBaseline[index] = current;
                row.PressPressure = current;
                row.FirstResultPressure = current;
                row.Leak = 0.0;
            }
            else
            {
                row.WaitPressure = current;
                row.SecondResultPressure = current;
                if (_manualWaterProofPressBaseline[index] is double baseline)
                    row.Leak = Math.Abs(baseline - current);
            }
        }
    }

    private void ResetManualWaterProofResults(WaterProofModelSettings profile)
    {
        Array.Clear(_manualWaterProofPressBaseline, 0, _manualWaterProofPressBaseline.Length);
        ManualWaterProofResults.Clear();
        for (int channel = 1; channel <= 3; channel++)
        {
            ManualWaterProofResults.Add(new WaterProofChannelResult
            {
                Channel = channel,
                Enabled = profile.IsChannelEnabled(channel),
                Connector = profile.ConnectorForChannel(channel),
                LeakLimit = profile.LeakLimit
            });
        }
    }

    private void ApplyManualWaterProofResult(WaterProofRunResult result)
    {
        foreach (WaterProofChannelMeasurement measurement in result.Channels)
        {
            if (!measurement.Enabled)
                continue;

            WaterProofChannelResult? row = ManualWaterProofResults.FirstOrDefault(
                item => item.Channel == measurement.Channel);
            if (row is null)
                continue;

            row.PressPressure = measurement.FirstPressure;
            row.WaitPressure = measurement.SecondPressure;
            row.FirstResultPressure = measurement.FirstPressure;
            row.SecondResultPressure = measurement.SecondPressure;
            row.Leak = measurement.Leak;
            row.LiveMachineValue = measurement.Leak;
            row.Passed = measurement.Passed;
            row.IsMeasured = measurement.Enabled;
        }
    }

    private async Task RunManualRelayCommandAsync(int relay, bool turnOn)
    {
        _accessSession.Demand(SettingsPermission.Relay);
        if (_test is null)
            return;

        ManualStatus = turnOn
            ? $"Đang bật Relay {relay}..."
            : $"Đang tắt Relay {relay}...";

        try
        {
            int activeRelay = await _test.SetManualRelayAsync(relay, turnOn);
            if (!_test.IsBoardConnected || _test.IsDeviceFault)
            {
                ManualStatus = TesterConnectionNotice.Message;
                return;
            }
            ManualRuntimeActive = _test.IsManualModeActive;
            ManualRelay1Status = activeRelay == 1 ? "ON" : "OFF";
            ManualRelay2Status = activeRelay == 2 ? "ON" : "OFF";
            ManualStatus = activeRelay == 0
                ? "MANUAL - tất cả relay OFF"
                : $"MANUAL - Relay {activeRelay} ON";
            RefreshManualCommands();
        }
        catch (Exception ex)
        {
            ManualRelay1Status = "OFF";
            ManualRelay2Status = "OFF";
            ManualStatus = _test.IsDeviceFault || !_test.IsBoardConnected
                ? TesterConnectionNotice.Message : "MANUAL FAULT - kiểm tra DeviceFault";
            RefreshManualCommands();
            if (_test.IsDeviceFault || !_test.IsBoardConnected)
                throw new InvalidOperationException(TesterConnectionNotice.Message, ex);
            throw;
        }
    }

    private async Task RunManualResetAsync()
    {
        _accessSession.Demand(SettingsPermission.Relay);
        if (_test is null)
            return;

        ManualStatus = "Đang reset manual...";
        try
        {
            await _test.ResetManualOutputsAsync();
            if (!_test.IsBoardConnected || _test.IsDeviceFault)
            {
                ManualStatus = TesterConnectionNotice.Message;
                return;
            }
            ManualRuntimeActive = _test.IsManualModeActive;
            ManualRelay1Status = "OFF";
            ManualRelay2Status = "OFF";
            ManualStatus = "MANUAL - reset complete, relay OFF";
            RefreshManualCommands();
        }
        catch (Exception ex)
        {
            ManualRelay1Status = "OFF";
            ManualRelay2Status = "OFF";
            ManualStatus = _test.IsDeviceFault || !_test.IsBoardConnected
                ? TesterConnectionNotice.Message : "MANUAL FAULT - kiểm tra DeviceFault";
            RefreshManualCommands();
            if (_test.IsDeviceFault || !_test.IsBoardConnected)
                throw new InvalidOperationException(TesterConnectionNotice.Message, ex);
            throw;
        }
    }

    private async Task RunManualResistanceAsync(CancellationToken cancellationToken)
    {
        _accessSession.Demand(SettingsPermission.Resistance);
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
                    update =>
                    {
                        if (!cancellationToken.IsCancellationRequested && !_manualControlsClosing)
                            UpdateManualResistanceResult(update);
                    },
                    cancellationToken);
            if (!_test.IsBoardConnected || _test.IsDeviceFault)
            {
                ManualResistanceStatus = TesterConnectionNotice.Message;
                return;
            }
            foreach (ResistanceResult result in results)
                UpdateManualResistanceResult(result);

            int passed = results.Count(result => result.Passed);
            ManualResistanceStatus =
                $"HOÀN THÀNH {results.Count} KÊNH • PASS {passed} • FAIL {results.Count - passed}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ManualResistanceStatus = "ĐÃ DỪNG ĐO ĐIỆN TRỞ";
        }
        catch (Exception ex)
        {
            // Chỉ ghi message ngắn; không format toàn bộ stack trace trên UI path.
            AsyncFileLogService.Current.Error(
                $"Manual resistance measurement failed: {ex.GetBaseException().Message}");

            for (int index = 0; index < ManualResistanceResults.Count; index++)
            {
                ResistanceResult current = ManualResistanceResults[index];
                if (current.ResultText is not ("ĐANG ĐO" or "CHỜ ĐO"))
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

            // Lỗi bo/relay thật vẫn giữ DeviceFault toàn cục.
            if (_test.IsDeviceFault || !_test.IsBoardConnected)
            {
                ManualResistanceStatus = TesterConnectionNotice.Message;
                throw new InvalidOperationException(TesterConnectionNotice.Message, ex);
            }

            // Lỗi Keysight manual chỉ là lỗi thiết bị đo phụ. Không MessageBox modal,
            // không rethrow ra AsyncRelayCommand: UI cập nhật ngay và vẫn tương tác được.
            ManualResistanceStatus = "KHÔNG KẾT NỐI ĐƯỢC KEYSIGHT 34461A";
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

    private void PrepareSettingsForSave()
    {
        ValidateAccessForSave();
        if (!IsProbeReplacementThresholdValid)
            throw new InvalidOperationException("Chu kỳ thay PIN phải là số nguyên lớn hơn 0, ví dụ 200.000.");
        if (_accessSession.Allows(SettingsPermission.Io))
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
            int ParseDiscardIo(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return 0;
                if (int.TryParse(text, out int value)) return value;
                throw new InvalidOperationException("Chân IO thùng lỗi phải là số nguyên; nhập đủ hai IO hoặc để trống cả hai.");
            }
            int first = ParseDiscardIo(DiscardContactIo1Text), second = ParseDiscardIo(DiscardContactIo2Text);
            ThtModelParser.ValidateDiscardContacts(first, second, BoardCapacity.FromSettings(Settings).TotalIoCapacity);
            Settings.DiscardContactIo1 = first;
            Settings.DiscardContactIo2 = second;
        }
        if (_accessSession.Allows(SettingsPermission.Resistance))
        {
            Settings.ResistanceChannels = ResistanceChannels
                .Select(editor => editor.ToSetting())
                .ToArray();
            ProductionConfigService.SetResistanceProfileForPath(
                Settings, _modelPath, Settings.ResistanceChannels);
        }
        if (_accessSession.Allows(SettingsPermission.Io))
        {
            Settings.AutoMasterSequence = true;
            // Manual là thao tác runtime tức thời, không phải cấu hình cần lưu.
            Settings.ManualModeEnabled = false;
            ProductionConfigService.SetMasterFaultRequiredCountForPath(
                Settings, _modelPath, MasterFaultRequiredCount);
            ProductionConfigService.SetMasterSelectedFaultSamplesForPath(Settings, _modelPath, MasterSelectedFaultSamples);
            ProductionConfigService.SetMasterOpenFaultRequiredCountForPath(Settings, _modelPath, MasterOpenFaultRequiredCount);
            ProductionConfigService.SetProductLot(
                Settings,
                _lotProductKey,
                Settings.LotNo,
                DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        if (_accessSession.Allows(SettingsPermission.Leak) && !HasWaterProofConfigurationError)
            ProductionConfigService.SetWaterProofProfileForPath(Settings, _modelPath, WaterProof);
        if (_accessSession.Allows(SettingsPermission.Label))
            Settings.LabelTemplateTypesByProduct[_lotProductKey] =
                PrintLabelForCurrentProduct ? LabelProfileResolver.NormalizeTemplateType(Settings.Label.TemplateType)
                    : ProductionConfigService.NoProductLabel;
    }

    public void Save()
    {
        PrepareSettingsForSave();
        _saveSettings(Settings);
    }

    public async Task SaveAsync()
    {
        PrepareSettingsForSave();
        // Freeze the editor DTO before the disk worker normalizes/writes it.
        // No WPF binding or shared runtime settings are accessed by that worker.
        ProductionSettings snapshot = JsonSerializer.Deserialize<ProductionSettings>(
            JsonSerializer.SerializeToUtf8Bytes(Settings))!;
        await Task.Run(() => _saveSettings(snapshot));
        ProductionConfigService.CopyInto(Settings, snapshot);
    }

    private void RefreshManualCommands()
    {
        ManualRelay1OnCommand?.RaiseCanExecuteChanged();
        ManualRelay1OffCommand?.RaiseCanExecuteChanged();
        ManualRelay2OnCommand?.RaiseCanExecuteChanged();
        ManualRelay2OffCommand?.RaiseCanExecuteChanged();
        ManualResetCommand?.RaiseCanExecuteChanged();
        ManualMeasureResistanceCommand?.RaiseCanExecuteChanged();
        ManualWaterProofTestCommand?.RaiseCanExecuteChanged();
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
