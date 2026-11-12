using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestProbeMaintenanceSharedPasswordAndReset()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-PinMaintenance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            ProductionPersistenceService? persistence = null;
            try
            {
                var access = new SettingsAccessService(Path.Combine(root, "access.json"));
                access.ChangePassword(SettingsAccessSession.Administrator, "admin", "station-password", "station-password");
                TaskCompletionSource? verificationGate = null;
                var settings = new ProductionSettings { MasterFaultRequiredCount = 0, ProbeReplacementThreshold = 10 };
                var vm = CreateTestViewModel(settings, out FakeBoard board, verifyProbeMaintenancePassword: async password =>
                {
                    if (verificationGate is not null) await verificationGate.Task;
                    return await access.VerifyAdminAsync(password);
                });
                var model = Model(("PIN", new[] { 1, 2 }));
                model.PartNumber = "PIN-TEST";
                typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
                typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(vm, true);
                var repository = new TestHistoryStore(Path.Combine(root, "history.db"));
                persistence = new ProductionPersistenceService(repository, settings, "SELF-TEST");
                await persistence.Initialization;
                typeof(TestViewModel).GetField("_historyStore", flags)!.SetValue(vm, repository);
                typeof(TestViewModel).GetField("_productionPersistence", flags)!.SetValue(vm, persistence);
                typeof(TestViewModel).GetField("_partCounterStore", flags)!.SetValue(vm,
                    new Lazy<PartCounterStore>(() => new PartCounterStore(Path.Combine(root, "PartCnt.txt"))));
                PartIdentitySnapshot part = PartIdentitySnapshot.Capture(model);
                ProbeCounterSnapshot counter = new("PIN-TEST", 10, 0);
                for (int i = 0; i < 10; i++) counter = await persistence.IncrementProbeCounterAsync(part, 10);
                var apply = typeof(TestViewModel).GetMethod("ApplyPartCounter", flags, null, [typeof(ProbeCounterSnapshot)], null)!;
                apply.Invoke(vm, [counter]);
                int commandsBefore = board.Commands.Count;
                Assert(vm.ProbeMaintenanceDue && vm.ProbeCycleText == "10/10", "PIN maintenance is due exactly at the configured threshold");
                Assert(!(await vm.TryResetProbeCycleAsync("admin")).Reset && vm.ProbeCycleCount == 10,
                    "The previous fixed password cannot reset PIN counters after settings password changed");
                typeof(TestViewModel).GetField("_productDetectedThisCycle", flags)!.SetValue(vm, true);
                Assert(!(await vm.TryResetProbeCycleAsync("station-password")).Reset && vm.ProbeCycleCount == 10,
                    "A correct settings password cannot reset maintenance while a product is in the test cycle");
                typeof(TestViewModel).GetField("_productDetectedThisCycle", flags)!.SetValue(vm, false);
                verificationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<(bool Reset, string Message)> pending = vm.TryResetProbeCycleAsync("station-password");
                Assert(!pending.IsCompleted && !(await vm.TryResetProbeCycleAsync("station-password")).Reset,
                    "Asynchronous authentication stays responsive and rejects a duplicate confirmation");
                typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, Model(("OTHER", new[] { 3, 4 })));
                verificationGate.SetResult();
                Assert(!(await pending).Reset && repository.GetAllProbeCounters().Single().Counter == 10,
                    "Changing model during authentication cannot reset a stale product's counter");
                verificationGate = null;
                typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
                Assert((await vm.TryResetProbeCycleAsync("station-password")).Reset && vm.ProbeCycleCount == 0 && !vm.ProbeMaintenanceDue,
                    "Current settings password authorizes the confirmed physical PIN replacement");
                access.ChangePassword(SettingsAccessSession.Administrator, "station-password", "next-password", "next-password");
                Assert(!(await vm.TryResetProbeCycleAsync("station-password")).Reset,
                    "Changing the settings password immediately invalidates the previous maintenance password");
                Assert((await vm.TryResetProbeCycleAsync("next-password")).Reset && board.Commands.Count == commandsBefore,
                    "The newly changed settings password is accepted without sending relay or scan commands");
                using var connection = new SqliteConnection($"Data Source={repository.DatabasePath};Pooling=False");
                connection.Open();
                using var query = connection.CreateCommand();
                query.CommandText = "SELECT COUNT(*) FROM ProbeMaintenanceEvents;";
                Assert((long)query.ExecuteScalar()! == 2, "Only two successful password-confirmed replacements create maintenance audit rows");
                query.CommandText = "SELECT COUNT(*) FROM Tests;";
                Assert((long)query.ExecuteScalar()! == 0, "Maintenance reset does not create, delete or count production tests");
                query.CommandText = "PRAGMA integrity_check;";
                Assert((string?)query.ExecuteScalar() == "ok", "PIN maintenance audit preserves SQLite integrity");
            }
            finally
            {
                if (persistence is not null) await persistence.DisposeAsync();
                SqliteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        });
    }

    private static void TestProbeMaintenanceAutomaticDialog()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-PinDialog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var access = new SettingsAccessService(Path.Combine(root, "access.json"));
            access.ChangePassword(SettingsAccessSession.Administrator, "admin", "settings-password", "settings-password");
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0, ProbeReplacementThreshold = 10 };
            var vm = CreateTestViewModel(settings, out _, verifyProbeMaintenancePassword: access.VerifyAdminAsync);
            var model = Model(("PIN", new[] { 1, 2 }));
            model.PartNumber = "PIN-TEST";
            typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
            typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(vm, true);
            var repository = new TestHistoryStore(Path.Combine(root, "history.db"));
            var persistence = new ProductionPersistenceService(repository, settings, "SELF-TEST");
            await persistence.Initialization;
            typeof(TestViewModel).GetField("_historyStore", flags)!.SetValue(vm, repository);
            typeof(TestViewModel).GetField("_productionPersistence", flags)!.SetValue(vm, persistence);
            typeof(TestViewModel).GetField("_partCounterStore", flags)!.SetValue(vm,
                new Lazy<PartCounterStore>(() => new PartCounterStore(Path.Combine(root, "PartCnt.txt"))));
            for (int i = 0; i < 10; i++) await persistence.IncrementProbeCounterAsync(PartIdentitySnapshot.Capture(model), 10);
            var apply = typeof(TestViewModel).GetMethod("ApplyPartCounter", flags, null, [typeof(ProbeCounterSnapshot)], null)!;
            apply.Invoke(vm, [new ProbeCounterSnapshot("PIN-TEST", 10, 9)]);
            var window = new TestWindow(vm, autoStartProduction: false)
            {
                WindowState = WindowState.Normal, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000, Top = -10000, Width = 1100, Height = 800, ShowActivated = false
            };
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0, seen = 0;
            var watcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            watcher.Tick += async (_, _) =>
            {
                attempts++;
                var dialog = window.OwnedWindows.OfType<ProbeMaintenanceResetWindow>().FirstOrDefault();
                if (dialog is not null)
                {
                    watcher.Stop();
                    seen++;
                    try
                    {
                        Assert(((TextBlock)dialog.FindName("CurrentCyclesText")).Text == "10" &&
                               ((TextBlock)dialog.FindName("ThresholdText")).Text == "10",
                            "Automatic replacement dialog displays the reached 10/10 threshold");
                        ((PasswordBox)dialog.FindName("AdminPasswordBox")).Password = "not-the-settings-password";
                        var confirm = (Task)typeof(ProbeMaintenanceResetWindow).GetMethod("ConfirmAsync", flags)!.Invoke(dialog, null)!;
                        await confirm;
                        Assert(dialog.IsVisible && vm.ProbeCycleCount == 10,
                            "Rejected replacement leaves the same dialog open and preserves the counter");
                        dialog.Close();
                        Assert(dialog.IsVisible && dialog.WindowStyle == WindowStyle.None &&
                               ((Button)dialog.FindName("CancelResetButton")).Visibility == Visibility.Collapsed,
                            "Overdue replacement has no Cancel/close button and rejects closing without a successful reset");
                        var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                            PresentationSource.FromVisual(dialog), 0, System.Windows.Input.Key.Escape)
                        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                        dialog.RaiseEvent(escape);
                        Assert(escape.Handled && dialog.IsVisible, "Escape cannot dismiss a mandatory PIN replacement");
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        dialog.UpdateLayout();
                        var button = (Button)dialog.FindName("ConfirmResetButton");
                        Point bottom = button.TranslatePoint(new Point(button.ActualWidth, button.ActualHeight), dialog);
                        Assert(button.ActualHeight >= 38 && bottom.Y <= dialog.ActualHeight && bottom.X <= dialog.ActualWidth,
                            "Auto-sized maintenance dialog fully contains its confirmation button and label");
                        var surface = (FrameworkElement)dialog.Content;
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
                            (int)Math.Ceiling(surface.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(surface);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using (var output = File.Create(Path.Combine(Path.GetTempPath(), "jbz-v313-pin-confirmation.png"))) encoder.Save(output);
                        ((PasswordBox)dialog.FindName("AdminPasswordBox")).Password = "settings-password";
                        await (Task)typeof(ProbeMaintenanceResetWindow).GetMethod("ConfirmAsync", flags)!.Invoke(dialog, null)!;
                        Assert(!dialog.IsVisible && vm.ProbeCycleCount == 0, "Only a successful current settings password reset closes the mandatory dialog");
                        completed.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        // Tear down this isolated test fixture even if its assertions fail.
                        typeof(ProbeMaintenanceResetWindow).GetField("_resetSucceeded", flags)!.SetValue(dialog, true);
                        dialog.Close();
                        completed.TrySetException(ex);
                    }
                }
                else if (attempts > 150)
                {
                    watcher.Stop();
                    completed.TrySetException(new InvalidOperationException("PIN replacement dialog was not opened at 10/10"));
                }
            };
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(window.OwnedWindows.OfType<ProbeMaintenanceResetWindow>().Count() == 0, "9/10 does not open a replacement dialog");
                watcher.Start();
                apply.Invoke(vm, [new ProbeCounterSnapshot("PIN-TEST", 10, 10)]);
                await completed.Task;
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(seen == 1 && vm.ProbeCycleCount == 0, "10/10 opens one mandatory confirmation and resets only after password authorization");
            }
            finally
            {
                watcher.Stop();
                foreach (var dialog in window.OwnedWindows.OfType<ProbeMaintenanceResetWindow>().ToArray())
                {
                    typeof(ProbeMaintenanceResetWindow).GetField("_resetSucceeded", flags)!.SetValue(dialog, true);
                    dialog.Close();
                }
                typeof(TestWindow).GetField("_allowClose", flags)!.SetValue(window, true);
                window.Close();
                await persistence.DisposeAsync();
                SqliteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        });
    }
}
