using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Text;
using JBZUniversalTester.ModelEditor.Models;
using System.IO;
namespace JBZUniversalTester.ModelEditor.Services;

public static class ThtFileService
{
    private const uint Free = 0xFFFFFFFF;
    private const uint Eoc = 0xFFFFFFFE;
    private const uint FatSect = 0xFFFFFFFD;
    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int MiniStreamCutoff = 4096;
    public const string ThtVersion = "V1.110";
    public const uint ThtToken = 0x389DEFB9;

    private static readonly string[] ProductHeaders = ["검사", "파트번호", "ＥＣＯ", "ＡＬＣ", "파트명", "옵 션", "비 고", "변 수", "저항식", "정 보", "라 벨"];
    private static readonly string[] ConnectorHeaders = ["번 호", "커넥터", "핀 수", "외부 접속", "연결핀", "컨차수", "옵 션", "비 고", "방 수"];
    private static readonly string[] PinHeaders = ["커넥터", "선이름", "Ｉ／Ｏ", "핀번호", "핀종류", "핀차수", "핀옵션", "비 고"];
    private static readonly string[] WireHeaders = ["선이름", "선연결", "굵 기", "색 깔", "선종류", "선차수", "선옵션", "비 고"];
    // Layout 17 chân của WH322244 có catalog AO,a1..a16. a17 chỉ xuất
    // hiện ở special PIN row IO208, không có wire-catalog row a17.
    private static readonly string[] Reference17ClipWireLabels =
        ["AO", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8", "a9",
         "a10", "a11", "a12", "a13", "a14", "a15", "a16"];

    static ThtFileService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static ModelDocument Load(string path)
    {
        var source = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!File.Exists(source)) throw new FileNotFoundException(source);

        // Giữ nguyên file nguồn để Save THT có thể bảo toàn container HarnProg.
        var sourceBytes = File.ReadAllBytes(source);
        var ole = new CompoundFileReader(sourceBytes);
        var contents = ole.ReadStream("Contents");
        var parsed = ParseContents(contents);
        var tables = ParseModelText(parsed.Text);
        byte[] summary;
        try { summary = ole.ReadStream("\u0005SummaryInformation"); }
        catch (ThtFormatException) { summary = Asset("summary_information.bin"); }

        var clipRowMap = ClipRows(tables.PinRows);
        var clipRowIndices = clipRowMap.Keys.ToHashSet();
        var clipIos = clipRowMap.Values.Distinct().OrderBy(x => x).ToList();
        int? clipStart = clipIos.Count > 0 ? clipIos[0] : null;
        int? clipEnd = clipIos.Count > 0 ? clipIos[^1] : null;

        var wireLookup = WireByName(tables.WireRows);
        var pins = new List<Pin>();
        var iosByConnector = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var displayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < tables.PinRows.Count; i++)
        {
            if (clipRowIndices.Contains(i)) continue;
            var row = Fit(tables.PinRows[i], PinHeaders.Length);
            var connectorName = row[0].Trim();
            if (!int.TryParse(row[2].Trim(), out var io) || !int.TryParse(row[3].Trim(), out var pinNo)) continue;
            if (string.IsNullOrEmpty(connectorName) || io <= 0 || pinNo <= 0) continue;
            var wire = wireLookup.TryGetValue(row[1].Trim(), out var w) ? w : Enumerable.Repeat("", WireHeaders.Length).ToList();
            pins.Add(new Pin { Io = io, Connector = connectorName, PinNo = pinNo, Line = row[1], Add = wire[1], Thickness = wire[2], Color = wire[3], TestNo = row[4], SourceThtRowIndex = i });
            if (!iosByConnector.TryGetValue(connectorName, out var list)) iosByConnector[connectorName] = list = [];
            list.Add(io);
            if (!displayName.ContainsKey(connectorName)) displayName[connectorName] = connectorName;
        }

        var clipPins = new List<Pin>();
        if (clipStart.HasValue && clipEnd.HasValue)
        {
            var byIo = clipRowMap.ToDictionary(kv => kv.Value, kv => Fit(tables.PinRows[kv.Key], PinHeaders.Length));
            for (var io = clipStart.Value; io <= clipEnd.Value; io++)
            {
                var raw = byIo[io];
                var localPin = io - clipStart.Value + 1;
                var rawLabel = raw[4].Trim();
                var displayLabel = rawLabel.Length > 0 ? rawLabel : (localPin == 1 ? "AO" : $"a{localPin - 1}");
                clipPins.Add(new Pin { Io = io, Connector = "CLIP", PinNo = localPin, Line = displayLabel, TestNo = displayLabel });
            }
            pins.AddRange(clipPins);
        }

        var hasDuplicateIo = pins.GroupBy(p => p.Io).Any(g => g.Count() > 1);
        pins = pins.OrderBy(p => p.Io).ToList();
        // HarnProg cho phép mở dữ liệu có I/O trùng để sửa. Không dựng liên kết
        // khi một số I/O còn đại diện cho nhiều chân khác nhau.
        if (!hasDuplicateIo)
            ModelLogic.RebuildConnections(pins);
        if (clipPins.Count > 0)
        {
            var clipByIo = pins.Where(p => IsClipConnector(p.Connector)).ToDictionary(p => p.Io);
            var common = clipByIo.Values.FirstOrDefault(p => IsClipCommonLabel(p.TestNo)) ?? clipByIo[clipByIo.Keys.Min()];
            var branches = clipByIo.OrderBy(kv => kv.Key).Where(kv => kv.Key != common.Io).Select(kv => kv.Value).ToList();
            common.MainIo = -1; common.Coupling = branches.Select(p => p.Io).ToList();
            foreach (var branch in branches) { branch.MainIo = common.Io; branch.Coupling.Clear(); }
        }

        var connectors = new List<Connector>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var original in tables.ConnectorRows)
        {
            var row = Fit(original, ConnectorHeaders.Length); var name = row[1].Trim();
            if (string.IsNullOrEmpty(name) || !iosByConnector.TryGetValue(name, out var actual)) continue;
            connectors.Add(new Connector { Name = name, PinCount = actual.Count, StartIo = actual.Min(), Waterproof = BoolText(row[8]) });
            added.Add(name);
        }
        foreach (var pair in iosByConnector)
        {
            if (added.Contains(pair.Key)) continue;
            connectors.Add(new Connector { Name = displayName[pair.Key], PinCount = pair.Value.Count, StartIo = pair.Value.Min() });
        }
        if (clipStart.HasValue && clipEnd.HasValue)
            connectors.Add(new Connector { Name = "CLIP", PinCount = clipEnd.Value - clipStart.Value + 1, StartIo = clipStart.Value });
        connectors = connectors.OrderBy(c => c.StartIo).ToList();

        LeakLayoutService.Load(source, connectors);
        var state = new ThtState
        {
            Version = parsed.Version,
            Token = parsed.Token,
            ProductRow = [.. tables.Product],
            ConnectorRows = tables.ConnectorRows.Select(r => new List<string>(r)).ToList(),
            PinRows = tables.PinRows.Select(r => new List<string>(r)).ToList(),
            WireRows = tables.WireRows.Select(r => new List<string>(r)).ToList(),
            Tail = parsed.Tail,
            SummaryInformation = summary,
            OriginalFileBytes = [.. sourceBytes],
            OriginalContents = [.. contents]
        };
        return new ModelDocument
        {
            Base = new BaseInfo { Model = Path.GetFileNameWithoutExtension(source), Number = tables.Product[1], Name = tables.Product[4], Kind = tables.Product[2], EoNo = tables.Product[5], Customer = tables.Product[3] },
            Connectors = connectors,
            Pins = pins,
            SourcePath = source,
            SourceFormat = "tht",
            ThtState = state
        };
    }

    public static string Export(ModelDocument document, string path) => Write(document, path, false);
    public static string Save(ModelDocument document, string path)
    {
        var target = Write(document, path, true);
        var reopened = Load(target);
        var reopenedByIdentity = reopened.Pins.ToDictionary(
            pin => $"{pin.Connector.ToUpperInvariant()}\u0001{pin.PinNo}");
        foreach (var pin in document.Pins)
        {
            var identity = $"{pin.Connector.ToUpperInvariant()}\u0001{pin.PinNo}";
            if (reopenedByIdentity.TryGetValue(identity, out var savedPin))
                pin.SourceThtRowIndex = savedPin.SourceThtRowIndex;
        }
        document.ThtState = reopened.ThtState;
        document.SourcePath = target;
        document.SourceFormat = "tht";
        return target;
    }

    public static void VerifyExport(string path)
    {
        var contents = ReadContentsForTest(path);
        _ = ParseContents(contents);
    }

    public static byte[] ReadContentsForTest(string path) => new CompoundFileReader(File.ReadAllBytes(path)).ReadStream("Contents");
    public static string ModelToThtText(ModelDocument document)
    {
        // Đảm bảo model mới tạo bằng UI có đúng quan hệ AO -> aN trước khi
        // chuyển sang representation đặc biệt của HarnProg.
        ModelLogic.NormalizeHarnProgClip(document.Pins);
        ValidateThtTextFields(document);
        var product = MergeProduct(document); var connectors = MergeConnectors(document); var pins = MergePins(document); var wires = MergeWires(document);
        ValidateClipPinTypeColumn(document, pins);
        var lines = new List<string>();
        void Section(string[] headers, IEnumerable<List<string>> rows)
        {
            lines.Add(string.Join('\t', headers) + "\t");
            foreach (var row in rows) lines.Add(string.Join('\t', Fit(row, headers.Length)) + "\t");
        }
        Section(ProductHeaders, [product]); lines.Add("");
        Section(ConnectorHeaders, connectors); lines.Add("");
        Section(PinHeaders, pins); lines.Add("");
        Section(WireHeaders, wires); lines.Add("");
        return string.Join('\n', lines) + "\n";
    }

    private static void ValidateThtTextFields(ModelDocument document)
    {
        static void Reject(string label, string? value)
        {
            if (value?.IndexOfAny(['\t', '\r', '\n']) >= 0)
                throw new ValidationException($"{label} chứa ký tự tab/xuống dòng không hỗ trợ trong file THT.");
        }

        Reject("Tên model", document.Base.Model);
        Reject("Mã hàng", document.Base.Number);
        Reject("Tên hàng", document.Base.Name);
        Reject("Loại xe", document.Base.Kind);
        Reject("EO No", document.Base.EoNo);
        Reject("ALC", document.Base.Customer);
        foreach (var connector in document.Connectors)
            Reject($"Tên connector {connector.Name}", connector.Name);
        foreach (var pin in document.Pins)
        {
            Reject($"Connector tại I/O {pin.Io}", pin.Connector);
            Reject($"Tên dây tại I/O {pin.Io}", pin.Line);
            Reject($"Dây nối tại I/O {pin.Io}", pin.Add);
            Reject($"Độ dày tại I/O {pin.Io}", pin.Thickness);
            Reject($"Màu tại I/O {pin.Io}", pin.Color);
            Reject($"Lần kiểm tra tại I/O {pin.Io}", pin.TestNo);
        }
    }

    private static string Write(ModelDocument document, string path, bool active)
    {
        ModelLogic.ValidatePinsForSave(document.Connectors, document.Pins);
        var target = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (!target.EndsWith(".tht", StringComparison.OrdinalIgnoreCase))
            target = Path.ChangeExtension(target, ".tht");

        var directory = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ThtExportException("Không xác định được thư mục lưu file THT.");

        Directory.CreateDirectory(directory);

        if (Directory.Exists(target))
            throw new ThtExportException(
                $"Đường dẫn xuất THT đang là một thư mục, không phải file:\n{target}");

        var leakLayout = LeakLayoutService.Prepare(document, target);
        var contents = BuildContents(document);
        var summary =
            document.ThtState is { SummaryInformation.Length: > 0 } state
                ? state.SummaryInformation
                : Asset("summary_information.bin");
        byte[] raw;

        if (document.ThtState is
            {
                OriginalFileBytes.Length: > 0,
                OriginalContents.Length: > 0
            } originalState)
        {
            // Nếu dữ liệu logic không thay đổi, trả lại CHÍNH XÁC file gốc.
            // Đây là kiểm tra round-trip nghiêm ngặt nhất.
            raw = contents.AsSpan().SequenceEqual(originalState.OriginalContents)
                ? [.. originalState.OriginalFileBytes]
                : PatchContentsPreservingOriginalContainer(
                    originalState.OriginalFileBytes,
                    contents);
        }
        else
        {
            // MODEL -> THT:
            // dùng container OLE thật do HarnProg V1.110 tạo ra,
            // sau đó chỉ thay stream Contents.
            var harnProgTemplate = Asset("harnprog_template_v1110.tht");
            raw = PatchContentsPreservingOriginalContainer(
                harnProgTemplate,
                contents);
        }

        WriteFileSafely(target, raw);
        LeakLayoutService.Save(target, leakLayout);
        return target;
    }

    private static void WriteFileSafely(string target, byte[] raw)
    {
        var directory = Path.GetDirectoryName(target)!;
        var temp = Path.Combine(
            directory,
            $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");

        try
        {
            // Nếu file đích đã tồn tại và mang thuộc tính ReadOnly,
            // bỏ ReadOnly trước khi ghi đè.
            if (File.Exists(target))
            {
                var attributes = File.GetAttributes(target);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(
                        target,
                        attributes & ~FileAttributes.ReadOnly);
                }
            }

            // Luôn tạo file tạm hoàn chỉnh trước.
            using (var stream = new FileStream(
                       temp,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(raw, 0, raw.Length);
                stream.Flush(flushToDisk: true);
            }

            // Rename/replace sau khi file tạm đã hoàn chỉnh. Nếu file đích đang bị
            // khóa, file cũ vẫn nguyên vẹn thay vì bị truncate trước khi copy xong.
            File.Move(temp, target, overwrite: true);
            temp = string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException(
                "Windows từ chối quyền ghi file THT.\n\n" +
                $"File: {target}\n\n" +
                "Hãy kiểm tra:\n" +
                "• Đóng HarnProg hoặc chương trình khác đang mở file này.\n" +
                "• Bỏ thuộc tính Read-only nếu file/thư mục đang bị khóa.\n" +
                "• Chọn thư mục mà tài khoản Windows có quyền ghi.\n\n" +
                $"Chi tiết: {ex.Message}",
                ex);
        }
        catch (IOException ex)
        {
            throw new IOException(
                "Không thể ghi file THT. File có thể đang được HarnProg hoặc " +
                "một chương trình khác sử dụng, hoặc thư mục không cho phép ghi.\n\n" +
                $"File: {target}\n\n" +
                $"Chi tiết: {ex.Message}",
                ex);
        }
        finally
        {
            if (!string.IsNullOrEmpty(temp))
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch
                {
                    // Không làm che mất lỗi ghi file chính.
                }
            }
        }
    }

    /// <summary>
    /// Vá stream Contents vào chính OLE/CFB của file HarnProg gốc.
    ///
    /// Không dựng lại container:
    /// - giữ nguyên header;
    /// - giữ nguyên Directory tree;
    /// - giữ nguyên SummaryInformation;
    /// - giữ nguyên sector layout hiện hữu nếu đủ;
    /// - chỉ cập nhật FAT chain/stream size khi Contents đổi kích thước.
    ///
    /// Điều này đặc biệt quan trọng với THT legacy của HarnProg.
    /// </summary>
    private static byte[] PatchContentsPreservingOriginalContainer(
        byte[] originalFile,
        byte[] newContents)
    {
        if (originalFile.Length < SectorSize ||
            !originalFile.AsSpan(0, 8)
                .SequenceEqual(Convert.FromHexString("D0CF11E0A1B11AE1")))
        {
            throw new ThtExportException(
                "THT nguồn không phải OLE Compound Document hợp lệ.");
        }

        if (newContents.Length < MiniStreamCutoff)
        {
            throw new ThtExportException(
                "Contents THT nhỏ hơn 4096 byte; không thể bảo toàn kiểu " +
                "regular-stream của HarnProg V1.110.");
        }

        var raw = originalFile.ToArray();

        var sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(
            raw.AsSpan(30, 2));
        var sectorSize = 1 << sectorShift;

        if (sectorSize != SectorSize)
        {
            throw new ThtExportException(
                $"Sector size THT chưa hỗ trợ: {sectorSize}.");
        }

        var fatSectorIds = ReadFatSectorIds(raw, sectorSize);
        if (fatSectorIds.Count == 0)
            throw new ThtExportException("THT nguồn không có FAT sector.");

        var fatEntriesPerSector = sectorSize / 4;
        var fat = new List<uint>(
            fatSectorIds.Count * fatEntriesPerSector);

        foreach (var fatSid in fatSectorIds)
        {
            var fatOffset = SectorFileOffset(fatSid, sectorSize);
            EnsureRange(raw, fatOffset, sectorSize, "FAT sector");

            for (var i = 0; i < fatEntriesPerSector; i++)
            {
                fat.Add(BinaryPrimitives.ReadUInt32LittleEndian(
                    raw.AsSpan(fatOffset + i * 4, 4)));
            }
        }

        var firstDirectorySector = BinaryPrimitives.ReadUInt32LittleEndian(
            raw.AsSpan(48, 4));
        var directoryChain = FollowFatChain(
            firstDirectorySector,
            fat,
            "Directory");

        var contentsDirectoryOffset = -1;
        uint oldContentsStart = Eoc;
        long oldContentsSize = 0;

        for (var directorySectorIndex = 0;
             directorySectorIndex < directoryChain.Count;
             directorySectorIndex++)
        {
            var sectorId = directoryChain[directorySectorIndex];
            var sectorOffset = SectorFileOffset(sectorId, sectorSize);
            EnsureRange(raw, sectorOffset, sectorSize, "Directory sector");

            for (var entryOffset = 0;
                 entryOffset < sectorSize;
                 entryOffset += 128)
            {
                var absolute = sectorOffset + entryOffset;
                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                    raw.AsSpan(absolute + 64, 2));

                if (nameLength < 2 || nameLength > 64)
                    continue;

                var name = Encoding.Unicode.GetString(
                    raw,
                    absolute,
                    nameLength - 2);

                if (!string.Equals(
                        name,
                        "Contents",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                contentsDirectoryOffset = absolute;
                oldContentsStart =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        raw.AsSpan(absolute + 116, 4));
                oldContentsSize =
                    (long)BinaryPrimitives.ReadUInt64LittleEndian(
                        raw.AsSpan(absolute + 120, 8));
                break;
            }

            if (contentsDirectoryOffset >= 0)
                break;
        }

        if (contentsDirectoryOffset < 0)
        {
            throw new ThtExportException(
                "Không tìm thấy Directory Entry 'Contents' trong THT gốc.");
        }

        if (oldContentsSize < MiniStreamCutoff ||
            oldContentsStart >= 0xFFFFFFF0)
        {
            throw new ThtExportException(
                "Stream Contents của THT gốc không phải regular FAT stream.");
        }

        var oldChain = FollowFatChain(
            oldContentsStart,
            fat,
            "Contents");

        var requiredSectorCount = Math.Max(
            1,
            (newContents.Length + sectorSize - 1) / sectorSize);

        var newChain = oldChain
            .Take(Math.Min(oldChain.Count, requiredSectorCount))
            .ToList();

        // Ưu tiên các sector FREE đã tồn tại trong chính file gốc.
        var physicalSectorCount =
            (raw.Length - sectorSize) / sectorSize;

        if (newChain.Count < requiredSectorCount)
        {
            for (var sid = 0;
                 sid < physicalSectorCount &&
                 newChain.Count < requiredSectorCount;
                 sid++)
            {
                if (sid >= fat.Count)
                    break;

                if (fat[sid] == Free)
                {
                    newChain.Add((uint)sid);
                    fat[sid] = Eoc; // reserve tạm
                }
            }
        }

        // Nếu vẫn thiếu, append sector mới nhưng KHÔNG thay số FAT sector.
        // Chỉ được append khi FAT hiện tại còn entry trống để quản lý.
        if (newChain.Count < requiredSectorCount)
        {
            var missing = requiredSectorCount - newChain.Count;

            for (var n = 0; n < missing; n++)
            {
                var newSid = (raw.Length - sectorSize) / sectorSize;

                if (newSid >= fat.Count)
                {
                    throw new ThtExportException(
                        "File THT tăng quá lớn, vượt dung lượng FAT hiện tại. " +
                        "Không ghi để tránh làm thay đổi container HarnProg.");
                }

                if (fat[newSid] != Free)
                {
                    throw new ThtExportException(
                        "Không còn FAT entry trống để mở rộng Contents.");
                }

                Array.Resize(ref raw, raw.Length + sectorSize);
                fat[newSid] = Eoc;
                newChain.Add((uint)newSid);
            }
        }

        // Giải phóng phần chain cũ không còn dùng.
        foreach (var oldSid in oldChain.Skip(requiredSectorCount))
        {
            if (oldSid < fat.Count)
                fat[(int)oldSid] = Free;
        }

        // Thiết lập chain mới.
        for (var i = 0; i < newChain.Count; i++)
        {
            var sid = newChain[i];

            if (sid >= fat.Count)
                throw new ThtExportException(
                    "FAT index vượt phạm vi khi ghi Contents.");

            fat[(int)sid] =
                i == newChain.Count - 1
                    ? Eoc
                    : newChain[i + 1];
        }

        // Ghi Contents vào chain, zero-fill phần sector còn dư.
        var copied = 0;
        foreach (var sid in newChain)
        {
            var offset = SectorFileOffset(sid, sectorSize);
            EnsureRange(raw, offset, sectorSize, "Contents sector");

            Array.Clear(raw, offset, sectorSize);

            var count = Math.Min(
                sectorSize,
                newContents.Length - copied);

            if (count > 0)
            {
                Buffer.BlockCopy(
                    newContents,
                    copied,
                    raw,
                    offset,
                    count);
                copied += count;
            }
        }

        if (copied != newContents.Length)
        {
            throw new ThtExportException(
                "Không ghi đủ dữ liệu stream Contents.");
        }

        // Directory Entry vẫn ở đúng vị trí cũ, chỉ đổi start/size.
        BinaryPrimitives.WriteUInt32LittleEndian(
            raw.AsSpan(contentsDirectoryOffset + 116, 4),
            newChain[0]);

        BinaryPrimitives.WriteUInt64LittleEndian(
            raw.AsSpan(contentsDirectoryOffset + 120, 8),
            (ulong)newContents.Length);

        // Ghi ngược FAT entries vào CHÍNH các FAT sector gốc.
        for (var fatSectorIndex = 0;
             fatSectorIndex < fatSectorIds.Count;
             fatSectorIndex++)
        {
            var fatSid = fatSectorIds[fatSectorIndex];
            var fatOffset = SectorFileOffset(fatSid, sectorSize);
            EnsureRange(raw, fatOffset, sectorSize, "FAT sector write");

            var baseIndex = fatSectorIndex * fatEntriesPerSector;

            for (var i = 0; i < fatEntriesPerSector; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    raw.AsSpan(fatOffset + i * 4, 4),
                    fat[baseIndex + i]);
            }
        }

        return raw;
    }

    private static List<uint> ReadFatSectorIds(
        byte[] raw,
        int sectorSize)
    {
        var result = new List<uint>();

        // 109 DIFAT entries inline trong header.
        for (var i = 0; i < 109; i++)
        {
            var sid = BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(76 + i * 4, 4));

            if (sid < 0xFFFFFFF0)
                result.Add(sid);
        }

        var firstDifatSector =
            BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(68, 4));
        var difatSectorCount =
            BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(72, 4));

        var current = firstDifatSector;
        var entriesPerDifatSector = sectorSize / 4 - 1;

        for (var sectorIndex = 0;
             sectorIndex < difatSectorCount &&
             current < 0xFFFFFFF0;
             sectorIndex++)
        {
            var offset = SectorFileOffset(current, sectorSize);
            EnsureRange(raw, offset, sectorSize, "DIFAT sector");

            for (var i = 0; i < entriesPerDifatSector; i++)
            {
                var sid = BinaryPrimitives.ReadUInt32LittleEndian(
                    raw.AsSpan(offset + i * 4, 4));

                if (sid < 0xFFFFFFF0)
                    result.Add(sid);
            }

            current = BinaryPrimitives.ReadUInt32LittleEndian(
                raw.AsSpan(
                    offset + entriesPerDifatSector * 4,
                    4));
        }

        return result;
    }

    private static List<uint> FollowFatChain(
        uint start,
        IReadOnlyList<uint> fat,
        string label)
    {
        var result = new List<uint>();
        var seen = new HashSet<uint>();
        var current = start;

        while (current < 0xFFFFFFF0)
        {
            if (current >= fat.Count)
                throw new ThtExportException(
                    $"{label} FAT chain vượt bảng FAT.");

            if (!seen.Add(current))
                throw new ThtExportException(
                    $"{label} FAT chain bị lặp.");

            result.Add(current);
            current = fat[(int)current];
        }

        if (result.Count == 0)
            throw new ThtExportException(
                $"{label} FAT chain rỗng.");

        return result;
    }

    private static int SectorFileOffset(
        uint sectorId,
        int sectorSize)
    {
        checked
        {
            return sectorSize + (int)sectorId * sectorSize;
        }
    }

    private static void EnsureRange(
        byte[] raw,
        int offset,
        int length,
        string label)
    {
        if (offset < 0 ||
            length < 0 ||
            offset > raw.Length - length)
        {
            throw new ThtExportException(
                $"{label} vượt kích thước file THT.");
        }
    }

    private static byte[] BuildContents(ModelDocument document)
    {
        var cp949 = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var textRaw = EncodeHarnProgText(ModelToThtText(document), cp949);
        var state = document.ThtState; var version = state?.Version ?? ThtVersion; var token = state?.Token ?? ThtToken;
        var tail = state is { Tail.Length: > 0 } ? state.Tail : Asset("template_tail_v1110.bin");
        if (version != ThtVersion) throw new ThtExportException($"Không thể ghi phiên bản THT {version}; chỉ hỗ trợ {ThtVersion}.");
        if (token != ThtToken) throw new ThtExportException($"Token THT không hỗ trợ: 0x{token:X8}");
        using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
        bw.Write(0u); bw.Write(0u); bw.Write(0u); bw.Write(token); bw.Write(MfcCString(Encoding.ASCII.GetBytes(version))); bw.Write(MfcCString(textRaw)); bw.Write(tail);
        var body = ms.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0, 4), (uint)(body.Length - 8)); BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4, 4), (uint)(body.Length - 20));
        return body;
    }

    // Never silently rename product/wire fields through transliteration or '?'.
    private static byte[] EncodeHarnProgText(string text, Encoding cp949)
    {
        try { return cp949.GetBytes(text); }
        catch (EncoderFallbackException ex)
        {
            throw new ThtExportException("Nội dung có ký tự không hỗ trợ trong bảng mã CP949 của THT. Giữ nguyên file gốc. " + ex.Message);
        }
    }

    private static List<string> MergeProduct(ModelDocument document)
    {
        var product = document.ThtState is not null ? Fit(document.ThtState.ProductRow, ProductHeaders.Length) : Enumerable.Repeat("", ProductHeaders.Length).ToList();
        product[1] = Safe(document.Base.Number).Length > 0 ? Safe(document.Base.Number) : Safe(document.Base.Model);
        product[2] = Safe(document.Base.Kind); product[4] = Safe(document.Base.Name); product[5] = Safe(document.Base.EoNo); product[3] = Safe(document.Base.Customer); return product;
    }

    private static List<List<string>> MergeConnectors(ModelDocument document)
    {
        var current = document.Connectors;
        var stateRows = document.ThtState?.ConnectorRows ?? [];
        var statePinRows = document.ThtState?.PinRows ?? [];
        var specialIos = HarnProgSpecialPins(document.Pins)
            .Select(pin => pin.Io)
            .ToHashSet();

        bool IsDedicatedSpecialConnector(Connector connector)
        {
            var connectorPins = document.Pins
                .Where(pin => pin.Connector.Trim().Equals(
                    connector.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            return connectorPins.Count > 0 &&
                   connectorPins.All(pin => specialIos.Contains(pin.Io));
        }

        var used = new HashSet<int>();
        var output = new List<List<string>>();
        var currentByName = current
            .Select((c, i) => (c, i))
            .Where(x => !string.IsNullOrWhiteSpace(x.c.Name))
            .ToDictionary(
                x => x.c.Name.Trim(),
                x => x.i,
                StringComparer.OrdinalIgnoreCase);
        var currentPinByIo = document.Pins.GroupBy(p => p.Io).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        var originalPinIos =
            new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var originalPin in statePinRows)
        {
            var row = Fit(originalPin, PinHeaders.Length);
            var name = row[0].Trim();
            if (name.Length == 0 ||
                !int.TryParse(row[2].Trim(), out var io) ||
                !int.TryParse(row[3].Trim(), out var pn) ||
                io <= 0 ||
                pn <= 0)
            {
                continue;
            }

            if (!originalPinIos.TryGetValue(name, out var list))
                originalPinIos[name] = list = [];
            list.Add(io);
        }

        int? Map(string oldName)
        {
            if (currentByName.TryGetValue(oldName.Trim(), out var direct) &&
                !used.Contains(direct))
            {
                return direct;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (originalPinIos.TryGetValue(oldName.Trim(), out var ios))
            {
                foreach (var io in ios)
                {
                    if (currentPinByIo.TryGetValue(io, out var p) &&
                        !string.IsNullOrWhiteSpace(p.Connector))
                    {
                        names.Add(p.Connector.Trim());
                    }
                }
            }

            if (names.Count == 1 &&
                currentByName.TryGetValue(names.First(), out var candidate) &&
                !used.Contains(candidate))
            {
                return candidate;
            }

            return null;
        }

        foreach (var original in stateRows)
        {
            var old = Fit(original, ConnectorHeaders.Length);
            var oldName = old[1].Trim();
            var idx = Map(oldName);

            if (!idx.HasValue)
            {
                // Giữ các dòng metadata không có PIN; bỏ connector mà người dùng
                // đã xóa hết PIN khỏi model.
                if (!originalPinIos.ContainsKey(oldName))
                    output.Add(old);
                continue;
            }

            used.Add(idx.Value);
            var c = current[idx.Value];

            // Cụm AO/aN là special PIN rows của HarnProg: PIN row không mang
            // connector. Nếu connector nội bộ chỉ dùng cho cụm special thì
            // không tạo thêm connector mới. Với THT round-trip, các row gốc
            // không map tới CLIP vẫn được giữ nguyên ở nhánh idx=null phía trên.
            if (IsDedicatedSpecialConnector(c))
                continue;

            var row = old;
            row[1] = Safe(c.Name);
            var originalCount =
                originalPinIos.TryGetValue(oldName, out var oi) ? oi.Count : 0;
            var currentCount = document.Pins.Count(p =>
                string.Equals(
                    p.Connector.Trim(),
                    c.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (originalCount == 0 || currentCount != originalCount)
                row[2] = c.PinCount.ToString();

            row[8] = c.Waterproof ? "1" : "";
            output.Add(row);
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (used.Contains(i) || IsDedicatedSpecialConnector(current[i]))
                continue;

            var connectorPins = document.Pins.Count(pin =>
                pin.Connector.Trim().Equals(
                    current[i].Name.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (connectorPins <= 0)
                continue;

            output.Add([
                "",
                Safe(current[i].Name),
                current[i].PinCount.ToString(),
                "",
                "",
                "",
                "",
                "",
                current[i].Waterproof ? "1" : ""
            ]);
        }

        return output;
    }

    private static List<List<string>> MergePins(ModelDocument document)
    {
        var current = document.Pins;
        var stateRows = document.ThtState?.PinRows ?? [];
        var originalClip = ClipRows(stateRows);
        var clipIndices = originalClip.Keys.ToHashSet();

        var specialPins = HarnProgSpecialPins(current)
            .OrderBy(pin => pin.Io)
            .ToList();
        var specialIos = specialPins.Select(pin => pin.Io).ToHashSet();
        var common = specialPins.FirstOrDefault(pin =>
            IsClipCommonLabel(PinTypeLabelFromPin(pin)))
            ?? specialPins.FirstOrDefault();
        var commonIo = common?.Io ?? -1;

        var byIo = current
            .Select((p, i) => (p, i))
            .GroupBy(x => x.p.Io)
            .ToDictionary(g => g.Key, g => g.First().i);
        var byIdentity = current
            .Select((p, i) => (p, i))
            .ToDictionary(
                x => $"{x.p.Connector.ToUpperInvariant()}\u0001{x.p.PinNo}",
                x => x.i);
        var bySourceRow = current
            .Select((p, i) => (p, i))
            .Where(x => x.p.SourceThtRowIndex >= 0)
            .ToDictionary(x => x.p.SourceThtRowIndex, x => x.i);
        var hasTrackedRows = bySourceRow.Count > 0;

        var used = new HashSet<int>();
        var output = new List<List<string>>();

        for (var originalIndex = 0;
             originalIndex < stateRows.Count;
             originalIndex++)
        {
            var old = Fit(stateRows[originalIndex], PinHeaders.Length);

            if (clipIndices.Contains(originalIndex))
            {
                var oldIo = originalClip[originalIndex];
                if (!byIo.TryGetValue(oldIo, out var candidate) ||
                    used.Contains(candidate) ||
                    !specialIos.Contains(current[candidate].Io))
                {
                    continue;
                }

                used.Add(candidate);
                var pin = current[candidate];
                var row = old;
                row[2] = pin.Io.ToString();

                var currentLabel = PinTypeLabelFromPin(pin);
                var expected = ClipLabelForNewPin(
                    pin,
                    commonIo,
                    specialPins.FindIndex(item => item.Io == pin.Io));

                // Round-trip file HarnProg gốc: các row legacy có PIN TYPE rỗng
                // (ví dụ a6/a12 của WH322244) phải tiếp tục rỗng nếu người dùng
                // không thực sự thay đổi mapping.
                row[4] =
                    string.IsNullOrWhiteSpace(old[4]) &&
                    currentLabel.Equals(expected, StringComparison.OrdinalIgnoreCase)
                        ? ""
                        : currentLabel;

                // Special PIN row luôn để connector/선이름 trống.
                row[0] = "";
                row[1] = "";
                output.Add(row);
                continue;
            }

            int? idx = null;
            if (bySourceRow.TryGetValue(originalIndex, out var tracked) &&
                !used.Contains(tracked) &&
                !specialIos.Contains(current[tracked].Io))
            {
                idx = tracked;
            }
            else if (!hasTrackedRows && int.TryParse(old[3].Trim(), out var oldPin) &&
                byIdentity.TryGetValue(
                         $"{old[0].Trim().ToUpperInvariant()}\u0001{oldPin}",
                         out var c2) &&
                !used.Contains(c2) &&
                !specialIos.Contains(current[c2].Io))
            {
                idx = c2;
            }
            else if (!hasTrackedRows && int.TryParse(old[2].Trim(), out var oldIo2) &&
                     byIo.TryGetValue(oldIo2, out var c1) &&
                     !used.Contains(c1) &&
                     !specialIos.Contains(current[c1].Io))
            {
                idx = c1;
            }

            if (!idx.HasValue)
            {
                var editable =
                    !string.IsNullOrWhiteSpace(old[0]) &&
                    int.TryParse(old[2].Trim(), out var li) &&
                    int.TryParse(old[3].Trim(), out var lp) &&
                    li > 0 &&
                    lp > 0;

                if (!editable)
                    output.Add(old);

                continue;
            }

            used.Add(idx.Value);
            var p = current[idx.Value];
            var r = old;
            r[0] = Safe(p.Connector);
            r[1] = Safe(p.Line);
            r[2] = p.Io.ToString();
            r[3] = p.PinNo.ToString();
            r[4] = Safe(p.TestNo);
            output.Add(r);
        }

        var remaining = current
            .Select((p, i) => (p, i))
            .Where(pair => !used.Contains(pair.i))
            .OrderBy(pair => pair.p.Io)
            .ToList();

        var specialEmitted = false;
        foreach (var pair in remaining)
        {
            if (specialIos.Contains(pair.p.Io))
            {
                if (specialEmitted)
                    continue;

                specialEmitted = true;
                foreach (var specialRow in BuildNewClipRows(specialPins, commonIo))
                    output.Add(specialRow);

                foreach (var special in specialPins)
                {
                    if (byIo.TryGetValue(special.Io, out var specialIndex))
                        used.Add(specialIndex);
                }

                continue;
            }

            var p = pair.p;
            output.Add([
                Safe(p.Connector),
                Safe(p.Line),
                p.Io.ToString(),
                p.PinNo.ToString(),
                Safe(p.TestNo),
                "",
                "",
                ""
            ]);
        }

        return output;
    }

    private static IEnumerable<List<string>> BuildNewClipRows(
        IReadOnlyList<Pin> clipPins,
        int commonIo)
    {
        var ordered = clipPins
            .OrderBy(pin => pin.Io)
            .ToList();

        var isReferenceLayout =
            ordered.Count == 17 &&
            commonIo > 0 &&
            ordered.Select(pin => pin.Io)
                .SequenceEqual(Enumerable.Range(commonIo, 17));

        if (isReferenceLayout)
        {
            // WH322244 gốc đặt IO+7 (a17) xuống cuối bảng PIN.
            ordered = ordered
                .OrderBy(pin => pin.Io == commonIo + 7 ? int.MaxValue : pin.Io)
                .ToList();
        }

        foreach (var pin in ordered)
        {
            var pinNumber = "";
            string label;

            if (isReferenceLayout)
            {
                // Giữ byte/text semantics của WH322244 gốc:
                // IO+6 và IO+12 có PIN TYPE rỗng; IO+12 có pin-number=0;
                // IO+7 là a17 và được đặt cuối.
                var offset = pin.Io - commonIo;
                label = offset switch
                {
                    0 => "AO",
                    6 or 12 => "",
                    7 => "a17",
                    _ => $"a{offset}"
                };

                if (offset == 12)
                    pinNumber = "0";
            }
            else
            {
                var originalIndex = clipPins
                    .OrderBy(item => item.Io)
                    .ToList()
                    .FindIndex(item => item.Io == pin.Io);

                label = ClipLabelForNewPin(pin, commonIo, originalIndex);
            }

            // Đây là mapping bắt buộc:
            // ModelCreator TÊN DÂY = AO/aN
            //      -> HarnProg PIN table:
            // connector="" | 선이름="" | I/O | 핀번호="" | 핀종류=AO/aN
            //
            // Vì vậy AO/aN KHÔNG được nằm ở cột 선이름 của PIN row.
            yield return [
                "",
                "",
                pin.Io.ToString(),
                pinNumber,
                label,
                "",
                "",
                ""
            ];
        }
    }

    private static void ValidateClipPinTypeColumn(
        ModelDocument document,
        IReadOnlyList<List<string>> serializedPinRows)
    {
        var specialPins = HarnProgSpecialPins(document.Pins)
            .OrderBy(pin => pin.Io)
            .ToList();

        if (specialPins.Count == 0)
            return;

        var byIo = new Dictionary<int, List<string>>();
        foreach (var source in serializedPinRows)
        {
            var row = Fit(source, PinHeaders.Length);
            if (!int.TryParse(row[2].Trim(), out var io) || io <= 0)
                continue;

            if (specialPins.Any(pin => pin.Io == io))
                byIo[io] = row;
        }

        if (byIo.Count != specialPins.Count)
        {
            throw new ThtExportException(
                $"Special PIN row AO/aN bị thiếu: expected={specialPins.Count}, actual={byIo.Count}.");
        }

        var common = specialPins.FirstOrDefault(pin =>
            IsClipCommonLabel(PinTypeLabelFromPin(pin)))
            ?? specialPins[0];
        var commonIo = common.Io;
        var isReference17 =
            specialPins.Count == 17 &&
            specialPins.Select(pin => pin.Io)
                .SequenceEqual(Enumerable.Range(commonIo, 17));

        for (var index = 0; index < specialPins.Count; index++)
        {
            var pin = specialPins[index];
            var row = byIo[pin.Io];
            var offset = pin.Io - commonIo;
            var expectedPinType = isReference17
                ? offset switch
                {
                    0 => "AO",
                    6 or 12 => "",
                    7 => "a17",
                    _ => $"a{offset}"
                }
                : ClipLabelForNewPin(pin, commonIo, index);

            // HarnProg V1.110 nhận AO/aN ở cột 핀종류, không phải 선이름.
            if (row[0].Length != 0 || row[1].Length != 0)
            {
                throw new ThtExportException(
                    $"IO{pin.Io}: special AO/aN phải có connector/선이름 rỗng trong PIN table.");
            }

            if (!string.Equals(
                    row[4].Trim(),
                    expectedPinType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ThtExportException(
                    $"IO{pin.Io}: PIN TYPE (핀종류) phải là '{expectedPinType}', nhưng đang là '{row[4]}'.");
            }
        }
    }

    private static List<List<string>> MergeWires(ModelDocument document)
    {
        var specialPins = HarnProgSpecialPins(document.Pins);
        var specialIos = specialPins.Select(pin => pin.Io).ToHashSet();
        var omittedSpecialCatalogIos = new HashSet<int>();
        if (specialPins.Count == 17)
        {
            var firstIo = specialPins.Min(pin => pin.Io);
            if (specialPins.Select(pin => pin.Io)
                .OrderBy(io => io)
                .SequenceEqual(Enumerable.Range(firstIo, 17)))
            {
                // WH322244 gốc có a17 trong PIN TYPE nhưng KHÔNG có a17
                // trong wire catalog.
                omittedSpecialCatalogIos.Add(firstIo + 7);
            }
        }

        var current = WireRows(
            document.Pins,
            specialIos,
            omittedSpecialCatalogIos);
        var stateRows = document.ThtState?.WireRows ?? [];
        var statePinRows = document.ThtState?.PinRows ?? [];

        // HarnProg gốc có AO/aN trong wire catalog dù PIN row để trống 선이름.
        // TÊN DÂY của ModelCreator là nguồn nhập liệu; khi export special row,
        // catalog vẫn phải giữ AO/aN với các cột phụ rỗng.
        if (stateRows.Count == 0 && specialPins.Count > 0)
            current = AddLegacyClipWireCatalog(current, document.Pins);

        var used = new HashSet<int>(); var output = new List<List<string>>();
        var currentByName = current.Select((r, i) => (r, i)).Where(x => !string.IsNullOrWhiteSpace(x.r[0])).ToDictionary(x => x.r[0].Trim(), x => x.i, StringComparer.OrdinalIgnoreCase);
        var currentPinByIo = document.Pins.GroupBy(p => p.Io).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single()); var originalWireIos = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in statePinRows) { var row = Fit(op, PinHeaders.Length); var name = row[1].Trim(); if (name.Length == 0 || !int.TryParse(row[2].Trim(), out var io) || !int.TryParse(row[3].Trim(), out var pn) || io <= 0 || pn <= 0) continue; if (!originalWireIos.TryGetValue(name, out var list)) originalWireIos[name] = list = []; list.Add(io); }
        int? Map(string oldName) { if (currentByName.TryGetValue(oldName.Trim(), out var direct) && !used.Contains(direct)) return direct; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); if (originalWireIos.TryGetValue(oldName.Trim(), out var ios)) foreach (var io in ios) if (currentPinByIo.TryGetValue(io, out var p) && !string.IsNullOrWhiteSpace(p.Line)) names.Add(p.Line.Trim()); if (names.Count == 1 && currentByName.TryGetValue(names.First(), out var c) && !used.Contains(c)) return c; return null; }
        foreach (var original in stateRows) { var old = Fit(original, WireHeaders.Length); var idx = Map(old[0]); if (!idx.HasValue) { output.Add(old); continue; } used.Add(idx.Value); var n = current[idx.Value]; for (var c = 0; c < 4; c++) old[c] = n[c]; output.Add(old); }
        for (var i = 0; i < current.Count; i++) if (!used.Contains(i)) output.Add(Fit(current[i], WireHeaders.Length)); return output;
    }

    private static List<List<string>> AddLegacyClipWireCatalog(
        List<List<string>> rows,
        IReadOnlyList<Pin> pins)
    {
        var result = rows.Select(row => Fit(row, WireHeaders.Length)).ToList();
        var names = result
            .Where(row => row.Count > 0 && !string.IsNullOrWhiteSpace(row[0]))
            .Select(row => row[0].Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var clipPins = HarnProgSpecialPins(pins)
            .OrderBy(pin => pin.Io)
            .ToList();

        if (clipPins.Count == 0)
            return result;

        var firstIo = clipPins[0].Io;
        var isReference17 = clipPins.Count == 17 &&
                            clipPins.Select(pin => pin.Io)
                                .SequenceEqual(Enumerable.Range(firstIo, 17));

        IEnumerable<string> labels = isReference17
            ? Reference17ClipWireLabels
            : clipPins.Select(PinTypeLabelFromPin)
                .Where(label => label.Length > 0 && IsClipLabel(label))
                .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var label in labels)
        {
            if (names.Add(label))
                result.Add([label, "", "", "", "", "", "", ""]);
        }

        return result;
    }

    private static List<List<string>> WireRows(
        IReadOnlyList<Pin> pins,
        IReadOnlySet<int>? specialIos = null,
        IReadOnlySet<int>? omittedSpecialCatalogIos = null)
    {
        var rows = new List<List<string>>();
        var byName =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in pins.OrderBy(p => p.Io))
        {
            var name = Safe(p.Line);
            if (name.Length == 0)
                continue;

            var isSpecial = specialIos?.Contains(p.Io) == true;
            if (isSpecial &&
                omittedSpecialCatalogIos?.Contains(p.Io) == true)
            {
                continue;
            }
            var row = new List<string>
            {
                name,
                isSpecial ? "" : Safe(p.Add),
                isSpecial ? "" : Safe(p.Thickness),
                isSpecial ? "" : Safe(p.Color),
                "",
                "",
                "",
                ""
            };

            if (!byName.TryGetValue(name, out var idx))
            {
                byName[name] = rows.Count;
                rows.Add(row);
            }
            else
            {
                for (var c = 1; c <= 3; c++)
                {
                    if (string.IsNullOrEmpty(rows[idx][c]) &&
                        !string.IsNullOrEmpty(row[c]))
                    {
                        rows[idx][c] = row[c];
                    }
                }
            }
        }

        return rows;
    }

    private static Dictionary<string, List<string>> WireByName(List<List<string>> rows)
    {
        var d = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); foreach (var original in rows) { var row = Fit(original, WireHeaders.Length); var name = row[0].Trim(); if (name.Length > 0 && !d.ContainsKey(name)) d[name] = row; }
        return d;
    }

    private static Dictionary<int, int> ClipRows(List<List<string>> pinRows)
    {
        var direct = new List<(int index, int io)>(); var hasCommon = false;
        for (var i = 0; i < pinRows.Count; i++) { var row = Fit(pinRows[i], PinHeaders.Length); if (!string.IsNullOrWhiteSpace(row[0]) || !int.TryParse(row[2].Trim(), out var io) || io <= 0 || (row[3].Trim() != "" && row[3].Trim() != "0")) continue; var label = row[4].Trim(); if (IsClipLabel(label)) { direct.Add((i, io)); hasCommon |= IsClipCommonLabel(label); } }
        if (direct.Count == 0 || !hasCommon) return new Dictionary<int, int>();
        var min = direct.Min(x => x.io); var max = direct.Max(x => x.io); var result = new Dictionary<int, int>();
        for (var i = 0; i < pinRows.Count; i++) { var row = Fit(pinRows[i], PinHeaders.Length); if (!string.IsNullOrWhiteSpace(row[0]) || !int.TryParse(row[2].Trim(), out var io) || io < min || io > max || (row[3].Trim() != "" && row[3].Trim() != "0")) continue; var label = row[4].Trim(); if (label.Length > 0 && !IsClipLabel(label)) continue; result[i] = io; }
        var ios = result.Values.Distinct().OrderBy(x => x).ToList(); if (ios.Count == 0 || !ios.SequenceEqual(Enumerable.Range(ios[0], ios[^1] - ios[0] + 1))) return new Dictionary<int, int>(); return result;
    }
    private static List<Pin> HarnProgSpecialPins(IReadOnlyList<Pin> pins)
    {
        var explicitCommon = pins
            .Where(pin =>
                IsClipCommonLabel(pin.Line) ||
                IsClipCommonLabel(pin.TestNo))
            .OrderBy(pin => pin.Io)
            .ToList();

        if (explicitCommon.Count > 1)
        {
            throw new ThtExportException(
                "HarnProg THT hiện chỉ hỗ trợ một cụm AO/aN. Có nhiều hơn một chân AO.");
        }

        if (explicitCommon.Count == 1)
        {
            // Khi TÊN DÂY có AO, chính giá trị AO/aN là nguồn sự thật.
            // Không yêu cầu connector nội bộ phải tên CLIP hoặc phải giống nhau.
            return pins
                .Where(pin =>
                    IsClipLabel(pin.Line) ||
                    IsClipLabel(pin.TestNo))
                .OrderBy(pin => pin.Io)
                .ToList();
        }

        // Backward compatibility với THT đã load: loader dùng connector nội bộ
        // "CLIP" cho các special PIN rows.
        return pins
            .Where(pin => IsClipConnector(pin.Connector))
            .OrderBy(pin => pin.Io)
            .ToList();
    }

    private static string PinTypeLabelFromPin(Pin pin)
    {
        if (ModelLogic.TryNormalizeHarnProgPinTypeLabel(pin.Line, out var lineLabel))
            return lineLabel;

        if (ModelLogic.TryNormalizeHarnProgPinTypeLabel(pin.TestNo, out var testLabel))
            return testLabel;

        return "";
    }

    private static bool IsClipConnector(string name) =>
        string.Equals(name.Trim(), "CLIP", StringComparison.OrdinalIgnoreCase);

    private static bool IsClipLabel(string value) =>
        ModelLogic.TryNormalizeHarnProgPinTypeLabel(value, out _);

    private static bool IsClipCommonLabel(string value) =>
        ModelLogic.TryNormalizeHarnProgPinTypeLabel(value, out var label) &&
        label == "AO";

    private static string ClipLabelForNewPin(
        Pin pin,
        int commonIo,
        int ordinal)
    {
        var explicitLabel = PinTypeLabelFromPin(pin);
        if (explicitLabel.Length > 0)
            return explicitLabel;

        if (pin.Io == commonIo)
            return "AO";

        return $"a{Math.Max(1, ordinal)}";
    }

    private static ParsedContents ParseContents(byte[] contents)
    {
        if (contents.Length < 24) throw new ThtFormatException("Stream Contents quá ngắn."); var outer = U32(contents, 0); var inner = U32(contents, 4); var flags = U32(contents, 8); var token = U32(contents, 12);
        if (outer != (uint)(contents.Length - 8)) throw new ThtFormatException("Contents outer size không đúng định dạng HarnProg."); if (inner != (uint)(contents.Length - 20)) throw new ThtFormatException("Contents inner size không đúng định dạng HarnProg."); if (flags != 0) throw new ThtFormatException($"Contents flags chưa hỗ trợ: 0x{flags:X8}");
        var (versionRaw, off) = ReadMfcCString(contents, 16); var (modelRaw, tailOff) = ReadMfcCString(contents, off); var version = Encoding.ASCII.GetString(versionRaw); if (version != ThtVersion) throw new ThtFormatException($"Phiên bản THT chưa hỗ trợ: {version}. Hiện hỗ trợ {ThtVersion}."); if (token != ThtToken) throw new ThtFormatException($"Token THT chưa hỗ trợ: 0x{token:X8}");
        string text; try { text = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(modelRaw); } catch (DecoderFallbackException ex) { throw new ThtFormatException("Không giải mã được bảng model bằng CP949: " + ex.Message); }
        return new(version, token, text, contents[tailOff..]);
    }
    private static ParsedTables ParseModelText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n'); var sections = normalized.Split("\n\n", StringSplitOptions.None).ToList(); while (sections.Count > 0 && string.IsNullOrWhiteSpace(sections[^1])) sections.RemoveAt(sections.Count - 1); if (sections.Count < 4) throw new ThtFormatException($"THT thiếu 4 bảng Product/Connector/Pin/Wire (tìm thấy {sections.Count}).");
        List<List<string>> Rows(string s) { var r = new List<List<string>>(); foreach (var line in s.Split('\n')) { if (line.Length == 0) continue; var cols = line.Split('\t').ToList(); if (cols.Count > 0 && cols[^1] == "") cols.RemoveAt(cols.Count - 1); r.Add(cols); } return r; }
        var pr = Rows(sections[0]); var cr = Rows(sections[1]); var pir = Rows(sections[2]); var wr = Rows(sections[3]); if (pr.Count < 2) throw new ThtFormatException("Bảng Product không có dòng dữ liệu."); return new(Fit(pr[1], ProductHeaders.Length), cr.Skip(1).Select(r => Fit(r, ConnectorHeaders.Length)).ToList(), pir.Skip(1).Select(r => Fit(r, PinHeaders.Length)).ToList(), wr.Skip(1).Select(r => Fit(r, WireHeaders.Length)).ToList());
    }

    private static byte[] BuildCompoundFile(byte[] contents, byte[] summary)
    {
        if (contents.Length < MiniStreamCutoff) throw new ThtExportException("Contents THT nhỏ hơn ngưỡng OLE dự kiến."); if (summary.Length >= MiniStreamCutoff) throw new ThtExportException("SummaryInformation template không hợp lệ.");
        var summaryMiniCount = Math.Max(1, (int)Math.Ceiling(summary.Length / (double)MiniSectorSize)); var rootSize = summaryMiniCount * MiniSectorSize; if ((int)Math.Ceiling(rootSize / (double)SectorSize) != 1) throw new ThtExportException("SummaryInformation vượt giới hạn writer THT.");
        var contentSectorCount = (int)Math.Ceiling(contents.Length / (double)SectorSize); const int dirSid = 0, minifatSid = 1, rootSid = 2, contentsSid = 3; var nonfat = 3 + contentSectorCount; var fatCount = 1; while ((int)Math.Ceiling((nonfat + fatCount) / (double)(SectorSize / 4)) != fatCount) fatCount = (int)Math.Ceiling((nonfat + fatCount) / (double)(SectorSize / 4)); if (fatCount > 109) throw new ThtExportException("File THT quá lớn cho định dạng legacy inline DIFAT.");
        var fatSids = Enumerable.Range(nonfat, fatCount).Select(x => (uint)x).ToList(); var fat = Enumerable.Repeat(Free, fatCount * (SectorSize / 4)).ToArray(); fat[dirSid] = Eoc; fat[minifatSid] = Eoc; fat[rootSid] = Eoc; for (var i = 0; i < contentSectorCount; i++) { var sid = contentsSid + i; fat[sid] = i == contentSectorCount - 1 ? Eoc : (uint)(sid + 1); }
        foreach (var sid in fatSids) fat[sid] = FatSect;
        var directory = DirEntry("Root Entry", 5, 0, Free, Free, 1, rootSid, rootSize).Concat(DirEntry("Contents", 2, 1, Free, 2, Free, contentsSid, contents.Length)).Concat(DirEntry("\u0005SummaryInformation", 2, 0, Free, Free, Free, 0, summary.Length)).Concat(new byte[128]).ToArray();
        var minifat = Enumerable.Repeat(Free, SectorSize / 4).ToArray(); for (var i = 0; i < summaryMiniCount; i++) minifat[i] = i == summaryMiniCount - 1 ? Eoc : (uint)(i + 1); var minifatBytes = UIntArrayBytes(minifat); var rootStream = Pad(Pad(summary, rootSize), SectorSize); var header = new byte[SectorSize]; Convert.FromHexString("D0CF11E0A1B11AE1").CopyTo(header, 0); W16(header, 24, 0x003E); W16(header, 26, 0x0003); W16(header, 28, 0xFFFE); W16(header, 30, 9); W16(header, 32, 6); W32(header, 40, 0); W32(header, 44, (uint)fatCount); W32(header, 48, dirSid); W32(header, 52, 0); W32(header, 56, MiniStreamCutoff); W32(header, 60, minifatSid); W32(header, 64, 1); W32(header, 68, Eoc); W32(header, 72, 0); var difat = fatSids.Concat(Enumerable.Repeat(Free, 109 - fatSids.Count)).ToArray(); UIntArrayBytes(difat).CopyTo(header, 76); var padded = Pad(contents, contentSectorCount * SectorSize); var fatBytes = UIntArrayBytes(fat); return header.Concat(directory).Concat(minifatBytes).Concat(rootStream).Concat(padded).Concat(fatBytes).ToArray();
    }
    private static byte[] DirEntry(string name, byte type, byte color, uint left, uint right, uint child, int start, long size) { var e = new byte[128]; var raw = Encoding.Unicode.GetBytes(name + "\0"); if (raw.Length > 64) throw new ThtExportException("Tên stream OLE quá dài."); raw.CopyTo(e, 0); W16(e, 64, (ushort)raw.Length); e[66] = type; e[67] = color; W32(e, 68, left); W32(e, 72, right); W32(e, 76, child); W32(e, 116, (uint)start); BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(120, 8), (ulong)size); return e; }

    private static byte[] MfcCString(byte[] raw) { if (raw.Length < 0xFF) return new[] { (byte)raw.Length }.Concat(raw).ToArray(); if (raw.Length < 0xFFFF) { var b = new byte[3 + raw.Length]; b[0] = 0xFF; W16(b, 1, (ushort)raw.Length); raw.CopyTo(b, 3); return b; } var c = new byte[7 + raw.Length]; c[0] = c[1] = c[2] = 0xFF; W32(c, 3, (uint)raw.Length); raw.CopyTo(c, 7); return c; }
    private static (byte[] raw, int next) ReadMfcCString(byte[] data, int offset) { if (offset >= data.Length) throw new ThtFormatException("CString vượt quá stream Contents."); var first = data[offset++]; int len; if (first < 0xFF) len = first; else { if (offset + 2 > data.Length) throw new ThtFormatException("Độ dài CString bị hỏng."); var w = U16(data, offset); offset += 2; if (w == 0xFFFF) { if (offset + 4 > data.Length) throw new ThtFormatException("Độ dài CString DWORD bị hỏng."); len = (int)U32(data, offset); offset += 4; } else len = w; } if (offset + len > data.Length) throw new ThtFormatException("Nội dung CString vượt quá stream Contents."); return (data[offset..(offset + len)], offset + len); }
    private static byte[] Asset(string name) { var asm = Assembly.GetExecutingAssembly(); var resource = $"JBZUniversalTester.ModelEditor.Assets.{name}"; using var s = asm.GetManifestResourceStream(resource) ?? throw new ThtExportException($"Thiếu dữ liệu THT embedded: {resource}"); using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray(); }
    private static string Safe(object? v) => v?.ToString()?.Trim() ?? ""; private static bool BoolText(string v) { var t = v.Trim().ToLowerInvariant(); return t.Length > 0 && t != "0" && t != "false" && t != "no" && t != "n"; }
    private static List<string> Fit(IEnumerable<string> row, int count) { var list = row.Select(v => v ?? "").ToList(); while (list.Count < count) list.Add(""); if (list.Count > count) list = list.Take(count).ToList(); return list; }
    private static byte[] Pad(byte[] data, int length) { if (data.Length >= length) return data[..length]; var r = new byte[length]; data.CopyTo(r, 0); return r; }
    private static byte[] UIntArrayBytes(uint[] values) { var b = new byte[values.Length * 4]; for (var i = 0; i < values.Length; i++) W32(b, i * 4, values[i]); return b; }
    private static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o, 2)); private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4)); private static void W16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o, 2), v); private static void W32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o, 4), v); private static void W32(byte[] b, int o, int v) => W32(b, o, (uint)v);

    private sealed record ParsedContents(string Version, uint Token, string Text, byte[] Tail);
    private sealed record ParsedTables(List<string> Product, List<List<string>> ConnectorRows, List<List<string>> PinRows, List<List<string>> WireRows);

    private sealed class CompoundFileReader
    {
        private readonly byte[] _data; private readonly int _major, _sectorSize, _miniSectorSize, _miniCutoff; private readonly uint _firstDir, _firstMinifat, _numMinifat, _firstDifat, _numDifat; private readonly List<uint> _fat = []; private readonly List<Entry> _entries = []; private readonly byte[] _rootStream; private readonly List<uint> _minifat = [];
        public CompoundFileReader(byte[] data)
        {
            if (data.Length < 512 || !data.AsSpan(0, 8).SequenceEqual(Convert.FromHexString("D0CF11E0A1B11AE1"))) throw new ThtFormatException("Không phải file OLE Compound Document của HarnProg."); _data = data; _major = U16(data, 26); _sectorSize = 1 << U16(data, 30); _miniSectorSize = 1 << U16(data, 32); _miniCutoff = (int)U32(data, 56); _firstDir = U32(data, 48); _firstMinifat = U32(data, 60); _numMinifat = U32(data, 64); _firstDifat = U32(data, 68); _numDifat = U32(data, 72);
            var fatIds = new List<uint>(); for (var i = 0; i < 109; i++) { var sid = U32(data, 76 + i * 4); if (sid < 0xFFFFFFF0) fatIds.Add(sid); }
            var dsid = _firstDifat; for (var n = 0; n < _numDifat && dsid < 0xFFFFFFF0; n++) { var s = Sector(dsid); var count = _sectorSize / 4 - 1; for (var i = 0; i < count; i++) { var v = U32(s, i * 4); if (v < 0xFFFFFFF0) fatIds.Add(v); } dsid = U32(s, count * 4); }
            foreach (var sid in fatIds) { var s = Sector(sid); for (var i = 0; i < _sectorSize / 4; i++) _fat.Add(U32(s, i * 4)); }
            if (_fat.Count == 0) throw new ThtFormatException("File THT không có FAT.");
            var dir = Chain(_firstDir, _fat).SelectMany(Sector).ToArray(); for (var o = 0; o + 128 <= dir.Length; o += 128) { var nameLen = U16(dir, o + 64); var name = nameLen >= 2 ? Encoding.Unicode.GetString(dir, o, nameLen - 2) : ""; var type = dir[o + 66]; var start = U32(dir, o + 116); ulong size = BinaryPrimitives.ReadUInt64LittleEndian(dir.AsSpan(o + 120, 8)); if (_major == 3) size &= 0xFFFFFFFF; _entries.Add(new Entry(name, type, start, (long)size)); }
            var root = EntryByName("Root Entry"); _rootStream = root.Size > 0 && root.Start < 0xFFFFFFF0 ? Chain(root.Start, _fat).SelectMany(Sector).Take((int)root.Size).ToArray() : []; if (_firstMinifat < 0xFFFFFFF0 && _numMinifat > 0) foreach (var sid in Chain(_firstMinifat, _fat)) { var s = Sector(sid); for (var i = 0; i < _sectorSize / 4; i++) _minifat.Add(U32(s, i * 4)); }
        }
        public byte[] ReadStream(string name) { var e = EntryByName(name); if (e.Size == 0) return []; if (e.Size < _miniCutoff && name != "Root Entry") { if (_minifat.Count == 0) throw new ThtFormatException($"Stream '{name}' cần MiniFAT nhưng file không có MiniFAT."); var chunks = new List<byte>(); var seen = new HashSet<uint>(); var sid = e.Start; while (sid < 0xFFFFFFF0) { if (!seen.Add(sid) || sid >= _minifat.Count) throw new ThtFormatException("MiniFAT chain không hợp lệ."); var off = (int)sid * _miniSectorSize; chunks.AddRange(_rootStream.Skip(off).Take(_miniSectorSize)); sid = _minifat[(int)sid]; } return chunks.Take((int)e.Size).ToArray(); } return Chain(e.Start, _fat).SelectMany(Sector).Take((int)e.Size).ToArray(); }
        private byte[] Sector(uint sid) { var off = _sectorSize + (long)sid * _sectorSize; if (off < 0 || off + _sectorSize > _data.Length) throw new ThtFormatException($"Sector OLE {sid} vượt quá kích thước file."); return _data.AsSpan((int)off, _sectorSize).ToArray(); }
        private static List<uint> Chain(uint start, List<uint> table) { var r = new List<uint>(); var seen = new HashSet<uint>(); var sid = start; while (sid < 0xFFFFFFF0) { if (!seen.Add(sid) || sid >= table.Count) throw new ThtFormatException("OLE FAT chain không hợp lệ."); r.Add(sid); sid = table[(int)sid]; } return r; }
        private Entry EntryByName(string name) => _entries.FirstOrDefault(e => e.Name == name) ?? throw new ThtFormatException($"Không tìm thấy stream OLE: '{name}'"); private sealed record Entry(string Name, byte Type, uint Start, long Size);
    }
}
