using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSmallQrManualPrintStart()
    {
        var minimum = typeof(ProductionSettingsPage).GetMethod("MinimumBatchPrintLot",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert((long)minimum.Invoke(null, new object[] { LabelSettings.SmallQrTemplate })! == 1,
            "Small QR batches reject LOT zero and begin at one");
        Assert((long)minimum.Invoke(null, new object[] { LabelSettings.SmallTemplate })! == 0,
            "Other label types retain their original valid LOT range");
        var model = new ProductModel { PartNumber = "PRINT-ONLY-PART", ProductName = "PRINT PRODUCT" };
        var settings = new LabelSettings { TemplateType = LabelSettings.SmallQrTemplate };
        for (long lot = 1; lot <= 3; lot++)
        {
            var request = LabelPrintRequest.Capture(new TestHistoryRecord
            {
                Finished = new DateTime(2026, 10, 8, 10, 0, 0), LotNo = lot,
                CycleId = "BATCH-PRINT-" + lot, PartNumber = model.PartNumber
            }, model, settings);
            Assert(request.Data.LotNo == lot && request.Data.Barcode.EndsWith(lot.ToString("D4"), StringComparison.Ordinal),
                "Manual QR LOT 1,2,3 produces 0001,0002,0003 without production LOT offset");
            Assert(request.Data.PartNumber == "PRINT-ONLY-PART",
                "The print request uses the chosen print model identity");
        }
    }
}
