namespace JBZUniversalTester.Models;

public enum BoardMode
{
    Auto = 0,
    D2xx = 1
}

public static class BoardModeCatalog
{
    public static string DisplayName(BoardMode mode) => mode switch
    {
        BoardMode.D2xx => "JBZ D2XX",
        _ => "Tự động nhận dạng"
    };
}

