using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JBZUniversalTester.Services;

public static class RunningWindowActivator
{
    public static Window? FindTarget(IEnumerable<Window> windows, Window? mainWindow)
    {
        Window[] visible = windows.Where(window => window.IsVisible && window.Focusable).ToArray();
        Window? target = visible.LastOrDefault(window => window.IsActive)
            ?? (mainWindow?.IsVisible == true ? mainWindow : visible.LastOrDefault());
        // Keep an owned confirmation/password dialog above its disabled owner.
        while (target is not null)
        {
            Window? child = visible.LastOrDefault(window => ReferenceEquals(window.Owner, target));
            if (child is null) break;
            target = child;
        }
        return target;
    }

    public static bool Activate(Application application)
    {
        application.Dispatcher.VerifyAccess();
        Window? target = FindTarget(application.Windows.Cast<Window>(), application.MainWindow);
        if (target is null) return false;
        Activate(target);
        return true;
    }

    public static void Activate(Window target)
    {
        target.Dispatcher.VerifyAccess();
        var owners = new Stack<Window>();
        for (Window? owner = target.Owner; owner is not null; owner = owner.Owner) owners.Push(owner);
        foreach (Window owner in owners) Restore(owner);
        Restore(target);
        nint handle = new WindowInteropHelper(target).Handle;
        _ = target.Activate();
        _ = SetForegroundWindow(handle);
    }

    private static void Restore(Window window)
    {
        if (window.WindowState != WindowState.Minimized) return;
        nint handle = new WindowInteropHelper(window).Handle;
        _ = ShowWindow(handle, 9); // SW_RESTORE retains the native maximized state.
        // Owned WPF windows can retain their managed Minimized value after
        // their owner's native restore. Synchronize it with the actual HWND.
        if (window.WindowState == WindowState.Minimized)
            window.SetCurrentValue(Window.WindowStateProperty,
                IsZoomed(handle) ? WindowState.Maximized : WindowState.Normal);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint handle);
}
