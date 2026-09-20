using System.Globalization;
using JBZUniversalTester.Core;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.ViewModels;

/// <summary>Runtime-only presentation model for one Leak run.</summary>
public sealed class WaterProofTestViewModel : ObservableObject
{
    private readonly WaterProofModelSettings _profile;
    private readonly double?[] _pressReference = new double?[3];
    private string _stageText = "PRESS";
    private string _channel1Text = "--";
    private string _channel2Text = "--";
    private string _channel3Text = "--";
    private bool _isRunning = true;

    public WaterProofTestViewModel(string modelName, WaterProofModelSettings profile)
    {
        _profile = profile.Clone();
        Title = $"KIỂM TRA ĐỘ RÒ RỈ - {modelName}";
    }

    public string Title { get; }
    public string StageText { get => _stageText; private set => Set(ref _stageText, value); }
    public string Channel1Text { get => _channel1Text; private set => Set(ref _channel1Text, value); }
    public string Channel2Text { get => _channel2Text; private set => Set(ref _channel2Text, value); }
    public string Channel3Text { get => _channel3Text; private set => Set(ref _channel3Text, value); }
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    public void ApplyProgress(WaterProofProgress progress)
    {
        if (!IsRunning)
            return;

        if (progress.Stage == WaterProofStage.Pressurizing)
        {
            StageText = "PRESS";
            for (int index = 0; index < 3 && index < progress.Values.Count; index++)
            {
                if (_profile.IsChannelEnabled(index + 1))
                    _pressReference[index] = Math.Abs(progress.Values[index]);
            }
            return;
        }

        if (progress.Stage != WaterProofStage.Waiting)
            return;

        StageText = "WAIT";
        for (int index = 0; index < 3 && index < progress.Values.Count; index++)
        {
            if (!_profile.IsChannelEnabled(index + 1) || !_pressReference[index].HasValue)
                continue;

            SetChannelText(index + 1, Math.Abs(_pressReference[index]!.Value - Math.Abs(progress.Values[index])));
        }
    }

    public void ApplyFinal(WaterProofRunResult result)
    {
        foreach (WaterProofChannelMeasurement measurement in result.Channels)
        {
            if (measurement.Enabled)
                SetChannelText(measurement.Channel, measurement.Leak);
        }

        StageText = result.Passed ? "PASS" : "FAIL";
        IsRunning = false;
    }

    public void SetTimeout() { StageText = "TIMEOUT"; IsRunning = false; }
    public void SetComError() { StageText = "LỖI COM"; IsRunning = false; }
    public void Cancel() { IsRunning = false; }

    private void SetChannelText(int channel, double value)
    {
        string text = value.ToString("0.0##", CultureInfo.InvariantCulture);
        switch (channel)
        {
            case 1: Channel1Text = text; break;
            case 2: Channel2Text = text; break;
            case 3: Channel3Text = text; break;
        }
    }
}
