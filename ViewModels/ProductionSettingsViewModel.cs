using System.Collections.ObjectModel;
using JBZUniversalTester.Core;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.ViewModels;

public sealed class ProductionSettingsViewModel : ObservableObject
{
    private int _masterFaultRequiredCount;
    private readonly TestViewModel? _test;
    private bool _manualRuntimeActive;
    private string _manualRelay1Status = "OFF";
    private string _manualRelay2Status = "OFF";
    private string _manualStatus = "Manual OFF";

    public ProductionSettings Settings { get; }
    public ObservableCollection<ResistanceChannelEditor> ResistanceChannels { get; }
    public IReadOnlyList<ChannelOption> ChannelOptions { get; } =
    [
        new(0, "Không dùng"),
        new(1, "1"),
        new(2, "2"),
        new(3, "3"),
        new(4, "4"),
        new(5, "5")
    ];

    public string MasterModelKey =>
        ProductionConfigService.GetMasterModelKeyFromPath(Settings.LastThtPath);

    public int MasterFaultRequiredCount
    {
        get => _masterFaultRequiredCount;
        set => Set(ref _masterFaultRequiredCount, Math.Clamp(value, 0, 99));
    }

    public bool IsManualModeEnabled
    {
        get => Settings.ManualModeEnabled;
        set
        {
            if (Settings.ManualModeEnabled == value)
                return;

            Settings.ManualModeEnabled = value;
            Raise();
            Raise(nameof(IsManualPanelVisible));
            RefreshManualCommands();
        }
    }

    public bool IsManualPanelVisible => IsManualModeEnabled;

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

    public AsyncRelayCommand ManualRelay1OnCommand { get; }
    public AsyncRelayCommand ManualRelay1OffCommand { get; }
    public AsyncRelayCommand ManualRelay2OnCommand { get; }
    public AsyncRelayCommand ManualRelay2OffCommand { get; }
    public AsyncRelayCommand ManualResetCommand { get; }

    public ProductionSettingsViewModel(TestViewModel? test = null)
    {
        _test = test;
        Settings = ProductionConfigService.Load();
        _manualRuntimeActive = test is not null && Settings.ManualModeEnabled;
        _manualStatus = Settings.ManualModeEnabled
            ? "MANUAL - chờ lệnh bảo trì"
            : "Manual OFF";
        ResistanceChannels = new ObservableCollection<ResistanceChannelEditor>(
            Settings.ResistanceChannels.Select((setting, index) =>
                new ResistanceChannelEditor(setting, index + 1)));
        _masterFaultRequiredCount = ProductionConfigService.GetMasterFaultRequiredCountForPath(
            Settings, Settings.LastThtPath);

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
        ManualRuntimeActive && _test is not null && !_test.IsDeviceFault;

    private async Task RunManualRelayCommandAsync(int relay, bool turnOn)
    {
        if (_test is null)
            return;

        ManualStatus = turnOn
            ? $"Đang bật Relay {relay}..."
            : $"Đang tắt Relay {relay}...";

        try
        {
            int activeRelay = await _test.SetManualRelayAsync(relay, turnOn);
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

    public void Save()
    {
        Settings.ExpansionCardCount = Math.Clamp(
            Settings.ExpansionCardCount,
            1,
            BoardIoDecoder.MaxExpansionCardCount);
        Settings.CardCount = BoardIoDecoder.ScanCardCountFromExpansionCards(
            Settings.ExpansionCardCount);
        Settings.ResistanceChannels = ResistanceChannels
            .Select(editor => editor.ToSetting())
            .ToArray();
        Settings.AutoMasterSequence = true;
        Settings.ManualModeEnabled = IsManualModeEnabled;
        ProductionConfigService.SetMasterFaultRequiredCountForPath(
            Settings, Settings.LastThtPath, MasterFaultRequiredCount);
        ProductionConfigService.Save(Settings);
    }

    private void RefreshManualCommands()
    {
        ManualRelay1OnCommand?.RaiseCanExecuteChanged();
        ManualRelay1OffCommand?.RaiseCanExecuteChanged();
        ManualRelay2OnCommand?.RaiseCanExecuteChanged();
        ManualRelay2OffCommand?.RaiseCanExecuteChanged();
        ManualResetCommand?.RaiseCanExecuteChanged();
    }
}

public sealed record ChannelOption(int Value, string Display);

public sealed class ResistanceChannelEditor : ObservableObject
{
    private int _channelSelection;
    private double _minOhm;
    private double _maxOhm;

    public string Name { get; }
    public string Label { get; }

    public int ChannelSelection
    {
        get => _channelSelection;
        set => Set(ref _channelSelection, Math.Clamp(value, 0, 5));
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
        Name = string.IsNullOrWhiteSpace(setting.Name)
            ? $"R{ordinal}"
            : setting.Name;
        Label = $"R{ordinal}";
        _channelSelection = setting.Enabled
            ? Math.Clamp(setting.Channel, 1, 5)
            : 0;
        _minOhm = setting.MinOhm;
        _maxOhm = setting.MaxOhm;
    }

    public ResistanceChannelSetting ToSetting() => new()
    {
        Enabled = ChannelSelection is >= 1 and <= 5,
        Name = Name,
        Channel = ChannelSelection is >= 1 and <= 5 ? ChannelSelection : 0,
        MinOhm = MinOhm,
        MaxOhm = MaxOhm
    };
}
