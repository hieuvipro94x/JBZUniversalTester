using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JBZUniversalTester.Services;

internal static class LocalizedMessageBox
{
    public static MessageBoxResult Show(string language, Window? owner, string text, string caption,
        MessageBoxButton buttons, MessageBoxImage image)
    {
        if (TestViewText.NormalizeLanguage(language) != "ko")
            return owner is null ? MessageBox.Show(text, caption, buttons, image) : MessageBox.Show(owner, text, caption, buttons, image);
        MessageBoxResult result = buttons == MessageBoxButton.OK ? MessageBoxResult.OK :
            buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = TestViewText.Translate(text, language), TextWrapping = TextWrapping.Wrap,
            FontSize = 16, Foreground = Brushes.Black, MaxWidth = 560 });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0) };
        panel.Children.Add(actions);
        var window = new Window { Title = TestViewText.Translate(caption, language), Owner = owner, Width = 620,
            SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = Brushes.White,
            FontFamily = new FontFamily("Malgun Gothic"),
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        void Add(string label, MessageBoxResult value, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button { Content = label, MinWidth = 90, Height = 36, Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault, IsCancel = isCancel, FontSize = 15 };
            button.Click += (_, _) => { result = value; window.Close(); };
            actions.Children.Add(button);
        }
        if (buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel)
        {
            Add("예", MessageBoxResult.Yes, isDefault: true);
            Add("아니요", MessageBoxResult.No);
        }
        else Add("확인", MessageBoxResult.OK, isDefault: true);
        if (buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel)
            Add("취소", MessageBoxResult.Cancel, isCancel: true);
        window.ShowDialog();
        return result;
    }

    public static MessageBoxResult Show(string language, string text, string caption, MessageBoxButton buttons, MessageBoxImage image) =>
        Show(language, null, text, caption, buttons, image);
}
