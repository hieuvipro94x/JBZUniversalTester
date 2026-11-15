using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static int RunActivationChild(string role, string name)
    {
        using var activation = new SingleInstanceActivation(name);
        if (role == "request")
        {
            if (SingleInstanceGate.TryAcquire(name, out Mutex? unexpected))
            {
                using (unexpected) unexpected!.ReleaseMutex();
                return 3;
            }
            activation.RequestActivation();
            return 2;
        }
        using var listen = EventWaitHandle.OpenExisting(name + ".listen");
        using var finish = EventWaitHandle.OpenExisting(name + ".finish");
        if (!SingleInstanceGate.TryAcquire(name, out Mutex? owner)) return 4;
        using (owner)
        {
            try
            {
                Console.WriteLine("READY");
                if (!listen.WaitOne(TimeSpan.FromSeconds(15))) return 5;
                activation.StartListening(() => Console.WriteLine("ACTIVATED"));
                Console.WriteLine("LISTENING");
                return finish.WaitOne(TimeSpan.FromSeconds(15)) ? 0 : 6;
            }
            finally { owner!.ReleaseMutex(); }
        }
    }

    private static void TestSingleInstanceActivation()
    {
        string name = @"Local\JBZUniversalTester.ActivationTests." + Guid.NewGuid().ToString("N");
        using var listen = new EventWaitHandle(false, EventResetMode.ManualReset, name + ".listen");
        using var finish = new EventWaitHandle(false, EventResetMode.ManualReset, name + ".finish");
        var children = new List<Process>();
        Process Spawn(string role)
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            foreach (string arg in new[] { "--activation-child", role, name }) info.ArgumentList.Add(arg);
            Process child = Process.Start(info)!; children.Add(child); return child;
        }
        string? Read(Process child) => child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        try
        {
            Process owner = Spawn("owner");
            Assert(Read(owner) == "READY", "The owner holds its activation channel before initializing its UI");
            Process early = Spawn("request");
            Assert(early.WaitForExit(5000) && early.ExitCode == 2 && !owner.HasExited,
                "An early repeated launch queues activation and exits while startup is paused");
            listen.Set();
            var responses = new[] { Read(owner), Read(owner) };
            Assert(responses.Contains("LISTENING") && responses.Contains("ACTIVATED"),
                "The early activation survives until the owner's listener becomes ready");
            Process late = Spawn("request");
            Assert(late.WaitForExit(5000) && late.ExitCode == 2 && Read(owner) == "ACTIVATED" && !owner.HasExited,
                "Later launches signal the same sole owner and exit without starting services");
            finish.Set();
            Assert(owner.WaitForExit(5000) && owner.ExitCode == 0, "Activation listener releases cleanly on exit");
        }
        finally
        {
            listen.Set(); finish.Set();
            foreach (Process child in children)
            {
                if (!child.HasExited && !child.WaitForExit(1000)) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
        OnSettingsDispatcher(async () =>
        {
            var main = new Window { ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000, Width = 200, Height = 100 };
            var dialog = new Window { ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000, Width = 160, Height = 80 };
            var hud = new Window { Focusable = false, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000, Width = 80, Height = 40 };
            try
            {
                Assert(RunningWindowActivator.FindTarget(new[] { main, dialog }, main) is null,
                    "No window before splash creation leaves activation pending");
                main.Show(); dialog.Owner = main; dialog.Show(); hud.Owner = dialog; hud.Show();
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(ReferenceEquals(RunningWindowActivator.FindTarget(new[] { main, dialog, hud }, main), dialog),
                    "An owned confirmation window is selected ahead of the main window and non-focusable Leak HUD");
                main.WindowState = WindowState.Minimized;
                dialog.WindowState = WindowState.Minimized;
                RunningWindowActivator.Activate(dialog);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert(main.WindowState != WindowState.Minimized && dialog.WindowState != WindowState.Minimized && !dialog.Topmost,
                    $"Activation restores the owner and dialog without leaving either permanently topmost: main={main.WindowState}, dialog={dialog.WindowState}, topmost={dialog.Topmost}");
                hud.Close();
                dialog.Close();
                Assert(ReferenceEquals(RunningWindowActivator.FindTarget(new[] { main }, main), main),
                    "The main window is activated when no owned dialog remains");
            }
            finally { hud.Close(); dialog.Close(); main.Close(); }
        });
        Console.WriteLine("INSTANCE_ACTIVATION PASS: early/late cross-process requests, owner selection and minimized restore");
    }
}
