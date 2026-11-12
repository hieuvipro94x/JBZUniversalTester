using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestPassSoundRelayOrder()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (bool resistance in new[] { true, false })
        {
            var stage = new TaskCompletionSource();
            var ding = new TaskCompletionSource();
            var events = new List<string>();
            int requests = 0;
            async Task Play(bool measuredResistance)
            {
                requests++;
                Assert(measuredResistance == resistance, "Audio selection follows the product resistance requirement");
                if (measuredResistance)
                {
                    events.Add("STAGEOK");
                    await stage.Task;
                }
                events.Add("DINGDONG");
                await ding.Task;
                events.Add("DONE");
            }
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0,
                Relay1JigPulseMs = 10, Relay2MarkingPulseMs = 15, PassMarkingToJigDelayMs = 5,
                ResistanceChannels = [new() { Name = "R1", Channel = 1, Enabled = resistance, MinOhm = 1, MaxOhm = 3 }] };
            var vm = CreateTestViewModel(settings, out FakeBoard board, passSoundPlayback: Play);
            object? Call(string method, params object?[] args) => typeof(TestViewModel).GetMethod(method, flags)!.Invoke(vm, args);
            void Set(string field, object value) => typeof(TestViewModel).GetField(field, flags)!.SetValue(vm, value);
            if (resistance)
            {
                var model = Model(("PAIR", new[] { 1, 2 }));
                Set("_model", model);
                Set("_masterApproved", true);
                Set("_runtimeMode", 1);
                Set("_cycleActive", true);
                Set("_cycleContinuityCompletedAt", DateTime.Now);
                Set("_productionPhase", (int)Enum.Parse(typeof(TestViewModel).GetNestedType("ProductionPhase", flags)!, "Resistance"));
                Call("PrepareResistanceRows", model);
                Call("UpdateProductionResistanceRows", (object)new ResistanceResult[] {
                    new() { Name = "R1", Channel = 1, ValueOhm = 2, IsStable = true, Passed = true, MeasurementStatus = "PASS" } });
                Assert(vm.ResultStatusText == "PASS" && events.SequenceEqual(new[] { "STAGEOK" }),
                    "Final measured PASS starts STAGEOK immediately, before recovery and commit");
            }
            else Call("StartProductionPassSound", false);
            Call("StartProductionPassSound", resistance);
            var playback = (Task)typeof(TestViewModel).GetField("_productionPassSoundTask", flags)!.GetValue(vm)!;
            Assert(requests == 1 && !playback.IsCompleted, "Durable PASS does not replay the existing sound");
            var engine = (TestEngine)typeof(TestViewModel).GetField("_engine", flags)!.GetValue(vm)!;
            engine.SetModel(Model(("PAIR", new[] { 1, 2 })));
            var results = resistance ? new ResistanceResult[] {
                new() { Name = "R1", Channel = 1, ValueOhm = 2, Passed = true, IsStable = true } } : [];
            bool relayCompleted = engine.CompletePassAsync(results, continuityAlreadyValidated: true)
                .WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Assert(relayCompleted && !playback.IsCompleted &&
                board.Commands.IndexOf("SET:2") >= 0 && board.Commands.IndexOf("SET:1") > board.Commands.IndexOf("SET:2") &&
                board.Commands.Last() == "OFF",
                "Configured MARKING/JIG sequence completes while PASS sound is still pending");
            stage.SetResult();
            Assert(events.Contains("DINGDONG") && !playback.IsCompleted, "DINGDONG follows STAGEOK without changing relay timing");
            ding.SetResult();
            playback.GetAwaiter().GetResult();
            Assert(events.Last() == "DONE", "Both WAV stages complete independently after the relay sequence");
            const BindingFlags observerFlags = BindingFlags.Static | BindingFlags.NonPublic;
            Task Observe(Task sound) => (Task)typeof(TestViewModel).GetMethod("ObserveProductionPassSoundAsync", observerFlags)!
                .Invoke(null, [sound])!;
            var staleSound = new TaskCompletionSource();
            Task observed = Observe(staleSound.Task);
            Set("_runtimeGeneration", 99L);
            vm.State = "LẮP SẢN PHẨM";
            staleSound.SetException(new IOException("Previous cycle sound failed"));
            observed.GetAwaiter().GetResult();
            Observe(Task.FromCanceled(new CancellationToken(true))).GetAwaiter().GetResult();
            Assert(vm.State == "LẮP SẢN PHẨM" && !vm.IsDeviceFault,
                "Failed or canceled audio cannot alter a later cycle or create a device fault");
        }
        var startupFault = CreateTestViewModel(new ProductionSettings(), out _,
            passSoundPlayback: _ => throw new IOException("Audio startup failed"));
        typeof(TestViewModel).GetMethod("StartProductionPassSound", flags)!.Invoke(startupFault, [true]);
        Assert(!startupFault.IsDeviceFault,
            "Synchronous audio startup failure is observed without becoming a D2XX fault");
        string source = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "ViewModels", "TestViewModel.cs"));
        Assert(!source.Contains("WaitForProductionPassSoundAsync", StringComparison.Ordinal),
            "Production orchestration cannot await sound completion before CompletePassAsync");
    }
}
