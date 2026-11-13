using System.Collections.Specialized;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JBZUniversalTester.Core;
using JBZUniversalTester.Models;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSettingsManualStablePresentation()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (double width in new[] { 1366d, 1920d })
            {
                var runtime = CreateTestViewModel(new ProductionSettings(), out FakeBoard board);
                var vm = new ProductionSettingsViewModel(runtime, _ => { });
                var page = new ProductionSettingsPage(null, vm, deferInitialLayout: true);
                try
                {
                    void Layout()
                    {
                        page.Measure(new Size(width, 768)); page.Arrange(new Rect(0, 0, width, 768)); page.UpdateLayout();
                    }
                    Layout(); await page.PrepareForDisplayAsync(); Layout();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Layout();
                    var scroll = (ScrollViewer)page.FindName("SettingsScrollViewer");
                    scroll.ScrollToVerticalOffset(Math.Min(80, scroll.ScrollableHeight)); Layout();
                    var panels = new[] { "IoSettingsPanel", "RelayLeakMainPanel", "LabelSettingsPanel", "ResistanceSettingsPanel" }
                        .Select(name => (FrameworkElement)page.FindName(name)).ToArray();
                    Rect Bounds(FrameworkElement element) => new(element.TranslatePoint(new Point(), page), element.RenderSize);
                    Rect[] before = panels.Select(Bounds).ToArray();
                    double offset = scroll.VerticalOffset, extent = scroll.ExtentHeight;
                    foreach (string status in new[] { "ManualStatus", "ManualWaterProofStatus", "ManualResistanceStatus" })
                        typeof(ProductionSettingsViewModel).GetProperty(status)!.SetValue(vm, new string('W', 500));
                    for (int channel = 1; channel <= 10; channel++)
                        vm.ManualResistanceResults.Add(new ResistanceResult { Name = $"R{channel}", Channel = channel });
                    Layout();
                    Assert(panels.Select(Bounds).SequenceEqual(before) && scroll.VerticalOffset == offset && scroll.ExtentHeight == extent,
                        $"Manual status/errors and ten results preserve all panel bounds and page scroll at width {width}");
                    var viewport = (ScrollViewer)page.FindName("ManualResistanceViewport");
                    viewport.ScrollToBottom(); Layout();
                    Assert(viewport.ScrollableHeight > 0 && viewport.VerticalOffset == viewport.ScrollableHeight,
                        "Every manual resistance result remains reachable in its own scroll region");
                    int collectionChanges = 0;
                    vm.ManualResistanceResults.CollectionChanged += (_, _) => collectionChanges++;
                    var row = vm.ManualResistanceResults[0];
                    var update = typeof(ProductionSettingsViewModel).GetMethod("UpdateManualResistanceResult", flags)!;
                    for (int i = 0; i < 100; i++)
                        update.Invoke(vm, [new ResistanceResult { Name = "R1", Channel = 1, ValueOhm = i, Passed = true,
                            IsStable = true, MeasurementStatus = "PASS" }]);
                    Assert(ReferenceEquals(row, vm.ManualResistanceResults[0]) && collectionChanges == 0 && row.ValueOhm == 99 && row.ResultText == "PASS",
                        "Live measurement updates reuse the rendered row without collection replacements");
                    int propertyChanges = 0;
                    row.PropertyChanged += (_, _) => propertyChanges++;
                    for (int i = 0; i < 100; i++)
                        update.Invoke(vm, [new ResistanceResult { Name = "R1", Channel = 1, ValueOhm = 99, Passed = true,
                            IsStable = true, MeasurementStatus = "PASS" }]);
                    Assert(propertyChanges == 0 && collectionChanges == 0,
                        "Repeated identical manual readings cause no binding or collection work");
                    update.Invoke(vm, [new ResistanceResult { Name = "R1", Channel = 1, MeasurementStatus = "LỖI" }]);
                    Assert(row.ResultText == "LỖI" && row.Display == "—", "An in-place equipment error clears stale measurement/PASS values");
                    var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Task running = (Task)typeof(ProductionSettingsViewModel).GetMethod("RunManualOperationAsync", flags)!
                        .Invoke(vm, [new Func<CancellationToken, Task>(_ => finish.Task)])!;
                    try
                    {
                        await Dispatcher.Yield(DispatcherPriority.Input);
                        Assert(vm.IsManualOperationRunning && page.IsEnabled && scroll.IsEnabled &&
                               page.FindName("ModelEditorHost") is ContentControl { Visibility: Visibility.Collapsed } &&
                               !vm.ManualRelay1OnCommand.CanExecute(null) && !vm.ManualMeasureResistanceCommand.CanExecute(null),
                            "A pending manual operation retains the settings view and responsive page while rejecting conflicting hardware commands");
                    }
                    finally { finish.TrySetResult(); await running; }
                }
                finally { page.ReleasePageResources(); }
            }
            Exception? reported = null;
            var report = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var command = new AsyncRelayCommand(() => Task.FromException(new IOException("Fixture occupied COM")),
                onError: error => { reported = error; report.TrySetResult(); });
            command.Execute(null);
            await report.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert(reported is IOException && command.CanExecute(null),
                "Inline command error reporting completes without a modal dialog or a permanently disabled command");
        });
    }
}
