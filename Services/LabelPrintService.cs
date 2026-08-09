using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

public sealed class LabelPrintService
{
    public async Task<LabelPrintTransportResult> PrintPassLabelAsync(
        LabelPrintRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string epl = EplLabelService.BuildPassLabel(request);
        string previewDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Labels");
        Directory.CreateDirectory(previewDirectory);
        string previewPath = Path.Combine(
            previewDirectory,
            $"{request.Data.TestedAt:yyyyMMdd_HHmmssfff}_{SafeFilePart(request.Data.PartNumber)}_{request.CycleId}.epl");
        await File.WriteAllTextAsync(previewPath, epl, Encoding.ASCII, ct);

        if (!string.IsNullOrWhiteSpace(request.PrinterCom))
        {
            await PrintToComAsync(epl, request, ct);
            return new LabelPrintTransportResult(
                true,
                $"Printed {request.Copies} label(s) via {request.PrinterCom}. Preview: {previewPath}");
        }

        if (!string.IsNullOrWhiteSpace(request.PrinterName))
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < request.Copies; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    RawPrinter.Send(request.PrinterName.Trim(), epl);
                }
            }, ct);
            return new LabelPrintTransportResult(
                true,
                $"Printed {request.Copies} label(s) via Windows printer '{request.PrinterName}'. Preview: {previewPath}");
        }

        return new LabelPrintTransportResult(
            false,
            $"No printer configured. EPL preview saved: {previewPath}");
    }

    private static async Task PrintToComAsync(
        string epl,
        LabelPrintRequest request,
        CancellationToken ct)
    {
        using var port = new SerialPort(
            request.PrinterCom.Trim(),
            request.BaudRate,
            Parity.None,
            8,
            StopBits.One)
        {
            Handshake = Handshake.None,
            Encoding = Encoding.ASCII,
            WriteTimeout = request.WriteTimeoutMs,
            DtrEnable = false,
            RtsEnable = false
        };

        await Task.Run(() => port.Open(), ct);
        byte[] bytes = Encoding.ASCII.GetBytes(epl);

        for (int i = 0; i < request.Copies; i++)
        {
            ct.ThrowIfCancellationRequested();
            await port.BaseStream.WriteAsync(bytes, ct);
            await port.BaseStream.FlushAsync(ct);
        }
    }

    private static string SafeFilePart(string? value)
    {
        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string safe = new((value ?? string.Empty)
            .Where(character => !invalid.Contains(character))
            .Take(60)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "NO_PART_NUMBER" : safe;
    }

    private static class RawPrinter
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class DocInfo1
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pDocName = "JBZ Label";
            [MarshalAs(UnmanagedType.LPWStr)] public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDataType = "RAW";
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] DocInfo1 di);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        public static void Send(string printerName, string text)
        {
            if (!OpenPrinter(printerName, out IntPtr printer, IntPtr.Zero))
                throw new InvalidOperationException($"Cannot open Windows printer '{printerName}'. Win32={Marshal.GetLastWin32Error()}");

            IntPtr unmanaged = IntPtr.Zero;
            try
            {
                var info = new DocInfo1();
                if (StartDocPrinter(printer, 1, info) == 0)
                    throw new InvalidOperationException($"StartDocPrinter failed. Win32={Marshal.GetLastWin32Error()}");

                try
                {
                    if (!StartPagePrinter(printer))
                        throw new InvalidOperationException($"StartPagePrinter failed. Win32={Marshal.GetLastWin32Error()}");

                    try
                    {
                        byte[] bytes = Encoding.ASCII.GetBytes(text);
                        unmanaged = Marshal.AllocCoTaskMem(bytes.Length);
                        Marshal.Copy(bytes, 0, unmanaged, bytes.Length);

                        if (!WritePrinter(printer, unmanaged, bytes.Length, out int written) || written != bytes.Length)
                            throw new InvalidOperationException($"WritePrinter failed. Win32={Marshal.GetLastWin32Error()}");
                    }
                    finally
                    {
                        EndPagePrinter(printer);
                    }
                }
                finally
                {
                    EndDocPrinter(printer);
                }
            }
            finally
            {
                if (unmanaged != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(unmanaged);
                ClosePrinter(printer);
            }
        }
    }
}
