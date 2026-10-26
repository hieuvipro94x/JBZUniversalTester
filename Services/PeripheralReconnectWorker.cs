namespace JBZUniversalTester.Services;

/// <summary>One background reconnect owner per peripheral; never starts a test or operates D2XX.</summary>
public sealed class PeripheralReconnectWorker(
    string name,
    Func<CancellationToken, Task> connect,
    CancellationToken lifetime,
    TimeSpan? retryInterval = null)
{
    private readonly object _gate = new();
    private readonly TimeSpan _interval = retryInterval ?? TimeSpan.FromSeconds(2);
    private Task _completion = Task.CompletedTask;
    private bool _running;
    private long _request;

    public Task Completion { get { lock (_gate) return _completion; } }

    public void Request()
    {
        lock (_gate)
        {
            if (lifetime.IsCancellationRequested) return;
            _request++;
            if (_running) return;
            _running = true;
            _completion = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        bool loggedFailure = false;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                long request;
                lock (_gate) request = _request;
                try
                {
                    await connect(lifetime).ConfigureAwait(false);
                    lifetime.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        // A newer failure/configuration must not be cleared by
                        // the completion of an older connection attempt.
                        if (request != _request) continue;
                        _running = false;
                    }
                    AsyncFileLogService.Current.Application($"PERIPHERAL_RECONNECT {name}: connected");
                    return;
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    if (!loggedFailure)
                    {
                        AsyncFileLogService.Current.Error($"PERIPHERAL_RECONNECT {name}: waiting for USB/device: {ex.Message}");
                        loggedFailure = true;
                    }
                }
                await Task.Delay(_interval, lifetime).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            // Once canceled there can be no replacement worker to overwrite.
            if (lifetime.IsCancellationRequested)
                lock (_gate) _running = false;
        }
    }
}
