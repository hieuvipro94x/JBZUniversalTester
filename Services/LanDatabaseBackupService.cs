using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using JBZUniversalTester.Models;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace JBZUniversalTester.Services;

/// <summary>One background worker; never opens the production database over SMB.</summary>
public sealed class LanDatabaseBackupService : IDisposable
{
    private sealed record Configuration(bool Enabled, string Ip, string Share);
    private readonly string _databasePath;
    private readonly Func<string, CancellationToken, Task<bool>> _canConnect;
    private readonly Func<string, string, string> _destination;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<string> _getMachineCode;
    private string _machineCode = string.Empty;
    private readonly string _statePath;
    private Dictionary<string, DateTime>? _completed;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _attempt = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private Configuration _configuration = new(false, string.Empty, "JBZBackup");
    private Task? _worker;
    private int _disposed;
    private string _status = "Chưa khởi động sao lưu LAN.";
    public string Status => Volatile.Read(ref _status);
    public string MachineCode => Volatile.Read(ref _machineCode);

    public LanDatabaseBackupService(string databasePath,
        Func<string, CancellationToken, Task<bool>>? canConnect = null,
        Func<string, string, string>? destination = null,
        Func<DateTime>? utcNow = null,
        Func<string>? getMachineCode = null)
    {
        _databasePath = databasePath;
        _canConnect = canConnect ?? CanConnectAsync;
        _destination = destination ?? ((ip, share) => BuildDestination(ip, share, MachineCode));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _getMachineCode = getMachineCode ?? (() => StationIdentityService.GetOrCreateMachineCode());
        _statePath = databasePath + ".lan-backup.json";
    }

    public void Configure(ProductionSettings settings)
    {
        var configuration = new Configuration(settings.LanBackupEnabled,
            (settings.LanBackupServerIp ?? string.Empty).Trim(),
            (settings.LanBackupShareName ?? string.Empty).Trim());
        if (configuration == Volatile.Read(ref _configuration))
            return;
        Volatile.Write(ref _configuration, configuration);
        Volatile.Write(ref _status, configuration.Enabled ? "Đang chờ kiểm tra lịch sao lưu..." : "Sao lưu LAN đã tắt.");
        if (_wake.CurrentCount == 0)
        {
            try { _wake.Release(); }
            catch (SemaphoreFullException) { /* Another settings save already woke the worker. */ }
        }
    }

    public void Start(ProductionSettings settings)
    {
        Configure(settings);
        _worker ??= Task.Run(() => RunAsync(_stop.Token));
    }

    public static string ValidateDestination(string serverIp, string shareName)
    {
        if (!IPAddress.TryParse(serverIp.Trim(), out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            throw new ArgumentException("IP máy chủ LAN không hợp lệ. Ví dụ: 192.168.10.150.");
        string share = shareName.Trim();
        if (share.Length == 0 || share is "." or ".." ||
            share.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Nhập tên thư mục chia sẻ trên máy chủ, ví dụ JBZBackup.");
        return $@"\\{address}\{share}";
    }

    public static string BuildDestination(string serverIp, string shareName, string machineCode)
    {
        string root = ValidateDestination(serverIp, shareName);
        if (!machineCode.StartsWith("MAY-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(machineCode[4..], "N", out Guid id) || id == Guid.Empty)
            throw new InvalidDataException("Chưa có mã máy hợp lệ để sao lưu LAN.");
        return root + "\\" + machineCode;
    }

    private static async Task<bool> CanConnectAsync(string serverIp, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        using var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync(IPAddress.Parse(serverIp), 445, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (MachineCode.Length == 0)
                        Volatile.Write(ref _machineCode, _getMachineCode());
                    await BackupOnceAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    Volatile.Write(ref _status, $"Chưa sao lưu được; sẽ thử lại: {ex.Message}");
                    AsyncFileLogService.Current.Error($"LAN_BACKUP_FAILED: {ex.Message}");
                }
                await _wake.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async Task<bool> BackupOnceAsync(CancellationToken ct = default)
    {
        if (!await _attempt.WaitAsync(0, ct).ConfigureAwait(false))
            return false;
        string? localSnapshot = null;
        string? remoteTemporary = null;
        try
        {
            Configuration configuration = Volatile.Read(ref _configuration);
            if (!configuration.Enabled || configuration.Ip.Length == 0)
            {
                Volatile.Write(ref _status, configuration.Enabled ? "Chưa nhập IP máy chủ." : "Sao lưu LAN đã tắt.");
                return false;
            }
            if (MachineCode.Length == 0)
                Volatile.Write(ref _machineCode, _getMachineCode());
            string destinationKey = BuildDestination(configuration.Ip, configuration.Share, MachineCode).ToUpperInvariant();
            LoadCompletedBackups();
            if (_completed!.TryGetValue(destinationKey, out DateTime completedAt) &&
                _utcNow() < completedAt.AddMonths(1))
            {
                Volatile.Write(ref _status, $"Đã sao lưu: {completedAt.ToLocalTime():dd/MM/yyyy HH:mm}. Lần tới: {completedAt.AddMonths(1).ToLocalTime():dd/MM/yyyy}.");
                return false;
            }
            if (!await _canConnect(configuration.Ip, ct).ConfigureAwait(false))
            {
                Volatile.Write(ref _status, "Đến hạn sao lưu; đang chờ kết nối máy chủ LAN.");
                return false;
            }
            ct.ThrowIfCancellationRequested();
            if (configuration != Volatile.Read(ref _configuration))
                return false;

            // Check SMB write access before reading/snapshotting the database.
            string directory = _destination(configuration.Ip, configuration.Share);
            Volatile.Write(ref _status, "Đang kết nối thư mục chia sẻ...");
            Directory.CreateDirectory(directory);
            remoteTemporary = Path.Combine(directory, $".JBZUniversalTester.{Guid.NewGuid():N}.upload");
            await using (var remote = new FileStream(remoteTemporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(_databasePath))
                {
                    Volatile.Write(ref _status, "Chưa có database để sao lưu.");
                    return false;
                }
                Volatile.Write(ref _status, "Đang tạo bản sao database ở nền...");
                localSnapshot = Path.Combine(Path.GetTempPath(), $"JBZLanBackup-{Guid.NewGuid():N}.db");
                await CreateSnapshotAsync(_databasePath, localSnapshot, ct).ConfigureAwait(false);
                await using var local = new FileStream(localSnapshot, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                Volatile.Write(ref _status, "Đang gửi backup lên máy chủ; vui lòng giữ kết nối mạng.");
                await local.CopyToAsync(remote, 64 * 1024, ct).ConfigureAwait(false);
                await remote.FlushAsync(ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            if (configuration != Volatile.Read(ref _configuration))
                return false;
            File.Move(remoteTemporary, Path.Combine(directory, "JBZUniversalTester.db"), overwrite: true);
            remoteTemporary = null;
            _completed[destinationKey] = _utcNow();
            SaveCompletedBackups();
            Volatile.Write(ref _status, $"Sao lưu thành công: {_completed[destinationKey].ToLocalTime():dd/MM/yyyy HH:mm}.");
            AsyncFileLogService.Current.Application($"LAN_BACKUP_OK path={directory}");
            return true;
        }
        finally
        {
            DeleteTemporary(localSnapshot);
            DeleteTemporary(remoteTemporary);
            _attempt.Release();
        }
    }

    private void LoadCompletedBackups()
    {
        if (_completed is not null)
            return;
        _completed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(_statePath))
                _completed = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(
                    File.ReadAllText(_statePath)) ?? _completed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AsyncFileLogService.Current.Error($"LAN_BACKUP_STATE_READ_FAILED: {ex.Message}");
        }
    }

    private void SaveCompletedBackups()
    {
        string temporary = _statePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(_completed));
            File.Move(temporary, _statePath, overwrite: true);
            File.SetAttributes(_statePath, File.GetAttributes(_statePath) | FileAttributes.Hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AsyncFileLogService.Current.Error($"LAN_BACKUP_STATE_WRITE_FAILED: {ex.Message}");
        }
        finally { DeleteTemporary(temporary); }
    }

    public static async Task CreateSnapshotAsync(string databasePath, string snapshotPath, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        ct = deadline.Token;
        ct.ThrowIfCancellationRequested();
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, DefaultTimeout = 1
        }.ToString());
        source.Open();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath, Pooling = false, DefaultTimeout = 1
        }.ToString());
        destination.Open();
        // Copy bounded page batches and release the source read lock between them.
        // Do not pin a source transaction or wait/retry on a busy production writer.
        using (sqlite3_backup backup = raw.sqlite3_backup_init(destination.Handle!, "main", source.Handle!, "main"))
        {
            if (backup.IsInvalid)
                throw new SqliteException(raw.sqlite3_errmsg(destination.Handle!).utf8_to_string(),
                    raw.sqlite3_errcode(destination.Handle!));
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int result = raw.sqlite3_backup_step(backup, 128);
                if (result == raw.SQLITE_DONE)
                    break;
                if (result != raw.SQLITE_OK)
                    throw new SqliteException(raw.sqlite3_errmsg(destination.Handle!).utf8_to_string(), result);
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
            int finish = raw.sqlite3_backup_finish(backup);
            if (finish != raw.SQLITE_OK)
                throw new SqliteException(raw.sqlite3_errmsg(destination.Handle!).utf8_to_string(), finish);
        }
        ct.ThrowIfCancellationRequested();
        using SqliteCommand check = destination.CreateCommand();
        check.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("LAN backup snapshot failed SQLite integrity_check.");
    }

    private static void DeleteTemporary(string? path)
    {
        if (path is null)
            return;
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AsyncFileLogService.Current.Error($"LAN_BACKUP_TEMP_CLEANUP_FAILED path={path}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _stop.Cancel();
        // Never wait on SMB I/O or backup completion on the WPF shutdown thread.
    }
}
