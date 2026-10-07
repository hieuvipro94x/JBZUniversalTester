using System.Globalization;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLotDailyCounterReset()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZLotReset", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "history.db");
        try
        {
            var now = new DateTime(2026, 10, 6, 9, 0, 0);
            var settings = new ProductionSettings();
            ProductionConfigService.SetProductLot(settings, "LOT-PART", 2000, "2026-10-06");
            var lots = new LotSequenceService(settings, _ => { }, () => now);
            lots.SelectProduct("LOT-PART", false);
            var model = new ProductModel { PartNumber = "LOT-PART", ModelName = "LOT-MODEL" };
            var part = PartIdentitySnapshot.Capture(model);
            var store = new TestHistoryStore(path);

            ProductionCommitResult Commit(string cycle, long lot, bool passed, string batch)
            {
                var history = new TestHistoryRecord
                {
                    CycleId = cycle, PartNumber = model.PartNumber, ModelName = model.ModelName,
                    Started = now, TestStartedAt = now, Finished = now, ResultAt = now,
                    Passed = passed, Result = passed ? "PASS" : "FAIL",
                    InspectionType = HistoryInspectionType.Product,
                    LotNo = lot, ProductionBatchKey = batch
                };
                return store.CommitResult(ProductionResultCommitRequest.Capture(
                    history, model, settings, [], [], null, ProgramIdentityService.VersionText), null);
            }

            string oldDay = lots.HistoryBatchKey;
            Commit("old-pass", 2106, true, oldDay);
            Assert(lots.TryReconcileCommittedLot(2106, out _), "Old day can recover its committed LOT");
            now = now.AddDays(1);
            string today = lots.HistoryBatchKey;
            var empty = store.GetStatistics(part, now);
            Assert(lots.NextLot == 2000 && empty.DailyTotal == 0 && empty.LastLotNo == 0,
                "New day starts with zero production and base LOT without yesterday's LOT");
            Assert(lots.TryReconcileCommittedLot(2106, out _, oldDay) && lots.NextLot == 2000,
                "Stale SQLite recovery from the old day cannot raise the new day's LOT");
            Assert(store.GetStatistics(part, now).LastLotNo == 0,
                "Even an unfiltered counter query never recovers yesterday's LOT");

            long first = lots.ReserveForCycle("today-pass");
            ProductionCommitResult committed = Commit("today-pass", first, true, today);
            Assert(first == 2001 && lots.TryCommitSuccessfulPass("today-pass", first, out _) &&
                   committed.Statistics.DailyTotal == 1 && committed.Statistics.DailyPass == 1,
                "First PASS after rollover is base plus one and counts only this period");
            Commit("today-fail", 9999, false, today);
            var withFail = store.GetStatistics(part, now);
            Assert(withFail.DailyTotal == 2 && withFail.DailyFail == 1 && withFail.LastLotNo == 2001,
                "FAIL contributes to production but cannot advance the recovered LOT");
            Assert(Commit("today-pass", first, true, today).AlreadyCommitted &&
                   store.GetStatistics(part, now).DailyTotal == 2,
                "CycleId duplicate remains exactly once after scoped counter queries");

            now = now.AddDays(-1);
            var previousDate = store.GetStatistics(part, now);
            Assert(lots.HistoryBatchKey == oldDay && previousDate.DailyTotal == 1 &&
                   previousDate.LastLotNo == 2106 &&
                   lots.TryReconcileCommittedLot(previousDate.LastLotNo, out _, oldDay) &&
                   lots.NextLot == 2106,
                "Selecting a previous date restores that date's saved production and LOT");
            now = now.AddDays(1);
            var selectedToday = store.GetStatistics(part, now);
            Assert(lots.HistoryBatchKey == today && selectedToday.DailyTotal == 2 &&
                   selectedToday.DailyPass == 1 && selectedToday.DailyFail == 1 &&
                   lots.TryReconcileCommittedLot(selectedToday.LastLotNo, out _, today) &&
                   lots.NextLot == 2001,
                "Moving the date forward restores the selected day's saved counters and LOT");

            ProductionConfigService.SetProductLot(settings, "LOT-PART", 3000, "2026-10-07");
            lots.RefreshActiveProduct();
            string manualBatch = lots.HistoryBatchKey;
            Assert(manualBatch != today && lots.NextLot == 3000 &&
                   store.GetStatistics(part, now).DailyTotal == 2 &&
                   store.GetStatistics(part, now).LastLotNo == 2001,
                "Changing a LOT batch never hides the selected day's existing production");
            Assert(lots.TryReconcileCommittedLot(9999, out _, today) && lots.NextLot == 3000,
                "Stale recovery cannot undo an operator's manual reset");
            Assert(lots.TryReconcileCommittedLot(2001, out _, manualBatch, restoreSelectedDate: true) &&
                   lots.NextLot == 2001,
                "Selected-day LOT restores its exact saved value even below the current configured base");
            ProductionConfigService.SetProductLot(settings, "LOT-PART", 3000, "2026-10-07");
            Assert(lots.HistoryBatchKey == manualBatch,
                "Saving unrelated settings does not reset the active period");

            now = now.AddDays(-1);
            string back = lots.HistoryBatchKey;
            Assert(back != oldDay && lots.NextLot == 3000 &&
                   store.GetStatistics(part, now).DailyTotal == 1 &&
                   lots.TryReconcileCommittedLot(2106, out _, back, restoreSelectedDate: true) &&
                   lots.NextLot == 2106,
                "Moving the Windows date backward restores that day's saved counters across LOT batches");
            now = now.AddDays(1);
            Assert(lots.HistoryBatchKey == manualBatch &&
                   store.GetStatistics(part, now).DailyTotal == 2 &&
                   lots.TryReconcileCommittedLot(2001, out _, manualBatch, restoreSelectedDate: true) &&
                   lots.NextLot == 2001,
                "Returning the Windows date restores that day's saved LOT and production");
            var savedModel = model;
            model = new ProductModel { PartNumber = "OTHER-PART", ModelName = "OTHER-MODEL" };
            var otherPart = PartIdentitySnapshot.Capture(model);
            Commit("other-part-pass", 7001, true, "2026-10-07:7000:");
            Assert(store.GetStatistics(otherPart, now).DailyTotal == 1 &&
                   store.GetStatistics(otherPart, now).LastLotNo == 7001 &&
                   store.GetStatistics(part, now).DailyTotal == 2 &&
                   store.GetStatistics(part, now).LastLotNo == 2001,
                "Selecting another part reads only that part's saved counters for the selected date");
            model = savedModel;
            now = now.AddDays(1);
            var newDate = store.GetStatistics(part, now);
            Assert(lots.NextLot == 3000 && newDate.DailyTotal == 0 && newDate.LastLotNo == 0,
                "Only a date without saved results starts with zero production and the configured base LOT");
            ProductionConfigService.SetProductLot(settings, "LOT-PART", 3000, "2026-10-05");
            Assert(settings.LotSettingsByProduct["LOT-PART"].LotNoDate == "2026-10-05",
                "Explicit date changes are saved even when the configured LOT base is unchanged");

            Assert(ScalarInt(path, "SELECT COUNT(*) FROM Tests;") == 4 &&
                   ScalarInt(path, "SELECT COUNT(*) FROM Tests WHERE CycleId='old-pass' AND Lot=2106;") == 1,
                "Resetting counters never deletes or rewrites prior production history");
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();
            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            Assert(Convert.ToString(integrity.ExecuteScalar(), CultureInfo.InvariantCulture) == "ok",
                "Counter period changes preserve SQLite integrity");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
