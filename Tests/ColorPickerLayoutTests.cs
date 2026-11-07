using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Pages;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestColorPickerLayout(string? imagePath = null)
    {
        OnSettingsDispatcher(async () =>
        {
            var page = new ColorPage();
            var implicitButton = new Style(typeof(Button));
            implicitButton.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(18, 8, 18, 8)));
            implicitButton.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(4)));
            implicitButton.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 46d));
            page.Resources[typeof(Button)] = implicitButton;
            page.Colors.Clear();
            page.Colors.Add(new WireColor { Code = "O", Name = "Cam", Value = "#FFA500" });
            var grid = (DataGrid)page.FindName("Grid");
            grid.SelectedItem = page.Colors[0];
            using var host = new HwndSource(new HwndSourceParameters("Color picker layout self-test")
                { Width = 978, Height = 822, WindowStyle = unchecked((int)0x80000000) });
            host.RootVisual = page;
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            page.Measure(new Size(978, 822)); page.Arrange(new Rect(0, 0, 978, 822)); page.UpdateLayout();
            var button = (Button)page.FindName("ColorButton");
            button.ApplyTemplate();
            var border = (Border)button.Template.FindName("PickerBorder", button);
            var label = (TextBlock)button.Content;
            var input = (TextBox)page.FindName("CodeBox");
            var buttonOrigin = button.TranslatePoint(new Point(), page);
            var inputOrigin = input.TranslatePoint(new Point(), page);
            var gridOrigin = grid.TranslatePoint(new Point(), page);
            Assert(Math.Abs(buttonOrigin.X - inputOrigin.X) < 0.1 && Math.Abs(button.ActualHeight - 44) < 0.1 &&
                   buttonOrigin.Y + button.ActualHeight <= gridOrigin.Y - 10,
                "Color swatch aligns with the inputs and leaves a visible gap above the table");
            Assert(Math.Abs(border.ActualWidth - button.ActualWidth) < 0.1 &&
                   Math.Abs(border.ActualHeight - button.ActualHeight) < 0.1 && border.BorderThickness == new Thickness(1) &&
                   label.ActualWidth < border.ActualWidth && label.ActualHeight < border.ActualHeight,
                "All four swatch borders and its HEX label fit inside the allocated button");
            Assert(page.SelectedHex == "#FFA500" && label.Text == "#FFA500" &&
                   border.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(255, 165, 0),
                "Selecting a palette row updates the swatch and HEX label");
            if (imagePath is not null)
            {
                var bitmap = new RenderTargetBitmap(978, 822, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(page);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(imagePath); encoder.Save(stream);
            }
        });
    }
}
