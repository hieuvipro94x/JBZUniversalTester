using System.Text.RegularExpressions;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSharedLabelCoordinates()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-SharedLabel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cfg = Path.Combine(root, "station.cfg");
            var a = Model(("PAIR", new[] { 1, 2 })); a.PartNumber = "1200020430";
            var b = Model(("PAIR", new[] { 1, 2 })); b.PartNumber = "1200020431";
            const string legacy = "N\nLEGACY_PRODUCT_LAYOUT\nP1\n";
            b.LabelTemplate = new LabelTemplateDefinition(RawTemplate: legacy);
            var settings = new ProductionSettings();
            settings.Label.BcwVehicleCode = "09";
            settings.Label.BcwPosition = "LH";
            settings.LotSettingsByProduct[a.PartNumber] = new ProductLotSettings { StartLotNo = 7000, UseConfiguredStartLot = true };
            settings.LotSettingsByProduct[b.PartNumber] = new ProductLotSettings { StartLotNo = 3000, UseConfiguredStartLot = true };
            settings.LabelTemplateTypesByProduct["OTHER-SMALL"] = LabelSettings.SmallTemplate;
            var edits = new Dictionary<string, string>();
            foreach (string type in new[] { LabelSettings.LargeTemplate, LabelSettings.LargeSqdzTemplate,
                         LabelSettings.SmallTemplate, LabelSettings.SmallQrTemplate, LabelSettings.BcwTemplate })
            {
                string original = BuiltInLabelTemplateStore.Load(BuiltInLabelTemplateStore.ReferenceFor(type));
                Match coordinate = Regex.Match(original, @"(?m)^(A|b)([0-9]+),([0-9]+),");
                Assert(coordinate.Success, "Coordinate fixture edits a verified command already present in the selected built-in template");
                string changedCoordinate = $"{coordinate.Groups[1].Value}{int.Parse(coordinate.Groups[2].Value) + 3},{int.Parse(coordinate.Groups[3].Value) + 2},";
                string edited = original[..coordinate.Index] + changedCoordinate + original[(coordinate.Index + coordinate.Length)..];
                settings.Label.TemplateType = type;
                settings.LabelTemplateTypesByProduct[a.PartNumber] = type;
                BuiltInLabelTemplateStore.SaveOverride(settings.Label, type, edited);
                edits[type] = edited;
                ProductionConfigService.SaveLegacyCfg(settings, cfg);
                settings = ProductionConfigService.LoadSavedSettings(cfg);
                foreach (var model in new[] { a, b, a })
                {
                    ProductionConfigService.ApplyProductLabelTemplate(settings, model.PartNumber);
                    var row = new TestHistoryRecord { CycleId = Guid.NewGuid().ToString("N"), Passed = true,
                        LotNo = model == a ? 7021 : 3001, Started = DateTime.Now, Finished = DateTime.Now };
                    var request = LabelPrintRequest.Capture(row, model, settings.Label);
                    Console.WriteLine($"SHARED_LABEL type={type} product={model.PartNumber} mode={request.Profile.Mode} inline_match={request.Profile.InlineTemplate == edited} coordinate_match={request.Payload.Contains(changedCoordinate, StringComparison.Ordinal)} legacy={request.Payload.Contains("LEGACY_PRODUCT_LAYOUT", StringComparison.Ordinal)}");
                    Assert(request.Profile.InlineTemplate == edited && request.Payload.Contains(changedCoordinate, StringComparison.Ordinal) &&
                           !request.Payload.Contains("LEGACY_PRODUCT_LAYOUT", StringComparison.Ordinal),
                        $"Shared {type} coordinates survive CFG reload and A -> B -> A even when B has an older THT label command");
                    Assert(request.Data.LotNo == row.LotNo && request.Data.PartNumber == model.PartNumber,
                        "A shared label layout keeps each product's own LOT and label data");
                }
                Assert(settings.LabelTemplateTypesByProduct["OTHER-SMALL"] == LabelSettings.SmallTemplate &&
                       settings.LotSettingsByProduct[a.PartNumber].StartLotNo == 7000 && settings.LotSettingsByProduct[b.PartNumber].StartLotNo == 3000,
                    "Sharing coordinates never changes another product's label type or start LOT");
            }
            settings = ProductionConfigService.LoadSavedSettings(cfg);
            Assert(edits.All(edit => BuiltInLabelTemplateStore.LoadOverride(settings.Label, edit.Key) == edit.Value) &&
                   b.LabelTemplate.RawTemplate == legacy,
                "All five shared label layouts persist independently; source THT label data remains untouched");
            foreach (string type in edits.Keys) BuiltInLabelTemplateStore.ClearOverride(settings.Label, type);
            settings.Label.TemplateType = LabelSettings.LargeTemplate;
            ProductionConfigService.ApplyProductLabelTemplate(settings, b.PartNumber);
            Assert(LabelPrintRequest.Capture(new TestHistoryRecord { LotNo = 3001, Finished = DateTime.Now }, b, settings.Label)
                       .Payload.Contains("LEGACY_PRODUCT_LAYOUT", StringComparison.Ordinal),
                "Clearing the shared customization preserves the existing legacy label fallback");
        }
        finally { Directory.Delete(root, true); }
    }
}
