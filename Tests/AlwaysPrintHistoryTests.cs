using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestAlwaysPrintHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-pass-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            string db = Path.Combine(root, "history.db");
            var store = new TestHistoryStore(db);
            var settings = new ProductionSettings { AutoPrintLabelOnPass = false };
            string journalPath = Path.Combine(root, "bcw.db");
            LabelPrintRequest legacyRequest = BcwRequest("legacy-pending");
            using (var old = new SqliteConnection("Data Source=" + journalPath))
            {
                old.Open();
                using var command = old.CreateCommand();
                command.CommandText = """
                    CREATE TABLE BcwPrints(CycleId TEXT PRIMARY KEY,Scope TEXT NOT NULL,Serial INTEGER NOT NULL,
                        State TEXT NOT NULL,RequestJson TEXT NOT NULL,Message TEXT NOT NULL DEFAULT '',UpdatedAt TEXT NOT NULL,UNIQUE(Scope,Serial));
                    CREATE UNIQUE INDEX IX_Bcw_ActiveScope ON BcwPrints(Scope) WHERE State<>'Sent';
                    INSERT INTO BcwPrints VALUES('legacy-pending','20261014|09|LH',1,'Failed',$Request,'legacy diagnostic','2026-10-14');
                    """;
                command.Parameters.AddWithValue("$Request", System.Text.Json.JsonSerializer.Serialize(legacyRequest));
                command.ExecuteNonQuery();
            }
            var journal = new BcwSerialStore(journalPath);
            var printer = new LabelPrintService(journal);
            var board = new FakeBoard();
            var visa = new FakeKeysightVisaService(connected: true, measurement: 1);
            var app = new AppSettings();
            using var engine = new TestEngine(board, visa, app, settings);
            var vm = new TestViewModel(new MainViewModel(), engine, board, visa, new WaterProofSerialService(),
                app, settings, new LegacyPhtHistoryService(enabled: false), false, printer);
            var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            try
            {
                persistence.Initialization.GetAwaiter().GetResult();
                typeof(TestViewModel).GetField("_productionPersistence", flags)!.SetValue(vm, persistence);
                var print = typeof(TestViewModel).GetMethod("PrintPassLabelSafeAsync", flags)!;
                for (int index = 0; index < 4; index++)
                {
                    string payload = "\r\nN\r\nFR\"KS91\"\r\n?\r\nPART-" + index + "\r\nP1\r\n";
                    var row = new TestHistoryRecord { CycleId = "offline-" + index, PartNumber = "PART-" + index,
                        ModelName = "MODEL-" + index, Started = DateTime.Today.AddMinutes(index),
                        Finished = DateTime.Today.AddMinutes(index).AddSeconds(1), Passed = true, Result = "PASS", LotNo = index + 1 };
                    var model = new ProductModel { PartNumber = row.PartNumber, ModelName = row.ModelName, ProductName = "TEST" };
                    LabelPrintRequest request = LabelPrintRequest.Capture(row, model, new LabelSettings { TemplateType = LabelSettings.SmallQrTemplate });
                    if (index >= 2)
                    {
                        request = BcwRequest(row.CycleId) with { RawDestination = "", PrinterCom = "", PrinterName = "" };
                        request = printer.CaptureProductionRequestAsync(request).GetAwaiter().GetResult();
                        row.LabelTemplateType = "BCW";
                        payload = request.Payload;
                    }
                    else request = request with { Payload = payload, RawDestination = Path.Combine(root, "must-not-print.epl") };
                    row.LabelPayload = payload;
                    row.LabelProfile = request.Profile.Id;
                    row.LabelSerial = request.Data.Barcode;
                    row.BarcodeValue = request.Data.Barcode;
                    long id = store.Add(row);
                    ((Task)print.Invoke(vm, [request, store, id, false])!).GetAwaiter().GetResult();
                    TestHistoryRecord saved = store.Search(new HistorySearchCriteria(null, null, null, row.PartNumber, "ALL")).Single();
                    Assert(saved.Passed && saved.LotNo == row.LotNo && saved.LabelPayload == payload && saved.ExportBarcodeText == payload &&
                           saved.BarcodeValue == request.Data.Barcode && saved.BarcodeOutputText.Length == 0 &&
                           saved.PrintMessage.Length == 0 && !saved.ExportContentText.Contains("인쇄", StringComparison.Ordinal),
                        "Offline PASS retains exact command and LOT without COM/print diagnostics in history");
                    Assert(saved.PrintStatus != LabelPrintStatus.Printed.ToString(), "An offline send cannot fabricate a printed result");
                    if (index == 3)
                        Assert(journal.Find("offline-2")!.Data.Barcode != journal.Find("offline-3")!.Data.Barcode,
                            "Every offline BCW PASS reserves its own immutable serial without blocking the next product");
                }
                Assert(!File.Exists(Path.Combine(root, "must-not-print.epl")) && vm.Total == 0 &&
                       !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                    "Production uses only the selected COM and never alters counters or relays during label delivery");
                Assert(journal.Find("legacy-pending")!.Payload == legacyRequest.Payload &&
                       Directory.GetFiles(root, "bcw.db.before-always-print-*.db").Length == 1,
                    "Old BCW journals are backed up once and migrate without deleting the pending label snapshot");
                string xlsx = Path.Combine(root, "history.xlsx");
                var all = new HistorySearchCriteria(null, null, null, "", "ALL", MaxRows: 1);
                Assert(HistoryExportService.ExportXlsx(xlsx, store.EnumerateForExport(all)) == 4,
                    "Payload export streams all filtered records beyond one UI page");
                using (var zip = ZipFile.OpenRead(xlsx))
                using (var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()))
                {
                    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    XDocument xml = XDocument.Parse(reader.ReadToEnd());
                    string cell = string.Concat(xml.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "N2")
                        .Descendants(ns + "t").Select(t => t.Value));
                    Assert(cell == "\r\nN\r\nFR\"KS91\"\r\n?\r\nPART-0\r\nP1\r\n",
                        "Original barcode-output column preserves the entire command including CRLF");
                }
                using var connection = new SqliteConnection("Data Source=" + db);
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check;";
                Assert((string)command.ExecuteScalar()! == "ok", "PASS payload changes preserve SQLite integrity");
            }
            finally
            {
                persistence.DisposeAsync().AsTask().GetAwaiter().GetResult();
                printer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, recursive: true); }
    }

    private static void TestCompactPrintConfig()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-compact-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cfg = Path.Combine(root, "settings.cfg");
            var settings = new ProductionSettings { AutoPrintLabelOnPass = false, ExpansionCardCount = 3, StartCardNumber = 2 };
            settings.Label.PrinterCom = "COM9";
            settings.Label.SmallQrTemplateOverrideBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("CUSTOM-TEMPLATE"));
            settings.MasterFaultCountsByModel["OLD-MASTER"] = 3;
            settings.LotSettingsByProduct["PART-A"] = new ProductLotSettings { LotNo = 123, StartLotNo = 1, BulkPrintLotNo = 120, LotNoDate = "2026-10-19" };
            settings.ResistanceProfilesByModel["PART-A"] = ResistanceMeasurementPlan.Normalize([]);
            settings.ResistanceProfilesByModel["PART-A"][6] = new ResistanceChannelSetting
                { Name = "R7", Enabled = false, Channel = 0, MinOhm = 5, MaxOhm = 10 };
            settings.ResistanceProfilesByModel["PART-B"] = ResistanceMeasurementPlan.Normalize([]);
            settings.WaterProofProfilesByModel["PART-A"] = new WaterProofModelSettings { Enabled = false };
            ProductionConfigService.SaveLegacyCfg(settings, cfg);
            string saved = File.ReadAllText(cfg);
            Assert(!saved.Contains("[AutoPrintLabelOnPass]") && !saved.Contains("[MasterFault.") &&
                   !saved.Contains("[TemperatureTolerance]") && !saved.Contains("[CardCount]") &&
                   !saved.Contains("[LabelTemplateBCWBase64]"), "Compact writer omits obsolete controls, aliases and empty label overrides");
            Assert(saved.Contains("[Resistance.Model.PART-A.R7]0;0;5;10") &&
                   saved.Split("[Resistance.Model.PART-B.").Length == 2 &&
                   !saved.Contains("[Resistance.Model.PART-A.R10]"),
                "Compact profiles keep explicit disabled settings and one default slot to prevent global fallback");
            File.AppendAllText(cfg, "[AutoPrintLabelOnPass]0\r\n[MasterFault.LEGACY]2\r\n");
            ProductionSettings loaded = ProductionConfigService.LoadSavedSettings(cfg);
            Assert(loaded.AutoPrintLabelOnPass && loaded.ExpansionCardCount == 3 && loaded.StartCardNumber == 2 &&
                   loaded.Label.PrinterCom == "COM9" && loaded.Label.SmallQrTemplateOverrideBase64 == settings.Label.SmallQrTemplateOverrideBase64 &&
                   loaded.LotSettingsByProduct["PART-A"].LotNo == 123 && loaded.ResistanceProfilesByModel["PART-A"].All(channel => !channel.Enabled) &&
                   loaded.ResistanceProfilesByModel["PART-A"].Length == 10 &&
                   loaded.ResistanceProfilesByModel["PART-A"][6].Channel == 0 &&
                   loaded.ResistanceProfilesByModel["PART-A"][6].MinOhm == 5 &&
                   loaded.ResistanceProfilesByModel["PART-B"].Length == 10 &&
                   !loaded.WaterProofProfilesByModel["PART-A"].Enabled && loaded.MasterFaultCountsByModel["LEGACY"] == 2,
                "Legacy configs still load, printing is always enabled, and card/LOT/template/disabled profiles retain their meanings");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
