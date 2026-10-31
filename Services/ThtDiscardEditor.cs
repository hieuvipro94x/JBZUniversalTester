using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed partial class ThtModelParser
{
    private static readonly object DiscardEditGate = new();

    public static void ValidateDiscardContacts(int first, int second, int capacity = BoardCapacity.MaxGlobalIo)
    {
        if (first == 0 && second == 0) return;
        if (first < 1 || second < 1 || first == second || first > capacity || second > capacity)
            throw new InvalidOperationException($"Hai IO thùng lỗi phải khác nhau, trong khoảng 1–{capacity}. Để trống cả hai nếu muốn giữ nguyên file THT.");
    }

    /// <summary>Legacy fill-if-absent helper. Product selection and ordinary parsing stay read-only.</summary>
    public string? EnsureDiscardContacts(string path, int first, int second)
        => EditDiscardContacts(path, first, second, replaceExisting: false);

    /// <summary>Explicit Settings save edits only the selected product's native THT.</summary>
    public string? SaveDiscardContacts(string path, int first, int second)
        => EditDiscardContacts(path, first, second, replaceExisting: true);

    private string? EditDiscardContacts(string path, int first, int second, bool replaceExisting)
    {
        ValidateDiscardContacts(first, second);
        if (!replaceExisting && first == 0 && second == 0) return null;
        ValidatePath(path);
        if (!Path.GetExtension(path).Equals(".tht", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Chỉ cập nhật chân thùng lỗi trong file .tht.");
        lock (DiscardEditGate)
        {
            string full = Path.GetFullPath(path);
            using var sourceLock = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            byte[] originalFile = File.ReadAllBytes(full);
            var existing = LoadAll(full);
            if (existing.Any(model => model.DiscardContactIo.Count > 0))
            {
                if (!existing.All(model => model.HasDiscardInterlock))
                    throw new InvalidDataException("THT đã có _DISCARD nhưng không đủ đúng hai IO. Giữ nguyên file; hãy kiểm tra cấu hình thùng lỗi.");
                if (!replaceExisting) return null;
            }
            int[] expectedPair = first == 0 ? [] : new[] { first, second }.Order().ToArray();
            if (existing.All(model => model.DiscardContactIo.Order().SequenceEqual(expectedPair))) return null;
            string sidecar = full + ".leak.json";
            byte[]? originalSidecar = File.Exists(sidecar) ? File.ReadAllBytes(sidecar) : null;
            if (originalSidecar is not null)
                _ = ModelLeakLayoutService.Apply(full, new WaterProofModelSettings());
            byte[] contents = new CompoundFileReader(originalFile).ReadStream("Contents");
            using var input = new BinaryReader(new MemoryStream(contents), Encoding.UTF8);
            EnsureRemaining(input, 16, "header Contents");
            uint outer = input.ReadUInt32(), inner = input.ReadUInt32();
            input.ReadUInt32();
            if (input.ReadUInt32() != ExpectedDocumentToken)
                throw new InvalidDataException("File THT không đúng định dạng Htdrv đã hỗ trợ; giữ nguyên file.");
            // These two length conventions are present in the station's original V1.110 files.
            // Do not guess a different archive layout or rewrite its trailing metadata.
            if ((outer != 0 && outer != contents.Length - 8) || (inner != 0 && inner != contents.Length - 20))
                throw new InvalidDataException("Header độ dài THT chưa được hỗ trợ để sửa; giữ nguyên file.");
            ReadMfcCString(input, Encoding.ASCII);
            int textStart = checked((int)input.BaseStream.Position);
            var strictCp949 = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            string modelText = ReadMfcCString(input, strictCp949);
            int tailStart = checked((int)input.BaseStream.Position);
            int trailingNulls = 0;
            while (tailStart - trailingNulls > textStart && contents[tailStart - trailingNulls - 1] == 0) trailingNulls++;
            modelText += new string('\0', trailingNulls);
            ThtModelTables tables = ParseModelText(modelText);
            List<RawPin> pins = ParsePins(tables.Pins);
            if (!replaceExisting && !existing.All(model => model.HasDiscardInterlock) && tables.Connectors.Rows.Any(row => row.Get("커넥터", "Connector").Equals("_DISCARD", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("THT đã có connector _DISCARD nhưng chưa có đủ hai chân IO. Giữ nguyên file để kiểm tra.");
            foreach (RawPin pin in pins.Where(pin => pin.IoNumber == first || pin.IoNumber == second))
                if (!pin.Connector.Equals("_DISCARD", StringComparison.OrdinalIgnoreCase) &&
                    new[] { pin.Connector, pin.WireName, pin.PinNumber, pin.PinType, pin.WireConnection }.Any(value => !string.IsNullOrWhiteSpace(value)))
                    throw new InvalidOperationException($"IO {pin.IoNumber} đã dùng cho Connector {pin.Connector}, chân {pin.PinNumber}, dây {pin.WireName}. Không sửa THT để tránh mất kết nối sản phẩm.");
            string updatedText = PatchDiscardTables(modelText, first, second);
            if (updatedText == modelText) return null;

            using var updated = new MemoryStream();
            updated.Write(contents, 0, textStart);
            using (var writer = new BinaryWriter(updated, Encoding.UTF8, leaveOpen: true))
                WriteDiscardCString(writer, strictCp949.GetBytes(updatedText));
            updated.Write(contents, tailStart, contents.Length - tailStart);
            byte[] changedContents = updated.ToArray();
            if (outer != 0) BinaryPrimitives.WriteUInt32LittleEndian(changedContents, checked((uint)changedContents.Length - 8));
            if (inner != 0) BinaryPrimitives.WriteUInt32LittleEndian(changedContents.AsSpan(4), checked((uint)changedContents.Length - 20));

            string directory = Path.GetDirectoryName(full)!;
            string temporary = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp.tht");
            string? backup = null;
            try
            {
                File.WriteAllBytes(temporary, originalFile);
                var before = LoadAll(temporary);
                WriteDiscardContents(temporary, changedContents);
                if (!new CompoundFileReader(temporary).ReadStream("Contents").SequenceEqual(changedContents))
                    throw new InvalidDataException("Nội dung THT ghi lại không khớp; giữ nguyên file gốc.");
                // Read back with the production parser before the original can be replaced.
                var after = LoadAll(temporary);
                if (before.Count != after.Count || after.Any(model => !model.DiscardContactIo.Order().SequenceEqual(expectedPair)) ||
                    before.Zip(after).Any(pair => !pair.First.Pins.Select(pin => pin with { OriginalOrder = 0 }).SequenceEqual(
                            pair.Second.Pins.Select(pin => pin with { OriginalOrder = 0 })) ||
                        pair.First.PartNumber != pair.Second.PartNumber || pair.First.Eco != pair.Second.Eco ||
                        pair.First.Nco != pair.Second.Nco || pair.First.Alc != pair.Second.Alc ||
                        pair.First.LabelTemplate != pair.Second.LabelTemplate ||
                        !pair.First.ResistanceSteps.SequenceEqual(pair.Second.ResistanceSteps)))
                    throw new InvalidDataException("Kiểm tra THT sau sửa không đạt; giữ nguyên file gốc.");
                if (!SHA256.HashData(File.ReadAllBytes(full)).SequenceEqual(SHA256.HashData(originalFile)))
                    throw new IOException("File THT vừa bị thay đổi bởi chương trình khác; hãy chọn lại mã hàng.");
                string backups = Path.Combine(directory, "ThtBackups");
                Directory.CreateDirectory(backups);
                backup = Path.Combine(backups, $"{Path.GetFileNameWithoutExtension(full)}.{DateTime.Now:yyyyMMdd_HHmmss_fff}.{Guid.NewGuid():N}.tht");
                // Windows atomically replaces the file and retains the original as the backup.
                string? sidecarTemporary = null;
                try
                {
                    if (originalSidecar is not null)
                    {
                        // Only rebind a sidecar validated against the original THT.
                        // Preserve every channel, machine setting and unknown JSON field.
                        var json = System.Text.Json.Nodes.JsonNode.Parse(originalSidecar)!;
                        json["ModelSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(temporary)));
                        sidecarTemporary = sidecar + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllText(sidecarTemporary, json.ToJsonString());
                        if (!File.ReadAllBytes(sidecar).SequenceEqual(originalSidecar))
                            throw new IOException("Cấu hình Leak vừa thay đổi; giữ nguyên THT và thử lại.");
                    }
                    File.Replace(temporary, full, backup);
                    try
                    {
                        if (sidecarTemporary is not null)
                            File.Replace(sidecarTemporary, sidecar, backup + ".leak.json");
                    }
                    catch
                    {
                        // Restore the exact original THT if its matching sidecar cannot be committed.
                        File.WriteAllBytes(temporary, originalFile);
                        File.Replace(temporary, full, null);
                        throw;
                    }
                }
                finally
                {
                    if (sidecarTemporary is not null && File.Exists(sidecarTemporary)) File.Delete(sidecarTemporary);
                }
                return backup;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static string PatchDiscardTables(string text, int first, int second)
    {
        var lines = text.Split('\n').ToList();
        bool pinsDone = false, connectorsDone = false;
        for (int start = 0; start < lines.Count; start++)
        {
            string[] headers = SplitColumns(lines[start].TrimEnd('\r')).Select(NormalizeHeader).ToArray();
            int Col(params string[] names) => Array.FindIndex(headers, header => names.Select(NormalizeHeader).Contains(header));
            int connector = Col("커넥터", "Connector");
            int io = Col("I/O", "IONo", "Ｉ／Ｏ");
            int pin = Col("핀번호", "PinNo");
            int count = Col("핀 수", "핀수", "PinCount");
            bool isPins = connector >= 0 && io >= 0 && pin >= 0;
            bool isConnectors = connector >= 0 && count >= 0 && io < 0;
            if (!isPins && !isConnectors) continue;
            if ((isPins && pinsDone) || (isConnectors && connectorsDone))
                throw new InvalidDataException("THT có bảng chân/connector lặp; không tự sửa định dạng chưa xác định.");
            int end = start + 1;
            while (end < lines.Count && !string.IsNullOrWhiteSpace(lines[end].TrimEnd('\r', '\0'))) end++;
            string ending = lines[start].EndsWith('\r') ? "\r" : string.Empty;
            var kept = lines.GetRange(start + 1, end - start - 1).Where(line =>
            {
                string[] cells = SplitColumns(line.TrimEnd('\r', '\0'));
                string Cell(int n) => n >= 0 && n < cells.Length ? cells[n].Trim() : string.Empty;
                if (Cell(connector).Equals("_DISCARD", StringComparison.OrdinalIgnoreCase)) return false;
                // An unused grid slot may contain only its IO number.
                return !isPins || !int.TryParse(Cell(io), out int value) || (value != first && value != second) ||
                    cells.Where((_, index) => index != io).Any(cell => !string.IsNullOrWhiteSpace(cell));
            }).ToList();
            string Row(int ioValue, int pinValue)
            {
                var cells = new string[headers.Length];
                Array.Fill(cells, string.Empty);
                cells[connector] = "_DISCARD";
                if (isPins) { cells[io] = ioValue.ToString(CultureInfo.InvariantCulture); cells[pin] = pinValue.ToString(CultureInfo.InvariantCulture); }
                if (count >= 0) cells[count] = "2";
                return string.Join('\t', cells) + ending;
            }
            if (first != 0)
            {
                kept.Add(Row(first, 1));
                if (isPins) kept.Add(Row(second, 2));
            }
            lines.RemoveRange(start + 1, end - start - 1);
            lines.InsertRange(start + 1, kept);
            if (isPins) pinsDone = true; else connectorsDone = true;
            start += kept.Count;
        }
        if (!pinsDone || !connectorsDone)
            throw new InvalidDataException("Không tìm thấy bảng chân và connector THT để cấu hình thùng lỗi.");
        // Split/join preserves all original row bytes and mixed line endings outside the edited rows.
        return string.Join('\n', lines);
    }

    private static void WriteDiscardCString(BinaryWriter writer, byte[] bytes)
    {
        if (bytes.Length < 255) writer.Write((byte)bytes.Length);
        else if (bytes.Length < 65535) { writer.Write((byte)255); writer.Write((ushort)bytes.Length); }
        else { writer.Write((byte)255); writer.Write(ushort.MaxValue); writer.Write(bytes.Length); }
        writer.Write(bytes);
    }

    private static void WriteDiscardContents(string path, byte[] bytes)
    {
        IDiscardStorage? storage = null;
        IStream? stream = null;
        try
        {
            Marshal.ThrowExceptionForHR(StgOpenStorage(path, IntPtr.Zero, 0x10012, IntPtr.Zero, 0, out storage));
            storage.OpenStream("Contents", IntPtr.Zero, 0x12, 0, out stream);
            stream.Seek(0, 0, IntPtr.Zero);
            stream.Write(bytes, bytes.Length, IntPtr.Zero);
            stream.SetSize(bytes.Length);
            stream.Commit(0);
            Marshal.FinalReleaseComObject(stream); stream = null;
            storage.Commit(0);
        }
        finally
        {
            if (stream is not null) Marshal.FinalReleaseComObject(stream);
            if (storage is not null) Marshal.FinalReleaseComObject(storage);
        }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgOpenStorage(string name, IntPtr priority, uint mode, IntPtr exclude, uint reserved, out IDiscardStorage storage);

    [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDiscardStorage
    {
        void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStream stream);
        void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr reserved1, uint mode, uint reserved2, out IStream stream);
        void CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IDiscardStorage storage);
        void OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr priority, uint mode, IntPtr exclude, uint reserved, out IDiscardStorage storage);
        void CopyTo(uint count, IntPtr excludeIids, IntPtr excludeNames, IDiscardStorage destination);
        void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string name, IDiscardStorage destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, uint flags);
        void Commit(uint flags);
    }
}
