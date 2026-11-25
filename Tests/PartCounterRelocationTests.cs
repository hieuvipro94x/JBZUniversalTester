using JBZUniversalTester.Services;
using Microsoft.Data.Sqlite;
namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestPartCounterRelocation()
    {
        string root=Path.Combine(Path.GetTempPath(),"JBZ-PartCntMove-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string app=Path.Combine(root,"EXE"), previous=Path.Combine(root,"OLD");Directory.CreateDirectory(app);Directory.CreateDirectory(previous);
            string destination=Path.Combine(root,"SHARED","PartCnt.txt"), source=Path.Combine(app,"PartCnt.txt");
            string legacy="PART-A 200000 500\r\nkeep malformed legacy row\r\n";File.WriteAllText(source,legacy);
            Func<IEnumerable<string>> noProbe=()=>throw new Exception("Warm/current-file migration must not scan old releases");
            Assert(ProductionDataUpgradeService.MigrateStationPartCounter(destination,app,noProbe).Count==1 &&
                   File.ReadAllText(destination)==legacy && File.ReadAllText(source)==legacy,
                "Initial relocation preserves every source byte, counters and malformed rows");
            File.WriteAllText(source,"PART-A 200000 9000\n");
            Assert(ProductionDataUpgradeService.MigrateStationPartCounter(destination,app,noProbe).Count==0 && File.ReadAllText(destination)==legacy,
                "A stale adjacent EXE mirror cannot overwrite the shared file or undo a PIN reset");
            File.Delete(source);
            string previousFile=Path.Combine(previous,"PartCnt.txt"); File.WriteAllText(previousFile,"PART-B 200000 700\n");
            string imported=Path.Combine(root,"IMPORT","PartCnt.txt");
            Assert(ProductionDataUpgradeService.MigrateStationPartCounter(imported,app,()=>[previous]).Count==1 &&
                   File.ReadAllText(imported)==File.ReadAllText(previousFile),"Legacy first import can inherit the unique previous mirror");
            string other=Path.Combine(root,"OTHER"); Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"PartCnt.txt"),"PART-B 200000 0\n");
            string ambiguous=Path.Combine(root,"AMBIGUOUS","PartCnt.txt");bool refused=false;
            try { ProductionDataUpgradeService.MigrateStationPartCounter(ambiguous,app,()=>[previous,other]); } catch(IOException) { refused=true; }
            Assert(refused && !File.Exists(ambiguous) && File.Exists(previousFile),"Divergent legacy files cannot be guessed by largest count");
            string db=Path.Combine(root,"SQLITE","JBZUniversalTester.db");var repository=new TestHistoryStore(db);
            Assert(!repository.HasImportedPartCounters(),"A fresh SQLite database still requires its legacy initial import");
            repository.ImportPartCountersOnce([new("PART-A",200000,12)],previousFile);
            Assert(repository.HasImportedPartCounters(),"SQLite records the initial import acknowledgement");
            string shared=Path.Combine(Path.GetDirectoryName(db)!,"PartCnt.txt");
            Assert(ProductionDataUpgradeService.MigrateStationPartCounter(shared,app,noProbe,includePreviousVersions:!repository.HasImportedPartCounters()).Count==0,
                "Established SQLite skips all old-version mirrors on upgrade");
            var mirror=new PartCounterStore(shared);mirror.MirrorAll(repository.GetAllProbeCounters());
            Assert(mirror.ReadAll().Single().Counter==12 && repository.GetAllProbeCounters().Single().Counter==12 &&
                   !File.Exists(source) && File.Exists(previousFile),"The shared mirror is rebuilt from SQLite without recreating PartCnt beside the EXE");
            Assert(Path.GetDirectoryName(RuntimePaths.PartCounterFile)==Path.GetDirectoryName(RuntimePaths.DatabaseFile) &&
                   new PartCounterStore().StoragePath==RuntimePaths.PartCounterFile,"All default PartCounterStore consumers use the canonical DB directory");
        }
        finally { SqliteConnection.ClearAllPools();if(Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(),"JBZ-PartCntMove-"),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true); }
    }
}
