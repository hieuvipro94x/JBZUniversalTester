using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public static class BcwLabelService
{
    public static void ValidateTemplate(string template)
    {
        string[] lines = template.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("'", StringComparison.Ordinal)).ToArray();
        if (lines.Length == 0 || lines[0] != "N" || lines[^1] != "P1" ||
            lines.Count(line => line.StartsWith("P", StringComparison.Ordinal)) != 1)
            throw new InvalidDataException("BCW: mẫu phải bắt đầu N và kết thúc một lệnh P1 để in một tem mỗi serial.");
        if (lines.Any(line => Regex.IsMatch(line, "^(FK|FS|FE|FR|V[0-9]{2},|\\^)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw new InvalidDataException("BCW: dùng EPL trực tiếp, không dùng FK/FS/FE/FR hoặc form V00 để tránh xóa/ghi flash máy in.");
        if (!lines.Any(line => line.StartsWith("A", StringComparison.Ordinal) && line.Contains("{PART_NUMBER}", StringComparison.Ordinal)) ||
            !lines.Any(line => line.StartsWith("A", StringComparison.Ordinal) && line.Contains("{BCW_POSITION}", StringComparison.Ordinal)) ||
            !lines.Any(line => Regex.IsMatch(line, "^b-?[0-9]+,-?[0-9]+,Q,.*,\"\\{BCW_QR\\}\"$", RegexOptions.CultureInvariant)) ||
            !lines.Any(line => Regex.IsMatch(line, "^A[0-9,NR-]+,\"\\{BCW_QR\\}\"$", RegexOptions.CultureInvariant)))
            throw new InvalidDataException("BCW: giữ mã sản phẩm, vị trí và {BCW_QR} trong cả lệnh QR b...Q và dòng chữ A... để dữ liệu luôn đồng nhất.");
        LabelTemplateRenderer.Render(template, new Dictionary<string, string>
        { ["PART_NUMBER"] = "VALIDATE", ["BCW_POSITION"] = "LH", ["BCW_QR"] = "2628709L00001" });
    }

    public static string Vehicle(ProductModel model, LabelSettings settings) =>
        model.LabelVariables.TryGetValue("BCW_VEHICLE_CODE", out string? value) ? value.Trim() : settings.BcwVehicleCode.Trim();

    public static string Position(ProductModel model, LabelSettings settings) =>
        (model.LabelVariables.TryGetValue("BCW_POSITION", out string? value) ? value : settings.BcwPosition).Trim().ToUpperInvariant();

    public static string Qr(DateTime date, string vehicle, string position, long sequence)
    {
        if (!Regex.IsMatch(vehicle, "^[A-Za-z0-9]{2}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("BCW: mã xe phải có đúng 2 ký tự chữ/số.");
        if (position is not ("RH" or "LH"))
            throw new InvalidDataException("BCW: vị trí phải là RH hoặc LH.");
        if (sequence is < 1 or > 99999)
            throw new InvalidDataException("BCW: số thứ tự phải từ 00001 đến 99999.");
        string qr = date.ToString("yy", CultureInfo.InvariantCulture) + date.DayOfYear.ToString("D3", CultureInfo.InvariantCulture) +
                    vehicle + position[0] + sequence.ToString("D5", CultureInfo.InvariantCulture);
        if (qr.Length != 13) throw new InvalidDataException("BCW: QR phải đủ 13 ký tự.");
        return qr;
    }

    public static LabelPrintRequest Bind(LabelPrintRequest request, DateTime localDate, long sequence)
    {
        string qr = Qr(localDate, request.Data.BcwVehicleCode, request.Data.BcwPosition, sequence);
        if (string.IsNullOrWhiteSpace(request.Data.PartNumber) || request.Data.PartNumber.Length > 15 ||
            request.Data.PartNumber.Any(c => c < 32 || c > 126 || c is '"' or '{' or '}'))
            throw new InvalidDataException("BCW: Part Number phải là ASCII, tối đa 15 ký tự, không chứa dấu nháy/ngoặc.");
        var variables = new Dictionary<string, string>
        {
            ["BCW_QR"] = qr, ["BCW_POSITION"] = request.Data.BcwPosition, ["PART_NUMBER"] = request.Data.PartNumber
        };
        string template = string.IsNullOrWhiteSpace(request.Profile.InlineTemplate)
            ? BuiltInLabelTemplateStore.Load(BuiltInLabelTemplateStore.ReferenceFor(LabelSettings.BcwTemplate))
            : request.Profile.InlineTemplate;
        ValidateTemplate(template);
        string payload = LabelTemplateRenderer.NormalizeEplJob(LabelTemplateRenderer.Render(template, variables));
        return request with { Payload = payload, Copies = 1, FormatName = "JBZBCW",
            Profile = request.Profile with { InlineTemplate = template },
            Data = request.Data with { Barcode = qr, BarcodePrint = qr, TestedAt = localDate, BcwSerial = sequence } };
    }

    public static LabelPrintRequest RestoreUnsent(TestHistoryRecord history, LabelSettings transport)
    {
        Match qr = Regex.Match(history.LabelPayload, "(?m)^b-?[0-9]+,-?[0-9]+,Q,[^\r\n]*,\"(?<qr>[0-9]{5}[A-Za-z0-9]{2}[RL][0-9]{5})\"\\r?$", RegexOptions.CultureInvariant);
        if (!qr.Success) throw new InvalidDataException("BCW: không đọc được snapshot cũ để khôi phục tem chưa gửi.");
        string value = qr.Groups["qr"].Value;
        var identity = new LabelSettings { TemplateType = LabelSettings.BcwTemplate,
            BcwVehicleCode = value.Substring(5,2), BcwPosition = value[7] == 'R' ? "RH" : "LH",
            PrinterCom = transport.PrinterCom, PrinterName = transport.PrinterName, RawDestination = transport.RawDestination,
            BaudRate = transport.BaudRate, WriteTimeoutMs = transport.WriteTimeoutMs, EncodingName = transport.EncodingName };
        var captured = LabelPrintRequest.Capture(history, new ProductModel { PartNumber = history.PartNumber,
            ProductName = history.PartName, SourcePath = history.ModelFile, ModelName = history.ModelName }, identity);
        string template = history.LabelPayload.Replace(value, "{BCW_QR}", StringComparison.Ordinal)
            .Replace("\"" + history.PartNumber + "\"", "\"{PART_NUMBER}\"", StringComparison.Ordinal)
            .Replace("\"" + identity.BcwPosition + "\"", "\"{BCW_POSITION}\"", StringComparison.Ordinal);
        return captured with { Payload = history.LabelPayload, Profile = captured.Profile with { InlineTemplate = template } };
    }
}
