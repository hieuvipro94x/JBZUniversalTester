using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    private static void TestSmoothOwnedWindowNavigation()
    {
        string navigation = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Views", "MainWindow.xaml.cs"));
        string picker = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "ViewModels", "HomeViewModel.cs"));
        Assert(!navigation.Contains("Hide();", StringComparison.Ordinal) && !picker.Contains(".Hide();", StringComparison.Ordinal),
            "Main and model-picker navigation preserve the already-rendered main window");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? main = null, child = null;
            try
            {
                Window Make() => new Window { Width = 1, Height = 1, Left = -30000,
                    Top = -30000, Opacity = 0, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                main = Make();
                main.Show();
                child = Make();
                child.Owner = main;
                child.Show();
                Assert(main.IsVisible && child.IsVisible &&
                       IsWindowVisible(new WindowInteropHelper(main).Handle) &&
                       IsWindowVisible(new WindowInteropHelper(child).Handle),
                    "The rendered main surface remains ready behind the child window");
                Assert(child.Owner == main, "Navigation retains ownership for safe application shutdown");
                child.Close();
                child = null;
                Assert(main.IsVisible && IsWindowVisible(new WindowInteropHelper(main).Handle),
                    "Closing the child reveals the existing main surface without a Hide/Show transition");
            }
            catch (Exception ex) { failure = ex; }
            finally { child?.Close(); main?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Smooth navigation regression", failure);
    }
}
