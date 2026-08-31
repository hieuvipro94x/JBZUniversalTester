using System.Windows;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Views;

public partial class FaultConfirmationWindow : Window
{
    private readonly Func<int, PinRecord?>? _pinResolver;

    public FaultConfirmationWindow(
        IReadOnlyList<FaultDetail> faults,
        string footer,
        Func<int, PinRecord?>? pinResolver = null)
    {
        InitializeComponent();

        _pinResolver = pinResolver;

        FaultDetail? primaryFault = faults
            .OrderBy(fault => FaultTypeCatalog.Priority(fault.Type))
            .FirstOrDefault();

        IReadOnlyList<OperatorFaultDisplay> displays = faults
            .Select(FaultDisplayFormatter.FormatOperator)
            .ToArray();

        string summary = primaryFault is null
            ? "KIỂM TRA SẢN PHẨM"
            : BuildShortSummary(primaryFault);
        ApplyCompactSummary(summary);

        FaultItemsControl.ItemsSource = displays;
        FooterText.Text = footer ?? string.Empty;
    }

    private void ApplyCompactSummary(string summary)
    {
        string[] lines = summary
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        FaultTypeText.Text = lines.Length > 0 ? lines[0] : "LỖI SẢN PHẨM";
        SummaryText.Text = lines.Length > 1
            ? string.Join(Environment.NewLine, lines.Skip(1))
            : "VUI LÒNG KIỂM TRA SẢN PHẨM";
    }

    private string BuildShortSummary(FaultDetail fault)
    {
        if (fault.Type != ProductFaultType.WrongWiring)
            return FaultDisplayFormatter.FormatOperator(fault).Title;

        PinRecord? actualFrom = ResolveActualPin(fault.ActualSourceIo);
        PinRecord? actualTo = ResolveActualPin(fault.ActualTargetIo);

        // Cả hai IO thực tế đều có trong THT:
        // dùng trực tiếp WireName + Connector của đúng PinRecord.
        if (actualFrom is not null && actualTo is not null)
        {
            string from = FormatMappedEndpoint(actualFrom, fault.ActualSourceIo);
            string to = FormatMappedEndpoint(actualTo, fault.ActualTargetIo);

            return $"LỖI SAI DÂY\n\n{from} NỐI NHẦM VỚI {to}";
        }

        // Một đầu IO không tồn tại trong THT => cắm nhầm lỗ.
        if (actualFrom is not null &&
            fault.ActualTargetIo is int badTargetIo &&
            badTargetIo > 0)
        {
            string known = FormatMappedEndpoint(actualFrom, fault.ActualSourceIo);
            return $"LỖI CẮM NHẦM LỖ\n\n{known} CẮM NHẦM VÀO IO {badTargetIo}";
        }

        if (actualTo is not null &&
            fault.ActualSourceIo is int badSourceIo &&
            badSourceIo > 0)
        {
            string known = FormatMappedEndpoint(actualTo, fault.ActualTargetIo);
            return $"LỖI CẮM NHẦM LỖ\n\n{known} CẮM NHẦM VÀO IO {badSourceIo}";
        }

        // Fallback nếu không có resolver/model: không tự bịa WireName/Housing.
        if (fault.ActualSourceIo is int sourceIo &&
            sourceIo > 0 &&
            string.IsNullOrWhiteSpace(fault.ActualConnectorFrom))
        {
            string known = ResolveFallbackKnownName(fault);
            return $"LỖI CẮM NHẦM LỖ\n\n{known} CẮM NHẦM VÀO IO {sourceIo}";
        }

        if (fault.ActualTargetIo is int targetIo &&
            targetIo > 0 &&
            string.IsNullOrWhiteSpace(fault.ActualConnectorTo))
        {
            string known = ResolveFallbackKnownName(fault);
            return $"LỖI CẮM NHẦM LỖ\n\n{known} CẮM NHẦM VÀO IO {targetIo}";
        }

        string fallbackFrom = FormatFallbackEndpoint(
            fault.WireName,
            fault.ActualConnectorFrom,
            fault.ActualSourceIo);

        string fallbackTo = FormatFallbackEndpoint(
            null,
            fault.ActualConnectorTo,
            fault.ActualTargetIo);

        return $"LỖI SAI DÂY\n\n{fallbackFrom} NỐI NHẦM VỚI {fallbackTo}";
    }

    private PinRecord? ResolveActualPin(int? io)
    {
        if (_pinResolver is null || io is not int value || value <= 0)
            return null;

        return _pinResolver(value);
    }

    private static string FormatMappedEndpoint(PinRecord pin, int? io)
    {
        string wire = !string.IsNullOrWhiteSpace(pin.WireName)
            ? pin.WireName.Trim()
            : io is int value && value > 0
                ? $"IO {value}"
                : "DÂY";

        string housing = FormatHousing(pin.Connector);

        return string.IsNullOrWhiteSpace(housing)
            ? wire
            : $"{wire} [{housing}]";
    }

    private static string FormatFallbackEndpoint(
        string? wireName,
        string? connector,
        int? io)
    {
        string wire = !string.IsNullOrWhiteSpace(wireName)
            ? wireName.Trim()
            : io is int value && value > 0
                ? $"IO {value}"
                : "DÂY";

        string housing = FormatHousing(connector);

        return string.IsNullOrWhiteSpace(housing)
            ? wire
            : $"{wire} [{housing}]";
    }

    private static string FormatHousing(string? connector)
    {
        if (string.IsNullOrWhiteSpace(connector))
            return string.Empty;

        string value = connector.Trim();

        // THT lưu "1", "2", ...
        if (int.TryParse(value, out int directNumber) && directNumber > 0)
            return $"HOUSING {directNumber}";

        // Chuẩn hóa mọi kiểu tên connector thường gặp trong THT:
        // 1 / 2 / 3 / 4
        // HOLDER 1 / HOLDER1 / HOLDER-1
        // CONNECTOR 1 / CN1 / HOUSING 1
        // => luôn hiển thị HOUSING 1 / 2 / 3 / 4...
        // Chỉ đổi cách HIỂN THỊ, tuyệt đối không sửa dữ liệu model/THT.
        string[] prefixes = ["HOUSING", "HOLDER", "CONNECTOR", "CN"];
        foreach (string prefix in prefixes)
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string suffix = value[prefix.Length..]
                .Trim()
                .TrimStart('-', '_', ':')
                .Trim();

            if (int.TryParse(suffix, out int number) && number > 0)
                return $"HOUSING {number}";

            if (prefix.Equals("HOUSING", StringComparison.OrdinalIgnoreCase))
                return value.ToUpperInvariant();
        }

        // ID đặc biệt: giữ nguyên ID thật, chỉ thêm nhãn HOUSING.
        return $"HOUSING {value}";
    }

    private static string ResolveFallbackKnownName(FaultDetail fault)
    {
        if (!string.IsNullOrWhiteSpace(fault.WireName))
        {
            string connector = !string.IsNullOrWhiteSpace(fault.ActualConnectorFrom)
                ? fault.ActualConnectorFrom
                : fault.ActualConnectorTo;

            return FormatFallbackEndpoint(fault.WireName, connector, null);
        }

        return "DÂY";
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
