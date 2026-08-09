using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Security;
using System.Text;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public static class HistoryExportService
{
    private enum HistoryCellType
    {
        Text,
        Number,
        DateTime
    }

    private sealed record HistoryColumn(
        string Header,
        double Width,
        HistoryCellType Type,
        Func<TestHistoryRecord, object?> GetValue);

    // Một nguồn định nghĩa duy nhất cho CSV và XLSX: thứ tự, header, width,
    // kiểu Excel và getter không thể lệch nhau khi thêm/sửa cột.
    private static readonly HistoryColumn[] Columns =
    [
        new("DateTime", 20, HistoryCellType.DateTime, r => r.Finished),
        new("파트명", 18, HistoryCellType.Text, r => r.PartName),
        new("파트번호", 20, HistoryCellType.Text, r => r.PartNumber),
        new("Eco", 12, HistoryCellType.Text, r => r.Eco),
        new("Nco", 12, HistoryCellType.Text, r => r.Nco),
        new("Alc", 12, HistoryCellType.Text, r => r.Alc),
        new("Lot", 12, HistoryCellType.Number, r => r.LotNo),
        new("Result", 22, HistoryCellType.Text, r => r.Result),
        new("FaultType", 24, HistoryCellType.Text, r => r.FaultType),
        new("FaultCode", 24, HistoryCellType.Text, r => r.FaultCode),
        new("ExpectedSourceIo", 14, HistoryCellType.Number, r => r.ExpectedSourceIo),
        new("ExpectedTargetIo", 14, HistoryCellType.Number, r => r.ExpectedTargetIo),
        new("ActualSourceIo", 14, HistoryCellType.Number, r => r.ActualSourceIo),
        new("ActualTargetIo", 14, HistoryCellType.Number, r => r.ActualTargetIo),
        new("FaultSummary", 44, HistoryCellType.Text, r => r.FaultSummary),
        new("FaultDetailsJson", 60, HistoryCellType.Text, r => r.FaultDetailsJson),
        new("HtdrvName", 44, HistoryCellType.Text, r => r.HtdrvName),
        new("ModelName", 22, HistoryCellType.Text, r => r.ModelName),
        new("ModelFile", 42, HistoryCellType.Text, r => r.ModelFile),
        new("OpenCount", 12, HistoryCellType.Number, r => r.OpenCount),
        new("WrongCount", 12, HistoryCellType.Number, r => r.WrongCount),
        new("ShortCount", 12, HistoryCellType.Number, r => r.ShortCount),
        new("Resistance", 32, HistoryCellType.Text, r => r.Resistance),
        new("MeasuredResistance", 18, HistoryCellType.Number, r => r.MeasuredResistance),
        new("ResistanceMin", 16, HistoryCellType.Number, r => r.ResistanceMin),
        new("ResistanceMax", 16, HistoryCellType.Number, r => r.ResistanceMax),
        new("DeviceName", 18, HistoryCellType.Text, r => r.DeviceName),
        new("DeviceNumber", 16, HistoryCellType.Text, r => r.DeviceNumber),
        new("OperatorCompany", 24, HistoryCellType.Text, r => r.OperatorCompany),
        new("ProductionLine", 22, HistoryCellType.Text, r => r.ProductionLine)
    ];

    public static void ExportCsv(string path, IEnumerable<TestHistoryRecord> records)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine(string.Join(',', Columns.Select(column => EscapeCsv(column.Header))));

        foreach (TestHistoryRecord record in records)
        {
            writer.WriteLine(string.Join(',', Columns.Select(column =>
                EscapeCsv(ToCsvValue(column, record)))));
        }
    }

    public static void ExportXlsx(string path, IReadOnlyList<TestHistoryRecord> records)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(path))
            File.Delete(path);

        using FileStream file = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(file, ZipArchiveMode.Create, leaveOpen: false);

        WriteEntry(archive, "[Content_Types].xml", ContentTypesXml());
        WriteEntry(archive, "_rels/.rels", RootRelsXml());
        WriteEntry(archive, "xl/workbook.xml", WorkbookXml());
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
        WriteEntry(archive, "xl/styles.xml", StylesXml());
        WriteEntry(archive, "xl/worksheets/sheet1.xml", SheetXml(records));
    }

    private static string ToCsvValue(HistoryColumn column, TestHistoryRecord record)
    {
        object? value = column.GetValue(record);
        if (value is null)
            return string.Empty;

        return column.Type switch
        {
            HistoryCellType.DateTime => ((DateTime)value).ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
            HistoryCellType.Number => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    private static string EscapeCsv(string? value)
    {
        string text = value ?? string.Empty;
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }

    private static void WriteEntry(ZipArchive archive, string name, string text)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string SheetXml(IReadOnlyList<TestHistoryRecord> records)
    {
        var sb = new StringBuilder(32_768);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        sb.Append("<cols>");
        for (int i = 0; i < Columns.Length; i++)
            sb.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{Columns[i].Width.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        sb.Append("</cols>");
        sb.Append("<sheetData>");
        AppendHeaderRow(sb);

        for (int i = 0; i < records.Count; i++)
            AppendDataRow(sb, i + 2, records[i]);

        sb.Append("</sheetData>");
        if (records.Count >= 1)
        {
            string lastColumn = ColumnName(Columns.Length);
            sb.Append($"<autoFilter ref=\"A1:{lastColumn}{records.Count + 1}\"/>");
        }
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static void AppendHeaderRow(StringBuilder sb)
    {
        sb.Append("<row r=\"1\">");
        for (int i = 0; i < Columns.Length; i++)
        {
            string cell = $"{ColumnName(i + 1)}1";
            sb.Append($"<c r=\"{cell}\" t=\"inlineStr\" s=\"1\"><is><t xml:space=\"preserve\">{Xml(Columns[i].Header)}</t></is></c>");
        }
        sb.Append("</row>");
    }

    private static void AppendDataRow(StringBuilder sb, int rowNumber, TestHistoryRecord record)
    {
        sb.Append($"<row r=\"{rowNumber}\">");
        for (int i = 0; i < Columns.Length; i++)
        {
            HistoryColumn column = Columns[i];
            string cell = $"{ColumnName(i + 1)}{rowNumber}";
            object? value = column.GetValue(record);

            if (value is null)
            {
                sb.Append($"<c r=\"{cell}\"/>");
                continue;
            }

            if (column.Type == HistoryCellType.DateTime)
            {
                double serial = ((DateTime)value).ToOADate();
                sb.Append($"<c r=\"{cell}\" s=\"2\"><v>{serial.ToString("0.###############", CultureInfo.InvariantCulture)}</v></c>");
            }
            else if (column.Type == HistoryCellType.Number)
            {
                string number = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                sb.Append($"<c r=\"{cell}\"><v>{Xml(number)}</v></c>");
            }
            else
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                sb.Append($"<c r=\"{cell}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Xml(text)}</t></is></c>");
            }
        }
        sb.Append("</row>");
    }

    private static string ColumnName(int index)
    {
        var chars = new Stack<char>();
        while (index > 0)
        {
            index--;
            chars.Push((char)('A' + (index % 26)));
            index /= 26;
        }
        return new string(chars.ToArray());
    }

    private static string Xml(string? value) =>
        SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static string ContentTypesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private static string RootRelsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string WorkbookXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets><sheet name="TestHistory" sheetId="1" r:id="rId1"/></sheets>
        </workbook>
        """;

    private static string WorkbookRelsXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
        </Relationships>
        """;

    private static string StylesXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy/mm/dd hh:mm:ss"/></numFmts>
          <fonts count="2">
            <font><sz val="10"/><name val="Calibri"/></font>
            <font><b/><color rgb="FFFFFFFF"/><sz val="10"/><name val="Calibri"/></font>
          </fonts>
          <fills count="3">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
            <fill><patternFill patternType="solid"><fgColor rgb="FF1F4E78"/><bgColor indexed="64"/></patternFill></fill>
          </fills>
          <borders count="2">
            <border><left/><right/><top/><bottom/><diagonal/></border>
            <border><left style="thin"><color rgb="FFD9E2F3"/></left><right style="thin"><color rgb="FFD9E2F3"/></right><top style="thin"><color rgb="FFD9E2F3"/></top><bottom style="thin"><color rgb="FFD9E2F3"/></bottom><diagonal/></border>
          </borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment vertical="center"/></xf>
            <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFill="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
            <xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1" applyAlignment="1"><alignment vertical="center"/></xf>
          </cellXfs>
          <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;
}
