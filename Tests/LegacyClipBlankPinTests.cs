using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;
using JBZUniversalTester.ModelEditor.Models;
using JBZUniversalTester.ModelEditor.Services;
using JBZUniversalTester.Services;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestLegacyClipBlankPinTypes()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-blank-clip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "blank-clip.tht");
            var document = new ModelDocument
            {
                SourceFormat = "tht", Base = new() { Model = "CLIP-TEST", Number = "CLIP-TEST", Name = "ORIGINAL" },
                Connectors = [new() { Name = "CN1", PinCount = 1, StartIo = 1 },
                    new() { Name = "CN2", PinCount = 1, StartIo = 2 },
                    new() { Name = "CLIP", PinCount = 6, StartIo = 196 }],
                Pins = [new() { Io = 1, Connector = "CN1", PinNo = 1, Line = "W1" },
                    new() { Io = 2, Connector = "CN2", PinNo = 1, Line = "W1" }]
            };
            document.Pins.AddRange(Enumerable.Range(0, 6).Select(index => new Pin
            {
                Io = 196 + index, Connector = "CLIP", PinNo = index + 1,
                Line = index == 0 ? "AO" : $"a{index}"
            }));
            ModelLogic.RebuildConnections(document.Pins);
            IntegratedThtStore.Save(document, path, null);
            var canonical = ThtFileService.Load(path);
            string text = ThtFileService.ModelToThtText(canonical)
                .Replace("\t197\t\ta1\t", "\t197\t\t\t", StringComparison.Ordinal)
                .Replace("\t199\t\ta3\t", "\t199\t\t\t", StringComparison.Ordinal);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(new byte[12]); writer.Write(canonical.ThtState!.Token);
                WriteCString(writer, Encoding.ASCII.GetBytes(canonical.ThtState.Version));
                WriteCString(writer, Encoding.GetEncoding(949).GetBytes(text));
                writer.Write(canonical.ThtState.Tail);
            }
            byte[] contents = stream.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(0), (uint)(contents.Length - 8));
            BinaryPrimitives.WriteUInt32LittleEndian(contents.AsSpan(4), (uint)(contents.Length - 20));
            typeof(ThtModelParser).GetMethod("WriteDiscardContents", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [path, contents]);
            VerifyLegacyClipBlankFile(path);

            var loaded = ThtFileService.Load(path);
            var invalidRows = loaded.ThtState!.PinRows.Select(row => row.ToList()).ToList();
            invalidRows.Single(row => row[2] == "198")[4] = "";
            bool rejected = false;
            try
            {
                typeof(ThtFileService).GetMethod("ValidateClipPinTypeColumn", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [loaded, invalidRows]);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is ThtExportException) { rejected = true; }
            Assert(rejected, "Legacy blank compatibility cannot accept accidental loss of an originally named branch");
        }
        finally { Directory.Delete(root, recursive: true); }

        static void WriteCString(BinaryWriter writer, byte[] bytes)
        {
            if (bytes.Length < 255) writer.Write((byte)bytes.Length);
            else { writer.Write((byte)255); writer.Write((ushort)bytes.Length); }
            writer.Write(bytes);
        }
    }

    private static void VerifyLegacyClipBlankFile(string source)
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-blank-clip-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] original = File.ReadAllBytes(source);
        try
        {
            string copy = Path.Combine(root, Path.GetFileName(source));
            File.WriteAllBytes(copy, original);
            var before = ThtFileService.Load(copy);
            var parser = new ThtModelParser();
            var productBefore = parser.Load(copy);
            var blanks = before.ThtState!.PinRows.Where(row => row[0] == "" &&
                row[4] == "" && before.Pins.Any(pin => pin.Connector == "CLIP" && pin.Io.ToString() == row[2]))
                .Select(row => row[2]).ToArray();
            Assert(blanks.Length > 0, "Regression fixture must contain unnamed legacy CLIP branches");
            var saved = IntegratedThtStore.Save(before.Clone(), copy, IntegratedThtStore.Fingerprint(copy));
            Assert(blanks.All(io => saved.ThtState!.PinRows.Single(row => row[2] == io)[4] == ""),
                "Saving without CLIP edits must leave every originally unnamed branch unnamed");
            saved.Base.Name = "METADATA EDIT ONLY";
            var edited = IntegratedThtStore.Save(saved, copy, IntegratedThtStore.Fingerprint(copy));
            Assert(JsonSerializer.Serialize(edited.ThtState!.PinRows) == JsonSerializer.Serialize(before.ThtState.PinRows) &&
                   edited.ThtState.Tail.SequenceEqual(before.ThtState.Tail) &&
                   edited.ThtState.SummaryInformation.SequenceEqual(before.ThtState.SummaryInformation),
                "Metadata edits preserve all raw PIN rows, blank labels, binary tail and OLE summary");
            var productAfter = parser.Load(copy);
            Assert(JsonSerializer.Serialize(productBefore.Clip) == JsonSerializer.Serialize(productAfter.Clip) &&
                   productBefore.Pins.SequenceEqual(productAfter.Pins),
                "An unnamed CLIP branch must not become a newly active aN branch in the runtime parser");
            Assert(Directory.GetFiles(Path.Combine(root, "ThtBackups"), "*.tht")
                       .Any(backup => File.ReadAllBytes(backup).SequenceEqual(original)) &&
                   File.ReadAllBytes(source).SequenceEqual(original),
                "Saving retains an exact pre-edit backup and never changes the supplied source fixture");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
