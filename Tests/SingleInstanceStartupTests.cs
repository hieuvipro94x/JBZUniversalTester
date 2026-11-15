using System.Diagnostics;
using System.Reflection;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static int RunSingleInstanceChild(string name)
    {
        using var start = EventWaitHandle.OpenExisting(name + ".start");
        using var finish = EventWaitHandle.OpenExisting(name + ".finish");
        Console.WriteLine("READY");
        if (!start.WaitOne(TimeSpan.FromSeconds(15))) return 3;
        if (!SingleInstanceGate.TryAcquire(name, out Mutex? owner))
        {
            Console.WriteLine("DUPLICATE");
            return 2;
        }

        using (owner)
        {
            try
            {
                Console.WriteLine("PRIMARY");
                // Keep the primary alive before any simulated initialization
                // completes. No App, database or hardware is constructed here.
                return finish.WaitOne(TimeSpan.FromSeconds(15)) ? 0 : 4;
            }
            finally { owner!.ReleaseMutex(); }
        }
    }

    private static void TestSingleInstanceStartup()
    {
        string name = @"Local\JBZUniversalTester.SelfTests." + Guid.NewGuid().ToString("N");
        using var start = new EventWaitHandle(false, EventResetMode.ManualReset, name + ".start");
        using var finish = new EventWaitHandle(false, EventResetMode.ManualReset, name + ".finish");
        var children = new List<Process>();
        try
        {
            Process Spawn()
            {
                var info = new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                    info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                info.ArgumentList.Add("--single-instance-child");
                info.ArgumentList.Add(name);
                Process process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start instance test child.");
                children.Add(process);
                return process;
            }

            string? ReadState(Process process) => process.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

            Process[] contenders = Enumerable.Range(0, 4).Select(_ => Spawn()).ToArray();
            foreach (Process child in contenders)
                Assert(ReadState(child) == "READY", "Every contender reaches the same startup barrier");
            start.Set();
            var primary = new List<Process>();
            foreach (Process child in contenders)
            {
                string? state = ReadState(child);
                Assert(state is "PRIMARY" or "DUPLICATE", "A launch is either the sole owner or rejected");
                if (state == "PRIMARY") primary.Add(child);
                else Assert(child.WaitForExit(5000) && child.ExitCode == 2,
                    "A rejected launch exits without a modal dialog or background lifetime");
            }
            Assert(primary.Count == 1 && !primary[0].HasExited,
                "Exactly one concurrent launch stays alive during slow startup");

            Process late = Spawn();
            Assert(ReadState(late) == "READY" && ReadState(late) == "DUPLICATE" &&
                   late.WaitForExit(5000) && late.ExitCode == 2 && !primary[0].HasExited,
                "Extra clicks remain blocked while the first process is still initializing");
            finish.Set();
            Assert(primary[0].WaitForExit(5000) && primary[0].ExitCode == 0,
                "The primary releases ownership on normal exit");
            Assert(SingleInstanceGate.TryAcquire(name, out Mutex? restarted),
                "A new launch is allowed after the previous primary exits");
            using (restarted) restarted!.ReleaseMutex();

            string source = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "App.xaml.cs"));
            int gate = source.IndexOf("SingleInstanceGate.TryAcquire", StringComparison.Ordinal);
            int initialization = source.IndexOf("StartupPerformanceTrace.Begin();", StringComparison.Ordinal);
            string rejection = source[gate..initialization];
            Assert(gate >= 0 && initialization > gate &&
                   rejection.Contains("Shutdown(2);", StringComparison.Ordinal) &&
                   !rejection.Contains("MessageBox.Show", StringComparison.Ordinal) &&
                   initialization < source.IndexOf("new StartupWindow()", StringComparison.Ordinal),
                "App rejects duplicate launches before splash, configuration, database or hardware initialization");
            string exit = source[source.IndexOf("protected override void OnExit", StringComparison.Ordinal)..];
            Assert(exit.IndexOf("if (!_ownsSingleInstanceMutex)", StringComparison.Ordinal) <
                   exit.IndexOf("AsyncFileLogService.Current", StringComparison.Ordinal),
                "A rejected process exits without creating runtime log or audio services");
        }
        finally
        {
            start.Set(); finish.Set();
            foreach (Process child in children)
            {
                if (!child.HasExited && !child.WaitForExit(1000)) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
    }
}
