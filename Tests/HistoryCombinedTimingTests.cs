using JBZUniversalTester.Models;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestHistoryCombinedTiming()
    {
        DateTime start = new(2026, 10, 7, 13, 38, 50);
        var record = new TestHistoryRecord
        {
            Started = start, InstallStartedAt = start, TestStartedAt = start,
            Finished = start.AddSeconds(1), ResultAt = start.AddSeconds(1),
            Passed = false, Result = "FAIL",
            RemovalStartedAt = start.AddSeconds(13),
            RemovedAt = start.AddSeconds(14.681)
        };
        Assert(record.InstallAndTestDurationSeconds == 1 &&
               record.ExportTestLogText.Contains("장착/검사 13:38:50~13:38:51(1.000초)", StringComparison.Ordinal) &&
               !record.ExportTestLogText.Contains("검사시작", StringComparison.Ordinal) &&
               record.ExportTestLogText.Contains("회로검사:FAIL", StringComparison.Ordinal) &&
               record.ExportTestLogText.Contains("탈거 13:39:03~13:39:04(1.681초)", StringComparison.Ordinal),
            "Coincident installation/test starts still measure until FAIL and retain the actual removal duration");

        record.Passed = true;
        record.Result = "PASS";
        record.ResultAt = start.AddSeconds(5);
        Assert(record.InstallAndTestDurationSeconds == 5 &&
               record.ExportTestLogText.Contains("장착/검사 13:38:50~13:38:55(5.000초)", StringComparison.Ordinal) &&
               record.ExportTestLogText.Contains("회로검사:PASS", StringComparison.Ordinal),
            "Combined duration ends at the final PASS rather than the start of testing");

        record.InstallStartedAt = null;
        record.TestStartedAt = null;
        record.ResultAt = null;
        Assert(record.InstallAndTestDurationSeconds == 1 &&
               record.ExportTestLogText.Contains("장착/검사 13:38:50~13:38:51(1.000초)", StringComparison.Ordinal),
            "Legacy rows retain combined duration using Started and Finished fallbacks");
        record.RemovedAt = null;
        Assert(record.RemovalDurationSeconds is null &&
               record.ExportTestLogText.Contains("탈거 13:39:03~미확인", StringComparison.Ordinal),
            "Removal stays unconfirmed until physical removal is recorded instead of inventing zero elapsed time");
    }
}
