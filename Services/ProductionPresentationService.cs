namespace JBZUniversalTester.Services;

internal interface IProductionPresentationState
{
    string State { get; }
    bool IsDeviceFault { get; }
    bool IsPassResultHeldDuringRemoval { get; }
    bool IsProductRemovalPending { get; }
    bool IsWaitingProductRemovalPhase { get; }
    bool IsWaitingOrContinuityPhase { get; }
    bool WaitForFaultProductRemoval { get; }
    bool DiscardRequiredForFault { get; }
    bool IsManualModeActive { get; }
    bool IsMasterSequenceActive { get; }
    bool IsMasterBadPhase { get; }
    bool PresentationCycleStarted { get; }
    bool HasProductActivity { get; }
    bool MasterApproved { get; }
}

/// <summary>
/// Pure projection of the current production state into operator-facing text and colors.
/// This service owns no hardware, lifecycle, persistence, or mutable production state.
/// </summary>
internal static class ProductionPresentationService
{
    public static string GetResultStatusText(IProductionPresentationState state)
    {
        if (state.IsDeviceFault)
            return "LỖI THIẾT BỊ";

        string value = state.State ?? string.Empty;

        if (state.IsPassResultHeldDuringRemoval)
            return "PASS";

        if (state.IsProductRemovalPending || state.IsWaitingProductRemovalPhase)
        {
            if (state.WaitForFaultProductRemoval && state.DiscardRequiredForFault)
                return "CHỜ XÁC NHẬN THÙNG LỖI";

            return "THÁO SẢN PHẨM";
        }

        if (state.IsManualModeActive || value.Equals("MANUAL", StringComparison.OrdinalIgnoreCase))
            return "MANUAL";

        if (value.Contains("THÁO SẢN PHẨM", StringComparison.OrdinalIgnoreCase))
            return "THÁO SẢN PHẨM";

        if (value.Contains("ĐỒNG BỘ DỮ LIỆU BO", StringComparison.OrdinalIgnoreCase))
            return "ĐỒNG BỘ BO";

        if (value.Contains("CHƯA KẾT NỐI", StringComparison.OrdinalIgnoreCase))
            return "CHƯA KẾT NỐI BO";

        if (value.Contains("KẾT NỐI BO", StringComparison.OrdinalIgnoreCase))
            return "ĐANG KẾT NỐI BO";

        if (value.StartsWith("PASS", StringComparison.OrdinalIgnoreCase))
            return "PASS";

        if (value.Contains("ĐANG TEST LEAK", StringComparison.OrdinalIgnoreCase))
            return "ĐANG TEST LEAK";

        if (value.Contains("THÁO CONNECTOR LEAK", StringComparison.OrdinalIgnoreCase))
            return "THÁO CONNECTOR LEAK";

        if (value.Contains("LẮP LẠI CONNECTOR LEAK", StringComparison.OrdinalIgnoreCase))
            return "LẮP CONNECTOR LEAK";

        if (state.IsMasterSequenceActive)
        {
            return state.IsMasterBadPhase
                ? "KIỂM TRA MASTER LỖI"
                : "KIỂM TRA MASTER ĐẠT";
        }

        if (value.Contains("ĐANG LẮP SẢN PHẨM", StringComparison.OrdinalIgnoreCase))
            return "ĐANG LẮP SẢN PHẨM";

        if (value.Contains("CHƯA ĐẠT", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("KHÔNG ĐẠT", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("LỖI", StringComparison.OrdinalIgnoreCase))
        {
            return "KHÔNG ĐẠT";
        }

        if (value.Contains("CHỜ THÁO", StringComparison.OrdinalIgnoreCase))
            return "CHỜ THÁO";

        if (value.Contains("ĐANG", StringComparison.OrdinalIgnoreCase))
            return "ĐANG KIỂM TRA";

        if (!state.PresentationCycleStarted &&
            !state.IsProductRemovalPending &&
            state.IsWaitingOrContinuityPhase &&
            !state.HasProductActivity)
        {
            return "LẮP SẢN PHẨM";
        }

        return "LẮP SẢN PHẨM";
    }

    public static string GetStateBackground(IProductionPresentationState state)
    {
        if (state.IsDeviceFault)
            return "#C62828";

        string value = state.State ?? string.Empty;

        if (state.IsManualModeActive || value.Equals("MANUAL", StringComparison.OrdinalIgnoreCase))
            return "#FFF3A0";

        if (state.IsProductRemovalPending &&
            !value.StartsWith("PASS", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("THÁO SẢN PHẨM", StringComparison.OrdinalIgnoreCase))
        {
            return "#E65100";
        }

        if (value.Contains("CHƯA KẾT NỐI", StringComparison.OrdinalIgnoreCase))
            return "#C62828";

        if (state.IsMasterSequenceActive)
        {
            if (value.Contains("LỖI THIẾT BỊ", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            {
                return "#C62828";
            }

            return "#FFF3A0";
        }

        if (state.MasterApproved &&
            value.Contains("CHỜ LẮP SẢN PHẨM", StringComparison.OrdinalIgnoreCase))
        {
            return "#FFF3A0";
        }

        if (value.StartsWith("PASS", StringComparison.OrdinalIgnoreCase))
            return "#2AA84A";

        if (value.Contains("LỖI", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("CHƯA ĐẠT", StringComparison.OrdinalIgnoreCase))
        {
            return "#C62828";
        }

        if (value.Contains("ĐANG KIỂM TRA", StringComparison.OrdinalIgnoreCase))
            return "#1976D2";

        if (value.Contains("LẮP SẢN PHẨM", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("CHỜ", StringComparison.OrdinalIgnoreCase))
        {
            return "#FFF3A0";
        }

        return "#FFF3A0";
    }

    public static string GetStateForeground(string stateBackground) =>
        stateBackground.Equals("#FFF3A0", StringComparison.OrdinalIgnoreCase)
            ? "#222222"
            : "#FFFFFF";
}
