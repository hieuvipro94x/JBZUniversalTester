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
        globalIo = 0;

        if (marker < markerBase || marker >= markerBase + 0x10)
            return false;

        if (index >= IoPerProtocolBank)
            return false;

        int bank = marker - markerBase;
        int relativeZeroBased = (bank * IoPerProtocolBank) + index;

        if (relativeZeroBased < 0 || relativeZeroBased >= Capacity.TotalIoCapacity)
            return false;

        int mapped = Capacity.FirstGlobalIo + relativeZeroBased;
        if (!Capacity.ContainsGlobalIo(mapped))
            return false;

        globalIo = mapped;
        return true;
    }

    public bool TryGetCardAddress(int globalIo, out BoardCardAddress address)
    {
        address = default!;
        if (!Capacity.ContainsGlobalIo(globalIo))
            return false;

        int relative = globalIo - Capacity.FirstGlobalIo;
        int physicalOffset = relative / BoardCapacity.IoPerPhysicalCard;
        int localIo = (relative % BoardCapacity.IoPerPhysicalCard) + 1;
        int physicalCard = Capacity.StartCardNumber + physicalOffset;
        int moduleOffset = physicalOffset / BoardCapacity.PhysicalCardsPerExpansionModule;
        int physicalInModule = (physicalOffset % BoardCapacity.PhysicalCardsPerExpansionModule) + 1;

        address = new BoardCardAddress(
            globalIo,
            physicalCard,
            localIo,
            moduleOffset + 1,
            physicalInModule);
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
