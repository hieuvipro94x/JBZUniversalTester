using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestModelRelayWiring()
    {
        OnSettingsDispatcher(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "JBZ-RelayModels-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string cfg = Path.Combine(root, "settings.cfg");
            try
            {
                var settings = new ProductionSettings
                {
                    RelayWiringMode = 0, LastThtPath = Path.Combine(root, "A.tht"),
                    Relay1JigPulseMs = 50, Relay2MarkingPulseMs = 50, PassMarkingToJigDelayMs = 0
                };
                ProductionConfigService.SaveLegacyCfg(settings, cfg);
                var editor = new ProductionSettingsViewModel(null,
                    saved => ProductionConfigService.SaveLegacyCfg(saved, cfg), settings);
                editor.Settings.RelayWiringMode = 1;
                await editor.SaveAsync();
                Assert(editor.Settings.RelayWiringMode == 1, "Saving preserves the selected THT relay mode in the editor");
                settings = ProductionConfigService.LoadSavedSettings(cfg);
                Assert(settings.RelayWiringMode == 0 &&
                       ProductionConfigService.GetRelayWiringModeForPath(settings, @"D:\Other\a.tht") == 1 &&
                       ProductionConfigService.GetRelayWiringModeForPath(settings, @"D:\Other\B.tht") == 0,
                    "Saving A persists by case-insensitive THT filename without changing the old fallback or B");

                settings.LastThtPath = Path.Combine(root, "B.tht");
                editor = new ProductionSettingsViewModel(null,
                    saved => ProductionConfigService.SaveLegacyCfg(saved, cfg), settings);
                Assert(editor.Settings.RelayWiringMode == 0, "Opening B does not inherit the preceding A selection");
                editor.Save();
                settings = ProductionConfigService.LoadSavedSettings(cfg);
                settings.LastThtPath = Path.Combine(root, "A.tht");
                editor = new ProductionSettingsViewModel(null,
                    saved => ProductionConfigService.SaveLegacyCfg(saved, cfg), settings);
                Assert(editor.Settings.RelayWiringMode == 1, "Reopening A recovers its saved relay wiring");

                var runtime = ProductionConfigService.LoadSavedSettings(cfg);
                using TestEngine engine = CreateEngine(out FakeBoard board, runtime);
                foreach ((string file, int mode) in new[] { ("A.tht", 1), ("B.tht", 0), ("A.tht", 1) })
                {
                    var model = Model(("PAIR", new[] { 1, 18 }));
                    model.SourcePath = Path.Combine(root, file);
                    model.PartNumber = "SAME-PRODUCT-CODE";
                    engine.SetModel(model);
                    board.Commands.Clear();
                    bool passed = await engine.CompletePassAsync([], continuityAlreadyValidated: true);
                    int marking = mode == 1 ? 1 : 2, jig = mode == 1 ? 2 : 1;
                    Assert(passed && board.Commands.IndexOf($"SET:{marking}") >= 0 &&
                           board.Commands.IndexOf($"SET:{marking}") < board.Commands.IndexOf($"SET:{jig}") &&
                           board.Commands.Last() == "OFF", "PASS uses the selected THT wiring and returns all relays OFF");
                    board.Commands.Clear();
                    await engine.EjectFaultProductAsync();
                    Assert(board.Commands.Count(c => c == $"SET:{jig}") == 1 &&
                           !board.Commands.Contains($"SET:{marking}") && board.Commands.Last() == "OFF",
                        "FAIL opens only the selected THT JIG relay and never MARKING");
                    board.Commands.Clear();
                    await engine.EjectMasterSampleAsync();
                    Assert(board.Commands.Contains($"SET:{jig}") && !board.Commands.Contains($"SET:{marking}"),
                        "Master eject also follows the selected THT wiring");
                    var snapshot = ProductionConfigSnapshot.Capture(runtime, "TEST", model.SourcePath);
                    Assert(snapshot.RelayWiringMode == mode && snapshot.FullConfigJson.Contains($"\"RelayWiringMode\":{mode}"),
                        "History snapshots record the effective THT wiring rather than the legacy default");
                }
                var legacy = new ProductionSettings { RelayWiringMode = 1 };
                Assert(ProductionConfigService.GetRelayWiringModeForPath(legacy, "Older.tht") == 1,
                    "Older CFGs retain their original relay wiring until the THT receives a saved override");
            }
            finally { Directory.Delete(root, recursive: true); }
        });
    }
}
