using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSettingsInvalidLeakLayoutRecovery()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-SettingsLeakRecovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "sample.tht");
                File.WriteAllBytes(path, BuildMinimalThtFile(
                    "파트번호\t파트명\nLEAK\tLEAK TEST\n\n번 호\t커넥터\t핀 수\n1\tA\t1\n2\tB\t1\n\n" +
                    "커넥터\t선이름\tI/O\t핀번호\nA\tW1\t1\t1\nB\tW1\t2\t1\n\n선이름\t선연결\t굵기\t색깔\nW1\t\t0.5\tB"));
                foreach (string invalidSidecar in new[] {
                    JsonSerializer.Serialize(new ModelLeakLayoutService.Layout(1, [], "WRONG-HASH")), "{broken" })
                {
                    File.WriteAllText(path + ".leak.json", invalidSidecar);
                    var settings = new ProductionSettings { LastThtPath = path, MasterFaultRequiredCount = 0 };
                    ProductionConfigService.SetWaterProofProfileForPath(settings, path, new WaterProofModelSettings
                        { Enabled = true, Channel1Enabled = true, Channel2Enabled = false, Channel3Enabled = false,
                            Channel1Connector = "A", PressMin = 42, LeakLimit = 7 });
                    string originalProfile = JsonSerializer.Serialize(settings.WaterProofProfilesByModel);
                    int saves = 0;
                    var vm = new ProductionSettingsViewModel(saveSettings: _ => saves++, initialSettings: settings);
                    Assert(vm.HasWaterProofConfigurationError && vm.WaterProof.Enabled && vm.WaterProof.PressMin == 42,
                        "Settings opens with an explicit Leak warning and retains the saved profile when the companion file is stale or malformed");
                    var page = new ProductionSettingsPage(null, vm, deferInitialLayout: true);
                    page.SetAccessSession(SettingsAccessSession.Administrator);
                    await page.PrepareForDisplayAsync();
                    Assert(!((UIElement)page.FindName("WaterProofConfigurationBody")).IsEnabled &&
                        ((UIElement)page.FindName("IoGeneralConfiguration")).IsEnabled &&
                        ((UIElement)page.FindName("LabelSettingsPanel")).IsEnabled &&
                        !vm.ManualWaterProofTestCommand.CanExecute(null),
                        "Untrusted Leak editing/manual execution is blocked while other settings remain available");
                    object?[] validation = [string.Empty];
                    bool valid = (bool)typeof(ProductionSettingsPage).GetMethod("ValidateSettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(page, validation)!;
                    Assert(valid, $"Invalid Leak metadata cannot block validation of other groups: {validation[0]}");
                    vm.Settings.DeviceName = "allowed station edit";
                    await vm.SaveAsync();
                    Assert(saves == 1 && JsonSerializer.Serialize(settings.WaterProofProfilesByModel) == originalProfile &&
                        File.ReadAllText(path + ".leak.json") == invalidSidecar,
                        "Saving unrelated settings preserves both the stored Leak profile and its external file byte-for-byte");
                    bool rejected = false;
                    try { ProductionConfigService.GetWaterProofProfileForPath(settings, path); }
                    catch (InvalidDataException) { rejected = true; }
                    Assert(rejected, "Production still rejects the mismatched/malformed THT-Leak pair");
                    vm.WaterProof.PressMin++;
                    bool editBlocked = false;
                    try { vm.Save(); }
                    catch (InvalidOperationException) { editBlocked = true; }
                    Assert(editBlocked && saves == 1, "The recovery editor cannot silently persist changed Leak assignments");
                    page.ReleasePageResources();
                }
            }
            finally { Directory.Delete(root, true); }
        });
    }

    private static void TestSettingsAuthenticationResponsiveness()
    {
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-AuthPerf-" + Guid.NewGuid().ToString("N"));
            try
            {
                int ticks = 0;
                long lastTick = Stopwatch.GetTimestamp();
                double maxGap = 0;
                var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(5) };
                timer.Tick += (_, _) => { long now = Stopwatch.GetTimestamp(); maxGap = Math.Max(maxGap,
                    Stopwatch.GetElapsedTime(lastTick, now).TotalMilliseconds); lastTick = now; ticks++; };
                timer.Start();
                try
                {
                    var loadWatch = Stopwatch.StartNew();
                    var service = await Task.Run(() => new SettingsAccessService(Path.Combine(root, "access.json")));
                    double loadMs = loadWatch.Elapsed.TotalMilliseconds;
                    var durations = new List<double>();
                    double maxPermissionRead = 0;
                    for (int round = 0; round < 5; round++)
                    {
                        var watch = Stopwatch.StartNew();
                        Task<bool> verification = service.VerifyAdminAsync("admin");
                        Assert(!verification.IsCompleted, "Password hashing must not execute synchronously on the WPF Dispatcher");
                        int ticksBefore = ticks;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        while (!verification.IsCompleted)
                        {
                            var readWatch = Stopwatch.StartNew();
                            _ = service.OperatorSession;
                            maxPermissionRead = Math.Max(maxPermissionRead, readWatch.Elapsed.TotalMilliseconds);
                            await Task.Delay(5);
                        }
                        Assert(await verification, "Asynchronous verification preserves correct Admin authentication");
                        durations.Add(watch.Elapsed.TotalMilliseconds);
                        Assert(ticks > ticksBefore, "WPF continues servicing input while the real password hash is being computed");
                    }
                    Console.WriteLine($"AUTH_PERF_TEST load_ms={loadMs:0.###} verify_avg_ms={durations.Average():0.###} " +
                        $"verify_max_ms={durations.Max():0.###} dispatcher_gap_max_ms={maxGap:0.###} " +
                        $"permission_read_max_ms={maxPermissionRead:0.###} input_ticks={ticks}");
                }
                finally { timer.Stop(); }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
}
