namespace JBZUniversalTester.Models;

/// <summary>
/// Nguồn sự thật duy nhất cho phạm vi card/I/O của bo JBZ.
///
/// V12.9 chuẩn hóa topology từ hai dữ kiện đã có trong project/báo cáo:
/// - 1 card mở rộng người vận hành chọn = 64 I/O.
/// - Mỗi card mở rộng có 2 port nội bộ, mỗi port = 32 I/O.
/// - trace Htdrv đã lưu trong project: 8C 00 04 00 quét model tới IO224 và
///   diagnostic round 256 I/O => byte xx là số scan-unit 64 I/O, không phải
///   số port 32 I/O. Vì vậy ExpansionCardCount=4 -> xx=4 -> 256 I/O.
/// </summary>
public sealed record BoardCapacity
{
    public const int IoPerPort = 32;
    public const int PortsPerExpansionCard = 2;
    public const int IoPerExpansionCard = IoPerPort * PortsPerExpansionCard;
    public const int MaxExpansionCardCount = 10;
    public const int MaxPortCount = MaxExpansionCardCount * PortsPerExpansionCard;
    public const int MaxGlobalIo = MaxExpansionCardCount * IoPerExpansionCard;

    // Compatibility aliases. Tên "PhysicalCard" cũ thực chất chỉ một port 32 IO.
    public const int IoPerPhysicalCard = IoPerPort;
    public const int PhysicalCardsPerExpansionModule = PortsPerExpansionCard;
    public const int IoPerExpansionModule = IoPerExpansionCard;
    public const int MaxExpansionModuleCount = MaxExpansionCardCount;
    public const int MaxPhysicalCardCount = MaxPortCount;

    public int ExpansionCardCount { get; init; }
    public int PortCount { get; init; }
    public int ScanCardCount { get; init; }
    public int TotalIoCapacity { get; init; }

    // Compatibility properties for existing persistence/runtime consumers.
    public int ExpansionModuleCount => ExpansionCardCount;
    public int PhysicalCardCount => PortCount;
    public int StartCardNumber => 1;

    /// <summary>Byte xx hiện gửi trong lệnh 8C 00 xx 00.</summary>
    public int StartScanParameter => ScanCardCount;

    public int FirstGlobalIo => 1;
    public int LastGlobalIo => TotalIoCapacity;

    public bool IsRangeWithinSystem =>
        ExpansionCardCount is >= 1 and <= MaxExpansionCardCount &&
        ScanCardCount == ExpansionCardCount &&
        PortCount == ExpansionCardCount * PortsPerExpansionCard &&
        TotalIoCapacity == ExpansionCardCount * IoPerExpansionCard;

    public static BoardCapacity FromSettings(ProductionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int expansion = Math.Clamp(
            settings.ExpansionCardCount,
            1,
            MaxExpansionCardCount);

        // Byte xx của START_SCAN là số scan-unit 64 I/O đã xác nhận bởi
        // trace command=4 / diagnostic 256 I/O trong project.
        int scan = expansion;

        return new BoardCapacity
        {
            ExpansionCardCount = expansion,
            PortCount = expansion * PortsPerExpansionCard,
            ScanCardCount = scan,
            TotalIoCapacity = expansion * IoPerExpansionCard
        };
    }

    public static BoardCapacity Create(int expansionCardCount)
    {
        var settings = new ProductionSettings
        {
            ExpansionCardCount = expansionCardCount
        };
        return FromSettings(settings);
    }

    public static int RequiredExpansionCardsForIo(int maxGlobalIo)
        => Math.Clamp(
            RequiredScanUnitsForIo(maxGlobalIo),
            1,
            MaxExpansionCardCount);

    public static int RequiredExpansionModulesForIo(int maxGlobalIo) =>
        RequiredExpansionCardsForIo(maxGlobalIo);

    /// <summary>
    /// Last 64-I/O scan-unit required to reach maxGlobalIo. The value is not
    /// clamped to installed capacity so callers can report a mismatch.
    /// </summary>
    public static int RequiredScanUnitsForIo(int maxGlobalIo)
    {
        if (maxGlobalIo <= 0)
            return 1;

        return Math.Max(
            1,
            (int)Math.Ceiling(maxGlobalIo / (double)IoPerExpansionCard));
    }

    public bool ContainsGlobalIo(int globalIo) =>
        globalIo >= FirstGlobalIo &&
        globalIo <= LastGlobalIo &&
        globalIo >= 1 &&
        globalIo <= MaxGlobalIo;

    public override string ToString() =>
        $"ExpansionCards={ExpansionCardCount}; Ports={PortCount}; Scan={ScanCardCount}; " +
        $"IO={FirstGlobalIo}-{LastGlobalIo}; START_SCAN xx={StartScanParameter}";
}

/// <summary>
/// Tách capacity phần cứng đã cài, capacity model yêu cầu và dải firmware đang
/// được yêu cầu quét. BoardCapacity bên trong vẫn mô tả một dải địa chỉ cụ thể.
/// </summary>
public sealed record BoardScanCapacity
{
    public required BoardCapacity Installed { get; init; }
    public required BoardCapacity Active { get; init; }
    public required int RequiredScanUnits { get; init; }
    public required int ModelMaxIo { get; init; }
    public required bool IsModelWithinInstalledCapacity { get; init; }

    public int InstalledScanUnits => Installed.ScanCardCount;
    public int ActiveScanUnits => Active.ScanCardCount;
    public int ActiveIoCapacity => Active.TotalIoCapacity;
    public int StartScanParameter => Active.StartScanParameter;
    public long RequiredIoCapacity =>
        (long)RequiredScanUnits * BoardCapacity.IoPerExpansionCard;

    public static BoardScanCapacity Create(ProductionSettings settings, int maxGlobalIo)
    {
        ArgumentNullException.ThrowIfNull(settings);

        BoardCapacity installed = BoardCapacity.FromSettings(settings);
        int required = BoardCapacity.RequiredScanUnitsForIo(maxGlobalIo);
        bool fits = installed.IsRangeWithinSystem &&
                    maxGlobalIo <= BoardCapacity.MaxGlobalIo &&
                    required <= installed.ScanCardCount;

        // Required chỉ dùng để validate model. Firmware phải quét đúng toàn bộ
        // số card operator đã cấu hình, không tự thu nhỏ START_SCAN theo model.
        BoardCapacity active = installed;

        return new BoardScanCapacity
        {
            Installed = installed,
            Active = active,
            RequiredScanUnits = required,
            ModelMaxIo = Math.Max(0, maxGlobalIo),
            IsModelWithinInstalledCapacity = fits
        };
    }

    public string CapacityErrorMessage => ModelMaxIo > BoardCapacity.MaxGlobalIo
        ? $"MODEL_CAPACITY_EXCEEDED: Model Max IO={ModelMaxIo} vượt giới hạn " +
          $"{BoardCapacity.MaxGlobalIo} IO / {BoardCapacity.MaxExpansionCardCount} card mở rộng."
        : $"KHÔNG ĐỦ CARD MỞ RỘNG: {Installed.TotalIoCapacity} / {RequiredIoCapacity} IO. " +
          $"Model cần {RequiredScanUnits} card, máy cấu hình {InstalledScanUnits} card.";

    public override string ToString() =>
        $"installed={InstalledScanUnits} required={RequiredScanUnits} " +
        $"active={ActiveScanUnits} io={ActiveIoCapacity}";
}

public sealed record BoardCardAddress(
    int GlobalIoNumber,
    int ExpansionCardNumber,
    int PortNumber,
    int LocalIoOnPort)
{
    // Compatibility aliases: "PhysicalCard" cũ là số port 32 IO toàn cục.
    public int PhysicalCardNumber =>
        ((ExpansionCardNumber - 1) * BoardCapacity.PortsPerExpansionCard) + PortNumber;
    public int LocalIoNumber => LocalIoOnPort;
    public int ExpansionModuleNumber => ExpansionCardNumber;
    public int PhysicalCardIndexInModule => PortNumber;
}
