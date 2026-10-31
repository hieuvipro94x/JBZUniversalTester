using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    // Standalone UI fixture: creates no HWND and never raises Loaded, opens a
    // COM port or starts board discovery. Application resources match production.
    private static void TestSingleWindowNavigation()
    {
        OnSettingsDispatcher(async () =>
        {
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var doc = XDocument.Load(Path.Combine(Environment.CurrentDirectory, "App.xaml"));
            var xml = new XElement(ns + "ResourceDictionary", new XAttribute("xmlns", ns.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
                doc.Root!.Element(ns + "Application.Resources")!.Elements());
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xml.ToString());
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var main = new MainWindow(new MainViewModel());
            object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(main, args);
            var host = (ContentControl)main.FindName("InternalPageHost");
            var actions = (Grid)main.FindName("MainActionBar");
            try
            {
                Task abandonedOpen = (Task)Call("ShowSettingsPageAsync", JBZUniversalTester.Services.SettingsAccessSession.Administrator)!;
                await Dispatcher.Yield(DispatcherPriority.Input);
                Assert(host.Content is null && host.Visibility == Visibility.Collapsed && actions.IsEnabled,
                    "Main stays interactive while disconnected settings templates are prepared; no loading page is attached");
                await (Task)Call("CloseInternalPageAsync")!;
                await abandonedOpen;
                Assert(host.Content is null && actions.IsEnabled,
                    "Navigation changed during preparation cannot attach the stale settings page");
                await (Task)Call("ShowSettingsPageAsync", JBZUniversalTester.Services.SettingsAccessSession.Administrator)!;
                var settings = (ProductionSettingsPage)(host.Content ?? throw new InvalidOperationException("Settings page was not opened"));
                Assert(application.Windows.Count == 1 && main.OwnedWindows.Count == 0 && !actions.IsEnabled,
                    "Opening Settings attaches a page without constructing another Window");
                Assert(settings.PrepareForDisplayAsync().IsCompletedSuccessfully &&
                    ((Button)settings.FindName("SaveSettingsButton")).IsEnabled &&
                    new[] { "IoSettingsPanel", "RelayLeakMainPanel", "LabelSettingsPanel", "ResistanceSettingsPanel" }
                        .All(name => ((Border)settings.FindName(name)).Visibility == Visibility.Visible),
                    "Settings is attached only after all editor panels are ready, so its first visible frame has no staged reveal");
                await (Task)Call("ShowSettingsPageAsync", JBZUniversalTester.Services.SettingsAccessSession.Administrator)!;
                Call("OpenHistory_Click", main, new RoutedEventArgs());
                Assert(ReferenceEquals(host.Content, settings),
                    "Another page cannot replace active Settings before it is closed safely");
                await (Task)Call("CloseInternalPageAsync")!;
                Assert(host.Content is null && settings.DataContext is null && actions.IsEnabled,
                    "Closing Settings releases its bindings and restores main actions");
                Call("OpenHistory_Click", main, new RoutedEventArgs());
                var history = (HistoryPage)(host.Content ?? throw new InvalidOperationException("History page was not opened"));
                await (Task)Call("ShowSettingsPageAsync", JBZUniversalTester.Services.SettingsAccessSession.Administrator)!;
                Assert(ReferenceEquals(host.Content, history) && application.Windows.Count == 1 && !actions.IsEnabled,
                    "History occupies the same single window and must be closed before another page opens");
                await (Task)Call("CloseInternalPageAsync")!;
                Assert(host.Visibility == Visibility.Collapsed && actions.IsEnabled && history.DataContext is null,
                    "History close fully restores Main without an extra window or stale page");
                Assert(main.FindName("EditThtMainButton") is Button { Content: "SỬA CẤU HÌNH THT" },
                    "MainWindow has a direct native THT editor action");
                await (Task)Call("ShowSettingsPageCoreAsync", JBZUniversalTester.Services.SettingsAccessSession.Administrator, true)!;
                var editing = (ProductionSettingsPage)host.Content!;
                Assert(editing.IsThtEditorOpen && ((ContentControl)editing.FindName("ModelEditorHost")).Content is JBZUniversalTester.ModelEditor.ThtEditorPage &&
                       application.Windows.Count == 1 && main.OwnedWindows.Count == 0 && !actions.IsEnabled,
                    "Direct editor entry replaces Main in the existing window without showing a second settings/editor window");
                await editing.TryLeaveAsync();
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(host.Content is null && actions.IsEnabled, "Leaving an unchanged editor restores Main directly");
            }
            finally
            {
                await (Task)Call("CloseInternalPageAsync")!;
                typeof(MainWindow).GetField("_shutdownComplete", flags)!.SetValue(main, true);
                main.Close();
            }
        });
    }
}
