namespace JBZUniversalTester.Services;

// Only instrument I/O uses this exception; D2XX route/relay failures remain fatal.
public sealed class KeysightEquipmentException : InvalidOperationException
{
    public KeysightEquipmentException(string message, Exception innerException)
        : base(message, innerException) { }
}
