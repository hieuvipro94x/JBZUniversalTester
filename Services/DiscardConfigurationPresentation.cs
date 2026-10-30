using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed record DiscardConfigurationDisplay(string Title, string IoText, string Description, string Summary, string Color, string Background);

public static class DiscardConfigurationPresentation
{
    public static DiscardConfigurationDisplay For(ProductModel? model)
    {
        if (model is null) return new("CHƯA CHỌN MÃ HÀNG", "—", string.Empty,
            "THÙNG LỖI: CHƯA CHỌN MÃ HÀNG", "#64748B", "#F1F5F9");
        if (model.DiscardContactIo.Count == 0)
            return new("CHƯA CÓ XÁC NHẬN THÙNG LỖI", "CHƯA CẤU HÌNH IO",
                "Mã hàng này chưa cấu hình _DISCARD. Vào Cài đặt → TỰ NHẬN IO THÙNG LỖI, đưa tay/vật qua cảm biến rồi lấy ra. Lưu Cài đặt và chọn lại THT để bổ sung.",
                "THÙNG LỖI: CHƯA CÓ XÁC NHẬN THÙNG LỖI", "#B45309", "#FFFBEB");
        string pair = "IO " + string.Join(" / ", model.DiscardContactIo);
        if (!model.HasDiscardInterlock || model.DiscardContactIo.Distinct().Count() != 2 ||
            model.DiscardContactIo.Any(io => io < 1 || io > BoardCapacity.MaxGlobalIo))
            return new("CẤU HÌNH THÙNG LỖI CHƯA HỢP LỆ", pair,
                "THT cần đúng hai IO khác nhau trong phạm vi hệ thống. Hãy kiểm tra cấu hình _DISCARD; phần mềm giữ nguyên file.",
                "THÙNG LỖI: CẤU HÌNH CHƯA HỢP LỆ • " + pair, "#B91C1C", "#FEF2F2");
        return new("ĐÃ CÓ XÁC NHẬN THÙNG LỖI", pair,
            "File THT đã cấu hình _DISCARD. Giữ nguyên hai IO trong file; cặp IO vừa học không ghi đè cấu hình có sẵn.",
            "THÙNG LỖI: ĐÃ CẤU HÌNH • " + pair, "#166534", "#E8F7EF");
    }
}
