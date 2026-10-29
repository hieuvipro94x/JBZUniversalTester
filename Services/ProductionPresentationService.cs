namespace JBZUniversalTester.Services;

internal interface IProductionPresentationState
{
    string State { get; }
    bool IsDeviceFault { get; }
    bool IsPersistenceFault { get; }
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
    internal const string RejectedProductRemovalText = "SẢN PHẨM KHÔNG ĐẠT\nTHÁO SẢN PHẨM";
    internal const string D2xxFaultText = TesterConnectionNotice.Message;
    internal const string PersistenceFaultText = "LỖI LƯU DỮ LIỆU\nDỪNG KIỂM TRA";

    internal static bool IsSpecificSystemError(string value) =>
        value.Contains("LỖI THIẾT BỊ", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("LỖI KEYSIGHT", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("LỖI LƯU", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("LỖI THỰC THI RELAY", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("RELAY BỊ CHẶN", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("KHÔNG THỂ MỞ JIG", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("LỖI KIỂM TRA LEAK", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("LỖI KIỂM TRA MẪU NG", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("CHƯA KẾT NỐI", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("KHÔNG KẾT NỐI", StringComparison.OrdinalIgnoreCase) ||
        (value.Contains("CẤU HÌNH", StringComparison.OrdinalIgnoreCase) &&
         (value.Contains("LỖI", StringComparison.OrdinalIgnoreCase) ||
          value.Contains("KHÔNG ĐỦ", StringComparison.OrdinalIgnoreCase) ||
          value.Contains("KHÔNG HỢP LỆ", StringComparison.OrdinalIgnoreCase)));

    public static string GetResultStatusText(IProductionPresentationState state)
    {
        string value = state.State ?? string.Empty;

        // Blocking faults outrank PASS/removal labels so the operator sees
        // why testing stopped even when a previous cycle is still latched.
        if (state.IsDeviceFault) return D2xxFaultText;
        if (state.IsPersistenceFault) return PersistenceFaultText;
        if (IsSpecificSystemError(value)) return value;

        if (state.IsPassResultHeldDuringRemoval)
            return "PASS";

        if (state.IsProductRemovalPending || state.IsWaitingProductRemovalPhase)
        {
            if (state.WaitForFaultProductRemoval && state.DiscardRequiredForFault)
                return "CHỜ XÁC NHẬN THÙNG LỖI";

            if (state.WaitForFaultProductRemoval &&
                (value.Contains("LỖI SAI DÂY", StringComparison.OrdinalIgnoreCase) ||
                 value.Contains("LỖI CHẬP MẠCH", StringComparison.OrdinalIgnoreCase)))
                return RejectedProductRemovalText;

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
            if (!state.IsDeviceFault &&
                !value.Contains("THIẾT BỊ", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("CẤU HÌNH", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("LƯU", StringComparison.OrdinalIgnoreCase) &&
                !value.Contains("KẾT NỐI", StringComparison.OrdinalIgnoreCase) &&
                (value.Contains("CHƯA ĐẠT", StringComparison.OrdinalIgnoreCase) ||
                 value.Contains("KHÔNG ĐẠT", StringComparison.OrdinalIgnoreCase) ||
                 value.Contains("FAIL", StringComparison.OrdinalIgnoreCase)))
                return RejectedProductRemovalText;
            return value;
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
        if (state.IsDeviceFault || state.IsPersistenceFault || IsSpecificSystemError(state.State ?? string.Empty))
            return "#C62828";

        // Every Master sample uses the same yellow throughout its sample flow.
        if (state.IsMasterSequenceActive)
            return "#F5E731";

        string value = state.State ?? string.Empty;
        string resultStatus = GetResultStatusText(state);

        if (resultStatus.Equals("ĐANG KIỂM TRA", StringComparison.OrdinalIgnoreCase))
            return "#DCEBFF";

        if (resultStatus.Equals("LẮP MẪU MASTER", StringComparison.OrdinalIgnoreCase))
            return "#F5E731";
        if (resultStatus.Equals("LẮP SẢN PHẨM", StringComparison.OrdinalIgnoreCase))
            return "#83CCEB";

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
            return "#DCEBFF";

        if (value.Contains("LẮP SẢN PHẨM", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("CHỜ", StringComparison.OrdinalIgnoreCase))
        {
            return "#FFF3A0";
        }

        return "#FFF3A0";
    }

    public static string GetStateForeground(string stateBackground, bool isTesting = false) =>
        isTesting && stateBackground.Equals("#DCEBFF", StringComparison.OrdinalIgnoreCase)
            ? "#173B88"
            : stateBackground.Equals("#F5E731", StringComparison.OrdinalIgnoreCase) ||
        stateBackground.Equals("#FFF3A0", StringComparison.OrdinalIgnoreCase) ||
        stateBackground.Equals("#83CCEB", StringComparison.OrdinalIgnoreCase)
            ? "#222222"
            : "#FFFFFF";
}
