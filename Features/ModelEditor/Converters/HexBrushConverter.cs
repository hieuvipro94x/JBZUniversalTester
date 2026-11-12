using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JBZUniversalTester.ModelEditor.Converters;

public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return Brushes.Transparent;
        try { return (Brush)new BrushConverter().ConvertFromString(text)!; }
        catch (Exception ex)
        {
            JBZUniversalTester.Services.AsyncFileLogService.Current.Error($"THT editor invalid color '{text}': {ex.Message}");
            return Brushes.Transparent;
        }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
