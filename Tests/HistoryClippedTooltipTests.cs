using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestHistoryClippedTooltips()
    {
        OnSettingsDispatcher(async () =>
        {
            var method = typeof(HistoryPage).GetMethod("IsHistoryTextClipped", BindingFlags.Static | BindingFlags.NonPublic)!;
            bool Clipped(TextBlock text) => (bool)method.Invoke(null, [text])!;
            var text = new TextBlock { Text = "FULL NAME.tht", ToolTip = "FULL NAME.tht", FontSize = 13,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center };
            void Layout(double width)
            {
                text.Measure(new Size(width, 32));
                text.Arrange(new Rect(0, 0, width, 32));
            }
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            Layout(300);
            Assert(!Clipped(text), "A complete filename does not open a tooltip");
            Layout(35);
            Assert(Clipped(text) && Equals(text.ToolTip, text.Text), "Truncated text opens its full-value tooltip without removing the binding");
            Layout(300);
            Assert(!Clipped(text), "The same recycled text element stops opening tooltips after it fits again");
            text.Text = string.Empty; Layout(35);
            Assert(!Clipped(text), "Empty barcode cells never open a tooltip");
            text.Text = "FULL NAME.tht";
            text.Width = 300; text.Height = 32;
            var viewport = new Border { Child = text, ClipToBounds = true };
            viewport.Measure(new Size(40, 32)); viewport.Arrange(new Rect(0, 0, 40, 32));
            Assert(text.ActualWidth == 300 && Clipped(text), "An ancestor viewport clipping the text is detected even when the cell itself is wide enough");
            viewport.Measure(new Size(300, 32)); viewport.Arrange(new Rect(0, 0, 300, 32));
            viewport.Clip = new RectangleGeometry(new Rect(0, 0, 40, 32));
            Assert(Clipped(text), "Explicit cell/viewport geometry is included in visible text bounds");
            viewport.Clip = new RectangleGeometry(new Rect(80, 0, 150, 32));
            Assert(!Clipped(text), "Clipping cell whitespace alone does not open a tooltip for centered text");

            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var document = XDocument.Load(Path.Combine(Environment.CurrentDirectory, "Views", "HistoryPage.xaml"));
            var grid = document.Descendants(ns + "DataGrid").Single();
            Assert((string?)grid.Attribute("ToolTipOpening") == "HistoryGrid_ToolTipOpening" &&
                   !grid.Element(ns + "DataGrid.RowStyle")!.Descendants(ns + "Setter")
                       .Any(setter => (string?)setter.Attribute("Property") == "ToolTip"),
                "History handles tooltip opening on demand and has no whole-row tooltip");
            var log = grid.Descendants(ns + "TextBlock").Single(node =>
                (string?)node.Attribute("Text") == "{Binding HistoryTestLogSingleLineText}");
            Assert((string?)log.Attribute("ToolTip") == "{Binding HistoryTestLogText}",
                "Truncated History log exposes the original complete multiline log");
        });
    }
}
