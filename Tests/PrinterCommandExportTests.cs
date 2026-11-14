using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static TestHistoryRecord[] PrinterCommandExportSamples()
    {
        TestHistoryRecord Row(long lot, string kind, bool pass, string payload) => new()
        {
            CycleId = "export-sample-" + lot + kind, InspectionType = kind, Passed = pass,
            Started = new DateTime(2026, 10, 21, 10, 7, 54).AddSeconds(lot - 7003),
            PartNumber = "test", PartName = "test", ModelName = "2", ModelFile = @"C:\Item\2.tht",
            LotNo = lot, BarcodeValue = "BARCODE_ONLY_MUST_NOT_REPLACE_COMMANDS",
            LabelPayload = payload, PrintStatus = LabelPrintStatus.Printed.ToString()
        };
        string Payload(long lot) => "\r\nN\r\n\r\nFR\"KS91\"\r\n?\r\ntest\r\n\r\n\r\n261021" + lot +
            "WH\r\ntest,SQDZQAL" + lot + "\r\nP1\r\n";
        var failedPrinter = Row(7008, HistoryInspectionType.Product, true, Payload(7008));
        failedPrinter.PrintStatus = LabelPrintStatus.Failed.ToString();
        var pendingPrinter = Row(7009, HistoryInspectionType.Product, true, Payload(7009));
        pendingPrinter.PrintStatus = LabelPrintStatus.NotRequested.ToString();
        return [Row(7003, HistoryInspectionType.Product, true, Payload(7003)),
            Row(7004, HistoryInspectionType.Product, true, Payload(7004)),
            Row(7005, HistoryInspectionType.Product, true, ""),
            Row(7006, HistoryInspectionType.Product, false, Payload(7006)),
            Row(7007, HistoryInspectionType.MasterGood, true, Payload(7007)),
            Row(7010, HistoryInspectionType.Interrupted, false, Payload(7010)),
            failedPrinter, pendingPrinter];
    }

    private static void TestExcelSavedPrinterCommands()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-command-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var records = PrinterCommandExportSamples();
            string[] originals = records.Select(r => r.LabelPayload).ToArray();
            string xlsx = Path.Combine(root, "history.xlsx"), csv = Path.Combine(root, "history.csv");
            Assert(HistoryExportService.ExportXlsx(xlsx, records) == records.Length &&
                HistoryExportService.ExportCsv(csv, records) == records.Length, "All filtered rows remain in Excel/CSV export");
            using var zip = ZipFile.OpenRead(xlsx);
            using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            var doc = XDocument.Parse(reader.ReadToEnd());
            string Cell(string address) => string.Concat(doc.Descendants().Single(e => e.Name.LocalName == "c" &&
                (string?)e.Attribute("r") == address).Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
            Assert(Cell("N2") == originals[0] && Cell("N3") == originals[1],
                "Excel preserves the entire FR KS91 / barcode / LOT / P1 block, including CRLF, quotes and blank lines");
            Assert(Cell("N4") == "" && Cell("N5") == "" && Cell("N6") == "" && Cell("N7") == "",
                "Missing payload, product FAIL, Master and interrupted rows never get invented printer commands or barcode-only fallback");
            Assert(Cell("N8") == originals[6] && Cell("N9") == originals[7],
                "Export preserves saved attempted commands independently of printer delivery status");
            Assert(Cell("K2") == "7003" && Cell("K3") == "7004" && records.Select(r => r.LabelPayload).SequenceEqual(originals),
                "Export neither renumbers LOT nor mutates saved printer commands");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            string text = File.ReadAllText(csv, Encoding.GetEncoding(949));
            Assert(!text.Contains("BARCODE_ONLY_MUST_NOT_REPLACE_COMMANDS", StringComparison.Ordinal) &&
                text.Contains("\"" + originals[0].Replace("\"", "\"\"") + "\"", StringComparison.Ordinal),
                "CSV also retains the exact quoted command block without a barcode-only substitute");
        }
        finally { Directory.Delete(root, true); }
    }
}
