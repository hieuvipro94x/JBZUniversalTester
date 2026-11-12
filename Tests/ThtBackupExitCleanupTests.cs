using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;
internal static partial class Program
{
    private static void TestThtBackupExitCleanup()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-BackupCleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var cleanup = new ThtBackupCleanupService();
            string a = Path.Combine(root, "A"), b = Path.Combine(root, "B"), untouched = Path.Combine(root, "Untouched");
            foreach (string parent in new[] { a, b, untouched })
            {
                Directory.CreateDirectory(Path.Combine(parent, "ThtBackups", "old"));
                File.WriteAllText(Path.Combine(parent, "ThtBackups", "old", "original.tht"), "old");
                File.WriteAllText(Path.Combine(parent, "ThtBackups", "deleted.tht"), "archived");
                File.WriteAllText(Path.Combine(parent, "current.tht"), "saved-current");
                File.WriteAllText(Path.Combine(parent, "current.tht.leak.json"), "leak-current");
                File.WriteAllText(Path.Combine(parent, "JBZUniversalTester.cfg"), "cfg");
                File.WriteAllText(Path.Combine(parent, "JBZUniversalTester.db"), "history");
                File.WriteAllText(Path.Combine(parent, "JBZUniversalTester.db.pre-schema-v9.backup"), "database-backup");
            }
            cleanup.TrackSavedFile(Path.Combine(a, "current.tht"));
            cleanup.TrackSavedFile(Path.Combine(a, ".", "current.tht"));
            cleanup.TrackSavedFile(Path.Combine(b, "current.tht"));
            Assert(Directory.Exists(Path.Combine(a, "ThtBackups")), "Saving retains backups until application exit");
            cleanup.CleanupAsync().GetAwaiter().GetResult();
            cleanup.CleanupAsync().GetAwaiter().GetResult();
            Assert(!Directory.Exists(Path.Combine(a, "ThtBackups")) && !Directory.Exists(Path.Combine(b, "ThtBackups")) &&
                   Directory.Exists(Path.Combine(untouched, "ThtBackups")),
                "Shutdown removes entire tracked backup folders, including old files, and leaves unrelated folders alone");
            foreach (string parent in new[] { a, b, untouched })
                Assert(File.ReadAllText(Path.Combine(parent, "current.tht")) == "saved-current" &&
                       File.ReadAllText(Path.Combine(parent, "current.tht.leak.json")) == "leak-current" &&
                       File.ReadAllText(Path.Combine(parent, "JBZUniversalTester.cfg")) == "cfg" &&
                       File.ReadAllText(Path.Combine(parent, "JBZUniversalTester.db")) == "history" &&
                       File.ReadAllText(Path.Combine(parent, "JBZUniversalTester.db.pre-schema-v9.backup")) == "database-backup",
                    "THT, Leak configuration, station settings, production history and database backups remain byte-for-byte intact");
            bool rejected = false;
            try { cleanup.TrackSavedFile(Path.Combine(a, "JBZUniversalTester.db")); }
            catch (ArgumentException) { rejected = true; }
            Assert(rejected, "A database cannot register a directory for THT cleanup");
            string locked = Path.Combine(a, "ThtBackups"); Directory.CreateDirectory(locked);
            string lockedFile = Path.Combine(locked, "locked.tht"); File.WriteAllText(lockedFile, "retained");
            cleanup.TrackSavedFile(Path.Combine(a, "current.tht"));
            using (var handle = File.Open(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                cleanup.CleanupAsync().GetAwaiter().GetResult();
                Assert(File.Exists(lockedFile), "A locked backup is logged and retained without aborting shutdown");
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
