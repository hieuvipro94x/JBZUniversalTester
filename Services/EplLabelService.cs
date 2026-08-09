using System.IO;
using System.Text;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public static class EplLabelService
{
    /// <summary>
    /// Label PASS theo đúng thứ tự dữ liệu tìm thấy trong mẫu ALL6.xls:
    /// PartNumber / Eco / PartName / yyMMdd+LOT+WH / PartNumber+yyMMdd+LOT.
    /// Format mặc định KS91 và kích thước 90 x 15 mm.
    /// </summary>
    public static string BuildPassLabel(LabelPrintData data, LabelSettings settings)
    {
        string date = data.TestedAt.ToString("yyMMdd");
        string serialText = $"{date}{data.LotNo}WH";
        string barcodeText = $"{data.PartNumber}{date}{data.LotNo}";

        return Build(
            data.PartNumber,
            data.Eco,
            data.PartName,
            serialText,
            barcodeText,
            settings.FormatName,
            settings.WidthMm,
            settings.HeightMm);
    }

    public static string Build90x15(
        string partNumber,
        string vehicle,
        string productName,
        string serialText,
        string barcodeText,
        string formatName = "KS91") =>
        Build(partNumber, vehicle, productName, serialText, barcodeText, formatName, 90, 15);

    public static string Build(
        string partNumber,
        string eco,
        string partName,
        string serialText,
        string barcodeText,
        string formatName,
        int widthMm,
        int heightMm)
    {
        static string Safe(string? value) =>
            (value ?? string.Empty)
                .Replace("\"", "'")
                .Replace("\r", " ")
                .Replace("\n", " ");

        widthMm = Math.Clamp(widthMm, 20, 200);
        heightMm = Math.Clamp(heightMm, 10, 150);

        // Máy EPL phổ biến dùng 203 dpi ~= 8 dot/mm.
        int widthDots = widthMm * 8;
        int heightDots = heightMm * 8;

        return $"""
N
q{widthDots}
Q{heightDots},24
R0,0
ON
D7
S3
FK"*"
FK"{Safe(formatName)}"
FS"{Safe(formatName)}"
V00,15,N,"DATA1"
V01,15,N,"DATA2"
V02,15,N,"DATA3"
V03,15,N,"DATA4"
V04,25,N,"DATA5"
b123,14,D,h4,V04
A610,26,0,1,1,1,N,V00
A610,46,0,1,1,1,N,V01
A610,62,0,1,1,1,N,V02
A610,80,0,1,1,1,N,V03
ZT
FE
FR"{Safe(formatName)}"
?
{Safe(partNumber)}
{Safe(eco)}
{Safe(partName)}
{Safe(serialText)}
{Safe(barcodeText)}
P1
""";
    }

    public static void SavePreview(string path, string epl)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(fullPath, epl, Encoding.ASCII);
    }
}
