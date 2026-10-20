using System.ComponentModel;
using JBZUniversalTester.Core;

namespace JBZUniversalTester.Models;

// A live display adapter; the original measurement objects remain authoritative.
public sealed class InspectionResultRow : ObservableObject, IDisposable
{
    private readonly ResistanceResult? _resistance;
    private readonly WaterProofChannelResult? _leak;
    private readonly INotifyPropertyChanged _source;
    public InspectionResultRow(ResistanceResult resistance)
    {
        _resistance = resistance;
        _source = resistance;
        _source.PropertyChanged += SourceChanged;
    }
    public InspectionResultRow(WaterProofChannelResult leak)
    {
        _leak = leak;
        _source = leak;
        _source.PropertyChanged += SourceChanged;
    }
    public bool IsResistance => _resistance is not null;
    public string Name => _resistance?.Name ?? "TEST LEAK";
    public string ChannelText => _resistance?.ChannelText ?? _leak!.ChannelText;
    public string MinDisplayText => _resistance?.MinDisplayText ?? "—";
    public string Display => _resistance?.Display ?? "—";
    public string MaxDisplayText => _resistance?.MaxDisplayText ?? "—";
    public string FirstPressureText => _leak?.FirstPressureText ?? "—";
    public string SecondPressureText => _leak?.SecondPressureText ?? "—";
    public string LeakText => _leak?.LeakText ?? "—";
    public string LeakLimitText => _leak?.LeakLimitText ?? "—";
    public string ResultText => _resistance?.ResultText ?? _leak!.ResultText;
    private void SourceChanged(object? sender, PropertyChangedEventArgs e) => Raise(null);
    public void Dispose() => _source.PropertyChanged -= SourceChanged;
}
