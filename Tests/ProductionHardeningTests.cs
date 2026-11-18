using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestInvalidProductionConfigBlocksDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-invalid-cfg-" + Guid.NewGuid().ToString("N"));
        object? previous = AppContext.GetData("JBZUniversalTester.SelfTest.DatabaseFile");
        Directory.CreateDirectory(root);
        try
        {
            AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile", Path.Combine(root, "history.db"));
            foreach (string invalid in new[] { "", "unreadable syntax", "[DeviceName]KEEP\n[devicename]DUPLICATE\n[PassMarkingRelayEnabled]False\n" })
            {
                File.WriteAllText(RuntimePaths.ConfigFile, invalid);
                bool rejected = false;
                try { ProductionConfigService.Load(); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected && File.ReadAllText(RuntimePaths.ConfigFile) == invalid,
                    "Invalid existing CFG blocks startup defaults and preserves the original file");
            }
            File.WriteAllText(RuntimePaths.ConfigFile, "[ExpansionCardCount]10\n[DeviceName]KEEP\n[PassMarkingRelayEnabled]False\n");
            var valid = ProductionConfigService.Load();
            Assert(valid.ExpansionCardCount == 10 && valid.DeviceName == "KEEP" && !valid.PassMarkingRelayEnabled,
                "Valid partial legacy CFG keeps its hardware and relay settings");
            using (var locked = new FileStream(RuntimePaths.ConfigFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool rejected = false;
                try { ProductionConfigService.Load(); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "An occupied CFG cannot silently become default hardware settings");
            }
            ProductionConfigService.Save(valid);
            var writer = Task.Run(() =>
            {
                for (int i = 0; i < 50; i++) ProductionConfigService.Save(valid);
            });
            for (int i = 0; i < 50; i++)
            {
                var concurrent = ProductionConfigService.Load();
                Assert(concurrent.ExpansionCardCount == 10 && !concurrent.PassMarkingRelayEnabled,
                    "Reading during an in-process atomic save preserves a complete configuration snapshot");
            }
            writer.GetAwaiter().GetResult();
            File.Delete(RuntimePaths.ConfigFile);
            Assert(ProductionConfigService.Load().ExpansionCardCount == new ProductionSettings().ExpansionCardCount,
                "A genuine fresh station can still initialize defaults");
        }
        finally
        {
            AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile", previous);
            Directory.Delete(root, true);
        }
    }

    private static void TestLogStorageFailureRecovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-log-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var logger = new AsyncFileLogService();
            string path = Path.Combine(root, "JBZUniversalTester.log");
            using (var locked = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                logger.Configure(true, root);
                Assert(SpinWait.SpinUntil(() => logger.LastWriteError.Length > 0, 5000),
                    "An unavailable log file exposes a meaningful storage error");
                var timer = Stopwatch.StartNew();
                for (int i = 0; i < 20000; i++) logger.Application("QUEUED-" + i);
                Assert(logger.DroppedLogCount > 0 && timer.Elapsed < TimeSpan.FromSeconds(3),
                    "A full log queue bounds memory and never waits for storage on scan/UI callers");
            }
            Assert(SpinWait.SpinUntil(() => logger.LastWriteError.Length == 0, 5000),
                "Logging automatically resumes after the file becomes writable");
            Assert(SpinWait.SpinUntil(() =>
            {
                long before = logger.DroppedLogCount;
                logger.Application("RECOVERY-FINAL");
                return logger.DroppedLogCount == before;
            }, 5000), "Recovered logging accepts new entries after the backlog drains");
            logger.Dispose();
            string content = File.ReadAllText(path);
            Assert(content.Contains("LOG_QUEUE_FULL") && content.Contains("RECOVERY-FINAL"),
                "Recovery reports dropped diagnostics and continues writing subsequent entries");

            string broken = Path.Combine(root, "broken");
            Directory.CreateDirectory(Path.Combine(broken, "JBZUniversalTester.log"));
            var failing = new AsyncFileLogService();
            failing.Configure(true, broken);
            Assert(SpinWait.SpinUntil(() => failing.LastWriteError.Length > 0, 5000), "Permanent log failure is observed");
            var shutdown = Stopwatch.StartNew();
            failing.Dispose();
            Assert(shutdown.Elapsed < TimeSpan.FromSeconds(4), "Permanent storage failure cannot hang application shutdown");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestXlsxControlCharacterEncoding()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-xlsx-controls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "history.xlsx");
            string original = "A\0\u0001\u001b\ufffe\ud800Z _x0001_ 한국 🙂 & < >\r\n";
            var row = new TestHistoryRecord { Started = DateTime.Today, Finished = DateTime.Today,
                PartName = original, LabelPayload = "N\r\n\u001bP1\u000c", Passed = true,
                InspectionType = HistoryInspectionType.Product };
            Assert(HistoryExportService.ExportXlsx(path, [row]) == 1, "Control-containing history remains exportable");
            using var zip = ZipFile.OpenRead(path);
            using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var document = XDocument.Load(stream);
            var text = document.Descendants(ns + "t").Select(t => t.Value).ToArray();
            Assert(text.Any(t => t.Contains("_x0000__x0001__x001B__xFFFE__xD800_") &&
                   t.Contains("_x005F_x0001_") && t.Contains("한국 🙂 & < >")),
                "SpreadsheetML escapes invalid XML code units, preserves Unicode and protects literal escape sequences");
            Assert(text.Any(t => t.Contains("_x001B_P1_x000C_")) && row.PartName == original,
                "Printer control bytes are represented losslessly without modifying stored history");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestPatchedNativeSqliteVersion()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";
        string version = (string)command.ExecuteScalar()!;
        Assert(Version.Parse(version) >= new Version(3, 50, 2), "Native SQLite contains the CVE-2025-6965 fix: " + version);
        Console.WriteLine("NATIVE_SQLITE_VERSION: " + version);
    }
}
