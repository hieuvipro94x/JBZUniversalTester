namespace JBZUniversalTester.Services;

/// <summary>
/// Canonical D2XX routing for the ten physical resistance relay channels.
/// The 0x91 payload is a direct selector, not a bit mask.
/// </summary>
public static class D2xxResistanceRouting
{
    public const int MinChannel = 1;
    public const int MaxChannel = 10;

    public static byte ToResistanceSelector(int channel)
    {
        if (channel is < MinChannel or > MaxChannel)
            throw new ArgumentOutOfRangeException(nameof(channel));

        return checked((byte)channel);
    }

    public static byte[] BuildRouteA() => [0x90, 0x00, 0x00, 0x01];

    public static byte[] BuildRouteB(int channel) =>
        [0x91, 0x00, 0x00, ToResistanceSelector(channel)];

    public static byte[] BuildReleaseRouteB() => [0x91, 0x00, 0x00, 0x00];

    public static byte[] BuildReleaseRouteA() => [0x90, 0x00, 0x00, 0x30];
}
