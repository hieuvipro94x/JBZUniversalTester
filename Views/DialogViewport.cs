using System.Windows;
using System.Windows.Interop;

namespace JBZUniversalTester.Views;

/// <summary>Constrain auxiliary windows before their first frame, on the owner's monitor.</summary>
internal static class DialogViewport
{
    public static void Attach(Window window) => window.SourceInitialized += Fit;

    private static void Fit(object? sender, EventArgs e)
    {
        var window = (Window)sender!;
        window.SourceInitialized -= Fit;
        var handle = new WindowInteropHelper(window.Owner ?? window).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(handle);
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget is null) return;
        var transform = source.CompositionTarget.TransformFromDevice;
        ApplyBounds(window, new Rect(
            transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top)),
            transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom))));
    }

    internal static void ApplyBounds(Window window, Rect workArea)
    {
        workArea.Inflate(-8, -8);
        double width = Math.Max(1, workArea.Width), height = Math.Max(1, workArea.Height);
        window.MinWidth = Math.Min(window.MinWidth, width);
        window.MinHeight = Math.Min(window.MinHeight, height);
        window.MaxWidth = Math.Min(window.MaxWidth, width);
        window.MaxHeight = Math.Min(window.MaxHeight, height);
        if (!double.IsNaN(window.Width)) window.Width = Math.Min(window.Width, window.MaxWidth);
        if (!double.IsNaN(window.Height)) window.Height = Math.Min(window.Height, window.MaxHeight);
        if (window.Content is FrameworkElement content)
            content.Measure(new Size(window.MaxWidth, window.MaxHeight));
        double measuredWidth = (window.Content as FrameworkElement)?.DesiredSize.Width ?? width;
        double measuredHeight = (window.Content as FrameworkElement)?.DesiredSize.Height ?? height;
        double finalWidth = double.IsNaN(window.Width) ? Math.Min(width, measuredWidth + 16) : window.Width;
        double finalHeight = double.IsNaN(window.Height) ? Math.Min(height, measuredHeight + 40) : window.Height;
        Window? owner = window.Owner;
        double centerX = owner is null ? workArea.Left + width / 2 : owner.Left + owner.ActualWidth / 2;
        double centerY = owner is null ? workArea.Top + height / 2 : owner.Top + owner.ActualHeight / 2;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = Math.Clamp(centerX - finalWidth / 2, workArea.Left, workArea.Right - finalWidth);
        window.Top = Math.Clamp(centerY - finalHeight / 2, workArea.Top, workArea.Bottom - finalHeight);
    }
}
