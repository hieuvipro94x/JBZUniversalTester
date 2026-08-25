using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using JBZUniversalTester.Models;

namespace JBZUniversalTester.Services;

/// <summary>
/// UART/RS232 client for the waterproof/leak tester. This service owns only the
/// leak COM port and never touches the D2XX board transport.
/// </summary>
public sealed class WaterProofSerialService : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _closeGate = new();
    private SerialPort? _port;
    private Task _pendingCloseTask = Task.CompletedTask;
    private string _connectedPort = string.Empty;
    private int _connectedBaud;
    private int _runSequence;

    public event EventHandler<string>? Log;

    public bool IsConnected => _port is { IsOpen: true };
    public string ConnectedPort => IsConnected ? _connectedPort : string.Empty;

    public async Task EnsureConnectedAsync(
        WaterProofMachineSettings machine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureConnectedCoreAsync(machine, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WaterProofRunResult> RunTestAsync(
        WaterProofMachineSettings machine,
        WaterProofModelSettings profile,
        Action<WaterProofProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(profile);

        Task<WaterProofRunResult> worker = Task.Run(
            () => RunTestWorkerAsync(machine, profile, progress, cancellationToken),
            CancellationToken.None);
        int watchdogMs = Math.Max(
            13_000,
            profile.PressTimeMs + profile.WaitTimeMs + 13_000);

        try
        {
            WaterProofRunResult result = await worker.WaitAsync(
                TimeSpan.FromMilliseconds(watchdogMs),
                cancellationToken).ConfigureAwait(false);

            // Mỗi sản phẩm dùng một phiên COM độc lập. Máy Leak có thể giữ
            // trạng thái sau :RESULT; không tái sử dụng handle của lượt trước.
            await DisconnectAsync(cancellationToken).ConfigureAwait(false);
            RaiseLog("[WP] COMPLETED SESSION CLOSED - next run will reconnect cleanly");
            return result;
        }
        catch (TimeoutException ex)
        {
            AbortActiveRun();
            ObserveLateWorker(worker);
            throw new TimeoutException(
                $"Máy Leak/driver COM không phản hồi trong {watchdogMs / 1000.0:0.#} giây.",
                ex);
        }
        catch (OperationCanceledException)
        {
            AbortActiveRun();
            ObserveLateWorker(worker);
            throw;
        }
    }

    private static void ObserveLateWorker(Task worker) =>
        _ = worker.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task<WaterProofRunResult> RunTestWorkerAsync(
        WaterProofMachineSettings machine,
        WaterProofModelSettings profile,
        Action<WaterProofProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.Enabled)
            throw new InvalidOperationException("Model hien tai chua bat kiem tra kin nuoc.");
        if (profile.EnabledChannelCount == 0)
            throw new InvalidOperationException("Kiem tra kin nuoc da bat nhung chua chon CH1/CH2/CH3.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        int runNumber = Interlocked.Increment(ref _runSequence);
        Stopwatch runWatch = Stopwatch.StartNew();
        Stopwatch lastRxWatch = Stopwatch.StartNew();

        try
        {
            RaiseLog($"[WP] RUN #{runNumber} START");
            await EnsureConnectedCoreAsync(machine, cancellationToken).ConfigureAwait(false);
            SerialPort port = _port ?? throw new InvalidOperationException("Cong may leak chua ket noi.");

            RaiseLog(
                $"[WP] COM state before test port={_connectedPort} baud={_connectedBaud} " +
                $"isOpen={port.IsOpen} readTimeout={port.ReadTimeout} writeTimeout={port.WriteTimeout}");

            ClearStaleSerialBuffers(port);

            string command = BuildTestCommand(profile);
            RaiseLog($"[WP] TX {command.Trim()}");
            port.Write(command);

            var buffer = new StringBuilder();
            int timeoutMs = Math.Max(
                10_000,
                profile.PressTimeMs + profile.WaitTimeMs + 10_000);

            while (runWatch.ElapsedMilliseconds < timeoutMs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string chunk = port.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    lastRxWatch.Restart();
                    buffer.Append(chunk);

                    foreach (string line in ExtractCompleteLines(buffer))
                    {
                        WaterProofRunResult? final = ProcessLine(runNumber, line, profile, progress);
                        if (final is not null)
                        {
                            RaiseLog($"[WP] RUN #{runNumber} COMPLETE elapsed_ms={runWatch.ElapsedMilliseconds}");
                            return final;
                        }
                    }

                    string pending = buffer.ToString().Trim();
                    if (pending.StartsWith(":RESULT", StringComparison.OrdinalIgnoreCase) &&
                        pending.Split(',').Length >= 7)
                    {
                        buffer.Clear();
                        WaterProofRunResult? final = ProcessLine(runNumber, pending, profile, progress);
                        if (final is not null)
                        {
                            RaiseLog($"[WP] RUN #{runNumber} COMPLETE elapsed_ms={runWatch.ElapsedMilliseconds}");
                            return final;
                        }
                    }
                }

                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"Qua thoi gian {timeoutMs / 1000.0:0} giay cho :RESULT tu may leak.");
        }
        catch (Exception ex) when (IsRecoverableSerialFault(ex))
        {
            RaiseLog($"[WP] RUN #{runNumber} {(ex is TimeoutException ? "TIMEOUT" : "ERROR")} {ex.GetType().Name}: {ex.Message}");
            RaiseLog($"[WP] last RX age={lastRxWatch.ElapsedMilliseconds}ms");
            RaiseLog($"[WP] serial IsOpen={(_port?.IsOpen == true)}");
            RaiseLog("[WP] INVALIDATE COM");
            DisconnectCore();
            throw;
        }
        finally
        {
            RaiseLog($"[WP] RUN #{runNumber} LOCK RELEASED");
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        AbortActiveRun();
        await WaitForPendingCloseAsync(cancellationToken).ConfigureAwait(false);

        bool entered = await _gate.WaitAsync(
            TimeSpan.FromSeconds(2),
            cancellationToken).ConfigureAwait(false);
        if (!entered)
        {
            RaiseLog("[WP] DISCONNECT TIMEOUT - COM đã được tách khỏi service; không chờ driver vô hạn.");
            return;
        }

        try
        {
            DisconnectCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void AbortActiveRun()
    {
        SerialPort? port = Interlocked.Exchange(ref _port, null);
        _connectedPort = string.Empty;
        _connectedBaud = 0;
        if (port is null)
            return;

        RaiseLog("[WP] ABORT ACTIVE COM - yêu cầu hủy I/O đang chờ.");
        ScheduleClose(port);
    }

    private async Task EnsureConnectedCoreAsync(
        WaterProofMachineSettings machine,
        CancellationToken cancellationToken)
    {
        string portName = (machine.PortName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(portName))
            throw new InvalidOperationException("Chua cau hinh cong COM UART/RS232 cho may leak.");
        if (machine.BaudRate <= 0)
            throw new InvalidOperationException("Baudrate may leak khong hop le.");

        if (_port is { IsOpen: true } &&
            string.Equals(_connectedPort, portName, StringComparison.OrdinalIgnoreCase) &&
            _connectedBaud == machine.BaudRate)
        {
            return;
        }

        DisconnectCore();
        await WaitForPendingCloseAsync(cancellationToken).ConfigureAwait(false);

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var port = new SerialPort(portName, machine.BaudRate, Parity.None, 8, StopBits.One)
            {
                Encoding = Encoding.ASCII,
                NewLine = "\r\n",
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = false,
                ReadTimeout = Math.Clamp(machine.ReadTimeoutMs, 100, 30_000),
                WriteTimeout = Math.Clamp(machine.WriteTimeoutMs, 100, 30_000)
            };

            try
            {
                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
                _port = port;
                _connectedPort = portName;
                _connectedBaud = machine.BaudRate;
            }
            catch
            {
                port.Dispose();
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);

        RaiseLog($"CONNECTED {portName} {machine.BaudRate} 8N1 ASCII");
    }

    private WaterProofRunResult? ProcessLine(
        int runNumber,
        string rawLine,
        WaterProofModelSettings profile,
        Action<WaterProofProgress>? progress)
    {
        string line = rawLine.Trim();
        if (line.Length == 0)
            return null;

        if (TryParseTriple(line, ":PRESS", out double[] press))
        {
            RaiseLog($"[WP] RUN #{runNumber} RX PRESS {line}");
            progress?.Invoke(new WaterProofProgress(WaterProofStage.Pressurizing, press, line));
            return null;
        }

        if (TryParseTriple(line, ":WAIT", out double[] wait))
        {
            RaiseLog($"[WP] RUN #{runNumber} RX WAIT {line}");
            progress?.Invoke(new WaterProofProgress(WaterProofStage.Waiting, wait, line));
            return null;
        }

        if (!TryParseResult(line, out double[] result))
            return null;

        RaiseLog($"[WP] RUN #{runNumber} RX RESULT {line}");
        progress?.Invoke(new WaterProofProgress(WaterProofStage.Evaluating, result, line));

        var channels = new List<WaterProofChannelMeasurement>(3);
        bool allPassed = true;
        for (int channel = 1; channel <= 3; channel++)
        {
            bool enabled = profile.IsChannelEnabled(channel);
            int offset = (channel - 1) * 2;
            double first = Math.Abs(result[offset]);
            double second = Math.Abs(result[offset + 1]);
            double leak = Math.Abs(first - second);

            bool passed = !enabled ||
                          (Math.Abs(second) >= Math.Abs(profile.PressMin) &&
                           leak <= profile.LeakLimit);

            channels.Add(new WaterProofChannelMeasurement(
                channel,
                enabled,
                first,
                second,
                leak,
                passed));

            if (enabled && !passed)
                allPassed = false;
        }

        return new WaterProofRunResult(channels, allPassed, line);
    }

    public static string BuildTestCommand(WaterProofModelSettings profile) =>
        $":TEST,{(profile.Channel1Enabled ? 1 : 0)}," +
        $"{(profile.Channel2Enabled ? 1 : 0)}," +
        $"{(profile.Channel3Enabled ? 1 : 0)},0," +
        $"{profile.PressTimeMs},{profile.WaitTimeMs}\r\n";

    private static bool TryParseTriple(string line, string prefix, out double[] values)
    {
        values = [];
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string[] parts = line[prefix.Length..]
            .Trim()
            .TrimStart(',')
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;

        values = new double[3];
        for (int i = 0; i < 3; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                values = [];
                return false;
            }
            values[i] = Math.Abs(values[i]);
        }
        return true;
    }

    private static bool TryParseResult(string line, out double[] values)
    {
        values = [];
        const string prefix = ":RESULT";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string[] parts = line[prefix.Length..]
            .Trim()
            .TrimStart(',')
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 6)
            throw new InvalidDataException($"RESULT may leak can 6 gia tri, nhan {parts.Length}: {line}");

        values = new double[6];
        for (int i = 0; i < 6; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                throw new InvalidDataException($"RESULT may leak co gia tri khong hop le '{parts[i]}'.");
            values[i] = Math.Abs(values[i]);
        }
        return true;
    }

    private static IEnumerable<string> ExtractCompleteLines(StringBuilder buffer)
    {
        while (true)
        {
            string text = buffer.ToString();
            int index = text.IndexOfAny(['\r', '\n']);
            if (index < 0)
                yield break;

            string line = text[..index];
            int remove = index + 1;
            while (remove < text.Length && (text[remove] == '\r' || text[remove] == '\n'))
                remove++;

            buffer.Remove(0, remove);
            if (!string.IsNullOrWhiteSpace(line))
                yield return line;
        }
    }

    private void ClearStaleSerialBuffers(SerialPort port)
    {
        try
        {
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
        }
        catch (Exception ex)
        {
            RaiseLog($"[WP] purge stale RX/TX skipped: {ex.Message}");
        }
    }

    private void DisconnectCore()
    {
        SerialPort? port = Interlocked.Exchange(ref _port, null);
        _connectedPort = string.Empty;
        _connectedBaud = 0;

        if (port is null)
            return;

        ScheduleClose(port);
    }

    private void ScheduleClose(SerialPort port)
    {
        Task closeTask = Task.Run(() => CloseAndDispose(port));
        lock (_closeGate)
            _pendingCloseTask = closeTask;
    }

    private async Task WaitForPendingCloseAsync(CancellationToken cancellationToken)
    {
        Task closeTask;
        lock (_closeGate)
            closeTask = _pendingCloseTask;

        if (closeTask.IsCompleted)
            return;

        try
        {
            await closeTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                "Cổng COM máy Leak chưa nhả handle cũ trong 2 giây; không mở chồng phiên mới.",
                ex);
        }
    }

    private void CloseAndDispose(SerialPort port)
    {
        try
        {
            if (port.IsOpen)
                port.Close();
        }
        catch (Exception ex)
        {
            RaiseLog($"[WP] close COM skipped: {ex.Message}");
        }
        finally
        {
            try
            {
                port.Dispose();
            }
            catch (Exception ex)
            {
                RaiseLog($"[WP] dispose COM skipped: {ex.Message}");
            }
        }
    }

    private static bool IsRecoverableSerialFault(Exception ex) =>
        ex is IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException;

    private void RaiseLog(string text) => Log?.Invoke(this, text);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisconnectAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        // Không Dispose semaphore khi một driver COM lỗi vẫn còn giữ worker.
        // Process có thể thoát ngay; worker nền sẽ tự release nếu driver trả về.
    }
}
