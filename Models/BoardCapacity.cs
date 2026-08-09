namespace JBZUniversalTester.Models;

/// <summary>
/// Nguồn sự thật duy nhất cho phạm vi card/I/O của bo JBZ.
///
/// V12.9 chuẩn hóa topology từ hai dữ kiện đã có trong project/báo cáo:
/// - 1 card vật lý = 32 I/O.
/// - 1 module/scan-unit = 2 card vật lý = 64 I/O.
/// - trace Htdrv đã lưu trong project: 8C 00 04 00 quét model tới IO224 và
///   diagnostic round 256 I/O => byte xx là số scan-unit 64 I/O, không phải
///   số card vật lý 32 I/O. Vì vậy ExpansionModuleCount=4 -> xx=4 -> 256 I/O.
///
/// StartCardNumber khác 1 chưa có trace phần cứng riêng; mapping offset hiện
/// được giữ tương thích và được cô lập tại BoardAddressMapper để dễ hiệu chỉnh.
/// </summary>
public sealed record BoardCapacity
{
    public const int IoPerPhysicalCard = 32;
    public const int PhysicalCardsPerExpansionModule = 2;
    public const int IoPerExpansionModule = IoPerPhysicalCard * PhysicalCardsPerExpansionModule;
    public const int MaxExpansionModuleCount = 10;
    public const int MaxPhysicalCardCount = MaxExpansionModuleCount * PhysicalCardsPerExpansionModule;
    public const int MaxGlobalIo = MaxPhysicalCardCount * IoPerPhysicalCard;

    public int ExpansionModuleCount { get; init; }
    public int PhysicalCardCount { get; init; }
    public int ScanCardCount { get; init; }
    public int TotalIoCapacity { get; init; }
    public int StartCardNumber { get; init; }

    /// <summary>Byte xx hiện gửi trong lệnh 8C 00 xx 00.</summary>
    public int StartScanParameter => ScanCardCount;

    public int FirstGlobalIo => ((StartCardNumber - 1) * IoPerPhysicalCard) + 1;
    public int LastGlobalIo => FirstGlobalIo + TotalIoCapacity - 1;

    public bool IsRangeWithinSystem =>
        StartCardNumber >= 1 &&
        StartCardNumber <= MaxPhysicalCardCount &&
        PhysicalCardCount >= 1 &&
        StartCardNumber + PhysicalCardCount - 1 <= MaxPhysicalCardCount &&
        LastGlobalIo <= MaxGlobalIo;

    public static BoardCapacity FromSettings(ProductionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int expansion = Math.Clamp(
            settings.ExpansionCardCount,
            1,
            MaxExpansionModuleCount);

        int physical = expansion * PhysicalCardsPerExpansionModule;
        int start = Math.Clamp(settings.StartCardNumber, 1, MaxPhysicalCardCount);

        // Byte xx của START_SCAN là số scan-unit 64 I/O đã xác nhận bởi
        // trace command=4 / diagnostic 256 I/O trong project.
        int scan = expansion;

        return new BoardCapacity
        {
            ExpansionModuleCount = expansion,
            PhysicalCardCount = physical,
            ScanCardCount = scan,
            TotalIoCapacity = physical * IoPerPhysicalCard,
            StartCardNumber = start
        };
    }

    public static BoardCapacity Create(int expansionModuleCount, int startCardNumber = 1)
    {
        var settings = new ProductionSettings
        {
            ExpansionCardCount = expansionModuleCount,
            StartCardNumber = startCardNumber
        };
        return FromSettings(settings);
    }

    public static int RequiredExpansionModulesForIo(int maxGlobalIo, int startCardNumber = 1)
    {
        if (maxGlobalIo <= 0)
            return 1;

        int first = ((Math.Clamp(startCardNumber, 1, MaxPhysicalCardCount) - 1) * IoPerPhysicalCard) + 1;
        if (maxGlobalIo < first)
            return 1;

        int requiredIoSpan = maxGlobalIo - first + 1;
        return Math.Clamp(
            (int)Math.Ceiling(requiredIoSpan / (double)IoPerExpansionModule),
            1,
            MaxExpansionModuleCount);
    }

    public bool ContainsGlobalIo(int globalIo) =>
        globalIo >= FirstGlobalIo &&
        globalIo <= LastGlobalIo &&
        globalIo >= 1 &&
        globalIo <= MaxGlobalIo;

    public override string ToString() =>
        $"Expansion={ExpansionModuleCount}; Physical={PhysicalCardCount}; Scan={ScanCardCount}; " +
        $"IO={FirstGlobalIo}-{LastGlobalIo}; START_SCAN xx={StartScanParameter}";
}

public sealed record BoardCardAddress(
    int GlobalIoNumber,
    int PhysicalCardNumber,
    int LocalIoNumber,
    int ExpansionModuleNumber,
    int PhysicalCardIndexInModule);
