using System.Reflection;
using System.Text;
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
    private static LabelPrintRequest UsbRequest(string cycle, string template = LabelSettings.LargeSqdzTemplate) =>
        LabelPrintRequest.Capture(new TestHistoryRecord { CycleId = cycle, LotNo = 2001,
            Started = DateTime.Today, Finished = DateTime.Today, Passed = true, Result = "PASS" },
            new ProductModel { PartNumber = "D9958-66300", ProductName = "USB TEST", Eco = "D9PL", Alc = "LH" },
            new LabelSettings { PrinterName = "Intermec PC43t (203 dpi)", TemplateType = template });

    private static void TestWindowsUsbPrintTransport()
    {
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-WindowsPrinter-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var sent = new List<(string Queue, byte[] Bytes)>();
                string? checkedQueue = null;
                bool fail = false;
                int uiThread = Environment.CurrentManagedThreadId, spoolThread = uiThread;
                await using var printer = new LabelPrintService(new BcwSerialStore(Path.Combine(root, "bcw.db")),
                    sendWindows: (queue, bytes) =>
                    {
                        spoolThread = Environment.CurrentManagedThreadId;
                        if (fail) throw new IOException("Simulated Windows spooler failure");
                        lock (sent) sent.Add((queue, bytes.ToArray()));
                    }, validateWindows: queue => checkedQueue = queue);
                var connected = await printer.ConnectAsync(new LabelSettings { PrinterName = "Intermec PC43t (203 dpi)" });
                Assert(connected.Connected && checkedQueue == "Intermec PC43t (203 dpi)" && printer.IsConnected && sent.Count == 0,
                    "Windows queue selection validates the exact queue without printing a label");
                foreach (string template in new[] { LabelSettings.LargeTemplate, LabelSettings.LargeSqdzTemplate,
                    LabelSettings.SmallTemplate, LabelSettings.SmallQrTemplate, LabelSettings.BcwTemplate })
                {
                    var request = UsbRequest("usb-" + template, template);
                    if (template == LabelSettings.BcwTemplate) request = await printer.CaptureProductionRequestAsync(request);
                    var result = await printer.PrintPassLabelAsync(request, productionSnapshot: true);
                    var effective = result.EffectiveRequest ?? request;
                    Assert(result.Printed && sent[^1].Queue == request.PrinterName &&
                           sent[^1].Bytes.SequenceEqual(Encoding.ASCII.GetBytes(effective.Payload)) && spoolThread != uiThread,
                        "All built-in labels including BCW send the frozen payload through Windows RAW off Dispatcher");
                    if (template == LabelSettings.BcwTemplate)
                    {
                        int before = sent.Count;
                        await printer.PrintPassLabelAsync(request, productionSnapshot: true);
                        Assert(sent.Count == before, "BCW production still blocks an automatic duplicate on Windows USB");
                    }
                }
                var copies = UsbRequest("usb-copies") with { Copies = 2 };
                int copyStart = sent.Count;
                await printer.PrintPassLabelAsync(copies);
                Assert(sent.Count == copyStart + 2, "Windows sends the selected number of copies");
                var helper = UsbRequest("usb-explicit-target");
                helper = helper with { Profile = helper.Profile with { Mode = LabelPrintMode.ExternalHelper },
                    ExternalHelperPath = Path.Combine(root, "must-not-launch.exe") };
                var explicitTarget = await printer.PrintPassLabelAsync(helper);
                Assert(explicitTarget.Printed && sent[^1].Queue == helper.PrinterName,
                    "An explicit Windows USB target never launches a stale model external helper");
                var frozen = UsbRequest("usb-frozen");
                await printer.PrintPassLabelAsync(frozen, productionSnapshot: true);
                Assert(sent[^1].Queue == frozen.PrinterName && sent[^1].Bytes.SequenceEqual(Encoding.ASCII.GetBytes(frozen.Payload)),
                    "A captured production job retains its queue and payload after editor changes");
                string fallback = Path.Combine(root, "must-not-print.epl");
                fail = true;
                bool threw = false;
                try { await printer.PrintPassLabelAsync(UsbRequest("usb-failed") with { RawDestination = fallback }, productionSnapshot: true); }
                catch (IOException) { threw = true; }
                Assert(threw && !File.Exists(fallback) && !printer.IsConnected,
                    "Spooler failure propagates and never falls back to COM/raw files or pretends to be connected");
                fail = false;
                int beforeCancel = sent.Count;
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                try { await printer.PrintPassLabelAsync(UsbRequest("cancelled"), cancel.Token); } catch (OperationCanceledException) { }
                Assert(sent.Count == beforeCancel, "A cancelled job never reaches the Windows sender");
                bool comRejected = false;
                try { await printer.PrintPassLabelAsync(UsbRequest("com-precedence") with { PrinterCom = "NOT_A_COM_PORT" }); }
                catch (ArgumentException) { comRejected = true; }
                Assert(comRejected && sent.Count == beforeCancel, "Legacy COM-first requests never silently fall back to a Windows printer");
                var offline = await printer.PrintPassLabelAsync(UsbRequest("offline") with { PrinterName = "", RawDestination = fallback }, productionSnapshot: true);
                Assert(!offline.Printed && !File.Exists(fallback), "Production still rejects an unconfigured COM/Windows target");
                await printer.DisconnectAsync();
                Assert(!printer.IsConnected && printer.ConnectedPort.Length == 0, "Disconnect clears Windows queue readiness");
            }
            finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, recursive: true); }
        });
    }

    private static void TestWindowsUsbProductionHistory()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-UsbProduction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var settings = new ProductionSettings();
                settings.Label.PrinterName = "Intermec PC43t (203 dpi)";
                var store = new TestHistoryStore(Path.Combine(root, "history.db"));
                int sends = 0; bool fail = false;
                await using var printer = new LabelPrintService(new BcwSerialStore(Path.Combine(root, "bcw.db")),
                    sendWindows: (queue, bytes) => { Interlocked.Increment(ref sends); if (fail) throw new IOException("Spooler unavailable"); },
                    validateWindows: _ => { });
                var board = new FakeBoard(); var visa = new FakeKeysightVisaService(true, 1);
                var app = new AppSettings(); using var engine = new TestEngine(board, visa, app, settings);
                var vm = new TestViewModel(new MainViewModel(), engine, board, visa, new WaterProofSerialService(),
                    app, settings, new LegacyPhtHistoryService(enabled: false), false, printer);
                await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
                await persistence.Initialization;
                typeof(TestViewModel).GetField("_productionPersistence", flags)!.SetValue(vm, persistence);
                var print = typeof(TestViewModel).GetMethod("PrintPassLabelSafeAsync", flags)!;
                for (int i = 0; i < 2; i++)
                {
                    fail = i == 1;
                    var request = UsbRequest("production-usb-" + i);
                    var row = new TestHistoryRecord { CycleId = request.CycleId, PartNumber = "USB-PRODUCTION-" + i,
                        ModelName = "USB MODEL", Started = DateTime.Today, Finished = DateTime.Now,
                        Passed = true, Result = "PASS", LotNo = 2001 + i, LabelPayload = request.Payload,
                        LabelSerial = request.Data.Barcode, BarcodeValue = request.Data.Barcode, LabelProfile = request.Profile.Id };
                    long id = store.Add(row);
                    await (Task)print.Invoke(vm, [request, store, id, false])!;
                    int afterFirst = sends;
                    await (Task)print.Invoke(vm, [request, store, id, false])!;
                    Assert(sends == afterFirst && sends == i + 1, "Production first-print transaction prevents duplicate USB sends even after a spooler error");
                    var saved = store.Search(new HistorySearchCriteria(null, null, null, row.PartNumber, "ALL")).Single();
                    Assert(saved.Passed && saved.LotNo == row.LotNo && saved.LabelPayload == request.Payload &&
                           (fail ? saved.PrintStatus != LabelPrintStatus.Printed.ToString() : saved.PrintStatus == LabelPrintStatus.Printed.ToString()),
                        "USB send success/failure updates print outcome without changing PASS, LOT or saved printer commands");
                }
                using var db = new SqliteConnection("Data Source=" + Path.Combine(root, "history.db"));
                db.Open(); using var query = db.CreateCommand(); query.CommandText = "PRAGMA integrity_check";
                Assert((string?)query.ExecuteScalar() == "ok", "USB outcomes preserve SQLite integrity");
            }
            finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, recursive: true); }
        });
    }

    private static void TestWindowsUsbSettings()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-PrinterSettings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                int saves = 0, enumerations = 0, uiThread = Environment.CurrentManagedThreadId, enumerateThread = uiThread;
                string cfg = Path.Combine(root, "test.cfg");
                var settings = new ProductionSettings { LastThtPath = Path.Combine(root, "MODEL.tht") };
                settings.Label.PrinterCom = "COM999";
                var vm = new ProductionSettingsViewModel(saveSettings: value => { saves++; ProductionConfigService.SaveLegacyCfg(value, cfg); }, initialSettings: settings);
                var page = new ProductionSettingsPage(null, vm, deferInitialLayout: true, installedPrinters: () =>
                { Interlocked.Increment(ref enumerations); enumerateThread = Environment.CurrentManagedThreadId;
                  return new[] { "Intermec PC43t (203 dpi)", "Other printer" }; });
                await page.PrepareForDisplayAsync();
                await ((Task)typeof(ProductionSettingsPage).GetMethod("RefreshPortsAsync", flags)!.Invoke(page, null)!);
                typeof(ProductionSettingsPage).GetField("_printerPortSelectionInitialized", flags)!.SetValue(page, true);
                var mode = (ComboBox)page.FindName("PrinterTransportComboBox");
                var target = (ComboBox)page.FindName("PrinterComComboBox");
                Assert((string)mode.SelectedValue == "COM" && (string)target.SelectedValue == "COM999" && enumerations == 0 && saves == 0,
                    "Legacy COM opens unchanged and does not query the Windows spooler or save on opening");
                async Task Settle() { for (int i = 0; i < 100; i++) { await Task.Delay(10); if (target.Items.Count == 2 && (string?)mode.SelectedValue == "WINDOWS") return; } throw new TimeoutException("Printer enumeration"); }
                mode.SelectedValue = "WINDOWS";
                await Settle();
                Assert(settings.Label.PrinterCom == "" && target.Items.Count == 2 && enumerateThread != uiThread && saves == 0,
                    "In qua USB asynchronously loads only installed Windows queues without autosaving");
                target.SelectedValue = "Intermec PC43t (203 dpi)";
                Assert(settings.Label.PrinterName == "Intermec PC43t (203 dpi)" && settings.Label.PrinterCom == "",
                    "Selecting USB persists an exclusive Windows printer target in the editor");
                await vm.SaveAsync();
                var loaded = ProductionConfigService.LoadSavedSettings(cfg);
                Assert(loaded.Label.PrinterName == settings.Label.PrinterName && loaded.Label.PrinterCom == "" && saves == 1,
                    "Explicit Save and CFG reload preserve the Windows printer selection");
                mode.SelectedValue = "COM";
                await ((Task)typeof(ProductionSettingsPage).GetMethod("RefreshPortsAsync", flags)!.Invoke(page, null)!);
                Assert(settings.Label.PrinterName == "" && settings.Label.PrinterCom == "COM999" && (string?)target.SelectedValue == "COM999",
                    "In qua COM restores its own port list without mixing Windows queue names");
                mode.SelectedValue = "WINDOWS";
                await Settle();
                Assert((string?)target.SelectedValue == "Intermec PC43t (203 dpi)", "Switching transport restores that editor's previous queue selection");
                page.ReleasePageResources();
                var missingVm = new ProductionSettingsViewModel(initialSettings: loaded, saveSettings: _ => throw new Exception("No autosave"));
                var missingPage = new ProductionSettingsPage(null, missingVm, deferInitialLayout: true, installedPrinters: () => []);
                await missingPage.PrepareForDisplayAsync();
                await ((Task)typeof(ProductionSettingsPage).GetMethod("RefreshPortsAsync", flags)!.Invoke(missingPage, null)!);
                Assert((string?)((ComboBox)missingPage.FindName("PrinterTransportComboBox")).SelectedValue == "WINDOWS" &&
                       (string?)((ComboBox)missingPage.FindName("PrinterComComboBox")).SelectedValue == loaded.Label.PrinterName,
                    "A temporarily missing USB queue retains the exact saved name rather than selecting another printer");
                missingPage.ReleasePageResources();
            }
            finally { Directory.Delete(root, recursive: true); }
        });
    }
}
