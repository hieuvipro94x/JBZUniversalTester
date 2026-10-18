using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestMainRemovalContacts()
    {
        var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 }, out FakeBoard board);
        // Startup without a remembered model must still show actual electrical pairs.
        int total = vm.Total, pass = vm.Pass, fail = vm.Fail, commands = board.Commands.Count;
        board.Publish(FrameSeq(10, (18, new[] { 1 }), (1, new[] { 18 }), (5, new[] { 9 })));
        Assert(vm.IsProductRemovalPending && vm.MainRemovalContacts.SequenceEqual(new[]
            { new StartupIoContactPair(1, 18), new StartupIoContactPair(5, 9) }),
            "MainWindow lists sorted unique connected pairs without requiring a selected model");
        Assert(vm.MainRemovalRows.Select(row => row.Io).SequenceEqual(new[] { 1, 18, 5, 9 }) &&
               vm.MainRemovalRows.Select(row => row.IsNetworkStart).SequenceEqual(new[] { false, false, true, false }) &&
               vm.MainRemovalRows.All(row => row.IoText == $"IO{row.Io}"),
            "Each pair is a separate group of individual IO rows with a separator only between pairs");
        AssertThrows<InvalidOperationException>(() => vm.SetModel(Model(("OTHER", new[] { 2, 19 }))),
            "MainWindow contact list cannot bypass the model selection interlock");
        board.Publish(FrameSeq(11) with { Complete = false });
        board.Publish(FrameSeq(12) with { UnknownBytes = 1 });
        Assert(vm.IsProductRemovalPending && vm.MainRemovalContacts.Count == 2,
            "Incomplete or corrupt scans cannot clear the remaining IO list or unlock selection");
        board.Publish(FrameSeq(13, (5, new[] { 9 })));
        Assert(vm.IsProductRemovalPending && vm.MainRemovalContacts.SequenceEqual(new[]
            { new StartupIoContactPair(5, 9) }),
            "Partial removal updates the list while keeping selection locked");
        board.Publish(FrameSeq(14));
        Assert(!vm.IsProductRemovalPending && vm.MainRemovalContacts.Count == 0,
            "A complete clean scan clears the MainWindow list and unlocks selection");
        Assert(vm.Total == total && vm.Pass == pass && vm.Fail == fail && board.Commands.Count == commands,
            "MainWindow removal monitoring never records results or commands relays");

        var lastModel = Model(("LAST-TESTED", new[] { 1, 18 }));
        lastModel.PartNumber = "LAST-TESTED-PART";
        lastModel.Pins.AddRange([
            new PinRecord("CN-A", "WIRE-05", 5, "2"),
            new PinRecord("CN-B", "WIRE-09", 9, "7"),
            new PinRecord("CN-C", "WIRE-DUPLICATE", 5, "3")]);
        vm.SetModel(lastModel);
        board.Publish(FrameSeq(15, (5, new[] { 9 })));
        Assert(vm.MainRemovalRows.Select(row => (row.Io, row.Connector, row.Pin, row.WireNameDisplay, row.IoCnPnText))
            .SequenceEqual(new[]
            {
                (5, "CN-A", "2", "WIRE-05", "IO5-CN-A-2"),
                (5, "CN-C", "3", "WIRE-DUPLICATE", "IO5-CN-C-3"),
                (9, "CN-B", "7", "WIRE-09", "IO9-CN-B-7")
            }),
            "Each endpoint and duplicate mapping keeps its own connector, pin and wire on its own row");
        board.Publish(FrameSeq(16, (10, new[] { 11 })));
        Assert(vm.MainRemovalRows.Count == 2 && vm.MainRemovalRows.All(row => row.WireNameDisplay == "-") &&
               vm.MainRemovalRows.Select(row => row.IoCnPnText).SequenceEqual(new[] { "IO10", "IO11" }),
            "Unknown IO remains visible without borrowing wire information from another connection");
        board.Publish(FrameSeq(17));
        Assert(vm.MainRemovalRows.Count == 0 && !vm.IsProductRemovalPending,
            "Complete removal clears enriched wire rows with the selection interlock");

        var common = new PinRecord("CLIP", "CLIP-COMMON", 40, "AO", PinType: "AO");
        int[] branchIos = [12, 24, 37, 51, 62, 71, 85, 96];
        ClipBranch[] branches = branchIos.Select((io, index) => new ClipBranch(
            $"a{index + 1}", index + 1, io,
            new PinRecord("CLIP", "", io, $"a{index + 1}", PinType: $"a{index + 1}"), null)).ToArray();
        var clipModel = new ProductModel { ModelName = "CLIP-REMOVAL",
            Pins = [common, .. branches.Take(7).Select(branch => branch.ClipPin)],
            Clip = new ClipTopology(common, branches) };
        vm.SetModel(clipModel);
        int clipTotal = vm.Total, clipPass = vm.Pass, clipFail = vm.Fail, clipCommands = board.Commands.Count;
        ScanFrame clipFrame = FrameSeq(18, branchIos.Select(io => (io, new[] { 40 })).ToArray());
        Assert(StartupIoInterlock.FindConnectedPairs(clipFrame, clipModel, BoardCapacity.Create(2)).Count == 8,
            "Expected multi-branch CLIP fan-in must remain product-removal evidence rather than being discarded as Probe");
        board.Publish(clipFrame);
        Assert(vm.IsProductRemovalPending && vm.MainRemovalContacts.Count == 8 && vm.MainRemovalRows.Count == 16 &&
               vm.MainRemovalRows.Where(row => row.Io == 40).All(row => row.WireNameDisplay == "AO") &&
               branches.All(branch => vm.MainRemovalRows.Any(row => row.Io == branch.TargetIo && row.WireNameDisplay == branch.Name)) &&
               vm.MainRemovalRows.Select(row => row.IsNetworkStart).Count(start => start) == 7,
            "MainWindow retains all CLIP pairs, actual configured IO, AO/aN names and pair separators, including topology-only pins");
        AssertThrows<InvalidOperationException>(() => vm.SetModel(lastModel),
            "Remaining CLIP locks selecting another product just like ordinary wiring");
        board.Publish(FrameSeq(19, branchIos.Select(io => (io, new[] { 40 }))
            .Concat(Enumerable.Range(100, 12).Select(io => (io, new[] { 127 }))).ToArray()));
        Assert(vm.IsProductRemovalPending && vm.MainRemovalContacts.Count == 8,
            "A simultaneous real Probe contact is excluded without hiding the remaining CLIP branches");
        board.Publish(FrameSeq(20) with { Complete = false });
        board.Publish(FrameSeq(21) with { UnknownBytes = 1 });
        Assert(vm.MainRemovalContacts.Count == 8 && vm.IsProductRemovalPending,
            "Incomplete/corrupt CLIP scans cannot clear removal evidence");
        board.Publish(FrameSeq(22, (96, new[] { 40 })));
        Assert(vm.IsProductRemovalPending && vm.MainRemovalContacts.Count == 1 &&
               vm.MainRemovalRows.Any(row => row.Io == 96 && row.WireNameDisplay == "a8"),
            "The final CLIP branch stays visible with its topology name until physically removed");
        board.Publish(FrameSeq(23));
        board.Publish(FrameSeq(24, Enumerable.Range(100, 12).Select(io => (io, new[] { 127 })).ToArray()));
        Assert(!vm.IsProductRemovalPending && vm.MainRemovalRows.Count == 0 &&
               vm.Total == clipTotal && vm.Pass == clipPass && vm.Fail == clipFail && board.Commands.Count == clipCommands,
            "Full CLIP removal unlocks MainWindow; Probe-only input neither locks product selection nor records results or fires relays");
    }

    private static void TestOrphanedProbeCandidateRestoresCenter()
    {
        foreach (bool useDiscard in new[] { true, false })
        {
            var production = new ProductionSettings { MasterFaultRequiredCount=0, ExpansionCardCount=2,
                ProductSettleTimeMs=10_000, IoConfirm1=1, IoConfirmN=1 };
            var vm = CreateTestViewModel(production, out FakeBoard board);
            var model = Model(("PAIR",new[] {1,2}));
            if(useDiscard) model.DiscardContactIo=[97,98];
            vm.SetModel(model);
            vm.StartProductionTestAsync().GetAwaiter().GetResult();
            int beforeTotal=vm.Total, beforeFail=vm.Fail, beforePass=vm.Pass, beforeCommands=board.Commands.Count;
            board.PublishProbePreview(new ProductionProbePreview(DateTime.Now,[97],12,12,Sequence:10,ScanGeneration:1));
            Assert(vm.CurrentProbePresentationState==ProbePresentationState.Candidate && !vm.IsCenterResultVisible,
                "Early preview hides waiting center until its complete frame is reconciled");
            if(useDiscard)
            {
                board.Publish(FrameSeq(10,(97,new[] {98})) with {ScanGeneration=1});
                Assert(!vm.IsCenterResultVisible,"Discard contact interlock does not prematurely show installation");
            }
            board.Publish(FrameSeq(11) with {ScanGeneration=1});
            board.Publish(FrameSeq(12) with {ScanGeneration=1});
            Assert(vm.CurrentProbePresentationState is ProbePresentationState.Released or ProbePresentationState.Inactive &&
                   vm.IsCenterResultVisible && vm.CenterResultText=="LẮP SẢN PHẨM",
                $"Candidate-only preview releases center after {(useDiscard ? "discard close/release" : "a newer complete empty frame")}; " +
                $"probe={vm.CurrentProbePresentationState}, center={vm.IsCenterResultVisible}, text={vm.CenterResultText}");
            Assert(vm.Total==beforeTotal && vm.Fail==beforeFail && vm.Pass==beforePass && board.Commands.Count==beforeCommands,
                "Preview cleanup never creates production results or hardware relay commands");
        }
    }

    private static void TestRemovalStatusNotifications()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0 });
        var model = Model(("PAIR", new[] { 1, 2 }));
        void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
        Set("_model", model);
        Set("_masterApproved", true);
        Set("_cycleActive", true);
        var phaseType = typeof(TestViewModel).GetNestedType("ProductionPhase", BindingFlags.NonPublic)!;
        Set("_productionPhase", Convert.ToInt32(Enum.Parse(phaseType, "Continuity")));
        var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
        engine.SetModel(model);
        engine.ProcessFrame(FrameSeq(1));
        Set("_presentationCycleStarted", true);
        string rendered = "";
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TestViewModel.ResultStatusText)) rendered = vm.ResultStatusText;
        };
        vm.State = "ĐANG KIỂM TRA...";
        vm.State = "LẮP SẢN PHẨM";
        Assert(rendered == "ĐANG KIỂM TRA", "Presentation can still show testing while the old activity latch is present");
        Set("_presentationCycleStarted", false);
        Assert(vm.ResultStatusText == "LẮP SẢN PHẨM" && rendered == "ĐANG KIỂM TRA",
            "Changing a dependent activity flag alone leaves the previously published text stale");
        typeof(TestViewModel).GetMethod("ApplyAuthoritativeProductionState", flags)!
            .Invoke(vm, [engine.GetProductionElectricalSnapshot()]);
        Assert(vm.State == "LẮP SẢN PHẨM" && rendered == "LẮP SẢN PHẨM" && vm.StateBackground == "#F5E731",
            "An authoritative waiting snapshot refreshes the bound label/colors even when raw State does not change");
    }
}
