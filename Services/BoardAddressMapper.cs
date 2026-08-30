using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// Chuyển địa chỉ bank/index của firmware sang Global I/O và ngược lại.
/// Production và Probe dùng CHUNG mapper này; chỉ semantics SOURCE/TARGET hay
/// touched-I/O được tách ở decoder/router phía sau.
/// </summary>
public sealed class BoardAddressMapper
{
    public const int IoPerProtocolBank = 128;

    public BoardCapacity Capacity { get; private set; }

    public BoardAddressMapper(BoardCapacity capacity)
    {
        Capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
    }

    public void Configure(BoardCapacity capacity)
    {
        Capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
    }

    public bool TryDecode(
        byte marker,
        byte markerBase,
        byte index,
        out int globalIo)
    {
        if (!TryDecodeProtocolAddress(marker, markerBase, index, out globalIo) ||
            !Capacity.ContainsGlobalIo(globalIo))
        {
            globalIo = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Decodes the absolute protocol I/O. The decoder uses this to consume a
    /// complete protocol word even when it lies beyond the active scan capacity.
    /// </summary>
    public static bool TryDecodeProtocolAddress(
        byte marker,
        byte markerBase,
        byte index,
        out int globalIo)
    {
        globalIo = 0;

        if (marker < markerBase || marker >= markerBase + 0x10 ||
            index >= IoPerProtocolBank)
        {
            return false;
        }

        int bank = marker - markerBase;
        int decoded = (bank * IoPerProtocolBank) + index + 1;
        if (decoded > BoardCapacity.MaxGlobalIo)
            return false;

        globalIo = decoded;
        return true;
    }

    public bool TryGetCardAddress(int globalIo, out BoardCardAddress address)
    {
        address = default!;
        if (!Capacity.ContainsGlobalIo(globalIo))
            return false;

        int zeroBased = globalIo - 1;
        int expansionCard = (zeroBased / BoardCapacity.IoPerExpansionCard) + 1;
        int port = ((zeroBased % BoardCapacity.IoPerExpansionCard) / BoardCapacity.IoPerPort) + 1;
        int localIo = (zeroBased % BoardCapacity.IoPerPort) + 1;

        address = new BoardCardAddress(
            globalIo,
            expansionCard,
            port,
            localIo);
        return true;
    }

    public BoardCardAddress GetCardAddress(int globalIo)
    {
        if (!TryGetCardAddress(globalIo, out BoardCardAddress address))
            throw new ArgumentOutOfRangeException(nameof(globalIo), globalIo,
                $"I/O nằm ngoài capacity hiện tại: {Capacity}");
        return address;
    }
}
