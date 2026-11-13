using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSettingsSaveStablePresentation()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            IEnumerable<DependencyObject> Descendants(DependencyObject parent)
            {
                yield return parent;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                    foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
            }
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var applyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var applyRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int writes = 0, closed = 0, applied = 0;
            var runtime = CreateTestViewModel(new ProductionSettings(), out FakeBoard board);
            var vm = new ProductionSettingsViewModel(runtime, snapshot =>
            {
                Interlocked.Increment(ref writes);
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                snapshot.DeviceName = "NORMALIZED-SAVE";
            }, new ProductionSettings());
            var page = new ProductionSettingsPage(null, vm, deferInitialLayout: true);
            try
            {
                void Layout() { page.Measure(new Size(1366, 768)); page.Arrange(new Rect(0, 0, 1366, 768)); page.UpdateLayout(); }
                Layout(); await page.PrepareForDisplayAsync(); Layout();
                var scroll = (ScrollViewer)page.FindName("SettingsScrollViewer");
                var save = (Button)page.FindName("SaveSettingsButton");
                var status = (TextBlock)page.FindName("SettingsSavedStatusText");
                var input = Descendants(page).OfType<TextBox>().Single(text =>
                    text.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == "Settings.DeviceName");
                input.SetCurrentValue(TextBox.TextProperty, "EDITED-SAVE");
                input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                scroll.ScrollToVerticalOffset(Math.Min(80, scroll.ScrollableHeight)); Layout();
                var panels = new[] { "IoSettingsPanel", "RelayLeakMainPanel", "LabelSettingsPanel", "ResistanceSettingsPanel" }
                    .Select(name => (FrameworkElement)page.FindName(name)).ToArray();
                Rect Bounds(FrameworkElement element) => new(element.TranslatePoint(new Point(), page), element.RenderSize);
                var before = panels.Select(Bounds).Append(Bounds(save)).ToArray();
                double extent = scroll.ExtentHeight, offset = scroll.VerticalOffset;
                bool inputRanDuringRefresh = false;
                input.TextChanged += (_, _) =>
                {
                    if (input.Text == "NORMALIZED-SAVE")
                        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                            inputRanDuringRefresh = (int)typeof(ProductionSettingsPage).GetField("_saveInProgress", flags)!.GetValue(page)! != 0));
                };
                page.RequestClose += (_, _) => closed++;
                page.SettingsSaved += async (_, _) => { applied++; applyEntered.TrySetResult(); await applyRelease.Task; };
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Layout();
                Assert(page.IsEnabled && scroll.IsEnabled && !save.IsEnabled && vm.Settings.DeviceName == "EDITED-SAVE" &&
                       !vm.ManualRelay1OnCommand.CanExecute(null) && !vm.ManualWaterProofTestCommand.CanExecute(null),
                    "A blocked disk write leaves the page and scrolling active but guards duplicate save/manual commands");
                var editClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = input };
                typeof(ProductionSettingsPage).GetMethod("Settings_PreviewMouseDown", flags)!.Invoke(page, [page, editClick]);
                var scrollClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = new ScrollBar() };
                typeof(ProductionSettingsPage).GetMethod("Settings_PreviewMouseDown", flags)!.Invoke(page, [page, scrollClick]);
                Assert(editClick.Handled && !scrollClick.Handled, "Save guards pending edits without disabling scrollbar input");
                Assert(panels.Select(Bounds).Append(Bounds(save)).SequenceEqual(before) &&
                       scroll.ExtentHeight == extent && scroll.VerticalOffset == offset && status.Visibility == Visibility.Visible,
                    "Saving cannot move the panels, save button or current scroll position");
                release.TrySetResult();
                await applyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Dispatcher.Yield(DispatcherPriority.Input);
                Assert(page.IsEnabled && !save.IsEnabled && !vm.ManualRelay1OnCommand.CanExecute(null),
                    "UI remains responsive while board/LOT application is pending; manual commands cannot race that apply");
                applyRelease.TrySetResult();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while ((int)typeof(ProductionSettingsPage).GetField("_saveInProgress", flags)!.GetValue(page)! != 0)
                    await Task.Delay(10, deadline.Token);
                await Dispatcher.Yield(DispatcherPriority.Background); Layout();
                Assert(writes == 1 && applied == 1 && closed == 0 && save.IsEnabled &&
                       input.Text == "NORMALIZED-SAVE" && inputRanDuringRefresh &&
                       ReferenceEquals(page.DataContext, vm) && panels.Select(Bounds).Append(Bounds(save)).SequenceEqual(before),
                    "Save yields during binding refresh, shows normalized values and stays on the same settings page");
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(writes == 1 && applied == 1 && closed == 0, "Saving unchanged settings neither writes again nor navigates away");
            }
            finally { release.TrySetResult(); applyRelease.TrySetResult(); page.ReleasePageResources(); }

            var failed = new ProductionSettingsViewModel(saveSettings: _ => throw new IOException("Fixture full disk"),
                initialSettings: new ProductionSettings());
            var failedPage = new ProductionSettingsPage(null, failed);
            try
            {
                failedPage.Measure(new Size(1366, 768)); failedPage.Arrange(new Rect(0, 0, 1366, 768)); failedPage.UpdateLayout();
                var editor = Descendants(failedPage).OfType<TextBox>().Single(text =>
                    text.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == "Settings.DeviceName");
                editor.SetCurrentValue(TextBox.TextProperty, "KEEP-FAILED-SAVE"); editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                var save = (Button)failedPage.FindName("SaveSettingsButton");
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while ((int)typeof(ProductionSettingsPage).GetField("_saveInProgress", flags)!.GetValue(failedPage)! != 0)
                    await Task.Delay(10, deadline.Token);
                Assert(failedPage.IsEnabled && save.IsEnabled && editor.Text == "KEEP-FAILED-SAVE" &&
                       ((TextBlock)failedPage.FindName("SettingsSavedStatusText")).Visibility == Visibility.Visible,
                    "A failed write reports inline and preserves pending edits without a modal dialog or disabled page");
            }
            finally { failedPage.ReleasePageResources(); }
        });
    }
}
