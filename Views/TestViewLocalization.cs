using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

/// <summary>One-time view setup. Raw VM states, trigger values and product bindings stay unchanged.</summary>
internal static class TestViewLocalization
{
    public static readonly DependencyProperty LanguageProperty = DependencyProperty.RegisterAttached(
        "Language", typeof(string), typeof(TestViewLocalization), new FrameworkPropertyMetadata("vi", FrameworkPropertyMetadataOptions.Inherits));
    public static string GetLanguage(DependencyObject element) => (string)element.GetValue(LanguageProperty);
    public static void SetLanguage(DependencyObject element, string language) => element.SetValue(LanguageProperty, language);

    public static void Apply(Window window, string language)
    {
        language = TestViewText.NormalizeLanguage(language);
        SetLanguage(window, language);
        if (language != "ko") return;
        window.FontFamily = new FontFamily("Malgun Gothic, Segoe UI");
        window.Title = TestViewText.Translate(window.Title, language);
        var converter = new DisplayConverter(language);
        var visited = new HashSet<DependencyObject>();
        Visit(window, converter, visited);
    }

    private static void Visit(DependencyObject node, DisplayConverter converter, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node)) return;
        if (node is TextBlock block)
        {
            if (block.Tag is "KeepProductData") return;
            if (block.Inlines.Any(inline => inline is not Run))
            {
                foreach (Inline inline in block.Inlines.ToArray()) Visit(inline, converter, visited);
            }
            else if (BindingOperations.GetBinding(block, TextBlock.TextProperty) is not null)
                LocalizeProperty(block, TextBlock.TextProperty, converter);
            else if (block.Name is not ("ModelText" or "CurrentCyclesText" or "ThresholdText"))
                LocalizeProperty(block, TextBlock.TextProperty, converter);
            // Generated Runs may contain bound part/wire data. Never traverse those as UI literals.
            return;
        }
        else if (node is Run run) LocalizeProperty(run, Run.TextProperty, converter);
        else if (node is ButtonBase button) LocalizeProperty(button, ContentControl.ContentProperty, converter);
        if (node is FrameworkElement element && element.ToolTip is string tooltip)
            element.ToolTip = TestViewText.Translate(tooltip, converter.Language);
        if (node is DataGrid grid)
        {
            foreach (DataGridColumn column in grid.Columns)
            {
                if (column.Header is string header) column.Header = TestViewText.Translate(header, converter.Language);
                if (column is DataGridBoundColumn bound && bound.Binding is Binding binding && IsDisplayPath(binding.Path?.Path))
                    bound.Binding = DisplayBinding(binding, converter);
            }
        }
        foreach (object child in LogicalTreeHelper.GetChildren(node).Cast<object>().ToArray())
            if (child is DependencyObject dependency) Visit(dependency, converter, visited);
        // Includes already-created dialog item templates; future rows use column bindings.
        if (node is Visual)
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i), converter, visited);
    }

    private static bool IsDisplayPath(string? path) => path is
        "ResultStatusText" or "CenterResultText" or "Status" or "FaultType" or "ResultText" or
        "ProbeMaintenanceStatus" or "Title" or "Name" or "Display" or "Channel1Text" or "Channel2Text" or "Channel3Text";

    private static Binding DisplayBinding(Binding original, DisplayConverter converter) => new(original.Path.Path)
    {
        Mode = BindingMode.OneWay,
        Converter = converter,
        ConverterParameter = original.Path.Path,
        StringFormat = original.StringFormat,
        FallbackValue = original.FallbackValue,
        TargetNullValue = original.TargetNullValue
    };

    private static void LocalizeProperty(DependencyObject node, DependencyProperty property, DisplayConverter converter)
    {
        if (BindingOperations.GetBinding(node, property) is Binding binding)
        {
            if (IsDisplayPath(binding.Path?.Path)) BindingOperations.SetBinding(node, property, DisplayBinding(binding, converter));
            return;
        }
        if (node.GetValue(property) is string text && text.Length > 0)
            node.SetCurrentValue(property, TestViewText.Translate(text, converter.Language));
    }

    private sealed class DisplayConverter(string language) : IValueConverter
    {
        public string Language { get; } = language;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is "PASS" && parameter is "ResultStatusText" or "CenterResultText") return "PASS";
            if (parameter is "Name" && value is not "TEST LEAK") return value;
            string text = TestViewText.Translate(value as string, Language);
            // Break at Korean word boundaries inside the narrow large-status panel.
            return parameter is "ResultStatusText" ? text
                .Replace("제품을 장착하세요", "제품을\n장착하세요", StringComparison.Ordinal)
                .Replace("제품을 탈거하세요", "제품을\n탈거하세요", StringComparison.Ordinal) : text;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
