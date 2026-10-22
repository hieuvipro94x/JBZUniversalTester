using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestRejectedMasterResistanceRemoval()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (bool resistancePass in new[] { false, true })
        {
            var settings = new ProductionSettings { MasterFaultRequiredCount = 1,
                MasterSelectedFaultSamples = MasterSampleSelection.WrongWiring,
                ResistanceChannels = [new() { Enabled = true, Name = "R1", Channel = 1, MinOhm = 0.5, MaxOhm = 2 }] };
            var app = new AppSettings();
            app.Test.ResistanceMinimumSettleMs = 0;
            app.Test.ResistanceSampleIntervalMs = 0;
            app.Test.ResistanceStableSampleCount = 2;
            app.Test.ResistanceStabilityTimeoutMs = 100;
            var board = new FakeBoard();
            var visa = new FakeKeysightVisaService(connected: true, measurement: resistancePass ? 1 : 3);
            using var engine = new TestEngine(board, visa, app, settings);
            var vm = new TestViewModel(new MainViewModel(), engine, board, visa, new WaterProofSerialService(),
                app, settings, new LegacyPhtHistoryService(enabled: false), requireStartupIoClear: false);
            var model = Model(("A", new[] { 1, 2 }), ("B", new[] { 3, 4 }));
            model.PartNumber = "MASTER-REJECTED-" + Guid.NewGuid().ToString("N");
            void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
            Set("_model", model);
            engine.SetModel(model);
            Set("_runtimeMode", 1);
            Set("_masterSequenceState", MasterSequenceState.TestingGoodMaster);
            Set("_requiredMasterFaultSamples", new[] { MasterSampleType.WrongWiring });
            Set("_masterPostStarted", 1);
            using var cycle = new CancellationTokenSource();
            Set("_cycleCts", cycle);
            long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
            ScanFrame Frame(long seq, bool all, long scanGeneration = 2) => FrameSeq(seq, all
                ? [(1, new[] { 2 }), (3, new[] { 4 })] : [(1, new[] { 2 })]) with
                { SourceCount = 640, ExpectedIoCount = 640, ScanUnitCount = 10, ScanGeneration = scanGeneration };
            board.Publish(Frame(100, true));
            long measuringFrame = 101;
            if (resistancePass) visa.BeforeMeasure = () => board.Publish(Frame(measuringFrame++, false));
            board.StartScanCallback = transport => transport.Publish(Frame(300, !resistancePass, 3));
            ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!).GetAwaiter().GetResult();
            int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
            ((Task)typeof(TestViewModel).GetMethod("CompleteGoodMasterAsync", flags)!.Invoke(vm, [generation])!)
                .GetAwaiter().GetResult();
            Assert(board.IsScanning && board.Commands.Last() == "START" &&
                   vm.MasterState == MasterSequenceState.TestingGoodMaster && vm.SelectedOperationTabIndex == 1 &&
                   vm.ResistanceDisplayRows.Count == 1 && !vm.MasterApproved,
                "Rejected Master must restart D2XX to watch removal, retaining measured results and keeping production locked");
            Assert(vm.ResultStatusText == "SẢN PHẨM KHÔNG ĐẠT\nTHÁO SẢN PHẨM" &&
                   vm.StateBackground == "#C62828" && vm.StateForeground == "#FFFFFF",
                "Rejected resistance or lost continuity explicitly asks for removal in the red product-failure status");
            board.Publish(Frame(301, false, 3));
            Assert(vm.MasterState == MasterSequenceState.TestingGoodMaster && vm.ResistanceDisplayRows.Count == 1,
                "Removing only one connection cannot reset the rejected sample");
            board.Publish(FrameSeq(302) with { Complete = false });
            board.Publish(FrameSeq(303) with { UnknownBytes = 1 });
            Assert(vm.MasterState == MasterSequenceState.TestingGoodMaster, "Invalid removal frames cannot re-arm Master");
            board.Publish(FrameSeq(304) with { ScanGeneration = 3 });
            board.Publish(FrameSeq(305) with { ScanGeneration = 3 });
            Assert(vm.MasterState == MasterSequenceState.WaitingGoodMaster && vm.Resistance.Count == 0 &&
                   vm.ResistanceDisplayRows.Count == 0 && vm.SelectedOperationTabIndex == 0 && !vm.MasterApproved,
                "Confirmed full removal clears the failed sample and returns to waiting for a good Master");
            Assert(vm.ResultStatusText == "KIỂM TRA MẪU ĐẠT",
                "Full removal replaces the rejection prompt with the request for another good sample");
            Assert(vm.Total == total && vm.Pass == pass && vm.Fail == fail &&
                   !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                $"Rejected Master retry cannot pulse relays or count production: Rpass={resistancePass}, " +
                $"{total}/{pass}/{fail}->{vm.Total}/{vm.Pass}/{vm.Fail}; commands={string.Join(',', board.Commands)}");
        }
    }

    private static void TestMasterResistanceRemovalTransition()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = CreateTestViewModel(new ProductionSettings
        {
            MasterFaultRequiredCount = 1,
            MasterSelectedFaultSamples = MasterSampleSelection.WrongWiring,
            ResistanceChannels = [new() { Enabled = true, Name = "R1", Channel = 1, MinOhm = 0.5, MaxOhm = 2 }]
        }, out FakeBoard board);
        var model = Model(("PAIR-A", new[] { 1, 2 }), ("PAIR-B", new[] { 3, 4 }));
        model.PartNumber = "MASTER-RESISTANCE-" + Guid.NewGuid().ToString("N");
        LoadReadyModel(vm, model);
        void SetField(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
        SetField("_runtimeMode", 1);
        SetField("_masterSequenceState", MasterSequenceState.EjectingGoodMaster);
        SetField("_masterGoodVerified", true);
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetFrameProcessingEnabled(true);
        var result = new ResistanceResult { Name = "R1", Channel = 1, ValueOhm = 1, Passed = true };
        vm.Resistance.Add(result);
        vm.ResistanceDisplayRows.Add(result);
        vm.SelectedOperationTabIndex = 1;
        vm.FinalWaterProofRows.Add(new WaterProofChannelResult
            { Channel = 1, Enabled = true, IsMeasured = true, Passed = true });
        int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
        Assert(vm.ResultStatusText == "HOÀN THÀNH KIỂM TRA MẪU ĐẠT" &&
               vm.StateBackground == "#2AA84A" && vm.StateForeground == "#FFFFFF",
            "Good Master resistance PASS cannot request the wrong-wire sample before full removal");
        board.Publish(FrameSeq(100, (1, new[] { 2 }), (3, new[] { 4 })));
        board.Publish(FrameSeq(101, (3, new[] { 4 })));
        Assert(vm.MasterState == MasterSequenceState.EjectingGoodMaster && vm.SelectedOperationTabIndex == 1 && vm.HasLeakResultRows,
            "Partial removal keeps the good sample and its measured results until all connections release");
        board.Publish(FrameSeq(102));
        Assert(vm.MasterState == MasterSequenceState.EjectingGoodMaster,
            "One empty scan cannot advance to the next sample");
        board.Publish(FrameSeq(103));
        Assert(vm.MasterState == MasterSequenceState.WaitingBadMaster && vm.ResultStatusText.Contains("SAI DÂY") &&
               vm.SelectedOperationTabIndex == 0 && vm.Resistance.Count == 0 && vm.ResistanceDisplayRows.Count == 0 &&
               !vm.HasLeakResultRows && vm.InspectionResultRows.Count == 0,
            "Confirmed full removal clears resistance results and opens the wrong-wire sample's electrical table");
        vm.SelectedOperationTabIndex = 1;
        Assert(vm.SelectedOperationTabIndex == 0,
            "A delayed resistance-table selection cannot cover the NG sample's electrical table");
        Assert(!vm.MasterApproved && vm.Total == total && vm.Pass == pass && vm.Fail == fail &&
               !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            $"Changing Master presentation cannot approve production, increment counters or pulse a relay: " +
            $"approved={vm.MasterApproved}, counters={total}/{pass}/{fail}->{vm.Total}/{vm.Pass}/{vm.Fail}, commands={string.Join(',', board.Commands)}");
    }

    private static void TestMasterLiveResistancePresentation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var settings = new ProductionSettings
        {
            MasterFaultRequiredCount = 1,
            ResistanceChannels =
            [
                new() { Enabled = true, Name = "R1", Channel = 1, MinOhm = 0.5, MaxOhm = 2 },
                new() { Enabled = true, Name = "R2", Channel = 2, MinOhm = 0.5, MaxOhm = 2 }
            ]
        };
        var app = new AppSettings();
        app.Test.ResistanceMinimumSettleMs = 0;
        app.Test.ResistanceSampleIntervalMs = 0;
        app.Test.ResistanceStableSampleCount = 2;
        app.Test.ResistanceStabilityTimeoutMs = 100;
        var board = new FakeBoard();
        var visa = new FakeKeysightVisaService(connected: true, measurement: 1);
        using var engine = new TestEngine(board, visa, app, settings);
        var vm = new TestViewModel(new MainViewModel(), engine, board, visa,
            new WaterProofSerialService(), app, settings,
            new LegacyPhtHistoryService(enabled: false), requireStartupIoClear: false);
        ProductModel model = Model(("PAIR", new[] { 1, 2 }));
        typeof(TestViewModel).GetField("_model", flags)!.SetValue(vm, model);
        engine.SetModel(model);
        using var cycle = new CancellationTokenSource();
        typeof(TestViewModel).GetField("_cycleCts", flags)!.SetValue(vm, cycle);
        Type runtimeModeType = typeof(TestViewModel).GetNestedType("RuntimeMode", BindingFlags.NonPublic)!;
        typeof(TestViewModel).GetField("_runtimeMode", flags)!.SetValue(vm,
            Convert.ToInt32(Enum.Parse(runtimeModeType, "Production")));
        typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(vm, MasterSequenceState.TestingGoodMaster);
        typeof(TestViewModel).GetField("_waterProofProfile", flags)!.SetValue(vm,
            new WaterProofModelSettings { Enabled = true, Channel1Enabled = true,
                Channel2Enabled = false, Channel3Enabled = false, Channel1Connector = "CN1", LeakLimit = 2 });
        typeof(TestViewModel).GetField("_masterApproved", flags)!.SetValue(vm, false);
        typeof(TestViewModel).GetField("_masterWaterProofPassedChannelsMask", flags)!.SetValue(vm, 1);
        typeof(TestViewModel).GetField("_lastWaterProofMeasurements", flags)!.SetValue(vm,
            new WaterProofChannelMeasurement[] { new(1, true, 80, 79, 1, true) });
        typeof(TestViewModel).GetField("_suppressEngineChanged", flags)!.SetValue(vm, 1);
        engine.SetFrameProcessingEnabled(true);
        engine.ProcessFrame(FrameSeq(100, (1, [2])), false);
        engine.ProcessFrame(FrameSeq(101, (1, [2])), false);
        typeof(TestViewModel).GetField("_suppressEngineChanged", flags)!.SetValue(vm, 0);
        long generation = (long)typeof(TestViewModel).GetField("_runtimeGeneration", flags)!.GetValue(vm)!;
        bool sawFirstMeasuring = false;
        bool sawFirstPass = false;
        bool sawCombined = false;
        visa.BeforeMeasure = () =>
        {
            Assert(vm.SelectedOperationTabIndex == 1 && vm.ResistanceDisplayRows.Count == 2,
                "Master selects the resistance table with all configured channels before reading");
            sawCombined = vm.HasLeakResultRows && vm.InspectionResultRows.Count == 3 &&
                   vm.InspectionResultRows.Last().ChannelText == "CH1" &&
                   vm.InspectionResultRows.Last().ResultText == "PASS" &&
                   vm.InspectionResultRows.Last().FirstPressureText == "80.0";
            if (board.ResistanceSteps.Count == 1)
            {
                sawFirstMeasuring = true;
                Assert(vm.ResistanceDisplayRows[0].ResultText == "ĐANG ĐO",
                    "Master row shows active measurement before its result is available");
            }
            else
            {
                sawFirstPass = true;
                Assert(vm.ResistanceDisplayRows[0].ResultText == "PASS" &&
                       vm.ResistanceDisplayRows[1].ResultText == "ĐANG ĐO",
                    "First Master channel becomes PASS while the next channel is still measuring");
                // End this UI-only fixture before History persistence or relay execution.
                cycle.Cancel();
                throw new OperationCanceledException(cycle.Token);
            }
        };
        ((Task)typeof(TestViewModel).GetMethod("CompleteGoodMasterAsync", flags)!
            .Invoke(vm, [generation])!).GetAwaiter().GetResult();
        Assert(sawFirstMeasuring && sawFirstPass && vm.SelectedOperationTabIndex == 1,
            $"Master keeps the measured results visible while the sample remains installed: first={sawFirstMeasuring}, pass={sawFirstPass}, tab={vm.SelectedOperationTabIndex}, state={vm.State}, master={vm.MasterStatus}");
        Assert(!vm.MasterApproved && !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Showing resistance progress cannot approve Master or trigger a relay");
        Assert(sawCombined,
            $"Good Master displays qualified Leak beside live resistance: rows={vm.InspectionResultRows.Count}, leak={vm.HasLeakResultRows}, continuity={engine.ContinuityPassed}, master={vm.MasterState}");
    }

    private static void TestMasterSelectedSamplesOnly()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (MasterSampleSelection selection in new[] { MasterSampleSelection.WrongWiring,
                     MasterSampleSelection.ShortCircuit, MasterSampleSelection.OpenCircuit,
                     MasterSampleSelection.WrongWiring | MasterSampleSelection.OpenCircuit })
        {
            var settings = new ProductionSettings { MasterFaultRequiredCount = 1, MasterSelectedFaultSamples = selection };
            TestViewModel vm = CreateTestViewModel(settings);
            LoadReadyModel(vm, Model(("PAIR", new[] { 1, 2 })));
            typeof(TestViewModel).GetField("_masterGoodVerified", flags)!.SetValue(vm, true);
            var validated = (HashSet<MasterSampleType>)typeof(TestViewModel).GetField("_validatedMasterFaultSamples", flags)!.GetValue(vm)!;
            MethodInfo transition = typeof(TestViewModel).GetMethod("TransitionToBadMaster", flags)!;
            foreach (MasterSampleType expected in MasterSampleCatalog.SelectedFaultSamples(selection))
            {
                transition.Invoke(vm, null);
                Assert((MasterSampleType)typeof(TestViewModel).GetField("_masterSampleType", flags)!.GetValue(vm)! == expected,
                    "Master transitions only to the next explicitly selected sample");
                Assert(vm.MasterSampleRequestText.Contains(MasterSampleCatalog.Name(expected)),
                    "Operator prompt follows the selected sample");
                typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(vm,
                    MasterSequenceState.EjectingBadMaster);
                Assert(vm.MasterSampleRequestText == $"HOÀN THÀNH KIỂM TRA MẪU {MasterSampleCatalog.Name(expected)}" &&
                       vm.ResultStatusText == vm.MasterSampleRequestText &&
                       vm.StateBackground == "#2AA84A" && vm.StateForeground == "#FFFFFF",
                    "Each validated fault sample shows its own completion text in PASS green until removal");
                typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(vm,
                    MasterSequenceState.WaitingBadMaster);
                Assert(vm.ResultStatusText == $"KIỂM TRA MẪU {MasterSampleCatalog.Name(expected)}" &&
                       vm.StateBackground == "#F5E731",
                    "Waiting for another installation shows the sample request without completion text");
                validated.Add(expected);
            }
        }
    }

    private static void TestMasterFirstConnectionPresentation()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (MasterSequenceState waiting in new[]
                 { MasterSequenceState.WaitingGoodMaster, MasterSequenceState.WaitingBadMaster })
        {
            TestViewModel vm = CreateTestViewModel(
                new ProductionSettings { MasterFaultRequiredCount = 1 }, out FakeBoard board);
            LoadReadyModel(vm, Model(("FIRST", new[] { 1, 2 }), ("SECOND", new[] { 3, 4 })));
            vm.StartProductionTestAsync().GetAwaiter().GetResult();
            typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(vm, waiting);
            var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
            Assert(vm.Faults.Count == 0, "Empty Master table has no installation rows");
            MethodInfo handle = typeof(TestViewModel).GetMethod("TryHandleContinuityPreviewFrame", flags)!;
            Assert((bool)handle.Invoke(vm, [ContinuityPreviewFrame(100, 1, new[] { 2 })])!,
                "Master consumes first SOURCE preview");
            Assert(engine.HasRealtimePresentationProductActivity && !engine.HasProductActivity,
                "First Master preview changes presentation without authoritative product presence");
            Assert(!vm.IsCenterResultVisible && vm.CenterResultText == string.Empty,
                "First product connection hides the installation overlay before presence debounce completes");
            typeof(TestViewModel).GetField("_presentationCycleStarted", flags)!.SetValue(vm, false);
            Assert(!vm.IsCenterResultVisible,
                "Realtime product evidence hides a stale waiting overlay even before its UI latch catches up");
            typeof(TestViewModel).GetField("_presentationCycleStarted", flags)!.SetValue(vm, true);
            Assert(vm.Faults.Any(row => row.WireName == "SECOND") &&
                   !vm.Faults.Any(row => row.WireName == "FIRST"),
                "First connection updates Master installation rows without a second connector or C0");
            Assert(!vm.MasterApproved && vm.MasterState == waiting && !engine.ContinuityPassed,
                "Preview cannot validate a sample or advance Master lifecycle");
            handle.Invoke(vm, [ContinuityPreviewFrame(100, 1, Array.Empty<int>())]);
            Assert(!engine.HasRealtimePresentationProductActivity && vm.Faults.Count == 0,
                "Removing the first preview connection clears realtime activity");
        }
    }

    private static void TestMasterWaitingReturnToMain()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ProductModel model = Model(("PAIR", new[] { 1, 2 }));
        foreach (MasterSequenceState waiting in new[]
                 { MasterSequenceState.WaitingGoodMaster, MasterSequenceState.WaitingBadMaster })
        {
            TestViewModel vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 1 });
            LoadReadyModel(vm, model);
            typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(vm, waiting);
            // A previous production cycle must not masquerade as a new Master sample.
            typeof(TestViewModel).GetField("_productDetectedThisCycle", flags)!.SetValue(vm, true);
            Assert(!vm.HasProductOnTestTable && vm.StopViewAsync().GetAwaiter().GetResult(),
                "Waiting for an uninstalled Master sample allows Back despite stale cycle ownership");
            Assert(!vm.MasterApproved, "Leaving the empty Master screen does not approve samples");
        }

        TestViewModel active = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 1 });
        LoadReadyModel(active, model);
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(active)!;
        engine.SetFrameProcessingEnabled(true);
        engine.ProcessFrame(FrameSeq(100, (1, new[] { 2 })), false);
        engine.ProcessFrame(FrameSeq(101, (1, new[] { 2 })), false);
        typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(active,
            MasterSequenceState.TestingGoodMaster);
        Assert(active.HasProductOnTestTable && !active.StopViewAsync().GetAwaiter().GetResult(),
            "Physically connected Master blocks Back");
        engine.ProcessFrame(FrameSeq(102), false);
        Assert(active.HasProductOnTestTable,
            "One empty frame does not discard confirmed Master presence");
        engine.ProcessFrame(FrameSeq(103), false);
        Assert(!active.HasProductOnTestTable && active.StopViewAsync().GetAwaiter().GetResult(),
            "Confirmed removal allows Back even before the coalesced Master UI transition");
        Assert(!active.MasterApproved, "Removal/Back cannot complete the Master gate");

        foreach (MasterSequenceState ejecting in new[]
                 { MasterSequenceState.EjectingGoodMaster, MasterSequenceState.EjectingBadMaster })
        {
            typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(active, ejecting);
            Assert(active.HasProductOnTestTable && !active.StopViewAsync().GetAwaiter().GetResult(),
                "Unconfirmed physical removal remains interlocked after engine reset");
            typeof(TestViewModel).GetField("_masterRemovalConfirmed", flags)!.SetValue(active, 1);
            Assert(!active.HasProductOnTestTable && active.StopViewAsync().GetAwaiter().GetResult(),
                "Confirmed empty table permits Back during the deferred ejection UI state");
            typeof(TestViewModel).GetField("_masterRemovalConfirmed", flags)!.SetValue(active, 0);
        }
        typeof(TestViewModel).GetField("_masterSequenceState", flags)!.SetValue(active,
            MasterSequenceState.WaitingGoodMaster);
        typeof(TestViewModel).GetField("_masterEjectInProgress", flags)!.SetValue(active, 1);
        Assert(!active.HasProductOnTestTable && active.StopViewAsync().GetAwaiter().GetResult(),
            "Master relay work alone cannot report a product on an empty table");
        typeof(TestViewModel).GetField("_masterEjectInProgress", flags)!.SetValue(active, 0);
        typeof(TestViewModel).GetField("_masterWaterProofSequenceActive", flags)!.SetValue(active, 1);
        Assert(!active.HasProductOnTestTable && active.StopViewAsync().GetAwaiter().GetResult(),
            "Master Leak work alone cannot report a product on an empty table");
        typeof(TestViewModel).GetField("_masterWaterProofSequenceActive", flags)!.SetValue(active, 0);
        typeof(TestViewModel).GetField("_productRemovalPending", flags)!.SetValue(active, 1);
        Assert(active.HasProductOnTestTable, "An explicit ProductRemoved interlock is preserved");
    }

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
                   reload.MasterFaultCountsByModel.Count == 0 &&
                   !File.ReadAllText(configPath).Contains("[MasterFault.", StringComparison.Ordinal),
                "Shared settings survive CFG reload without writing unused per-model Master archives");

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
            vm.State = "ĐANG KIỂM TRA...";
            Assert(vm.ResultStatusText == "LẮP SẢN PHẨM" && vm.CenterResultText == "LẮP SẢN PHẨM" &&
                   vm.StateBackground == "#83CCEB" && vm.StateForeground == "#222222",
                "A delayed generic testing message cannot replace the confirmed idle state after Master completion");
            SetField("_presentationCycleStarted", true);
            Assert(vm.ResultStatusText == "ĐANG KIỂM TRA" && vm.StateBackground == "#83CCEB" && vm.StateForeground == "#000000",
                "A new product installation after Master still shows active testing");
            SetField("_presentationCycleStarted", false);
            vm.State = "LẮP SẢN PHẨM";
        }
        finally
        {
            // Temporary fixture data only; production databases are never touched.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
