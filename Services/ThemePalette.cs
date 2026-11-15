using System.Windows;
using System.Windows.Media;

namespace JBZUniversalTester.Services;

/// <summary>Shared theme for dialogs constructed in code as well as compiled XAML.</summary>
public static class ThemePalette
{
    public const string Source = "/JBZUniversalTester;component/Themes/Theme.xaml";
    private static readonly ThreadLocal<ResourceDictionary> Resources = new(() =>
        (ResourceDictionary)Application.LoadComponent(new Uri(Source, UriKind.Relative)));

    public static Brush Brush(string key) => (Brush)Resources.Value![key];

    public static void Apply(Window window)
    {
        ResourceDictionary shared = Resources.Value!;
        if (!window.Resources.MergedDictionaries.Any(dictionary => ReferenceEquals(dictionary, shared) || dictionary.Source?.OriginalString == Source))
            window.Resources.MergedDictionaries.Add(shared);
        window.SetResourceReference(Window.BackgroundProperty, "PageBackground");
        window.SetResourceReference(Window.ForegroundProperty, "TextPrimary");
    }
}
