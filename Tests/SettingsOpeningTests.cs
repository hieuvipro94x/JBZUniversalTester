using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSettingsOpeningResponsiveness()
    {
        OnSettingsDispatcher(async () =>
        {
            foreach (int width in new[] { 1366, 1920 })
            {
                var vm = new ProductionSettingsViewModel(saveSettings: _ => throw new InvalidOperationException("Opening cannot save settings"));
                var page = new ProductionSettingsPage(null, vm, deferInitialLayout: true);
                Border[] panels = new[] { "IoSettingsPanel", "RelayLeakMainPanel", "LabelSettingsPanel", "ResistanceSettingsPanel" }
                    .Select(name => (Border)page.FindName(name)).ToArray();
                var save = (Button)page.FindName("SaveSettingsButton");
                Assert(panels.All(panel => panel.Visibility == Visibility.Collapsed) && !save.IsEnabled,
                    "First settings frame does not synchronously lay out every editor or allow a premature save");
                var events = new List<string>();
                var batches = new List<double>();
                var descriptor = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Border));
                var handlers = new List<(Border Panel, EventHandler Handler)>();
                foreach (Border panel in panels)
                {
                    EventHandler handler = (_, _) =>
                    {
                        events.Add(panel.Name);
                        long batchStarted = Stopwatch.GetTimestamp();
                        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input,
                            new Action(() => { events.Add("INPUT"); batches.Add(Stopwatch.GetElapsedTime(batchStarted).TotalMilliseconds); }));
                    };
                    descriptor.AddValueChanged(panel, handler);
                    handlers.Add((panel, handler));
                }
                page.Measure(new Size(width, 1000));
                page.Arrange(new Rect(0, 0, width, 1000));
                page.UpdateLayout();
                var clock = Stopwatch.StartNew();
                Task opening = page.PrepareForDisplayAsync();
                Assert(!opening.IsCompleted && ReferenceEquals(opening, page.PrepareForDisplayAsync()),
                    "Opening yields to the dispatcher and duplicate requests share the same initialization");
                await opening;
                foreach (var (panel, handler) in handlers) descriptor.RemoveValueChanged(panel, handler);
                Assert(events.Count == 8 && Enumerable.Range(0, 4).All(index =>
                    events[index * 2] == panels[index].Name && events[index * 2 + 1] == "INPUT"),
                    "Dispatcher services input between every panel layout instead of blocking across the entire settings page");
                Assert(panels.All(panel => panel.Visibility == Visibility.Visible) && save.IsEnabled &&
                    page.FindName("SettingsOpeningOverlay") is null,
                    "Prepared editors are ready for direct navigation without a loading overlay");
                Assert(vm.ResistanceChannels.Count == 10 && ReferenceEquals(page.DataContext, vm),
                    "Deferred layout retains every resistance editor and its original configuration bindings");
                Console.WriteLine($"SETTINGS_OPEN_TEST width={width} staged_total_ms={clock.Elapsed.TotalMilliseconds:0.###} batches_ms={string.Join("/", batches.Select(value => value.ToString("0.###")))}");
                page.ReleasePageResources();
            }
            var canceledPage = new ProductionSettingsPage(null, new ProductionSettingsViewModel(), deferInitialLayout: true);
            Task canceled = canceledPage.PrepareForDisplayAsync();
            canceledPage.ReleasePageResources();
            await canceled;
            Assert(canceledPage.DataContext is null && !((Button)canceledPage.FindName("SaveSettingsButton")).IsEnabled &&
                ((Border)canceledPage.FindName("ResistanceSettingsPanel")).Visibility == Visibility.Collapsed,
                "Closing during deferred opening cannot revive bindings, enable saving or initialize the remaining panels");
        });
    }
}
