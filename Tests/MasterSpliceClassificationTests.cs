using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestMasterSpliceClassification()
    {
        var model = Model(("SPLICE", new[] { 1, 2, 3, 4 }), ("B", new[] { 5, 6 }),
            ("C", new[] { 7, 8 }), ("D", new[] { 9, 10 }));
        using TestEngine engine = CreateEngine(out _);
        long sequence = 100;
        WiringFaultPair[] Fit(params int[][] connectedGroups)
        {
            engine.SetModel(model);
            var pairs = connectedGroups.Select(group => (group[0], group.Skip(1).ToArray())).ToArray();
            engine.ProcessFrame(FrameSeq(sequence++, pairs), false);
            Thread.Sleep(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs + 20);
            engine.ProcessFrame(FrameSeq(sequence++, pairs), false);
            var productionBefore = engine.WiringFaults.ToArray();
            var master = engine.GetMasterWiringFaults().ToArray();
            Assert(engine.WiringFaults.SequenceEqual(productionBefore),
                "Master topology classification cannot mutate production fault classification");
            return master;
        }
        var replacement = Fit([1, 2, 3, 5, 6]);
        Assert(replacement.Length > 0 && replacement.All(fault => fault.FaultType == ProductFaultType.WrongWiring) &&
               replacement.Any(fault => fault.ExpectedSourceIo == 1 && fault.ExpectedTargetIo == 4),
            "Replacing splice endpoint 4 with endpoint 5 is wrong wiring despite a five-IO actual group");
        var bridge = Fit([1, 2, 3, 4, 5, 6]);
        Assert(bridge.Length > 0 && bridge.All(fault => fault.FaultType == ProductFaultType.ShortCircuit),
            "An extra bridge on an intact splice remains short-circuit evidence");
        var swapped = Fit([1, 2, 3, 6], [5, 4]);
        Assert(swapped.Length > 0 && swapped.All(fault => fault.FaultType == ProductFaultType.WrongWiring),
            "Swapping a splice branch and a separate wire supports the wrong-wire sample");
        var mixed = Fit([1, 2, 3, 5, 6], [7, 8, 9, 10]);
        Assert(mixed.Any(fault => fault.FaultType == ProductFaultType.WrongWiring) &&
               mixed.Any(fault => fault.FaultType == ProductFaultType.ShortCircuit),
            "One splice replacement cannot reclassify a separate short group");
        Assert(Fit([1, 2, 3, 4], [5, 6], [7, 8], [9, 10]).Length == 0,
            "Correct factory splices never become Master fault evidence");
        model = Model(("G1", new[] { 1, 2, 3, 4 }), ("G2", new[] { 5, 6, 7, 8 }));
        Assert(Fit([1, 2, 3, 8], [5, 6, 7, 4]).All(fault => fault.FaultType == ProductFaultType.WrongWiring),
            "Swapping branches between two splices is not a short merely because both groups have four endpoints");
        model = Model(("B", new[] { 5, 6 }));
        var ao = new PinRecord("CLIP", "AO", 201, "AO");
        var branches = Enumerable.Range(202, 3).Select((io, i) => new ClipBranch("a" + (i + 1), i + 1, io,
            new PinRecord("CLIP", "a" + (i + 1), io, "a" + (i + 1)), null)).ToArray();
        model.Clip = new ClipTopology(ao, branches);
        model.Pins.Add(ao); model.Pins.AddRange(branches.Select(branch => branch.ClipPin));
        Assert(Fit([201, 202, 203, 5, 6]).All(fault => fault.FaultType == ProductFaultType.WrongWiring),
            "A missing configured CLIP branch replaced by a foreign wire supports wrong-wire Master");
        Assert(Fit([201, 202, 203, 204, 5, 6]).All(fault => fault.FaultType == ProductFaultType.ShortCircuit),
            "An intact AO/aN group bridged to another network supports short Master");
        TestMasterSpliceSampleGate();
    }

    private static void TestMasterSpliceSampleGate()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (var requested in new[] { MasterSampleType.WrongWiring, MasterSampleType.ShortCircuit })
            {
                string directory = Path.Combine(Path.GetTempPath(), "JBZ-SpliceMasterGate-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var settings = new ProductionSettings { MasterFaultRequiredCount = 1,
                    MasterSelectedFaultSamples = MasterSampleCatalog.SelectionFlag(requested) };
                var store = new TestHistoryStore(Path.Combine(directory, "history.db"));
                await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
                await persistence.Initialization;
                var vm = CreateTestViewModel(settings, out FakeBoard board);
                void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                object? Get(string name) => typeof(TestViewModel).GetField(name, flags)!.GetValue(vm);
                object? Call(string name, params object[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
                Set("_historyStore", store); Set("_productionPersistence", persistence);
                Set("_lotSequence", new LotSequenceService(settings, saved =>
                    ProductionConfigService.SaveLegacyCfg(saved, Path.Combine(directory, "settings.cfg"))));
                Set("_partCounterStore", new Lazy<PartCounterStore>(() => new PartCounterStore(Path.Combine(directory, "PartCnt.txt"))));
                var model = Model(("SPLICE", new[] { 1, 2, 3, 4 }), ("B", new[] { 5, 6 }));
                model.PartNumber = "SPLICE-MASTER-GATE";
                vm.SetModel(model); await (Task)Get("_statisticsLoadTask")!;
                Set("_masterGoodVerified", true); Call("TransitionToBadMaster");
                var engine = (TestEngine)Get("_engine")!;
                engine.SetFrameProcessingEnabled(true);
                long sequence = 100, generation = (long)Get("_runtimeGeneration")!;
                var wrong = new[] { (1, new[] { 2, 3, 5, 6 }) };
                var shorts = new[] { (1, new[] { 2, 3, 4, 5, 6 }) };
                void Feed((int Source, int[] Targets)[] pairs)
                {
                    Set("_runtimeMode", 0);
                    var frame = FrameSeq(sequence++, pairs); board.Publish(frame); engine.ProcessFrame(frame, false);
                    Set("_runtimeMode", 1); Call("ProcessMasterEngineChangedOnUi", generation);
                }
                var rejected = requested == MasterSampleType.WrongWiring ? shorts : wrong;
                Feed(rejected); await Task.Delay(150); Set("_masterBadCollectNotBeforeUtcTicks", 0L); Feed(rejected);
                Assert(!vm.IsMasterSampleAccepted && vm.MasterDetectedFaultCount == 0 &&
                       !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                    $"{requested} cannot accept the other fault category or eject its sample");
                var accepted = requested == MasterSampleType.WrongWiring ? wrong : shorts;
                board.StartScanCallback = transport => transport.Publish(FrameSeq(sequence++, accepted));
                Feed(accepted); await Task.Delay(150); Set("_masterBadCollectNotBeforeUtcTicks", 0L); Feed(accepted);
                for (int i = 0; i < 200 && vm.MasterState != MasterSequenceState.EjectingBadMaster; i++) await Task.Delay(10);
                Assert(vm.MasterState == MasterSequenceState.EjectingBadMaster && vm.IsMasterSampleAccepted &&
                       vm.Total == 0 && vm.Pass == 0 && vm.Fail == 0 && !board.Commands.Contains("SET:2"),
                    $"{requested} accepts its own splice evidence without production FAIL/counts or marking relay");
                for (int i = 0; i < 200 && (int)Get("_masterEjectInProgress")! != 0; i++) await Task.Delay(10);
                await (Task)Get("_masterPersistenceTask")!;
                Assert(!vm.IsPersistenceFault && !vm.IsDeviceFault, "Splice Master confirmation completes without hardware/persistence faults");
                SqliteConnection.ClearAllPools();
            }
        });
    }

    private static void TestMasterSpliceOnItemFiles(string directory)
    {
        int tested = 0;
        foreach (string path in Directory.EnumerateFiles(directory, "*.tht", SearchOption.AllDirectories))
        {
            ProductModel model = new ThtModelParser().Load(path);
            var groups = model.Nets.Select(net => net.IoNumbers.Where(io => io > 0).ToHashSet()).Where(group => group.Count > 0).ToList();
            if (model.Clip is { } clip)
                groups.Add(clip.Branches.Select(branch => branch.TargetIo).Append(clip.CommonIo).Where(io => io > 0).ToHashSet());
            // Build connected expected groups independently from the parsed THT.
            for (int i = 0; i < groups.Count; i++)
                for (int j = i + 1; j < groups.Count; j++)
                    if (groups[i].Overlaps(groups[j]))
                    {
                        groups[i].UnionWith(groups[j]); groups.RemoveAt(j);
                        j = i; // New union can overlap an earlier candidate.
                    }
            int spliceIndex = groups.FindIndex(group => group.Count >= 3);
            int otherIndex = groups.FindIndex(group => group.Count >= 2 && groups.IndexOf(group) != spliceIndex);
            if (spliceIndex < 0 || otherIndex < 0)
            {
                Console.WriteLine($"ITEM_MASTER_SPLICE skipped_no_splice_pair file={Path.GetFileName(path)}");
                continue;
            }
            using TestEngine engine = CreateEngine(out _);
            long sequence = 100;
            WiringFaultPair[] Scan(List<HashSet<int>> physical)
            {
                engine.SetModel(model);
                var pairs = physical.SelectMany(group => group.Select(source =>
                    (source, group.Where(io => io != source).ToArray()))).ToArray();
                engine.ProcessFrame(FrameSeq(sequence++, pairs), false);
                Thread.Sleep(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs + 20);
                engine.ProcessFrame(FrameSeq(sequence++, pairs), false);
                var before = engine.WiringFaults.ToArray();
                var result = engine.GetMasterWiringFaults().ToArray();
                Assert(engine.WiringFaults.SequenceEqual(before), "Real THT Master reads cannot alter production classification");
                return result;
            }
            Assert(Scan(groups).Length == 0, $"Real THT correct splices cannot produce faults: {path}");
            int removed = groups[spliceIndex].Last(), foreign = groups[otherIndex].Last();
            List<HashSet<int>> swapped = groups.Select(group => new HashSet<int>(group)).ToList();
            swapped[spliceIndex].Remove(removed); swapped[spliceIndex].Add(foreign);
            swapped[otherIndex].Remove(foreign); swapped[otherIndex].Add(removed);
            var wrong = Scan(swapped);
            Assert(wrong.Length > 0 && wrong.All(fault => fault.FaultType == ProductFaultType.WrongWiring),
                $"Real THT branch swap must support wrong-wire Master: {path}, swapped={removed}<->{foreign}");
            List<HashSet<int>> bridged = groups.Select(group => new HashSet<int>(group)).ToList();
            bridged[spliceIndex].UnionWith(bridged[otherIndex]); bridged.RemoveAt(otherIndex);
            var shorts = Scan(bridged);
            Assert(shorts.Length > 0 && shorts.All(fault => fault.FaultType == ProductFaultType.ShortCircuit),
                $"Real THT intact groups with an added bridge must support short Master: {path}");
            Console.WriteLine($"ITEM_MASTER_SPLICE PASS file={Path.GetFileName(path)} splice=[{string.Join(',', groups[spliceIndex])}] " +
                $"swap={removed}<->{foreign} wrong={wrong.Length} bridge_short={shorts.Length} production_unchanged=true");
            tested++;
        }
        Assert(tested > 0, "At least one actual item with a splice must be verified");
        Console.WriteLine($"ITEM_MASTER_SPLICE_SUMMARY files_tested={tested} hardware=simulated_frames actual_tht=true");
    }
}
