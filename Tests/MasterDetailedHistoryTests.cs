using System.Reflection;
using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestDetailedMasterHistory()
    {
        var displayExample = new TestHistoryRecord
        {
            InspectionType = HistoryInspectionType.MasterGood, Passed = true,
            Started = new DateTime(2026, 10, 14, 10, 58, 39, 955),
            InstallStartedAt = new DateTime(2026, 10, 14, 10, 58, 39, 955),
            TestStartedAt = new DateTime(2026, 10, 14, 10, 58, 40, 256),
            ResultAt = new DateTime(2026, 10, 14, 10, 58, 40, 258),
            Finished = new DateTime(2026, 10, 14, 10, 58, 41)
        };
        Assert(displayExample.ExportTestLogText.Contains("장착시작 10:58:39 장착시간 0.301초") &&
               displayExample.ExportTestLogText.Contains("검사시작 10:58:40 확인시각 10:58:40 확인시간 0.002초") &&
               !System.Text.RegularExpressions.Regex.IsMatch(
                   displayExample.HistoryTestLogText + displayExample.ExportTestLogText, @"\d{2}:\d{2}:\d{2}\.\d") &&
               displayExample.InstallStartedAt.Value.Millisecond == 955,
            "History and export show whole-second clock times while retaining exact timestamps and measured durations");
        string root = Path.Combine(Path.GetTempPath(), "JBZ-MasterDetail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string db = Path.Combine(root, "history.db");
            var store = new TestHistoryStore(db);
            DateTime at = DateTime.Now.AddMinutes(-10);
            store.Add(new TestHistoryRecord { Started = at, Finished = at.AddSeconds(1), PartNumber = "LEGACY",
                InspectionType = HistoryInspectionType.Product, Passed = true, Result = "PASS", CycleId = "legacy-product" });
            var settings = new ProductionSettings { MasterFaultRequiredCount = 1 };
            TestViewModel vm = CreateTestViewModel(settings);
            var model = Model(("MASTER-WIRE", new[] { 1, 2 }));
            model.PartNumber = "MASTER-HISTORY-" + Guid.NewGuid().ToString("N");
            LoadReadyModel(vm, model);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string name, object? value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            object? Call(string name, params object?[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
            ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!).GetAwaiter().GetResult();
            int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
            long probe = vm.ProbeCycleCount;
            Set("_historyStore", store);
            var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
            try
            {
                persistence.Initialization.GetAwaiter().GetResult();
                Set("_productionPersistence", persistence);
                Set("_masterValidationProductionDay", MasterSampleCatalog.ProductionDay(at));
                Set("_masterValidationSessionId", Guid.NewGuid().ToString("N"));
                var samples = new MasterSampleType?[] { null, MasterSampleType.WrongWiring, MasterSampleType.ShortCircuit, MasterSampleType.OpenCircuit };
                for (int i = 0; i < samples.Length; i++)
                {
                    MasterSampleType? sample = samples[i];
                    string inspection = sample is null ? HistoryInspectionType.MasterGood : HistoryInspectionType.MasterBad;
                    Call("BeginMasterHistoryCycle", inspection);
                    Set("_masterSampleType", sample ?? MasterSampleType.WrongWiring);
                    DateTime install = at.AddMinutes(i + 1);
                    Set("_masterInstallStartedAt", install);
                    Set("_masterTestStartedAt", install.AddSeconds(2.5));
                    FaultDetail[] faults = sample is null ? [] : [new FaultDetail
                    {
                        Type = MasterSampleCatalog.FaultType(sample.Value), WireName = "MASTER-WIRE",
                        ExpectedSourceIo = 1, ExpectedTargetIo = 2, ActualSourceIo = 1, ActualTargetIo = 3
                    }];
                    var keys = (HashSet<MasterFaultKey>)typeof(TestViewModel).GetField("_masterDetectedFaultKeys", flags)!.GetValue(vm)!;
                    keys.Clear();
                    foreach (FaultDetail fault in faults) keys.Add(MasterFaultKey.From(fault));
                    if (sample is null)
                        vm.Resistance.Add(new ResistanceResult { Name = "R1", Channel = 1, ValueOhm = 1, Passed = true });
                    Call("RecordMasterHistory", inspection, true, faults, install.AddSeconds(5.75));
                    ((Task)typeof(TestViewModel).GetField("_masterPersistenceTask", flags)!.GetValue(vm)!).GetAwaiter().GetResult();
                    Call("RecordMasterHistory", inspection, true, faults, install.AddSeconds(6));
                    Call("UpdateMasterRemovalHistory", install.AddSeconds(6), install.AddSeconds(9.25));
                    ((Task)typeof(TestViewModel).GetField("_masterPersistenceTask", flags)!.GetValue(vm)!).GetAwaiter().GetResult();
                }
                Assert(vm.Total == total && vm.Pass == pass && vm.Fail == fail && vm.ProbeCycleCount == probe,
                    "All four Master history paths preserve production and Probe counters");
            }
            finally { persistence.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

            // Reopen the DB so these assertions verify durable fields, not only the VM snapshot.
            store = new TestHistoryStore(db);
            var criteria = new HistorySearchCriteria(null, null, null, "", "ALL", MaxRows: 2);
            var rows = new List<TestHistoryRecord>();
            while (true)
            {
                var page = store.SearchSummary(criteria);
                if (page.Count == 0) break;
                rows.AddRange(page);
                criteria = criteria with { BeforeHistoryAt = page[^1].EffectiveTestStartedAt, BeforeId = page[^1].Id };
            }
            Assert(rows.Count == 5 && rows.Select(row => row.CycleId).Distinct().Count() == 5 &&
                   rows[0].CycleId == "legacy-product" && rows[0].TestStartedAt is null,
                "Master recording preserves legacy rows, keyset ordering and exactly-once cycle commits");
            var names = new[] { "정상", "오배선", "단락", "핀 빠짐 / 단선" };
            for (int i = 0; i < names.Length; i++)
            {
                TestHistoryRecord row = rows[i + 1];
                Assert(row.InspectionTypeText == names[i] + " 마스터 검사" &&
                       row.InstallDurationSeconds == 2.5 && row.TestDurationSeconds == 3.25 && row.RemovalDurationSeconds == 3.25 &&
                       row.HistoryTestLogText.Contains("장착:") && row.HistoryTestLogText.Contains("확인 합격:") &&
                       row.HistoryTestLogText.Contains("탈거:") && row.HistoryTestLogText.Contains("2.500초") &&
                       row.HistoryTestLogText.Contains("3.250초") && row.HistoryResultText == "확인 합격" &&
                       !row.HistoryTestLogText.Contains("Phiên ") && row.ExportLotText.Length == 0 &&
                       row.ExportTestLogText.Contains("확인시각") && row.ExportTestLogText.Contains("장착시간 2.500초"),
                    "Each stored sample displays its actual type and all stage timestamps and durations");
                Assert(!System.Text.RegularExpressions.Regex.IsMatch(
                    row.HistoryTestLogText + row.ExportTestLogText, @"\d{2}:\d{2}:\d{2}\.\d"),
                    "Every Master type uses HH:mm:ss in its visible and exported stage timestamps");
                Assert(!(row.InspectionTypeText + row.HistoryResultText + row.HistoryTestLogText)
                        .Any(character => character is >= '\u00c0' and <= '\u024f' or >= '\u1e00' and <= '\u1eff'),
                    "Master type, result, audit, fault and stage labels are Korean without leftover Vietnamese text");
                string complete = row.HistoryTestLogText;
                Assert(complete.Contains('\n') && !row.HistoryTestLogSingleLineText.Contains('\n') &&
                       !row.HistoryTestLogSingleLineText.Contains('\r') &&
                       complete.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                           .All(part => row.HistoryTestLogSingleLineText.Contains(part)),
                    "A compact history row preserves every detail for its full multiline hover tooltip");
            }
            Assert(rows[1].HistoryTestLogText.Contains("저항: R1=") &&
                   rows.Skip(2).All(row => row.Resistance.Length == 0 && row.HistoryTestLogText.Contains("불량 상세:") &&
                       row.HistoryTestLogText.Contains("검출 수 1/1")),
                "Good Master retains resistance results and NG samples retain their own electrical fault evidence");
            var unknown = new TestHistoryRecord { InspectionType = HistoryInspectionType.MasterBad,
                Started = at, Finished = at.AddSeconds(1), InspectionTrace = "MẪU YÊU CẦU: ĐẠT → SAI DÂY → CHẬP MẠCH" };
            Assert(unknown.InspectionTypeText == "불량 마스터 검사" && unknown.HistoryTestLogText.Contains("별도 확인시각 없음"),
                "Legacy history never guesses a sample from the requested list or invents missing confirmation timestamps");
            unknown.Passed = false;
            Assert(unknown.HistoryResultText == "확인 불합격" && unknown.HistoryTestLogText.Contains("기록 미완료") &&
                   unknown.ExportTestLogText.Contains("확인시각 미확인"),
                "Unconfirmed/failed Master results never fabricate successful confirmation or removal timing");
            string csv = Path.Combine(root, "master.csv"), xlsx = Path.Combine(root, "master.xlsx");
            HistoryExportService.ExportCsv(csv, store.EnumerateForExport(criteria with { BeforeHistoryAt = null, BeforeId = null }));
            HistoryExportService.ExportXlsx(xlsx, rows);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string csvText = File.ReadAllText(csv, Encoding.GetEncoding(949));
            Assert(csvText.Contains("오배선") && csvText.Contains("단락") && csvText.Contains("핀 빠짐 / 단선") &&
                   csvText.Contains("확인시각") && File.Exists(xlsx), "CSV/XLSX retain all Master sample types and detailed timing");
            Assert(File.ReadAllLines(csv, Encoding.GetEncoding(949)).Length == rows.Count + 1,
                "CSV keeps one physical line per history record");
            using (ZipArchive archive = ZipFile.OpenRead(xlsx))
            {
                using var sheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                using var styleStream = archive.GetEntry("xl/styles.xml")!.Open();
                var sheet = XDocument.Load(sheetStream);
                var styles = XDocument.Load(styleStream);
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                XElement[] exportedRows = sheet.Descendants(ns + "row").Skip(1).ToArray();
                string[] logs = exportedRows.Select(row => row.Elements(ns + "c")
                    .Single(cell => ((string?)cell.Attribute("r"))!.StartsWith("L", StringComparison.Ordinal))
                    .Descendants(ns + "t").Single().Value).ToArray();
                Assert(exportedRows.All(row => (string?)row.Attribute("ht") == "20" &&
                           (string?)row.Attribute("customHeight") == "1") &&
                       logs.SequenceEqual(rows.Select(row => row.ExportTestLogSingleLineText)) &&
                       !styles.Descendants(ns + "alignment").Any(alignment => (string?)alignment.Attribute("wrapText") == "1"),
                    "Excel rows have equal single-line heights and no wrapping while the complete log stays in every editable cell");
            }
            using var connection = new SqliteConnection($"Data Source={db}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            Assert((string?)command.ExecuteScalar() == "ok", "Detailed Master history DB integrity stays ok");
            command.CommandText = "SELECT COUNT(*) FROM TestFaults;";
            Assert(Convert.ToInt32(command.ExecuteScalar()) == 3, "Every NG sample retains its attached fault row");
            command.CommandText = "SELECT COUNT(*) FROM ResistanceMeasurements;";
            Assert(Convert.ToInt32(command.ExecuteScalar()) == 1, "Good sample retains its resistance child row");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(
                    Path.Combine(Path.GetTempPath(), "JBZ-MasterDetail-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, recursive: true);
        }
    }
}
