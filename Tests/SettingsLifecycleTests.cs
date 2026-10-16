using System.IO.Ports;
using System.Reflection;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void OnSettingsDispatcher(Func<Task> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await test(); }
                catch (Exception ex) { failure = ex; }
                finally { frame.Continue = false; }
            }));
            Dispatcher.PushFrame(frame);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert(thread.Join(TimeSpan.FromSeconds(15)), "Settings dispatcher regression finishes without hanging");
        if (failure is not null) throw new InvalidOperationException("Settings lifecycle regression", failure);
    }

    private static void TestSettingsAsyncSave()
    {
        OnSettingsDispatcher(async () =>
        {
            int uiThread = Environment.CurrentManagedThreadId, writerThread = 0;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var vm = new ProductionSettingsViewModel(saveSettings: snapshot =>
            {
                writerThread = Environment.CurrentManagedThreadId;
                snapshot.DeviceName = "NORMALIZED-SNAPSHOT";
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult(); // Simulated slow Flush(true), never real config I/O.
            });
            vm.Settings.DeviceName = "OPERATOR-EDIT";
            Task saving = vm.SaveAsync();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert(!saving.IsCompleted && vm.Settings.DeviceName == "OPERATOR-EDIT" && writerThread != uiThread,
                    "Slow durable writes run off Dispatcher and cannot mutate the live editor snapshot");
                bool heartbeat = false;
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => heartbeat = true, DispatcherPriority.Background);
                Assert(heartbeat, "Dispatcher handles input/paint work while config storage is blocked");
            }
            finally { release.TrySetResult(); }
            await saving;
            Assert(vm.Settings.DeviceName == "NORMALIZED-SNAPSHOT" && Environment.CurrentManagedThreadId == uiThread,
                "Saved normalized fields return to editors on their original Dispatcher");

            var failed = new ProductionSettingsViewModel(saveSettings: _ => throw new IOException("Fixture disk error"));
            failed.Settings.DeviceName = "KEEP-UNSAVED-EDIT";
            bool reported = false;
            try { await failed.SaveAsync(); }
            catch (IOException) { reported = true; }
            Assert(reported && failed.Settings.DeviceName == "KEEP-UNSAVED-EDIT",
                "Save failures propagate while preserving the operator's pending edits");
        });

        string root = Path.Combine(Path.GetTempPath(), "JBZ-SettingsAtomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cfg = Path.Combine(root, "fixture.cfg");
            Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
                ProductionConfigService.SaveLegacyCfg(new ProductionSettings { DeviceName = "WRITER-" + index }, cfg))))
                .GetAwaiter().GetResult();
            string saved = File.ReadAllText(cfg);
            Assert(saved.Contains("[DeviceName]WRITER-") && saved.Split("[CardCount]").Length == 2 && !File.Exists(cfg + ".tmp"),
                "Concurrent config writers cannot collide on the atomic temporary file or leave a partial config");
            Assert(ProductionConfigService.LoadSavedSettings(cfg).DeviceName.StartsWith("WRITER-"),
                "Applying settings reads the complete persisted configuration");
            AssertThrows<FileNotFoundException>(() => ProductionConfigService.LoadSavedSettings(Path.Combine(root, "missing.cfg")),
                "A missing saved configuration cannot silently replace runtime settings with defaults");
            File.WriteAllText(cfg, "[DeviceName]INCOMPLETE");
            AssertThrows<InvalidDataException>(() => ProductionConfigService.LoadSavedSettings(cfg),
                "A partial configuration is rejected instead of resetting card capacity and safety settings");
        }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-SettingsAtomic-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void TestSettingsManualStop()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var visa = new FakeKeysightVisaService(connected: true, measurement: 1);
            TestViewModel runtime = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 },
                out FakeBoard board, visa: visa);
            var vm = new ProductionSettingsViewModel(runtime, _ => { });
            var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken operationToken = default;
            Func<CancellationToken, Task> operation = async token =>
            {
                operationToken = token;
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { await cleanup.Task; }
            };
            int commands = board.Commands.Count;
            Task running = (Task)typeof(ProductionSettingsViewModel).GetMethod("RunManualOperationAsync", flags)!
                .Invoke(vm, [operation])!;
            Assert(vm.IsManualOperationRunning && !vm.ManualRelay1OnCommand.CanExecute(null) &&
                   !vm.ManualRelay2OnCommand.CanExecute(null) && !vm.ManualWaterProofTestCommand.CanExecute(null),
                "One settings operation immediately locks every other manual command");
            Task closing = vm.StopManualOperationsAsync(closing: true);
            Assert(operationToken.IsCancellationRequested && !closing.IsCompleted,
                "Settings close cancels the operation and still waits for its physical cleanup");
            cleanup.TrySetResult();
            try { await running; } catch (OperationCanceledException) { }
            await closing;
            Assert(!vm.IsManualOperationRunning && !vm.ManualRelay1OnCommand.CanExecute(null) &&
                   !runtime.IsDeviceFault && board.Commands.Count == commands,
                "A closed settings session cannot accept late commands, fault the board or emit relay pulses");

            // Cancellation before the VISA gate is acquired must still release Manual mode.
            await runtime.EnterManualModeAsync();
            var gate = (SemaphoreSlim)typeof(TestViewModel).GetField("_manualRelayGate", flags)!.GetValue(runtime)!;
            await gate.WaitAsync();
            using var cancel = new CancellationTokenSource();
            Task measuring = runtime.MeasureManualResistanceAsync(
                [new ResistanceStep("R1", 1, 0.5, 2)], ct: cancel.Token);
            cancel.Cancel();
            gate.Release();
            try { await measuring; } catch (OperationCanceledException) { }
            Assert(!runtime.IsDeviceFault && !runtime.IsManualModeActive && board.IsScanning && gate.CurrentCount == 1 &&
                   visa.MeasureCallCount == 0,
                "Cancellation during a queued resistance operation restores scan without leaking/releasing someone else's gate");

            var conflict = new ProductionSettingsViewModel(runtime, _ => { });
            conflict.Settings.Label.PrinterCom = "COM7";
            conflict.Settings.WaterProofMachine.PortName = "COM7";
            commands = board.Commands.Count;
            bool rejected = false;
            try
            {
                await (Task)typeof(ProductionSettingsViewModel).GetMethod("RunManualWaterProofAsync", flags)!
                    .Invoke(conflict, [CancellationToken.None])!;
            }
            catch (InvalidOperationException ex) { rejected = ex.Message.Contains("COM7"); }
            Assert(rejected && board.Commands.Count == commands && !runtime.IsDeviceFault,
                "Printer/Leak COM conflicts are rejected before touching D2XX scan or relay outputs");
        });
    }

    private static void TestSettingsPrinterClose()
    {
        OnSettingsDispatcher(async () =>
        {
            int uiThread = Environment.CurrentManagedThreadId, closerThread = 0, closeCount = 0;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new LabelPrintService(closePort: port =>
            {
                closerThread = Environment.CurrentManagedThreadId;
                Interlocked.Increment(ref closeCount);
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult(); // Native driver close fixture; no actual COM is opened.
                port.Dispose();
            });
            typeof(LabelPrintService).GetField("_printerPort", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, new SerialPort());
            Task disconnect = service.DisconnectAsync();
            Task second = Task.CompletedTask;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                second = service.DisconnectAsync();
                Assert(closerThread != uiThread && !disconnect.IsCompleted && !second.IsCompleted,
                    "A slow COM driver cannot block Dispatcher or allow overlapping close/reopen ownership");
                using var cancel = new CancellationTokenSource();
                Task connect = service.ConnectAsync(new LabelSettings { PrinterCom = "COM999" }, cancel.Token);
                cancel.Cancel();
                bool cancelled = false;
                try { await connect; } catch (OperationCanceledException) { cancelled = true; }
                Assert(cancelled && !service.IsConnected, "Superseded printer selection cannot bypass the closing handle's gate");
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            finally
            {
                release.TrySetResult();
                await Task.WhenAll(disconnect, second);
                await service.DisposeAsync();
            }
            Assert(closeCount == 1, "Driver disposal executes exactly once for the owned serial handle");
            int attempts = 0;
            var recovering = new LabelPrintService(closePort: port =>
            {
                if (++attempts == 1) throw new IOException("Fixture driver failed to dispose");
                port.Dispose();
            });
            typeof(LabelPrintService).GetField("_printerPort", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(recovering, new SerialPort());
            bool failed = false;
            try { await recovering.DisconnectAsync(); } catch (IOException) { failed = true; }
            await recovering.DisconnectAsync();
            await recovering.DisposeAsync();
            Assert(failed && attempts == 2, "Failed COM disposal keeps its owner for a safe cleanup retry before replacement");
        });
    }
}
