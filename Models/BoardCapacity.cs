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
        => Math.Clamp(
            RequiredScanUnitsForIo(maxGlobalIo, startCardNumber),
            1,
            MaxExpansionModuleCount);

    /// <summary>
    /// Số scan-unit 64 I/O mà model thực sự yêu cầu. Giá trị này cố ý không
    /// clamp theo phần cứng đã cài để caller có thể phát hiện capacity mismatch.
    /// </summary>
    public static int RequiredScanUnitsForIo(int maxGlobalIo, int startCardNumber = 1)
    {
        if (maxGlobalIo <= 0)
            return 1;

        int first = ((Math.Clamp(startCardNumber, 1, MaxPhysicalCardCount) - 1) * IoPerPhysicalCard) + 1;
        if (maxGlobalIo < first)
            return 1;

        int requiredIoSpan = maxGlobalIo - first + 1;
        return Math.Max(
            1,
            (int)Math.Ceiling(requiredIoSpan / (double)IoPerExpansionModule));
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

    public static BoardScanCapacity Create(ProductionSettings settings, int maxGlobalIo)
    {
        ArgumentNullException.ThrowIfNull(settings);

        BoardCapacity installed = BoardCapacity.FromSettings(settings);
        int required = BoardCapacity.RequiredScanUnitsForIo(
            maxGlobalIo,
            installed.StartCardNumber);
        bool fits = installed.IsRangeWithinSystem &&
                    maxGlobalIo <= BoardCapacity.MaxGlobalIo &&
                    required <= installed.ScanCardCount;

        // Chưa có model thì dùng dải tối thiểu đã được firmware chấp nhận.
        // Khi model vượt capacity, giữ dải active trong giới hạn phần cứng;
        // transport sẽ từ chối START production thay vì gửi tham số sai.
        int activeUnits = maxGlobalIo <= 0
            ? 1
            : Math.Min(required, installed.ScanCardCount);
        BoardCapacity active = BoardCapacity.Create(activeUnits, installed.StartCardNumber);

        return new BoardScanCapacity
        {
            Installed = installed,
            Active = active,
            RequiredScanUnits = required,
            ModelMaxIo = Math.Max(0, maxGlobalIo),
            IsModelWithinInstalledCapacity = fits
        };
    }

    public string CapacityErrorMessage =>
        $"MODEL_CAPACITY_EXCEEDED: Model yêu cầu {RequiredScanUnits} scan units / " +
        $"IO tối đa {ModelMaxIo}, máy chỉ cấu hình {InstalledScanUnits} scan units / " +
        $"{Installed.TotalIoCapacity} IO.";

    public override string ToString() =>
        $"installed={InstalledScanUnits} required={RequiredScanUnits} " +
        $"active={ActiveScanUnits} io={ActiveIoCapacity}";
}

public sealed record BoardCardAddress(
    int GlobalIoNumber,
    int PhysicalCardNumber,
    int LocalIoNumber,
    int ExpansionModuleNumber,
    int PhysicalCardIndexInModule);
