namespace JBZUniversalTester.Services;

public static class SingleInstanceGate
{
    // Atomic even while the first process is still starting. The owner keeps
    // the handle for its whole lifetime; rejected processes retain no handle.
    public static bool TryAcquire(string name, out Mutex? owner)
    {
        var candidate = new Mutex(initiallyOwned: true, name, out bool createdNew);
        if (!createdNew)
        {
            candidate.Dispose();
            owner = null;
            return false;
        }

        owner = candidate;
        return true;
    }
}
