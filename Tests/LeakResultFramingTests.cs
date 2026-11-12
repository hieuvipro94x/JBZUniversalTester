using System.Reflection;
using System.Text;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLeakResultFragmentedPayload()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var complete = typeof(WaterProofSerialService).GetMethod("HasCompleteUnterminatedResult", flags)!;
        bool Ready(string text) => (bool)complete.Invoke(null, [text])!;
        const string truncated = ":RESULT,-83.9,-80.3,0.0,0.0,0.0,";
        Assert(!Ready(truncated) && !Ready(":RESULT,1,2,3,4,5,") && !Ready(":RESULT,1,2,3,4,5,-"),
            "A trailing comma or partial numeric field cannot count as the sixth Leak result value");
        var buffer = new StringBuilder(truncated);
        buffer.Append("0.0,");
        Assert(Ready(buffer.ToString()) && Ready(":RESULT,83.9,80.3,0,0,0,0"),
            "The buffered RESULT becomes ready after its actual sixth value arrives, with or without trailing comma");
        Assert(!Ready(":RESULT,1,2,,4,5,6,7") && !Ready(":RESULT,1,2,3,4,5,NaN") &&
            !Ready(":PRESS,1,2,3,4,5,6"), "Empty or nonnumeric fields cannot shift channel positions or trigger a result");
        var service = new WaterProofSerialService();
        var profile = new WaterProofModelSettings { Enabled = true, Channel1Enabled = true,
            Channel2Enabled = false, Channel3Enabled = false, PressMin = 35, LeakLimit = 20 };
        var result = (WaterProofRunResult)typeof(WaterProofSerialService).GetMethod("ProcessLine",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [1, buffer.ToString(), profile, null, null])!;
        Assert(result.Passed && result.Channels.Count == 3 && result.Channels[0].FirstPressure == 83.9 &&
            result.Channels[0].SecondPressure == 80.3 && Math.Abs(result.Channels[0].Leak - 3.6) < 0.00001,
            "Fragment reassembly preserves the actual CH1 values and the existing pressure/leak evaluation");
        service.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
