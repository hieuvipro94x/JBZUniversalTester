using JBZUniversalTester.Models;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestManualLeakWithAutomaticDisabled()
    {
        var vm = CreateTestViewModel(new ProductionSettings(), out FakeBoard board);
        var profile = new WaterProofModelSettings { Enabled = false,
            Channel1Enabled = true, Channel2Enabled = false, Channel3Enabled = false,
            Channel1Connector = "A", PressMin = 42, LeakLimit = 7 };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        board.Commands.Clear();
        // Cancellation reaches the serial service gate before any port opens.
        // Previously this threw "HÃY BẬT TEST LEAK TRƯỚC" instead.
        AssertThrows<OperationCanceledException>(() => vm.TestManualWaterProofAsync(
                new WaterProofMachineSettings { PortName = "COM7" }, profile, ct: cts.Token)
            .GetAwaiter().GetResult(), "Manual Leak reaches the run gate while automatic Leak is disabled");
        Assert(!profile.Enabled && profile.Channel1Enabled && profile.Channel1Connector == "A" &&
               profile.PressMin == 42 && profile.LeakLimit == 7,
            "Manual execution preserves the caller's automatic-enable flag and test parameters");
        Assert(!vm.IsDeviceFault && !vm.IsManualModeActive &&
               !board.Commands.Any(command => command.StartsWith("SET:", StringComparison.Ordinal)),
            "Cancelled manual Leak cleans up without device fault or PASS/JIG relay pulse");
    }
}
