using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestMasterShortGroupAndConfiguredOpen()
    {
        using (TestEngine engine = CreateEngine(out _))
        {
            var model = Model(("A", new[] { 1, 18 }), ("B", new[] { 2, 19 }), ("C", new[] { 3, 20 }));
            long sequence = 100;
            void Fit(params (int Source, int[] Targets)[] pairs)
            {
                engine.SetModel(model);
                engine.ProcessFrame(FrameSeq(sequence++, pairs), false);
                Thread.Sleep(Math.Max(ProductionTimingPolicy.DefaultWrongConnectionConfirmMs,
                    ProductionTimingPolicy.DefaultShortCircuitConfirmMs) + 20);
                engine.ProcessFrame(FrameSeq(sequence++, pairs), false);
            }
            Fit((1, [2]));
            Assert(engine.GetMasterWiringFaults().Count > 0 &&
                   engine.GetMasterWiringFaults().All(fault => fault.FaultType == ProductFaultType.WrongWiring),
                "Two incorrectly connected IOs are wrong-wire Master evidence, not short evidence");
            Fit((1, [2, 3]));
            Assert(engine.GetMasterWiringFaults().Count > 0 &&
                   engine.GetMasterWiringFaults().All(fault => fault.FaultType == ProductFaultType.ShortCircuit),
                "Three connected IOs qualify as short evidence even when original pairs are missing");
            Fit((1, [2]), (2, [3]));
            Assert(engine.GetMasterWiringFaults().Count > 0 &&
                   engine.GetMasterWiringFaults().All(fault => fault.FaultType == ProductFaultType.ShortCircuit),
                "Short groups use transitive physical connectivity, not the number of targets in one source row");
            Fit((1, [2]), (3, [19]));
            Assert(engine.GetMasterWiringFaults().Count > 0 &&
                   engine.GetMasterWiringFaults().All(fault => fault.FaultType == ProductFaultType.WrongWiring),
                "Separate two-IO faults never combine into a three-IO short group");
            Fit((1, [60, 61]));
            Assert(engine.GetMasterWiringFaults().Any(fault => fault.FaultType == ProductFaultType.ShortCircuit),
                "Unexpected IOs outside the item topology still count in a physical short group");
            model = Model(("COMMON", new[] { 1, 2, 3, 4 }));
            Fit((1, [2, 3, 4]));
            Assert(engine.GetMasterWiringFaults().Count == 0,
                "A valid common net in the item topology cannot confirm a short Master");
        }

        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-MasterConfirm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (int missingCount in new[] { 1, 2, 3 })
                {
                    var settings = new ProductionSettings { MasterFaultRequiredCount = 1,
                        MasterOpenFaultRequiredCount = missingCount, MasterSelectedFaultSamples = MasterSampleSelection.OpenCircuit,
                        IoConfirm1 = 1, IoConfirmN = 1 };
                    var vm = CreateTestViewModel(settings, out FakeBoard board);
                    var model = Model(Enumerable.Range(1, 20).Select(source =>
                        ("W" + source, new[] { source, source + 20 })).ToArray());
                    model.PartNumber = "OPEN-AUTO-" + missingCount;
                    vm.SetModel(model);
                    await (Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!;
                    void Set(string name, object value) => typeof(TestViewModel).GetField(name, flags)!.SetValue(vm, value);
                    object? Get(string name) => typeof(TestViewModel).GetField(name, flags)!.GetValue(vm);
                    object? Call(string name, params object?[] args) => typeof(TestViewModel).GetMethod(name, flags)!.Invoke(vm, args);
                    var store = new TestHistoryStore(Path.Combine(root, $"{missingCount}.db"));
                    await using var persistence = new ProductionPersistenceService(store, settings, "SELFTEST");
                    await persistence.Initialization;
                    Set("_historyStore", store);
                    Set("_productionPersistence", persistence);
                    Set("_masterGoodVerified", true);
                    Call("TransitionToBadMaster");
                    var engine = (TestEngine)Get("_engine")!;
                    engine.SetFrameProcessingEnabled(true);
                    long generation = (long)Get("_runtimeGeneration")!;
                    long sequence = 100;
                    (int Source, int[] Targets)[] Pairs(int missing, bool allSources = true) =>
                        Enumerable.Range(1, allSources ? 20 : 19).Select(source =>
                            (source, source <= 20 - missing ? new[] { source + 20 } : Array.Empty<int>())).ToArray();
                    void Feed(params (int Source, int[] Targets)[] pairs)
                    {
                        Set("_runtimeMode", 0);
                        var frame = FrameSeq(sequence++, pairs);
                        board.Publish(frame);
                        engine.ProcessFrame(frame, false);
                        Set("_runtimeMode", 1);
                        Call("ProcessMasterEngineChangedOnUi", generation);
                    }
                    int total = vm.Total, pass = vm.Pass, fail = vm.Fail;
                    string lot = vm.Lot;
                    Feed(Pairs(20));
                    Assert(vm.MasterState == MasterSequenceState.WaitingBadMaster && vm.MasterOpenProgressText == "20/0",
                        "An absent sample remains locked and cannot display a false fully connected count");
                    Feed(Pairs(19));
                    await Task.Delay(150);
                    Set("_masterBadCollectNotBeforeUtcTicks", 0L);
                    Feed(Pairs(19));
                    Assert(vm.MasterState == MasterSequenceState.TestingBadMaster && !vm.MasterApproved &&
                           vm.MasterDetectedFaultCount == 19 && vm.MasterRequiredFaultCount == missingCount &&
                           vm.MasterOpenProgressText == "20/1" &&
                           !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                        "Partially fitted samples cannot auto-pass with more missing connections than configured");
                    Feed(Pairs(missingCount, allSources: false));
                    Assert(vm.MasterState == MasterSequenceState.TestingBadMaster && !vm.IsOpenMasterSampleAccepted &&
                           engine.GetMasterConnectedConnectionCount() == 20 - missingCount &&
                           vm.MasterOpenProgressText == $"20/{20 - missingCount}",
                        "The exact missing count still requires coverage of every THT source");
                    var wrong = Pairs(missingCount);
                    wrong[0] = (1, [2]);
                    Feed(wrong);
                    Assert(!vm.IsOpenMasterSampleAccepted, "An unexpected wire candidate blocks automatic open-Master acceptance");
                    Feed(Pairs(Math.Max(0, missingCount - 1)));
                    Assert(vm.MasterState == MasterSequenceState.TestingBadMaster && !vm.IsOpenMasterSampleAccepted,
                        "Fewer missing connections, including a good sample, cannot satisfy the configured open count");
                    Feed(Pairs(0));
                    Assert(!vm.IsOpenMasterSampleAccepted, "A fully connected good sample cannot pass as an open sample");
                    string cycleId = (string)Get("_masterHistoryCycleId")!;
                    board.StartScanCallback = transport => transport.Publish(FrameSeq(sequence++, (1, [21])));
                    Feed(Pairs(missingCount));
                    for (int attempt = 0; attempt < 100 && vm.MasterState != MasterSequenceState.EjectingBadMaster; attempt++)
                        await Task.Delay(10);
                    Assert(vm.MasterState == MasterSequenceState.EjectingBadMaster &&
                           vm.MasterRequiredFaultCount == missingCount && vm.MasterDetectedFaultCount == missingCount &&
                           vm.MasterOpenProgressText == $"20/{20 - missingCount} • OK" &&
                           vm.ResultStatusText == "HOÀN THÀNH KIỂM TRA MẪU TUỘT TUÝT / ĐỨT DÂY" &&
                           vm.StateBackground == "#2AA84A" && vm.IsOpenMasterSampleAccepted &&
                           !vm.MasterApproved && !vm.IsPersistenceFault &&
                           board.Commands.Count(command => command == "SET:1") == 1 && !board.Commands.Contains("SET:2"),
                        $"Exact configured open count auto-passes and ejects only JIG, preserving removal interlock: required={missingCount}, status={vm.MasterOpenProgressText}");
                    await (Task)Get("_masterPersistenceTask")!;
                    Call("CollectCurrentMasterFaults", generation);
                    await (Task)Get("_masterPersistenceTask")!;
                    using var connection = new SqliteConnection($"Data Source={Path.Combine(root, $"{missingCount}.db")};Pooling=False");
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM Tests WHERE CycleId=$Cycle AND Passed=1 AND InspectionTrace LIKE $Trace;";
                    command.Parameters.AddWithValue("$Cycle", cycleId);
                    command.Parameters.AddWithValue("$Trace", $"%KẾT NỐI 20/{20 - missingCount}%");
                    Assert(Convert.ToInt64(command.ExecuteScalar()) == 1,
                        "Automatic open acceptance records the configured count and total/connected progress exactly once");
                    command.CommandText = "SELECT COUNT(*) FROM TestFaults f JOIN Tests t ON t.Id=f.TestId WHERE t.CycleId=$Cycle;";
                    Assert(Convert.ToInt64(command.ExecuteScalar()) == missingCount,
                        "Every detected open fault remains attached to its original Master test");
                    command.CommandText = "PRAGMA integrity_check;";
                    Assert((string?)command.ExecuteScalar() == "ok", "Automatic Master recording retains temporary SQLite integrity");
                    command.CommandText = "PRAGMA foreign_key_check;";
                    using (var foreignKeys = command.ExecuteReader())
                        Assert(!foreignKeys.Read(), "Automatic Master recording creates no orphan history rows");
                    Assert(vm.Total == total && vm.Pass == pass && vm.Fail == fail && vm.Lot == lot,
                        "Automatic Master acceptance cannot increment production counts or LOT");
                    if (missingCount == 2)
                    {
                        for (int attempt = 0; attempt < 100 && (int)Get("_masterEjectInProgress")! != 0; attempt++)
                            await Task.Delay(10);
                        int commands = board.Commands.Count;
                        string acceptedState = vm.State;
                        var window = new JBZUniversalTester.Views.TestWindow(vm, autoStartProduction: false);
                        // The offline-view constructor sets a capacity warning;
                        // restore the accepted fixture state without starting production.
                        vm.State = acceptedState;
                        var surface = (System.Windows.FrameworkElement)window.Content;
                        window.Content = null; // No Loaded/startup hardware lifecycle.
                        surface.DataContext = vm;
                        surface.Resources.MergedDictionaries.Add(window.Resources);
                        surface.Measure(new System.Windows.Size(1920, 1080));
                        surface.Arrange(new System.Windows.Rect(0, 0, 1920, 1080));
                        surface.UpdateLayout();
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                        var counter = (System.Windows.Controls.TextBlock)window.FindName("FaultCountValueText");
                        var counterBorder = (System.Windows.Controls.Border)window.FindName("FaultCountValueBorder");
                        Assert(counter.Text == "2/2" &&
                               System.Windows.Controls.Grid.GetRow(counterBorder) == 3 &&
                               System.Windows.Controls.Grid.GetColumn(counterBorder) == 1 &&
                               Descendants(surface).OfType<System.Windows.Controls.TextBlock>()
                                   .All(text => text.Text != "20/18 • OK") &&
                               !Descendants(surface).OfType<System.Windows.Controls.Button>()
                                   .Any(button => Equals(button.Content, "XÁC NHẬN ĐÃ LẮP XONG MẪU")),
                            "Actual TestView renders detected/required open faults, never total/connected pins, without a confirmation button");
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1920, 1080, 96, 96,
                            System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(surface);
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using (var output = File.Create(Path.Combine(Path.GetTempPath(), "JBZ-MasterOpen-V327.png")))
                            encoder.Save(output);
                        // Project each sample into the real bound counter without
                        // running another hardware cycle or writing more history.
                        foreach (var sample in new[] { MasterSampleType.WrongWiring, MasterSampleType.ShortCircuit, MasterSampleType.OpenCircuit })
                        {
                            Set("_masterSampleType", sample);
                            var detectedKeys = (HashSet<MasterFaultKey>)Get("_masterDetectedFaultKeys")!;
                            MasterFaultKey[] acceptedKeys = detectedKeys.ToArray();
                            Set("_masterSequenceState", MasterSequenceState.WaitingBadMaster);
                            Set("_masterBadVerified", false);
                            Set("_masterFaultCollectionLocked", false);
                            detectedKeys.Clear();
                            for (int detected = 0; detected <= 2; detected++)
                            {
                                if (detected > 0)
                                {
                                    Set("_masterSequenceState", MasterSequenceState.TestingBadMaster);
                                    var key = new MasterFaultKey(MasterSampleCatalog.FaultType(sample), detected, detected + 20, 0, 0);
                                    detectedKeys.Add(key);
                                    detectedKeys.Add(key); // Repeated physical evidence is one fault.
                                }
                                Call("RaiseMasterState");
                                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                                Assert(counter.Text == $"{detected}/2" && vm.WrongCountText == $"{detected}/2" &&
                                       !vm.IsMasterSampleAccepted,
                                    $"{sample} advances actual bound fault counters 0/2 -> 1/2 -> 2/2 without counting pins or duplicate evidence");
                            }
                            detectedKeys.Clear();
                            detectedKeys.UnionWith(acceptedKeys);
                            Set("_masterSequenceState", MasterSequenceState.EjectingBadMaster);
                            Set("_masterBadVerified", true);
                            Set("_masterFaultCollectionLocked", true);
                            Call("RaiseMasterState");
                            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                            Assert(counter.Text == "2/2" && vm.IsMasterSampleAccepted &&
                                   ((System.Windows.Media.SolidColorBrush)counterBorder.Background).Color.ToString() == "#FFDCFCE7",
                                $"{sample} renders accepted detected/required progress in the Số lỗi cell");
                            Set("_masterBadVerified", false);
                            Call("RaiseMasterState");
                            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                            Assert(counter.Text == "2/2" && !vm.IsMasterSampleAccepted,
                                $"{sample} has no OK before acceptance");
                            Set("_masterBadVerified", true);
                        }
                        Set("_masterSequenceState", MasterSequenceState.EjectingGoodMaster);
                        Call("RaiseMasterState");
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                        Assert(counter.Text == vm.CurrentFaultCount.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                               !counter.Text.Contains('/') && vm.IsMasterSampleAccepted,
                            "Accepted good Master retains the ordinary production fault count");
                        Set("_masterSequenceState", MasterSequenceState.TestingGoodMaster);
                        Call("RaiseMasterState");
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                        Assert(counter.Text == vm.CurrentFaultCount.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                               !counter.Text.Contains('/') && !vm.IsMasterSampleAccepted,
                            "Good Master shows only actual faults, never connected-pin progress");
                        Set("_masterApproved", true);
                        Call("RaiseMasterState");
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                        Assert(counter.Text == vm.CurrentFaultCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            "Production restores the ordinary fault count outside Master");
                        Set("_masterApproved", false);
                        Set("_masterSequenceState", MasterSequenceState.EjectingBadMaster);
                        Set("_masterSampleType", MasterSampleType.OpenCircuit);
                        Call("RaiseMasterState");
                        Assert(board.Commands.Count == commands, "Rendering automatic Master status never sends hardware commands");
                        typeof(JBZUniversalTester.Views.TestWindow).GetField("_allowClose", flags)!.SetValue(window, true);
                        window.Close();
                    }
                }
            }
            finally
            {
                AppSoundService.Current.StopAll(force: true);
                SqliteConnection.ClearAllPools();
                if (Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "JBZ-MasterConfirm-"), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(root, true);
            }
        });
    }
}
