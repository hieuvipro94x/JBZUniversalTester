using JBZUniversalTester.Models;
using System.Buffers.Binary;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JBZUniversalTester.Services;

/// <summary>
/// Đọc model HarnProg/Htdrv từ file .THT/.THA.
///
/// File THT không phải file văn bản thuần. Đây là OLE Compound Document;
/// dữ liệu model nằm trong stream "Contents" và chuỗi được serialize theo
/// MFC CArchive/CString. Lớp này đọc đúng stream Contents, kiểm tra token,
/// lấy model_text CP949, rồi ánh xạ bốn bảng Part/Connector/Pin/Wire.
/// </summary>
public sealed class ThtModelParser
{
    private const uint ExpectedDocumentToken = 0x389DEFB9;
    private const int FieldRecordCount = 64;
    private const int FieldRecordSize = 48;
    private const int ExpectedFieldTableSize = FieldRecordCount * FieldRecordSize;
    private const int ViewRecordSize = 248;

    private static readonly Regex BlankLineRegex = new(
        @"\n[ \t]*\n",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BranchPinRegex = new(
        @"^a(?<number>[1-9]\d{0,4})$",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private readonly Encoding _cp949;

    /// <summary>
    /// Thông tin chẩn đoán của file vừa đọc gần nhất.
    /// Có thể dùng để ghi log khi cần kiểm tra version/field/view.
    /// </summary>
    public ThtLoadDiagnostics? LastDiagnostics { get; private set; }

    static ThtModelParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ThtModelParser()
    {
        _cp949 = Encoding.GetEncoding(
            949,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ReplacementFallback);
    }

    public ProductModel Load(string path)
    {
        ValidatePath(path);

        ThtArchiveDocument document = ReadArchive(path);
        ThtModelTables tables = ParseModelText(document.ModelText);

        ProductModel model = BuildProductModel(path, tables, document.EmbeddedResistanceText);

        LastDiagnostics = new ThtLoadDiagnostics
        {
            Version = document.Version,
            DocumentToken = document.DocumentToken,
            PartRowCount = tables.Part.Rows.Count,
            ConnectorRowCount = tables.Connectors.Rows.Count,
            PinRowCount = tables.Pins.Rows.Count,
            WireRowCount = tables.Wires.Rows.Count,
            FieldCount = document.Fields.Count,
            ViewCount = document.Views.Count,
            ParsedPinCount = model.Pins.Count,
            ParsedNetworkCount = model.Nets.Count,
            ParsedResistanceStepCount = model.ResistanceSteps.Count
        };

        return model;
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Đường dẫn file model không hợp lệ.",
                nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Không tìm thấy file model.",
                path);
        }

        string extension = Path.GetExtension(path);

        if (!extension.Equals(".tht", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".tha", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Định dạng '{extension}' không được hỗ trợ. " +
                "Chỉ nhận file .THT hoặc .THA.");
        }
    }

    // =====================================================================
    // ĐỌC OLE STREAM CONTENTS
    // =====================================================================

    private ThtArchiveDocument ReadArchive(string path)
    {
        byte[] contents;

        try
        {
            var compoundFile = new CompoundFileReader(path);
            contents = compoundFile.ReadStream("Contents");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException(
                "Không thể mở OLE storage của file THT/THA.",
                ex);
        }

        using var stream = new MemoryStream(contents, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        EnsureRemaining(reader, 16, "header Contents");

        uint outerLength = reader.ReadUInt32();
        uint innerLength = reader.ReadUInt32();
        uint flags = reader.ReadUInt32();
        uint documentToken = reader.ReadUInt32();

        if (documentToken != ExpectedDocumentToken)
        {
            throw new InvalidDataException(
                "File không đúng định dạng tài liệu Htdrv/HarnProg. " +
                $"Token mong đợi 0x{ExpectedDocumentToken:X8}, " +
                $"nhưng đọc được 0x{documentToken:X8}.");
        }

        string version = ReadMfcCString(reader, Encoding.ASCII);
        string modelText = ReadMfcCString(reader, _cp949);
        long metadataStart = reader.BaseStream.Position;
        string embeddedResistanceText = TryReadEmbeddedResistanceText(contents, metadataStart, _cp949);

        if (string.IsNullOrWhiteSpace(modelText))
        {
            throw new InvalidDataException(
                "Stream Contents không chứa model_text.");
        }

        int fieldTableOffset = FindFieldTableOffset(contents, metadataStart);

        var fields = new List<ThtFieldRecord>();
        var views = new List<ThtViewRecord>();

        if (fieldTableOffset >= 0)
        {
            ParseFieldAndViewTables(
                contents,
                fieldTableOffset,
                fields,
                views);
        }

        return new ThtArchiveDocument
        {
            OuterLength = outerLength,
            InnerLength = innerLength,
            Flags = flags,
            DocumentToken = documentToken,
            Version = version,
            ModelText = modelText,
            EmbeddedResistanceText = embeddedResistanceText,
            Fields = fields,
            Views = views
        };
    }

    private static string ReadMfcCString(
        BinaryReader reader,
        Encoding encoding)
    {
        EnsureRemaining(reader, 1, "độ dài CString");

        byte first = reader.ReadByte();
        int byteLength;

        if (first < 0xFF)
        {
            byteLength = first;
        }
        else
        {
            EnsureRemaining(reader, 2, "độ dài CString mở rộng");
            ushort length16 = reader.ReadUInt16();

            if (length16 == 0xFFFF)
            {
                EnsureRemaining(reader, 4, "độ dài CString 32-bit");
                uint length32 = reader.ReadUInt32();

                if (length32 > int.MaxValue)
                {
                    throw new InvalidDataException(
                        "CString quá lớn để xử lý.");
                }

                byteLength = (int)length32;
            }
            else
            {
                byteLength = length16;
            }
        }

        EnsureRemaining(reader, byteLength, "nội dung CString");

        byte[] bytes = reader.ReadBytes(byteLength);

        return encoding
            .GetString(bytes)
            .TrimEnd('\0');
    }

    /// <summary>
    /// Htdrv lưu cấu hình điện trở ngay sau model_text dưới dạng:
    /// FF FF FF 00 + MFC CString, ví dụ:
    /// 1\n8000\n11000\n\n2\n8000\n11000\n
    /// </summary>
    private static string TryReadEmbeddedResistanceText(
        byte[] contents,
        long startOffset,
        Encoding encoding)
    {
        if (startOffset < 0 || startOffset + 5 > contents.LongLength)
            return string.Empty;

        int start = checked((int)startOffset);
        if (contents[start] != 0xFF ||
            contents[start + 1] != 0xFF ||
            contents[start + 2] != 0xFF ||
            contents[start + 3] != 0x00)
        {
            return string.Empty;
        }

        try
        {
            using var stream = new MemoryStream(
                contents,
                start + 4,
                contents.Length - (start + 4),
                writable: false);
            using var reader = new BinaryReader(
                stream,
                Encoding.UTF8,
                leaveOpen: false);

            string text = ReadMfcCString(reader, encoding)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Trim();

            return Regex.IsMatch(
                    text,
                    @"(?m)^\s*\d+\s*\n\s*[0-9.,]+\s*\n\s*[0-9.,]+\s*$")
                ? text
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void EnsureRemaining(
        BinaryReader reader,
        long requiredBytes,
        string section)
    {
        long remaining =
            reader.BaseStream.Length - reader.BaseStream.Position;

        if (requiredBytes < 0 || remaining < requiredBytes)
        {
            throw new EndOfStreamException(
                $"File THT bị thiếu dữ liệu tại {section}. " +
                $"Cần {requiredBytes} byte, còn {remaining} byte.");
        }
    }

    // =====================================================================
    // FIELD TABLE / VIEW TABLE
    // =====================================================================

    private int FindFieldTableOffset(
        byte[] contents,
        long searchStart)
    {
        int start = (int)Math.Clamp(
            searchStart,
            0,
            contents.Length);

        for (int offset = start;
             offset <= contents.Length - 4;
             offset++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(
                contents.AsSpan(offset, 4));

            if (value != ExpectedFieldTableSize)
            {
                continue;
            }

            if (IsValidFieldTableCandidate(contents, offset))
            {
                return offset;
            }
        }

        // Model text vẫn có thể dùng được. Field/View chỉ là metadata giao diện
        // nên không làm hỏng việc nạp model khi phiên bản file khác một chút.
        return -1;
    }

    private bool IsValidFieldTableCandidate(
        byte[] contents,
        int offset)
    {
        long fieldsStart = offset + 4L;
        long viewCountOffset =
            fieldsStart + ExpectedFieldTableSize;

        if (viewCountOffset + 4 > contents.Length)
        {
            return false;
        }

        uint viewCount = BinaryPrimitives.ReadUInt32LittleEndian(
            contents.AsSpan((int)viewCountOffset, 4));

        if (viewCount == 0 || viewCount > 64)
        {
            return false;
        }

        long requiredEnd =
            viewCountOffset + 4L + (long)viewCount * ViewRecordSize;

        if (requiredEnd > contents.Length)
        {
            return false;
        }

        int nonEmptyNames = 0;

        for (int index = 0; index < FieldRecordCount; index++)
        {
            int recordOffset =
                (int)fieldsStart + index * FieldRecordSize;

            byte group = contents[recordOffset];

            if (group > 16)
            {
                return false;
            }

            string name = DecodeZeroTerminated(
                contents.AsSpan(recordOffset + 1, 31),
                _cp949);

            uint width = BinaryPrimitives.ReadUInt32LittleEndian(
                contents.AsSpan(recordOffset + 36, 4));

            if (!string.IsNullOrWhiteSpace(name))
            {
                nonEmptyNames++;
            }

            if (width > 100_000)
            {
                return false;
            }
        }

        return nonEmptyNames >= 16;
    }

    private void ParseFieldAndViewTables(
        byte[] contents,
        int fieldTableOffset,
        ICollection<ThtFieldRecord> fields,
        ICollection<ThtViewRecord> views)
    {
        int offset = fieldTableOffset;

        uint tableSize = BinaryPrimitives.ReadUInt32LittleEndian(
            contents.AsSpan(offset, 4));
        offset += 4;

        if (tableSize != ExpectedFieldTableSize)
        {
            return;
        }

        for (int index = 0; index < FieldRecordCount; index++)
        {
            ReadOnlySpan<byte> record =
                contents.AsSpan(offset, FieldRecordSize);

            fields.Add(new ThtFieldRecord
            {
                Index = index,
                Group = record[0],
                Name = DecodeZeroTerminated(record.Slice(1, 31), _cp949),
                Reserved = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(32, 4)),
                ColumnWidth = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(36, 4)),
                ValueType = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(40, 4)),
                FormatFlags = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(44, 4))
            });

            offset += FieldRecordSize;
        }

        uint viewCount = BinaryPrimitives.ReadUInt32LittleEndian(
            contents.AsSpan(offset, 4));
        offset += 4;

        for (int index = 0;
             index < checked((int)viewCount) &&
             offset + ViewRecordSize <= contents.Length;
             index++)
        {
            ReadOnlySpan<byte> record =
                contents.AsSpan(offset, ViewRecordSize);

            var fieldIds = new List<uint>();

            // Trong record Htdrv, danh sách cột hiển thị bắt đầu tại 0x4C
            // (offset 76) và kết thúc bởi 0xFFFFFFFF.
            for (int fieldOffset = 76;
                 fieldOffset <= ViewRecordSize - 4;
                 fieldOffset += 4)
            {
                uint fieldId = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(fieldOffset, 4));

                if (fieldId == 0xFFFFFFFF)
                {
                    break;
                }

                if (fieldId == 0)
                {
                    // Sau danh sách thường là vùng dự trữ toàn số 0.
                    if (fieldIds.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                fieldIds.Add(fieldId);
            }

            views.Add(new ThtViewRecord
            {
                ViewId = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(0, 4)),
                ViewName = DecodeZeroTerminated(
                    record.Slice(4, 20),
                    _cp949),
                ObjectGroup = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(24, 4)),
                TypeMask = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(28, 4)),
                Subview = BinaryPrimitives.ReadUInt32LittleEndian(
                    record.Slice(32, 4)),
                FieldIds = fieldIds
            });

            offset += ViewRecordSize;
        }
    }

    private static string DecodeZeroTerminated(
        ReadOnlySpan<byte> bytes,
        Encoding encoding)
    {
        int length = bytes.IndexOf((byte)0);

        if (length < 0)
        {
            length = bytes.Length;
        }

        return encoding
            .GetString(bytes[..length])
            .Trim();
    }

    // =====================================================================
    // MODEL_TEXT: 4 BẢNG TAB
    // =====================================================================

    private static ThtModelTables ParseModelText(string modelText)
    {
        string normalized = modelText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim('\0', '\n');

        string[] blocks = BlankLineRegex
            .Split(normalized)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (blocks.Length < 4)
        {
            throw new InvalidDataException(
                "model_text không đủ 4 bảng Part/Connector/Pin/Wire. " +
                $"Chỉ đọc được {blocks.Length} bảng.");
        }

        List<ThtTextTable> parsedTables = blocks
            .Select(ParseTable)
            .Where(x => x.Headers.Count > 0)
            .ToList();

        ThtTextTable? part = FindTable(
            parsedTables,
            "파트번호");

        ThtTextTable? connectors = FindTable(
            parsedTables,
            "번 호",
            "커넥터",
            "핀 수");

        ThtTextTable? pins = FindTable(
            parsedTables,
            "커넥터",
            "선이름",
            "I/O",
            "핀번호");

        ThtTextTable? wires = FindTable(
            parsedTables,
            "선이름",
            "선연결",
            "굵기",
            "색깔");

        if (part is null || pins is null || wires is null)
        {
            throw new InvalidDataException(
                "Không nhận diện được đầy đủ bảng Part, Pin và Wire " +
                "trong model_text của file THT.");
        }

        return new ThtModelTables
        {
            Part = part,
            Connectors = connectors ?? new ThtTextTable(),
            Pins = pins,
            Wires = wires,
            AllTables = parsedTables
        };
    }

    private static ThtTextTable ParseTable(string block)
    {
        string[] lines = block
            .Split('\n')
            .Select(x => x.TrimEnd('\r'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (lines.Length == 0)
        {
            return new ThtTextTable();
        }

        string[] originalHeaders = SplitColumns(lines[0]);
        string[] normalizedHeaders = originalHeaders
            .Select(NormalizeHeader)
            .ToArray();

        var rows = new List<ThtTextRow>();

        foreach (string line in lines.Skip(1))
        {
            string[] values = SplitColumns(line);

            bool hasAnyValue = values.Any(
                value => !string.IsNullOrWhiteSpace(value));

            if (!hasAnyValue)
            {
                continue;
            }

            var row = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            for (int column = 0;
                 column < normalizedHeaders.Length;
                 column++)
            {
                string header = normalizedHeaders[column];

                if (string.IsNullOrWhiteSpace(header))
                {
                    continue;
                }

                string value = column < values.Length
                    ? CleanCell(values[column])
                    : string.Empty;

                // Nếu file có tên cột trùng, giữ giá trị không rỗng đầu tiên.
                if (!row.TryGetValue(header, out string? current) ||
                    (string.IsNullOrWhiteSpace(current) &&
                     !string.IsNullOrWhiteSpace(value)))
                {
                    row[header] = value;
                }
            }

            rows.Add(new ThtTextRow(row));
        }

        return new ThtTextTable
        {
            Headers = normalizedHeaders,
            Rows = rows
        };
    }

    private static string[] SplitColumns(string line)
    {
        // Không lọc ô rỗng. Ô rỗng là một phần của định dạng và quyết định
        // đúng vị trí của các cột phía sau.
        return line.Split('\t');
    }

    private static ThtTextTable? FindTable(
        IEnumerable<ThtTextTable> tables,
        params string[] requiredHeaders)
    {
        string[] normalizedRequired = requiredHeaders
            .Select(NormalizeHeader)
            .ToArray();

        return tables.FirstOrDefault(table =>
            normalizedRequired.All(required =>
                table.Headers.Contains(
                    required,
                    StringComparer.OrdinalIgnoreCase)));
    }

    private static string NormalizeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (char character in value.Normalize(NormalizationForm.FormKC))
        {
            char normalized = character switch
            {
                'Ｉ' => 'I',
                'Ｏ' => 'O',
                '／' => '/',
                '－' => '-',
                '０' => '0',
                '１' => '1',
                '２' => '2',
                '３' => '3',
                '４' => '4',
                '５' => '5',
                '６' => '6',
                '７' => '7',
                '８' => '8',
                '９' => '9',
                _ => character
            };

            if (!char.IsWhiteSpace(normalized))
            {
                builder.Append(normalized);
            }
        }

        return builder.ToString();
    }

    // =====================================================================
    // ÁNH XẠ SANG PRODUCTMODEL
    // =====================================================================

    private static string NormalizeWireIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // Hai tên dây nhìn giống nhau trong Htdrv phải luôn rơi vào cùng một net,
        // kể cả file có full-width/Unicode hoặc khoảng trắng thừa.
        string normalized = value
            .Normalize(NormalizationForm.FormKC)
            .Trim();

        return Regex.Replace(normalized, @"\s+", " ");
    }

    private static ProductModel BuildProductModel(
        string path,
        ThtModelTables tables,
        string embeddedResistanceText)
    {
        var model = new ProductModel
        {
            ModelName = Path.GetFileNameWithoutExtension(path),
            SourcePath = Path.GetFullPath(path)
        };

        ReadPartInformation(tables.Part, model);

        Dictionary<string, WireDefinition> wireDefinitions =
            ParseWireDefinitions(tables.Wires);

        Dictionary<string, string> canonicalWireNames =
            BuildCanonicalWireMap(wireDefinitions);

        List<RawPin> rawPins = ParsePins(tables.Pins);

        if (rawPins.Count == 0)
        {
            throw new InvalidDataException(
                "Bảng Pin không có bản ghi I/O hợp lệ.");
        }

        var normalPins = new List<ParsedPin>();
        var specialPins = new List<RawPin>();
        var pinByRaw = new Dictionary<RawPin, PinRecord>();

        foreach (RawPin rawPin in rawPins)
        {
            if (rawPin.Connector.Equals(
                    "_DISCARD",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool isSpecial = IsSpecialA0Pin(rawPin.PinType);

            // V12.4: AO/A0 + aN là topology CLIP thật, KHÔNG được bỏ qua.
            // Giữ raw row để sau khi đã dựng normal pin map có thể liên kết:
            // A0 common -> aN -> đúng I/O ghi trên row aN trong THT.
            if (isSpecial)
                specialPins.Add(rawPin);

            string exactWireName = NormalizeWireIdentity(rawPin.WireName);

            if (string.IsNullOrWhiteSpace(exactWireName))
            {
                var emptyWirePin = new PinRecord(
                    rawPin.Connector,
                    string.Empty,
                    rawPin.IoNumber,
                    rawPin.PinNumber);
                model.Pins.Add(emptyWirePin);
                pinByRaw[rawPin] = emptyWirePin;
                continue;
            }

            string canonicalWireName = canonicalWireNames.TryGetValue(
                exactWireName,
                out string? canonicalName)
                ? canonicalName
                : exactWireName;

            WireDefinition? direct = GetWireDefinition(
                rawPin.WireName,
                wireDefinitions);

            WireDefinition? canonical = GetWireDefinition(
                canonicalWireName,
                wireDefinitions);

            string linkedWire = FirstNotEmpty(
                direct?.LinkedWire,
                canonical?.LinkedWire);

            string section = FirstNotEmpty(
                direct?.Section,
                canonical?.Section);

            string color = FirstNotEmpty(
                direct?.Color,
                canonical?.Color);

            var pin = new PinRecord(
                rawPin.Connector,
                exactWireName,
                rawPin.IoNumber,
                rawPin.PinNumber,
                linkedWire,
                section,
                color);

            // Giữ nguyên tất cả row pin. THT thật có thể dùng cùng một I/O cho
            // nhiều circuit (trace/model production có I/O 11 cho PR1 và SS1).
            model.Pins.Add(pin);
            pinByRaw[rawPin] = pin;

            if (!isSpecial)
                normalPins.Add(new ParsedPin(pin, canonicalWireName));
        }

        // Mạng continuity được gom theo TÊN DÂY THT (sau normalize). Hai pin có
        // cùng tên dây luôn thuộc cùng một mạng. Quan hệ 선연결 chỉ dùng để gộp
        // thêm các tên dây được file khai báo nối chung. Thứ tự pin trong THT
        // được giữ nguyên: I/O đầu tiên là source, các I/O sau là receiver.
        model.Nets = normalPins
            .Where(x => !string.IsNullOrWhiteSpace(x.NetName))
            .GroupBy(
                x => x.NetName,
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                PinRecord[] pins = group
                    .Select(x => x.Pin)
                    .Distinct()
                    .ToArray();

                int[] ioNumbers = pins
                    .Select(x => x.IoNumber)
                    .Distinct()
                    .ToArray();

                return new WireNet(
                    group.Key,
                    ioNumbers,
                    pins);
            })
            .Where(net => net.IoNumbers.Count >= 2)
            .ToList();

        // Dựng riêng topology CLIP sau normal network. Không trộn CLIP vào
        // WireNet thông thường vì CLIP có một đầu A0 chung và quan hệ có thể
        // xuất hiện theo cả hai chiều trong frame board.
        model.Clip = BuildClipTopology(model, specialPins, normalPins, pinByRaw);

        // Nếu file có row AO/aN nhưng thiếu A0 hoặc không có branch hợp lệ,
        // fallback an toàn: chỉ các special row không dựng được topology mới
        // bị bỏ qua để tránh báo chập giả.
        HashSet<int> recognizedClipIo = model.Clip is null
            ? []
            : new HashSet<int>(
                new[] { model.Clip.CommonIo }
                    .Concat(model.Clip.Branches.Select(branch => branch.ClipPin.IoNumber)));

        HashSet<int> normalIo = normalPins.Select(item => item.Pin.IoNumber).ToHashSet();
        foreach (RawPin special in specialPins)
        {
            if (!recognizedClipIo.Contains(special.IoNumber) &&
                !normalIo.Contains(special.IoNumber))
            {
                model.IgnoredIo.Add(special.IoNumber);
            }
        }

        if (model.Pins.Count == 0)
        {
            throw new InvalidDataException(
                "File THT không có chân kiểm tra hợp lệ.");
        }

        // Format thật trong stream Contents được ưu tiên. Nếu file đời khác
        // không có block nhúng thì mới fallback sang bảng text.
        model.ResistanceSteps =
            ParseEmbeddedResistanceSteps(embeddedResistanceText);

        if (model.ResistanceSteps.Count == 0)
        {
            model.ResistanceSteps = ParseResistanceSteps(tables.AllTables);
        }

        return model;
    }

    private static List<ResistanceStep> ParseEmbeddedResistanceSteps(
        string text)
    {
        var result = new List<ResistanceStep>();

        if (string.IsNullOrWhiteSpace(text))
            return result;

        string normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        string[] groups = Regex.Split(
            normalized,
            @"\n\s*\n",
            RegexOptions.CultureInvariant);

        foreach (string group in groups)
        {
            string[] lines = group
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (lines.Length < 3 ||
                !int.TryParse(lines[0], out int channel) ||
                channel <= 0 ||
                !TryParseFlexibleDouble(lines[1], out double minOhm) ||
                !TryParseFlexibleDouble(lines[2], out double maxOhm) ||
                minOhm < 0 ||
                maxOhm < minOhm)
            {
                continue;
            }

            result.Add(new ResistanceStep(
                $"R{channel}",
                channel,
                minOhm,
                maxOhm,
                "90 00 00 01",
                $"91 00 00 {channel:X2}"));
        }

        return result
            .GroupBy(step => step.Channel)
            .Select(group => group.First())
            .OrderBy(step => step.Channel)
            .ToList();
    }

    private static List<ResistanceStep> ParseResistanceSteps(
        IReadOnlyList<ThtTextTable> tables)
    {
        var result = new List<ResistanceStep>();

        foreach (ThtTextTable table in tables)
        {
            bool looksLikeResistance = table.Headers.Any(header =>
                HeaderMatches(header,
                    "저항", "저항값", "Resistance", "ResistanceValue",
                    "Ohm", "Ω", "MinOhm", "MaxOhm", "RMin", "RMax"));

            if (!looksLikeResistance)
                continue;

            foreach (ThtTextRow row in table.Rows)
            {
                string enabledText = row.Get(
                    "사용", "사용여부", "Enable", "Enabled", "Use");

                if (!string.IsNullOrWhiteSpace(enabledText) &&
                    IsExplicitFalse(enabledText))
                    continue;

                string minText = row.Get(
                    "최소", "최소값", "Min", "MinOhm", "RMin", "저항Min");
                string maxText = row.Get(
                    "최대", "최대값", "Max", "MaxOhm", "RMax", "저항Max");

                if (!TryParseFlexibleDouble(minText, out double minOhm) ||
                    !TryParseFlexibleDouble(maxText, out double maxOhm) ||
                    minOhm < 0 || maxOhm < minOhm)
                    continue;

                string channelText = row.Get(
                    "채널", "Channel", "Ch", "No", "번호");
                int channel = int.TryParse(channelText, out int parsedChannel) && parsedChannel > 0
                    ? parsedChannel
                    : result.Count + 1;

                string name = row.Get(
                    "이름", "명칭", "Name", "Step", "항목", "Item");
                if (string.IsNullOrWhiteSpace(name))
                    name = $"R{channel}";

                string routeA = row.Get(
                    "RouteA", "경로A", "출력A", "OutputA", "CommandA");
                string routeB = row.Get(
                    "RouteB", "경로B", "출력B", "OutputB", "CommandB");

                result.Add(new ResistanceStep(
                    name.Trim(),
                    channel,
                    minOhm,
                    maxOhm,
                    routeA.Trim(),
                    routeB.Trim()));
            }
        }

        return result
            .GroupBy(step => new { step.Name, step.Channel })
            .Select(group => group.First())
            .OrderBy(step => step.Channel)
            .ToList();
    }

    private static bool HeaderMatches(string normalizedHeader, params string[] aliases)
    {
        return aliases
            .Select(NormalizeHeader)
            .Any(alias =>
                normalizedHeader.Equals(alias, StringComparison.OrdinalIgnoreCase) ||
                normalizedHeader.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExplicitFalse(string value)
    {
        string text = value.Trim();
        return text.Equals("0", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("N", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("NO", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("OFF", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("미사용", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseFlexibleDouble(string value, out double result)
    {
        value = (value ?? string.Empty)
            .Replace("Ω", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("ohm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return double.TryParse(
                   value,
                   NumberStyles.Float | NumberStyles.AllowThousands,
                   CultureInfo.InvariantCulture,
                   out result) ||
               double.TryParse(
                   value,
                   NumberStyles.Float | NumberStyles.AllowThousands,
                   CultureInfo.CurrentCulture,
                   out result);
    }

    private static void ReadPartInformation(
        ThtTextTable partTable,
        ProductModel model)
    {
        ThtTextRow? row = partTable.Rows.FirstOrDefault();

        if (row is null)
        {
            return;
        }

        // Ánh xạ đúng theo tên cột THT, không xóa ô trống rồi lấy theo vị trí.
        model.PartNumber = row.Get(
            "파트번호",
            "PartNumber",
            "KicPno");

        model.ProductName = row.Get(
            "파트명",
            "PartName",
            "ProductName");

        model.Eco = row.Get(
            "ECO",
            "ＥＣＯ",
            "Eco",
            "VehicleType");

        model.Nco = row.Get(
            "NCO",
            "ＮＣＯ",
            "Nco");

        model.Alc = row.Get(
            "ALC",
            "ＡＬＣ",
            "Alc",
            "CustomerCode");

        // Giữ tương thích các binding cũ trên TestWindow.
        model.VehicleType = model.Eco;
        model.CustomerCode = model.Alc;
    }

    private static Dictionary<string, WireDefinition> ParseWireDefinitions(
        ThtTextTable wireTable)
    {
        var result = new Dictionary<string, WireDefinition>(
            StringComparer.OrdinalIgnoreCase);

        foreach (ThtTextRow row in wireTable.Rows)
        {
            string name = NormalizeWireIdentity(row.Get(
                "선이름",
                "WireName",
                "Circuit"));

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var candidate = new WireDefinition(
                Name: name,
                LinkedWire: NormalizeWireIdentity(row.Get("선연결", "PinLink", "LinkedWire")),
                Section: row.Get("굵기", "Section"),
                Color: row.Get("색깔", "Color"),
                WireType: row.Get("선종류", "WireType"),
                WireOrder: row.Get("선차수", "WireOrder"),
                WireOption: row.Get("선옵션", "WireOption"),
                Remark: row.Get("비고", "Remark"));

            if (!result.TryGetValue(name, out WireDefinition? current) ||
                candidate.InformationScore > current.InformationScore)
            {
                result[name] = candidate;
            }
        }

        return result;
    }

    private static List<RawPin> ParsePins(
        ThtTextTable pinTable)
    {
        var result = new List<RawPin>();
        string lastConnector = string.Empty;

        foreach (ThtTextRow row in pinTable.Rows)
        {
            string ioText = row.Get(
                "I/O",
                "IONo",
                "Ｉ／Ｏ");

            if (!int.TryParse(ioText, out int ioNumber) ||
                ioNumber <= 0 ||
                ioNumber > 65_535)
            {
                continue;
            }

            string connector = row.Get(
                "커넥터",
                "Connector");

            string wireName = row.Get(
                "선이름",
                "WireName",
                "Circuit");

            string pinNumber = row.Get(
                "핀번호",
                "PinNo");

            string pinType = row.Get(
                "핀종류",
                "Type1",
                "PinType");

            if (!string.IsNullOrWhiteSpace(connector))
            {
                lastConnector = connector;
            }
            else if (!IsSpecialA0Pin(pinType) &&
                     (!string.IsNullOrWhiteSpace(wireName) ||
                      !string.IsNullOrWhiteSpace(pinNumber)))
            {
                connector = lastConnector;
            }

            result.Add(new RawPin
            {
                Connector = connector,
                WireName = wireName,
                IoNumber = ioNumber,
                PinNumber = pinNumber,
                PinType = pinType,
                PinOrder = row.Get("핀차수", "TestOrder", "PinOrder"),
                PinOption = row.Get("핀옵션", "Option", "PinOption"),
                Remark = row.Get("비고", "Remark")
            });
        }

        return result;
    }

    /// <summary>
    /// 선연결 là quan hệ hai chiều. Không follow link theo một chiều vì model
    /// production có MC2 -> P1 và P1 -> MC2; cách cũ tạo cycle và tách sai net.
    /// </summary>
    private static Dictionary<string, string> BuildCanonicalWireMap(
        IReadOnlyDictionary<string, WireDefinition> definitions)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var order = definitions.Keys
            .Select((name, index) => new { Name = name, Index = index })
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);

        void EnsureNode(string name)
        {
            if (!adjacency.ContainsKey(name))
                adjacency[name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var pair in definitions)
        {
            string name = NormalizeWireIdentity(pair.Key);
            EnsureNode(name);

            string linked = NormalizeWireIdentity(pair.Value.LinkedWire);
            if (string.IsNullOrWhiteSpace(linked))
                continue;

            EnsureNode(linked);
            adjacency[name].Add(linked);
            adjacency[linked].Add(name);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string start in adjacency.Keys)
        {
            if (!visited.Add(start))
                continue;

            var component = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                component.Add(current);

                foreach (string next in adjacency[current])
                {
                    if (visited.Add(next))
                        queue.Enqueue(next);
                }
            }

            string canonical = component
                .OrderBy(name => order.TryGetValue(name, out int index) ? index : int.MaxValue)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .First();

            foreach (string name in component)
                result[name] = canonical;
        }

        return result;
    }

    private static WireDefinition? GetWireDefinition(
        string wireName,
        IReadOnlyDictionary<string, WireDefinition> definitions)
    {
        return definitions.TryGetValue(
            NormalizeWireIdentity(wireName),
            out WireDefinition? value)
            ? value
            : null;
    }

    // =====================================================================
    // QUY TẮC CLIP A0/AO - aN
    // =====================================================================

    private static ClipTopology? BuildClipTopology(
        ProductModel model,
        IReadOnlyCollection<RawPin> specialPins,
        IReadOnlyCollection<ParsedPin> normalPins,
        IReadOnlyDictionary<RawPin, PinRecord> pinByRaw)
    {
        RawPin? commonRaw = specialPins.FirstOrDefault(pin => IsCommonA0(pin.PinType));
        if (commonRaw is null)
            return null;

        RawPin[] branchRows = specialPins
            .Where(pin => TryGetBranchNumber(pin.PinType, out _))
            .OrderBy(pin => GetBranchNumber(pin.PinType))
            .ThenBy(pin => pin.IoNumber)
            .ToArray();

        if (branchRows.Length == 0)
            return null;

        Dictionary<int, PinRecord> normalByIo = normalPins
            .Select(item => item.Pin)
            .GroupBy(pin => pin.IoNumber)
            .ToDictionary(group => group.Key, group => group.First());

        PinRecord commonPin = pinByRaw.GetValueOrDefault(commonRaw) ??
            FindModelPinForRaw(model, commonRaw) ?? new PinRecord(
            FirstNotEmpty(commonRaw.Connector, "CLIP"),
            FirstNotEmpty(commonRaw.WireName, "CLIP-A0"),
            commonRaw.IoNumber,
            FirstNotEmpty(commonRaw.PinNumber, "A0"),
            "CLIP COMMON");

        AddModelPinIfMissing(model, commonPin);

        var branches = new List<ClipBranch>();
        var seenBranch = new HashSet<(int BranchNumber, int TargetIo)>();

        foreach (RawPin raw in branchRows)
        {
            if (!TryGetBranchNumber(raw.PinType, out int branchNumber))
                continue;

            // Điểm quan trọng: a1/a2/a3... chỉ là tên nhánh CLIP.
            // I/O đích phải lấy từ CỘT I/O của row aN, không lấy số N.
            int targetIo = raw.IoNumber;
            if (targetIo <= 0 || targetIo > 65_535)
                continue;

            if (!seenBranch.Add((branchNumber, targetIo)))
                continue;

            string branchName = $"a{branchNumber}";
            PinRecord clipPin = pinByRaw.GetValueOrDefault(raw) ??
                FindModelPinForRaw(model, raw) ?? new PinRecord(
                FirstNotEmpty(raw.Connector, "CLIP"),
                FirstNotEmpty(raw.WireName, $"CLIP-{branchName.ToUpperInvariant()}"),
                targetIo,
                FirstNotEmpty(raw.PinNumber, branchName),
                $"{branchName} -> I/O {targetIo}");

            AddModelPinIfMissing(model, clipPin);
            normalByIo.TryGetValue(targetIo, out PinRecord? targetPin);

            branches.Add(new ClipBranch(
                branchName,
                branchNumber,
                targetIo,
                clipPin,
                targetPin));
        }

        return branches.Count == 0
            ? null
            : new ClipTopology(commonPin, branches);
    }

    private static PinRecord? FindModelPinForRaw(ProductModel model, RawPin raw)
    {
        PinRecord[] sameIo = model.Pins
            .Where(pin => pin.IoNumber == raw.IoNumber)
            .ToArray();

        if (sameIo.Length == 0)
            return null;

        return sameIo.FirstOrDefault(pin =>
                   string.Equals(pin.PinNumber, raw.PinNumber, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(pin.Connector, raw.Connector, StringComparison.OrdinalIgnoreCase))
               ?? sameIo[0];
    }

    private static void AddModelPinIfMissing(
        ProductModel model,
        PinRecord pin)
    {
        if (model.Pins.All(existing =>
                existing.IoNumber != pin.IoNumber))
        {
            model.Pins.Add(pin);
        }
    }

    private static bool IsSpecialA0Pin(string pinType)
    {
        return IsCommonA0(pinType) ||
               TryGetBranchNumber(pinType, out _);
    }

    private static bool IsCommonA0(string pinType)
    {
        string normalized = NormalizeSpecialPinName(pinType);

        return normalized.Equals(
                   "a0",
                   StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(
                   "ao",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetBranchNumber(
        string pinType,
        out int branchNumber)
    {
        // IMPORTANT: an out parameter must be assigned on every return path.
        // Do not put the first assignment behind short-circuit && because when
        // Match.Success is false, TryParse is skipped and branchNumber would
        // remain unassigned (CS0177).
        branchNumber = 0;

        Match match = BranchPinRegex.Match(
            NormalizeSpecialPinName(pinType));

        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["number"].Value, out int parsedBranchNumber))
        {
            return false;
        }

        if (parsedBranchNumber is <= 0 or > 65_535)
        {
            return false;
        }

        branchNumber = parsedBranchNumber;
        return true;
    }

    private static int GetBranchNumber(string pinType)
    {
        return TryGetBranchNumber(pinType, out int number)
            ? number
            : int.MaxValue;
    }

    private static string NormalizeSpecialPinName(string value)
    {
        return (value ?? string.Empty)
            .Normalize(NormalizationForm.FormKC)
            .Trim()
            .Replace('０', '0')
            .Replace('Ｏ', 'O')
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string CleanCell(string value)
    {
        return new string((value ?? string.Empty)
                .Where(character =>
                    !char.IsControl(character) ||
                    character is '\t' or '\r' or '\n')
                .ToArray())
            .Trim();
    }

    private static string FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(
                   value => !string.IsNullOrWhiteSpace(value))
               ?? string.Empty;
    }

    // =====================================================================
    // CẤU TRÚC NỘI BỘ
    // =====================================================================

    private sealed class ThtArchiveDocument
    {
        public uint OuterLength { get; init; }
        public uint InnerLength { get; init; }
        public uint Flags { get; init; }
        public uint DocumentToken { get; init; }
        public string Version { get; init; } = string.Empty;
        public string ModelText { get; init; } = string.Empty;
        public string EmbeddedResistanceText { get; init; } = string.Empty;
        public IReadOnlyList<ThtFieldRecord> Fields { get; init; } = [];
        public IReadOnlyList<ThtViewRecord> Views { get; init; } = [];
    }

    private sealed class ThtFieldRecord
    {
        public int Index { get; init; }
        public byte Group { get; init; }
        public string Name { get; init; } = string.Empty;
        public uint Reserved { get; init; }
        public uint ColumnWidth { get; init; }
        public uint ValueType { get; init; }
        public uint FormatFlags { get; init; }
    }

    private sealed class ThtViewRecord
    {
        public uint ViewId { get; init; }
        public string ViewName { get; init; } = string.Empty;
        public uint ObjectGroup { get; init; }
        public uint TypeMask { get; init; }
        public uint Subview { get; init; }
        public IReadOnlyList<uint> FieldIds { get; init; } = [];
    }

    private sealed class ThtModelTables
    {
        public ThtTextTable Part { get; init; } = new();
        public ThtTextTable Connectors { get; init; } = new();
        public ThtTextTable Pins { get; init; } = new();
        public ThtTextTable Wires { get; init; } = new();
        public IReadOnlyList<ThtTextTable> AllTables { get; init; } = [];
    }

    private sealed class ThtTextTable
    {
        public IReadOnlyList<string> Headers { get; init; } = [];
        public IReadOnlyList<ThtTextRow> Rows { get; init; } = [];
    }

    private sealed class ThtTextRow
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public ThtTextRow(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public string Get(params string[] names)
        {
            foreach (string name in names)
            {
                string normalized = NormalizeHeader(name);

                if (_values.TryGetValue(
                        normalized,
                        out string? value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }

    private sealed class RawPin
    {
        public string Connector { get; init; } = string.Empty;
        public string WireName { get; init; } = string.Empty;
        public int IoNumber { get; init; }
        public string PinNumber { get; init; } = string.Empty;
        public string PinType { get; init; } = string.Empty;
        public string PinOrder { get; init; } = string.Empty;
        public string PinOption { get; init; } = string.Empty;
        public string Remark { get; init; } = string.Empty;

        public int InformationScore =>
            (!string.IsNullOrWhiteSpace(Connector) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(WireName) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(PinNumber) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(PinType) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(PinOrder) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(PinOption) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(Remark) ? 1 : 0);
    }

    private sealed record ParsedPin(
        PinRecord Pin,
        string NetName);

    private sealed record WireDefinition(
        string Name,
        string LinkedWire,
        string Section,
        string Color,
        string WireType,
        string WireOrder,
        string WireOption,
        string Remark)
    {
        public int InformationScore =>
            (!string.IsNullOrWhiteSpace(LinkedWire) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(Section) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(Color) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(WireType) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(WireOrder) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(WireOption) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(Remark) ? 1 : 0);
    }

    // =====================================================================
    // OLE COMPOUND FILE READER - KHÔNG CẦN NUGET
    // =====================================================================

    private sealed class CompoundFileReader
    {
        private const uint FreeSector = 0xFFFFFFFF;
        private const uint EndOfChain = 0xFFFFFFFE;
        private const uint FatSector = 0xFFFFFFFD;
        private const uint DifatSector = 0xFFFFFFFC;

        private static readonly byte[] CompoundSignature =
            [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

        private readonly byte[] _file;
        private readonly int _sectorSize;
        private readonly int _miniSectorSize;
        private readonly uint _miniStreamCutoff;
        private readonly uint[] _fat;
        private readonly uint[] _miniFat;
        private readonly List<DirectoryEntry> _directoryEntries;
        private readonly byte[] _rootMiniStream;

        public CompoundFileReader(string path)
        {
            _file = ReadAllBytesSharedWithRetry(path);

            if (_file.Length < 512 ||
                !_file.AsSpan(0, 8).SequenceEqual(CompoundSignature))
            {
                throw new InvalidDataException(
                    "File không phải OLE Compound Document hợp lệ.");
            }

            ushort byteOrder = ReadUInt16(28);

            if (byteOrder != 0xFFFE)
            {
                throw new InvalidDataException(
                    "OLE Compound Document không dùng little-endian.");
            }

            ushort sectorShift = ReadUInt16(30);
            ushort miniSectorShift = ReadUInt16(32);

            _sectorSize = 1 << sectorShift;
            _miniSectorSize = 1 << miniSectorShift;

            if (_sectorSize is not (512 or 4096) ||
                _miniSectorSize != 64)
            {
                throw new InvalidDataException(
                    "Kích thước sector OLE không được hỗ trợ.");
            }

            uint numberOfFatSectors = ReadUInt32(44);
            uint firstDirectorySector = ReadUInt32(48);
            _miniStreamCutoff = ReadUInt32(56);
            uint firstMiniFatSector = ReadUInt32(60);
            uint numberOfMiniFatSectors = ReadUInt32(64);
            uint firstDifatSector = ReadUInt32(68);
            uint numberOfDifatSectors = ReadUInt32(72);

            List<uint> fatSectorIds = ReadDifat(
                numberOfFatSectors,
                firstDifatSector,
                numberOfDifatSectors);

            _fat = ReadAllocationTable(fatSectorIds);

            byte[] directoryBytes = ReadRegularChain(
                firstDirectorySector,
                expectedLength: null);

            _directoryEntries = ParseDirectory(directoryBytes);

            DirectoryEntry root = _directoryEntries.FirstOrDefault(
                entry => entry.Type == 5)
                ?? throw new InvalidDataException(
                    "OLE storage không có Root Entry.");

            _rootMiniStream = root.StreamSize == 0
                ? []
                : ReadRegularChain(
                    root.StartSector,
                    checked((long)root.StreamSize));

            _miniFat = ReadMiniFat(
                firstMiniFatSector,
                numberOfMiniFatSectors);
        }



        private static byte[] ReadAllBytesSharedWithRetry(string path)
        {
            Exception? lastError = null;

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        bufferSize: 64 * 1024,
                        options: FileOptions.SequentialScan);

                    if (stream.Length > int.MaxValue)
                    {
                        throw new InvalidDataException(
                            "File THT/THA quá lớn để đọc.");
                    }

                    var result = new byte[checked((int)stream.Length)];
                    stream.ReadExactly(result);
                    return result;
                }
                catch (IOException ex)
                {
                    lastError = ex;

                    if (attempt < 4)
                        Thread.Sleep(75);
                }
            }

            throw new IOException(
                $"Không thể đọc file THT/THA '{path}'. File có thể đang bị chương trình khác giữ hoặc chưa copy xong.",
                lastError);
        }

        public byte[] ReadStream(string streamName)
        {
            DirectoryEntry? entry = _directoryEntries.FirstOrDefault(
                item => item.Type == 2 &&
                        item.Name.Equals(
                            streamName,
                            StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                throw new InvalidDataException(
                    $"OLE storage không có stream '{streamName}'.");
            }

            if (entry.StreamSize == 0)
            {
                return [];
            }

            if (entry.StreamSize < _miniStreamCutoff)
            {
                return ReadMiniChain(
                    entry.StartSector,
                    checked((long)entry.StreamSize));
            }

            return ReadRegularChain(
                entry.StartSector,
                checked((long)entry.StreamSize));
        }

        private List<uint> ReadDifat(
            uint numberOfFatSectors,
            uint firstDifatSector,
            uint numberOfDifatSectors)
        {
            var result = new List<uint>();

            for (int index = 0; index < 109; index++)
            {
                uint sectorId = ReadUInt32(76 + index * 4);

                if (IsNormalSector(sectorId))
                {
                    result.Add(sectorId);
                }
            }

            uint currentDifatSector = firstDifatSector;
            int entriesPerDifatSector = _sectorSize / 4 - 1;

            for (uint index = 0;
                 index < numberOfDifatSectors &&
                 IsNormalSector(currentDifatSector);
                 index++)
            {
                ReadOnlySpan<byte> sector = GetSector(currentDifatSector);

                for (int entry = 0;
                     entry < entriesPerDifatSector;
                     entry++)
                {
                    uint fatSectorId =
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            sector.Slice(entry * 4, 4));

                    if (IsNormalSector(fatSectorId))
                    {
                        result.Add(fatSectorId);
                    }
                }

                currentDifatSector =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        sector.Slice(_sectorSize - 4, 4));
            }

            if ((uint)result.Count < numberOfFatSectors)
            {
                throw new InvalidDataException(
                    "OLE DIFAT không đủ số FAT sector đã khai báo.");
            }

            return result
                .Take(checked((int)numberOfFatSectors))
                .ToList();
        }

        private uint[] ReadAllocationTable(
            IReadOnlyList<uint> sectorIds)
        {
            var entries = new List<uint>(
                sectorIds.Count * (_sectorSize / 4));

            foreach (uint sectorId in sectorIds)
            {
                ReadOnlySpan<byte> sector = GetSector(sectorId);

                for (int offset = 0;
                     offset < _sectorSize;
                     offset += 4)
                {
                    entries.Add(
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            sector.Slice(offset, 4)));
                }
            }

            return entries.ToArray();
        }

        private uint[] ReadMiniFat(
            uint firstMiniFatSector,
            uint numberOfMiniFatSectors)
        {
            if (numberOfMiniFatSectors == 0 ||
                !IsNormalSector(firstMiniFatSector))
            {
                return [];
            }

            byte[] bytes = ReadRegularChain(
                firstMiniFatSector,
                checked((long)numberOfMiniFatSectors * _sectorSize));

            var entries = new uint[bytes.Length / 4];

            for (int index = 0; index < entries.Length; index++)
            {
                entries[index] =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(index * 4, 4));
            }

            return entries;
        }

        private byte[] ReadRegularChain(
            uint startSector,
            long? expectedLength)
        {
            if (!IsNormalSector(startSector))
            {
                return [];
            }

            using var output = new MemoryStream();
            var visited = new HashSet<uint>();
            uint current = startSector;

            while (IsNormalSector(current))
            {
                if (!visited.Add(current))
                {
                    throw new InvalidDataException(
                        "OLE FAT chain bị lặp vòng.");
                }

                ReadOnlySpan<byte> sector = GetSector(current);
                output.Write(sector);

                if (expectedLength is long length &&
                    output.Length >= length)
                {
                    break;
                }

                if (current >= (uint)_fat.Length)
                {
                    throw new InvalidDataException(
                        "OLE FAT chain vượt ngoài bảng FAT.");
                }

                uint next = _fat[current];

                if (next == EndOfChain)
                {
                    break;
                }

                if (next is FreeSector or FatSector or DifatSector)
                {
                    throw new InvalidDataException(
                        "OLE FAT chain chứa sector đặc biệt không hợp lệ.");
                }

                current = next;
            }

            byte[] result = output.ToArray();

            if (expectedLength is long expected)
            {
                if (result.LongLength < expected)
                {
                    throw new EndOfStreamException(
                        "OLE stream ngắn hơn kích thước đã khai báo.");
                }

                if (result.LongLength > expected)
                {
                    Array.Resize(ref result, checked((int)expected));
                }
            }

            return result;
        }

        private byte[] ReadMiniChain(
            uint startMiniSector,
            long expectedLength)
        {
            if (_miniFat.Length == 0 ||
                _rootMiniStream.Length == 0)
            {
                throw new InvalidDataException(
                    "OLE mini stream chưa được khởi tạo.");
            }

            using var output = new MemoryStream();
            var visited = new HashSet<uint>();
            uint current = startMiniSector;

            while (IsNormalSector(current))
            {
                if (!visited.Add(current))
                {
                    throw new InvalidDataException(
                        "OLE MiniFAT chain bị lặp vòng.");
                }

                long miniOffset =
                    (long)current * _miniSectorSize;

                if (miniOffset < 0 ||
                    miniOffset + _miniSectorSize > _rootMiniStream.LongLength)
                {
                    throw new InvalidDataException(
                        "OLE mini sector vượt ngoài root mini stream.");
                }

                output.Write(
                    _rootMiniStream,
                    checked((int)miniOffset),
                    _miniSectorSize);

                if (output.Length >= expectedLength)
                {
                    break;
                }

                if (current >= (uint)_miniFat.Length)
                {
                    throw new InvalidDataException(
                        "OLE MiniFAT chain vượt ngoài bảng MiniFAT.");
                }

                uint next = _miniFat[current];

                if (next == EndOfChain)
                {
                    break;
                }

                if (next == FreeSector)
                {
                    throw new InvalidDataException(
                        "OLE MiniFAT chain kết thúc không hợp lệ.");
                }

                current = next;
            }

            byte[] result = output.ToArray();

            if (result.LongLength < expectedLength)
            {
                throw new EndOfStreamException(
                    "OLE mini stream ngắn hơn kích thước đã khai báo.");
            }

            if (result.LongLength > expectedLength)
            {
                Array.Resize(ref result, checked((int)expectedLength));
            }

            return result;
        }

        private List<DirectoryEntry> ParseDirectory(byte[] bytes)
        {
            var result = new List<DirectoryEntry>();

            for (int offset = 0;
                 offset + 128 <= bytes.Length;
                 offset += 128)
            {
                ReadOnlySpan<byte> record = bytes.AsSpan(offset, 128);
                ushort nameLength =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        record.Slice(64, 2));

                byte type = record[66];

                if (type == 0)
                {
                    continue;
                }

                int actualNameByteLength = Math.Clamp(
                    nameLength >= 2 ? nameLength - 2 : 0,
                    0,
                    64);

                string name = actualNameByteLength == 0
                    ? string.Empty
                    : Encoding.Unicode
                        .GetString(record.Slice(0, actualNameByteLength))
                        .TrimEnd('\0');

                result.Add(new DirectoryEntry
                {
                    Name = name,
                    Type = type,
                    StartSector =
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            record.Slice(116, 4)),
                    StreamSize =
                        BinaryPrimitives.ReadUInt64LittleEndian(
                            record.Slice(120, 8))
                });
            }

            return result;
        }

        private ReadOnlySpan<byte> GetSector(uint sectorId)
        {
            long offset =
                ((long)sectorId + 1) * _sectorSize;

            if (offset < 0 ||
                offset + _sectorSize > _file.LongLength)
            {
                throw new InvalidDataException(
                    $"OLE sector {sectorId} vượt ngoài file.");
            }

            return _file.AsSpan(
                checked((int)offset),
                _sectorSize);
        }

        private ushort ReadUInt16(int offset)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(
                _file.AsSpan(offset, 2));
        }

        private uint ReadUInt32(int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(
                _file.AsSpan(offset, 4));
        }

        private static bool IsNormalSector(uint sectorId)
        {
            return sectorId < DifatSector;
        }

        private sealed class DirectoryEntry
        {
            public string Name { get; init; } = string.Empty;
            public byte Type { get; init; }
            public uint StartSector { get; init; }
            public ulong StreamSize { get; init; }
        }
    }
}

/// <summary>
/// Thông tin kiểm tra nhanh sau khi nạp THT. Không ảnh hưởng giao diện hiện tại.
/// </summary>
public sealed class ThtLoadDiagnostics
{
    public string Version { get; init; } = string.Empty;
    public uint DocumentToken { get; init; }
    public int PartRowCount { get; init; }
    public int ConnectorRowCount { get; init; }
    public int PinRowCount { get; init; }
    public int WireRowCount { get; init; }
    public int FieldCount { get; init; }
    public int ViewCount { get; init; }
    public int ParsedPinCount { get; init; }
    public int ParsedNetworkCount { get; init; }
    public int ParsedResistanceStepCount { get; init; }
}