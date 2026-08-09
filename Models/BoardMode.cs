namespace JBZUniversalTester.Models;

public enum BoardMode
{
    Auto = 0,
    D2xx = 1,
    UartTtl = 2
}

public static class BoardModeCatalog
{
    public static string DisplayName(BoardMode mode) => mode switch
    {
        BoardMode.D2xx => "JBZ D2XX",
        BoardMode.UartTtl => "JBZ UART TTL",
        _ => "Tự động nhận dạng"
    };
}

public sealed record BoardProtocolEvent(
    DateTime Timestamp,
    string Family,
    string Raw,
    IReadOnlyList<string> Values);
