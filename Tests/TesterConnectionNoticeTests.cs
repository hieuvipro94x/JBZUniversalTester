using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestUnifiedTesterConnectionNotice()
    {
        const string message = "CHƯA KẾT NỐI MÁY TEST!";
        Assert(TesterConnectionNotice.Message == message, "One exact operator message names the main tester");
        Assert(TestViewText.Translate(message, "vi") == message &&
               TestViewText.Translate(message, "ko") == "검사기가 연결되지 않았습니다!",
            "The shared connection notice keeps the selected TestView language");
        OnSettingsDispatcher(async () =>
        {
            var runtime = CreateTestViewModel(new ProductionSettings(), out FakeBoard board,
                hardwareFaultReport: (_, _, _) => { });
            board.SetConnectionStateForTest(false);
            board.ThrowOnConnect = true;
            await runtime.InitializeHardwareAsync();
            runtime.ReportBoardUnavailableForOperatorAction("fixture");
            Assert(runtime.IsBoardStartupUnavailable && !runtime.IsDeviceFault &&
                   runtime.State == message && runtime.ResultStatusText == message &&
                   runtime.BoardConnectionMessage == message && runtime.HardwareStatus == message,
                "Startup and unavailable operator actions use the same notice without creating a fatal fault");
            var editor = new ProductionSettingsViewModel(runtime,
                saveSettings: _ => throw new InvalidOperationException("Notification must not save settings"));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            await (Task)typeof(ProductionSettingsViewModel).GetMethod("RunManualRelayCommandAsync", flags)!
                .Invoke(editor, [1, true])!;
            Assert(editor.ManualStatus == message, "Offline relay actions cannot show a false success status");
            await (Task)typeof(ProductionSettingsViewModel).GetMethod("RunManualResetAsync", flags)!.Invoke(editor, null)!;
            Assert(editor.ManualStatus == message && board.Commands.Count == 0,
                "Offline manual reset uses the shared notice without sending hardware commands");

            var faulted = CreateTestViewModel(new ProductionSettings(), out _,
                hardwareFaultReport: (_, _, _) => { });
            MethodInfo report = typeof(TestViewModel).GetMethod("ReportDeviceFaultForTest", flags)!;
            report.Invoke(faulted, [new IOException("Fixture USB loss"), -1]);
            report.Invoke(faulted, [new IOException("Repeated fixture USB loss"), -1]);
            Assert(faulted.IsDeviceFault && faulted.DeviceFaultMessage == message &&
                   faulted.BoardConnectionMessage == message && faulted.HardwareStatus == message &&
                   faulted.State == message && faulted.MasterStatus == message && faulted.ResultStatusText == message &&
                   faulted.DeviceFaultTransitionCount == 1 && faulted.DeviceFaultDialogCount == 1 &&
                   faulted.StateBackground == "#C62828",
                "Actual device loss shares the notice across all statuses and retains the one-shot blocking fault path");
        });
    }
}
