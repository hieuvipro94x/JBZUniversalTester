using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace JBZUniversalTester.Services;

/// <summary>
/// Quản lý toàn bộ âm thanh của ứng dụng.
/// Các file WAV được đọc trực tiếp từ EmbeddedResource trong EXE.
/// </summary>
public sealed class AppSoundService : IDisposable
{
    private static readonly Lazy<AppSoundService> LazyInstance =
        new(() => new AppSoundService());

    private readonly object _gate = new();

    private SoundPlayer? _clickPlayer;
    private SoundPlayer? _testOkPlayer;
    private SoundPlayer? _startupPlayer;
    private SoundPlayer? _wiringFaultPlayer;

    // Giữ stream sống trong toàn bộ vòng đời SoundPlayer.
    private MemoryStream? _clickStream;
    private MemoryStream? _testOkStream;
    private MemoryStream? _startupStream;
    private MemoryStream? _wiringFaultStream;

    private bool _initialized;
    private bool _disposed;
    private bool _wiringFaultAlarmActive;

    private int _globalButtonHandlerRegistered;
    private int _startupPlayed;

    public static AppSoundService Current => LazyInstance.Value;

    public bool IsWiringFaultAlarmActive
    {
        get
        {
            lock (_gate)
            {
                return _wiringFaultAlarmActive;
            }
        }
    }

    private AppSoundService()
    {
    }

    /// <summary>
    /// Nạp tài nguyên âm thanh và đăng ký tiếng click cho toàn bộ Button WPF.
    /// Nên gọi một lần trong App.OnStartup().
    /// </summary>
    public void Initialize()
    {
        lock (_gate)
        {
            ThrowIfDisposed();

            if (!_initialized)
            {
                _clickPlayer = CreatePlayer("CLICK.wav", out _clickStream);
                _testOkPlayer = CreatePlayer("DINGDONG.wav", out _testOkStream);
                _startupPlayer = CreatePlayer("START.wav", out _startupStream);
                _wiringFaultPlayer = CreatePlayer("TESTPOINT.wav", out _wiringFaultStream);

                _initialized = true;
            }
        }

        if (Interlocked.Exchange(ref _globalButtonHandlerRegistered, 1) == 0)
        {
            EventManager.RegisterClassHandler(
                typeof(Button),
                Button.ClickEvent,
                new RoutedEventHandler(OnAnyButtonClick),
                handledEventsToo: true);
        }
    }

    /// <summary>
    /// Phát âm thanh khởi động đúng một lần trong mỗi lần chạy ứng dụng.
    /// </summary>
    public void PlayStartup()
    {
        EnsureInitialized();

        if (Interlocked.Exchange(ref _startupPlayed, 1) != 0)
        {
            return;
        }

        SafePlay(_startupPlayer);
    }

    public void PlayClick()
    {
        EnsureInitialized();
        SafePlay(_clickPlayer);
    }

    public void PlayTestOk()
    {
        EnsureInitialized();
        SafePlay(_testOkPlayer);
    }

    /// <summary>Phát TESTPOINT.wav một lần khi đầu dò firmware chuyển sang ON.</summary>
    public void PlayTestPoint()
    {
        EnsureInitialized();
        lock (_gate)
        {
            if (_disposed || _wiringFaultAlarmActive)
                return;
            SafePlay(_wiringFaultPlayer);
        }
    }

    /// <summary>
    /// Bật/tắt âm thanh cảnh báo chập mạch hoặc đấu sai.
    /// Khi bật, TESTPOINT.wav sẽ lặp liên tục cho đến khi trạng thái lỗi hết.
    /// </summary>
    public void SetWiringFaultAlarm(bool active)
    {
        EnsureInitialized();

        lock (_gate)
        {
            if (_disposed || _wiringFaultAlarmActive == active)
            {
                return;
            }

            _wiringFaultAlarmActive = active;

            try
            {
                if (active)
                {
                    _wiringFaultPlayer?.PlayLooping();
                }
                else
                {
                    _wiringFaultPlayer?.Stop();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Không thể đổi trạng thái âm cảnh báo: {ex}");
            }
        }
    }

    public void StopAll()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _wiringFaultAlarmActive = false;

            SafeStop(_clickPlayer);
            SafeStop(_testOkPlayer);
            SafeStop(_startupPlayer);
            SafeStop(_wiringFaultPlayer);
        }
    }

    private void OnAnyButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.IsEnabled)
        {
            PlayClick();
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }

    private static SoundPlayer? CreatePlayer(
        string fileName,
        out MemoryStream? retainedStream)
    {
        retainedStream = null;

        try
        {
            Assembly assembly = typeof(AppSoundService).Assembly;

            string? resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name =>
                    name.EndsWith(
                        ".Assets.Sounds." + fileName,
                        StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(
                        "." + fileName,
                        StringComparison.OrdinalIgnoreCase));

            if (resourceName is null)
            {
                Debug.WriteLine(
                    $"Không tìm thấy EmbeddedResource âm thanh: {fileName}");
                return null;
            }

            using Stream? resourceStream =
                assembly.GetManifestResourceStream(resourceName);

            if (resourceStream is null)
            {
                Debug.WriteLine(
                    $"Không mở được EmbeddedResource âm thanh: {resourceName}");
                return null;
            }

            using var buffer = new MemoryStream();
            resourceStream.CopyTo(buffer);

            retainedStream = new MemoryStream(
                buffer.ToArray(),
                writable: false);

            var player = new SoundPlayer(retainedStream);
            player.Load();
            return player;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Không thể nạp âm thanh {fileName}: {ex}");
            retainedStream?.Dispose();
            retainedStream = null;
            return null;
        }
    }

    private static void SafePlay(SoundPlayer? player)
    {
        try
        {
            player?.Play();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Không thể phát âm thanh: {ex}");
        }
    }

    private static void SafeStop(SoundPlayer? player)
    {
        try
        {
            player?.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Không thể dừng âm thanh: {ex}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AppSoundService));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            StopAll();

            _clickPlayer?.Dispose();
            _testOkPlayer?.Dispose();
            _startupPlayer?.Dispose();
            _wiringFaultPlayer?.Dispose();

            _clickStream?.Dispose();
            _testOkStream?.Dispose();
            _startupStream?.Dispose();
            _wiringFaultStream?.Dispose();

            _disposed = true;
        }
    }
}