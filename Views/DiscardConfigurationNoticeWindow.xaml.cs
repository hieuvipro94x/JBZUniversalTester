using System.IO;
using System.Windows;
using System.Windows.Media;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.Views;

public partial class DiscardConfigurationNoticeWindow : Window
{
    public DiscardConfigurationNoticeWindow(ProductModel model)
    {
        InitializeComponent();
        var display = DiscardConfigurationPresentation.For(model);
        var accent = (Brush)new BrushConverter().ConvertFromString(display.Color)!;
        ConfiguredHeaderText.Text = display.Title;
        ConfiguredHeaderText.Foreground = ConfiguredIoText.Foreground = accent;
        ConfiguredIoPanel.BorderBrush = accent;
        ConfiguredIoPanel.Background = (Brush)new BrushConverter().ConvertFromString(display.Background)!;
        ConfiguredFileText.Text = Path.GetFileName(model.SourcePath);
        ConfiguredIoText.Text = display.IoText;
        ConfiguredDescriptionText.Text = display.Description;
    }
    private void Continue_Click(object sender, RoutedEventArgs e) => Close();
}
