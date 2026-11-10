using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace JBZUniversalTester.Services;

/// <summary>Session-local activation channel; it never opens hardware or runtime files.</summary>
public sealed class SingleInstanceActivation : IDisposable
{
    private readonly EventWaitHandle _request;
    private readonly MemoryMappedFile _identity;
    private readonly MemoryMappedViewAccessor _owner;
    private RegisteredWaitHandle? _listener;
    private int _disposed;

    // Construct BEFORE acquiring the instance mutex. Every contender therefore
    // already holds the event while the winner starts, so early clicks persist.
    public SingleInstanceActivation(string instanceName)
    {
        _identity = MemoryMappedFile.CreateOrOpen(instanceName + ".Owner", sizeof(int));
        try
        {
            _owner = _identity.CreateViewAccessor(0, sizeof(int));
            try { _request = new EventWaitHandle(false, EventResetMode.AutoReset, instanceName + ".Activate"); }
            catch { _owner.Dispose(); throw; }
        }
        catch { _identity.Dispose(); throw; }
    }

    public void StartListening(Action activate)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_listener is not null) throw new InvalidOperationException("Activation listener already started.");
        _owner.Write(0, Environment.ProcessId);
        _owner.Flush();
        _listener = ThreadPool.RegisterWaitForSingleObject(_request, (_, _) =>
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                try { activate(); }
                catch (Exception ex) { AsyncFileLogService.Current.Error($"Window activation request failed: {ex}"); }
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void RequestActivation()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        int ownerPid = _owner.ReadInt32(0);
        // Pass the launcher's foreground permission to the actual owner only.
        // If startup has not published its PID yet, the event still queues the
        // request and the first splash Show can consume it.
        if (ownerPid > 0) _ = AllowSetForegroundWindow((uint)ownerPid);
        _request.Set();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _listener?.Unregister(null);
        _request.Dispose();
        _owner.Dispose();
        _identity.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
