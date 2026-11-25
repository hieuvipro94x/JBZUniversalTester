using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static ProductModel RenameTestModel(string path)
    {
        var model = new ThtModelParser().Load(path);
        ModelFileIdentityService.Capture(model);
        return model;
    }
    private static ProductionCommitResult RenameTestRecord(TestHistoryStore store, ProductModel model, int index, bool passed, DateTime at)
        => store.CommitResult(ProductionResultCommitRequest.Capture(new TestHistoryRecord
        {
            CycleId = "rename-" + index, ModelName = model.ModelName, ModelFile = model.SourcePath,
            PartNumber = model.PartNumber, Passed = passed, Result = passed ? "PASS" : "FAIL",
            Started = at, TestStartedAt = at, ResultAt = at, Finished = at,
            InspectionType = HistoryInspectionType.Product, LotNo = index
        }, model, new ProductionSettings(),
            passed ? [] : [new FaultDetail { Type = ProductFaultType.WrongWiring, ExpectedSourceIo = 1, ExpectedTargetIo = 2 }],
            [new ResistanceResult { Name = "R1", Channel = 1, ValueOhm = 100, MinOhm = 99, MaxOhm = 101, Passed = true }],
            [new WaterProofChannelMeasurement(1,true,85,84.8,.2,true)], "SELFTEST"), null);
    private static ProductionStatisticsSnapshot RenameTestStats(TestHistoryStore store, ProductModel model, DateTime now) =>
        store.GetFileStatistics(ModelIdentitySnapshot.Capture(model), PartIdentitySnapshot.Capture(model), now);

    private static void TestThtRenameStatistics()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-Rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string db = Path.Combine(root,"history.db"), aPath = CreateDiscardSettingsTht(root,"A");
            var store = new TestHistoryStore(db); var a = RenameTestModel(aPath); DateTime now = DateTime.Now;
            for (int i=1;i<=100;i++) RenameTestRecord(store,a,i,i!=50,now);
            RenameTestRecord(store,a,101,true,now.AddDays(-1));
            Assert(RenameTestStats(store,a,now).DailyTotal==100,"A begins with exactly today's 100 PASS+FAIL");
            string copyPath = Path.Combine(root,"COPY.tht"); File.Copy(aPath,copyPath);
            var copy = RenameTestModel(copyPath);
            Assert(copy.PartNumber==a.PartNumber && copy.SourceHash==a.SourceHash && RenameTestStats(store,copy,now).DailyTotal==0,
                "A separate copy remains new even with identical part number and bytes");
            RenameTestRecord(store,copy,102,true,now);
            string bPath=Path.Combine(root,"B.tht"); File.Move(aPath,bPath); var b=RenameTestModel(bPath);
            string before=HistoryDataSnapshot(db);
            var bStats=RenameTestStats(store,b,now);
            Assert(bStats.DailyTotal==100 && bStats.DailyPass==99 && bStats.DailyFail==1 && bStats.LifetimeTotal==101,
                "A to B rename restores original daily, PASS, FAIL and lifetime totals");
            Assert(HistoryDataSnapshot(db)==before,"Recognizing a rename does not rewrite history, metadata or child rows");
            var committed=RenameTestRecord(store,b,103,true,now);
            Assert(committed.FileStatistics!.DailyTotal==101 && committed.FileStatistics.DailyPass==100,
                "The first PASS under the new name increments the original group once");
            Assert(RenameTestRecord(store,b,103,true,now).FileStatistics!.DailyTotal==101,
                "Duplicate CycleId under an alias cannot increment production again");
            store=new TestHistoryStore(db);
            Assert(RenameTestStats(store,b,now).DailyTotal==101 && RenameTestStats(store,copy,now).DailyTotal==1,
                "Restart preserves the rename alias and independent copy's quantity");
            string cPath=Path.Combine(root,"C.tht"); File.Move(bPath,cPath); var c=RenameTestModel(cPath);
            Assert(RenameTestStats(store,c,now).DailyTotal==101,"A to B to C rename chains retain one production identity");
            Assert(RenameTestStats(store,c,now.AddDays(1)).DailyTotal==0,"Daily quantity still resets by selected date");
            string otherPath=CreateDiscardSettingsTht(root,"DIFFERENT"); var other=RenameTestModel(otherPath);other.PartNumber=a.PartNumber;
            Assert(RenameTestStats(store,other,now).DailyTotal==0,"Shared part metadata alone cannot inherit another THT's production");
            string wrongPartPath=Path.Combine(root,"WRONG_PART.tht"); File.Copy(cPath,wrongPartPath);
            var wrongPart=RenameTestModel(wrongPartPath); wrongPart.PartNumber="ANOTHER-PART";
            Assert(RenameTestStats(store,wrongPart,now).DailyTotal==0,
                "A mismatched product number cannot join a rename group even if a caller supplies the same hash");
            // Two independently produced files disappear: identical bytes cannot prove which one was renamed.
            File.Delete(copyPath); string ambiguousPath=Path.Combine(root,"AMBIGUOUS.tht"); File.Move(cPath,ambiguousPath);
            Assert(RenameTestStats(store,RenameTestModel(ambiguousPath),now).DailyTotal==0,
                "Ambiguous rename evidence never silently merges two independent production groups");
            Assert(ScalarInt(db,"SELECT COUNT(*) FROM Tests;")==103 &&
                   ScalarInt(db,"SELECT COUNT(*) FROM ResistanceMeasurements;")==103 &&
                   ScalarInt(db,"SELECT COUNT(*) FROM WaterProofMeasurements;")==103 &&
                   ScalarText(db,"PRAGMA integrity_check;")=="ok",
                "All history IDs and measurement children survive aliases and migration");
        }
        finally { SqliteConnection.ClearAllPools(); if(Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(),"JBZ-Rename-"),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true); }
    }

    private static void TestThtRenameLegacyMigration()
    {
        string root=Path.Combine(Path.GetTempPath(),"JBZ-RenameLegacy-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string db=Path.Combine(root,"legacy.db"), aPath=CreateDiscardSettingsTht(root,"OLD"); var now=DateTime.Now;
            var old=new TestHistoryStore(db); RenameTestRecord(old,RenameTestModel(aPath),1,false,now);
            // Simulate the exact v9 schema before filename identities existed, on a fixture only.
            ExecuteSql(db,"DROP TABLE ThtFileIdentities; UPDATE SchemaInfo SET SchemaVersion=9;");
            string before=HistoryDataSnapshot(db); string bPath=Path.Combine(root,"RENAMED.tht");File.Move(aPath,bPath);
            var migrated=new TestHistoryStore(db);
            Assert(File.Exists(db+".pre-schema-v10.backup") && HistoryDataSnapshot(db)==before,
                "v9 to v10 migration backs up SQLite and preserves every history/child row unchanged");
            Assert(RenameTestStats(migrated,RenameTestModel(bPath),now).DailyFail==1 && HistoryDataSnapshot(db)==before,
                "Legacy model SHA256 and part metadata recover a pure rename without editing original history");
            Assert(ScalarText(db,"PRAGMA integrity_check;")=="ok" &&
                   ScalarInt(db,"SELECT COUNT(*) FROM TestFaults f LEFT JOIN Tests t ON t.Id=f.TestId WHERE t.Id IS NULL;")==0,
                "Legacy rename recovery leaves SQLite sound with no orphan faults");
        }
        finally { SqliteConnection.ClearAllPools(); if(Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(),"JBZ-RenameLegacy-"),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true); }
    }
}
