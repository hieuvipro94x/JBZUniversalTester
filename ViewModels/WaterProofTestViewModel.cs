using System.Globalization;
using JBZUniversalTester.Core;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.ViewModels;

/// <summary>Runtime-only presentation model for one Leak run.</summary>
public sealed class WaterProofTestViewModel : ObservableObject
{
    private const string ActiveBackground = "#FFF3A0";
    private const string DisabledBackground = "#E5E7EB";
    private const string PassBackground = "#32CD32";
    private const string FailBackground = "#FF4040";

    private readonly WaterProofModelSettings _profile;
    private readonly double?[] _pressReference = new double?[3];

    private string _stageText = "PRESS";
    private string _channel1Text = "--";
    private string _channel2Text = "--";
    private string _channel3Text = "--";
    private string _channel1Background = DisabledBackground;
    private string _channel2Background = DisabledBackground;
    private string _channel3Background = DisabledBackground;
    private bool _isRunning;

    public WaterProofTestViewModel(string modelName, WaterProofModelSettings profile)
    {
        _profile = profile.Clone();
        Title = $"KIỂM TRA ĐỘ RÒ RỈ - {modelName}";
        BeginRun();
    }

    public string Title { get; }
    public string StageText { get => _stageText; private set => Set(ref _stageText, value); }
    public string Channel1Text { get => _channel1Text; private set => Set(ref _channel1Text, value); }
    public string Channel2Text { get => _channel2Text; private set => Set(ref _channel2Text, value); }
    public string Channel3Text { get => _channel3Text; private set => Set(ref _channel3Text, value); }
    public string Channel1Background { get => _channel1Background; private set => Set(ref _channel1Background, value); }
    public string Channel2Background { get => _channel2Background; private set => Set(ref _channel2Background, value); }
    public string Channel3Background { get => _channel3Background; private set => Set(ref _channel3Background, value); }
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    /// <summary>
    /// Re-arms the same compact Leak window for a connector retest on the same product.
    /// The window remains owned by the current physical product until ProductRemoved.
    /// </summary>
    public void BeginRun()
    {
        Array.Clear(_pressReference, 0, _pressReference.Length);
        StageText = "PRESS";
        Channel1Text = "--";
        Channel2Text = "--";
        Channel3Text = "--";
        Channel1Background = _profile.IsChannelEnabled(1) ? ActiveBackground : DisabledBackground;
        Channel2Background = _profile.IsChannelEnabled(2) ? ActiveBackground : DisabledBackground;
        Channel3Background = _profile.IsChannelEnabled(3) ? ActiveBackground : DisabledBackground;
        IsRunning = true;
    }

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
                {
                    _pressReference[index] = Math.Abs(progress.Values[index]);
                    SetChannelText(index + 1, progress.Values[index]);
                }
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

            SetChannelText(index + 1, progress.Values[index]);
        }
    }

    public void ApplyFinal(WaterProofRunResult result)
    {
        if (!IsRunning)
            return;

        foreach (WaterProofChannelMeasurement measurement in result.Channels)
        {
            if (!measurement.Enabled || !_profile.IsChannelEnabled(measurement.Channel))
                continue;

            SetChannelText(measurement.Channel, measurement.Leak);
            SetChannelBackground(
                measurement.Channel,
                measurement.Passed ? PassBackground : FailBackground);
        }

        StageText = result.Passed ? "PASS" : "FAIL";
        IsRunning = false;
    }

    public void SetTimeout()
    {
        StageText = "TIMEOUT";
        IsRunning = false;
    }

    public void SetComError()
    {
        StageText = "LỖI COM";
        IsRunning = false;
    }

    public void Cancel() => IsRunning = false;

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

    private void SetChannelBackground(int channel, string value)
    {
        switch (channel)
        {
            case 1: Channel1Background = value; break;
            case 2: Channel2Background = value; break;
            case 3: Channel3Background = value; break;
        }
    }

}
