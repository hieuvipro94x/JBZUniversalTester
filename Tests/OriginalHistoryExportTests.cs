using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestOriginalHistoryExportColumns()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string root = Path.Combine(Path.GetTempPath(), "JBZ-original-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ProductModel model = ParseThtLabelModelText("ALC-SAVED");
            string db = Path.Combine(root, "existing.db");
            var original = new TestHistoryStore(db);
            DateTime at = new(2026, 1, 12, 7, 29, 44);
            string[] inspections = [HistoryInspectionType.Product, HistoryInspectionType.Product,
                HistoryInspectionType.MasterGood, HistoryInspectionType.LeakRetest];
            ProductionResultCommitRequest? firstRequest = null;
            for (int index = 0; index < inspections.Length; index++)
            {
                var row = new TestHistoryRecord
                {
                    CycleId = "saved-" + index, InspectionType = inspections[index],
                    Started = at.AddSeconds(index), TestStartedAt = index == 0 ? null : at.AddSeconds(index),
                    Finished = at.AddSeconds(index == 0 ? 60 : index + 1), Passed = index != 1,
                    LotNo = index == 0 ? 2001 : 9999, ProductionCounter = 8000 + index,
                    PartNumber = model.PartNumber, PartName = model.ProductName,
                    ModelName = model.ModelName, ModelFile = model.SourcePath,
                    Eco = model.Eco, Nco = model.Nco, Alc = model.Alc,
                    Result = index == 1 ? "FAIL" : "PASS", HtdrvName = "SAVED-VERSION",
                    InspectionTrace = "SAVED-TRACE", BarcodeValue = "SAVED-BARCODE",
                    PrintStatus = LabelPrintStatus.Printed.ToString(), LabelPayload = "RAW-PRINT-PAYLOAD"
                };
                FaultDetail[] faults = index == 1
                    ? [new FaultDetail { Type = ProductFaultType.WrongWiring, ExpectedSourceIo = 1,
                        ExpectedTargetIo = 2, ActualSourceIo = 1, ActualTargetIo = 3 }]
                    : [];
                ProductionResultCommitRequest request = ProductionResultCommitRequest.Capture(
                    row, model, new ProductionSettings(), faults, [], null, "SAVED-VERSION");
                if (index == 0)
                {
                    request = request with
                    {
                        Resistance = [new ResistancePersistenceSnapshot(1, "R1", 100, 90, 110, true, 1, 0)],
                        WaterProof = [new WaterProofPersistenceSnapshot(1, true, 80, 79, 1, true)]
                    };
                    firstRequest = request;
                }
                original.CommitResult(request, null);
            }

            // Reopen an existing database, including a legacy NULL TestStartedAt row.
            // No current THT file is available: export must use the stored metadata.
            string before = HistoryDataSnapshot(db);
            var store = new TestHistoryStore(db);
            var criteria = new HistorySearchCriteria(null, null, null, string.Empty, "ALL", MaxRows: 1);
            var loaded = new List<TestHistoryRecord>();
            while (loaded.Count < 4)
            {
                IReadOnlyList<TestHistoryRecord> page = store.SearchSummary(criteria);
                Assert(page.Count == 1, "Original-column History retains incremental access to every saved row");
                loaded.Add(page[0]);
                criteria = criteria with { BeforeHistoryAt = page[0].EffectiveTestStartedAt, BeforeId = page[0].Id };
            }
            Assert(loaded.Select(row => row.CycleId).SequenceEqual(new[] { "saved-0", "saved-1", "saved-2", "saved-3" }) &&
                   loaded.Select(row => row.Id).Distinct().Count() == 4 && loaded[0].TestStartedAt is null &&
                   loaded.All(row => row.ExportLotText.Length == 0) &&
                   loaded.Select(row => row.ExportPassCountText).SequenceEqual(new[] { "2001", "", "", "" }) &&
                   loaded.Select(row => row.ExportProgressText).SequenceEqual(new[] { "1/1", "0/1", "1/1", "1/1" }),
                "Blank Lot, saved PASS count and progress preserve legacy fallback/order and isolate Master/Leak retests");
            var all = new HistorySearchCriteria(null, null, null, string.Empty, "ALL", MaxRows: 1);
            HistorySummary summary = store.GetHistorySummary(all);
            Assert(summary.Total == 4 && summary.ProductTotal == 2 && summary.ProductPass == 1 &&
                   summary.ProductFail == 1 && summary.MasterTotal == 1 && summary.LeakRetestTotal == 1,
                "Original-column pagination does not change full filtered summary counts");

            string csv = Path.Combine(root, "history.csv"), xlsx = Path.Combine(root, "history.xlsx");
            Assert(HistoryExportService.ExportCsv(csv, store.EnumerateForExport(all)) == 4 &&
                   HistoryExportService.ExportXlsx(xlsx, store.EnumerateForExport(all)) == 4,
                "Both exports include the full filtered dataset even when the UI page size is one");
            string[] expected = ["장착시간", "파일", "파트명", "파트번호", "Eco", "Nco", "Alc", "Lot",
                "진도", "결과", "합격", "검사기록", "바코드입력", "바코드출력", "측정", "JBZUniversalTester", "내용", "메모"];
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string[] lines = File.ReadAllLines(csv, Encoding.GetEncoding(949));
            Assert(lines.Length == 5 && lines[0].Split(',').SequenceEqual(expected) &&
                   lines[1].Contains(",AE EV PE,NCO-7,ALC-SAVED,,1/1,합격,2001,", StringComparison.Ordinal) &&
                   lines[2].Contains(",,0/1,불량,,", StringComparison.Ordinal),
                "CSV has original column order and exact parsed THT metadata with separate blank Lot and PASS count");
            string exportVersion = $"V{ProgramIdentityService.VersionText}";
            using (var csvReader = new Microsoft.VisualBasic.FileIO.TextFieldParser(csv, Encoding.GetEncoding(949)))
            {
                csvReader.SetDelimiters(",");
                csvReader.ReadFields();
                for (int index = 0; index < loaded.Count; index++)
                    Assert(csvReader.ReadFields()![15] == exportVersion,
                        "CSV displays the running software version, including existing history");
            }
            Assert(loaded.All(row => row.HtdrvName == "SAVED-VERSION"),
                "Export leaves saved program identities unchanged");
            using (ZipArchive archive = ZipFile.OpenRead(xlsx))
            {
                using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
                XDocument sheet = XDocument.Parse(reader.ReadToEnd());
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                XElement[] rows = sheet.Descendants(ns + "row").ToArray();
                string Text(XElement cell) => string.Concat(cell.Descendants(ns + "t").Select(t => t.Value));
                string Cell(int row, string address) => Text(rows[row - 1].Elements(ns + "c").Single(c => (string?)c.Attribute("r") == address));
                Assert(rows.Length == 5 && rows[0].Elements(ns + "c").Select(Text).SequenceEqual(expected) &&
                       Cell(2, "E2") == model.Eco && Cell(2, "F2") == model.Nco && Cell(2, "G2") == model.Alc &&
                       Cell(2, "H2") == "" && Cell(2, "I2") == "1/1" && Cell(2, "K2") == "2001" &&
                       Cell(3, "H3") == "" && Cell(3, "I3") == "0/1" && Cell(3, "K3") == "" &&
                       Cell(2, "N2") == "RAW-PRINT-PAYLOAD" &&
                       DateTime.FromOADate(double.Parse(rows[1].Elements(ns + "c").First().Element(ns + "v")!.Value,
                           CultureInfo.InvariantCulture)) == at,
                    "Actual XLSX cells retain THT fields, legacy test-start timestamp, PASS count and barcode semantics");
                Assert(Enumerable.Range(2, 4).All(row => Cell(row, $"P{row}") == exportVersion),
                    "Every XLSX row, including existing history, displays the running software version under JBZUniversalTester");
            }

            Assert(HistoryDataSnapshot(db) == before,
                "Reopening, filtering, paging and CSV/XLSX export leave every saved Test, child row and metadata unchanged");
            // Part/model last-used timestamps are mutable repository bookkeeping;
            // exactly-once persistence concerns the Test and its attached children.
            string testsBeforeDuplicate = HistoryDataSnapshot(db, includeMetadata: false);
            ProductionCommitResult duplicate = store.CommitResult(firstRequest!, null);
            Assert(duplicate.AlreadyCommitted && HistoryDataSnapshot(db, includeMetadata: false) == testsBeforeDuplicate,
                "Existing CycleId stays exactly once with its original fault/resistance/Leak relationships");
            using var verify = new SqliteConnection($"Data Source={db};Pooling=False");
            verify.Open();
            using SqliteCommand check = verify.CreateCommand();
            check.CommandText = "PRAGMA integrity_check;";
            Assert((string?)check.ExecuteScalar() == "ok", "Original-column export database integrity_check remains ok");
            check.CommandText = "PRAGMA foreign_key_check;";
            using SqliteDataReader foreignKeys = check.ExecuteReader();
            Assert(!foreignKeys.Read(), "Original-column export leaves no orphan child rows");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string HistoryDataSnapshot(string db, bool includeMetadata = true)
    {
        using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
        connection.Open();
        var tables = new Dictionary<string, List<object?[]>>();
        string[] names = includeMetadata
            ? ["Tests", "TestFaults", "ResistanceMeasurements", "WaterProofMeasurements", "Parts", "Models"]
            : ["Tests", "TestFaults", "ResistanceMeasurements", "WaterProofMeasurements"];
        foreach (string table in names)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY Id;";
            using SqliteDataReader reader = command.ExecuteReader();
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                object?[] row = new object?[reader.FieldCount];
                for (int column = 0; column < row.Length; column++)
                    row[column] = reader.IsDBNull(column) ? null : reader.GetValue(column);
                rows.Add(row);
            }
            tables[table] = rows;
        }
        return JsonSerializer.Serialize(tables);
    }
}
