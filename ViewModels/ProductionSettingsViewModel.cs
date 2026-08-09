using System.Collections.ObjectModel;
using JBZUniversalTester.Core;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.ViewModels;

public sealed class ProductionSettingsViewModel : ObservableObject
{
    private int _masterFaultRequiredCount;

    public ProductionSettings Settings { get; }
    public ObservableCollection<ResistanceChannelSetting> ResistanceChannels { get; }

    public string MasterModelKey =>
        ProductionConfigService.GetMasterModelKeyFromPath(Settings.LastThtPath);


    public bool UseAutoBoard
    {
        get => Settings.BoardMode == BoardMode.Auto;
        set { if (value) SetBoardMode(BoardMode.Auto); }
    }

    public bool UseD2xxBoard
    {
        get => Settings.BoardMode == BoardMode.D2xx;
        set { if (value) SetBoardMode(BoardMode.D2xx); }
    }

    public bool IsD2xxSettingsEnabled => Settings.BoardMode != BoardMode.UartTtl;
    public bool IsUartSettingsEnabled => Settings.BoardMode != BoardMode.D2xx;

    public bool UseUartTtlBoard
    {
        get => Settings.BoardMode == BoardMode.UartTtl;
        set { if (value) SetBoardMode(BoardMode.UartTtl); }
    }

    private void SetBoardMode(BoardMode mode)
    {
        if (Settings.BoardMode == mode)
            return;
        Settings.BoardMode = mode;
        Raise(nameof(UseAutoBoard));
        Raise(nameof(UseD2xxBoard));
        Raise(nameof(UseUartTtlBoard));
        Raise(nameof(IsD2xxSettingsEnabled));
        Raise(nameof(IsUartSettingsEnabled));
    }

    public int MasterFaultRequiredCount
    {
        get => _masterFaultRequiredCount;
        set => Set(ref _masterFaultRequiredCount, Math.Clamp(value, 1, 99));
    }

    public ProductionSettingsViewModel()
    {
        Settings = ProductionConfigService.Load();
        ResistanceChannels = new ObservableCollection<ResistanceChannelSetting>(Settings.ResistanceChannels);
        _masterFaultRequiredCount = ProductionConfigService.GetMasterFaultRequiredCountForPath(
            Settings, Settings.LastThtPath);
    }

    public void Save()
    {
        Settings.ExpansionCardCount = Math.Clamp(
            Settings.ExpansionCardCount,
            1,
            BoardIoDecoder.MaxExpansionCardCount);
        Settings.CardCount = BoardIoDecoder.ScanCardCountFromExpansionCards(
            Settings.ExpansionCardCount);
        Settings.ResistanceChannels = ResistanceChannels.ToArray();
        Settings.AutoMasterSequence = true;
        ProductionConfigService.SetMasterFaultRequiredCountForPath(
            Settings, Settings.LastThtPath, MasterFaultRequiredCount);
        ProductionConfigService.Save(Settings);
    }
}
