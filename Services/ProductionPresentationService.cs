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
    bool IsWaitingMasterSample { get; }
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
        string value = state.State ?? string.Empty;

        if (state.IsPassResultHeldDuringRemoval)
            return "PASS";

        if (state.IsProductRemovalPending || state.IsWaitingProductRemovalPhase)
        {
            if (state.WaitForFaultProductRemoval && state.DiscardRequiredForFault)
                return "CHỜ XÁC NHẬN THÙNG LỖI";

            if (state.WaitForFaultProductRemoval &&
                (value.Contains("LỖI SAI DÂY", StringComparison.OrdinalIgnoreCase) ||
                 value.Contains("LỖI CHẬP MẠCH", StringComparison.OrdinalIgnoreCase)))
                return "KHÔNG ĐẠT";

            return "THÁO SẢN PHẨM";
        }

        if (value.Contains("THÁO SẢN PHẨM", StringComparison.OrdinalIgnoreCase))
            return "THÁO SẢN PHẨM";

        if (value.StartsWith("PASS", StringComparison.OrdinalIgnoreCase))
            return "PASS";

        if (state.IsDeviceFault ||
            value.Contains("CHƯA KẾT NỐI", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("CHƯA ĐẠT", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("KHÔNG ĐẠT", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("LỖI", StringComparison.OrdinalIgnoreCase))
        {
            return "KHÔNG ĐẠT";
        }

        if (state.IsWaitingMasterSample)
            return "LẮP MẪU MASTER";

        if (state.IsWaitingOrContinuityPhase &&
            (state.PresentationCycleStarted || state.HasProductActivity) &&
            value.Equals("LẮP SẢN PHẨM", StringComparison.OrdinalIgnoreCase))
        {
            return "ĐANG KIỂM TRA";
        }

        // After Master completes, a delayed generic testing message must not
        // outrank the confirmed waiting state. Keep actual new-product activity
        // and explicit equipment/measurement states on their existing paths.
        if (state.MasterApproved &&
            !state.IsManualModeActive &&
            !state.PresentationCycleStarted &&
            state.IsWaitingOrContinuityPhase &&
            !state.HasProductActivity &&
            (value.Equals("ĐANG KIỂM TRA", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("ĐANG KIỂM TRA...", StringComparison.OrdinalIgnoreCase)))
        {
            return "LẮP SẢN PHẨM";
        }

        // Đồng bộ model là thông báo nền, không phải một pha kiểm tra sản phẩm.
        // Nếu chưa có hoạt động sản phẩm thì ô trạng thái vẫn phải mời lắp hàng.
        if (value.StartsWith("ĐÃ ĐỒNG BỘ MÃ HÀNG", StringComparison.OrdinalIgnoreCase) &&
            !state.PresentationCycleStarted &&
            state.IsWaitingOrContinuityPhase &&
            !state.HasProductActivity)
        {
            return "LẮP SẢN PHẨM";
        }

        if (state.IsManualModeActive ||
            state.IsMasterSequenceActive ||
            value.Equals("MANUAL", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("ĐANG", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("KIỂM TRA", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("KẾT NỐI BO", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("ĐỒNG BỘ", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("CONNECTOR LEAK", StringComparison.OrdinalIgnoreCase))
        {
            return "ĐANG KIỂM TRA";
        }

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
        string resultStatus = GetResultStatusText(state);

        if (resultStatus.Equals("ĐANG KIỂM TRA", StringComparison.OrdinalIgnoreCase))
            return "#1976D2";

        if (resultStatus.Equals("LẮP SẢN PHẨM", StringComparison.OrdinalIgnoreCase))
            return "#FFF3A0";

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
        stateBackground.Equals("#FFF3A0", StringComparison.OrdinalIgnoreCase) ||
        stateBackground.Equals("#1976D2", StringComparison.OrdinalIgnoreCase)
            ? "#222222"
            : "#FFFFFF";
}
