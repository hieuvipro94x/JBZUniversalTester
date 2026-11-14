using System.Reflection;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestBoardFaultRecoveryStorage()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-board-checkpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = CreateDiscardSettingsTht(root, "RECOVERY-A");
            var model = new ThtModelParser().Load(path);
            var settings = new ProductionSettings { MasterFaultRequiredCount = 0 };
            var store = new TestHistoryStore(Path.Combine(root, "history.db"));
            DateTime at = DateTime.Today.AddHours(8);
            TestHistoryRecord Row(string cycle, string kind, bool pass) => new()
            {
                CycleId = cycle, InspectionType = kind, Started = at, TestStartedAt = at, ResultAt = at.AddSeconds(10),
                Finished = at.AddSeconds(10), Passed = pass, Result = pass ? "PASS" : "INTERRUPTED",
                PartNumber = model.PartNumber, ModelName = model.ModelName, ModelFile = path,
                LotNo = pass ? 7001 : 0, InspectionTrace = pass ? "회로 검사:PASS" : "검사 중단: 보드 연결 끊김",
                FaultCode = pass ? "" : "BOARD_DISCONNECTED"
            };
            ProductionResultCommitRequest Capture(TestHistoryRecord row) => ProductionResultCommitRequest.Capture(
                row, model, settings, [], [], null, "SELFTEST");
            store.CommitResult(Capture(Row("completed-pass", HistoryInspectionType.Product, true)), null);
            var interrupted = Capture(Row("interrupted-cycle", HistoryInspectionType.Interrupted, false));
            var checkpoint = new BoardFaultCheckpoint(interrupted.History.CycleId, path, model.PartNumber,
                Views.PartSelectionWindow.PartKey(model), at.AddMinutes(1));
            var recovery = new BoardFaultRecovery(checkpoint, interrupted);
            BoardFaultRecoveryJournal.Write(store.DatabasePath, recovery);
            Assert(File.Exists(BoardFaultRecoveryJournal.JournalPath(store.DatabasePath)), "Fault journal is durable before the database transaction");
            BoardFaultRecoveryJournal.Replay(store);
            BoardFaultRecoveryJournal.Replay(store);
            // Simulate a crash after DB commit but before the journal was removed.
            BoardFaultRecoveryJournal.Write(store.DatabasePath, recovery);
            BoardFaultRecoveryJournal.Replay(new TestHistoryStore(store.DatabasePath));
            var all = new HistorySearchCriteria(null, null, null, "", "ALL");
            var rows = store.SearchAllSummary(all);
            var summary = store.GetHistorySummary(all);
            Assert(rows.Count == 2 && summary.ProductTotal == 1 && summary.ProductPass == 1 && summary.ProductFail == 0 && summary.InterruptedTotal == 1,
                "Interrupted rows survive restart/exactly-once replay without counting PASS/FAIL");
            var saved = rows.Single(row => row.CycleId == "interrupted-cycle");
            Assert(saved.HistoryResultText == "중단" && saved.ExportProgressText == "INTERRUPTED" &&
                   saved.ExportPassCountText == "" && saved.BarcodeOutputText == "" && saved.PrintStatus == "NotRequested",
                "Interrupted History/Excel cannot pretend to be FAIL, consume LOT or show a barcode");
            Assert(store.SearchAllSummary(all with { Result = "FAIL" }).Count == 0 &&
                   store.SearchAllSummary(all with { InspectionType = HistoryInspectionType.Interrupted }).Count == 1,
                "History filters separate interrupted inspections from actual product failures");
            store.UpsertActiveCycle(checkpoint.CycleId, model.PartNumber, path, at, "LATE_STALE_STAGE");
            Assert(store.GetBoardFaultCheckpoint()?.ModelPath == path, "A late stage write cannot overwrite a fault recovery marker");
            store.SaveBoardFaultRecovery(recovery with { InterruptedTest = Capture(Row("completed-pass", HistoryInspectionType.Interrupted, false)) });
            Assert(store.SearchAllSummary(all).Single(row => row.CycleId == "completed-pass").Passed,
                "A completed PASS with the same CycleId always wins over a fault snapshot");
            string csv = Path.Combine(root, "history.csv"), xlsx = Path.Combine(root, "history.xlsx");
            Assert(HistoryExportService.ExportCsv(csv, store.EnumerateForExport(all)) == 2 &&
                   HistoryExportService.ExportXlsx(xlsx, store.EnumerateForExport(all)) == 2,
                "Complete filtered exports include the interrupted row");
            store.AcceptBoardFaultCheckpoint(checkpoint.CycleId);
            Assert(store.GetBoardFaultCheckpoint() is null && store.SearchAllSummary(all).Count == 2,
                "Acknowledging a restored runtime checkpoint preserves all history rows");
            BoardFaultRecoveryJournal.Write(store.DatabasePath, recovery);
            BoardFaultRecoveryJournal.Replay(store);
            Assert(store.GetBoardFaultCheckpoint() is null && store.SearchAllSummary(all).Count == 2,
                "An old journal replay after acknowledgment cannot restore an already accepted product again");
            using var connection = new SqliteConnection("Data Source=" + store.DatabasePath);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            Assert((string?)command.ExecuteScalar() == "ok", "Recovery transactions keep SQLite integrity ok");
            foreach (string child in new[] { "TestFaults", "ResistanceMeasurements", "WaterProofMeasurements" })
            {
                command.CommandText = $"SELECT COUNT(*) FROM {child} c LEFT JOIN Tests t ON t.Id=c.TestId WHERE t.Id IS NULL;";
                Assert(Convert.ToInt64(command.ExecuteScalar()) == 0, "No history child becomes orphaned during checkpoint replay");
            }
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    private static void TestBoardDisconnectExitsAndRestores()
    {
        OnSettingsDispatcher(async () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            string root = Path.Combine(Path.GetTempPath(), "JBZ-board-restore-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            object? previousDatabase = AppContext.GetData("JBZUniversalTester.SelfTest.DatabaseFile");
            AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile", Path.Combine(root, "history.db"));
            TestViewModel? vm = null, restored = null;
            try
            {
                string selected = CreateDiscardSettingsTht(root, "LAST-SELECTED-A");
                string stale = CreateDiscardSettingsTht(root, "STALE-B");
                byte[] original = File.ReadAllBytes(selected);
                var model = new ThtModelParser().Load(selected);
                vm = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0, ShowDiscardConfigurationNotice = false }, out FakeBoard board);
                vm.SetModel(model);
                await ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(vm)!);
                typeof(TestViewModel).GetField("_cycleTestStartedAt", flags)!.SetValue(vm, (DateTime?)DateTime.Now.AddSeconds(-3));
                typeof(TestViewModel).GetField("_productDetectedThisCycle", flags)!.SetValue(vm, true);
                typeof(TestViewModel).GetField("_cycleActive", flags)!.SetValue(vm, true);
                int opens = board.ConnectAttempts;
                var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                vm.BoardFaultExitRequested += (_, _) => exit.TrySetResult();
                board.SetConnectionStateForTest(false);
                await (Task)typeof(TestViewModel).GetMethod("HardwareMonitorLoopAsync", flags)!.Invoke(vm, [CancellationToken.None])!;
                await exit.Task.WaitAsync(TimeSpan.FromSeconds(6));
                Assert(vm.IsDeviceFault && vm.CanCloseAfterBoardFault && board.ConnectAttempts == opens && vm.Pass == 0 && vm.Fail == 0,
                    "USB loss locks the cycle, saves before exit, and never opens/reconnects the tester in the current process");
                var store = new TestHistoryStore(RuntimePaths.DatabaseFile);
                var checkpoint = store.GetBoardFaultCheckpoint();
                Assert(checkpoint?.ModelPath == selected && store.SearchAllSummary(new(null,null,null,"","ALL")).Single().IsInterruptedRecord,
                    "The pending inspection and exact last THT path are durable before the exit request");
                await vm.ShutdownAsync();
                restored = CreateTestViewModel(new ProductionSettings { MasterFaultRequiredCount = 0,
                    LastThtPath = stale, ShowDiscardConfigurationNotice = false }, out FakeBoard nextBoard);
                await restored.PrepareStartupModelAsync();
                await (Task)typeof(TestViewModel).GetMethod("LoadLastTestedModelAsync", flags)!.Invoke(restored, null)!;
                await ((Task)typeof(TestViewModel).GetField("_statisticsLoadTask", flags)!.GetValue(restored)!);
                Assert(restored.CurrentModelPath == selected && restored.BoardRecoveryNotice.Contains("LAST-SELECTED-A") &&
                       restored.DailyTestCount == 0 && restored.Pass == 0 && restored.Fail == 0 &&
                       store.GetBoardFaultCheckpoint() is null && File.ReadAllBytes(selected).SequenceEqual(original),
                    "Restart restores the checkpoint THT over stale CFG and keeps interrupted history outside production totals");
                var gate = typeof(TestViewModel).GetMethod("HandleBoardRestartRemoval", flags)!;
                for (int i=1;i<=6;i++) gate.Invoke(restored, [FrameSeq(i,(1,new[]{2}),(2,new[]{1}))]);
                Assert((int)typeof(TestViewModel).GetField("_boardRestartRemovalRequired", flags)!.GetValue(restored)! == 1,
                    "A product left in the jig cannot resume or pass automatically after restart");
                for (int i=0;i<8;i++) gate.Invoke(restored, [FrameSeq(10)]);
                Assert((int)typeof(TestViewModel).GetField("_boardRestartRemovalRequired", flags)!.GetValue(restored)! == 1,
                    "Duplicate empty frames cannot fake removal confirmation");
                for (int i=11;i<15;i++) gate.Invoke(restored, [FrameSeq(i)]);
                Assert((int)typeof(TestViewModel).GetField("_boardRestartRemovalRequired", flags)!.GetValue(restored)! == 0 &&
                       !nextBoard.Commands.Any(command => command.StartsWith("SET:",StringComparison.Ordinal)),
                    "Only fresh complete empty frames unlock a new installation, without firing relays");
            }
            finally
            {
                if (vm is not null) await vm.ShutdownAsync();
                if (restored is not null) await restored.ShutdownAsync();
                AppContext.SetData("JBZUniversalTester.SelfTest.DatabaseFile", previousDatabase);
                SqliteConnection.ClearAllPools();
                Directory.Delete(root, true);
            }
        });
    }
}
