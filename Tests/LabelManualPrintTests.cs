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
        var validRange = typeof(ProductionSettingsPage).GetMethod("IsValidBatchPrintRange",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Valid(long first, long last) => (bool)validRange.Invoke(null,
            new object[] { first, last, LabelSettings.SmallQrTemplate })!;
        Assert(Valid(1, 120), "A batch of 120 labels must be accepted");
        Assert(!Valid(0, 120) && !Valid(120, 1), "QR ranges reject zero and reversed endpoints");
        var model = new ProductModel { PartNumber = "PRINT-ONLY-PART", ProductName = "PRINT PRODUCT" };
        var settings = new LabelSettings { TemplateType = LabelSettings.SmallQrTemplate };
        for (long lot = 1; lot <= 120; lot++)
        {
            var request = LabelPrintRequest.Capture(new TestHistoryRecord
            {
                Finished = new DateTime(2026, 10, 8, 10, 0, 0), LotNo = lot,
                CycleId = "BATCH-PRINT-" + lot, PartNumber = model.PartNumber
            }, model, settings);
            Assert(request.Data.LotNo == lot && request.Data.Barcode.EndsWith(lot.ToString("D4"), StringComparison.Ordinal),
                "Manual QR LOT 1 through 120 produces every label from 0001 through 0120");
            Assert(request.Data.PartNumber == "PRINT-ONLY-PART",
                "The print request uses the chosen print model identity");
        }
    }
}
