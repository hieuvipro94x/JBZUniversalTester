using System.Reflection;
using JBZUniversalTester.Models;
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
                ResistanceChannels = [new() { Name = "R1", Channel = 1, Enabled = true, MinOhm = 1, MaxOhm = 3 }] };
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
            var wait = (Task<bool>)Call("WaitForProductionPassSoundAsync", CancellationToken.None)!;
            Assert(requests == 1 && !wait.IsCompleted, "Durable PASS joins the existing sound and cannot release relay early");
            stage.SetResult();
            Assert(events.Contains("DINGDONG") && !wait.IsCompleted, "STAGEOK completion alone cannot release relay");
            ding.SetResult();
            Assert(wait.GetAwaiter().GetResult() && events.Last() == "DONE" &&
                !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
                "Audio gate completes only after DINGDONG, without issuing a relay itself");

            Call("ResetCycleInspectionTrace");
            using var cancellation = new CancellationTokenSource();
            Set("_productionPassSoundTask", new TaskCompletionSource().Task);
            var canceled = (Task<bool>)Call("WaitForProductionPassSoundAsync", cancellation.Token)!;
            cancellation.Cancel();
            bool cancellationObserved = false;
            try { canceled.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancellationObserved = true; }
            Assert(cancellationObserved, "Canceled cycle cannot pass the audio relay gate");
            var staleSound = new TaskCompletionSource();
            Set("_productionPassSoundTask", staleSound.Task);
            var stale = (Task<bool>)Call("WaitForProductionPassSoundAsync", CancellationToken.None)!;
            Set("_runtimeGeneration", 99L);
            vm.State = "LẮP SẢN PHẨM";
            staleSound.SetException(new IOException("Previous cycle sound failed"));
            Assert(!stale.GetAwaiter().GetResult() && vm.State == "LẮP SẢN PHẨM",
                "Previous generation audio cannot overwrite the current screen or release relay");
            Set("_productionPassSoundTask", Task.FromException(new IOException("Audio interrupted")));
            var failed = (Task<bool>)Call("WaitForProductionPassSoundAsync", CancellationToken.None)!;
            Assert(!failed.GetAwaiter().GetResult() && !vm.IsDeviceFault && vm.State.Contains("CHƯA CHẠY RELAY"),
                "Interrupted audio blocks relays without a fatal D2XX restart");
        }
        var startupFault = CreateTestViewModel(new ProductionSettings(), out _,
            passSoundPlayback: _ => throw new IOException("Audio startup failed"));
        typeof(TestViewModel).GetMethod("StartProductionPassSound", flags)!.Invoke(startupFault, [true]);
        var blocked = (Task<bool>)typeof(TestViewModel).GetMethod("WaitForProductionPassSoundAsync", flags)!
            .Invoke(startupFault, [CancellationToken.None])!;
        Assert(!blocked.GetAwaiter().GetResult() && !startupFault.IsDeviceFault,
            "Synchronous audio startup failure stays inside the audio gate and never becomes a D2XX fault");
    }
}
