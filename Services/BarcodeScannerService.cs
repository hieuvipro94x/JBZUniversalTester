using System.IO.Ports;
using System.Text;

namespace JBZUniversalTester.Services;

public sealed class BarcodeScannerService : IDisposable
{
    private readonly object _gate = new();
    private readonly StringBuilder _buffer = new();
    private SerialPort? _port;
    public event EventHandler<string>? BarcodeReceived;
    public bool IsConnected => _port?.IsOpen == true;

    public void Connect(string portName, int baudRate)
    {
        if (string.IsNullOrWhiteSpace(portName))
            throw new InvalidOperationException("Chưa cấu hình COM cho máy quét barcode.");
        Disconnect();
        var port = new SerialPort(portName.Trim(), baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            Encoding = Encoding.ASCII,
            NewLine = "\r\n",
            ReadTimeout = 500
        };
        port.DataReceived += OnDataReceived;
        try { port.Open(); _port = port; }
        catch { port.DataReceived -= OnDataReceived; port.Dispose(); throw; }
    }

    private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        if (sender is not SerialPort port) return;
        try
        {
            string chunk = port.ReadExisting();
            var completed = new List<string>();
            lock (_gate)
            {
                _buffer.Append(chunk);
                while (TryTakeLine(out string value)) completed.Add(value);
            }
            foreach (string value in completed) BarcodeReceived?.Invoke(this, value);
        }
        catch (Exception ex)
        {
            AsyncFileLogService.Current.Error($"BARCODE_RX_ERROR port={port.PortName} error={ex.Message}");
        }
    }

    private bool TryTakeLine(out string value)
    {
        string text = _buffer.ToString();
        int end = text.IndexOfAny(['\r', '\n']);
        if (end < 0) { value = string.Empty; return false; }
        value = text[..end].Trim();
        int consumed = end;
        while (consumed < text.Length && text[consumed] is '\r' or '\n') consumed++;
        _buffer.Remove(0, consumed);
        return value.Length > 0 || TryTakeLine(out value);
    }

    public void Disconnect()
    {
        SerialPort? port = _port;
        _port = null;
        if (port is null) return;
        port.DataReceived -= OnDataReceived;
        try { if (port.IsOpen) port.Close(); } finally { port.Dispose(); }
        lock (_gate) _buffer.Clear();
    }

    public void Dispose() => Disconnect();
}
