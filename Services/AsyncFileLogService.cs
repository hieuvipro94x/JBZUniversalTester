using System.Threading.Channels;
using System.IO;

namespace JBZUniversalTester.Services;

public enum AppLogCategory
{
    Application,
    Board,
    Test,
    Error
}

public enum AppLogLevel
{
    Normal = 0,
    Diagnostic = 1,
    ProtocolTrace = 2
}

/// <summary>
/// Writer log không block luồng scan/UI. Mọi dòng được đưa vào Channel và
/// một reader duy nhất append ra file theo ngày.
/// </summary>
public sealed class AsyncFileLogService : IDisposable
{
    public static AsyncFileLogService Current { get; } = new();

    private readonly Channel<(AppLogCategory Category, DateTime Timestamp, string Message)> _channel =
        Channel.CreateUnbounded<(AppLogCategory, DateTime, string)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private readonly CancellationTokenSource _cts = new();
    private Task? _writerTask;
    private int _started;
    private int _disposed;

    public string RootDirectory { get; private set; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "Logs");

    public AppLogLevel Level { get; set; } = AppLogLevel.Normal;

    public void Initialize(string? rootDirectory = null)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        if (!string.IsNullOrWhiteSpace(rootDirectory))
        {
            RootDirectory = Path.IsPathRooted(rootDirectory)
                ? rootDirectory
                : Path.Combine(AppContext.BaseDirectory, rootDirectory);
        }

        foreach (string name in Enum.GetNames<AppLogCategory>())
            Directory.CreateDirectory(Path.Combine(RootDirectory, name));

        _writerTask = Task.Run(WriterLoopAsync);
        Write(AppLogCategory.Application, $"Log service initialized. Root={RootDirectory}");
    }

    public void Write(AppLogCategory category, string message, AppLogLevel level = AppLogLevel.Normal)
    {
        if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _disposed) != 0 || level > Level)
            return;

        string safe = string.IsNullOrWhiteSpace(message) ? "(empty)" : message.Trim();
        _channel.Writer.TryWrite((category, DateTime.Now, $"[{level}] {safe}"));
    }

    public void Application(string message, AppLogLevel level = AppLogLevel.Normal) => Write(AppLogCategory.Application, message, level);
    public void Board(string message, AppLogLevel level = AppLogLevel.Normal) => Write(AppLogCategory.Board, message, level);
    public void Test(string message, AppLogLevel level = AppLogLevel.Normal) => Write(AppLogCategory.Test, message, level);
    public void Error(string message) => Write(AppLogCategory.Error, message, AppLogLevel.Normal);

    private async Task WriterLoopAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_cts.Token))
            {
                string category = item.Category.ToString();
                string directory = Path.Combine(RootDirectory, category);
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"{category}_{item.Timestamp:yyyyMMdd}.log");
                string line = $"[{item.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {item.Message}{Environment.NewLine}";
                await File.AppendAllTextAsync(path, line, new System.Text.UTF8Encoding(false), _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Logging tuyệt đối không được làm process crash.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();
        try
        {
            if (_writerTask is not null && !_writerTask.Wait(TimeSpan.FromSeconds(2)))
                _cts.Cancel();
        }
        catch
        {
            _cts.Cancel();
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
