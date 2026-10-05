using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestGlobalMasterConfigurationAndCompletion()
    {
        ProductModel first = Model(("PAIR", new[] { 1, 2 }));
        first.PartNumber = first.ModelName = "GLOBAL-A";
        first.SourcePath = "GLOBAL-A.tht";
        ProductModel second = Model(("PAIR", new[] { 1, 2 }));
        second.PartNumber = second.ModelName = "GLOBAL-B";
        second.SourcePath = "GLOBAL-B.tht";
        var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
        settings.MasterFaultCountsByModel[first.PartNumber] = 4;
        settings.MasterFaultCountsByModel[second.PartNumber] = 0;
        settings.MasterSelectedFaultSamplesByModel[first.PartNumber] = MasterSampleSelection.ShortCircuit;
        settings.MasterOpenFaultCountsByModel[first.PartNumber] = 9;

        foreach (ProductModel model in new[] { first, second })
        {
            Assert(ProductionConfigService.GetMasterSampleRequiredCount(settings, model) == 0,
                "Shared disabled Master overrides every legacy model count");
            TestViewModel disabled = CreateTestViewModel(settings);
            LoadReadyModel(disabled, model);
            Assert(disabled.MasterApproved && !disabled.IsMasterSequenceActive,
                "Every model skips samples when shared Master is disabled");
        }

        ProductionConfigService.SetMasterFaultRequiredCountForPath(settings, first.SourcePath, 3);
        ProductionConfigService.SetMasterSelectedFaultSamplesForPath(settings, first.SourcePath,
            MasterSampleSelection.WrongWiring | MasterSampleSelection.OpenCircuit);
        ProductionConfigService.SetMasterOpenFaultRequiredCountForPath(settings, first.SourcePath, 2);
        foreach (ProductModel model in new[] { first, second })
        {
            Assert(ProductionConfigService.GetMasterSampleRequiredCount(settings, model) == 3 &&
                   ProductionConfigService.GetMasterFaultRequiredCountForPath(settings, model.SourcePath) == 3,
                "Saving one model's page updates shared Master count for all models");
            Assert(ProductionConfigService.GetMasterOpenFaultRequiredCount(settings, model) == 2 &&
                   ProductionConfigService.GetMasterSelectedFaultSamples(settings, model) ==
                       (MasterSampleSelection.WrongWiring | MasterSampleSelection.OpenCircuit),
                "Sample selection and open count are shared despite legacy overrides");
            TestViewModel enabled = CreateTestViewModel(settings);
            LoadReadyModel(enabled, model);
            Assert(!enabled.MasterApproved && enabled.MasterState == MasterSequenceState.WaitingGoodMaster,
                "Every model requires its own samples when shared Master is enabled");
        }

        string root = Path.Combine(Path.GetTempPath(), "JBZ-GlobalMaster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string configPath = Path.Combine(root, "settings.cfg");
            ProductionConfigService.SaveLegacyCfg(settings, configPath);
            var reload = (ProductionSettings)typeof(ProductionConfigService).GetMethod("LoadEnglishCfg",
                BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { configPath })!;
            Assert(ProductionConfigService.GetMasterSampleRequiredCount(reload, second) == 3 &&
                   ProductionConfigService.GetMasterOpenFaultRequiredCount(reload, second) == 2 &&
                   reload.MasterFaultCountsByModel[first.PartNumber] == 4,
                "Shared settings survive CFG reload while legacy entries are preserved");

            TestViewModel vm = CreateTestViewModel(settings);
            LoadReadyModel(vm, first);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            void SetField(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            var samples = (HashSet<MasterSampleType>)typeof(TestViewModel).GetField(
                "_validatedMasterFaultSamples", flags)!.GetValue(vm)!;
            SetField("_masterGoodVerified", true);
            SetField("_masterBadVerified", true);
            SetField("_masterRequiredFaultCount", 2);
            var keys = (HashSet<MasterFaultKey>)typeof(TestViewModel).GetField(
                "_masterDetectedFaultKeys", flags)!.GetValue(vm)!;
            keys.Add(new MasterFaultKey(ProductFaultType.OpenCircuit, 1, 2, 1, 2));
            keys.Add(new MasterFaultKey(ProductFaultType.OpenCircuit, 3, 4, 3, 4));
            SetField("_masterRecordedHistoryStore", new TestHistoryStore(Path.Combine(root, "history.db")));
            SetField("_productionRuntimeState", (int)ProductionRuntimeState.TestingRealtime);
            SetField("_productionPresentationMode", (int)ProductionPresentationMode.Product);
            SetField("_presentationCycleStarted", true);
            samples.Add(MasterSampleType.WrongWiring);
            MethodInfo complete = typeof(TestViewModel).GetMethod("CompleteMasterValidation", flags)!;
            complete.Invoke(vm, null);
            Assert(!vm.MasterApproved, "Missing selected NG sample cannot unlock production");
            samples.Add(MasterSampleType.OpenCircuit);
            SetField("_masterEjectInProgress", 1);
            complete.Invoke(vm, null);
            Assert(!vm.MasterApproved, "Master cannot finish while JIG relay is still active");
            SetField("_masterEjectInProgress", 0);
            complete.Invoke(vm, null);
            Assert(vm.MasterApproved && vm.MasterState == MasterSequenceState.Completed &&
                   vm.CurrentProductionRuntimeState == ProductionRuntimeState.WaitingForProduct &&
                   vm.CurrentProductionPresentationMode == ProductionPresentationMode.Waiting &&
                   vm.ResultStatusText == "LẮP SẢN PHẨM" && vm.CenterResultText == "LẮP SẢN PHẨM",
                "Completed Master clears testing state and returns both result areas to product installation");
        }
        finally
        {
            // Temporary fixture data only; production databases are never touched.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
